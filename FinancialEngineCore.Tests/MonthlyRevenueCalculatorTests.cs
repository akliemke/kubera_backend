using FinancialEngineCore.Domain;
using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Tests;

public class MonthlyRevenueCalculatorTests
{
    private const int Year = 2024;

    private static TraderMonthSummary Fee(int broker, int month, string key, decimal fee, string keyType = BillingKeyTypes.Trader)
        => new() { Broker = broker, Month = month, Trader = key, KeyType = keyType, AssetClass = "", TotalFee = fee };

    private static FixedMonthlyFee Fixed(int broker, decimal amount, DateOnly from, DateOnly to)
        => new() { Broker = broker, Description = "Plataforma", Amount = amount, ValidFrom = from, ValidTo = to, IsActive = true };

    private static EntityBillingConfig Entity(int broker, string entity, decimal floor, DateOnly from, DateOnly to)
        => new() { Broker = broker, Entity = entity, FloorFee = floor, ValidFrom = from, ValidTo = to, IsActive = true };

    [Fact]
    public void Calculate_CombinesVariableFixedAndFloor()
    {
        var result = MonthlyRevenueCalculator.Calculate(
            Year, [3],
            [Fee(1, 3, "MESA_A", 400m, BillingKeyTypes.Entity), Fee(1, 3, "JOAO", 600m)],
            [Entity(1, "MESA_A", 1000m, new DateOnly(2024, 1, 1), new DateOnly(2099, 1, 1))],
            [Fixed(1, 250m, new DateOnly(2024, 1, 1), new DateOnly(2099, 1, 1))], []);

        var revenue = Assert.Single(result);
        Assert.Equal(1000m, revenue.VariableFee);
        Assert.Equal(250m, revenue.FixedFee);
        Assert.Equal(600m, revenue.FloorAdjustment); // MESA_A: 1000 - 400
        Assert.Equal(1850m, revenue.TotalFee);
    }

    [Fact]
    public void Calculate_BrokerWithOnlyFixedFee_IsIncluded()
    {
        var result = MonthlyRevenueCalculator.Calculate(
            Year, [3], [], [],
            [Fixed(2, 500m, new DateOnly(2024, 1, 1), new DateOnly(2099, 1, 1))], []);

        var revenue = Assert.Single(result);
        Assert.Equal(2, revenue.Broker);
        Assert.Equal(500m, revenue.TotalFee);
    }

    [Fact]
    public void Calculate_FixedFeeAndFloorRespectValidityPerMonth()
    {
        // Fixed fee valid only from March; floor config valid only until February
        var result = MonthlyRevenueCalculator.Calculate(
            Year, [2, 3],
            [Fee(1, 2, "MESA_A", 100m, BillingKeyTypes.Entity), Fee(1, 3, "MESA_A", 100m, BillingKeyTypes.Entity)],
            [Entity(1, "MESA_A", 1000m, new DateOnly(2024, 1, 1), new DateOnly(2024, 2, 29))],
            [Fixed(1, 250m, new DateOnly(2024, 3, 1), new DateOnly(2099, 1, 1))], []);

        var feb = result.Single(r => r.Month == 2);
        Assert.Equal(0m, feb.FixedFee);
        Assert.Equal(900m, feb.FloorAdjustment);

        var mar = result.Single(r => r.Month == 3);
        Assert.Equal(250m, mar.FixedFee);
        Assert.Equal(0m, mar.FloorAdjustment);
    }

    [Fact]
    public void Calculate_MarketDataAddedToTotal_BrokerWithOnlyMarketDataIncluded()
    {
        var result = MonthlyRevenueCalculator.Calculate(
            Year, [3],
            [Fee(1, 3, "JOAO", 1000m)],
            [],
            [],
            [
                new MarketDataBilling { Broker = 1, Year = Year, Month = 3, Trader = "JOAO", Amount = 150m },
                new MarketDataBilling { Broker = 1, Year = Year, Month = 3, Trader = "MARIA", Amount = 150m },
                new MarketDataBilling { Broker = 2, Year = Year, Month = 3, Trader = "ANA", Amount = 90m },
                new MarketDataBilling { Broker = 1, Year = Year, Month = 4, Trader = "JOAO", Amount = 150m } // other month
            ]);

        var broker1 = result.Single(r => r.Broker == 1);
        Assert.Equal(300m, broker1.MarketDataFee);
        Assert.Equal(1300m, broker1.TotalFee);

        var broker2 = result.Single(r => r.Broker == 2);
        Assert.Equal(90m, broker2.TotalFee);
    }

    [Fact]
    public void Calculate_FloorIsPerBroker()
    {
        // Same entity name in two brokers: each broker's floor uses only its own fees/configs
        var result = MonthlyRevenueCalculator.Calculate(
            Year, [3],
            [Fee(1, 3, "MESA_A", 900m, BillingKeyTypes.Entity), Fee(2, 3, "MESA_A", 100m, BillingKeyTypes.Entity)],
            [Entity(1, "MESA_A", 1000m, new DateOnly(2024, 1, 1), new DateOnly(2099, 1, 1))],
            [], []);

        Assert.Equal(100m, result.Single(r => r.Broker == 1).FloorAdjustment);
        Assert.Equal(0m, result.Single(r => r.Broker == 2).FloorAdjustment);
    }
}
