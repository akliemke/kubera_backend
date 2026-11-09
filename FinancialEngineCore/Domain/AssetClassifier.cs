using System.Text.RegularExpressions;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Domain;

/// <summary>
/// Classifica a ordem em EQUITY ou BMF pela nomenclatura da B3 (a base de ordens não traz AssetClass).
/// Raiz de 4 caracteres = código de emissor, ex: PETR, B3SA (segmento Bovespa: à vista, fracionário, ETF, FII, BDR, opções de ações).
/// Raiz de 3 caracteres = código de mercadoria (segmento BM&amp;F: futuros, rolagens, opções sobre futuros).
/// </summary>
public static partial class AssetClassifier
{
    /// <summary>
    /// Classifica pela raiz (Asset). Sem Asset, deduz pelo Symbol: o Asset é preferido porque o Symbol
    /// sozinho é ambíguo (ex.: BDR "XXXU34" casa com o padrão de futuro).
    /// </summary>
    public static string Classify(string? asset, string? symbol)
    {
        var root = asset?.Trim().ToUpperInvariant();

        if (string.IsNullOrEmpty(root))
            return ClassifyBySymbol(symbol);

        if (root.Length == 4 && root.All(char.IsAsciiLetterOrDigit))
            return AssetClasses.Equity;

        if (root.Length == 3 && root.All(char.IsAsciiLetterOrDigit))
            return AssetClasses.Bmf;

        return AssetClasses.Unknown;
    }

    private static string ClassifyBySymbol(string? symbol)
    {
        var normalized = symbol?.Trim().ToUpperInvariant();

        if (string.IsNullOrEmpty(normalized))
            return AssetClasses.Unknown;

        if (FuturePattern().IsMatch(normalized))
            return AssetClasses.Bmf;

        if (IssuerPattern().IsMatch(normalized))
            return AssetClasses.Equity;

        return AssetClasses.Unknown;
    }

    /// <summary>Futuro B3: RAIZ(3) + mês (F..Z) + ano(2). Ex: WINZ25, DI1F27, BGIV25</summary>
    [GeneratedRegex("^[A-Z0-9]{3}[FGHJKMNQUVXZ][0-9]{2}")]
    private static partial Regex FuturePattern();

    /// <summary>Ativo do segmento Bovespa: código de emissor de 4 caracteres. Ex: PETR4, B3SA3, PETRK300</summary>
    [GeneratedRegex("^[A-Z0-9]{4}")]
    private static partial Regex IssuerPattern();
}
