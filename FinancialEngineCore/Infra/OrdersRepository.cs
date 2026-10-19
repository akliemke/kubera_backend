using Dapper;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Infra;

public class OrdersRepository
{
    private readonly DbConnectionFactory _dbConnectionFactory;

    public OrdersRepository(DbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<List<Order>> GetOrdersAsync(string businessDate, CancellationToken cancellationToken)
    {
        const string sql = "SELECT * FROM history.vw_billable_orders WHERE businessDate = @BusinessDate";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var orders = await conn.QueryAsync<Order>(sql, new { BusinessDate = businessDate });
        return orders.ToList();
    }

    public async Task<List<Order>> GetOrdersByBrokerAsync(
        DateOnly tradeDate,
        int brokerId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM history.vw_billable_orders
            WHERE businessDate = @BusinessDate
              AND EnteringFirm = @BrokerId";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var orders = await conn.QueryAsync<Order>(sql, new
        {
            BusinessDate = tradeDate.ToString("yyyy-MM-dd"),
            BrokerId = brokerId.ToString()
        });
        return orders.ToList();
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
            SELECT DISTINCT o.businessDate
            FROM history.orders o
            WHERE o.EnteringFirm = @BrokerIdText
              AND o.businessDate >= @MonthStart
              AND o.businessDate < @BusinessDate
              AND NOT EXISTS (
                  SELECT 1 FROM financial_engine.daily_billing db
                  WHERE db.Broker = @BrokerId
                    AND db.TradeDate = o.businessDate::date)
            ORDER BY o.businessDate";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var dates = await conn.QueryAsync<string>(sql, new
        {
            BrokerIdText = brokerId.ToString(),
            BrokerId = brokerId,
            MonthStart = new DateOnly(tradeDate.Year, tradeDate.Month, 1).ToString("yyyy-MM-dd"),
            BusinessDate = tradeDate.ToString("yyyy-MM-dd")
        });
        return dates.ToList();
    }
}
