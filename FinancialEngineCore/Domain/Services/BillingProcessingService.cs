using FinancialEngineCore.Domain.Strategies;
using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;
using Microsoft.Extensions.Logging;

namespace FinancialEngineCore.Domain.Services;

/// <summary>
/// Implements billing processing logic.
/// Coordinates order retrieval, calculation (via strategies), and persistence.
/// Uses pre-loaded context to avoid N+1 queries and wraps writes in a transaction.
/// </summary>
public class BillingProcessingService : IBillingProcessingService
{
    private readonly OrdersRepository _orderRepository;
    private readonly DailyBillingRepository _dailyBillingRepository;
    private readonly OrderBillingRepository _orderBillingRepository;
    private readonly BracketFeeTrackingRepository _bracketFeeTrackingRepository;
    private readonly EntityBillingConfigRepository _entityBillingConfigRepository;
    private readonly EquityFeeBracketRepository _bracketRepository;
    private readonly BmfFeeConfigRepository _bmfConfigRepository;
    private readonly PriceModificationFactorRepository _priceModificationFactorRepository;
    private readonly DbConnectionFactory _dbConnectionFactory;
    private readonly CalculationStrategyRegistry _strategyRegistry;
    private readonly ILogger<BillingProcessingService> _logger;

    public BillingProcessingService(
        OrdersRepository orderRepository,
        DailyBillingRepository dailyBillingRepository,
        OrderBillingRepository orderBillingRepository,
        BracketFeeTrackingRepository bracketFeeTrackingRepository,
        EntityBillingConfigRepository entityBillingConfigRepository,
        EquityFeeBracketRepository bracketRepository,
        BmfFeeConfigRepository bmfConfigRepository,
        PriceModificationFactorRepository priceModificationFactorRepository,
        DbConnectionFactory dbConnectionFactory,
        CalculationStrategyRegistry strategyRegistry,
        ILogger<BillingProcessingService> logger)
    {
        _orderRepository = orderRepository;
        _dailyBillingRepository = dailyBillingRepository;
        _orderBillingRepository = orderBillingRepository;
        _bracketFeeTrackingRepository = bracketFeeTrackingRepository;
        _entityBillingConfigRepository = entityBillingConfigRepository;
        _bracketRepository = bracketRepository;
        _bmfConfigRepository = bmfConfigRepository;
        _priceModificationFactorRepository = priceModificationFactorRepository;
        _dbConnectionFactory = dbConnectionFactory;
        _strategyRegistry = strategyRegistry;
        _logger = logger;
    }

    public async Task ProcessBillingAsync(ProcessBilling billing, CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing billing {ProcessId} for date {Date}, brokers: {Brokers}",
            billing.ProcessId, billing.ProcessDate, billing.ProcessBroker);

        var brokerIds = ParseBrokerIds(billing.ProcessBroker);

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(brokerIds, options, async (brokerId, token) =>
        {
            await ProcessBrokerAsync(brokerId, billing.ProcessDate, billing.ProcessId, token);
        });

