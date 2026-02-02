using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;
using Microsoft.Extensions.Logging;

namespace FinancialEngineCore.Domain.Strategies;

/// <summary>
/// Calculation strategy for BMF assets (futures contracts).
/// Uses per-contract fees with a single monthly CAP.
/// </summary>
public class BmfCalculationStrategy : ICalculationStrategy
{
    private readonly BmfFeeConfigRepository _configRepository;
    private readonly DailyBillingRepository _dailyBillingRepository;
    private readonly ILogger<BmfCalculationStrategy> _logger;

    public string AssetClass => "BMF";

    public BmfCalculationStrategy(
        BmfFeeConfigRepository configRepository,
        DailyBillingRepository dailyBillingRepository,
        ILogger<BmfCalculationStrategy> logger)
    {
        _configRepository = configRepository;
        _dailyBillingRepository = dailyBillingRepository;
        _logger = logger;
    }

    public async Task<BillingCalculationResult> CalculateAsync(
        int brokerId,
        string trader,
        DateTime tradeDate,
        IReadOnlyList<Order> orders,
        CancellationToken ct)
    {
        // Get applicable config (special or default)
        var config = await _configRepository.GetApplicableConfigAsync(
            brokerId, trader, tradeDate, ct);

        if (config == null)
        {
            _logger.LogWarning(
                "No BMF config found for broker {BrokerId}, trader {Trader}",
                brokerId, trader);

            return CreateEmptyResult(brokerId, trader, tradeDate, orders);
        }

        // Get fee already charged in the month (for CAP)
        var feeChargedInMonth = await _dailyBillingRepository.GetMonthFeeChargedAsync(
            brokerId, trader, AssetClass, tradeDate, ct);

        return BmfBillingCalculator.Calculate(orders, config, feeChargedInMonth, tradeDate);
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
