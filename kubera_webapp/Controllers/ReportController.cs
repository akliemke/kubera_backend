using FinancialEngineCore.Domain;
using FinancialEngineCore.Infra;
using KuberaWebApp.Services;
using System.Globalization;

namespace KuberaWebApp.Controllers;

public static class ReportController
{
    public static RouteGroupBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var reportGroup = app.MapGroup("/report");

        reportGroup.MapGet("/billing/daily/{data}", async (
            string data,
            DailyBillingRepository repository,
            CancellationToken ct) =>
        {
            if (!DateOnly.TryParse(data, out var tradeDate))
                return Results.BadRequest("Formato de data inválido. Use yyyy-MM-dd.");

            var billings = await repository.GetByDateAsync(tradeDate, ct);

            return Results.Ok(new
            {
                Date = tradeDate.ToString("yyyy-MM-dd"),
                Records = billings.Select(b => new
                {
                    b.Broker,
                    b.Trader,
                    b.AssetClass,
                    b.TotalNotional,
                    b.TotalQty,
                    b.TotalFee,
                    b.AccumulatedBefore,
                    b.AccumulatedAfter
                }),
                Summary = new
                {
                    TotalFee = billings.Sum(b => b.TotalFee),
                    TotalNotional = billings.Sum(b => b.TotalNotional),
                    TotalQty = billings.Sum(b => b.TotalQty),
                    RecordCount = billings.Count
                }
            });
        });

        // PDF must be registered BEFORE the generic /billing/month/{ano_mes}
        // to avoid the catch-all route capturing the request
        reportGroup.MapGet("/billing/month/{ano_mes}/broker/{brokerId:int}/pdf", async (
            string ano_mes,
            int brokerId,
            DailyBillingRepository dailyBillingRepository,
            OrderBillingRepository orderBillingRepository,
            BrokerRepository brokerRepository,
            FixedMonthlyFeeRepository fixedMonthlyFeeRepository,
            EntityBillingConfigRepository entityBillingConfigRepository,
            MarketDataBillingRepository marketDataBillingRepository,
            MonthlyPdfReportService pdfService,
            CancellationToken ct) =>
        {
            if (!DateTime.TryParseExact(ano_mes + "-01", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var monthDate))
            {
                return Results.BadRequest("Formato invalido. Use yyyy-MM (ex: 2024-01).");
            }

            var year = monthDate.Year;
            var month = monthDate.Month;

            var summariesTask = dailyBillingRepository.GetFullMonthSummaryByBrokerAsync(brokerId, year, month, ct);
            var ordersTask = orderBillingRepository.GetByBrokerAndMonthAsync(brokerId, year, month, ct);
            var brokersTask = brokerRepository.GetAllAsync(ct);
            var fixedFeesTask = fixedMonthlyFeeRepository.GetByBrokerAndMonthAsync(brokerId, year, month, ct);
            var entityConfigsTask = entityBillingConfigRepository.GetByBrokerAndMonthAsync(brokerId, year, month, ct);
            var marketDataTask = marketDataBillingRepository.GetByPeriodAsync(year, month, month, brokerId, ct);

            await Task.WhenAll(summariesTask, ordersTask, brokersTask, fixedFeesTask, entityConfigsTask, marketDataTask);

            var summaries = await summariesTask;
            var orders = await ordersTask;
            var broker = (await brokersTask).FirstOrDefault(b => b.Code == brokerId);
            var fixedFees = await fixedFeesTask;
            var entityConfigs = await entityConfigsTask;
            var marketData = await marketDataTask;

            if (broker == null)
                return Results.NotFound($"Corretora {brokerId} nao encontrada.");

            if (summaries.Count == 0 && !fixedFees.Any() && marketData.Count == 0)
                return Results.NotFound($"Nenhum dado de billing para corretora {brokerId} em {ano_mes}.");

            var pdfBytes = pdfService.Generate(broker, year, month, summaries, orders, fixedFees, entityConfigs, marketData);

            return Results.File(pdfBytes, "application/pdf", $"billing_{brokerId}_{ano_mes}.pdf");
        });

