using FinancialEngineCore.Model;

namespace FinancialEngineCore.Domain;

/// <summary>
/// Pre-loaded context with all data needed to calculate billing for a broker on a given date.
/// Eliminates N+1 queries by loading everything upfront.
/// </summary>
public class BrokerBillingContext
{
    public required IReadOnlyList<EquityFeeBracket> AllEquityBrackets { get; init; }
    public required IReadOnlyList<BmfFeeConfig> AllBmfConfigs { get; init; }
    public required IReadOnlyDictionary<string, decimal> EquityAccumulatedByTrader { get; init; }
    public required IReadOnlyDictionary<string, decimal> BmfFeeChargedByTrader { get; init; }
    public required IReadOnlyDictionary<string, Dictionary<long, decimal>> FeePerBracketByTrader { get; init; }
    public required IReadOnlyDictionary<string, decimal> PriceFactors { get; init; }
}
