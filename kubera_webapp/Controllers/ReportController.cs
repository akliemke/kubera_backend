using FinancialEngineCore.Infra;
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

        reportGroup.MapGet("/billing/month/{ano_mes}", async (
            string ano_mes,
            DailyBillingRepository repository,
            CancellationToken ct) =>
        {
            // Expected format: yyyy-MM (e.g., 2024-01)
            if (!DateTime.TryParseExact(ano_mes + "-01", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var monthDate))
            {
                return Results.BadRequest("Formato inválido. Use yyyy-MM (ex: 2024-01).");
            }

            var summaries = await repository.GetMonthSummaryAsync(monthDate.Year, monthDate.Month, ct);

            return Results.Ok(new
            {
                Year = monthDate.Year,
                Month = monthDate.Month,
                Brokers = summaries.GroupBy(s => s.Broker).Select(g => new
                {
                    Broker = g.Key,
                    AssetClasses = g.Select(s => new
                    {
                        s.AssetClass,
                        s.TotalNotional,
                        s.TotalQty,
                        s.TotalFee,
                        s.TraderCount,
                        s.TradingDays
                    }),
                    TotalFee = g.Sum(s => s.TotalFee)
                }),
                Summary = new
                {
                    TotalFee = summaries.Sum(s => s.TotalFee),
                    TotalNotional = summaries.Sum(s => s.TotalNotional),
                    TotalQty = summaries.Sum(s => s.TotalQty),
                    BrokerCount = summaries.Select(s => s.Broker).Distinct().Count()
                }
            });
        });

        reportGroup.MapGet("/billing/anual/{ano}", async (
            int ano,
            DailyBillingRepository repository,
            CancellationToken ct) =>
        {
            if (ano < 2000 || ano > 2100)
                return Results.BadRequest("Ano inválido.");

            var summaries = await repository.GetYearSummaryAsync(ano, ct);

            return Results.Ok(new
            {
                Year = ano,
                Brokers = summaries.GroupBy(s => s.Broker).Select(g => new
                {
                    Broker = g.Key,
                    MonthlyBreakdown = g.GroupBy(s => s.Month).Select(m => new
                    {
                        Month = m.Key,
                        AssetClasses = m.Select(s => new
                        {
                            s.AssetClass,
                            s.TotalNotional,
                            s.TotalQty,
                            s.TotalFee
                        }),
                        TotalFee = m.Sum(s => s.TotalFee)
                    }),
                    TotalFee = g.Sum(s => s.TotalFee)
                }),
                Summary = new
                {
                    TotalFee = summaries.Sum(s => s.TotalFee),
                    TotalNotional = summaries.Sum(s => s.TotalNotional),
                    TotalQty = summaries.Sum(s => s.TotalQty),
                    BrokerCount = summaries.Select(s => s.Broker).Distinct().Count(),
                    MonthsWithData = summaries.Select(s => s.Month).Distinct().Count()
                }
            });
        });

        reportGroup.MapGet("/dashboard", async (
            DailyBillingRepository billingRepository,
            BrokerRepository brokerRepository,
            CancellationToken ct) =>
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var prevMonth = today.Month == 1 ? 12 : today.Month - 1;
            var prevYear = today.Month == 1 ? today.Year - 1 : today.Year;

            var yearTask = billingRepository.GetYearSummaryAsync(today.Year, ct);
            var monthTask = billingRepository.GetMonthSummaryAsync(today.Year, today.Month, ct);
            var prevMonthTask = billingRepository.GetMonthSummaryAsync(prevYear, prevMonth, ct);
            var brokersTask = brokerRepository.GetAllAsync(ct);

            await Task.WhenAll(yearTask, monthTask, prevMonthTask, brokersTask);

            var yearData = await yearTask;
            var monthData = await monthTask;
            var prevMonthData = await prevMonthTask;
            var brokers = (await brokersTask).ToDictionary(b => b.Code, b => b.Name ?? $"Broker {b.Code}");

            var receitaYTD = yearData.Sum(s => s.TotalFee);
            var receitaMTD = monthData.Sum(s => s.TotalFee);
            var receitaMesAnterior = prevMonthData.Sum(s => s.TotalFee);

            var receitaMensal = yearData
                .GroupBy(s => s.Month)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    Month = g.Key,
                    TotalFee = g.Sum(s => s.TotalFee),
                    TotalNotional = g.Sum(s => s.TotalNotional),
                    TotalQty = g.Sum(s => s.TotalQty)
                });

            var volumeCorretoras = monthData
                .GroupBy(s => s.Broker)
                .Select(g => new
                {
                    Broker = g.Key,
                    Name = brokers.TryGetValue(g.Key, out var name) ? name : $"Broker {g.Key}",
                    TotalFee = g.Sum(s => s.TotalFee),
                    TotalNotional = g.Sum(s => s.TotalNotional),
                    TotalQty = g.Sum(s => s.TotalQty)
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
