using FinancialEngineCore.Domain;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Tests;

public class MarketDataFeeCalculatorTests
{
    private const int Year = 2024;
    private const int Month = 11;

    private static UserExchangeAccount Account(int broker, string trader) => new(broker, trader);

    private static MarketDataFeeConfig Config(long id, int broker, decimal amount, DateOnly? from = null, DateOnly? to = null)
        => new()
        {
            Id = id,
            Broker = broker,
            AmountPerUser = amount,
            ValidFrom = from ?? new DateOnly(2024, 1, 1),
            ValidTo = to ?? new DateOnly(2099, 1, 1),
            IsActive = true
        };

    private static MarketDataExclusion Exclusion(int? broker, string trader, bool active = true)
        => new() { Broker = broker, Trader = trader, IsActive = active };

    [Fact]
    public void Calculate_AmountPerLinkedUser()
    {
        var result = MarketDataFeeCalculator.Calculate(Year, Month,
            [Account(1, "JOAO"), Account(1, "MARIA"), Account(1, "PEDRO")],
            [],
            [Config(10, 1, 150m)]);

        Assert.Equal(3, result.Charges.Count);
        Assert.Equal(450m, result.Charges.Sum(c => c.Amount));
        Assert.All(result.Charges, c => Assert.Equal(10, c.ConfigId));
    }

    [Fact]
    public void Calculate_UserSeenManyTimesOrWithDifferentCasing_ChargedOnce()
    {
        var result = MarketDataFeeCalculator.Calculate(Year, Month,
            [Account(1, "JOAO"), Account(1, "joao"), Account(1, "JOAO")],
            [],
            [Config(10, 1, 150m)]);

        Assert.Single(result.Charges);
    }

    [Fact]
    public void Calculate_SameUserInTwoBrokers_ChargedInEach()
    {
        var result = MarketDataFeeCalculator.Calculate(Year, Month,
            [Account(1, "JOAO"), Account(2, "JOAO")],
            [],
            [Config(10, 1, 150m), Config(20, 2, 90m)]);

        Assert.Equal(240m, result.Charges.Sum(c => c.Amount));
    }

    [Fact]
    public void Calculate_GlobalAndBrokerExclusions_CaseInsensitive()
    {
        var result = MarketDataFeeCalculator.Calculate(Year, Month,
            [Account(1, "JOAO"), Account(1, "API_BOT"), Account(2, "API_BOT"), Account(1, "ADMIN"), Account(2, "ADMIN")],
            [Exclusion(null, "api_bot"), Exclusion(1, "Admin"), Exclusion(2, "JOAO", active: false)],
            [Config(10, 1, 100m), Config(20, 2, 100m)]);

        // Charged: JOAO@1, ADMIN@2 (ADMIN only excluded for broker 1; inactive exclusion ignored)
        Assert.Equal(
            new[] { (1, "JOAO"), (2, "ADMIN") },
            result.Charges.Select(c => (c.Broker, c.Trader)).OrderBy(x => x.Broker));
        Assert.Equal(3, result.Excluded.Count);
    }

    [Fact]
    public void Calculate_ConfigValidity_LatestValidFromWins_ExpiredIgnored()
    {
        var result = MarketDataFeeCalculator.Calculate(Year, Month,
            [Account(1, "JOAO")],
            [],
            [
                Config(1, 1, 100m, new DateOnly(2024, 1, 1), new DateOnly(2099, 1, 1)),
                Config(2, 1, 120m, new DateOnly(2024, 11, 15), new DateOnly(2099, 1, 1)),
                Config(3, 1, 999m, new DateOnly(2023, 1, 1), new DateOnly(2024, 10, 31)) // expired before Nov
            ]);

        var charge = Assert.Single(result.Charges);
        Assert.Equal(120m, charge.Amount);
        Assert.Equal(2, charge.ConfigId);
    }

    [Fact]
    public void Calculate_BrokerWithoutConfig_NotChargedAndReported()
    {
        var result = MarketDataFeeCalculator.Calculate(Year, Month,
            [Account(1, "JOAO"), Account(3, "ANA")],
            [],
            [Config(10, 1, 150m)]);

        Assert.Single(result.Charges);
        Assert.Equal(new[] { 3 }, result.BrokersWithoutConfig);
    }
}
