using FinancialEngineCore.Model;
using Microsoft.Extensions.Logging;

namespace FinancialEngineCore.Domain.Strategies;

/// <summary>
/// Calculation strategy for BMF assets (futures contracts).
/// Uses per-contract fees with a single monthly CAP.
/// </summary>
public class BmfCalculationStrategy : ICalculationStrategy
{
    private readonly ILogger<BmfCalculationStrategy> _logger;

    public string AssetClass => "BMF";

    public BmfCalculationStrategy(ILogger<BmfCalculationStrategy> logger)
    {
        _logger = logger;
    }

    public BillingCalculationResult Calculate(
        int brokerId,
        string trader,
        DateTime tradeDate,
        IReadOnlyList<Order> orders,
        BrokerBillingContext context)
    {
        if (orders.Count == 0)
            return CreateEmptyResult(brokerId, trader, tradeDate, orders);

        var config = context.GetBmfConfig(trader);

        if (config == null)
        {
            _logger.LogWarning(
                "No BMF config found for broker {BrokerId}, trader {Trader}",
                brokerId, trader);

            return CreateEmptyResult(brokerId, trader, tradeDate, orders);
        }

        return BmfBillingCalculator.Calculate(
            orders, config, context.GetBmfFeeCharged(trader), tradeDate);
    }

    private BillingCalculationResult CreateEmptyResult(
        int brokerId,
        string trader,
        DateTime tradeDate,
        IReadOnlyList<Order> orders)
    {
        return new BillingCalculationResult
        {
            Trader = trader,
            Broker = brokerId,
            TradeDate = tradeDate,
            AssetClass = AssetClass,
            TotalNotional = 0,
            TotalQty = orders.Sum(o => o.CumQty),
            TotalFee = 0,
            AccumulatedBefore = 0,
            AccumulatedAfter = 0,
            ConfigId = null,
            OrderFees = orders.Select(o => new OrderFeeResult
            {
                OrderId = o.Id,
                Notional = 0,
                Qty = o.CumQty,
                FeeAmount = 0
            }).ToList()
        };
    }
}
