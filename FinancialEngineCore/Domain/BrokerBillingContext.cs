using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Domain;

/// <summary>
/// Pre-loaded context with all data needed to calculate billing for a broker on a given date.
/// Encapsulates lookup logic (special vs default, accumulated values) so consumers
/// don't need to manipulate raw dictionaries.
/// Billing keys are (KeyType, Key) and matched case-insensitively on Key everywhere.
/// </summary>
public class BrokerBillingContext
{
    private readonly IReadOnlyList<EquityFeeBracket> _allEquityBrackets;
    private readonly IReadOnlyList<BmfFeeConfig> _allBmfConfigs;
    private readonly Dictionary<BillingKey, decimal> _equityAccumulated;
    private readonly Dictionary<BillingKey, decimal> _bmfFeeCharged;
    private readonly Dictionary<BillingKey, Dictionary<decimal, decimal>> _feePerBracket;

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
            .GroupBy(s => new BillingKey(s.KeyType, s.Trader))
            .ToDictionary(g => g.Key, g => g.Sum(s => s.TotalNotional));

        _bmfFeeCharged = monthSummaries
            .Where(s => s.AssetClass.Equals("BMF", StringComparison.OrdinalIgnoreCase))
            .GroupBy(s => new BillingKey(s.KeyType, s.Trader))
            .ToDictionary(g => g.Key, g => g.Sum(s => s.TotalFee));

        _feePerBracket = bracketFees
            .GroupBy(f => new BillingKey(f.KeyType, f.Trader))
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(f => f.NotionalFrom).ToDictionary(b => b.Key, b => b.Sum(f => f.TotalFee)));
    }

    /// <summary>
    /// Resolve equity brackets: special (Trader/KeyType == key) or default (Trader IS NULL).
    /// </summary>
    public IReadOnlyList<EquityFeeBracket> GetEquityBrackets(BillingKey key)
    {
        var special = _allEquityBrackets
            .Where(b => key.Matches(b.KeyType, b.Trader))
            .ToList();

        return special.Count > 0
            ? special
            : _allEquityBrackets.Where(b => b.Trader == null).ToList();
    }

    /// <summary>
    /// Resolve BMF config: special (Trader/KeyType == key) or default (Trader IS NULL).
    /// </summary>
    public BmfFeeConfig? GetBmfConfig(BillingKey key)
    {
        return _allBmfConfigs.FirstOrDefault(c => key.Matches(c.KeyType, c.Trader))
            ?? _allBmfConfigs.FirstOrDefault(c => c.Trader == null);
    }

    /// <summary>
    /// Trader has its own special rule (KeyType TRADER) for the asset class.
    /// Such a trader is billed under its own key even if its Entity has an EntityBillingConfig.
    /// </summary>
    public bool HasTraderSpecialRule(string trader, string assetClass)
    {
        var key = BillingKey.Trader(trader);

        if (assetClass.Equals("EQUITY", StringComparison.OrdinalIgnoreCase))
            return _allEquityBrackets.Any(b => key.Matches(b.KeyType, b.Trader));

        if (assetClass.Equals("BMF", StringComparison.OrdinalIgnoreCase))
            return _allBmfConfigs.Any(c => key.Matches(c.KeyType, c.Trader));

        return false;
    }

    public decimal GetEquityAccumulated(BillingKey key)
        => _equityAccumulated.GetValueOrDefault(key, 0m);

    public decimal GetBmfFeeCharged(BillingKey key)
        => _bmfFeeCharged.GetValueOrDefault(key, 0m);

    /// <summary>
    /// Fee already charged this month per bracket, keyed by bracket NotionalFrom
    /// (stable across bracket versions / default→special switches within the month).
    /// </summary>
    public Dictionary<decimal, decimal> GetFeePerBracket(BillingKey key)
        => _feePerBracket.GetValueOrDefault(key, new Dictionary<decimal, decimal>());
}
