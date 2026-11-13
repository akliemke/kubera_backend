using System.Data;
using Dapper;
using FinancialEngineCore.Model;
using Npgsql;
using NpgsqlTypes;

namespace FinancialEngineCore.Infra;

public class OrderBillingRepository
{
    private readonly DbConnectionFactory _dbConnectionFactory;

    public OrderBillingRepository(DbConnectionFactory connectionFactory)
    {
        _dbConnectionFactory = connectionFactory;
    }

    /// <summary>
    /// Insere múltiplos registros de billing por ordem via binary COPY (1 round-trip).
    /// Participa da transação aberta na conexão.
    /// </summary>
    public async Task CopyAsync(
        IReadOnlyList<OrderBilling> orderBillings,
        NpgsqlConnection conn,
        CancellationToken cancellationToken)
    {
        if (orderBillings.Count == 0)
            return;

        const string copy = @"
            COPY financial_engine.order_billing
                (OrderId, Asset, DailyBillingId, Broker, Trader, KeyType, TradeDate,
                 AssetClass, Notional, Qty, FeeAmount)
            FROM STDIN (FORMAT BINARY)";

        await using var writer = await conn.BeginBinaryImportAsync(copy, cancellationToken);
        foreach (var ob in orderBillings)
        {
            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(ob.OrderId, NpgsqlDbType.Varchar, cancellationToken);
            if (ob.Asset is null)
                await writer.WriteNullAsync(cancellationToken);
            else
                await writer.WriteAsync(ob.Asset, NpgsqlDbType.Varchar, cancellationToken);
            await writer.WriteAsync(ob.DailyBillingId, NpgsqlDbType.Bigint, cancellationToken);
            await writer.WriteAsync(ob.Broker, NpgsqlDbType.Integer, cancellationToken);
            await writer.WriteAsync(ob.Trader, NpgsqlDbType.Varchar, cancellationToken);
            await writer.WriteAsync(ob.KeyType, NpgsqlDbType.Varchar, cancellationToken);
            await writer.WriteAsync(ob.TradeDate, NpgsqlDbType.Date, cancellationToken);
            await writer.WriteAsync(ob.AssetClass, NpgsqlDbType.Varchar, cancellationToken);
            await writer.WriteAsync(ob.Notional, NpgsqlDbType.Numeric, cancellationToken);
            await writer.WriteAsync(ob.Qty, NpgsqlDbType.Numeric, cancellationToken);
            await writer.WriteAsync(ob.FeeAmount, NpgsqlDbType.Numeric, cancellationToken);
        }
        await writer.CompleteAsync(cancellationToken);
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
            SELECT ob.OrderId, ob.Broker, ob.Trader, ob.KeyType, ob.TradeDate, ob.AssetClass,
                   ob.Notional, ob.Qty, ob.FeeAmount, ob.Asset,
                   o.Symbol, o.Side
            FROM financial_engine.order_billing ob
            -- history.orders_new tem 1 linha por (ordem, dia): GTC/GTD repete o OrderId em vários dias
            LEFT JOIN history.orders_new o ON o.OrderId = ob.OrderId AND o.TradeDate = ob.TradeDate
            WHERE ob.Broker = @BrokerId
              AND ob.TradeDate >= @MonthStart AND ob.TradeDate < @MonthEnd
            ORDER BY ob.Trader, ob.KeyType, ob.AssetClass, ob.TradeDate, ob.OrderId, ob.Asset";

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
