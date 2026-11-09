using FinancialEngineCore.Model;
using Microsoft.Extensions.Logging;

namespace FinancialEngineCore.Domain.Strategies;

/// <summary>
/// Calculation strategy for BMF assets (futures contracts).
/// Uses per-contract fees with a single monthly CAP: DI1 by maturity, mini/cheio by contract,
/// every other BMF asset at a fixed fee per contract (ValorOutros).
/// </summary>
public class BmfCalculationStrategy : ICalculationStrategy
{
    private static readonly HashSet<string> MiniSymbols = new(StringComparer.OrdinalIgnoreCase) { "WIN", "WDO" };
    private static readonly HashSet<string> CheioSymbols = new(StringComparer.OrdinalIgnoreCase) { "IND", "DOL" };

    private readonly ILogger<BmfCalculationStrategy> _logger;

    public string AssetClass => AssetClasses.Bmf;

    public BmfCalculationStrategy(ILogger<BmfCalculationStrategy> logger)
    {
        _logger = logger;
    }

    public BillingCalculationResult Calculate(
        int brokerId,
        BillingKey billingKey,
        DateOnly tradeDate,
        IReadOnlyList<Order> orders,
        BrokerBillingContext context)
    {
        var feeChargedInMonth = context.GetBmfFeeCharged(billingKey);

        if (orders.Count == 0)
            return CreateEmptyResult(brokerId, billingKey, tradeDate, orders, feeChargedInMonth);

        var config = context.GetBmfConfig(billingKey);

        if (config == null)
        {
            _logger.LogWarning(
                "No BMF config found for broker {BrokerId}, billing key {BillingKey}",
                brokerId, billingKey);

            return CreateEmptyResult(brokerId, billingKey, tradeDate, orders, feeChargedInMonth);
        }

        var result = Calculate(
            orders, config, feeChargedInMonth, tradeDate,
            billingKey: billingKey, brokerId: brokerId);

        if (result.UnknownSymbols.Count > 0)
        {
            _logger.LogWarning(
                "BMF orders without contract root charged as zero for broker {BrokerId}, billing key {BillingKey} on {Date}: {Symbols}",
                brokerId, billingKey, tradeDate, string.Join(", ", result.UnknownSymbols));
        }

        return result;
    }

    /// <summary>
    /// Calcula o fee para um grupo de ordens BMF de um (Trader, Broker, Dia).
    /// </summary>
    /// <param name="orders">Ordens do dia</param>
    /// <param name="config">Configuração BMF aplicável</param>
    /// <param name="feeChargedInMonth">Fee já cobrado no mês (para aplicar CAP)</param>
    /// <param name="tradeDate">Data da operação (para calcular vencimento DI1)</param>
    public static BillingCalculationResult Calculate(
        IReadOnlyList<Order> orders,
        BmfFeeConfig config,
        decimal feeChargedInMonth,
        DateOnly tradeDate,
        BillingKey? billingKey = null,
        int? brokerId = null)
    {
        var key = billingKey ?? BillingKey.Trader(orders[0].EnteringTrader);
        var broker = brokerId ?? int.Parse(orders[0].EnteringFirm);

        // Calcular quantidade total e fee por tipo de contrato
        decimal totalQty = 0;
        decimal calculatedFee = 0;
        var unknownSymbols = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var order in orders)
        {
            var qty = order.CumQty;
            totalQty += qty;

            var contractType = ClassifyContract(GetContractRoot(order));
            if (contractType == ContractType.Unknown)
                unknownSymbols.Add(order.Symbol);

            var feePerContract = GetFeePerContract(contractType, order.Symbol, config, tradeDate);
            calculatedFee += qty * feePerContract;
        }

        // Aplicar CAP mensal (NULL = sem teto)
        var totalFee = config.CapMensal is { } cap
            ? Math.Min(calculatedFee, Math.Max(0, cap - feeChargedInMonth))
            : calculatedFee;

        // Distribuir fee proporcionalmente entre as ordens
        var orderFees = DistributeFeeProportionally(orders, totalFee);

