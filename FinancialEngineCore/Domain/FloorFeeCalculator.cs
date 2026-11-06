using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Domain;

/// <summary>
/// Ajuste de piso de uma entity no mês: quanto falta para o fee calculado atingir o piso.
/// </summary>
public record FloorFeeAdjustment(string Entity, decimal FloorFee, decimal CalculatedFee, decimal Adjustment);

/// <summary>
/// Calcula o ajuste de piso mensal (FloorFee) por entity de um broker.
/// Fonte única para PDF e relatórios, para que todos mostrem a mesma receita.
/// </summary>
public static class FloorFeeCalculator
{
    /// <param name="monthSummaries">Fee do mês por BillingKey (Trader + KeyType) de um broker</param>
    /// <param name="entityConfigs">Configs de entity do broker válidas no mês</param>
    public static List<FloorFeeAdjustment> Calculate(
        IEnumerable<TraderMonthSummary> monthSummaries,
        IEnumerable<EntityBillingConfig> entityConfigs)
    {
        // Só chaves ENTITY: um trader com o mesmo nome da entity não conta no piso dela
        var feeByKey = monthSummaries
            .Where(s => s.KeyType == BillingKeyTypes.Entity)
            .GroupBy(s => s.Trader, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.TotalFee), StringComparer.OrdinalIgnoreCase);

        // Mais de uma config da mesma entity no mês (troca de vigência): vale a mais recente
        var floorByEntity = entityConfigs
            .Where(c => c.FloorFee is > 0)
            .GroupBy(c => c.Entity, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(c => c.ValidFrom).First());

        var adjustments = new List<FloorFeeAdjustment>();
        foreach (var config in floorByEntity)
        {
            var floor = config.FloorFee!.Value;
            var entityFee = feeByKey.GetValueOrDefault(config.Entity, 0m);

            if (entityFee < floor)
                adjustments.Add(new FloorFeeAdjustment(config.Entity, floor, entityFee, floor - entityFee));
        }

        return adjustments;
    }
}
