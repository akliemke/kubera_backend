using FinancialEngineCore.Domain;
using FinancialEngineCore.Domain.Services;
using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Tests;

public class BillingKeyResolutionTests
{
    private static Order CreateOrder(string trader, string entity, string assetClass = "EQUITY")
    {
        return new Order
        {
            OrderId = "1",
            CumQty = 100,
            AvgPx = 100,
            Symbol = "PETR4",
            AssetClass = assetClass,
            EnteringTrader = trader,
            Entity = entity,
            ExecBroker = "1",
            Instance = "OMS1",
            Side = "BUY",
            TradeDate = DateOnly.FromDateTime(DateTime.Today)
        };
    }

    private static EquityFeeBracket Bracket(string? trader, string? keyType, decimal bps) => new()
    {
        Id = bps.GetHashCode(),
        Broker = 1,
        Trader = trader,
        KeyType = keyType,
        NotionalFrom = 0,
        Bps = bps,
        IsActive = true
    };

    private static BmfFeeConfig BmfConfig(string? trader, string? keyType, decimal valorMini) => new()
    {
        Id = valorMini.GetHashCode(),
        Broker = 1,
        Trader = trader,
        KeyType = keyType,
        ValorMini = valorMini,
        IsActive = true
    };

    private static BrokerBillingContext CreateContext(
        IReadOnlyList<EquityFeeBracket>? brackets = null,
        IReadOnlyList<BmfFeeConfig>? bmfConfigs = null,
        IReadOnlyList<TraderMonthSummary>? monthSummaries = null)
    {
        return new BrokerBillingContext(
            brackets ?? [],
            bmfConfigs ?? [],
            monthSummaries ?? [],
            [],
            new Dictionary<string, decimal>());
    }

    [Fact]
    public void ResolveBillingKey_EntityConfigured_ReturnsEntity()
    {
        var order = CreateOrder("TRADER1", "MESA_A");
        var entitySet = BillingProcessingService.BuildEntityKeyMap(new[] { "MESA_A" });

        var result = BillingProcessingService.ResolveBillingKey(order, entitySet);

        Assert.Equal(BillingKey.Entity("MESA_A"), result);
        Assert.Equal(BillingKeyTypes.Entity, result.Type);
    }

    [Fact]
    public void ResolveBillingKey_EntityNotConfigured_ReturnsTrader()
    {
        var order = CreateOrder("TRADER1", "MESA_B");
        var entitySet = BillingProcessingService.BuildEntityKeyMap(new[] { "MESA_A" });

        var result = BillingProcessingService.ResolveBillingKey(order, entitySet);

        Assert.Equal(BillingKey.Trader("TRADER1"), result);
    }

    [Fact]
    public void ResolveBillingKey_NoConfigs_ReturnsTrader()
    {
        var order = CreateOrder("TRADER1", "MESA_A");
        var entitySet = BillingProcessingService.BuildEntityKeyMap([]);

        var result = BillingProcessingService.ResolveBillingKey(order, entitySet);

        Assert.Equal(BillingKey.Trader("TRADER1"), result);
    }

    [Fact]
    public void ResolveBillingKey_CaseInsensitive_ReturnsConfiguredSpelling()
    {
        var order = CreateOrder("TRADER1", "mesa_a");
        var entitySet = BillingProcessingService.BuildEntityKeyMap(new[] { "MESA_A" });

        var result = BillingProcessingService.ResolveBillingKey(order, entitySet);

        // Canonical key = spelling from the entity config, so all casings group together
        Assert.Equal("MESA_A", result.Key);
    }

    [Fact]
    public void ResolveBillingKey_MixedGrouping_EntityAndTrader()
    {
        var entitySet = BillingProcessingService.BuildEntityKeyMap(new[] { "MESA_A" });

        var order1 = CreateOrder("TRADER1", "MESA_A"); // entity configured
        var order2 = CreateOrder("TRADER2", "MESA_B"); // entity NOT configured
        var order3 = CreateOrder("TRADER3", "MESA_A"); // entity configured (different trader, same entity)

        Assert.Equal(BillingKey.Entity("MESA_A"), BillingProcessingService.ResolveBillingKey(order1, entitySet));
        Assert.Equal(BillingKey.Trader("TRADER2"), BillingProcessingService.ResolveBillingKey(order2, entitySet));
        Assert.Equal(BillingKey.Entity("MESA_A"), BillingProcessingService.ResolveBillingKey(order3, entitySet));
    }

    [Fact]
    public void ResolveBillingKey_SameEntity_DifferentAssetClasses_GroupedSeparately()
    {
        var entitySet = BillingProcessingService.BuildEntityKeyMap(new[] { "MESA_A" });

        var equityOrder = CreateOrder("TRADER1", "MESA_A", "EQUITY");
        var bmfOrder = CreateOrder("TRADER1", "MESA_A", "BMF");

        // Both resolve to entity "MESA_A", but would be grouped separately by AssetClass
        Assert.Equal(BillingKey.Entity("MESA_A"), BillingProcessingService.ResolveBillingKey(equityOrder, entitySet));
        Assert.Equal(BillingKey.Entity("MESA_A"), BillingProcessingService.ResolveBillingKey(bmfOrder, entitySet));
        // The actual separation by AssetClass happens in the GroupBy in ProcessDateAsync
    }

