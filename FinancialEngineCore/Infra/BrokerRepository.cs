using Dapper;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Infra;

public class BrokerRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public BrokerRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<BrokerModel>> GetAllAsync(CancellationToken cancellationToken)
    {
        const string sql = "SELECT * FROM management.brokers ORDER BY Code";
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryAsync<BrokerModel>(sql);
    }

    public async Task<BrokerModel?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        const string sql = "SELECT * FROM management.brokers WHERE Id = @Id";
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryFirstOrDefaultAsync<BrokerModel>(sql, new { Id = id });
    }

    public async Task<BrokerModel?> GetByCodeAsync(int code, CancellationToken cancellationToken)
    {
        const string sql = "SELECT * FROM management.brokers WHERE Code = @Code";
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryFirstOrDefaultAsync<BrokerModel>(sql, new { Code = code });
    }

    public async Task<BrokerModel> CreateAsync(BrokerModel broker, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO management.brokers (Name, Document, Code, ContractDate, Status, BillingCurrency, Contacts)
            VALUES (@Name, @Document, @Code, @ContractDate, @Status, @BillingCurrency, @Contacts)
            RETURNING *";
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QuerySingleAsync<BrokerModel>(sql, broker);
    }

    public async Task<bool> UpdateAsync(BrokerModel broker, CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE management.brokers
            SET Name = @Name, Document = @Document, Code = @Code,
                ContractDate = @ContractDate, Status = @Status,
                BillingCurrency = @BillingCurrency, Contacts = @Contacts
            WHERE Id = @Id";
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await conn.ExecuteAsync(sql, broker);
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM management.brokers WHERE Id = @Id";
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await conn.ExecuteAsync(sql, new { Id = id });
        return rows > 0;
    }

    public async Task<bool> SetStatusAsync(long id, bool status, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE management.brokers SET Status = @Status WHERE Id = @Id";
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await conn.ExecuteAsync(sql, new { Id = id, Status = status });
        return rows > 0;
    }
}
