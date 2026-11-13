using FinancialEngineCore.Domain.Strategies;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Tests;

public class EquityCalculationStrategyTests
{
    private static readonly Dictionary<string, decimal> NoPriceFactors = new();

    private static List<EquityFeeBracket> CreateDefaultBrackets()
    {
        return new List<EquityFeeBracket>
        {
            new() { Id = 1, Broker = 1, NotionalFrom = 0, NotionalTo = 50_000_000, Bps = 0.5m, Cap = 2100, IsActive = true },
            new() { Id = 2, Broker = 1, NotionalFrom = 50_000_000, NotionalTo = 150_000_000, Bps = 0.45m, Cap = 5200, IsActive = true },
            new() { Id = 3, Broker = 1, NotionalFrom = 150_000_000, NotionalTo = 500_000_000, Bps = 0.35m, Cap = 15000, IsActive = true },
            new() { Id = 4, Broker = 1, NotionalFrom = 500_000_000, NotionalTo = null, Bps = 0.30m, Cap = 31000, IsActive = true },
        };
    }

    private static Order CreateOrder(decimal qty, decimal price, string trader = "TRADER1", string symbol = "PETR4")
    {
        return new Order
        {
            Id = 1,
            CumQty = qty,
            AvgPx = price,
            Symbol = symbol,
            AssetClass = "EQUITY",
            EnteringTrader = trader,
            EnteringFirm = "1",
            Entity = "MESA1",
            OmsInstance = "OMS1",
            Side = "BUY",
            TradeDate = DateOnly.FromDateTime(DateTime.Today)
        };
    }

    [Fact]
    public void Calculate_SingleOrder_FirstBracket_NoCapHit()
    {
        // Arrange: 10M notional, should be charged at 0.5 bps
        var orders = new List<Order> { CreateOrder(100_000, 100) }; // 10M notional
        var brackets = CreateDefaultBrackets();
        var feePerBracket = new Dictionary<decimal, decimal>();

        // Act
        var result = EquityCalculationStrategy.Calculate(orders, brackets, 0, feePerBracket, NoPriceFactors);

        // Assert: 10M * 0.00005 = 500
        Assert.Equal(10_000_000m, result.TotalNotional);
        Assert.Equal(500m, result.TotalFee);
        Assert.Equal(0m, result.AccumulatedBefore);
        Assert.Equal(10_000_000m, result.AccumulatedAfter);
    }

    [Fact]
    public void Calculate_SingleOrder_FirstBracket_HitsCap()
    {
        // Arrange: 50M notional at 0.5 bps = 2500, but CAP is 2100
        var orders = new List<Order> { CreateOrder(500_000, 100) }; // 50M notional
        var brackets = CreateDefaultBrackets();
        var feePerBracket = new Dictionary<decimal, decimal>();

        // Act
        var result = EquityCalculationStrategy.Calculate(orders, brackets, 0, feePerBracket, NoPriceFactors);

        // Assert: 50M * 0.00005 = 2500 -> CAP 2100
        Assert.Equal(50_000_000m, result.TotalNotional);
        Assert.Equal(2100m, result.TotalFee);
    }

    [Fact]
    public void Calculate_MultipleBrackets_200M_NoCapHit()
    {
        // Arrange: 200M notional crossing 3 brackets
        // Faixa 1: 50M * 0.00005 = 2500 -> CAP 2100 -> 2100
        // Faixa 2: 100M * 0.000045 = 4500 (no cap)
        // Faixa 3: 50M * 0.000035 = 1750 (no cap)
        // Total: 2100 + 4500 + 1750 = 8350
        var orders = new List<Order> { CreateOrder(2_000_000, 100) }; // 200M notional
        var brackets = CreateDefaultBrackets();
        var feePerBracket = new Dictionary<decimal, decimal>();

        // Act
        var result = EquityCalculationStrategy.Calculate(orders, brackets, 0, feePerBracket, NoPriceFactors);

        // Assert
        Assert.Equal(200_000_000m, result.TotalNotional);
        Assert.Equal(8350m, result.TotalFee);
    }

