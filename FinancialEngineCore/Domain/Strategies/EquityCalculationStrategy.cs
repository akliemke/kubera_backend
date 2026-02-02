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

    public string AssetClass => "EQUITY";

    public EquityCalculationStrategy(
        EquityFeeBracketRepository bracketRepository,
        DailyBillingRepository dailyBillingRepository)
    {
        _bracketRepository = bracketRepository;
        _dailyBillingRepository = dailyBillingRepository;
    }

    public async Task<BillingCalculationResult> CalculateAsync(
        int brokerId,
        string trader,
        DateTime tradeDate,
        IReadOnlyList<Order> orders,
        CancellationToken ct)
    {
        // Get applicable brackets (special or default)
        var brackets = await _bracketRepository.GetApplicableBracketsAsync(
            brokerId, trader, tradeDate, ct);
        var bracketList = brackets.ToList();

        // Get accumulated notional for the month
        var accumulatedBefore = await _dailyBillingRepository.GetMonthAccumulatedAsync(
            brokerId, trader, AssetClass, tradeDate, ct);

        // TODO: Implement per-bracket fee tracking for CAPs
        var feeChargedPerBracket = new Dictionary<long, decimal>();

        return EquityBillingCalculator.Calculate(
            orders,
            bracketList,
            accumulatedBefore,
            feeChargedPerBracket);
    }
}