        // Valores de fee: TotalFee = receita completa (variável + fixa + piso + Market Data), igual ao PDF mensal.
        // VariableFee / FixedFee / FloorAdjustment / MarketDataFee detalham a composição.
        reportGroup.MapGet("/billing/month/{ano_mes}", async (
            string ano_mes,
            DailyBillingRepository repository,
            RevenueReportService revenueService,
            CancellationToken ct) =>
        {
            // Expected format: yyyy-MM (e.g., 2024-01)
            if (!DateTime.TryParseExact(ano_mes + "-01", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var monthDate))
            {
                return Results.BadRequest("Formato inválido. Use yyyy-MM (ex: 2024-01).");
            }

            var summariesTask = repository.GetMonthSummaryAsync(monthDate.Year, monthDate.Month, ct);
            var revenueTask = revenueService.GetMonthAsync(monthDate.Year, monthDate.Month, ct);

            await Task.WhenAll(summariesTask, revenueTask);

            var summaries = await summariesTask;
            var classesByBroker = summaries.ToLookup(s => s.Broker);
            var revenueByBroker = (await revenueTask).ToDictionary(r => r.Broker);

            // Brokers com billing variável ou só com cobrança fixa no mês
            var brokers = classesByBroker.Select(g => g.Key)
                .Union(revenueByBroker.Keys)
                .OrderBy(b => b)
                .Select(broker =>
                {
                    var classes = classesByBroker[broker].ToList();
                    var revenue = revenueByBroker.GetValueOrDefault(broker);
                    var variableFee = classes.Sum(s => s.TotalFee);
                    var fixedFee = revenue?.FixedFee ?? 0m;
                    var floorAdjustment = revenue?.FloorAdjustment ?? 0m;
                    var marketDataFee = revenue?.MarketDataFee ?? 0m;

                    return new
                    {
                        Broker = broker,
                        AssetClasses = classes.Select(s => new
                        {
                            s.AssetClass,
                            s.TotalNotional,
                            s.TotalQty,
                            s.TotalFee,
                            s.TraderCount,
                            s.TradingDays
                        }),
                        VariableFee = variableFee,
                        FixedFee = fixedFee,
                        FloorAdjustment = floorAdjustment,
                        MarketDataFee = marketDataFee,
                        TotalFee = variableFee + fixedFee + floorAdjustment + marketDataFee
                    };
                })
                .ToList();

            return Results.Ok(new
            {
                Year = monthDate.Year,
                Month = monthDate.Month,
                Brokers = brokers,
                Summary = new
                {
                    TotalFee = brokers.Sum(b => b.TotalFee),
                    VariableFee = brokers.Sum(b => b.VariableFee),
                    FixedFee = brokers.Sum(b => b.FixedFee),
                    FloorAdjustment = brokers.Sum(b => b.FloorAdjustment),
                    MarketDataFee = brokers.Sum(b => b.MarketDataFee),
                    TotalNotional = summaries.Sum(s => s.TotalNotional),
                    TotalQty = summaries.Sum(s => s.TotalQty),
                    BrokerCount = brokers.Count
                }
            });
        });

        reportGroup.MapGet("/billing/anual/{ano}", async (
            int ano,
            DailyBillingRepository repository,
            RevenueReportService revenueService,
            CancellationToken ct) =>
        {
            if (ano < 2000 || ano > 2100)
                return Results.BadRequest("Ano inválido.");

            var summariesTask = repository.GetYearSummaryAsync(ano, ct);
            var revenueTask = revenueService.GetYearAsync(ano, ct);

            await Task.WhenAll(summariesTask, revenueTask);

            var summaries = await summariesTask;
            var classesByBrokerMonth = summaries.ToLookup(s => (s.Broker, s.Month));
            var revenueByBrokerMonth = (await revenueTask).ToDictionary(r => (r.Broker, r.Month));

            var keys = classesByBrokerMonth.Select(g => g.Key)
                .Union(revenueByBrokerMonth.Keys)
                .ToList();

            var brokers = keys
                .GroupBy(k => k.Broker)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var months = g.OrderBy(k => k.Month).Select(key =>
                    {
                        var classes = classesByBrokerMonth[key].ToList();
                        var revenue = revenueByBrokerMonth.GetValueOrDefault(key);
                        var variableFee = classes.Sum(s => s.TotalFee);
                        var fixedFee = revenue?.FixedFee ?? 0m;
                        var floorAdjustment = revenue?.FloorAdjustment ?? 0m;
                        var marketDataFee = revenue?.MarketDataFee ?? 0m;

                        return new
                        {
                            key.Month,
                            AssetClasses = classes.Select(s => new
                            {
                                s.AssetClass,
                                s.TotalNotional,
                                s.TotalQty,
                                s.TotalFee
                            }),
                            VariableFee = variableFee,
                            FixedFee = fixedFee,
                            FloorAdjustment = floorAdjustment,
                            MarketDataFee = marketDataFee,
                            TotalFee = variableFee + fixedFee + floorAdjustment + marketDataFee
                        };
                    }).ToList();

                    return new
                    {
                        Broker = g.Key,
                        MonthlyBreakdown = months,
                        VariableFee = months.Sum(m => m.VariableFee),
                        FixedFee = months.Sum(m => m.FixedFee),
                        FloorAdjustment = months.Sum(m => m.FloorAdjustment),
                        MarketDataFee = months.Sum(m => m.MarketDataFee),
                        TotalFee = months.Sum(m => m.TotalFee)
                    };
                })
                .ToList();

