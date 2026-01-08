using Dapper;
using kubera_webapp.Models;
using Npgsql;
using System.Collections.Concurrent;
using System.Data;

namespace kubera_webapp.Infra;

public class BrokerRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private ConcurrentBag<BrokerModel> _brokers;
    public readonly ConcurrentDictionary<string, BrokerModel> BrokerByCode;

    public BrokerRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<BrokerModel>> GetAllAsync(CancellationToken cts)
    {
        var sql = "SELECT * FROM management.brokers";
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cts);
        var brokers = await conn.QueryAsync<BrokerModel>(sql);
        _brokers = new ConcurrentBag<BrokerModel>(brokers);
        return brokers;
    }

    public async Task<BrokerModel> CreateAsync(BrokerModel cliente, CancellationToken cts)
    {
        var sql = @"INSERT INTO brokers (Id, Nome, Email)
                    VALUES (@Id, @Nome, @Email)";
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cts);
        await conn.ExecuteAsync(sql, cliente);
        return cliente;
    }

    public async Task UpdateBrokerAsync(BrokerModel broker, CancellationToken cts)
    {
        var sql = @"UPDATE brokers SET Nome = @Nome, Email = @Email WHERE Id = @Id";
        using var conn = await _connectionFactory.CreateOpenConnectionAsync(cts);
        await conn.ExecuteAsync(sql, broker);
    }
}
