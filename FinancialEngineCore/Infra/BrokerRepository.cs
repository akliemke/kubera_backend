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
        const string sql = "SELECT * FROM management.brokers";
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var brokers = await conn.QueryAsync<BrokerModel>(sql);
        return brokers;
    }

    public async Task<BrokerModel> CreateAsync(BrokerModel broker, CancellationToken cancellationToken)
    {
        const string sql = @"INSERT INTO management.brokers (Id, Name, Document, Code, ContractDate, Status, BillingCurrency, Contacts)
                             VALUES (@Id, @Name, @Document, @Code, @ContractDate, @Status, @BillingCurrency, @Contacts)";
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync(sql, broker);
        return broker;
    }

    public async Task UpdateAsync(BrokerModel broker, CancellationToken cancellationToken)
    {
        const string sql = @"UPDATE management.brokers
                             SET Name = @Name, Document = @Document, Code = @Code,
                                 ContractDate = @ContractDate, Status = @Status,
                                 BillingCurrency = @BillingCurrency, Contacts = @Contacts
                             WHERE Id = @Id";
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync(sql, broker);
    }
}