    [Fact]
    public void ResolveBillingKey_EntityAndTraderWithSameName_AreDifferentKeys()
    {
        var entitySet = BillingProcessingService.BuildEntityKeyMap(new[] { "JOAO" });

        var entityOrder = CreateOrder("TRADER1", "JOAO"); // entity JOAO configured
        var traderOrder = CreateOrder("JOAO", "MESA_B");  // trader JOAO, entity not configured

        var entityKey = BillingProcessingService.ResolveBillingKey(entityOrder, entitySet);
        var traderKey = BillingProcessingService.ResolveBillingKey(traderOrder, entitySet);

        Assert.Equal("JOAO", entityKey.Key);
        Assert.Equal("JOAO", traderKey.Key);
        Assert.NotEqual(entityKey, traderKey);
    }

    [Fact]
    public void ResolveBillingKey_TraderWithSpecialRule_LeavesEntityOnlyForThatAssetClass()
    {
        var entitySet = BillingProcessingService.BuildEntityKeyMap(new[] { "MESA_A" });
        var context = CreateContext(brackets:
        [
            Bracket(null, null, 0.5m),
            Bracket("JOAO", BillingKeyTypes.Trader, 0.2m)
        ]);

        var equityOrder = CreateOrder("JOAO", "MESA_A", "EQUITY");
        var bmfOrder = CreateOrder("JOAO", "MESA_A", "BMF");

        // EQUITY special rule prevails: billed under the trader's own key
        Assert.Equal(BillingKey.Trader("JOAO"),
            BillingProcessingService.ResolveBillingKey(equityOrder, entitySet, context.HasTraderSpecialRule));
        // No BMF special rule: stays in the entity
        Assert.Equal(BillingKey.Entity("MESA_A"),
            BillingProcessingService.ResolveBillingKey(bmfOrder, entitySet, context.HasTraderSpecialRule));
    }

    [Fact]
    public void ResolveBillingKey_EntityRuleWithTraderName_DoesNotPullTraderOutOfEntity()
    {
        var entitySet = BillingProcessingService.BuildEntityKeyMap(new[] { "MESA_A" });
        // Special rule for an ENTITY named JOAO, not for the trader JOAO
        var context = CreateContext(brackets: [Bracket("JOAO", BillingKeyTypes.Entity, 0.2m)]);

        var order = CreateOrder("JOAO", "MESA_A");

        Assert.Equal(BillingKey.Entity("MESA_A"),
            BillingProcessingService.ResolveBillingKey(order, entitySet, context.HasTraderSpecialRule));
    }

    [Fact]
    public void Context_SpecialRules_MatchByKeyType()
    {
        var defaultBracket = Bracket(null, null, 0.5m);
        var traderBracket = Bracket("X", BillingKeyTypes.Trader, 0.2m);
        var entityBracket = Bracket("X", BillingKeyTypes.Entity, 0.1m);
        var context = CreateContext(
            brackets: [defaultBracket, traderBracket, entityBracket],
            bmfConfigs: [BmfConfig(null, null, 1m), BmfConfig("X", BillingKeyTypes.Entity, 2m)]);

        Assert.Equal(0.2m, Assert.Single(context.GetEquityBrackets(BillingKey.Trader("x"))).Bps);
        Assert.Equal(0.1m, Assert.Single(context.GetEquityBrackets(BillingKey.Entity("X"))).Bps);
        Assert.Equal(0.5m, Assert.Single(context.GetEquityBrackets(BillingKey.Trader("Y"))).Bps);

        Assert.Equal(2m, context.GetBmfConfig(BillingKey.Entity("x"))!.ValorMini);
        Assert.Equal(1m, context.GetBmfConfig(BillingKey.Trader("X"))!.ValorMini); // falls back to default
    }

    [Fact]
    public void Context_Accumulated_SeparatedByKeyType()
    {
        var context = CreateContext(monthSummaries:
        [
            new TraderMonthSummary { Trader = "JOAO", KeyType = BillingKeyTypes.Trader, AssetClass = "EQUITY", TotalNotional = 100m },
            new TraderMonthSummary { Trader = "joao", KeyType = BillingKeyTypes.Entity, AssetClass = "EQUITY", TotalNotional = 900m },
            new TraderMonthSummary { Trader = "Joao", KeyType = BillingKeyTypes.Trader, AssetClass = "EQUITY", TotalNotional = 50m }
        ]);

        Assert.Equal(150m, context.GetEquityAccumulated(BillingKey.Trader("JOAO")));
        Assert.Equal(900m, context.GetEquityAccumulated(BillingKey.Entity("JOAO")));
    }

    [Fact]
    public void BuildPriceFactorMap_DuplicateSymbol_MostRecentWinsAndCaseInsensitive()
    {
        var factors = new[]
        {
            new PriceModificationFactor { Id = 1, Symbol = "AZUL54", Factor = 1_000, ValidFrom = new DateOnly(2026, 1, 1) },
            new PriceModificationFactor { Id = 2, Symbol = "azul54", Factor = 10_000, ValidFrom = new DateOnly(2026, 6, 1) },
        };

        var map = BillingProcessingService.BuildPriceFactorMap(factors);

        Assert.Single(map);
        Assert.Equal(10_000m, map["AZUL54"]);
    }
}
