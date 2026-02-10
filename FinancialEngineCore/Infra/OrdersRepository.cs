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
}
