using Dapper;
using FinancialEngineCore.Model;
using KuberaWebApp.Infra;

namespace FinancialEngineCore.Infra;
public class FeeBracketRepository
{
    private DbConnectionFactory _dbConnectionFactory;

    public FeeBracketRepository(DbConnectionFactory connectionFactory)
    {
        _dbConnectionFactory = connectionFactory; 
    }

    public async Task<IEnumerable<FeeBracket>> GetFeeBracketsByBrokerAsync(int brokerId, CancellationToken cts)
    {
        var sql = @"SELECT * FROM financial_engine.fee_brackets 
                    WHERE Broker = @BrokerId AND IsActive = TRUE
                    ORDER BY NotionalFrom ASC";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cts);
        var feeBrackets = await conn.QueryAsync<FeeBracket>(sql, new { BrokerId = brokerId });
        return feeBrackets;
    }

    public async Task<IEnumerable<FeeBracket>> GetAll(CancellationToken cts)
    {
        var sql = "SELECT * FROM financial_engine.fee_brackets WHERE IsActive = TRUE";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cts);
        var feeBrackets = await conn.QueryAsync<FeeBracket>(sql);
        return feeBrackets;
    }
}
