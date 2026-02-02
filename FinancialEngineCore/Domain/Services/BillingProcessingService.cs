using FinancialEngineCore.Domain.Strategies;
using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;
using Microsoft.Extensions.Logging;

namespace FinancialEngineCore.Domain.Services;

/// <summary>
/// Implements billing processing logic.
/// Coordinates order retrieval, calculation (via strategies), and persistence.
/// </summary>
public class BillingProcessingService : IBillingProcessingService
{
    private readonly OrdersRepository _orderRepository;
    private readonly DailyBillingRepository _dailyBillingRepository;
    private readonly OrderBillingRepository _orderBillingRepository;
    private readonly CalculationStrategyRegistry _strategyRegistry;
    private readonly ILogger<BillingProcessingService> _logger;

    public BillingProcessingService(
        OrdersRepository orderRepository,
        DailyBillingRepository dailyBillingRepository,
        OrderBillingRepository orderBillingRepository,
        CalculationStrategyRegistry strategyRegistry,
        ILogger<BillingProcessingService> logger)
    {
        _orderRepository = orderRepository;
        _dailyBillingRepository = dailyBillingRepository;
        _orderBillingRepository = orderBillingRepository;
        _strategyRegistry = strategyRegistry;
        _logger = logger;
    }

    public async Task ProcessBillingAsync(ProcessBilling billing, CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing billing {ProcessId} for date {Date}, brokers: {Brokers}",
            billing.ProcessId, billing.ProcessDate, billing.ProcessBroker);

        var brokerIds = ParseBrokerIds(billing.ProcessBroker);

        foreach (var brokerId in brokerIds)
        {
            await ProcessBrokerAsync(brokerId, billing.ProcessDate, billing.ProcessId, ct);
        }

        _logger.LogInformation("Completed billing {ProcessId}", billing.ProcessId);
    }

    public async Task ProcessBrokerAsync(int brokerId, DateTime tradeDate, int processBillingId, CancellationToken ct)
    {
        _logger.LogDebug("Processing broker {BrokerId} for date {Date}", brokerId, tradeDate);

        // Get all orders for this broker/date
        var allOrders = await _orderRepository.GetOrdersAsync(tradeDate.ToString("yyyy-MM-dd"), ct);
        var brokerOrders = allOrders.Where(o => o.EnteringFirm == brokerId.ToString()).ToList();

        if (brokerOrders.Count == 0)
        {
            _logger.LogDebug("No orders for broker {BrokerId} on {Date}", brokerId, tradeDate);
            return;
        }

        // Delete existing billing for reprocessing
        await _orderBillingRepository.DeleteByDateAsync(brokerId, tradeDate, ct);
        await _dailyBillingRepository.DeleteByDateAsync(brokerId, tradeDate, ct);

        // Group by (Trader, AssetClass)
        var groups = brokerOrders
            .GroupBy(o => new { o.EnteringTrader, o.AssetClass })
            .ToList();

        foreach (var group in groups)
        {
            var orders = group.ToList();
            var trader = group.Key.EnteringTrader;
            var assetClass = group.Key.AssetClass;

            var result = await CalculateAsync(brokerId, trader, tradeDate, assetClass, orders, ct);

            if (result != null)
            {
                await PersistResultAsync(result, processBillingId, ct);
            }
        }
    }

    private async Task<BillingCalculationResult?> CalculateAsync(
        int brokerId,
        string trader,
        DateTime tradeDate,
        string assetClass,
        List<Order> orders,
        CancellationToken ct)
    {
        var strategy = _strategyRegistry.GetStrategy(assetClass);

        if (strategy == null)
        {
            _logger.LogWarning(
                "No calculation strategy found for AssetClass {AssetClass}, trader {Trader}",
                assetClass, trader);
            return null;
        }

        return await strategy.CalculateAsync(brokerId, trader, tradeDate, orders, ct);
    }

    private async Task PersistResultAsync(
        BillingCalculationResult result,
        int processBillingId,
        CancellationToken ct)
    {
        var dailyBilling = new DailyBilling
        {
            Broker = result.Broker,
            Trader = result.Trader,
            TradeDate = result.TradeDate,
            AssetClass = result.AssetClass,
            TotalNotional = result.TotalNotional,
            TotalQty = result.TotalQty,
            TotalFee = result.TotalFee,
            AccumulatedBefore = result.AccumulatedBefore,
            AccumulatedAfter = result.AccumulatedAfter,
            EquityConfigId = result.AssetClass == "EQUITY" ? result.ConfigId : null,
            BmfConfigId = result.AssetClass == "BMF" ? result.ConfigId : null,
            ProcessBillingId = processBillingId,
            CalculatedAt = DateTime.UtcNow
        };

        var dailyBillingId = await _dailyBillingRepository.InsertAsync(dailyBilling, ct);

        var orderBillings = result.OrderFees.Select(of => new OrderBilling
        {
            OrderId = of.OrderId,
            DailyBillingId = dailyBillingId,
            Broker = result.Broker,
            Trader = result.Trader,
            TradeDate = result.TradeDate,
            AssetClass = result.AssetClass,
            Notional = of.Notional,
            Qty = of.Qty,
            FeeAmount = of.FeeAmount
        }).ToList();

        await _orderBillingRepository.InsertBatchAsync(orderBillings, ct);

        _logger.LogDebug(
            "Persisted billing for {Trader}/{Broker}/{Date}/{AssetClass}: Fee={Fee}",
            result.Trader, result.Broker, result.TradeDate, result.AssetClass, result.TotalFee);
    }

    private static List<int> ParseBrokerIds(string processBroker)
    {
        if (string.IsNullOrWhiteSpace(processBroker))
            return new List<int>();

        return processBroker
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToList();
    }
}