    [Fact]
    public void Calculate_WithAccumulated_PartialFirstBracket()
    {
        // Arrange: Already accumulated 40M, now trading 30M more
        // First bracket has 10M remaining (40M -> 50M), then 20M in second bracket
        // Faixa 1: 10M * 0.00005 = 500 (no cap yet)
        // Faixa 2: 20M * 0.000045 = 900
        // Total: 500 + 900 = 1400
        var orders = new List<Order> { CreateOrder(300_000, 100) }; // 30M notional
        var brackets = CreateDefaultBrackets();
        var feePerBracket = new Dictionary<decimal, decimal>();

        // Act
        var result = EquityCalculationStrategy.Calculate(orders, brackets, 40_000_000m, feePerBracket, NoPriceFactors);

        // Assert
        Assert.Equal(30_000_000m, result.TotalNotional);
        Assert.Equal(40_000_000m, result.AccumulatedBefore);
        Assert.Equal(70_000_000m, result.AccumulatedAfter);
        Assert.Equal(1400m, result.TotalFee);
    }

    [Fact]
    public void Calculate_WithAccumulated_CapAlreadyPartiallyUsed()
    {
        // Arrange: Already accumulated 40M with 2000 fee charged in bracket 1
        // Now trading 30M more
        // Faixa 1: 10M * 0.00005 = 500, but CAP 2100 - 2000 = 100 remaining
        // Faixa 2: 20M * 0.000045 = 900
        // Total: 100 + 900 = 1000
        var orders = new List<Order> { CreateOrder(300_000, 100) }; // 30M notional
        var brackets = CreateDefaultBrackets();
        var feePerBracket = new Dictionary<decimal, decimal> { { 0m, 2000m } };

        // Act
        var result = EquityCalculationStrategy.Calculate(orders, brackets, 40_000_000m, feePerBracket, NoPriceFactors);

        // Assert
        Assert.Equal(30_000_000m, result.TotalNotional);
        Assert.Equal(1000m, result.TotalFee);
    }

    [Fact]
    public void Calculate_MultipleOrders_FeeDistributedProportionally()
    {
        // Arrange: 2 orders of 5M each = 10M total
        // Total fee: 10M * 0.00005 = 500
        // Each order should get 250 (50%)
        var orders = new List<Order>
        {
            CreateOrder(50_000, 100), // 5M
            CreateOrder(50_000, 100), // 5M
        };
        var brackets = CreateDefaultBrackets();
        var feePerBracket = new Dictionary<decimal, decimal>();

        // Act
        var result = EquityCalculationStrategy.Calculate(orders, brackets, 0, feePerBracket, NoPriceFactors);

        // Assert
        Assert.Equal(10_000_000m, result.TotalNotional);
        Assert.Equal(500m, result.TotalFee);
        Assert.Equal(2, result.OrderFees.Count);
        Assert.Equal(250m, result.OrderFees[0].FeeAmount);
        Assert.Equal(250m, result.OrderFees[1].FeeAmount);
    }

    [Fact]
    public void Calculate_MultipleOrders_DifferentSizes_ProportionalDistribution()
    {
        // Arrange: 2 orders - 8M and 2M = 10M total
        // Total fee: 10M * 0.00005 = 500
        // Order 1 (8M): 80% -> 400
        // Order 2 (2M): 20% -> 100
        var orders = new List<Order>
        {
            CreateOrder(80_000, 100), // 8M
            CreateOrder(20_000, 100), // 2M
        };
        var brackets = CreateDefaultBrackets();
        var feePerBracket = new Dictionary<decimal, decimal>();

        // Act
        var result = EquityCalculationStrategy.Calculate(orders, brackets, 0, feePerBracket, NoPriceFactors);

        // Assert
        Assert.Equal(10_000_000m, result.TotalNotional);
        Assert.Equal(500m, result.TotalFee);
        Assert.Equal(400m, result.OrderFees[0].FeeAmount);
        Assert.Equal(100m, result.OrderFees[1].FeeAmount);
    }

