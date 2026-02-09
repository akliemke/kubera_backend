using FinancialEngineCore.Model;

namespace FinancialEngineCore.Domain.Strategies;

/// <summary>
/// Calculation strategy for EQUITY assets (stocks, options, REITs).
/// Uses progressive fee brackets with per-bracket CAPs.
/// All data is resolved from the pre-loaded BrokerBillingContext.
/// </summary>
public class EquityCalculationStrategy : ICalculationStrategy
{
    public string AssetClass => "EQUITY";

    public BillingCalculationResult Calculate(
        int brokerId,
        string trader,
        DateTime tradeDate,
        IReadOnlyList<Order> orders,
        BrokerBillingContext context)
    {
        if (orders.Count == 0)
        {
            return new BillingCalculationResult
            {
                Trader = trader,
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

        // Resolve brackets: special (Trader == trader) or default (Trader == null)
        var specialBrackets = context.AllEquityBrackets
            .Where(b => b.Trader != null && b.Trader.Equals(trader, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var brackets = specialBrackets.Count > 0
            ? specialBrackets
            : context.AllEquityBrackets.Where(b => b.Trader == null).ToList();

        var accumulatedBefore = context.EquityAccumulatedByTrader
            .GetValueOrDefault(trader, 0m);

        var feePerBracket = context.FeePerBracketByTrader
            .GetValueOrDefault(trader, new Dictionary<long, decimal>());

        return EquityBillingCalculator.Calculate(
            orders,
            brackets,
            accumulatedBefore,
            feePerBracket,
            context.PriceFactors);
    }
}
