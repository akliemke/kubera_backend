using FinancialEngineCore.Domain;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Tests;

public class BillableOrderBuilderTests
{
    private static readonly Dictionary<string, string> SymbolAssets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PETR4"] = "PETR",
        ["WINZ26"] = "WIN",
        ["BTGDOLZ26"] = "DOL"
    };

    private static Order CreateOrder(string id, string symbol, decimal qty = 100, decimal price = 10, string? tif = null) => new()
    {
        OrderId = id,
        CumQty = qty,
        AvgPx = price,
        Symbol = symbol,
        EnteringTrader = "TRADER1",
        Entity = "MESA1",
        ExecBroker = "1",
        Instance = "OMS1",
        Side = "BUY",
        TimeInForce = tif,
        TradeDate = new DateOnly(2026, 11, 12)
    };

    private static List<Order> Build(IEnumerable<Order> orders, params DayTrade[] trades)
        => BillableOrderBuilder.Build(orders, SymbolAssets, trades);

    [Fact]
    public void Build_NormalOrder_PassesThroughWithAssetFromMap()
    {
        var result = Build([CreateOrder("1", "PETR4", 100, 10, "DAY")]);

        var order = Assert.Single(result);
        Assert.Equal("PETR", order.Asset);
        Assert.Equal(100m, order.CumQty);
        Assert.Equal(10m, order.AvgPx);
    }

    [Fact]
    public void Build_SymbolNotInMap_AssetNull()
    {
        var order = Assert.Single(Build([CreateOrder("1", "VALE3")]));

        Assert.Null(order.Asset);
    }

    [Fact]
    public void Build_SymbolLookup_IsCaseInsensitive()
    {
        var order = Assert.Single(Build([CreateOrder("1", "petr4")]));

        Assert.Equal("PETR", order.Asset);
    }

    [Fact]
    public void Build_Gtc_UsesTradesOfDayWithVwap()
    {
        var result = Build(
            [CreateOrder("1", "PETR4", 1000, 50, "GTC")],
            new DayTrade("1", 100, 50, null),
            new DayTrade("1", 100, 60, null));

        var order = Assert.Single(result);
        Assert.Equal(200m, order.CumQty);
        Assert.Equal(55m, order.AvgPx);
        Assert.Equal("PETR", order.Asset);
    }

    [Fact]
    public void Build_GtdWithoutTradesOfDay_Excluded()
    {
        Assert.Empty(Build([CreateOrder("1", "PETR4", 1000, 50, "gtd")]));
    }

    [Fact]
    public void Build_Synthetic_OneRowPerLegWithFactor()
    {
        var result = Build(
            [CreateOrder("1", "BTGDOLZ26", 1, 5000, "DAY")],
            new DayTrade("1", 0.2m, 5000, "1234-WDO"),
            new DayTrade("1", 0.2m, 5010, "1235-wdo"),
            new DayTrade("1", 1, 5000, "321-DOL"));

        Assert.Equal(2, result.Count);

        var wdo = result.Single(o => o.Asset == "WDO");
        Assert.Equal(2m, wdo.CumQty); // 0,4 cheio * 5 = 2 minis
        Assert.Equal(5005m, wdo.AvgPx);

        var dol = result.Single(o => o.Asset == "DOL");
        Assert.Equal(1m, dol.CumQty);
        Assert.All(result, o => Assert.Equal("1", o.OrderId));
    }

    [Fact]
    public void Build_SyntheticTradeWithoutSuffix_UnknownLeg()
    {
        var order = Assert.Single(Build(
            [CreateOrder("1", "BTGDOLZ26")],
            new DayTrade("1", 1, 5000, "1234")));

        Assert.Equal(BillableOrderBuilder.UnknownLeg, order.Asset);
    }

    [Fact]
    public void Build_SyntheticWithoutTradesOfDay_Excluded()
    {
        Assert.Empty(Build([CreateOrder("1", "BTGDOLZ26")]));
    }

    [Fact]
    public void NeedsTrades_OnlyGoodTillAndSynthetic()
    {
        Assert.False(BillableOrderBuilder.NeedsTrades(CreateOrder("1", "PETR4", tif: "DAY"), SymbolAssets));
        Assert.False(BillableOrderBuilder.NeedsTrades(CreateOrder("1", "VALE3"), SymbolAssets));
        Assert.True(BillableOrderBuilder.NeedsTrades(CreateOrder("1", "PETR4", tif: "GTC"), SymbolAssets));
        Assert.True(BillableOrderBuilder.NeedsTrades(CreateOrder("1", "BTGDOLZ26"), SymbolAssets));
    }
}
