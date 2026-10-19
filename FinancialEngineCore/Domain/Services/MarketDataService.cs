using FinancialEngineCore.Infra;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FinancialEngineCore.Domain.Services;

/// <summary>
/// Snapshot dos vínculos usuário ↔ broker e apuração mensal da cobrança de Market Data.
/// </summary>
public class MarketDataService
{
    private readonly UserExchangeAccountApiClient _apiClient;
    private readonly UserExchangeAccountSnapshotRepository _snapshotRepository;
    private readonly MarketDataFeeConfigRepository _configRepository;
    private readonly MarketDataExclusionRepository _exclusionRepository;
    private readonly MarketDataBillingRepository _billingRepository;
    private readonly DailyBillingRepository _dailyBillingRepository;
    private readonly DbConnectionFactory _dbConnectionFactory;
    private readonly ILogger<MarketDataService> _logger;

    public MarketDataService(
        UserExchangeAccountApiClient apiClient,
        UserExchangeAccountSnapshotRepository snapshotRepository,
        MarketDataFeeConfigRepository configRepository,
        MarketDataExclusionRepository exclusionRepository,
        MarketDataBillingRepository billingRepository,
        DailyBillingRepository dailyBillingRepository,
        DbConnectionFactory dbConnectionFactory,
        ILogger<MarketDataService> logger)
    {
        _apiClient = apiClient;
        _snapshotRepository = snapshotRepository;
        _configRepository = configRepository;
        _exclusionRepository = exclusionRepository;
        _billingRepository = billingRepository;
        _dailyBillingRepository = dailyBillingRepository;
        _dbConnectionFactory = dbConnectionFactory;
        _logger = logger;
    }

    public bool IsApiConfigured => _apiClient.IsConfigured;

    /// <summary>
    /// Busca os vínculos atuais na API, grava o snapshot do dia e recalcula a Market Data do mês
    /// corrente. No dia 1 também recalcula o mês anterior, para fechá-lo com todos os snapshots.
    /// </summary>
    public async Task<int> SyncAsync(DateOnly today, CancellationToken ct)
    {
        var accounts = await _apiClient.GetActiveAccountsAsync(ct);
        await _snapshotRepository.ReplaceSnapshotAsync(today, accounts, ct);

        _logger.LogInformation("User exchange account snapshot for {Date}: {Count} accounts", today, accounts.Count);

        if (today.Day == 1)
        {
            var previous = today.AddMonths(-1);
            await RecalculateMonthAsync(previous.Year, previous.Month, null, ct);
        }
        await RecalculateMonthAsync(today.Year, today.Month, null, ct);

        return accounts.Count;
    }

    /// <summary>
    /// Recalcula a Market Data de um mês para os brokers informados (ou todos com snapshot/apuração no mês).
    /// Cada broker em sua transação, sob o mesmo lock do processamento de billing.
    /// </summary>
    public async Task<MarketDataResult> RecalculateMonthAsync(int year, int month, IReadOnlyCollection<int>? brokers, CancellationToken ct)
    {
        var accounts = await _snapshotRepository.GetAccountsInMonthAsync(year, month, null, ct);
        var existing = await _billingRepository.GetByPeriodAsync(year, month, month, null, ct);

        // Inclui brokers que já tinham apuração: se todos os usuários foram excluídos, a apuração é zerada
        var targetBrokers = brokers is { Count: > 0 }
            ? brokers.Distinct().ToList()
            : accounts.Select(a => a.Broker).Union(existing.Select(e => e.Broker)).Distinct().ToList();

        var charges = new List<MarketDataCharge>();
        var excluded = new List<Model.UserExchangeAccount>();
        var withoutConfig = new List<int>();

        foreach (var broker in targetBrokers.OrderBy(b => b))
        {
            await using var conn = await _dbConnectionFactory.CreateOpenNpgsqlConnectionAsync(ct);
            await using var txn = await conn.BeginTransactionAsync(ct);

            await _dailyBillingRepository.AcquireBrokerLockAsync(broker, conn, txn);
            var result = await RecalculateBrokerMonthAsync(broker, year, month, conn, txn, ct);
            await txn.CommitAsync(ct);

            charges.AddRange(result.Charges);
            excluded.AddRange(result.Excluded);
            withoutConfig.AddRange(result.BrokersWithoutConfig);
        }

        return new MarketDataResult(charges, excluded, withoutConfig);
    }

    /// <summary>
    /// Recalcula e grava a Market Data de um broker no mês, na transação informada.
    /// Chamado ao final do processamento de billing do broker (cobrança adicionada automaticamente).
    /// </summary>
    public async Task<MarketDataResult> RecalculateBrokerMonthAsync(
        int brokerId, int year, int month, NpgsqlConnection conn, NpgsqlTransaction txn, CancellationToken ct)
    {
        var firstDay = new DateOnly(year, month, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);

        var accountsTask = _snapshotRepository.GetAccountsInMonthAsync(year, month, brokerId, ct);
        var exclusionsTask = _exclusionRepository.GetAllActiveAsync(ct);
        var configsTask = _configRepository.GetByPeriodAsync(firstDay, lastDay, brokerId, ct);

        await Task.WhenAll(accountsTask, exclusionsTask, configsTask);

        var result = MarketDataFeeCalculator.Calculate(
            year, month, await accountsTask, await exclusionsTask, await configsTask);

        if (result.BrokersWithoutConfig.Count > 0)
        {
            _logger.LogWarning(
                "Broker {BrokerId} has linked accounts in {Year}-{Month:00} but no Market Data price configured: not charged",
                brokerId, year, month);
        }

        await _billingRepository.ReplaceMonthAsync(brokerId, year, month, result.Charges, conn, txn, ct);

        _logger.LogInformation(
            "Market Data {BrokerId} {Year}-{Month:00}: {Charged} users charged, {Excluded} excluded, total {Total}",
            brokerId, year, month, result.Charges.Count, result.Excluded.Count, result.Charges.Sum(c => c.Amount));

        return result;
    }
}