    [Fact]
    public void Calculate_NoBrackets_ReturnsZeroFee()
    {
        // Arrange
        var orders = new List<Order> { CreateOrder(100_000, 100) };
        var brackets = new List<EquityFeeBracket>();
        var feePerBracket = new Dictionary<decimal, decimal>();

        // Act
        var result = EquityCalculationStrategy.Calculate(orders, brackets, 0, feePerBracket, NoPriceFactors);

        // Assert
        Assert.Equal(10_000_000m, result.TotalNotional);
        Assert.Equal(0m, result.TotalFee);
    }

    [Fact]
    public void Calculate_VeryLargeVolume_CrossesAllBrackets()
    {
        // Arrange: 600M notional crossing all 4 brackets
        // Faixa 1: 50M * 0.00005 = 2500 -> CAP 2100
        // Faixa 2: 100M * 0.000045 = 4500 -> no cap (cap 5200)
        // Faixa 3: 350M * 0.000035 = 12250 -> no cap (cap 15000)
        // Faixa 4: 100M * 0.00003 = 3000 -> no cap (cap 31000)
        // Total: 2100 + 4500 + 12250 + 3000 = 21850
        var orders = new List<Order> { CreateOrder(6_000_000, 100) }; // 600M notional
        var brackets = CreateDefaultBrackets();
        var feePerBracket = new Dictionary<decimal, decimal>();

        // Act
        var result = EquityCalculationStrategy.Calculate(orders, brackets, 0, feePerBracket, NoPriceFactors);

        // Assert
        Assert.Equal(600_000_000m, result.TotalNotional);
        Assert.Equal(21850m, result.TotalFee);
    }

    [Fact]
    public void Calculate_BracketFees_ReturnedCorrectly()
    {
        // Arrange: 200M notional crossing 3 brackets
        var orders = new List<Order> { CreateOrder(2_000_000, 100) }; // 200M notional
        var brackets = CreateDefaultBrackets();
        var feePerBracket = new Dictionary<decimal, decimal>();

        // Act
        var result = EquityCalculationStrategy.Calculate(orders, brackets, 0, feePerBracket, NoPriceFactors);

        // Assert: 3 faixas foram usadas
        Assert.Equal(3, result.BracketFees.Count);

        // Faixa 1: 50M, calculated 2500, charged 2100 (CAP)
        var bracket1 = result.BracketFees.First(b => b.BracketId == 1);
        Assert.Equal(50_000_000m, bracket1.NotionalInBracket);
        Assert.Equal(2500m, bracket1.CalculatedFee);
        Assert.Equal(2100m, bracket1.ChargedFee);
        Assert.Equal(2100m, bracket1.BracketCap);

        // Faixa 2: 100M, calculated 4500, charged 4500
        var bracket2 = result.BracketFees.First(b => b.BracketId == 2);
        Assert.Equal(100_000_000m, bracket2.NotionalInBracket);
        Assert.Equal(4500m, bracket2.CalculatedFee);
        Assert.Equal(4500m, bracket2.ChargedFee);

        // Faixa 3: 50M, calculated 1750, charged 1750
        var bracket3 = result.BracketFees.First(b => b.BracketId == 3);
        Assert.Equal(50_000_000m, bracket3.NotionalInBracket);
        Assert.Equal(1750m, bracket3.CalculatedFee);
        Assert.Equal(1750m, bracket3.ChargedFee);
    }

    [Fact]
    public void Calculate_BracketFees_WithAccumulatedFee_CapAppliedCorrectly()
    {
        // Arrange: Already charged 2000 in bracket 1, now trading more
        var orders = new List<Order> { CreateOrder(300_000, 100) }; // 30M notional
        var brackets = CreateDefaultBrackets();
        var feePerBracket = new Dictionary<decimal, decimal> { { 0m, 2000m } };

        // Act
        var result = EquityCalculationStrategy.Calculate(orders, brackets, 40_000_000m, feePerBracket, NoPriceFactors);

        // Assert
        Assert.Equal(2, result.BracketFees.Count);

        // Faixa 1: 10M remaining, calculated 500, but CAP only has 100 remaining
        var bracket1 = result.BracketFees.First(b => b.BracketId == 1);
        Assert.Equal(10_000_000m, bracket1.NotionalInBracket);
        Assert.Equal(500m, bracket1.CalculatedFee);
        Assert.Equal(100m, bracket1.ChargedFee);
        Assert.Equal(2000m, bracket1.AccumulatedFeeBefore);

        // Faixa 2: 20M, full charge
        var bracket2 = result.BracketFees.First(b => b.BracketId == 2);
        Assert.Equal(20_000_000m, bracket2.NotionalInBracket);
        Assert.Equal(900m, bracket2.CalculatedFee);
        Assert.Equal(900m, bracket2.ChargedFee);
        Assert.Equal(0m, bracket2.AccumulatedFeeBefore);
    }

