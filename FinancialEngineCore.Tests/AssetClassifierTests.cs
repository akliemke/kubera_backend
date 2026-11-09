using FinancialEngineCore.Domain;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Tests;

public class AssetClassifierTests
{
    [Theory]
    [InlineData("PETR", "PETR4")]     // à vista
    [InlineData("PETR", "PETR4F")]    // fracionário
    [InlineData("BOVA", "BOVA11")]    // ETF
    [InlineData("HGLG", "HGLG11")]    // FII
    [InlineData("AAPL", "AAPL34")]    // BDR
    [InlineData("B3SA", "B3SA3")]     // emissor com dígito
    [InlineData("PETR", "PETRK300")]  // opção de ação
    [InlineData("PETR", "PETRX35W2")] // opção semanal
    [InlineData("petr", "petr4")]     // case-insensitive
    public void Classify_IssuerRoot_ReturnsEquity(string asset, string symbol)
    {
        Assert.Equal(AssetClasses.Equity, AssetClassifier.Classify(asset, symbol));
    }

    [Theory]
    [InlineData("WIN", "WINZ25")]
    [InlineData("DI1", "DI1F27")]
    [InlineData("BGI", "BGIV25")]
    [InlineData("WDO", "BTGDOLZ26")] // perna do sintético devolvida pela view
    [InlineData("DOL", "BTGDOLZ26")]
    public void Classify_CommodityRoot_ReturnsBmf(string asset, string symbol)
    {
        Assert.Equal(AssetClasses.Bmf, AssetClassifier.Classify(asset, symbol));
    }

    [Theory]
    [InlineData(null, "WINZ25", AssetClasses.Bmf)]
    [InlineData("", "DI1F27", AssetClasses.Bmf)]
    [InlineData(null, "PETR4", AssetClasses.Equity)]
    [InlineData("  ", "BOVA11", AssetClasses.Equity)]
    [InlineData(null, "", AssetClasses.Unknown)]
    [InlineData(null, "X1", AssetClasses.Unknown)]
    public void Classify_WithoutAsset_UsesSymbol(string? asset, string symbol, string expected)
    {
        Assert.Equal(expected, AssetClassifier.Classify(asset, symbol));
    }

    [Theory]
    [InlineData("?", "BTGDOLZ26")] // perna de sintético sem sufixo no UniqueTradeId
    [InlineData("AB", "AB12")]
    [InlineData("BTGDOL", "BTGDOLZ26")]
    public void Classify_UnrecognizedRoot_ReturnsUnknown(string asset, string symbol)
    {
        Assert.Equal(AssetClasses.Unknown, AssetClassifier.Classify(asset, symbol));
    }
}
