using FinancialEngineCore.Domain;
using FinancialEngineCore.Infra;

namespace KuberaWebApp.Services;

/// <summary>
/// Carrega os dados e calcula a receita completa por (Broker, Mês):
/// variável + cobranças fixas + ajuste de piso + Market Data (mesma composição do PDF mensal).
/// </summary>
public class RevenueReportService(
    DailyBillingRepository dailyBillingRepository,
    EntityBillingConfigRepository entityBillingConfigRepository,
    FixedMonthlyFeeRepository fixedMonthlyFeeRepository,
    MarketDataBillingRepository marketDataBillingRepository)
{
    /// <summary>
    /// Receita dos meses informados de um ano. Meses futuros são ignorados
    /// (cobrança fixa só é devida a partir do mês corrente).
    /// </summary>
    public async Task<List<BrokerMonthRevenue>> GetAsync(int year, IEnumerable<int> months, CancellationToken ct)
    {
        var currentMonthStart = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        var monthList = months
            .Where(m => new DateOnly(year, m, 1) <= currentMonthStart)
            .Distinct()
            .ToList();

        if (monthList.Count == 0)
            return [];

        var from = new DateOnly(year, monthList.Min(), 1);
        var toExclusive = new DateOnly(year, monthList.Max(), 1).AddMonths(1);
        var lastDay = toExclusive.AddDays(-1);

        var feeTask = dailyBillingRepository.GetFeeByBillingKeyAndMonthAsync(from, toExclusive, ct);
        var entityConfigsTask = entityBillingConfigRepository.GetByPeriodAsync(from, lastDay, ct);
        var fixedFeesTask = fixedMonthlyFeeRepository.GetByPeriodAsync(from, lastDay, ct);
        var marketDataTask = marketDataBillingRepository.GetByPeriodAsync(year, monthList.Min(), monthList.Max(), null, ct);

        await Task.WhenAll(feeTask, entityConfigsTask, fixedFeesTask, marketDataTask);

        return MonthlyRevenueCalculator.Calculate(
            year, monthList, await feeTask, await entityConfigsTask, await fixedFeesTask, await marketDataTask);
    }

    public Task<List<BrokerMonthRevenue>> GetMonthAsync(int year, int month, CancellationToken ct)
        => GetAsync(year, [month], ct);

    public Task<List<BrokerMonthRevenue>> GetYearAsync(int year, CancellationToken ct)
        => GetAsync(year, Enumerable.Range(1, 12), ct);
}