    [Fact]
    public void Calculate_BracketFees_EmptyWhenNoBrackets()
    {
        // Arrange
        var orders = new List<Order> { CreateOrder(100_000, 100) };
        var brackets = new List<EquityFeeBracket>();
        var feePerBracket = new Dictionary<decimal, decimal>();

        // Act
        var result = EquityCalculationStrategy.Calculate(orders, brackets, 0, feePerBracket, NoPriceFactors);

        // Assert
        Assert.Empty(result.BracketFees);
    }

    [Fact]
    public void Calculate_BracketFees_SumMatchesTotalFee()
    {
        // Arrange: Any volume that crosses multiple brackets
        var orders = new List<Order> { CreateOrder(6_000_000, 100) }; // 600M notional
        var brackets = CreateDefaultBrackets();
        var feePerBracket = new Dictionary<decimal, decimal>();

        // Act
        var result = EquityCalculationStrategy.Calculate(orders, brackets, 0, feePerBracket, NoPriceFactors);

        // Assert: Sum of ChargedFee in brackets should equal TotalFee
        var sumBracketFees = result.BracketFees.Sum(b => b.ChargedFee);
        Assert.Equal(result.TotalFee, sumBracketFees);
    }

    // ============================================
    // Price Modification Factor Tests
    // ============================================

    [Fact]
    public void Calculate_WithPriceFactor_NotionalAdjustedCorrectly()
    {
        // Arrange: AZUL54 with factor 10000
        // qty=100, price=1_000_000 → raw notional = 100M
        // adjusted notional = 100 × (1_000_000 / 10_000) = 100 × 100 = 10_000
        // But let's use more realistic numbers:
        // qty=100_000, price=1000 (represents price of 10000 shares)
        // raw notional = 100M, adjusted = 100_000 × (1000 / 10_000) = 100_000 × 0.1 = 10_000
        // Actually let's keep it simple:
        // qty=100_000, price=100 → raw notional = 10M
        // With factor 10: adjusted = 100_000 × (100/10) = 1M
        // Fee at 0.5 bps on 1M = 1M × 0.00005 = 50
        var orders = new List<Order> { CreateOrder(100_000, 100, symbol: "AZUL54") };
        var brackets = CreateDefaultBrackets();
        var feePerBracket = new Dictionary<decimal, decimal>();
        var priceFactors = new Dictionary<string, decimal> { { "AZUL54", 10m } };

        // Act
        var result = EquityCalculationStrategy.Calculate(orders, brackets, 0, feePerBracket, priceFactors);

        // Assert: adjusted notional = 100_000 × (100/10) = 1M
        Assert.Equal(1_000_000m, result.TotalNotional);
        // Fee: 1M × 0.00005 = 50
        Assert.Equal(50m, result.TotalFee);
        Assert.Equal(0m, result.AccumulatedBefore);
        Assert.Equal(1_000_000m, result.AccumulatedAfter);
    }

