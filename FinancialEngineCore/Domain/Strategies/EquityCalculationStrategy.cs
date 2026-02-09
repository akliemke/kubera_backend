using FinancialEngineCore.Model;

namespace FinancialEngineCore.Domain.Strategies;

/// <summary>
/// Calculation strategy for EQUITY assets (stocks, options, REITs).
/// Uses progressive fee brackets with per-bracket CAPs.
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

        return EquityBillingCalculator.Calculate(
            orders,
            context.GetEquityBrackets(trader),
            context.GetEquityAccumulated(trader),
            context.GetFeePerBracket(trader),
            context.PriceFactors);
    }
}
