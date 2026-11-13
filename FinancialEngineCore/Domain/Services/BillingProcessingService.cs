using FinancialEngineCore.Domain.Strategies;
using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Collections.Concurrent;
using System.Diagnostics;

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
    private readonly MarketDataService _marketDataService;
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
        MarketDataService marketDataService,
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
        _marketDataService = marketDataService;
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

        // Symbol -> Asset map is per day for all brokers: load once per date for the whole job
        var symbolAssets = new ConcurrentDictionary<DateOnly, Lazy<Task<IReadOnlyDictionary<string, string>>>>();

        await Parallel.ForEachAsync(brokerIds, options, async (brokerId, token) =>
        {
            await ProcessBrokerAsync(brokerId, billing.ProcessDate, billing.ProcessId, symbolAssets, token);
        });

        _logger.LogInformation("Completed billing {ProcessId}", billing.ProcessId);
    }

    /// <summary>
    /// Processes a broker for a date and, in cascade, every later date of the same month
    /// that was already processed — month accumulations and CAPs depend on prior days.
    /// Everything runs in a single transaction under a per-broker advisory lock.
    /// At the end, the broker's monthly Market Data charge is recalculated (added automatically).
    /// </summary>
    public Task ProcessBrokerAsync(int brokerId, DateOnly tradeDate, int processBillingId, CancellationToken ct)
        => ProcessBrokerAsync(brokerId, tradeDate, processBillingId, new(), ct);

    private async Task ProcessBrokerAsync(
        int brokerId,
        DateOnly tradeDate,
        int processBillingId,
        ConcurrentDictionary<DateOnly, Lazy<Task<IReadOnlyDictionary<string, string>>>> symbolAssets,
        CancellationToken ct)
    {
        _logger.LogDebug("Processing broker {BrokerId} for date {Date}", brokerId, tradeDate);

        var unbilledDates = await _orderRepository.GetUnbilledDatesBeforeAsync(tradeDate, brokerId, ct);
        if (unbilledDates.Count > 0)
        {
            _logger.LogWarning(
                "Broker {BrokerId}: processing {Date} while earlier dates of the month have orders but no billing ({UnbilledDates}). Month accumulations will be incomplete until they are processed",
                brokerId, tradeDate, string.Join(", ", unbilledDates));
        }

        await using var conn = await _dbConnectionFactory.CreateOpenNpgsqlConnectionAsync(ct);
        await using var txn = await conn.BeginTransactionAsync(ct);
        try
        {
            await _dailyBillingRepository.AcquireBrokerLockAsync(brokerId, conn, txn);

            var laterDates = await _dailyBillingRepository.GetProcessedDatesAfterAsync(brokerId, tradeDate, conn, txn);
            if (laterDates.Count > 0)
            {
                _logger.LogInformation(
                    "Broker {BrokerId}: reprocessing {Date} cascades to {LaterDates}",
                    brokerId, tradeDate, string.Join(", ", laterDates));
            }

            await ProcessDateAsync(brokerId, tradeDate, processBillingId, symbolAssets, conn, txn, ct);
            foreach (var date in laterDates)
                await ProcessDateAsync(brokerId, date, processBillingId, symbolAssets, conn, txn, ct);

            // Market Data is monthly: recalculated at the end of the broker's billing
            await _marketDataService.RecalculateBrokerMonthAsync(brokerId, tradeDate.Year, tradeDate.Month, conn, txn, ct);

            await txn.CommitAsync(ct);
        }
        catch
        {
            await txn.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task ProcessDateAsync(
        int brokerId,
        DateOnly tradeDate,
        int processBillingId,
        ConcurrentDictionary<DateOnly, Lazy<Task<IReadOnlyDictionary<string, string>>>> symbolAssets,
        NpgsqlConnection conn,
        NpgsqlTransaction txn,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // Orders and configs use their own connections (in parallel); month accumulations
        // must be read inside the transaction to see days rewritten earlier in the cascade
        var ordersTask = LoadOrdersAsync(brokerId, tradeDate, symbolAssets, ct);
        var entityConfigsTask = _entityBillingConfigRepository.GetByBrokerAndDateAsync(brokerId, tradeDate, ct);
        var bracketsTask = _bracketRepository.GetAllByBrokerAndDateAsync(brokerId, tradeDate, ct);
        var bmfConfigsTask = _bmfConfigRepository.GetAllByBrokerAndDateAsync(brokerId, tradeDate, ct);
        var priceFactorsTask = _priceModificationFactorRepository.GetAllByDateAsync(tradeDate, ct);

        var monthSummary = await _dailyBillingRepository.GetMonthSummaryByBrokerAsync(brokerId, tradeDate, conn, txn);
        var feePerBracket = await _bracketFeeTrackingRepository.GetMonthFeePerBracketByBrokerAsync(brokerId, tradeDate, conn, txn);

        await Task.WhenAll(ordersTask, entityConfigsTask, bracketsTask, bmfConfigsTask, priceFactorsTask);

        var brokerOrders = await ordersTask;
        var loadMs = sw.ElapsedMilliseconds;

        // Always clear the date (CASCADE handles children): orders may have been removed at the source
        sw.Restart();
        await _dailyBillingRepository.DeleteByDateAsync(brokerId, tradeDate, conn, txn);
        var deleteMs = sw.ElapsedMilliseconds;

        if (brokerOrders.Count == 0)
        {
            _logger.LogDebug("No orders for broker {BrokerId} on {Date}", brokerId, tradeDate);
            return;
        }

        var context = new BrokerBillingContext(
            await bracketsTask,
            await bmfConfigsTask,
            monthSummary,
            feePerBracket,
            BuildPriceFactorMap(await priceFactorsTask));

        // Resolve billing key: entity-based or trader-based (canonical casing)
        var entityKeyMap = BuildEntityKeyMap((await entityConfigsTask).Select(c => c.Entity));

        // Group by (AssetClass, BillingKey), case-insensitive, and calculate everything in memory.
        // AssetClass first: a trader's special rule is per asset class
        sw.Restart();
        var results = new List<BillingCalculationResult>();
        var groupedOrders = brokerOrders
            .GroupBy(order => order.AssetClass, StringComparer.OrdinalIgnoreCase)
            .SelectMany(byClass => byClass
                .GroupBy(order => ResolveBillingKey(order, entityKeyMap, context.HasTraderSpecialRule))
                .Select(byKey => (BillingKey: byKey.Key, AssetClass: byClass.Key, Orders: byKey.ToList())));

        foreach (var (billingKey, assetClass, orders) in groupedOrders)
        {
            var strategy = _strategyRegistry.GetStrategy(assetClass);

            if (strategy == null)
            {
                _logger.LogWarning(
                    "No calculation strategy found for AssetClass {AssetClass}, billing key {BillingKey}; orders not billed: {Symbols}",
                    assetClass, billingKey,
                    string.Join(", ", orders.Select(o => $"{o.Symbol} ({o.Asset})").Distinct()));
                continue;
            }

            if (billingKey.Type == BillingKeyTypes.Trader && orders.Any(o => entityKeyMap.ContainsKey(o.Entity)))
            {
                _logger.LogInformation(
                    "Broker {BrokerId} {Date}: trader {Trader} has its own {AssetClass} special rule and is billed outside its entity",
                    brokerId, tradeDate, billingKey.Key, assetClass);
            }

            results.Add(strategy.Calculate(brokerId, billingKey, tradeDate, orders, context));
        }
        var calcMs = sw.ElapsedMilliseconds;

        sw.Restart();
        await PersistResultsAsync(results, processBillingId, conn, txn, ct);

        _logger.LogInformation(
            "Broker {BrokerId} {Date}: {OrderCount} orders, {GroupCount} groups | load={LoadMs}ms calc={CalcMs}ms delete={DeleteMs}ms persist={PersistMs}ms",
            brokerId, tradeDate, brokerOrders.Count, results.Count,
            loadMs, calcMs, deleteMs, sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Persists all results of a broker in a constant number of round-trips:
    /// reserve daily_billing Ids, then one binary COPY per table.
    /// </summary>
    private async Task PersistResultsAsync(
        IReadOnlyList<BillingCalculationResult> results,
        int processBillingId,
        NpgsqlConnection conn,
        NpgsqlTransaction txn,
        CancellationToken ct)
    {
        var ids = await _dailyBillingRepository.ReserveIdsAsync(results.Count, conn, txn);
        var calculatedAt = DateTime.UtcNow;

        var dailyBillings = new List<DailyBilling>(results.Count);
        var orderBillings = new List<OrderBilling>();
        var bracketTrackings = new List<BracketFeeTracking>();

        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            var dailyBillingId = ids[i];

            dailyBillings.Add(new DailyBilling
            {
                Id = dailyBillingId,
                Broker = result.Broker,
                Trader = result.Trader,
                KeyType = result.KeyType,
                TradeDate = result.TradeDate,
                AssetClass = result.AssetClass,
                TotalNotional = result.TotalNotional,
                TotalQty = result.TotalQty,
                TotalFee = result.TotalFee,
                AccumulatedBefore = result.AccumulatedBefore,
                AccumulatedAfter = result.AccumulatedAfter,
                EquityConfigId = result.AssetClass == AssetClasses.Equity ? result.ConfigId : null,
                BmfConfigId = result.AssetClass == AssetClasses.Bmf ? result.ConfigId : null,
                ProcessBillingId = processBillingId,
                CalculatedAt = calculatedAt
            });

            orderBillings.AddRange(result.OrderFees.Select(of => new OrderBilling
            {
                OrderId = of.OrderId,
                Asset = of.Asset,
                DailyBillingId = dailyBillingId,
                Broker = result.Broker,
                Trader = result.Trader,
                KeyType = result.KeyType,
                TradeDate = result.TradeDate,
                AssetClass = result.AssetClass,
                Notional = of.Notional,
                Qty = of.Qty,
                FeeAmount = of.FeeAmount
            }));

            // Bracket fee tracking (EQUITY only)
            bracketTrackings.AddRange(result.BracketFees.Select(bf => new BracketFeeTracking
            {
                DailyBillingId = dailyBillingId,
                BracketId = bf.BracketId,
                NotionalInBracket = bf.NotionalInBracket,
                CalculatedFee = bf.CalculatedFee,
                ChargedFee = bf.ChargedFee,
                AccumulatedFeeBefore = bf.AccumulatedFeeBefore,
                BracketCap = bf.BracketCap
            }));
        }

        await _dailyBillingRepository.CopyAsync(dailyBillings, conn, ct);
        await _orderBillingRepository.CopyAsync(orderBillings, conn, ct);
        await _bracketFeeTrackingRepository.CopyAsync(bracketTrackings, conn, ct);

        _logger.LogDebug(
            "Persisted {DailyCount} daily billings, {OrderCount} order billings, {BracketCount} bracket trackings",
            dailyBillings.Count, orderBillings.Count, bracketTrackings.Count);
    }

    private async Task<List<Order>> LoadOrdersAsync(
        int brokerId,
        DateOnly tradeDate,
        ConcurrentDictionary<DateOnly, Lazy<Task<IReadOnlyDictionary<string, string>>>> symbolAssets,
        CancellationToken ct)
    {
        // CancellationToken.None: the shared load must not be cancelled by a single broker
        var map = await symbolAssets.GetOrAdd(tradeDate, date =>
            new Lazy<Task<IReadOnlyDictionary<string, string>>>(
                () => _orderRepository.GetSymbolAssetsAsync(date, CancellationToken.None))).Value;

        return await _orderRepository.GetOrdersByBrokerAsync(tradeDate, brokerId, ct, map);
    }

    /// <summary>
    /// Case-insensitive map from symbol to price factor. Overlapping validities for the same symbol:
    /// the most recent ValidFrom wins (instead of failing the whole broker on a duplicate key).
    /// </summary>
    internal static IReadOnlyDictionary<string, decimal> BuildPriceFactorMap(IEnumerable<PriceModificationFactor> factors)
    {
        return factors
            .GroupBy(f => f.Symbol.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(f => f.ValidFrom).ThenByDescending(f => f.Id).First().Factor,
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Case-insensitive map from entity name to its canonical spelling (as configured).
    /// </summary>
    internal static IReadOnlyDictionary<string, string> BuildEntityKeyMap(IEnumerable<string> configuredEntities)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in configuredEntities)
            map.TryAdd(entity, entity);
        return map;
    }

    /// <summary>
    /// TRADER key if the trader has its own special rule for the order's asset class
    /// (special rule prevails over the entity), else ENTITY key if the entity is configured, else TRADER key.
    /// </summary>
    /// <param name="hasTraderSpecialRule">(trader, assetClass) -> trader has a TRADER special rule valid on the day</param>
    internal static BillingKey ResolveBillingKey(
        Order order,
        IReadOnlyDictionary<string, string> entityKeyMap,
        Func<string, string, bool>? hasTraderSpecialRule = null)
    {
        if (entityKeyMap.Count > 0
            && entityKeyMap.TryGetValue(order.Entity, out var canonicalEntity)
            && hasTraderSpecialRule?.Invoke(order.EnteringTrader, order.AssetClass) != true)
            return BillingKey.Entity(canonicalEntity);

        return BillingKey.Trader(order.EnteringTrader);
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
