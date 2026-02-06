using FinancialEngineCore.Model;

namespace FinancialEngineCore.Domain;

/// <summary>
/// Calculador de billing para BMF (futuros).
/// Cobrança por número de contratos com CAP mensal único.
/// </summary>
public static class BmfBillingCalculator
{
    private static readonly HashSet<string> MiniSymbols = new(StringComparer.OrdinalIgnoreCase) { "WIN", "WDO" };
    private static readonly HashSet<string> CheioSymbols = new(StringComparer.OrdinalIgnoreCase) { "IND", "DOL" };

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
        DateTime tradeDate)
    {
        var trader = orders[0].EnteringTrader;
        var broker = int.Parse(orders[0].EnteringFirm);

        // Calcular quantidade total e fee por tipo de contrato
        decimal totalQty = 0;
        decimal calculatedFee = 0;

        foreach (var order in orders)
        {
            var qty = order.CumQty;
            totalQty += qty;

            var contractType = ClassifyContract(order.ExchangeSymbol);
            var feePerContract = GetFeePerContract(contractType, order.ExchangeSymbol, config, tradeDate);
            calculatedFee += qty * feePerContract;
        }

        // Aplicar CAP mensal
        var capRemaining = Math.Max(0, config.CapMensal - feeChargedInMonth);
        var totalFee = Math.Min(calculatedFee, capRemaining);

        // Distribuir fee proporcionalmente entre as ordens
        var orderFees = DistributeFeeProportionally(orders, totalQty, totalFee);

        return new BillingCalculationResult
        {
            Trader = trader,
            Broker = broker,
            TradeDate = tradeDate,
            AssetClass = "BMF",
            TotalNotional = 0,
            TotalQty = totalQty,
            TotalFee = totalFee,
            AccumulatedBefore = feeChargedInMonth,
            AccumulatedAfter = feeChargedInMonth + totalFee,
            ConfigId = config.Id,
            OrderFees = orderFees
        };
    }

    /// <summary>
    /// Classifica o tipo de contrato baseado no ExchangeSymbol.
    /// </summary>
    private static ContractType ClassifyContract(string? exchangeSymbol)
    {
        if (string.IsNullOrEmpty(exchangeSymbol))
            return ContractType.Unknown;

        // Extrair prefixo do símbolo (ex: WIN, WDO, IND, DOL, DI1)
        var prefix = ExtractSymbolPrefix(exchangeSymbol);

        if (MiniSymbols.Contains(prefix))
            return ContractType.Mini;

        if (CheioSymbols.Contains(prefix))
            return ContractType.Cheio;

        if (prefix.Equals("DI1", StringComparison.OrdinalIgnoreCase))
            return ContractType.DI1;

        return ContractType.Unknown;
    }

    /// <summary>
    /// Extrai o prefixo do símbolo.
    /// Ex: "WINZ25" -> "WIN", "DI1F25" -> "DI1", "WDOZ24" -> "WDO"
    /// Formato BMF: PREFIXO(3) + MÊS(1) + ANO(2)
    /// </summary>
    private static string ExtractSymbolPrefix(string symbol)
    {
        if (symbol.Length < 3)
            return symbol.ToUpperInvariant();

        // Símbolos BMF têm 3 caracteres de prefixo: WIN, WDO, IND, DOL, DI1
        return symbol.Substring(0, 3).ToUpperInvariant();
    }

    /// <summary>
    /// Obtém o valor por contrato baseado no tipo.
    /// </summary>
    private static decimal GetFeePerContract(
        ContractType type,
        string? exchangeSymbol,
        BmfFeeConfig config,
        DateTime tradeDate)
    {
        return type switch
        {
            ContractType.Mini => config.ValorMini,
            ContractType.Cheio => config.ValorCheio,
            ContractType.DI1 => GetDI1FeeByMaturity(exchangeSymbol, config, tradeDate),
            _ => 0
        };
    }

    /// <summary>
    /// Calcula o fee do DI1 baseado no vencimento.
    /// </summary>
    private static decimal GetDI1FeeByMaturity(
        string? exchangeSymbol,
        BmfFeeConfig config,
        DateTime tradeDate)
    {
        if (string.IsNullOrEmpty(exchangeSymbol))
            return config.DI1Venc1a4;

        var maturityNumber = CalculateMaturityNumber(exchangeSymbol, tradeDate);

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
    private static int CalculateMaturityNumber(string exchangeSymbol, DateTime tradeDate)
    {
        // Formato: DI1 + letra do mês + ano (ex: DI1F25)
        if (exchangeSymbol.Length < 5)
            return 1;

        var monthCode = exchangeSymbol[3];
        var yearStr = exchangeSymbol.Substring(4);

        if (!int.TryParse(yearStr, out var year))
            return 1;

        // Ajustar ano (25 -> 2025)
        if (year < 100)
            year += 2000;

        var maturityMonth = GetMonthFromCode(monthCode);
        if (maturityMonth == 0)
            return 1;

        var maturityDate = new DateTime(year, maturityMonth, 1);
        var tradeDateMonth = new DateTime(tradeDate.Year, tradeDate.Month, 1);

        // Calcular diferença em meses
        var monthsDiff = ((maturityDate.Year - tradeDateMonth.Year) * 12) +
                         (maturityDate.Month - tradeDateMonth.Month);

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
    /// Distribui o fee total proporcionalmente entre as ordens.
    /// </summary>
    private static List<OrderFeeResult> DistributeFeeProportionally(
        IReadOnlyList<Order> orders,
        decimal totalQty,
        decimal totalFee)
    {
        if (totalQty == 0)
        {
            return orders.Select(o => new OrderFeeResult
            {
                OrderId = o.Id,
                Notional = 0,
                Qty = o.CumQty,
                FeeAmount = 0
            }).ToList();
        }

        return orders.Select(o => new OrderFeeResult
        {
            OrderId = o.Id,
            Notional = 0,
            Qty = o.CumQty,
            FeeAmount = Math.Round((o.CumQty / totalQty) * totalFee, 2)
        }).ToList();
    }

    private enum ContractType
    {
        Unknown,
        Mini,
        Cheio,
        DI1
    }
}
