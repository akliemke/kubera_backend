using FinancialEngineCore.Domain.Strategies;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Tests;

public class BmfCalculationStrategyTests
{
    private static BmfFeeConfig CreateDefaultConfig()
    {
        return new BmfFeeConfig
        {
            Id = 1,
            Broker = 1,
            Trader = null,
            ValidFrom = new DateOnly(2024, 1, 1),
            ValidTo = new DateOnly(2099, 12, 31),
            ValorMini = 0.03m,
            ValorCheio = 0.15m,
            ValorOutros = 0.07m,
            DI1Venc1a4 = 0.01m,
            DI1Venc5 = 0.02m,
            DI1Venc6 = 0.04m,
            DI1Venc7 = 0.05m,
            DI1Venc8Plus = 0.10m,
            CapMensal = 25000m,
            IsActive = true
        };
    }

    /// <summary>Asset padrão = raiz do Symbol (3 primeiros caracteres)</summary>
    private static Order CreateBmfOrder(string symbol, decimal qty, string trader = "TRADER1", string? asset = null)
    {
        return new Order
        {
            OrderId = "1",
            CumQty = qty,
            AvgPx = 0, // Not used for BMF
            Symbol = symbol,
            Asset = asset ?? (symbol.Length >= 3 ? symbol[..3] : symbol),
            AssetClass = AssetClasses.Bmf,
            EnteringTrader = trader,
            ExecBroker = "1",
            Entity = "MESA1",
            Instance = "OMS1",
            Side = "BUY",
            TradeDate = new DateOnly(2024, 11, 15) // November 2024
        };
    }

    [Fact]
    public void Calculate_MiniContracts_NoCapHit()
    {
        // Arrange: 500 mini contracts at 0.03 = 15.00
        var orders = new List<Order> { CreateBmfOrder("WINZ24", 500) };
        var config = CreateDefaultConfig();

        // Act
        var result = BmfCalculationStrategy.Calculate(orders, config, 0, orders[0].TradeDate);

        // Assert
        Assert.Equal(500m, result.TotalQty);
        Assert.Equal(15.00m, result.TotalFee);
    }

    [Fact]
    public void Calculate_CheioContracts_NoCapHit()
    {
        // Arrange: 100 cheio contracts at 0.15 = 15.00
        var orders = new List<Order> { CreateBmfOrder("INDZ24", 100) };
        var config = CreateDefaultConfig();

        // Act
        var result = BmfCalculationStrategy.Calculate(orders, config, 0, orders[0].TradeDate);

        // Assert
        Assert.Equal(100m, result.TotalQty);
        Assert.Equal(15.00m, result.TotalFee);
    }

    [Fact]
    public void Calculate_MixedContracts_NoCapHit()
    {
        // Arrange: 500 mini + 100 cheio
        // Mini: 500 * 0.03 = 15.00
        // Cheio: 100 * 0.15 = 15.00
        // Total: 30.00
        var orders = new List<Order>
        {
            CreateBmfOrder("WINZ24", 500),
            CreateBmfOrder("INDZ24", 100),
        };
        var config = CreateDefaultConfig();

        // Act
        var result = BmfCalculationStrategy.Calculate(orders, config, 0, orders[0].TradeDate);

        // Assert
        Assert.Equal(600m, result.TotalQty);
        Assert.Equal(30.00m, result.TotalFee);
    }

    [Fact]
    public void Calculate_DI1_FirstToFourthMaturity()
    {
        // Arrange: DI1F25 (Jan/25) traded in Nov/24 = 2 months = 2nd maturity
        // 200 contracts at 0.01 = 2.00
        var tradeDate = new DateOnly(2024, 11, 15);
        var orders = new List<Order> { CreateBmfOrder("DI1F25", 200) };
        orders[0].TradeDate = tradeDate;
        var config = CreateDefaultConfig();

        // Act
        var result = BmfCalculationStrategy.Calculate(orders, config, 0, tradeDate);

        // Assert
        Assert.Equal(200m, result.TotalQty);
        Assert.Equal(2.00m, result.TotalFee);
    }

