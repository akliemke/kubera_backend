using FinancialEngineCore.Model;

namespace FinancialEngineCore.Domain;

/// <summary>
/// Calculador de billing para EQUITY (ações, opções, FIIs).
/// Cobrança progressiva por faixas de volume com BPS + CAP por faixa.
/// </summary>
public static class EquityBillingCalculator
{
    /// <summary>
    /// Calcula o fee para um grupo de ordens EQUITY de um (Trader, Broker, Dia).
    /// </summary>
    /// <param name="orders">Ordens do dia</param>
    /// <param name="brackets">Faixas de cobrança aplicáveis (ordenadas por NotionalFrom)</param>
    /// <param name="accumulatedBefore">Volume acumulado no mês até o dia anterior</param>
    /// <param name="feeChargedPerBracket">Fee já cobrado em cada faixa no mês (para aplicar CAPs)</param>
    public static BillingCalculationResult Calculate(
        IReadOnlyList<Order> orders,
        IReadOnlyList<EquityFeeBracket> brackets,
        decimal accumulatedBefore,
        IReadOnlyDictionary<long, decimal> feeChargedPerBracket)
    {
        var trader = orders[0].EnteringTrader;
        var broker = int.Parse(orders[0].EnteringFirm);
        var tradeDate = orders[0].TradeDate;

        // Calcular notional total do dia
        var totalNotional = orders.Sum(o => o.Notional);
        var accumulatedAfter = accumulatedBefore + totalNotional;

        // Calcular fee total do dia usando faixas progressivas (com breakdown por faixa)
        var (totalFee, bracketFees) = CalculateProgressiveFeeWithBreakdown(
            accumulatedBefore,
            totalNotional,
            brackets,
            feeChargedPerBracket);

        // Distribuir fee proporcionalmente entre as ordens
        var orderFees = DistributeFeeProportionally(orders, totalNotional, totalFee);

        return new BillingCalculationResult
        {
            Trader = trader,
            Broker = broker,
            TradeDate = tradeDate,
            AssetClass = "EQUITY",
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
    /// </summary>
    private static (decimal TotalFee, List<BracketFeeResult> BracketFees) CalculateProgressiveFeeWithBreakdown(
        decimal accumulatedBefore,
        decimal dayNotional,
        IReadOnlyList<EquityFeeBracket> brackets,
        IReadOnlyDictionary<long, decimal> feeChargedPerBracket)
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

            // Calcular quanto do notional cai nesta faixa
            var bracketStart = Math.Max(bracket.NotionalFrom, currentPosition);
            var notionalInBracket = Math.Min(remainingNotional, bracketEnd - bracketStart);

            if (notionalInBracket <= 0)
                continue;

            // Calcular fee para esta porção
            var bpsRate = bracket.Bps / 10000m; // Converter BPS para taxa decimal
            var calculatedFee = notionalInBracket * bpsRate;

            // Aplicar CAP da faixa
            var feeAlreadyCharged = feeChargedPerBracket.GetValueOrDefault(bracket.Id, 0);
            var capRemaining = Math.Max(0, bracket.Cap - feeAlreadyCharged);
            var feeToCharge = Math.Min(calculatedFee, capRemaining);

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
    /// Distribui o fee total proporcionalmente entre as ordens.
    /// </summary>
    private static List<OrderFeeResult> DistributeFeeProportionally(
        IReadOnlyList<Order> orders,
        decimal totalNotional,
        decimal totalFee)
    {
        if (totalNotional == 0)
        {
            return orders.Select(o => new OrderFeeResult
            {
                OrderId = o.Id,
                Notional = o.Notional,
                Qty = 0,
                FeeAmount = 0
            }).ToList();
        }

        return orders.Select(o => new OrderFeeResult
        {
            OrderId = o.Id,
            Notional = o.Notional,
            Qty = 0,
            FeeAmount = Math.Round((o.Notional / totalNotional) * totalFee, 2)
        }).ToList();
    }

}
