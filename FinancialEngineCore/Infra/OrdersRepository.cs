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
        const string sql = "SELECT * FROM history.orders WHERE businessDate = @BusinessDate";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var orders = await conn.QueryAsync<Order>(sql, new { BusinessDate = businessDate });
        return orders.ToList();
    }
}