    [Fact]
    public void Calculate_DI1_FifthMaturity()
    {
        // Arrange: DI1K25 (May/25) traded in Nov/24 = 6 months -> but K is May (5th month)
        // Actually: Nov/24 to May/25 = 6 months = 6th maturity
        // Let's use a date where we get exactly 5th maturity
        // Trade in Dec/24, contract Apr/25 (J) = 4 months = 4th maturity
        // Trade in Nov/24, contract Apr/25 (J) = 5 months = 5th maturity
        var tradeDate = new DateOnly(2024, 11, 15);
        var orders = new List<Order> { CreateBmfOrder("DI1J25", 100) }; // April 2025
        orders[0].TradeDate = tradeDate;
        var config = CreateDefaultConfig();

        // Act
        var result = BmfCalculationStrategy.Calculate(orders, config, 0, tradeDate);

        // Assert: April is J, Nov to Apr = 5 months
        Assert.Equal(100m, result.TotalQty);
        Assert.Equal(2.00m, result.TotalFee); // 5th maturity = 0.02
    }

    [Fact]
    public void Calculate_DI1_SixthMaturity()
    {
        // Arrange: Trade in Nov/24, contract May/25 (K) = 6 months = 6th maturity
        var tradeDate = new DateOnly(2024, 11, 15);
        var orders = new List<Order> { CreateBmfOrder("DI1K25", 100) }; // May 2025
        orders[0].TradeDate = tradeDate;
        var config = CreateDefaultConfig();

        // Act
        var result = BmfCalculationStrategy.Calculate(orders, config, 0, tradeDate);

        // Assert
        Assert.Equal(100m, result.TotalQty);
        Assert.Equal(4.00m, result.TotalFee); // 6th maturity = 0.04
    }

    [Fact]
    public void Calculate_DI1_SeventhMaturity()
    {
        // Arrange: Trade in Nov/24, contract Jun/25 (M) = 7 months = 7th maturity
        var tradeDate = new DateOnly(2024, 11, 15);
        var orders = new List<Order> { CreateBmfOrder("DI1M25", 100) }; // June 2025
        orders[0].TradeDate = tradeDate;
        var config = CreateDefaultConfig();

        // Act
        var result = BmfCalculationStrategy.Calculate(orders, config, 0, tradeDate);

        // Assert
        Assert.Equal(100m, result.TotalQty);
        Assert.Equal(5.00m, result.TotalFee); // 7th maturity = 0.05
    }

    [Fact]
    public void Calculate_DI1_EighthPlusMaturity()
    {
        // Arrange: Trade in Nov/24, contract Oct/25 (V) = 11 months = 11th maturity
        var tradeDate = new DateOnly(2024, 11, 15);
        var orders = new List<Order> { CreateBmfOrder("DI1V25", 100) }; // October 2025
        orders[0].TradeDate = tradeDate;
        var config = CreateDefaultConfig();

        // Act
        var result = BmfCalculationStrategy.Calculate(orders, config, 0, tradeDate);

        // Assert
        Assert.Equal(100m, result.TotalQty);
        Assert.Equal(10.00m, result.TotalFee); // 8th+ maturity = 0.10
    }

    [Fact]
    public void Calculate_MixedBmfTypes()
    {
        // Arrange: Mini + Cheio + DI1
        // Mini: 500 * 0.03 = 15.00
        // Cheio: 100 * 0.15 = 15.00
        // DI1 (2nd mat): 200 * 0.01 = 2.00
        // DI1 (6th mat): 50 * 0.04 = 2.00
        // Total: 34.00
        var tradeDate = new DateOnly(2024, 11, 15);
        var orders = new List<Order>
        {
            CreateBmfOrder("WINZ24", 500),
            CreateBmfOrder("INDZ24", 100),
            CreateBmfOrder("DI1F25", 200), // 2nd maturity
            CreateBmfOrder("DI1K25", 50),  // 6th maturity
        };
        foreach (var o in orders) o.TradeDate = tradeDate;
        var config = CreateDefaultConfig();

        // Act
        var result = BmfCalculationStrategy.Calculate(orders, config, 0, tradeDate);

        // Assert
        Assert.Equal(850m, result.TotalQty);
        Assert.Equal(34.00m, result.TotalFee);
    }

