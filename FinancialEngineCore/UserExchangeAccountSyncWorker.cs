using FinancialEngineCore.Domain.Services;
using FinancialEngineCore.Infra;

namespace FinancialEngineCore;

/// <summary>
/// Tira o snapshot diário dos vínculos usuário ↔ broker (API externa) usado na cobrança de Market Data.
/// Roda uma vez por dia a partir de UserExchangeAccountsApi:SyncHourLocal; se falhar, tenta de novo no próximo ciclo.
/// </summary>
public class UserExchangeAccountSyncWorker : BackgroundService
{
    private readonly MarketDataService _marketDataService;
    private readonly UserExchangeAccountSnapshotRepository _snapshotRepository;
    private readonly ILogger<UserExchangeAccountSyncWorker> _logger;
    private readonly int _syncHourLocal;

    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(30);

    public UserExchangeAccountSyncWorker(
        MarketDataService marketDataService,
        UserExchangeAccountSnapshotRepository snapshotRepository,
        IConfiguration configuration,
        ILogger<UserExchangeAccountSyncWorker> logger)
    {
        _marketDataService = marketDataService;
        _snapshotRepository = snapshotRepository;
        _logger = logger;
        _syncHourLocal = configuration.GetValue("UserExchangeAccountsApi:SyncHourLocal", 6);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_marketDataService.IsApiConfigured)
        {
            _logger.LogWarning("UserExchangeAccountsApi:BaseUrl not configured: Market Data account sync disabled");
            return;
        }

        _logger.LogInformation("UserExchangeAccountSyncWorker starting (daily sync from {Hour}h local)", _syncHourLocal);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;
                var today = DateOnly.FromDateTime(now);

                if (now.Hour >= _syncHourLocal && !await _snapshotRepository.HasSnapshotAsync(today, stoppingToken))
                    await _marketDataService.SyncAsync(today, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "User exchange account sync failed; will retry in {Interval}", _checkInterval);
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("UserExchangeAccountSyncWorker stopped.");
    }
}
