using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Domain;

/// <summary>
/// Pre-loaded context with all data needed to calculate billing for a broker on a given date.
/// Encapsulates lookup logic (special vs default, accumulated values) so consumers
/// don't need to manipulate raw dictionaries.
/// Billing keys are matched case-insensitively everywhere.
/// </summary>
public class BrokerBillingContext
{
    private readonly IReadOnlyList<EquityFeeBracket> _allEquityBrackets;
    private readonly IReadOnlyList<BmfFeeConfig> _allBmfConfigs;
    private readonly Dictionary<string, decimal> _equityAccumulated;
    private readonly Dictionary<string, decimal> _bmfFeeCharged;
    private readonly Dictionary<string, Dictionary<decimal, decimal>> _feePerBracket;

    public IReadOnlyDictionary<string, decimal> PriceFactors { get; }

    public BrokerBillingContext(
        IReadOnlyList<EquityFeeBracket> allEquityBrackets,
        IReadOnlyList<BmfFeeConfig> allBmfConfigs,
        IReadOnlyList<TraderMonthSummary> monthSummaries,
        IReadOnlyList<TraderBracketFee> bracketFees,
        IReadOnlyDictionary<string, decimal> priceFactors)
    {
        _allEquityBrackets = allEquityBrackets;
        _allBmfConfigs = allBmfConfigs;
        PriceFactors = priceFactors;

        // GroupBy + Sum: rows that differ only by casing collapse into the same key
        _equityAccumulated = monthSummaries
            .Where(s => s.AssetClass.Equals("EQUITY", StringComparison.OrdinalIgnoreCase))
            .GroupBy(s => s.Trader, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.TotalNotional), StringComparer.OrdinalIgnoreCase);

        _bmfFeeCharged = monthSummaries
            .Where(s => s.AssetClass.Equals("BMF", StringComparison.OrdinalIgnoreCase))
            .GroupBy(s => s.Trader, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.TotalFee), StringComparer.OrdinalIgnoreCase);

        _feePerBracket = bracketFees
            .GroupBy(f => f.Trader, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(f => f.NotionalFrom).ToDictionary(b => b.Key, b => b.Sum(f => f.TotalFee)),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolve equity brackets: special (Trader == trader) or default (Trader IS NULL).
    /// </summary>
    public IReadOnlyList<EquityFeeBracket> GetEquityBrackets(string trader)
    {
        var special = _allEquityBrackets
            .Where(b => b.Trader != null && b.Trader.Equals(trader, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return special.Count > 0
            ? special
            : _allEquityBrackets.Where(b => b.Trader == null).ToList();
    }

    /// <summary>
    /// Resolve BMF config: special (Trader == trader) or default (Trader IS NULL).
    /// </summary>
    public BmfFeeConfig? GetBmfConfig(string trader)
    {
        return _allBmfConfigs
            .FirstOrDefault(c => c.Trader != null && c.Trader.Equals(trader, StringComparison.OrdinalIgnoreCase))
            ?? _allBmfConfigs.FirstOrDefault(c => c.Trader == null);
    }

    public decimal GetEquityAccumulated(string trader)
        => _equityAccumulated.GetValueOrDefault(trader, 0m);

    public decimal GetBmfFeeCharged(string trader)
        => _bmfFeeCharged.GetValueOrDefault(trader, 0m);

    /// <summary>
    /// Fee already charged this month per bracket, keyed by bracket NotionalFrom
    /// (stable across bracket versions / default→special switches within the month).
    /// </summary>
    public Dictionary<decimal, decimal> GetFeePerBracket(string trader)
        => _feePerBracket.GetValueOrDefault(trader, new Dictionary<decimal, decimal>());
}