    [Fact]
    public void Calculate_HitsMonthlyCap()
    {
        // Arrange: Massive volume that would exceed cap
        // 1,000,000 mini contracts * 0.03 = 30,000
        // Cap is 25,000
        var orders = new List<Order> { CreateBmfOrder("WINZ24", 1_000_000) };
        var config = CreateDefaultConfig();

        // Act
        var result = BmfCalculationStrategy.Calculate(orders, config, 0, orders[0].TradeDate);

        // Assert
        Assert.Equal(1_000_000m, result.TotalQty);
        Assert.Equal(25000m, result.TotalFee); // Capped at 25,000
    }

    [Fact]
    public void Calculate_PartialCapRemaining()
    {
        // Arrange: Already charged 20,000 this month, 5,000 cap remaining
        // 500 mini * 0.03 = 15.00, but only 5,000 remaining
        var orders = new List<Order> { CreateBmfOrder("WINZ24", 500) };
        var config = CreateDefaultConfig();

        // Act - simulate massive previous charges
        // Actually let's make it charge more: 200,000 contracts = 6,000
        var orders2 = new List<Order> { CreateBmfOrder("WINZ24", 200_000) }; // 6,000 fee
        var result = BmfCalculationStrategy.Calculate(orders2, config, 20000m, orders2[0].TradeDate);

        // Assert: 6,000 calculated but only 5,000 cap remaining
        Assert.Equal(200_000m, result.TotalQty);
        Assert.Equal(5000m, result.TotalFee);
    }

    [Fact]
    public void Calculate_CapAlreadyExhausted()
    {
        // Arrange: Already charged 25,000 (full cap)
        var orders = new List<Order> { CreateBmfOrder("WINZ24", 500) };
        var config = CreateDefaultConfig();

        // Act
        var result = BmfCalculationStrategy.Calculate(orders, config, 25000m, orders[0].TradeDate);

        // Assert: No more charges
        Assert.Equal(500m, result.TotalQty);
        Assert.Equal(0m, result.TotalFee);
    }

    [Fact]
    public void Calculate_MultipleOrders_FeeDistributedProportionally()
    {
        // Arrange: 2 orders of 500 mini each = 1000 total
        // Fee: 1000 * 0.03 = 30.00
        // Each order gets 15.00
        var orders = new List<Order>
        {
            CreateBmfOrder("WINZ24", 500),
            CreateBmfOrder("WINZ24", 500),
        };
        var config = CreateDefaultConfig();

        // Act
        var result = BmfCalculationStrategy.Calculate(orders, config, 0, orders[0].TradeDate);

        // Assert
        Assert.Equal(1000m, result.TotalQty);
        Assert.Equal(30.00m, result.TotalFee);
        Assert.Equal(2, result.OrderFees.Count);
        Assert.Equal(15.00m, result.OrderFees[0].FeeAmount);
        Assert.Equal(15.00m, result.OrderFees[1].FeeAmount);
    }

    [Fact]
    public void Calculate_WDO_MiniContract()
    {
        // Arrange: WDO is also mini
        var orders = new List<Order> { CreateBmfOrder("WDOZ24", 1000) };
        var config = CreateDefaultConfig();

        // Act
        var result = BmfCalculationStrategy.Calculate(orders, config, 0, orders[0].TradeDate);

        // Assert: 1000 * 0.03 = 30.00
        Assert.Equal(1000m, result.TotalQty);
        Assert.Equal(30.00m, result.TotalFee);
    }

    [Fact]
    public void Calculate_DOL_CheioContract()
    {
        // Arrange: DOL is cheio
        var orders = new List<Order> { CreateBmfOrder("DOLZ24", 100) };
        var config = CreateDefaultConfig();

        // Act
        var result = BmfCalculationStrategy.Calculate(orders, config, 0, orders[0].TradeDate);

        // Assert: 100 * 0.15 = 15.00
        Assert.Equal(100m, result.TotalQty);
        Assert.Equal(15.00m, result.TotalFee);
    }

