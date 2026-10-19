using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Domain;

/// <summary>
/// Receita de um broker num mês, com a mesma composição do PDF mensal:
/// fee variável (EQUITY+BMF) + cobranças fixas + ajuste de piso + Market Data.
/// </summary>
public record BrokerMonthRevenue(
    int Broker, int Month, decimal VariableFee, decimal FixedFee, decimal FloorAdjustment, decimal MarketDataFee)
{
    public decimal TotalFee => VariableFee + FixedFee + FloorAdjustment + MarketDataFee;
}

/// <summary>
/// Calcula a receita por (Broker, Mês) de um ano. Fonte única para relatórios e dashboard,
/// para que todos batam com o total do PDF mensal.
/// </summary>
public static class MonthlyRevenueCalculator
{
    /// <param name="year">Ano dos meses calculados</param>
    /// <param name="months">Meses a calcular (1-12)</param>
    /// <param name="feeByKeyAndMonth">Fee por (Broker, BillingKey, Month) do ano</param>
    /// <param name="entityConfigs">Configs de entity (todos os brokers) que tocam o período</param>
    /// <param name="fixedFees">Cobranças fixas (todos os brokers) que tocam o período</param>
    /// <param name="marketDataBilling">Apuração de Market Data (todos os brokers) dos meses do período</param>
    public static List<BrokerMonthRevenue> Calculate(
        int year,
        IEnumerable<int> months,
        IEnumerable<TraderMonthSummary> feeByKeyAndMonth,
        IEnumerable<EntityBillingConfig> entityConfigs,
        IEnumerable<FixedMonthlyFee> fixedFees,
        IEnumerable<MarketDataBilling> marketDataBilling)
    {
        var feeLookup = feeByKeyAndMonth.ToLookup(f => (f.Broker, f.Month));
        var marketDataLookup = marketDataBilling.Where(m => m.Year == year).ToLookup(m => (m.Broker, m.Month));
        var configList = entityConfigs.ToList();
        var fixedList = fixedFees.ToList();

        var revenues = new List<BrokerMonthRevenue>();
        foreach (var month in months.Distinct().OrderBy(m => m))
        {
            var firstDay = new DateOnly(year, month, 1);
            var lastDay = firstDay.AddMonths(1).AddDays(-1);

            // Mesmo critério de vigência do PDF: config/cobrança vale no mês se toca o mês
            bool ValidInMonth(DateOnly from, DateOnly to) => from <= lastDay && to >= firstDay;

            var monthFixed = fixedList.Where(f => ValidInMonth(f.ValidFrom, f.ValidTo)).ToList();
            var monthConfigs = configList.Where(c => ValidInMonth(c.ValidFrom, c.ValidTo)).ToList();

            // Mesmo critério do PDF: broker entra se teve billing variável, cobrança fixa ou Market Data no mês
            var brokers = feeLookup.Where(g => g.Key.Month == month).Select(g => g.Key.Broker)
                .Union(monthFixed.Select(f => f.Broker))
                .Union(marketDataLookup.Where(g => g.Key.Month == month).Select(g => g.Key.Broker))
                .Distinct()
                .OrderBy(b => b);

            foreach (var broker in brokers)
            {
                var keyFees = feeLookup[(broker, month)].ToList();
                var floor = FloorFeeCalculator
                    .Calculate(keyFees, monthConfigs.Where(c => c.Broker == broker))
                    .Sum(a => a.Adjustment);

                revenues.Add(new BrokerMonthRevenue(
                    broker,
                    month,
                    VariableFee: keyFees.Sum(f => f.TotalFee),
                    FixedFee: monthFixed.Where(f => f.Broker == broker).Sum(f => f.Amount),
                    FloorAdjustment: floor,
                    MarketDataFee: marketDataLookup[(broker, month)].Sum(m => m.Amount)));
            }
        }

        return revenues;
    }
}
