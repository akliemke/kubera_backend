using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Domain;

/// <summary>
/// Pre-loaded context with all data needed to calculate billing for a broker on a given date.
/// Encapsulates lookup logic (special vs default, accumulated values) so consumers
/// don't need to manipulate raw dictionaries.
/// </summary>
public class BrokerBillingContext
{
    private readonly IReadOnlyList<EquityFeeBracket> _allEquityBrackets;
    private readonly IReadOnlyList<BmfFeeConfig> _allBmfConfigs;
    private readonly Dictionary<string, decimal> _equityAccumulated;
    private readonly Dictionary<string, decimal> _bmfFeeCharged;
    private readonly Dictionary<string, Dictionary<long, decimal>> _feePerBracket;

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

        _equityAccumulated = monthSummaries
            .Where(s => s.AssetClass.Equals("EQUITY", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(s => s.Trader, s => s.TotalNotional);

        _bmfFeeCharged = monthSummaries
            .Where(s => s.AssetClass.Equals("BMF", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(s => s.Trader, s => s.TotalFee);

        _feePerBracket = bracketFees
            .GroupBy(f => f.Trader)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(f => f.BracketId, f => f.TotalFee));
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

    public Dictionary<long, decimal> GetFeePerBracket(string trader)
        => _feePerBracket.GetValueOrDefault(trader, new Dictionary<long, decimal>());
}
