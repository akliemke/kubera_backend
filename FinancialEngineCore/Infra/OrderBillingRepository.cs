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