    [Fact]
    public void Calculate_MixOrdersWithAndWithoutFactor_CorrectNotionalAndDistribution()
    {
        // Arrange: 2 orders - AZUL54 (with factor) and PETR4 (without factor)
        // AZUL54: qty=100_000, price=100, factor=10 → adjusted = 100_000 × (100/10) = 1M
        // PETR4: qty=100_000, price=100 → notional = 100_000 × 100 = 10M
        // Total adjusted notional = 11M
        // Fee: 11M × 0.00005 = 550
        // AZUL54 proportion: 1M/11M ≈ 9.09% → 550 × (1/11) = 50
        // PETR4 proportion: 10M/11M ≈ 90.91% → 550 × (10/11) = 500
        var orders = new List<Order>
        {
            CreateOrder(100_000, 100, symbol: "AZUL54"),
            CreateOrder(100_000, 100, symbol: "PETR4"),
        };
        var brackets = CreateDefaultBrackets();
        var feePerBracket = new Dictionary<decimal, decimal>();
        var priceFactors = new Dictionary<string, decimal> { { "AZUL54", 10m } };

        // Act
        var result = EquityCalculationStrategy.Calculate(orders, brackets, 0, feePerBracket, priceFactors);

        // Assert
        Assert.Equal(11_000_000m, result.TotalNotional);
        Assert.Equal(550m, result.TotalFee);

        // AZUL54: 1M/11M * 550 = 50
        Assert.Equal(1_000_000m, result.OrderFees[0].Notional);
        Assert.Equal(50m, result.OrderFees[0].FeeAmount);

        // PETR4: 10M/11M * 550 = 500
        Assert.Equal(10_000_000m, result.OrderFees[1].Notional);
        Assert.Equal(500m, result.OrderFees[1].FeeAmount);
    }

    [Fact]
    public void Calculate_WithPriceFactor_ProportionalDistributionAcrossMultipleOrders()
    {
        // Arrange: 2 orders of same symbol with factor
        // Order 1: qty=200_000, price=100, factor=10 → adjusted = 200_000 × (100/10) = 2M
        // Order 2: qty=100_000, price=100, factor=10 → adjusted = 100_000 × (100/10) = 1M
        // Total adjusted = 3M
        // Fee: 3M × 0.00005 = 150
        // Order 1: 2/3 × 150 = 100
        // Order 2: 1/3 × 150 = 50
        var orders = new List<Order>
        {
            CreateOrder(200_000, 100, symbol: "AZUL54"),
            CreateOrder(100_000, 100, symbol: "AZUL54"),
        };
        var brackets = CreateDefaultBrackets();
        var feePerBracket = new Dictionary<decimal, decimal>();
        var priceFactors = new Dictionary<string, decimal> { { "AZUL54", 10m } };

        // Act
        var result = EquityCalculationStrategy.Calculate(orders, brackets, 0, feePerBracket, priceFactors);

        // Assert
        Assert.Equal(3_000_000m, result.TotalNotional);
        Assert.Equal(150m, result.TotalFee);
        Assert.Equal(100m, result.OrderFees[0].FeeAmount);
        Assert.Equal(50m, result.OrderFees[1].FeeAmount);
    }

    [Fact]
    public void Calculate_NullCap_ChargesWithoutCeiling()
    {
        // 50M in bracket 1: 2500 calculated; CAP NULL = no ceiling
        var orders = new List<Order> { CreateOrder(500_000, 100) };
        var brackets = CreateDefaultBrackets();
        brackets[0].Cap = null;

        var result = EquityCalculationStrategy.Calculate(orders, brackets, 0, new Dictionary<decimal, decimal>(), NoPriceFactors);

        Assert.Equal(2500m, result.TotalFee);
        Assert.Null(result.BracketFees.Single().BracketCap);
    }

    [Fact]
    public void Calculate_ZeroCap_ChargesNothingInBracket()
    {
        var orders = new List<Order> { CreateOrder(100_000, 100) }; // 10M
        var brackets = CreateDefaultBrackets();
        brackets[0].Cap = 0;

        var result = EquityCalculationStrategy.Calculate(orders, brackets, 0, new Dictionary<decimal, decimal>(), NoPriceFactors);

        Assert.Equal(0m, result.TotalFee);
    }

    [Fact]
    public void Calculate_FirstBracketStartsAboveZero_VolumeBelowIsNotCharged()
    {
        // Only bracket: 10M+ at 0.5 bps, no cap. Day volume 30M from zero -> only 20M charged
        var brackets = new List<EquityFeeBracket>
        {
            new() { Id = 1, NotionalFrom = 10_000_000, NotionalTo = null, Bps = 0.5m, Cap = null }
        };
        var orders = new List<Order> { CreateOrder(300_000, 100) };

        var result = EquityCalculationStrategy.Calculate(orders, brackets, 0, new Dictionary<decimal, decimal>(), NoPriceFactors);

        Assert.Equal(1000m, result.TotalFee); // 20M * 0.00005
        Assert.Equal(20_000_000m, result.BracketFees.Single().NotionalInBracket);
    }

