using FinancialEngineCore.Domain;
using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Tests;

public class FloorFeeCalculatorTests
{
    private static TraderMonthSummary Summary(string key, decimal fee, string assetClass = "EQUITY")
        => new() { Trader = key, AssetClass = assetClass, TotalFee = fee };

    private static EntityBillingConfig Config(string entity, decimal? floor, DateOnly? validFrom = null)
        => new() { Entity = entity, FloorFee = floor, ValidFrom = validFrom ?? new DateOnly(2024, 1, 1), ValidTo = new DateOnly(2099, 1, 1) };

    [Fact]
    public void Calculate_BelowFloor_AdjustsDifference_SumsAssetClasses()
    {
        var result = FloorFeeCalculator.Calculate(
            [Summary("MESA_A", 300m), Summary("MESA_A", 200m, "BMF")],
            [Config("MESA_A", 1000m)]);

        var adj = Assert.Single(result);
        Assert.Equal(500m, adj.CalculatedFee);
        Assert.Equal(500m, adj.Adjustment);
    }

    [Fact]
    public void Calculate_AboveFloorOrNoFloor_NoAdjustment()
    {
        var result = FloorFeeCalculator.Calculate(
            [Summary("MESA_A", 1500m), Summary("MESA_B", 10m)],
            [Config("MESA_A", 1000m), Config("MESA_B", null)]);

        Assert.Empty(result);
    }

    [Fact]
    public void Calculate_CaseInsensitiveMatch()
    {
        var result = FloorFeeCalculator.Calculate([Summary("mesa_a", 400m)], [Config("MESA_A", 1000m)]);

        Assert.Equal(600m, Assert.Single(result).Adjustment);
    }

    [Fact]
    public void Calculate_TwoConfigsSameEntityInMonth_UsesLatestOnce()
    {
        var result = FloorFeeCalculator.Calculate(
            [Summary("MESA_A", 0m)],
            [Config("MESA_A", 1000m, new DateOnly(2024, 1, 1)), Config("MESA_A", 1500m, new DateOnly(2024, 6, 15))]);

        Assert.Equal(1500m, Assert.Single(result).Adjustment);
    }
}
