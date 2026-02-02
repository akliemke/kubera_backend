using FinancialEngineCore.Domain.Services;
using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;

namespace FinancialEngineCore;

/// <summary>
/// Background worker that polls for pending billing jobs and processes them.
/// Delegates actual processing to IBillingProcessingService.
/// </summary>
public class BillingWorker : BackgroundService
{
    private readonly ProcessBillingRepository _processBillingRepository;
    private readonly IBillingProcessingService _billingProcessingService;
    private readonly ILogger<BillingWorker> _logger;

    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(1);
    private readonly TimeSpan _errorRetryDelay = TimeSpan.FromSeconds(5);

    public BillingWorker(
        ILogger<BillingWorker> logger,
        ProcessBillingRepository processBillingRepository,
        IBillingProcessingService billingProcessingService)
    {
        _logger = logger;
        _processBillingRepository = processBillingRepository;
        _billingProcessingService = billingProcessingService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BillingWorker starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingBillingsAsync(stoppingToken);
                await Task.Delay(_pollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in BillingWorker main loop");
                await Task.Delay(_errorRetryDelay, stoppingToken);
            }
        }

        _logger.LogInformation("BillingWorker stopped.");
    }

    private async Task ProcessPendingBillingsAsync(CancellationToken ct)
    {
        // Try to acquire a single pending billing with lock
        // Loop until no more pending billings are available
        while (true)
        {
            var billing = await _processBillingRepository.TryAcquireNextAsync(ct);
            if (billing == null)
                break;

            await ProcessSingleBillingAsync(billing, ct);
        }
    }

    private async Task ProcessSingleBillingAsync(ProcessBilling billing, CancellationToken ct)
    {
        try
        {
            await _billingProcessingService.ProcessBillingAsync(billing, ct);

            billing.Status = "Processed";
            await _processBillingRepository.UpdateAsync(billing, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing billing {ProcessId}", billing.ProcessId);

            billing.Status = "Error";
            await _processBillingRepository.UpdateAsync(billing, ct);
        }
    }
}
