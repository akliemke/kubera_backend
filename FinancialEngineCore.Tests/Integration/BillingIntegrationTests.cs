using Dapper;
using FinancialEngineCore.Domain;
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
        await SeedOrder(1, 300_000, 100, "JOAO", "PETR4", "PETR");

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
        await SeedOrder(1, 500, 0, "TRADER1", "WINZ24", "WIN");
        await SeedOrder(2, 100, 0, "TRADER1", "INDZ24", "IND");

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

        await SeedOrder(1, 100_000, 100, "TRADER1", "PETR4", "PETR"); // 10M notional
        await SeedOrder(2, 200, 0, "TRADER1", "WINZ24", "WIN"); // 200 Mini

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
        await SeedOrder(1, 100_000, 100, "TRADER_NORMAL", "PETR4", "PETR");
        await SeedOrder(2, 100_000, 100, "TRADER_VIP", "VALE3", "VALE");

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
        await SeedOrder(1, 100_000, 100, "JOAO", "PETR4", "PETR", entity: "MESA_A"); // 10M
        await SeedOrder(2, 50_000, 100, "MARIA", "VALE3", "VALE", entity: "MESA_A"); // 5M
        await SeedOrder(3, 20_000, 100, "PEDRO", "ITUB4", "ITUB", entity: "MESA_B"); // 2M

        var service = CreateService();

        // Act
        await service.ProcessBrokerAsync(BrokerId, _tradeDate, ProcessBillingId, CancellationToken.None);

        // Assert: MESA_A consolidated (15M), PEDRO separate (2M)
        var mesaBilling = await GetDailyBilling("MESA_A", "EQUITY", keyType: BillingKeyTypes.Entity);
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
        await SeedOrder(1, 500, 350_000, "TRADER1", "AZUL54", "AZUL");
        await SeedOrder(2, 1000, 30, "TRADER1", "PETR4", "PETR");

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
        await SeedOrder(1, 100_000, 100, "TRADER1", "PETR4", "PETR"); // 10M

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
    // Test 8: GTC/GTD uses daily trades, DAY uses CumQty/AvgPx
    // ==========================================
    [Fact]
    public async Task GtcGtd_UsesTradesOfDay_DayUsesCumQty()
    {
        // Arrange: same trader, 1 GTC order + 1 DAY order
        await SeedBroker();
        await SeedDefaultEquityBrackets();

        // GTC order: CumQty=1000 (accumulated over multiple days), AvgPx=50 (stale VWAP)
        // But today only 200 shares were filled at price 55
        await SeedOrder(1, 1000, 50, "TRADER1", "PETR4", "PETR", timeInForce: "GTC");
        await SeedTrade(orderId: 1, lastQty: 200, lastPx: 55, "TRADER1", "PETR4", "PETR");

        // DAY order: CumQty=300, AvgPx=100 (all fills are today by definition)
        await SeedOrder(2, 300, 100, "TRADER1", "VALE3", "VALE");

        var service = CreateService();

        // Act
        await service.ProcessBrokerAsync(BrokerId, _tradeDate, ProcessBillingId, CancellationToken.None);

        // Assert
        var billing = await GetDailyBilling("TRADER1", "EQUITY");
        Assert.NotNull(billing);

        // GTC notional from trades: 200 * 55 = 11,000
        // DAY notional from order: 300 * 100 = 30,000
        // Total: 41,000
        Assert.Equal(41_000m, billing.TotalNotional);

        // Fee: 41,000 * 0.00005 (bracket 1) = 2.05
        Assert.Equal(2.05m, billing.TotalFee);

        // 2 order billings (1 GTC + 1 DAY)
        var orderBillings = await GetOrderBillings(billing.Id);
        Assert.Equal(2, orderBillings.Count);
    }

    // ==========================================
    // Test 9: Reprocessing a past day cascades to later processed days of the month
    // ==========================================
    [Fact]
    public async Task Reprocessamento_DiaAnterior_RecalculaDiasSeguintesEmCascata()
    {
        await SeedBroker();
        await SeedDefaultEquityBrackets();

        var day13 = new DateOnly(2024, 11, 13);
        var day14 = new DateOnly(2024, 11, 14);
        var day15 = new DateOnly(2024, 11, 15);

        await SeedOrder(1, 300_000, 100, "JOAO", "PETR4", "PETR", tradeDate: day13); // 30M
        await SeedOrder(2, 300_000, 100, "JOAO", "PETR4", "PETR", tradeDate: day14); // 30M
        await SeedOrder(3, 100_000, 100, "JOAO", "PETR4", "PETR", tradeDate: day15); // 10M

        var service = CreateService();
        foreach (var day in new[] { day13, day14, day15 })
            await service.ProcessBrokerAsync(BrokerId, day, ProcessBillingId, CancellationToken.None);

        // Before correction: day14 = 20M in b1 (cap remaining 600) + 10M in b2 (450) = 1050
        Assert.Equal(1050m, (await GetDailyBilling("JOAO", "EQUITY", day14))!.TotalFee);

        // Late correction on day 13: +20M (day 13 becomes 50M)
        await SeedOrder(4, 200_000, 100, "JOAO", "PETR4", "PETR", tradeDate: day13);

        // Act: reprocess ONLY day 13
        await service.ProcessBrokerAsync(BrokerId, day13, ProcessBillingId, CancellationToken.None);

        // Day 13: 50M in b1 = 2500 -> CAP 2100
        var b13 = await GetDailyBilling("JOAO", "EQUITY", day13);
        Assert.Equal(2100m, b13!.TotalFee);

        // Day 14: starts at 50M -> 30M all in b2 = 1350
        var b14 = await GetDailyBilling("JOAO", "EQUITY", day14);
        Assert.Equal(50_000_000m, b14!.AccumulatedBefore);
        Assert.Equal(1350m, b14.TotalFee);
        Assert.Single(await GetBracketFeeTrackings(b14.Id));

        // Day 15: starts at 80M -> 10M in b2 = 450
        var b15 = await GetDailyBilling("JOAO", "EQUITY", day15);
        Assert.Equal(80_000_000m, b15!.AccumulatedBefore);
        Assert.Equal(450m, b15.TotalFee);
    }

    // ==========================================
    // Test 10: Reprocessing a day whose orders were removed clears the stale billing
    // ==========================================
    [Fact]
    public async Task Reprocessamento_DiaSemOrdens_RemoveBillingAntigo()
    {
        await SeedBroker();
        await SeedDefaultEquityBrackets();
        await SeedOrder(1, 100_000, 100, "TRADER1", "PETR4", "PETR");

        var service = CreateService();
        await service.ProcessBrokerAsync(BrokerId, _tradeDate, ProcessBillingId, CancellationToken.None);
        Assert.NotNull(await GetDailyBilling("TRADER1", "EQUITY"));

        await using (var conn = new NpgsqlConnection(_db.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("DELETE FROM history.orders");
        }

        await service.ProcessBrokerAsync(BrokerId, _tradeDate, ProcessBillingId, CancellationToken.None);

        Assert.Null(await GetDailyBilling("TRADER1", "EQUITY"));
    }

    // ==========================================
    // Test 11: CAP NULL persists and charges without ceiling
    // ==========================================
    [Fact]
    public async Task CapNulo_SemTeto_PersisteBracketCapNulo()
    {
        await SeedBroker();
        await InsertEquityBracket(BrokerId, null, 0m, null, 0.5m, cap: null);
        await SeedOrder(1, 1_000_000, 100, "TRADER1", "PETR4", "PETR"); // 100M -> 5000, no cap

        await CreateService().ProcessBrokerAsync(BrokerId, _tradeDate, ProcessBillingId, CancellationToken.None);

        var billing = await GetDailyBilling("TRADER1", "EQUITY");
        Assert.Equal(5000m, billing!.TotalFee);
        Assert.Null(Assert.Single(await GetBracketFeeTrackings(billing.Id)).BracketCap);
    }

    // ==========================================
    // Test 12: Entity casing differences group into one billing key
    // ==========================================
    [Fact]
    public async Task EntityBilling_CasingDiferente_AgrupaNaGrafiaDaConfig()
    {
        await SeedBroker();
        await SeedDefaultEquityBrackets();
        await SeedEntityBillingConfig("MESA_A");
        await SeedOrder(1, 100_000, 100, "JOAO", "PETR4", "PETR", entity: "mesa_a");
        await SeedOrder(2, 100_000, 100, "MARIA", "VALE3", "VALE", entity: "MESA_A");

        await CreateService().ProcessBrokerAsync(BrokerId, _tradeDate, ProcessBillingId, CancellationToken.None);

        var billing = await GetDailyBilling("MESA_A", "EQUITY", keyType: BillingKeyTypes.Entity);
        Assert.Equal(20_000_000m, billing!.TotalNotional);
        Assert.Equal(2, (await GetOrderBillings(billing.Id)).Count);
    }

    // ==========================================
    // Entity and trader with the same name are different billing keys
    // ==========================================
    [Fact]
    public async Task EntityETraderComMesmoNome_ChavesSeparadas_AcumuladosIndependentes()
    {
        await SeedBroker();
        await SeedDefaultEquityBrackets();
        await SeedEntityBillingConfig("JOAO");

        // Entity JOAO already accumulated 45M in the month; trader JOAO accumulated nothing
        await SeedPreviousDailyBilling("JOAO", "EQUITY", totalNotional: 45_000_000m, totalFee: 2100m,
            tradeDate: _tradeDate.AddDays(-1), keyType: BillingKeyTypes.Entity);
        await SeedPreviousBracketFeeTracking(bracketId: 1, chargedFee: 2100m, tradeDate: _tradeDate.AddDays(-1));

        await SeedOrder(1, 100_000, 100, "MARIA", "PETR4", "PETR", entity: "JOAO"); // 10M -> entity JOAO
        await SeedOrder(2, 100_000, 100, "JOAO", "VALE3", "VALE", entity: "MESA_B"); // 10M -> trader JOAO

        await CreateService().ProcessBrokerAsync(BrokerId, _tradeDate, ProcessBillingId, CancellationToken.None);

        var entityBilling = await GetDailyBilling("JOAO", "EQUITY", keyType: BillingKeyTypes.Entity);
        var traderBilling = await GetDailyBilling("JOAO", "EQUITY", keyType: BillingKeyTypes.Trader);

        // Entity: 45M -> 55M. Bracket 1 CAP (2100) already reached; 5M in bracket 2 * 0.45 bps = 225
        Assert.Equal(45_000_000m, entityBilling!.AccumulatedBefore);
        Assert.Equal(225m, entityBilling.TotalFee);

        // Trader: starts from zero, 10M * 0.5 bps = 500
        Assert.Equal(0m, traderBilling!.AccumulatedBefore);
        Assert.Equal(500m, traderBilling.TotalFee);

        var orderBilling = Assert.Single(await GetOrderBillings(traderBilling.Id));
        Assert.Equal(BillingKeyTypes.Trader, orderBilling.KeyType);
    }

    // ==========================================
    // Trader's own special rule prevails over the entity grouping
    // ==========================================
    [Fact]
    public async Task TraderComRegraEspecial_DentroDeEntity_CobradoNaPropriaRegra()
    {
        await SeedBroker();
        await SeedDefaultEquityBrackets();
        await SeedEntityBillingConfig("MESA_A");
        await InsertEquityBracket(BrokerId, "JOAO", 0, null, 0.20m, null);

        await SeedOrder(1, 100_000, 100, "JOAO", "PETR4", "PETR", entity: "MESA_A");  // 10M
        await SeedOrder(2, 100_000, 100, "MARIA", "VALE3", "VALE", entity: "MESA_A"); // 10M

        await CreateService().ProcessBrokerAsync(BrokerId, _tradeDate, ProcessBillingId, CancellationToken.None);

        // JOAO leaves the entity: 10M * 0.20 bps = 200
        var joao = await GetDailyBilling("JOAO", "EQUITY", keyType: BillingKeyTypes.Trader);
        Assert.Equal(10_000_000m, joao!.TotalNotional);
        Assert.Equal(200m, joao.TotalFee);

        // MESA_A only has MARIA: 10M * 0.5 bps = 500
        var mesa = await GetDailyBilling("MESA_A", "EQUITY", keyType: BillingKeyTypes.Entity);
        Assert.Equal(10_000_000m, mesa!.TotalNotional);
        Assert.Equal(500m, mesa.TotalFee);
    }

    // ==========================================
    // Test 13: Period queries feed the monthly revenue (variable + fixed + floor)
    // ==========================================
    [Fact]
    public async Task ReceitaMensal_ConsultasPorPeriodo_CompoemVariavelFixaEPiso()
    {
        await SeedBroker();
        await SeedPreviousDailyBilling("MESA_A", "EQUITY", totalNotional: 1_000_000m, totalFee: 300m, tradeDate: new DateOnly(2024, 11, 5), keyType: BillingKeyTypes.Entity);
        await SeedPreviousDailyBilling("MESA_A", "BMF", totalNotional: 0m, totalFee: 100m, tradeDate: new DateOnly(2024, 11, 6), keyType: BillingKeyTypes.Entity);
        await SeedPreviousDailyBilling("JOAO", "EQUITY", totalNotional: 1_000_000m, totalFee: 500m, tradeDate: new DateOnly(2024, 10, 7));

        await using (var conn = new NpgsqlConnection(_db.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(@"
                INSERT INTO financial_engine.entity_billing_configs (Broker, Entity, ValidFrom, ValidTo, IsActive, FloorFee)
                VALUES (@Broker, 'MESA_A', '2024-11-01', '2099-12-31', TRUE, 1000)", new { Broker = BrokerId });
            await conn.ExecuteAsync(@"
                INSERT INTO financial_engine.fixed_monthly_fees (Broker, Description, Amount, ValidFrom, ValidTo, IsActive)
                VALUES (@Broker, 'Plataforma', 250, '2024-01-01', '2099-12-31', TRUE)", new { Broker = BrokerId });
        }

        var dbFactory = new DbConnectionFactory(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = _db.ConnectionString })
            .Build());
        var from = new DateOnly(2024, 10, 1);
        var toExclusive = new DateOnly(2024, 12, 1);

        var fees = await new DailyBillingRepository(dbFactory).GetFeeByBillingKeyAndMonthAsync(from, toExclusive, CancellationToken.None);
        var configs = await new EntityBillingConfigRepository(dbFactory).GetByPeriodAsync(from, toExclusive.AddDays(-1), CancellationToken.None);
        var fixedFees = await new FixedMonthlyFeeRepository(dbFactory).GetByPeriodAsync(from, toExclusive.AddDays(-1), CancellationToken.None);

        var revenue = MonthlyRevenueCalculator.Calculate(2024, [10, 11], fees, configs, fixedFees, []);

        // October: JOAO 500 variable + 250 fixed; MESA_A floor not yet valid
        var oct = revenue.Single(r => r.Month == 10);
        Assert.Equal(500m, oct.VariableFee);
        Assert.Equal(250m, oct.FixedFee);
        Assert.Equal(0m, oct.FloorAdjustment);

        // November: MESA_A 400 (EQUITY+BMF) + 250 fixed + floor 600
        var nov = revenue.Single(r => r.Month == 11);
        Assert.Equal(400m, nov.VariableFee);
        Assert.Equal(600m, nov.FloorAdjustment);
        Assert.Equal(1250m, nov.TotalFee);
    }

    // ==========================================
    // AssetClass is derived from the trades' Asset (root); orders without trades fall back to the Symbol;
    // unrecognized roots are not billed
    // ==========================================
    [Fact]
    public async Task AssetClass_DeduzidaPelaRaiz_RaizDesconhecidaNaoCobrada()
    {
        await SeedBroker();
        await SeedDefaultEquityBrackets();
        await SeedDefaultBmfConfig();

        await SeedOrder(1, 100_000, 100, "TRADER1", "B3SA3", "B3SA"); // EQUITY (issuer root with digit)
        await SeedOrder(2, 100, 0, "TRADER1", "BGIV24", "BGI");        // BMF, other contract: 100 * 0.07
        await SeedOrder(3, 100, 10, "TRADER1", "XX1", "X");            // unrecognized root
        await SeedOrder(4, 100, 0, "TRADER1", "WINZ24", null);         // no trades: root from Symbol, mini 100 * 0.03

        await CreateService().ProcessBrokerAsync(BrokerId, _tradeDate, ProcessBillingId, CancellationToken.None);

        Assert.Equal(500m, (await GetDailyBilling("TRADER1", "EQUITY"))!.TotalFee);
        Assert.Equal(10m, (await GetDailyBilling("TRADER1", "BMF"))!.TotalFee);

        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        Assert.Equal(0, await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM financial_engine.order_billing WHERE OrderId = 3"));
    }

    // ==========================================
    // Synthetic (BTGDOL = DOL + WDO): the view returns one row per leg, LastQty in cheio units
    // ==========================================
    [Fact]
    public async Task Sintetico_ViewQuebraPorPerna_CobraMiniECheio()
    {
        await SeedBroker();
        await SeedDefaultBmfConfig();

        // Only the synthetic order arrives; its trades carry Asset DOL and the leg in the UniqueTradeId suffix
        await SeedOrder(1, 4, 5800, "TRADER1", "BTGDOLZ24", asset: null);
        await SeedTrade(orderId: 1, lastQty: 0.2m, lastPx: 5800, "TRADER1", "BTGDOLZ24", "DOL", uniqueTradeId: "1001-WDO");
        await SeedTrade(orderId: 1, lastQty: 1.8m, lastPx: 5800, "TRADER1", "BTGDOLZ24", "DOL", uniqueTradeId: "1002-WDO");
        await SeedTrade(orderId: 1, lastQty: 2m, lastPx: 5800, "TRADER1", "BTGDOLZ24", "DOL", uniqueTradeId: "1003-DOL");

        await CreateService().ProcessBrokerAsync(BrokerId, _tradeDate, ProcessBillingId, CancellationToken.None);

        // WDO: 2.0 cheio * 5 = 10 minis * 0.03 = 0.30; DOL: 2 * 0.15 = 0.30
        var billing = await GetDailyBilling("TRADER1", "BMF");
        Assert.Equal(12m, billing!.TotalQty);
        Assert.Equal(0.60m, billing.TotalFee);

        var legs = (await GetOrderBillings(billing.Id)).OrderBy(o => o.Asset).ToList();
        Assert.Equal(new[] { "DOL", "WDO" }, legs.Select(o => o.Asset));
        Assert.All(legs, o => Assert.Equal(1, o.OrderId));
        Assert.Equal(new[] { 2m, 10m }, legs.Select(o => o.Qty));

        // Report detail: one line per leg, not duplicated by the view join
        var detail = await new OrderBillingRepository(CreateDbFactory())
            .GetByBrokerAndMonthAsync(BrokerId, _tradeDate.Year, _tradeDate.Month, CancellationToken.None);
        Assert.Equal(2, detail.Count);
        Assert.All(detail, d => Assert.Equal("BTGDOLZ24", d.Symbol));
        Assert.Equal(0.60m, detail.Sum(d => d.FeeAmount));
    }

    // ==========================================
    // Test 14: Market Data is added automatically at the end of the broker's billing
    // ==========================================
    [Fact]
    public async Task MarketData_ProcessamentoDoBroker_ApuraUsuariosVinculadosMenosExcluidos()
    {
        await SeedBroker();
        await SeedDefaultEquityBrackets();
        await SeedOrder(1, 100_000, 100, "JOAO", "PETR4", "PETR");

        // JOAO linked on 2 days (charged once), maria on 1 day, API_BOT globally excluded,
        // ADMIN1 excluded only for this broker, other broker's user ignored
        await SeedSnapshot(new DateOnly(2024, 11, 1), (BrokerId, "JOAO"), (BrokerId, "API_BOT"), (BrokerId, "ADMIN1"), (2, "OUTRO"));
        await SeedSnapshot(new DateOnly(2024, 11, 2), (BrokerId, "JOAO"), (BrokerId, "maria"));
        await SeedSnapshot(new DateOnly(2024, 10, 31), (BrokerId, "PEDRO")); // other month
        await SeedMarketDataConfig(BrokerId, 150m);
        await SeedMarketDataExclusion(null, "api_bot");
        await SeedMarketDataExclusion(BrokerId, "ADMIN1");

        await CreateService().ProcessBrokerAsync(BrokerId, _tradeDate, ProcessBillingId, CancellationToken.None);

        var billing = await GetMarketDataBilling(2024, 11);
        Assert.Equal(new[] { "JOAO", "maria" }, billing.Select(b => b.Trader).OrderBy(t => t, StringComparer.OrdinalIgnoreCase));
        Assert.All(billing, b => Assert.Equal(150m, b.Amount));
        Assert.All(billing, b => Assert.Equal(BrokerId, b.Broker));

        // Reprocessing is idempotent
        await CreateService().ProcessBrokerAsync(BrokerId, _tradeDate, ProcessBillingId, CancellationToken.None);
        Assert.Equal(2, (await GetMarketDataBilling(2024, 11)).Count);
    }

    // ==========================================
    // Test 15: New exclusion + recalculation removes the user; revenue includes Market Data
    // ==========================================
    [Fact]
    public async Task MarketData_ExclusaoERecalculo_RemoveUsuario_EReceitaIncluiMarketData()
    {
        await SeedBroker();
        await SeedSnapshot(new DateOnly(2024, 11, 3), (BrokerId, "JOAO"), (BrokerId, "MARIA"));
        await SeedMarketDataConfig(BrokerId, 100m);

        var service = CreateMarketDataService();
        await service.RecalculateMonthAsync(2024, 11, null, CancellationToken.None);
        Assert.Equal(2, (await GetMarketDataBilling(2024, 11)).Count);

        await SeedMarketDataExclusion(BrokerId, "MARIA");
        var result = await service.RecalculateMonthAsync(2024, 11, null, CancellationToken.None);

        Assert.Single(result.Excluded);
        var billing = Assert.Single(await GetMarketDataBilling(2024, 11));
        Assert.Equal("JOAO", billing.Trader);

        // Revenue (reports/dashboard) picks it up for a broker with no variable billing
        var marketData = await new MarketDataBillingRepository(CreateDbFactory())
            .GetByPeriodAsync(2024, 11, 11, null, CancellationToken.None);
        var revenue = Assert.Single(MonthlyRevenueCalculator.Calculate(2024, [11], [], [], [], marketData));
        Assert.Equal(100m, revenue.MarketDataFee);
        Assert.Equal(100m, revenue.TotalFee);
    }

    // ==========================================
    // Test 16: Sync reads the external API, stores the day's snapshot and recalculates the month
    // ==========================================
    [Fact]
    public async Task MarketData_Sync_GravaSnapshotDoDiaERecalculaMes()
    {
        await SeedBroker();
        await SeedMarketDataConfig(BrokerId, 80m);

        var handler = new StubHttpHandler($$"""[{"broker": {{BrokerId}}, "user": "JOAO"}, {"broker": {{BrokerId}}, "user": "JOAO"}, {"broker": {{BrokerId}}, "user": " ANA "}]""");
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://accounts.test/") };
        var service = CreateMarketDataService(http);

        var count = await service.SyncAsync(new DateOnly(2024, 11, 20), CancellationToken.None);

        Assert.Equal(3, count);
        Assert.Equal("http://accounts.test/accounts", handler.LastRequestUri?.ToString());

        var billing = await GetMarketDataBilling(2024, 11);
        Assert.Equal(new[] { "ANA", "JOAO" }, billing.Select(b => b.Trader).OrderBy(t => t));
    }

    private sealed class StubHttpHandler(string json) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private async Task SeedSnapshot(DateOnly date, params (int Broker, string Trader)[] accounts)
    {
        await new UserExchangeAccountSnapshotRepository(CreateDbFactory()).ReplaceSnapshotAsync(
            date, accounts.Select(a => new UserExchangeAccount(a.Broker, a.Trader)), CancellationToken.None);
    }

    private async Task SeedMarketDataConfig(int broker, decimal amountPerUser)
    {
        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO financial_engine.market_data_fee_configs (Broker, AmountPerUser, ValidFrom, ValidTo, IsActive)
            VALUES (@Broker, @Amount, '2024-01-01', '2099-12-31', TRUE)",
            new { Broker = broker, Amount = amountPerUser });
    }

    private async Task SeedMarketDataExclusion(int? broker, string trader)
    {
        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO financial_engine.market_data_exclusions (Broker, Trader, Reason, IsActive)
            VALUES (@Broker, @Trader, 'teste', TRUE)",
            new { Broker = broker, Trader = trader });
    }

    private async Task<List<MarketDataBilling>> GetMarketDataBilling(int year, int month)
        => await new MarketDataBillingRepository(CreateDbFactory()).GetByPeriodAsync(year, month, month, null, CancellationToken.None);

    // ==========================================
    // Helper: Create service with real repositories
    // ==========================================
    private DbConnectionFactory CreateDbFactory()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _db.ConnectionString
            }).Build();

        return new DbConnectionFactory(config);
    }

    private MarketDataService CreateMarketDataService(HttpClient? apiHttpClient = null)
    {
        var dbFactory = CreateDbFactory();
        return new MarketDataService(
            new UserExchangeAccountApiClient(apiHttpClient ?? new HttpClient()),
            new UserExchangeAccountSnapshotRepository(dbFactory),
            new MarketDataFeeConfigRepository(dbFactory),
            new MarketDataExclusionRepository(dbFactory),
            new MarketDataBillingRepository(dbFactory),
            new DailyBillingRepository(dbFactory),
            dbFactory,
            NullLogger<MarketDataService>.Instance);
    }

    private BillingProcessingService CreateService()
    {
        var dbFactory = CreateDbFactory();

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
            dbFactory, registry, CreateMarketDataService(), NullLogger<BillingProcessingService>.Instance);
    }

    // ==========================================
    // Seed helpers
    // ==========================================
    private async Task SeedBroker()
    {
        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "INSERT INTO financial_engine.brokers (Code, Name) VALUES (@Code, @Name) ON CONFLICT DO NOTHING",
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

    private async Task InsertEquityBracket(int broker, string? trader, decimal notionalFrom, decimal? notionalTo, decimal bps, decimal? cap,
        string keyType = BillingKeyTypes.Trader)
    {
        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO financial_engine.equity_fee_brackets
                (Broker, Trader, KeyType, ValidFrom, ValidTo, NotionalFrom, NotionalTo, Bps, Cap, IsActive)
            VALUES (@Broker, @Trader, @KeyType, '2024-01-01', '2099-12-31', @NotionalFrom, @NotionalTo, @Bps, @Cap, TRUE)",
            new
            {
                Broker = broker,
                Trader = trader,
                KeyType = trader is null ? null : keyType,
                NotionalFrom = notionalFrom,
                NotionalTo = notionalTo,
                Bps = bps,
                Cap = cap
            });
    }

    private async Task SeedDefaultBmfConfig()
    {
        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO financial_engine.bmf_fee_configs
                (Broker, Trader, ValidFrom, ValidTo, ValorMini, ValorCheio, ValorOutros,
                 DI1Venc1a4, DI1Venc5, DI1Venc6, DI1Venc7, DI1Venc8Plus, CapMensal, IsActive)
            VALUES (@Broker, NULL, '2024-01-01', '2099-12-31', 0.03, 0.15, 0.07,
                    0.01, 0.02, 0.04, 0.05, 0.10, 25000, TRUE)",
            new { Broker = BrokerId });
    }

    /// <summary>
    /// history.orders has no Asset (only trades do). For non-GTC orders with an asset, also seeds one
    /// execution trade (CumQty @ AvgPx) carrying it; asset null = order without trades (or trades seeded apart).
    /// GTC/GTD orders take their trades (and Asset) from explicit SeedTrade calls.
    /// </summary>
    private async Task SeedOrder(long id, decimal qty, decimal price, string trader, string symbol,
        string? asset, string entity = "MESA1", string? timeInForce = null, DateOnly? tradeDate = null)
    {
        var date = tradeDate ?? _tradeDate;
        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO history.orders
                (Id, CumQty, AvgPx, Symbol, EnteringTrader,
                 Entity, TradeDate, EnteringFirm, OmsInstance, Side, businessDate, TimeInForce)
            VALUES (@Id, @CumQty, @AvgPx, @Symbol, @EnteringTrader,
                    @Entity, @TradeDate, @EnteringFirm, 'OMS1', 'BUY', @BusinessDate, @TimeInForce)",
            new
            {
                Id = id,
                CumQty = qty,
                AvgPx = price,
                Symbol = symbol,
                EnteringTrader = trader,
                Entity = entity,
                TradeDate = date,
                EnteringFirm = BrokerId.ToString(),
                BusinessDate = date.ToString("yyyy-MM-dd"),
                TimeInForce = timeInForce
            });

        if (asset is not null && timeInForce is null)
            await SeedTrade(id, qty, price, trader, symbol, asset, entity, tradeDate: date);
    }

    private async Task SeedPreviousDailyBilling(string trader, string assetClass,
        decimal totalNotional, decimal totalFee, DateOnly tradeDate, decimal totalQty = 0,
        string keyType = BillingKeyTypes.Trader)
    {
        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO financial_engine.daily_billing
                (Broker, Trader, KeyType, TradeDate, AssetClass, TotalNotional, TotalQty, TotalFee,
                 AccumulatedBefore, AccumulatedAfter, ProcessBillingId)
            VALUES (@Broker, @Trader, @KeyType, @TradeDate, @AssetClass, @TotalNotional, @TotalQty, @TotalFee,
                    0, @TotalNotional, @ProcessBillingId)",
            new
            {
                Broker = BrokerId,
                Trader = trader,
                KeyType = keyType,
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

    private async Task SeedTrade(long orderId, decimal lastQty, decimal lastPx, string trader,
        string symbol, string asset, string entity = "MESA1", string? uniqueTradeId = null, DateOnly? tradeDate = null)
    {
        var date = tradeDate ?? _tradeDate;
        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO history.trades
                (OrderId, LastQty, LastPx, UniqueTradeId, Symbol, Asset, EnteringTrader,
                 Entity, TradeDate, EnteringFirm, OmsInstance, Side, businessDate)
            VALUES (@OrderId, @LastQty, @LastPx, @UniqueTradeId, @Symbol, @Asset, @EnteringTrader,
                    @Entity, @TradeDate, @EnteringFirm, 'OMS1', 'BUY', @BusinessDate)",
            new
            {
                OrderId = orderId,
                LastQty = lastQty,
                LastPx = lastPx,
                UniqueTradeId = uniqueTradeId,
                Symbol = symbol,
                Asset = asset,
                EnteringTrader = trader,
                Entity = entity,
                TradeDate = date,
                EnteringFirm = BrokerId.ToString(),
                BusinessDate = date.ToString("yyyy-MM-dd")
            });
    }

    // ==========================================
    // Assert helpers
    // ==========================================
    private async Task<DailyBilling?> GetDailyBilling(string trader, string assetClass, DateOnly? tradeDate = null,
        string keyType = BillingKeyTypes.Trader)
    {
        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<DailyBilling>(
            @"SELECT * FROM financial_engine.daily_billing
              WHERE Broker = @Broker AND Trader = @Trader AND KeyType = @KeyType
                AND AssetClass = @AssetClass AND TradeDate = @TradeDate",
            new { Broker = BrokerId, Trader = trader, KeyType = keyType, AssetClass = assetClass, TradeDate = tradeDate ?? _tradeDate });
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
