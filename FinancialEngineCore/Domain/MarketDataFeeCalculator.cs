using FinancialEngineCore.Model;

namespace FinancialEngineCore.Domain;

/// <summary>Cobrança de Market Data de um usuário no mês.</summary>
public record MarketDataCharge(int Broker, string Trader, decimal Amount, long ConfigId);

/// <summary>
/// Resultado da apuração de Market Data de um mês.
/// </summary>
/// <param name="Charges">Usuários cobrados (um por (Broker, Trader))</param>
/// <param name="Excluded">Usuários vinculados mas não cobrados por estarem na lista de exclusão</param>
/// <param name="BrokersWithoutConfig">Brokers com usuários vinculados mas sem preço válido no mês</param>
public record MarketDataResult(
    List<MarketDataCharge> Charges,
    List<UserExchangeAccount> Excluded,
    List<int> BrokersWithoutConfig);

/// <summary>
/// Apura a cobrança mensal de Market Data: valor fixo por usuário com conta vinculada ao broker
/// em qualquer dia do mês, menos os usuários excluídos.
/// </summary>
public static class MarketDataFeeCalculator
{
    /// <param name="accountsInMonth">Vínculos vistos em algum snapshot do mês (pode ter repetição/casing diferente)</param>
    /// <param name="exclusions">Exclusões (Broker NULL = todos os brokers)</param>
    /// <param name="configs">Preços por broker</param>
    public static MarketDataResult Calculate(
        int year,
        int month,
        IEnumerable<UserExchangeAccount> accountsInMonth,
        IEnumerable<MarketDataExclusion> exclusions,
        IEnumerable<MarketDataFeeConfig> configs)
    {
        var firstDay = new DateOnly(year, month, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);

        // Usuário conta uma vez por broker, independente de quantos dias apareceu ou da grafia
        var accounts = accountsInMonth
            .DistinctBy(a => (a.Broker, a.Trader.ToUpperInvariant()))
            .ToList();

        var activeExclusions = exclusions.Where(e => e.IsActive).ToList();
        bool IsExcluded(UserExchangeAccount account) => activeExclusions.Any(e =>
            (e.Broker is null || e.Broker == account.Broker)
            && e.Trader.Equals(account.Trader, StringComparison.OrdinalIgnoreCase));

        // Preço válido no mês (vigência que toca o mês); mais de um: vale o ValidFrom mais recente
        var configByBroker = configs
            .Where(c => c.IsActive && c.ValidFrom <= lastDay && c.ValidTo >= firstDay)
            .GroupBy(c => c.Broker)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.ValidFrom).First());

        var charges = new List<MarketDataCharge>();
        var excluded = new List<UserExchangeAccount>();
        var brokersWithoutConfig = new SortedSet<int>();

        foreach (var account in accounts.OrderBy(a => a.Broker).ThenBy(a => a.Trader, StringComparer.OrdinalIgnoreCase))
        {
            if (IsExcluded(account))
            {
                excluded.Add(account);
                continue;
            }

            if (!configByBroker.TryGetValue(account.Broker, out var config))
            {
                brokersWithoutConfig.Add(account.Broker);
                continue;
            }

            charges.Add(new MarketDataCharge(account.Broker, account.Trader, config.AmountPerUser, config.Id));
        }

        return new MarketDataResult(charges, excluded, brokersWithoutConfig.ToList());
    }
}
