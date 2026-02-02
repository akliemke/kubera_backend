using FinancialEngineCore.Model;

namespace FinancialEngineCore.Domain.Strategies;

/// <summary>
/// Strategy interface for asset-class-specific billing calculations.
/// Each asset class (EQUITY, BMF, etc.) implements its own strategy.
/// </summary>
public interface ICalculationStrategy
{
    /// <summary>
    /// The asset class this strategy handles (e.g., "EQUITY", "BMF").
    /// </summary>
    string AssetClass { get; }

    /// <summary>
    /// Calculates the billing for a set of orders.
    /// </summary>
    Task<BillingCalculationResult> CalculateAsync(
        int brokerId,
        string trader,
        DateTime tradeDate,
        IReadOnlyList<Order> orders,
        CancellationToken ct);
}
