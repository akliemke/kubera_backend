namespace FinancialEngineCore.Model;

/// <summary>
/// Classe do ativo (coluna AssetClass). Determina qual strategy de cobrança aplicar.
/// Strings (não enum) porque são gravadas direto no banco.
/// </summary>
public static class AssetClasses
{
    public const string Equity = "EQUITY";
    public const string Bmf = "BMF";

    /// <summary>Ativo não classificado: sem strategy, não é cobrado (gera warning).</summary>
    public const string Unknown = "UNKNOWN";
}
