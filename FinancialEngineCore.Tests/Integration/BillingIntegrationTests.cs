using Dapper;
using FinancialEngineCore.Domain.Services;
using FinancialEngineCore.Domain.Strategies;
using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace FinancialEngineCore.Tests.Integration;

[Collection("Database")]
public class BillingIntegrationTests : IAsyncLifetime
{
    private readonly DatabaseFixture _db;
    private readonly DateOnly _tradeDate = new(2024, 11, 15);
    private const int BrokerId = 1;
    private const int ProcessBillingId = 1;

    public BillingIntegrationTests(DatabaseFixture db)
    {
        _db = db;
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => _db.CleanupAsync();

    // ==========================================
    // Test 1: EQUITY progressive brackets
    // ==========================================
    [Fact]
    public async Task EquityDay_ProgressiveBrackets_PersistsCorrectly()
    {
        // Arrange: Broker 1, 4 brackets, JOAO accumulated 40M, operates 30M today
        await SeedBroker();
        await SeedDefaultEquityBrackets();

        // Previous days: JOAO accumulated 40M
        await SeedPreviousDailyBilling("JOAO", "EQUITY", totalNotional: 40_000_000m, totalFee: 2000m,
            tradeDate: new DateOnly(2024, 11, 10));
        await SeedPreviousBracketFeeTracking(bracketId: 1, chargedFee: 2000m, tradeDate: new DateOnly(2024, 11, 10));

        // Today: JOAO operates 30M (total will be 70M, crossing into bracket 2)
        await SeedOrder(1, 300_000, 100, "JOAO", "PETR4", "EQUITY");

        var service = CreateService();

        // Act
        await service.ProcessBrokerAsync(BrokerId, _tradeDate, ProcessBillingId, CancellationToken.None);

        // Assert
        var billing = await GetDailyBilling("JOAO", "EQUITY");
        Assert.NotNull(billing);
        Assert.Equal(30_000_000m, billing.TotalNotional);
        Assert.Equal(40_000_000m, billing.AccumulatedBefore);
        Assert.Equal(70_000_000m, billing.AccumulatedAfter);

        // Fee: Bracket1 remaining = 10M * 0.00005 = 500, but cap=2100, already charged 2000, so 100
        //       Bracket2 = 20M * 0.000045 = 900
        //       Total = 1000
        Assert.Equal(1000m, billing.TotalFee);

        // Verify order_billing exists
        var orderBillings = await GetOrderBillings(billing.Id);
        Assert.Single(orderBillings);
        Assert.Equal(1000m, orderBillings[0].FeeAmount);

        // Verify bracket_fee_tracking
        var bracketFees = await GetBracketFeeTrackings(billing.Id);
        Assert.Equal(2, bracketFees.Count);
    }

    // ==========================================
    // Test 2: BMF mixed contracts with CAP
    // ==========================================
    [Fact]
    public async Task BmfDay_MixedContracts_CapEnforced()
    {
        // Arrange: BMF config with cap=25000, trader already charged 24950
        await SeedBroker();
        await SeedDefaultBmfConfig();

        // Previous: already charged 24950 in the month
        await SeedPreviousDailyBilling("TRADER1", "BMF", totalNotional: 0m, totalFee: 24950m,
            tradeDate: new DateOnly(2024, 11, 10), totalQty: 50000);

        // Today: 500 Mini(WIN) + 100 Cheio(IND)
        // Mini: 500 * 0.03 = 15, Cheio: 100 * 0.15 = 15, Total = 30
        // But CAP remaining = 25000 - 24950 = 50
        await SeedOrder(1, 500, 0, "TRADER1", "WINZ24", "BMF", "WINZ24");
        await SeedOrder(2, 100, 0, "TRADER1", "INDZ24", "BMF", "INDZ24");

        var service = CreateService();

        // Act
        await service.ProcessBrokerAsync(BrokerId, _tradeDate, ProcessBillingId, CancellationToken.None);

        // Assert
        var billing = await GetDailyBilling("TRADER1", "BMF");
        Assert.NotNull(billing);
        Assert.Equal(600m, billing.TotalQty); // 500 + 100
        Assert.Equal(30m, billing.TotalFee); // 30 < 50 remaining, so no cap hit

        var orderBillings = await GetOrderBillings(billing.Id);
        Assert.Equal(2, orderBillings.Count);
    }

    // ==========================================
    // Test 3: Mixed asset classes
    // ==========================================
    [Fact]
    public async Task MixedAssetClasses_CreatesSeparateBillingRecords()
    {
        // Arrange: same trader, EQUITY + BMF orders
        await SeedBroker();
        await SeedDefaultEquityBrackets();
        await SeedDefaultBmfConfig();

        await SeedOrder(1, 100_000, 100, "TRADER1", "PETR4", "EQUITY"); // 10M notional
        await SeedOrder(2, 200, 0, "TRADER1", "WINZ24", "BMF", "WINZ24"); // 200 Mini

        var service = CreateService();

        // Act
        await service.ProcessBrokerAsync(BrokerId, _tradeDate, ProcessBillingId, CancellationToken.None);

        // Assert: 2 separate daily_billing records
        var equityBilling = await GetDailyBilling("TRADER1", "EQUITY");
        var bmfBilling = await GetDailyBilling("TRADER1", "BMF");

        Assert.NotNull(equityBilling);
        Assert.NotNull(bmfBilling);

        // EQUITY: 10M * 0.00005 = 500
        Assert.Equal(10_000_000m, equityBilling.TotalNotional);
        Assert.Equal(500m, equityBilling.TotalFee);

        // BMF: 200 * 0.03 = 6
        Assert.Equal(200m, bmfBilling.TotalQty);
        Assert.Equal(6m, bmfBilling.TotalFee);
    }

    // ==========================================
    // Test 4: Special trader brackets
    // ==========================================
    [Fact]
    public async Task SpecialTrader_UsesSpecialBrackets()
    {
        // Arrange: default brackets + special brackets for VIP
        await SeedBroker();
        await SeedDefaultEquityBrackets();

        // Special bracket for VIP: single bracket, 0.30 bps, high cap
        await InsertEquityBracket(BrokerId, "TRADER_VIP", 0, null, 0.30m, 100000m);

        // Both trade 10M
        await SeedOrder(1, 100_000, 100, "TRADER_NORMAL", "PETR4", "EQUITY");
        await SeedOrder(2, 100_000, 100, "TRADER_VIP", "VALE3", "EQUITY");

        var service = CreateService();

        // Act
        await service.ProcessBrokerAsync(BrokerId, _tradeDate, ProcessBillingId, CancellationToken.None);

        // Assert
        var normalBilling = await GetDailyBilling("TRADER_NORMAL", "EQUITY");
        var vipBilling = await GetDailyBilling("TRADER_VIP", "EQUITY");

        Assert.NotNull(normalBilling);
        Assert.NotNull(vipBilling);

        // NORMAL: 10M * 0.00005 (0.5 bps) = 500
        Assert.Equal(500m, normalBilling.TotalFee);

        // VIP: 10M * 0.00003 (0.30 bps) = 300
        Assert.Equal(300m, vipBilling.TotalFee);
    }

    // ==========================================
    // Test 5: Entity billing
    // ==========================================
    [Fact]
    public async Task EntityBilling_GroupsOrdersByEntity()
    {
        // Arrange: MESA_A configured for entity billing
        await SeedBroker();
        await SeedDefaultEquityBrackets();
        await SeedEntityBillingConfig("MESA_A");

        // JOAO and MARIA both from MESA_A, PEDRO from MESA_B
        await SeedOrder(1, 100_000, 100, "JOAO", "PETR4", "EQUITY", entity: "MESA_A"); // 10M
        await SeedOrder(2, 50_000, 100, "MARIA", "VALE3", "EQUITY", entity: "MESA_A"); // 5M
        await SeedOrder(3, 20_000, 100, "PEDRO", "ITUB4", "EQUITY", entity: "MESA_B"); // 2M

        var service = CreateService();

        // Act
        await service.ProcessBrokerAsync(BrokerId, _tradeDate, ProcessBillingId, CancellationToken.None);

        // Assert: MESA_A consolidated (15M), PEDRO separate (2M)
        var mesaBilling = await GetDailyBilling("MESA_A", "EQUITY");
        var pedroBilling = await GetDailyBilling("PEDRO", "EQUITY");

        Assert.NotNull(mesaBilling);
        Assert.NotNull(pedroBilling);

        // MESA_A: 15M notional (10M + 5M), fee = 15M * 0.00005 = 750
        Assert.Equal(15_000_000m, mesaBilling.TotalNotional);
        Assert.Equal(750m, mesaBilling.TotalFee);

        // PEDRO: 2M notional, fee = 2M * 0.00005 = 100
        Assert.Equal(2_000_000m, pedroBilling.TotalNotional);
        Assert.Equal(100m, pedroBilling.TotalFee);

        // MESA_A should have 2 order billings (JOAO + MARIA)
        var mesaOrderBillings = await GetOrderBillings(mesaBilling.Id);
        Assert.Equal(2, mesaOrderBillings.Count);
    }

    // ==========================================
    // Test 6: Price modification factor
    // ==========================================
    [Fact]
    public async Task PriceFactor_AdjustsEquityNotional()
    {
        // Arrange: AZUL54 with factor=10000
        await SeedBroker();
        await SeedDefaultEquityBrackets();
        await SeedPriceModificationFactor("AZUL54", 10000m);

        // AZUL54: qty=500, price=350000, notional = 500 * (350000/10000) = 17500
        // PETR4: qty=1000, price=30, notional = 30000
        // Total notional = 47500
        await SeedOrder(1, 500, 350_000, "TRADER1", "AZUL54", "EQUITY");
        await SeedOrder(2, 1000, 30, "TRADER1", "PETR4", "EQUITY");

        var service = CreateService();

        // Act
        await service.ProcessBrokerAsync(BrokerId, _tradeDate, ProcessBillingId, CancellationToken.None);

        // Assert
        var billing = await GetDailyBilling("TRADER1", "EQUITY");
        Assert.NotNull(billing);

        // Total notional = 17500 + 30000 = 47500
        Assert.Equal(47_500m, billing.TotalNotional);

        // Fee: 47500 * 0.00005 = 2.375
        Assert.Equal(2.375m, billing.TotalFee);
    }

    // ==========================================
    // Test 7: Reprocessing (delete + recreate)
    // ==========================================
    [Fact]
    public async Task Reprocessamento_DeletesAndRecreates()
    {
        // Arrange: existing billing for today + orders
        await SeedBroker();
        await SeedDefaultEquityBrackets();

        // Seed existing billing (as if already processed today)
        await SeedPreviousDailyBilling("TRADER1", "EQUITY", totalNotional: 5_000_000m, totalFee: 999m,
            tradeDate: _tradeDate); // Same date!

        // Now seed the actual orders for reprocessing
        await SeedOrder(1, 100_000, 100, "TRADER1", "PETR4", "EQUITY"); // 10M

        var service = CreateService();

        // Act: reprocess should delete old billing and create new
        await service.ProcessBrokerAsync(BrokerId, _tradeDate, ProcessBillingId, CancellationToken.None);

        // Assert: only 1 billing record for today (old one deleted by CASCADE)
        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM financial_engine.daily_billing WHERE Broker = @BrokerId AND TradeDate = @TradeDate",
            new { BrokerId, TradeDate = _tradeDate });
        Assert.Equal(1, count);

        var billing = await GetDailyBilling("TRADER1", "EQUITY");
        Assert.NotNull(billing);
        Assert.Equal(10_000_000m, billing.TotalNotional);
        Assert.Equal(500m, billing.TotalFee); // Recalculated, not 999
    }

