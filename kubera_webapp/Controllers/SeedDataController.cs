using Dapper;
using FinancialEngineCore.Infra;
using System.Data;
using System.Globalization;

namespace KuberaWebApp.Controllers;

public static class SeedDataController
{
    private static readonly (string Symbol, decimal BasePrice)[] EquitySymbols =
    [
        ("PETR4", 29.50m), ("VALE3", 58.00m), ("ITUB4", 33.50m), ("BBDC4", 12.80m),
        ("WEGE3", 95.00m), ("ABEV3", 45.50m), ("RENT3", 60.00m), ("B3SA3", 11.50m)
    ];

    private static readonly string[] MonthAbbreviations =
        ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

    // DI1 month codes: F=Jan, G=Feb, H=Mar, J=Apr, K=May, M=Jun, N=Jul, Q=Aug, U=Sep, V=Oct, X=Nov, Z=Dec
    private static readonly char[] DI1MonthCodes = ['F', 'G', 'H', 'J', 'K', 'M', 'N', 'Q', 'U', 'V', 'X', 'Z'];

    public static RouteGroupBuilder MapSeedDataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/seed").WithTags("Seed Data");

        group.MapPost("/broker-935", async (
            SeedBroker935Request request,
            DbConnectionFactory dbFactory,
            CancellationToken ct) =>
        {
            if (!DateTime.TryParseExact(request.Month + "-01", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var monthDate))
            {
                return Results.BadRequest("Formato invalido. Use yyyy-MM (ex: 2026-02).");
            }

            var year = monthDate.Year;
            var month = monthDate.Month;
            var tradingDays = GetTradingDays(year, month);
            var monthSuffix = MonthAbbreviations[month - 1] + (year % 100);

            using var conn = await dbFactory.CreateOpenConnectionAsync(ct);
            using var tx = conn.BeginTransaction();

            try
            {
                // 1. Ensure broker 935 exists
                await conn.ExecuteAsync(@"
                    INSERT INTO financial_engine.brokers (Code, Name, Document, Status, BillingCurrency, Contacts)
                    VALUES (935, 'Corretora B3 Teste', '93.500.000/0001-35', true, 'BRL', ARRAY['teste@b3.com.br'])
                    ON CONFLICT DO NOTHING", transaction: tx);

                // 2. Clean existing data for this month
                var firstDay = new DateOnly(year, month, 1);
                var lastDay = firstDay.AddMonths(1).AddDays(-1);
                var businessDatePrefix = $"{year}-{month:D2}";

                await conn.ExecuteAsync(@"
                    DELETE FROM financial_engine.daily_billing
                    WHERE Broker = 935 AND TradeDate >= @FirstDay AND TradeDate <= @LastDay",
                    new { FirstDay = firstDay, LastDay = lastDay }, tx);

                await conn.ExecuteAsync(@"
                    DELETE FROM history.trades WHERE OrderId IN (
                        SELECT Id FROM history.orders WHERE EnteringFirm = '935' AND businessDate LIKE @Prefix
                    )", new { Prefix = businessDatePrefix + "%" }, tx);

                await conn.ExecuteAsync(@"
                    DELETE FROM history.orders WHERE EnteringFirm = '935' AND businessDate LIKE @Prefix",
                    new { Prefix = businessDatePrefix + "%" }, tx);

                // 3. Ensure fee configs exist (only insert if none active)
                var bracketsCreated = 0;
                var bmfConfigsCreated = 0;

                var existingBrackets = await conn.QuerySingleAsync<int>(@"
                    SELECT COUNT(*) FROM financial_engine.equity_fee_brackets
                    WHERE Broker = 935 AND IsActive = TRUE", transaction: tx);

                if (existingBrackets == 0)
                {
                    await conn.ExecuteAsync(@"
                        INSERT INTO financial_engine.equity_fee_brackets
                            (Broker, Trader, ValidFrom, ValidTo, NotionalFrom, NotionalTo, Bps, Cap, IsActive)
                        VALUES
                            (935, NULL, @ValidFrom, @ValidTo, 0, 500000, 0.55, 275.00, true),
                            (935, NULL, @ValidFrom, @ValidTo, 500000, 2000000, 0.42, 840.00, true),
                            (935, NULL, @ValidFrom, @ValidTo, 2000000, NULL, 0.32, 1600.00, true)",
                        new { ValidFrom = new DateOnly(year, 1, 1), ValidTo = new DateOnly(year, 12, 31) }, tx);
                    bracketsCreated = 3;
                }

                var existingBmf = await conn.QuerySingleAsync<int>(@"
                    SELECT COUNT(*) FROM financial_engine.bmf_fee_configs
                    WHERE Broker = 935 AND IsActive = TRUE", transaction: tx);

                if (existingBmf == 0)
                {
                    await conn.ExecuteAsync(@"
                        INSERT INTO financial_engine.bmf_fee_configs
                            (Broker, Trader, ValidFrom, ValidTo, ValorMini, ValorCheio, ValorOutros,
                             DI1Venc1a4, DI1Venc5, DI1Venc6, DI1Venc7, DI1Venc8Plus, CapMensal, IsActive)
                        VALUES
                            (935, NULL, @ValidFrom, @ValidTo, 0.50, 2.50, 1.00,
                             1.20, 1.00, 0.80, 0.60, 0.40, 5000.00, true)",
                        new { ValidFrom = new DateOnly(year, 1, 1), ValidTo = new DateOnly(year, 12, 31) }, tx);
                    bmfConfigsCreated = 1;
                }

                // 4. Generate orders and trades
                int ordersCreated = 0;
                int tradesCreated = 0;

                for (int dayIdx = 0; dayIdx < tradingDays.Count; dayIdx++)
                {
                    var day = tradingDays[dayIdx];
                    var dateStr = day.ToString("yyyy-MM-dd");

                    // --- TRADER_A: 3-4 EQUITY orders per day (high volume) ---
                    var traderACount = 3 + (dayIdx % 2);
                    for (int i = 0; i < traderACount; i++)
                    {
                        var symIdx = (dayIdx * 3 + i) % EquitySymbols.Length;
                        var (symbol, basePrice) = EquitySymbols[symIdx];
                        var qty = ((dayIdx + i) % 5 + 1) * 500;
                        var priceVar = 1m + ((dayIdx * 7 + i * 3) % 20 - 10) * 0.005m;
                        var price = Math.Round(basePrice * priceVar, 2);
                        var side = (dayIdx + i) % 3 == 0 ? "SELL" : "BUY";

                        string? tif = null;
                        if (dayIdx % 7 == 0 && i == 0)
                            tif = "GTC";

                        var orderId = await InsertOrderAsync(conn, tx, qty, price, symbol, symbol[..4],
                            "TRADER_A", side, day, dateStr, tif);
                        ordersCreated++;
                        if (tif is null) tradesCreated++;

                        // Generate trades for GTC orders
                        if (tif == "GTC")
                        {
                            var tradeCount = 2 + (dayIdx % 3);
                            var remainQty = qty;
                            for (int t = 0; t < tradeCount; t++)
                            {
                                var tradeQty = t < tradeCount - 1
                                    ? (int)(remainQty / (decimal)(tradeCount - t))
                                    : remainQty;
                                var tpVar = 1m + (t - 1) * 0.002m;
                                var tradePrice = Math.Round(price * tpVar, 2);

                                await conn.ExecuteAsync(@"
                                    INSERT INTO history.trades
                                        (OrderId, LastQty, LastPx, Symbol, Asset, EnteringTrader,
                                         Entity, TradeDate, EnteringFirm, OmsInstance, Side, businessDate)
                                    VALUES
                                        (@OrderId, @Qty, @Price, @Symbol, @Asset, 'TRADER_A',
                                         'MESA_TESTE', @TradeDate, '935', 'OMS_TESTE', @Side, @BusinessDate)",
                                    new { OrderId = orderId, Qty = tradeQty, Price = tradePrice,
                                          Symbol = symbol, Asset = symbol[..4], TradeDate = day, BusinessDate = dateStr, Side = side }, tx);
                                tradesCreated++;
                                remainQty -= tradeQty;
                            }
                        }
                    }

                    // --- TRADER_B: 2-3 EQUITY orders per day (lower volume) ---
                    var traderBCount = 2 + (dayIdx % 3 == 0 ? 1 : 0);
                    for (int i = 0; i < traderBCount; i++)
                    {
                        var symIdx = (dayIdx * 2 + i + 4) % EquitySymbols.Length;
                        var (symbol, basePrice) = EquitySymbols[symIdx];
                        var qty = ((dayIdx + i + 2) % 4 + 1) * 300;
                        var priceVar = 1m + ((dayIdx * 5 + i * 7) % 20 - 10) * 0.004m;
                        var price = Math.Round(basePrice * priceVar, 2);
                        var side = (dayIdx + i) % 2 == 0 ? "BUY" : "SELL";

                        await InsertOrderAsync(conn, tx, qty, price, symbol, symbol[..4], "TRADER_B", side, day, dateStr);
                        ordersCreated++;
                        tradesCreated++;
                    }

                    // --- TRADER_C: 2-3 BMF orders per day ---
                    // Mini contract (WIN or WDO)
                    var miniSym = dayIdx % 2 == 0 ? "WIN" : "WDO";
                    var miniQty = 5 + (dayIdx % 10) * 2;
                    var miniPrice = miniSym == "WIN"
                        ? 126000m + dayIdx * 150m
                        : 5780m + dayIdx * 10m;

                    await InsertOrderAsync(conn, tx, miniQty, miniPrice, miniSym + monthSuffix, miniSym,
                        "TRADER_C", dayIdx % 2 == 0 ? "BUY" : "SELL", day, dateStr);
                    ordersCreated++;
                    tradesCreated++;

                    // Cheio contract (IND or DOL)
                    var cheioSym = dayIdx % 3 == 0 ? "IND" : "DOL";
                    var cheioQty = 1 + (dayIdx % 5);
                    var cheioPrice = cheioSym == "IND"
                        ? 126000m + dayIdx * 200m
                        : 5780m + dayIdx * 15m;

                    await InsertOrderAsync(conn, tx, cheioQty, cheioPrice, cheioSym + monthSuffix, cheioSym,
                        "TRADER_C", dayIdx % 2 == 0 ? "SELL" : "BUY", day, dateStr);
                    ordersCreated++;
                    tradesCreated++;

                    // DI1 contract (every other day)
                    if (dayIdx % 2 == 0)
                    {
                        // Vencimento ~6 meses a frente
                        var maturityMonth = (month + 5) % 12;
                        var maturityYear = maturityMonth < month ? year + 1 : year;
                        var di1Symbol = $"DI1{DI1MonthCodes[maturityMonth]}{maturityYear % 100}";
                        var di1Qty = 10 + (dayIdx % 8) * 5;

                        await InsertOrderAsync(conn, tx, di1Qty, 0, di1Symbol, "DI1", "TRADER_C", "BUY", day, dateStr);
                        ordersCreated++;
                        tradesCreated++;
                    }

                    // Sintético de dólar (DOL + WDO) a cada 3 dias: só a ordem do sintético chega;
                    // a perna de cada trade vem no sufixo do UniqueTradeId e LastQty no padrão do cheio
                    if (dayIdx % 3 == 1)
                    {
                        var synthSymbol = "BTGDOL" + monthSuffix;
                        var synthPrice = 5780m + dayIdx * 12m;
                        var synthOrderId = await conn.QuerySingleAsync<long>(@"
                            INSERT INTO history.orders
                                (CumQty, AvgPx, Symbol, EnteringTrader,
                                 Entity, TradeDate, EnteringFirm, OmsInstance, Side, businessDate)
                            VALUES
                                (3, @Price, @Symbol, 'TRADER_C',
                                 'MESA_TESTE', @TradeDate, '935', 'OMS_TESTE', 'BUY', @BusinessDate)
                            RETURNING Id",
                            new { Price = synthPrice, Symbol = synthSymbol, TradeDate = day, BusinessDate = dateStr }, tx);
                        ordersCreated++;

                        foreach (var (leg, lastQty) in new[] { ("DOL", 2m), ("WDO", 0.6m), ("WDO", 0.4m) })
                        {
                            await conn.ExecuteAsync(@"
                                INSERT INTO history.trades
                                    (OrderId, LastQty, LastPx, UniqueTradeId, Symbol, Asset, EnteringTrader,
                                     Entity, TradeDate, EnteringFirm, OmsInstance, Side, businessDate)
                                VALUES
                                    (@OrderId, @Qty, @Price, @UniqueTradeId, @Symbol, 'DOL', 'TRADER_C',
                                     'MESA_TESTE', @TradeDate, '935', 'OMS_TESTE', 'BUY', @BusinessDate)",
                                new { OrderId = synthOrderId, Qty = lastQty, Price = synthPrice,
                                      UniqueTradeId = $"{synthOrderId}{tradesCreated}-{leg}", Symbol = synthSymbol,
                                      TradeDate = day, BusinessDate = dateStr }, tx);
                            tradesCreated++;
                        }
                    }
                }

                tx.Commit();

                return Results.Ok(new
                {
                    Message = "Dados gerados com sucesso para Corretora B3 Teste (935)",
                    Broker = 935,
                    Month = request.Month,
                    TradingDays = tradingDays.Count,
                    FirstDay = tradingDays.First().ToString("yyyy-MM-dd"),
                    LastDay = tradingDays.Last().ToString("yyyy-MM-dd"),
                    OrdersCreated = ordersCreated,
                    TradesCreated = tradesCreated,
                    ConfigsCreated = new
                    {
                        EquityBrackets = bracketsCreated,
                        BmfConfigs = bmfConfigsCreated
                    },
                    Traders = new[]
                    {
                        "TRADER_A (EQUITY, alto volume, inclui GTC)",
                        "TRADER_B (EQUITY, volume moderado)",
                        "TRADER_C (BMF: mini, cheio, DI1)"
                    }
                });
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return Results.Problem($"Erro ao gerar dados: {ex.Message}");
            }
        })
        .WithName("SeedBroker935")
        .WithDescription("Gera dados ficticios (orders + trades) para a corretora 935 em um mes especifico");

        return group;
    }

    /// <summary>
    /// history.orders não tem Asset (só os trades têm): ordem não-GTC ganha um trade de execução
    /// (CumQty @ AvgPx) com a raiz. GTC/GTD gera os trades à parte.
    /// </summary>
    private static async Task<long> InsertOrderAsync(IDbConnection conn, IDbTransaction tx,
        decimal qty, decimal price, string symbol, string asset, string trader, string side,
        DateOnly day, string businessDate, string? timeInForce = null)
    {
        var orderId = await conn.QuerySingleAsync<long>(@"
            INSERT INTO history.orders
                (CumQty, AvgPx, Symbol, EnteringTrader, Entity,
                 TradeDate, EnteringFirm, OmsInstance, Side, businessDate, TimeInForce)
            VALUES
                (@Qty, @Price, @Symbol, @Trader, 'MESA_TESTE',
                 @TradeDate, '935', 'OMS_TESTE', @Side, @BusinessDate, @TimeInForce)
            RETURNING Id",
            new { Qty = qty, Price = price, Symbol = symbol, Trader = trader, Side = side,
                  TradeDate = day, BusinessDate = businessDate, TimeInForce = timeInForce }, tx);

        if (timeInForce is null)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO history.trades
                    (OrderId, LastQty, LastPx, Symbol, Asset, EnteringTrader,
                     Entity, TradeDate, EnteringFirm, OmsInstance, Side, businessDate)
                VALUES
                    (@OrderId, @Qty, @Price, @Symbol, @Asset, @Trader,
                     'MESA_TESTE', @TradeDate, '935', 'OMS_TESTE', @Side, @BusinessDate)",
                new { OrderId = orderId, Qty = qty, Price = price, Symbol = symbol, Asset = asset,
                      Trader = trader, Side = side, TradeDate = day, BusinessDate = businessDate }, tx);
        }

        return orderId;
    }

    private static List<DateOnly> GetTradingDays(int year, int month)
    {
        var days = new List<DateOnly>();
        var daysInMonth = DateTime.DaysInMonth(year, month);
        for (int d = 1; d <= daysInMonth; d++)
        {
            var date = new DateOnly(year, month, d);
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                days.Add(date);
        }
        return days;
    }
}

public record SeedBroker935Request(string Month);