        return new BillingCalculationResult
        {
            Trader = key.Key,
            KeyType = key.Type,
            Broker = broker,
            TradeDate = tradeDate,
            AssetClass = AssetClasses.Bmf,
            TotalNotional = 0,
            TotalQty = totalQty,
            TotalFee = totalFee,
            AccumulatedBefore = feeChargedInMonth,
            AccumulatedAfter = feeChargedInMonth + totalFee,
            ConfigId = config.Id,
            OrderFees = orderFees,
            UnknownSymbols = unknownSymbols.ToList()
        };
    }

    /// <summary>
    /// Raiz do contrato: o Asset (WIN, WDO, DI1; perna do sintético) ou, sem Asset, o prefixo do Symbol.
    /// Ex: "WINZ25" -> "WIN", "DI1F25" -> "DI1". Formato BMF: RAIZ(3) + MÊS(1) + ANO(2)
    /// </summary>
    private static string GetContractRoot(Order order)
    {
        if (!string.IsNullOrWhiteSpace(order.Asset))
            return order.Asset.Trim().ToUpperInvariant();

        var symbol = order.Symbol.Trim();
        return (symbol.Length < 3 ? symbol : symbol[..3]).ToUpperInvariant();
    }

    /// <summary>
    /// Classifica o tipo de contrato pela raiz. Raiz que não é mini, cheio nem DI1 = demais contratos.
    /// </summary>
    private static ContractType ClassifyContract(string root)
    {
        if (string.IsNullOrEmpty(root))
            return ContractType.Unknown;

        if (MiniSymbols.Contains(root))
            return ContractType.Mini;

        if (CheioSymbols.Contains(root))
            return ContractType.Cheio;

        if (root.Equals("DI1", StringComparison.OrdinalIgnoreCase))
            return ContractType.DI1;

        return ContractType.Other;
    }

    /// <summary>
    /// Obtém o valor por contrato baseado no tipo.
    /// </summary>
    private static decimal GetFeePerContract(
        ContractType type,
        string symbol,
        BmfFeeConfig config,
        DateOnly tradeDate)
    {
        return type switch
        {
            ContractType.Mini => config.ValorMini,
            ContractType.Cheio => config.ValorCheio,
            ContractType.DI1 => GetDI1FeeByMaturity(symbol, config, tradeDate),
            ContractType.Other => config.ValorOutros,
            _ => 0
        };
    }

    /// <summary>
    /// Calcula o fee do DI1 baseado no vencimento (lido do Symbol, ex: DI1F27).
    /// </summary>
    private static decimal GetDI1FeeByMaturity(
        string symbol,
        BmfFeeConfig config,
        DateOnly tradeDate)
    {
        if (string.IsNullOrEmpty(symbol))
            return config.DI1Venc1a4;

        var maturityNumber = CalculateMaturityNumber(symbol, tradeDate);

        return maturityNumber switch
        {
            <= 4 => config.DI1Venc1a4,
            5 => config.DI1Venc5,
            6 => config.DI1Venc6,
            7 => config.DI1Venc7,
            _ => config.DI1Venc8Plus
        };
    }

    /// <summary>
    /// Calcula o número do vencimento baseado no símbolo e data da operação.
    /// Ex: DI1F25 operado em Nov/24 = 2º vencimento (Jan/25 é 2 meses à frente)
    /// </summary>
    private static int CalculateMaturityNumber(string symbol, DateOnly tradeDate)
    {
        // Formato: DI1 + letra do mês + ano (ex: DI1F25)
        if (symbol.Length < 5)
            return 1;

        var monthCode = symbol[3];
        var yearStr = symbol.Substring(4);

        if (!int.TryParse(yearStr, out var year))
            return 1;

        // Ajustar ano (25 -> 2025)
        if (year < 100)
            year += 2000;

        var maturityMonth = GetMonthFromCode(monthCode);
        if (maturityMonth == 0)
            return 1;

        // Calcular diferença em meses
        var monthsDiff = ((year - tradeDate.Year) * 12) +
                         (maturityMonth - tradeDate.Month);

        return Math.Max(1, monthsDiff);
    }

    /// <summary>
    /// Converte código de mês para número.
    /// F=Jan, G=Fev, H=Mar, J=Abr, K=Mai, M=Jun, N=Jul, Q=Ago, U=Set, V=Out, X=Nov, Z=Dez
    /// </summary>
    private static int GetMonthFromCode(char code)
    {
        return char.ToUpper(code) switch
        {
            'F' => 1,
            'G' => 2,
            'H' => 3,
            'J' => 4,
            'K' => 5,
            'M' => 6,
            'N' => 7,
            'Q' => 8,
            'U' => 9,
            'V' => 10,
            'X' => 11,
            'Z' => 12,
            _ => 0
        };
    }

    /// <summary>
    /// Distribui o fee total proporcionalmente à quantidade das ordens (Σ ordens = fee do dia em centavos).
    /// </summary>
    private static List<OrderFeeResult> DistributeFeeProportionally(
        IReadOnlyList<Order> orders,
        decimal totalFee)
    {
        var fees = FeeDistribution.Distribute(orders.Select(o => o.CumQty).ToList(), totalFee);

        return orders.Select((o, i) => new OrderFeeResult
        {
            OrderId = o.Id,
            Asset = o.Asset,
            Notional = 0,
            Qty = o.CumQty,
            FeeAmount = fees[i]
        }).ToList();
    }

    private BillingCalculationResult CreateEmptyResult(
        int brokerId,
        BillingKey billingKey,
        DateOnly tradeDate,
        IReadOnlyList<Order> orders,
        decimal feeChargedInMonth)
    {
        return new BillingCalculationResult
        {
            Trader = billingKey.Key,
            KeyType = billingKey.Type,
            Broker = brokerId,
            TradeDate = tradeDate,
            AssetClass = AssetClass,
            TotalNotional = 0,
            TotalQty = orders.Sum(o => o.CumQty),
            TotalFee = 0,
            AccumulatedBefore = feeChargedInMonth,
            AccumulatedAfter = feeChargedInMonth,
            ConfigId = null,
            OrderFees = orders.Select(o => new OrderFeeResult
            {
                OrderId = o.Id,
                Asset = o.Asset,
                Notional = 0,
                Qty = o.CumQty,
                FeeAmount = 0
            }).ToList()
        };
    }

    private enum ContractType
    {
        Unknown,
        Mini,
        Cheio,
        DI1,
        Other
    }
}