    // ==========================================
    // Helper: Create service with real repositories
    // ==========================================
    private BillingProcessingService CreateService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _db.ConnectionString
            }).Build();

        var dbFactory = new DbConnectionFactory(config);

        var orderRepo = new OrdersRepository(dbFactory);
        var dailyBillingRepo = new DailyBillingRepository(dbFactory);
        var orderBillingRepo = new OrderBillingRepository(dbFactory);
        var bracketFeeTrackingRepo = new BracketFeeTrackingRepository(dbFactory);
        var entityBillingConfigRepo = new EntityBillingConfigRepository(dbFactory);
        var bracketRepo = new EquityFeeBracketRepository(dbFactory);
        var bmfConfigRepo = new BmfFeeConfigRepository(dbFactory);
        var priceFactorRepo = new PriceModificationFactorRepository(dbFactory);

        var equityStrategy = new EquityCalculationStrategy();
        var bmfStrategy = new BmfCalculationStrategy(NullLogger<BmfCalculationStrategy>.Instance);
        var registry = new CalculationStrategyRegistry(new ICalculationStrategy[] { equityStrategy, bmfStrategy });

        return new BillingProcessingService(
            orderRepo, dailyBillingRepo, orderBillingRepo, bracketFeeTrackingRepo,
            entityBillingConfigRepo, bracketRepo, bmfConfigRepo, priceFactorRepo,
            dbFactory, registry, NullLogger<BillingProcessingService>.Instance);
    }

    // ==========================================
    // Seed helpers
    // ==========================================
    private async Task SeedBroker()
    {
        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "INSERT INTO management.brokers (Code, Name) VALUES (@Code, @Name) ON CONFLICT DO NOTHING",
            new { Code = BrokerId, Name = "BTG Test" });
    }

    private async Task SeedDefaultEquityBrackets()
    {
        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        var brackets = new[]
        {
            new { Id = 1L, Broker = BrokerId, NotionalFrom = 0m, NotionalTo = (decimal?)50_000_000m, Bps = 0.5m, Cap = 2100m },
            new { Id = 2L, Broker = BrokerId, NotionalFrom = 50_000_000m, NotionalTo = (decimal?)150_000_000m, Bps = 0.45m, Cap = 5200m },
            new { Id = 3L, Broker = BrokerId, NotionalFrom = 150_000_000m, NotionalTo = (decimal?)500_000_000m, Bps = 0.35m, Cap = 15000m },
            new { Id = 4L, Broker = BrokerId, NotionalFrom = 500_000_000m, NotionalTo = (decimal?)null, Bps = 0.30m, Cap = 31000m },
        };

        foreach (var b in brackets)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO financial_engine.equity_fee_brackets
                    (Broker, Trader, ValidFrom, ValidTo, NotionalFrom, NotionalTo, Bps, Cap, IsActive)
                VALUES (@Broker, NULL, '2024-01-01', '2099-12-31', @NotionalFrom, @NotionalTo, @Bps, @Cap, TRUE)",
                b);
        }
    }

    private async Task InsertEquityBracket(int broker, string? trader, decimal notionalFrom, decimal? notionalTo, decimal bps, decimal cap)
    {
        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO financial_engine.equity_fee_brackets
                (Broker, Trader, ValidFrom, ValidTo, NotionalFrom, NotionalTo, Bps, Cap, IsActive)
            VALUES (@Broker, @Trader, '2024-01-01', '2099-12-31', @NotionalFrom, @NotionalTo, @Bps, @Cap, TRUE)",
            new { Broker = broker, Trader = trader, NotionalFrom = notionalFrom, NotionalTo = notionalTo, Bps = bps, Cap = cap });
    }

    private async Task SeedDefaultBmfConfig()
    {
        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO financial_engine.bmf_fee_configs
                (Broker, Trader, ValidFrom, ValidTo, ValorMini, ValorCheio,
                 DI1Venc1a4, DI1Venc5, DI1Venc6, DI1Venc7, DI1Venc8Plus, CapMensal, IsActive)
            VALUES (@Broker, NULL, '2024-01-01', '2099-12-31', 0.03, 0.15,
                    0.01, 0.02, 0.04, 0.05, 0.10, 25000, TRUE)",
            new { Broker = BrokerId });
    }

    private async Task SeedOrder(long id, decimal qty, decimal price, string trader, string symbol,
        string assetClass, string? exchangeSymbol = null, string entity = "MESA1")
    {
        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO history.orders
                (Id, CumQty, AvgPx, Symbol, ExchangeSymbol, AssetClass, EnteringTrader,
                 Entity, TradeDate, EnteringFirm, OmsInstance, Side, businessDate)
            VALUES (@Id, @CumQty, @AvgPx, @Symbol, @ExchangeSymbol, @AssetClass, @EnteringTrader,
                    @Entity, @TradeDate, @EnteringFirm, 'OMS1', 'BUY', @BusinessDate)",
            new
            {
                Id = id,
                CumQty = qty,
                AvgPx = price,
                Symbol = symbol,
                ExchangeSymbol = exchangeSymbol ?? symbol,
                AssetClass = assetClass,
                EnteringTrader = trader,
                Entity = entity,
                TradeDate = _tradeDate,
                EnteringFirm = BrokerId.ToString(),
                BusinessDate = _tradeDate.ToString("yyyy-MM-dd")
            });
    }

    private async Task SeedPreviousDailyBilling(string trader, string assetClass,
        decimal totalNotional, decimal totalFee, DateOnly tradeDate, decimal totalQty = 0)
    {
        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO financial_engine.daily_billing
                (Broker, Trader, TradeDate, AssetClass, TotalNotional, TotalQty, TotalFee,
                 AccumulatedBefore, AccumulatedAfter, ProcessBillingId)
            VALUES (@Broker, @Trader, @TradeDate, @AssetClass, @TotalNotional, @TotalQty, @TotalFee,
                    0, @TotalNotional, @ProcessBillingId)",
            new
            {
                Broker = BrokerId,
                Trader = trader,
                TradeDate = tradeDate,
                AssetClass = assetClass,
                TotalNotional = totalNotional,
                TotalQty = totalQty,
                TotalFee = totalFee,
                ProcessBillingId
            });
    }

    private async Task SeedPreviousBracketFeeTracking(long bracketId, decimal chargedFee, DateOnly tradeDate)
    {
        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();

        // Get the daily_billing Id for this date
        var dailyBillingId = await conn.ExecuteScalarAsync<long>(
            "SELECT Id FROM financial_engine.daily_billing WHERE Broker = @Broker AND TradeDate = @TradeDate LIMIT 1",
            new { Broker = BrokerId, TradeDate = tradeDate });

        await conn.ExecuteAsync(@"
            INSERT INTO financial_engine.bracket_fee_tracking
                (DailyBillingId, BracketId, NotionalInBracket, CalculatedFee, ChargedFee, AccumulatedFeeBefore, BracketCap)
            VALUES (@DailyBillingId, @BracketId, 0, @ChargedFee, @ChargedFee, 0, 2100)",
            new { DailyBillingId = dailyBillingId, BracketId = bracketId, ChargedFee = chargedFee });
    }

    private async Task SeedEntityBillingConfig(string entity)
    {
        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO financial_engine.entity_billing_configs
                (Broker, Entity, ValidFrom, ValidTo, IsActive)
            VALUES (@Broker, @Entity, '2024-01-01', '2099-12-31', TRUE)",
            new { Broker = BrokerId, Entity = entity });
    }

    private async Task SeedPriceModificationFactor(string symbol, decimal factor)
    {
        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO financial_engine.price_modification_factors
                (Symbol, Factor, ValidFrom, ValidTo, IsActive)
            VALUES (@Symbol, @Factor, '2024-01-01', '2099-12-31', TRUE)",
            new { Symbol = symbol, Factor = factor });
    }

    // ==========================================
    // Assert helpers
    // ==========================================
    private async Task<DailyBilling?> GetDailyBilling(string trader, string assetClass)
    {
        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        return await conn.QueryFirstOrDefaultAsync<DailyBilling>(
            @"SELECT * FROM financial_engine.daily_billing
              WHERE Broker = @Broker AND Trader = @Trader AND AssetClass = @AssetClass AND TradeDate = @TradeDate",
            new { Broker = BrokerId, Trader = trader, AssetClass = assetClass, TradeDate = _tradeDate });
    }

    private async Task<List<OrderBilling>> GetOrderBillings(long dailyBillingId)
    {
        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        var result = await conn.QueryAsync<OrderBilling>(
            "SELECT * FROM financial_engine.order_billing WHERE DailyBillingId = @Id",
            new { Id = dailyBillingId });
        return result.ToList();
    }

    private async Task<List<BracketFeeTracking>> GetBracketFeeTrackings(long dailyBillingId)
    {
        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        var result = await conn.QueryAsync<BracketFeeTracking>(
            "SELECT * FROM financial_engine.bracket_fee_tracking WHERE DailyBillingId = @Id",
            new { Id = dailyBillingId });
        return result.ToList();
    }
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
}
