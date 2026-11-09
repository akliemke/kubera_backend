using FinancialEngineCore.Model;

namespace FinancialEngineCore.Domain.Strategies;

/// <summary>
/// Calculation strategy for EQUITY assets (stocks, options, REITs).
/// Uses progressive fee brackets with per-bracket CAPs.
/// </summary>
public class EquityCalculationStrategy : ICalculationStrategy
{
    public string AssetClass => AssetClasses.Equity;

    public BillingCalculationResult Calculate(
        int brokerId,
        BillingKey billingKey,
        DateOnly tradeDate,
        IReadOnlyList<Order> orders,
        BrokerBillingContext context)
    {
        if (orders.Count == 0)
        {
            return new BillingCalculationResult
            {
                Trader = billingKey.Key,
                KeyType = billingKey.Type,
                Broker = brokerId,
                TradeDate = tradeDate,
                AssetClass = AssetClass,
                TotalNotional = 0,
                TotalQty = 0,
                TotalFee = 0,
                AccumulatedBefore = 0,
                AccumulatedAfter = 0,
                ConfigId = null,
                OrderFees = new List<OrderFeeResult>()
            };
        }

        return Calculate(
            orders,
            context.GetEquityBrackets(billingKey),
            context.GetEquityAccumulated(billingKey),
            context.GetFeePerBracket(billingKey),
            context.PriceFactors,
            billingKey: billingKey,
            brokerId: brokerId);
    }

    /// <summary>
    /// Calcula o fee para um grupo de ordens EQUITY de um (Trader, Broker, Dia).
    /// </summary>
    /// <param name="orders">Ordens do dia</param>
    /// <param name="brackets">Faixas de cobrança aplicáveis (ordenadas por NotionalFrom)</param>
    /// <param name="accumulatedBefore">Volume acumulado no mês até o dia anterior</param>
    /// <param name="feeChargedPerBracket">Fee já cobrado no mês por faixa, indexado pelo NotionalFrom da faixa (para aplicar CAPs)</param>
    /// <param name="priceFactors">Fatores de modificação de preço por símbolo (symbol → factor)</param>
    public static BillingCalculationResult Calculate(
        IReadOnlyList<Order> orders,
        IReadOnlyList<EquityFeeBracket> brackets,
        decimal accumulatedBefore,
        IReadOnlyDictionary<decimal, decimal> feeChargedPerBracket,
        IReadOnlyDictionary<string, decimal> priceFactors,
        BillingKey? billingKey = null,
        int? brokerId = null)
    {
        var key = billingKey ?? BillingKey.Trader(orders[0].EnteringTrader);
        var broker = brokerId ?? int.Parse(orders[0].EnteringFirm);
        var tradeDate = orders[0].TradeDate;

        // Calcular notional total do dia (com ajuste de fator de preço)
        var totalNotional = orders.Sum(o => GetAdjustedNotional(o, priceFactors));
        var accumulatedAfter = accumulatedBefore + totalNotional;

        // Calcular fee total do dia usando faixas progressivas (com breakdown por faixa)
        var (totalFee, bracketFees) = CalculateProgressiveFeeWithBreakdown(
            accumulatedBefore,
            totalNotional,
            brackets,
            feeChargedPerBracket);

        // Distribuir fee proporcionalmente entre as ordens
        var orderFees = DistributeFeeProportionally(orders, totalFee, priceFactors);

        return new BillingCalculationResult
        {
            Trader = key.Key,
            KeyType = key.Type,
            Broker = broker,
            TradeDate = tradeDate,
            AssetClass = AssetClasses.Equity,
            TotalNotional = totalNotional,
            TotalQty = 0,
            TotalFee = totalFee,
            AccumulatedBefore = accumulatedBefore,
            AccumulatedAfter = accumulatedAfter,
            ConfigId = brackets.FirstOrDefault()?.Id,
            OrderFees = orderFees,
            BracketFees = bracketFees
        };
    }

