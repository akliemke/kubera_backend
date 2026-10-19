using FinancialEngineCore.Domain;

namespace FinancialEngineCore.Tests;

public class FeeDistributionTests
{
    [Fact]
    public void Distribute_EqualWeights_SumMatchesTotal()
    {
        var shares = FeeDistribution.Distribute([1m, 1m, 1m], 100m);

        Assert.Equal(100m, shares.Sum());
        Assert.Equal(new[] { 33.34m, 33.33m, 33.33m }, shares);
    }

    [Fact]
    public void Distribute_TotalWithMoreThanTwoDecimals_SumMatchesRoundedTotal()
    {
        var shares = FeeDistribution.Distribute([3m, 7m], 10.005m);

        Assert.Equal(10.01m, shares.Sum());
    }

    [Fact]
    public void Distribute_ZeroTotalOrZeroWeights_AllZero()
    {
        Assert.All(FeeDistribution.Distribute([1m, 2m], 0m), s => Assert.Equal(0m, s));
        Assert.All(FeeDistribution.Distribute([0m, 0m], 50m), s => Assert.Equal(0m, s));
    }

    [Fact]
    public void Distribute_Proportional()
    {
        var shares = FeeDistribution.Distribute([1m, 3m], 100m);

        Assert.Equal(new[] { 25m, 75m }, shares);
    }
}
