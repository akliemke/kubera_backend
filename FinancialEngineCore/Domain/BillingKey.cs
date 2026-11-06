using FinancialEngineCore.Model;

namespace FinancialEngineCore.Domain;

/// <summary>
/// Chave de cobrança: (Type, Key). Type = TRADER (Key = EnteringTrader) ou ENTITY (Key = Entity).
/// Igualdade case-insensitive na Key.
/// </summary>
public readonly record struct BillingKey(string Type, string Key)
{
    public static BillingKey Trader(string trader) => new(BillingKeyTypes.Trader, trader);
    public static BillingKey Entity(string entity) => new(BillingKeyTypes.Entity, entity);

    public bool Equals(BillingKey other)
        => string.Equals(Type, other.Type, StringComparison.Ordinal)
           && string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode()
        => HashCode.Combine(Type, StringComparer.OrdinalIgnoreCase.GetHashCode(Key));

    /// <summary>Regra especial (Trader, KeyType) pertence a esta chave?</summary>
    public bool Matches(string? ruleKeyType, string? ruleTrader)
        => ruleTrader != null
           && string.Equals(ruleKeyType, Type, StringComparison.Ordinal)
           && ruleTrader.Equals(Key, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"{Key} ({Type})";
}
