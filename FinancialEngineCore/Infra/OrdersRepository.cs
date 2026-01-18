using Dapper;
using FinancialEngineCore.Model;
using KuberaWebApp.Infra;

namespace FinancialEngineCore.Infra;
public class OrdersRepository
{
    private DbConnectionFactory _dbConnectionFactory;

    public OrdersRepository(DbConnectionFactory dbConnectiondFactory)
    {
        _dbConnectionFactory = dbConnectiondFactory;
        
    }


    public async Task<List<Order>> GetOrders(string businessDate, CancellationToken cancellationToken)
    {
        var sql = "SELECT * FROM history.orders WHERE businessDate = 'date'";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var orders = await conn.QueryAsync<Order>(sql);

        return new List<Order>(orders);
    }
}