    [Fact]
    public void Calculate_NullCapMensal_ChargesWithoutCeiling()
    {
        var config = CreateDefaultConfig();
        config.CapMensal = null;
        var orders = new List<Order> { CreateBmfOrder("INDZ24", 1_000_000) }; // 150,000

        var result = BmfCalculationStrategy.Calculate(orders, config, 100_000m, orders[0].TradeDate);

        Assert.Equal(150_000m, result.TotalFee);
        Assert.Equal(250_000m, result.AccumulatedAfter);
    }

    [Fact]
    public void Calculate_OtherBmfContracts_FixedFeePerContract()
    {
        // Not mini, cheio nor DI1: ValorOutros (0.07) per contract
        var orders = new List<Order>
        {
            CreateBmfOrder("WINZ24", 100), // 3.00
            CreateBmfOrder("BGIV25", 50),  // 3.50
            CreateBmfOrder("CCMK26", 10)   // 0.70
        };

        var result = BmfCalculationStrategy.Calculate(orders, CreateDefaultConfig(), 0, orders[0].TradeDate);

        Assert.Equal(7.20m, result.TotalFee);
        Assert.Empty(result.UnknownSymbols);
    }

    [Fact]
    public void Calculate_ContractTypeComesFromAsset_NotSymbol()
    {
        // Synthetic legs: the view returns Asset = leg (WDO mini / DOL cheio) with the synthetic Symbol
        var orders = new List<Order>
        {
            CreateBmfOrder("BTGDOLZ24", 10, asset: "WDO"), // 10 * 0.03 = 0.30
            CreateBmfOrder("BTGDOLZ24", 2, asset: "DOL")   //  2 * 0.15 = 0.30
        };

        var result = BmfCalculationStrategy.Calculate(orders, CreateDefaultConfig(), 0, orders[0].TradeDate);

        Assert.Equal(0.60m, result.TotalFee);
        Assert.Equal(12m, result.TotalQty);
    }

    [Fact]
    public void Calculate_WithoutAsset_UsesSymbolRoot()
    {
        var order = CreateBmfOrder("WINZ24", 100);
        order.Asset = null;

        var result = BmfCalculationStrategy.Calculate([order], CreateDefaultConfig(), 0, order.TradeDate);

        Assert.Equal(3.00m, result.TotalFee);
    }

    [Fact]
    public void Calculate_WithoutContractRoot_ZeroFeeAndReported()
    {
        var orders = new List<Order>
        {
            CreateBmfOrder("WINZ24", 100),
            CreateBmfOrder("", 50, asset: "")
        };

        var result = BmfCalculationStrategy.Calculate(orders, CreateDefaultConfig(), 0, orders[0].TradeDate);

        Assert.Equal(3.00m, result.TotalFee); // only WIN: 100 * 0.03
        Assert.Single(result.UnknownSymbols);
    }

    [Fact]
    public void Calculate_OrderFeesSumToRoundedTotal()
    {
        // 0.03 + 0.03 + 0.15 = 0.21 split by qty (1/3 each = 0.07): sum must close at 0.21
        var orders = new List<Order>
        {
            CreateBmfOrder("WDOZ24", 1),
            CreateBmfOrder("WDOZ24", 1),
            CreateBmfOrder("INDZ24", 1)
        };

        var result = BmfCalculationStrategy.Calculate(orders, CreateDefaultConfig(), 0, orders[0].TradeDate);

        Assert.Equal(0.21m, result.TotalFee);
        Assert.Equal(0.21m, result.OrderFees.Sum(o => o.FeeAmount));
    }

    [Fact]
    public void Calculate_FractionalCents_TotalFeeRoundedAndEqualsSumOfOrders()
    {
        // 10 mini * 0,0333 = 0,333 -> 0,33
        var config = CreateDefaultConfig();
        config.ValorMini = 0.0333m;
        var orders = new List<Order> { CreateBmfOrder("WINZ24", 7), CreateBmfOrder("WDOZ24", 3) };

        var result = BmfCalculationStrategy.Calculate(orders, config, 0, orders[0].TradeDate);

        Assert.Equal(0.33m, result.TotalFee);
        Assert.Equal(result.TotalFee, result.OrderFees.Sum(o => o.FeeAmount));
    }
}
