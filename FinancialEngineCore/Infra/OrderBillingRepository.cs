using System.Data;
using Dapper;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Infra;

public class OrderBillingRepository
{
    private readonly DbConnectionFactory _dbConnectionFactory;

    public OrderBillingRepository(DbConnectionFactory connectionFactory)
    {
        _dbConnectionFactory = connectionFactory;
    }

    /// <summary>
    /// Insere múltiplos registros de billing por ordem (bulk insert).
    /// </summary>
    public async Task InsertBatchAsync(
        IEnumerable<OrderBilling> orderBillings,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO financial_engine.order_billing
                (OrderId, DailyBillingId, Broker, Trader, TradeDate,
                 AssetClass, Notional, Qty, FeeAmount)
            VALUES
                (@OrderId, @DailyBillingId, @Broker, @Trader, @TradeDate,
                 @AssetClass, @Notional, @Qty, @FeeAmount)";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync(sql, orderBillings);
    }

    /// <summary>
    /// Insere múltiplos registros de billing por ordem (transactional overload).
    /// </summary>
    public async Task InsertBatchAsync(
        IEnumerable<OrderBilling> orderBillings,
        IDbConnection conn,
        IDbTransaction txn)
    {
        const string sql = @"
            INSERT INTO financial_engine.order_billing
                (OrderId, DailyBillingId, Broker, Trader, TradeDate,
                 AssetClass, Notional, Qty, FeeAmount)
            VALUES
                (@OrderId, @DailyBillingId, @Broker, @Trader, @TradeDate,
                 @AssetClass, @Notional, @Qty, @FeeAmount)";

        await conn.ExecuteAsync(sql, orderBillings, transaction: txn);
    }

    /// <summary>
    /// Busca order billings por DailyBillingId.
    /// </summary>
    public async Task<IEnumerable<OrderBilling>> GetByDailyBillingIdAsync(
        long dailyBillingId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM financial_engine.order_billing
            WHERE DailyBillingId = @DailyBillingId";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryAsync<OrderBilling>(sql, new { DailyBillingId = dailyBillingId });
    }

    /// <summary>
    /// Deleta registros vinculados a um DailyBilling (para reprocessamento).
    /// </summary>
    public async Task DeleteByDailyBillingIdAsync(
        long dailyBillingId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            DELETE FROM financial_engine.order_billing
            WHERE DailyBillingId = @DailyBillingId";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync(sql, new { DailyBillingId = dailyBillingId });
    }

    /// <summary>
    /// Busca order billings com dados da ordem (Symbol, Side) para um broker/mês.
    /// </summary>
    public async Task<List<OrderBillingDetail>> GetByBrokerAndMonthAsync(
        int brokerId,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1);

        const string sql = @"
            SELECT ob.OrderId, ob.Broker, ob.Trader, ob.TradeDate, ob.AssetClass,
                   ob.Notional, ob.Qty, ob.FeeAmount,
                   o.Symbol, o.Side
            FROM financial_engine.order_billing ob
            LEFT JOIN history.vw_billable_orders o ON o.Id = ob.OrderId
                AND o.businessDate = to_char(ob.TradeDate, 'YYYY-MM-DD')
            WHERE ob.Broker = @BrokerId
              AND ob.TradeDate >= @MonthStart AND ob.TradeDate < @MonthEnd
            ORDER BY ob.Trader, ob.AssetClass, ob.TradeDate, ob.OrderId";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var result = await conn.QueryAsync<OrderBillingDetail>(sql, new
        {
            BrokerId = brokerId,
            MonthStart = monthStart,
            MonthEnd = monthEnd
        });
        return result.ToList();
    }

    /// <summary>
    /// Deleta registros de um broker/dia específico (para reprocessamento).
    /// </summary>
    public async Task DeleteByDateAsync(
        int brokerId,
        DateOnly tradeDate,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            DELETE FROM financial_engine.order_billing
            WHERE Broker = @BrokerId AND TradeDate = @TradeDate";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync(sql, new { BrokerId = brokerId, TradeDate = tradeDate });
    }
}
