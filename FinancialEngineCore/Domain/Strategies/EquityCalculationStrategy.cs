using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Domain.Strategies;

/// <summary>
/// Calculation strategy for EQUITY assets (stocks, options, REITs).
/// Uses progressive fee brackets with per-bracket CAPs.
/// </summary>
public class EquityCalculationStrategy : ICalculationStrategy
{
    private readonly EquityFeeBracketRepository _bracketRepository;
    private readonly DailyBillingRepository _dailyBillingRepository;
    private readonly BracketFeeTrackingRepository _bracketFeeTrackingRepository;

    public string AssetClass => "EQUITY";

    public EquityCalculationStrategy(
        EquityFeeBracketRepository bracketRepository,
        DailyBillingRepository dailyBillingRepository,
        BracketFeeTrackingRepository bracketFeeTrackingRepository)
    {
        _bracketRepository = bracketRepository;
        _dailyBillingRepository = dailyBillingRepository;
        _bracketFeeTrackingRepository = bracketFeeTrackingRepository;
    }

    public async Task<BillingCalculationResult> CalculateAsync(
        int brokerId,
        string trader,
        DateTime tradeDate,
        IReadOnlyList<Order> orders,
        CancellationToken ct)
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

        // Get applicable brackets (special or default)
        var brackets = await _bracketRepository.GetApplicableBracketsAsync(
            brokerId, trader, tradeDate, ct);
        var bracketList = brackets.ToList();

        // Get accumulated notional for the month
        var accumulatedBefore = await _dailyBillingRepository.GetMonthAccumulatedAsync(
            brokerId, trader, AssetClass, tradeDate, ct);

        // Get fee already charged per bracket in the month (for CAP enforcement)
        var feeChargedPerBracket = await _bracketFeeTrackingRepository.GetMonthFeePerBracketAsync(
            brokerId, trader, tradeDate, ct);

        return EquityBillingCalculator.Calculate(
            orders,
            bracketList,
            accumulatedBefore,
            feeChargedPerBracket);
    }
}
