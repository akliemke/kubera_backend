namespace FinancialEngineCore.Domain.Strategies;

/// <summary>
/// Registry that holds all calculation strategies and provides lookup by asset class.
/// </summary>
public class CalculationStrategyRegistry
{
    private readonly Dictionary<string, ICalculationStrategy> _strategies;

    public CalculationStrategyRegistry(IEnumerable<ICalculationStrategy> strategies)
    {
        _strategies = strategies.ToDictionary(
            s => s.AssetClass,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the calculation strategy for the specified asset class.
    /// Returns null if no strategy is registered for the asset class.
    /// </summary>
    public ICalculationStrategy? GetStrategy(string assetClass)
    {
        return _strategies.GetValueOrDefault(assetClass);
    }

    /// <summary>
    /// Gets all registered asset classes.
    /// </summary>
    public IEnumerable<string> GetRegisteredAssetClasses()
    {
        return _strategies.Keys;
    }
}