    [Fact]
    public void Calculate_GapBetweenBrackets_GapVolumeNotCharged_PositionAdvances()
    {
        // [0,50M) 0.5bps, gap [50M,60M), [60M,+) 0.3bps; day volume 100M from zero
        var brackets = new List<EquityFeeBracket>
        {
            new() { Id = 1, NotionalFrom = 0, NotionalTo = 50_000_000, Bps = 0.5m, Cap = null },
            new() { Id = 2, NotionalFrom = 60_000_000, NotionalTo = null, Bps = 0.3m, Cap = null }
        };
        var orders = new List<Order> { CreateOrder(1_000_000, 100) };

        var result = EquityCalculationStrategy.Calculate(orders, brackets, 0, new Dictionary<decimal, decimal>(), NoPriceFactors);

        // 50M * 0.00005 = 2500 ; 40M (60M..100M) * 0.00003 = 1200
        Assert.Equal(3700m, result.TotalFee);
        Assert.Equal(40_000_000m, result.BracketFees.Single(b => b.BracketId == 2).NotionalInBracket);
    }

    [Fact]
    public void Calculate_FeeChargedKeyedByNotionalFrom_CapSurvivesNewBracketVersion()
    {
        // Bracket version changed mid-month (new Id 99) but same range start: CAP consumption carries over
        var brackets = CreateDefaultBrackets();
        brackets[0].Id = 99;
        var orders = new List<Order> { CreateOrder(100_000, 100) }; // 10M -> 500 calculated
        var feePerBracket = new Dictionary<decimal, decimal> { { 0m, 2000m } };

        var result = EquityCalculationStrategy.Calculate(orders, brackets, 20_000_000m, feePerBracket, NoPriceFactors);

        Assert.Equal(100m, result.TotalFee); // CAP 2100 - 2000 already charged
    }

    [Fact]
    public void Calculate_OrderFeesSumToRoundedTotal()
    {
        // Three orders splitting 500.00 unevenly by notional: per-order rounding alone would not close
        var orders = new List<Order>
        {
            CreateOrder(33_333.3333m, 100),
            CreateOrder(33_333.3333m, 100),
            CreateOrder(33_333.3334m, 100)
        };

        var result = EquityCalculationStrategy.Calculate(orders, CreateDefaultBrackets(), 0, new Dictionary<decimal, decimal>(), NoPriceFactors);

        Assert.Equal(Math.Round(result.TotalFee, 2), result.OrderFees.Sum(o => o.FeeAmount));
    }

    [Fact]
    public void Calculate_FractionalCents_ChargedFeeRoundedAndEqualsSumOfOrders()
    {
        // 1.234.567 * 0,00005 = 61,72835 -> 61,73
        var orders = new List<Order>
        {
            CreateOrder(1_000, 1_234.567m),
        };
        var brackets = CreateDefaultBrackets();

        var result = EquityCalculationStrategy.Calculate(orders, brackets, 0, new Dictionary<decimal, decimal>(), NoPriceFactors);

        Assert.Equal(61.73m, result.TotalFee);
        Assert.Equal(61.73m, result.BracketFees.Single().ChargedFee);
        Assert.Equal(61.72835m, result.BracketFees.Single().CalculatedFee);
        Assert.Equal(result.TotalFee, result.OrderFees.Sum(o => o.FeeAmount));
    }

    [Fact]
    public void Calculate_RoundedFeeAboveRemainingCap_ChargesOnlyRemaining()
    {
        // Faixa 1 ja cobrou 2099,99 (CAP 2100): 61,73 calculado -> cobra so 0,01
        var orders = new List<Order> { CreateOrder(1_000, 1_234.567m) };
        var feePerBracket = new Dictionary<decimal, decimal> { [0] = 2_099.99m };

        var result = EquityCalculationStrategy.Calculate(orders, CreateDefaultBrackets(), 40_000_000, feePerBracket, NoPriceFactors);

        Assert.Equal(0.01m, result.TotalFee);
    }
}
