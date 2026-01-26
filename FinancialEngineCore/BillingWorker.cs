using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;

namespace FinancialEngineCore;

public class BillingWorker : BackgroundService
{
    private readonly ProcessBillingRepository _billingRepository;
    private readonly OrdersRepository _orderRepository;
    private readonly FeeBracketRepository _feeBracketRepository;
    private readonly ILogger<BillingWorker> _logger;

    public BillingWorker(
        ILogger<BillingWorker> logger,
        ProcessBillingRepository billingRepository,
        OrdersRepository orderRepository,
        FeeBracketRepository feeBracketRepository)
    {
        _billingRepository = billingRepository;
        _orderRepository = orderRepository;
        _feeBracketRepository = feeBracketRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                var billingsToProcess = await _billingRepository.GetAllAsync(stoppingToken);
                await ProcessBillingRecords(billingsToProcess, stoppingToken);
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }
            await Task.Delay(1000, stoppingToken);
        }
    }


    private async Task ProcessBillingRecords(List<ProcessBilling> billings, CancellationToken stoppingToken)
    {
        foreach (var billing in billings)
        {
            var orders = await _orderRepository.GetOrdersAsync(billing.ProcessDate.ToString("yyyy-MM-dd"), stoppingToken);

            // get faixas
            var brackets = await _feeBracketRepository.GetAllAsync(stoppingToken);
            // get past calcs
            // calculate
            // insert records to track calcs
            // finalize



            // Simulate processing logic
            billing.Status = "Processed";
            await _billingRepository.UpdateAsync(billing, stoppingToken);
            _logger.LogInformation("Processed billing record with ID: {id}", billing.ProcessId);
        }

        await Task.CompletedTask;
    }
}