            return Results.Ok(new
            {
                Year = ano,
                Brokers = brokers,
                Summary = new
                {
                    TotalFee = brokers.Sum(b => b.TotalFee),
                    VariableFee = brokers.Sum(b => b.VariableFee),
                    FixedFee = brokers.Sum(b => b.FixedFee),
                    FloorAdjustment = brokers.Sum(b => b.FloorAdjustment),
                    MarketDataFee = brokers.Sum(b => b.MarketDataFee),
                    TotalNotional = summaries.Sum(s => s.TotalNotional),
                    TotalQty = summaries.Sum(s => s.TotalQty),
                    BrokerCount = brokers.Count,
                    MonthsWithData = keys.Select(k => k.Month).Distinct().Count()
                }
            });
        });

        reportGroup.MapGet("/dashboard", async (
            DailyBillingRepository billingRepository,
            BrokerRepository brokerRepository,
            RevenueReportService revenueService,
            CancellationToken ct) =>
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var prevMonth = today.Month == 1 ? 12 : today.Month - 1;
            var prevYear = today.Month == 1 ? today.Year - 1 : today.Year;

            var yearTask = billingRepository.GetYearSummaryAsync(today.Year, ct);
            var monthTask = billingRepository.GetMonthSummaryAsync(today.Year, today.Month, ct);
            var brokersTask = brokerRepository.GetAllAsync(ct);
            var yearRevenueTask = revenueService.GetYearAsync(today.Year, ct);
            // Em janeiro o mês anterior é do ano anterior
            var prevMonthRevenueTask = prevYear == today.Year
                ? Task.FromResult(new List<BrokerMonthRevenue>())
                : revenueService.GetMonthAsync(prevYear, prevMonth, ct);

            await Task.WhenAll(yearTask, monthTask, brokersTask, yearRevenueTask, prevMonthRevenueTask);

            var yearData = await yearTask;
            var monthData = await monthTask;
            var brokers = (await brokersTask).ToDictionary(b => b.Code, b => b.Name ?? $"Broker {b.Code}");
            var yearRevenue = await yearRevenueTask;
            var prevMonthRevenue = prevYear == today.Year
                ? yearRevenue.Where(r => r.Month == prevMonth).ToList()
                : await prevMonthRevenueTask;
            var monthRevenue = yearRevenue.Where(r => r.Month == today.Month).ToList();

            var receitaYTD = yearRevenue.Sum(r => r.TotalFee);
            var receitaMTD = monthRevenue.Sum(r => r.TotalFee);
            var receitaMesAnterior = prevMonthRevenue.Sum(r => r.TotalFee);

            var volumeByMonth = yearData.ToLookup(s => s.Month);
            var revenueByMonth = yearRevenue.ToLookup(r => r.Month);
            var receitaMensal = volumeByMonth.Select(g => g.Key)
                .Union(revenueByMonth.Select(g => g.Key))
                .OrderBy(m => m)
                .Select(month => new
                {
                    Month = month,
                    TotalFee = revenueByMonth[month].Sum(r => r.TotalFee),
                    VariableFee = revenueByMonth[month].Sum(r => r.VariableFee),
                    FixedFee = revenueByMonth[month].Sum(r => r.FixedFee),
                    FloorAdjustment = revenueByMonth[month].Sum(r => r.FloorAdjustment),
                    MarketDataFee = revenueByMonth[month].Sum(r => r.MarketDataFee),
                    TotalNotional = volumeByMonth[month].Sum(s => s.TotalNotional),
                    TotalQty = volumeByMonth[month].Sum(s => s.TotalQty)
                });

            var volumeByBroker = monthData.ToLookup(s => s.Broker);
            var revenueByBroker = monthRevenue.ToDictionary(r => r.Broker);
            var volumeCorretoras = volumeByBroker.Select(g => g.Key)
                .Union(revenueByBroker.Keys)
                .Select(broker => new
                {
                    Broker = broker,
                    Name = brokers.TryGetValue(broker, out var name) ? name : $"Broker {broker}",
                    TotalFee = revenueByBroker.GetValueOrDefault(broker)?.TotalFee ?? 0m,
                    TotalNotional = volumeByBroker[broker].Sum(s => s.TotalNotional),
                    TotalQty = volumeByBroker[broker].Sum(s => s.TotalQty)
                })
                .OrderByDescending(b => b.TotalFee);

            return Results.Ok(new
            {
                ReceitaYTD = receitaYTD,
                ReceitaMTD = receitaMTD,
                ReceitaMesAnterior = receitaMesAnterior,
                TradersAtivos = monthData.Sum(s => s.TraderCount),
                ReceitaMensal = receitaMensal,
                VolumeCorretoras = volumeCorretoras
            });
        });

        return reportGroup;
    }
}
