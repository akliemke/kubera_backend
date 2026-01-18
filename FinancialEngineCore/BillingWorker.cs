using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;

namespace FinancialEngineCore;

public class BillingWorker : BackgroundService
{
    private readonly BillingRepository _billingRepository;
    private readonly OrdersRepository _orderRepository;
    private readonly ILogger<BillingWorker> _logger;

    public BillingWorker(ILogger<BillingWorker> logger, BillingRepository billingRepository, OrdersRepository orderRepository)
    {
        _billingRepository = billingRepository;
        _orderRepository = orderRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                var billingsToProcess = await _billingRepository.GetAll(stoppingToken);
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
            var orders = await _orderRepository.GetOrders(billing.ProcessDate.ToString("yyyy-MM-dd"), stoppingToken);
            
            // get faixas
            // get past calcs
            // calculate
            // insert records to track calcs
            // finalize



            // Simulate processing logic
            billing.Status = "Processed";
            _billingRepository.Update(billing);
            _logger.LogInformation("Processed billing record with ID: {id}", billing.ProcessId);
        }

        await Task.CompletedTask;
    }
}
