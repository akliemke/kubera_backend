using System.Data;
using Dapper;
using FinancialEngineCore.Model;
using Npgsql;
using NpgsqlTypes;

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
        const string sql = "SELECT * FROM financial_engine.brokers ORDER BY Code";
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryAsync<BrokerModel>(sql);
    }

    public async Task<BrokerModel?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        const string sql = "SELECT * FROM financial_engine.brokers WHERE Id = @Id";
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryFirstOrDefaultAsync<BrokerModel>(sql, new { Id = id });
    }

    public async Task<BrokerModel?> GetByCodeAsync(int code, CancellationToken cancellationToken)
    {
        const string sql = "SELECT * FROM financial_engine.brokers WHERE Code = @Code";
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryFirstOrDefaultAsync<BrokerModel>(sql, new { Code = code });
    }

    public async Task<BrokerModel> CreateAsync(BrokerModel broker, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO financial_engine.brokers (Name, Document, Code, ContractDate, Status, BillingCurrency, Contacts)
            VALUES (@Name, @Document, @Code, @ContractDate, @Status, @BillingCurrency, @Contacts)
            RETURNING *";
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QuerySingleAsync<BrokerModel>(sql, BuildParameters(broker));
    }

    public async Task<bool> UpdateAsync(BrokerModel broker, CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE financial_engine.brokers
            SET Name = @Name, Document = @Document, Code = @Code,
                ContractDate = @ContractDate, Status = @Status,
                BillingCurrency = @BillingCurrency, Contacts = @Contacts
            WHERE Id = @Id";
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await conn.ExecuteAsync(sql, BuildParameters(broker));
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM financial_engine.brokers WHERE Id = @Id";
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await conn.ExecuteAsync(sql, new { Id = id });
        return rows > 0;
    }

    public async Task<bool> SetStatusAsync(long id, bool status, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE financial_engine.brokers SET Status = @Status WHERE Id = @Id";
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await conn.ExecuteAsync(sql, new { Id = id, Status = status });
        return rows > 0;
    }

    private static DynamicParameters BuildParameters(BrokerModel broker)
    {
        var p = new DynamicParameters();
        p.Add("Id", broker.Id);
        p.Add("Code", broker.Code);
        p.Add("Name", broker.Name);
        p.Add("Document", broker.Document);
        p.Add("ContractDate", broker.ContractDate);
        p.Add("Status", broker.Status);
        p.Add("BillingCurrency", broker.BillingCurrency);
        p.Add("Contacts", new NpgsqlTextArrayParameter(broker.Contacts));
        return p;
    }
}

/// <summary>
/// Wraps a string[] so Dapper sends it as a single PostgreSQL text[] parameter
/// instead of expanding it for IN clauses.
/// </summary>
public class NpgsqlTextArrayParameter : SqlMapper.ICustomQueryParameter
{
    private readonly string[] _value;

    public NpgsqlTextArrayParameter(string[]? value) => _value = value ?? [];

    public void AddParameter(IDbCommand command, string name)
    {
        var param = new NpgsqlParameter(name, NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = _value
        };
        command.Parameters.Add(param);
    }
}