    /// <summary>
    /// Calcula o fee progressivo atravessando as faixas, retornando também o breakdown por faixa.
    /// Volume que cai fora de qualquer faixa (lacunas ou abaixo da primeira faixa) não é cobrado.
    /// </summary>
    private static (decimal TotalFee, List<BracketFeeResult> BracketFees) CalculateProgressiveFeeWithBreakdown(
        decimal accumulatedBefore,
        decimal dayNotional,
        IReadOnlyList<EquityFeeBracket> brackets,
        IReadOnlyDictionary<decimal, decimal> feeChargedPerBracket)
    {
        var bracketFees = new List<BracketFeeResult>();

        if (brackets.Count == 0 || dayNotional <= 0)
            return (0, bracketFees);

        decimal totalFee = 0;
        decimal currentPosition = accumulatedBefore;
        decimal remainingNotional = dayNotional;

        foreach (var bracket in brackets.OrderBy(b => b.NotionalFrom))
        {
            if (remainingNotional <= 0)
                break;

            // Verificar se já passamos desta faixa
            var bracketEnd = bracket.NotionalTo ?? decimal.MaxValue;
            if (currentPosition >= bracketEnd)
                continue;

            // Volume entre a posição atual e o início da faixa não pertence a nenhuma faixa: não cobrado
            if (currentPosition < bracket.NotionalFrom)
            {
                var gap = Math.Min(remainingNotional, bracket.NotionalFrom - currentPosition);
                currentPosition += gap;
                remainingNotional -= gap;

                if (remainingNotional <= 0)
                    break;
            }

            // Calcular quanto do notional cai nesta faixa
            var notionalInBracket = Math.Min(remainingNotional, bracketEnd - currentPosition);

            if (notionalInBracket <= 0)
                continue;

            // Calcular fee para esta porção
            var bpsRate = bracket.Bps / 10000m; // Converter BPS para taxa decimal
            var calculatedFee = notionalInBracket * bpsRate;

            // Aplicar CAP da faixa (NULL = sem teto)
            var feeAlreadyCharged = feeChargedPerBracket.GetValueOrDefault(bracket.NotionalFrom, 0);
            var feeToCharge = bracket.Cap is { } cap
                ? Math.Min(calculatedFee, Math.Max(0, cap - feeAlreadyCharged))
                : calculatedFee;

            // Registrar breakdown da faixa
            bracketFees.Add(new BracketFeeResult
            {
                BracketId = bracket.Id,
                NotionalInBracket = notionalInBracket,
                CalculatedFee = calculatedFee,
                ChargedFee = feeToCharge,
                AccumulatedFeeBefore = feeAlreadyCharged,
                BracketCap = bracket.Cap
            });

            totalFee += feeToCharge;
            currentPosition += notionalInBracket;
            remainingNotional -= notionalInBracket;
        }

        return (totalFee, bracketFees);
    }

    /// <summary>
    /// Distribui o fee total proporcionalmente ao notional das ordens (Σ ordens = fee do dia em centavos).
    /// </summary>
    private static List<OrderFeeResult> DistributeFeeProportionally(
        IReadOnlyList<Order> orders,
        decimal totalFee,
        IReadOnlyDictionary<string, decimal> priceFactors)
    {
        var notionals = orders.Select(o => GetAdjustedNotional(o, priceFactors)).ToList();
        var fees = FeeDistribution.Distribute(notionals, totalFee);

        return orders.Select((o, i) => new OrderFeeResult
        {
            OrderId = o.Id,
            Asset = o.Asset,
            Notional = notionals[i],
            Qty = 0,
            FeeAmount = fees[i]
        }).ToList();
    }

    /// <summary>
    /// Retorna o notional ajustado pelo fator de preço, se houver.
    /// Se o símbolo tiver fator cadastrado: qty × (price / factor).
    /// Caso contrário: qty × price (notional padrão).
    /// </summary>
    private static decimal GetAdjustedNotional(Order order, IReadOnlyDictionary<string, decimal> priceFactors)
    {
        if (priceFactors.TryGetValue(order.Symbol, out var factor) && factor != 0)
            return order.CumQty * (order.AvgPx / factor);

        return order.Notional;
    }
}
