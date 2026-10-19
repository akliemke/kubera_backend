using FinancialEngineCore.Domain.Services;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Tests;

public class BillingKeyResolutionTests
{
    private static Order CreateOrder(string trader, string entity, string assetClass = "EQUITY")
    {
        return new Order
        {
            Id = 1,
            CumQty = 100,
            AvgPx = 100,
            Symbol = "PETR4",
            AssetClass = assetClass,
            EnteringTrader = trader,
            Entity = entity,
            EnteringFirm = "1",
            OmsInstance = "OMS1",
            Side = "BUY",
            TradeDate = DateOnly.FromDateTime(DateTime.Today)
        };
    }

    [Fact]
    public void ResolveBillingKey_EntityConfigured_ReturnsEntity()
    {
        var order = CreateOrder("TRADER1", "MESA_A");
        var entitySet = BillingProcessingService.BuildEntityKeyMap(new[] { "MESA_A" });

        var result = BillingProcessingService.ResolveBillingKey(order, entitySet);

        Assert.Equal("MESA_A", result);
    }

    [Fact]
    public void ResolveBillingKey_EntityNotConfigured_ReturnsTrader()
    {
        var order = CreateOrder("TRADER1", "MESA_B");
        var entitySet = BillingProcessingService.BuildEntityKeyMap(new[] { "MESA_A" });

        var result = BillingProcessingService.ResolveBillingKey(order, entitySet);

        Assert.Equal("TRADER1", result);
    }

    [Fact]
    public void ResolveBillingKey_NoConfigs_ReturnsTrader()
    {
        var order = CreateOrder("TRADER1", "MESA_A");
        var entitySet = BillingProcessingService.BuildEntityKeyMap([]);

        var result = BillingProcessingService.ResolveBillingKey(order, entitySet);

        Assert.Equal("TRADER1", result);
    }

    [Fact]
    public void ResolveBillingKey_CaseInsensitive_ReturnsConfiguredSpelling()
    {
        var order = CreateOrder("TRADER1", "mesa_a");
        var entitySet = BillingProcessingService.BuildEntityKeyMap(new[] { "MESA_A" });

        var result = BillingProcessingService.ResolveBillingKey(order, entitySet);

        // Canonical key = spelling from the entity config, so all casings group together
        Assert.Equal("MESA_A", result);
    }

    [Fact]
    public void ResolveBillingKey_MixedGrouping_EntityAndTrader()
    {
        var entitySet = BillingProcessingService.BuildEntityKeyMap(new[] { "MESA_A" });

        var order1 = CreateOrder("TRADER1", "MESA_A"); // entity configured
        var order2 = CreateOrder("TRADER2", "MESA_B"); // entity NOT configured
        var order3 = CreateOrder("TRADER3", "MESA_A"); // entity configured (different trader, same entity)

        Assert.Equal("MESA_A", BillingProcessingService.ResolveBillingKey(order1, entitySet));
        Assert.Equal("TRADER2", BillingProcessingService.ResolveBillingKey(order2, entitySet));
        Assert.Equal("MESA_A", BillingProcessingService.ResolveBillingKey(order3, entitySet));
    }

    [Fact]
    public void ResolveBillingKey_SameEntity_DifferentAssetClasses_GroupedSeparately()
    {
        var entitySet = BillingProcessingService.BuildEntityKeyMap(new[] { "MESA_A" });

        var equityOrder = CreateOrder("TRADER1", "MESA_A", "EQUITY");
        var bmfOrder = CreateOrder("TRADER1", "MESA_A", "BMF");

        // Both resolve to entity "MESA_A", but would be grouped separately by AssetClass
        var equityKey = BillingProcessingService.ResolveBillingKey(equityOrder, entitySet);
        var bmfKey = BillingProcessingService.ResolveBillingKey(bmfOrder, entitySet);

        Assert.Equal("MESA_A", equityKey);
        Assert.Equal("MESA_A", bmfKey);
        // The actual separation by AssetClass happens in the GroupBy in ProcessBrokerAsync
    }
}
