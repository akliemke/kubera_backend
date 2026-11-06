namespace FinancialEngineCore.Model;

/// <summary>
/// Tipo da chave de cobrança (coluna KeyType). Separa trader e entity com o mesmo nome.
/// Strings (não enum) porque são gravadas direto no banco.
/// </summary>
public static class BillingKeyTypes
{
    public const string Trader = "TRADER";
    public const string Entity = "ENTITY";

    public static bool IsValid(string? keyType) => keyType is Trader or Entity;

    /// <summary>
    /// Normaliza a chave de uma regra (faixa EQUITY / config BMF): Trader vazio = regra padrão (KeyType NULL);
    /// Trader preenchido exige KeyType TRADER ou ENTITY.
    /// </summary>
    public static (string? Trader, string? KeyType, string? Error) NormalizeRuleKey(string? trader, string? keyType)
    {
        if (string.IsNullOrWhiteSpace(trader))
            return (null, null, null);

        var normalizedType = keyType?.Trim().ToUpperInvariant();
        return IsValid(normalizedType)
            ? (trader, normalizedType, null)
            : (trader, null, $"KeyType deve ser {Trader} ou {Entity} quando Trader é informado");
    }

    public static string Label(string? keyType) => keyType == Entity ? "entidade" : "trader";
}