        _logger.LogInformation("Completed billing {ProcessId}", billing.ProcessId);
    }

    public async Task ProcessBrokerAsync(int brokerId, DateTime tradeDate, int processBillingId, CancellationToken ct)
    {
        _logger.LogDebug("Processing broker {BrokerId} for date {Date}", brokerId, tradeDate);

        // Get orders for this broker/date (filtered at DB level)
        var brokerOrders = await _orderRepository.GetOrdersByBrokerAsync(tradeDate, brokerId, ct);

        if (brokerOrders.Count == 0)
        {
            _logger.LogDebug("No orders for broker {BrokerId} on {Date}", brokerId, tradeDate);
            return;
        }

        // Resolve billing key: entity-based or trader-based
        var entityConfigs = await _entityBillingConfigRepository
            .GetByBrokerAndDateAsync(brokerId, tradeDate, ct);
        var entityBillingSet = new HashSet<string>(
            entityConfigs.Select(c => c.Entity),
            StringComparer.OrdinalIgnoreCase);

        // Pre-load all data needed for calculations (5 queries in parallel)
        var context = await PreloadBrokerContextAsync(brokerId, tradeDate, ct);

        // Group by (BillingKey, AssetClass)
        var groups = brokerOrders
            .GroupBy(o => new
            {
                BillingKey = ResolveBillingKey(o, entityBillingSet),
                o.AssetClass
            })
            .ToList();

        // All writes within a single transaction
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(ct);
        using var txn = conn.BeginTransaction();
        try
        {
            // Delete existing billing for reprocessing (CASCADE handles children)
            await _dailyBillingRepository.DeleteByDateAsync(brokerId, tradeDate, conn, txn);

            foreach (var group in groups)
            {
                var orders = group.ToList();
                var trader = group.Key.BillingKey;
                var assetClass = group.Key.AssetClass;

                var strategy = _strategyRegistry.GetStrategy(assetClass);

                if (strategy == null)
                {
                    _logger.LogWarning(
                        "No calculation strategy found for AssetClass {AssetClass}, trader {Trader}",
                        assetClass, trader);
                    continue;
                }

                var result = strategy.Calculate(brokerId, trader, tradeDate, orders, context);
                await PersistResultAsync(result, processBillingId, conn, txn);
            }

            txn.Commit();
        }
        catch
        {
            txn.Rollback();
            throw;
        }
    }

    private async Task<BrokerBillingContext> PreloadBrokerContextAsync(
        int brokerId, DateTime tradeDate, CancellationToken ct)
    {
        var bracketsTask = _bracketRepository.GetAllByBrokerAndDateAsync(brokerId, tradeDate, ct);
        var configsTask = _bmfConfigRepository.GetAllByBrokerAndDateAsync(brokerId, tradeDate, ct);
        var summaryTask = _dailyBillingRepository.GetMonthSummaryByBrokerAsync(brokerId, tradeDate, ct);
        var feePerBracketTask = _bracketFeeTrackingRepository.GetMonthFeePerBracketByBrokerAsync(brokerId, tradeDate, ct);
        var priceFactorsTask = _priceModificationFactorRepository.GetAllByDateAsync(tradeDate, ct);

        await Task.WhenAll(bracketsTask, configsTask, summaryTask, feePerBracketTask, priceFactorsTask);

        var priceFactors = (await priceFactorsTask).ToDictionary(f => f.Symbol, f => f.Factor);

        return new BrokerBillingContext(
            await bracketsTask,
            await configsTask,
            await summaryTask,
            await feePerBracketTask,
            priceFactors);
    }

    private async Task PersistResultAsync(
        BillingCalculationResult result,
        int processBillingId,
        System.Data.IDbConnection conn,
        System.Data.IDbTransaction txn)
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

        var dailyBillingId = await _dailyBillingRepository.InsertAsync(dailyBilling, conn, txn);

        // Persist order fees
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

        await _orderBillingRepository.InsertBatchAsync(orderBillings, conn, txn);

        // Persist bracket fee tracking (EQUITY only)
        if (result.BracketFees.Count > 0)
        {
            var bracketTrackings = result.BracketFees.Select(bf => new BracketFeeTracking
            {
                DailyBillingId = dailyBillingId,
                BracketId = bf.BracketId,
                NotionalInBracket = bf.NotionalInBracket,
                CalculatedFee = bf.CalculatedFee,
                ChargedFee = bf.ChargedFee,
                AccumulatedFeeBefore = bf.AccumulatedFeeBefore,
                BracketCap = bf.BracketCap
            }).ToList();

            await _bracketFeeTrackingRepository.InsertBatchAsync(bracketTrackings, conn, txn);
        }

        _logger.LogDebug(
            "Persisted billing for {Trader}/{Broker}/{Date}/{AssetClass}: Fee={Fee}, Brackets={BracketCount}",
            result.Trader, result.Broker, result.TradeDate, result.AssetClass, result.TotalFee, result.BracketFees.Count);
    }

    internal static string ResolveBillingKey(Order order, HashSet<string> entityBillingSet)
    {
        if (entityBillingSet.Count > 0 && entityBillingSet.Contains(order.Entity))
            return order.Entity;

        return order.EnteringTrader;
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
