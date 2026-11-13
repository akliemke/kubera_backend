using Dapper;
using FinancialEngineCore.Domain;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Infra;

public class OrdersRepository
{
    private readonly DbConnectionFactory _dbConnectionFactory;

    public OrdersRepository(DbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    /// <summary>
    /// Ordens cobráveis do broker no dia (ver <see cref="BillableOrderBuilder"/>). Trades detalhados só
    /// das ordens GTC/GTD e sintéticas.
    /// </summary>
    /// <param name="symbolAssets">Mapa Symbol → Asset do dia (<see cref="GetSymbolAssetsAsync"/>); null = carrega</param>
    public async Task<List<Order>> GetOrdersByBrokerAsync(
        DateOnly tradeDate,
        int brokerId,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? symbolAssets = null)
    {
        const string ordersSql = @"
            SELECT OrderId, CumQty, AvgPx, Symbol, EnteringTrader, Entity, TradeDate, ExecBroker,
                   Instance, Side, TimeInForce
            FROM history.orders_new
            WHERE TradeDate = @TradeDate
              AND ExecBroker = @BrokerId";

        const string tradesSql = @"
            SELECT OrderId, Qty, Price, UniqueTradeId
            FROM history.trades_new
            WHERE OrderId = ANY(@OrderIds)
              AND TradeDate = @TradeDate";

        symbolAssets ??= await GetSymbolAssetsAsync(tradeDate, cancellationToken);

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var orders = (await conn.QueryAsync<Order>(ordersSql, new
        {
            TradeDate = tradeDate,
            BrokerId = brokerId.ToString()
        })).ToList();

        var orderIds = orders
            .Where(o => BillableOrderBuilder.NeedsTrades(o, symbolAssets))
            .Select(o => o.OrderId)
            .ToArray();

        var trades = orderIds.Length == 0
            ? Enumerable.Empty<DayTrade>()
            : await conn.QueryAsync<DayTrade>(tradesSql, new { OrderIds = orderIds, TradeDate = tradeDate });

        return Classify(BillableOrderBuilder.Build(orders, symbolAssets, trades));
    }

    /// <summary>
    /// Mapa Symbol → Asset (raiz) dos trades do dia, de todos os brokers (history.orders_new não tem Asset).
    /// Mesmo Symbol com Assets diferentes: vale o do primeiro trade.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetSymbolAssetsAsync(
        DateOnly tradeDate, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT DISTINCT ON (Symbol) Symbol, Asset
            FROM history.trades_new
            WHERE TradeDate = @TradeDate
              AND Asset IS NOT NULL
            ORDER BY Symbol, Id";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await conn.QueryAsync<(string Symbol, string Asset)>(
            sql, new { TradeDate = tradeDate });

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (symbol, asset) in rows)
            map.TryAdd(symbol.Trim(), asset.Trim());
        return map;
    }

    /// <summary>
    /// A base de ordens não traz AssetClass: classifica pela raiz (Asset) / Symbol. Ponto único de classificação.
    /// </summary>
    private static List<Order> Classify(IEnumerable<Order> orders)
    {
        var list = orders.ToList();
        foreach (var order in list)
            order.AssetClass = AssetClassifier.Classify(order.Asset, order.Symbol);
        return list;
    }

    /// <summary>
    /// Datas do mês anteriores a uma data que têm ordens do broker mas nenhum billing processado.
    /// Indica processamento fora de ordem (acumulados do dia informado ficariam incompletos).
    /// </summary>
    public async Task<List<string>> GetUnbilledDatesBeforeAsync(
        DateOnly tradeDate,
        int brokerId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT DISTINCT to_char(o.TradeDate, 'YYYY-MM-DD') AS TradeDate
            FROM history.orders_new o
            WHERE o.ExecBroker = @BrokerIdText
              AND o.TradeDate >= @MonthStart
              AND o.TradeDate < @TradeDate
              AND NOT EXISTS (
                  SELECT 1 FROM financial_engine.daily_billing db
                  WHERE db.Broker = @BrokerId
                    AND db.TradeDate = o.TradeDate)
            ORDER BY 1";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var dates = await conn.QueryAsync<string>(sql, new
        {
            BrokerIdText = brokerId.ToString(),
            BrokerId = brokerId,
            MonthStart = new DateOnly(tradeDate.Year, tradeDate.Month, 1),
            TradeDate = tradeDate
        });
        return dates.ToList();
    }
}
