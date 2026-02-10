using FinancialEngineCore.Model;

namespace FinancialEngineCore.Domain.Strategies;

/// <summary>
/// Strategy interface for asset-class-specific billing calculations.
/// Each asset class (EQUITY, BMF, etc.) implements its own strategy.
/// Calculations are synchronous — all required data is pre-loaded in BrokerBillingContext.
/// </summary>
public interface ICalculationStrategy
{
    /// <summary>
    /// The asset class this strategy handles (e.g., "EQUITY", "BMF").
    /// </summary>
    string AssetClass { get; }

    /// <summary>
    /// Calculates the billing for a set of orders using pre-loaded context.
    /// </summary>
    BillingCalculationResult Calculate(
        int brokerId,
        string trader,
        DateOnly tradeDate,
        IReadOnlyList<Order> orders,
        BrokerBillingContext context);
}
