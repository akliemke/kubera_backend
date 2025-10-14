using Dapper;
using kubera_webapp.Models;
using Npgsql;
using System.Collections.Concurrent;
using System.Data;

namespace kubera_webapp.Repository;

public class BrokerRepository
{
    private readonly IDbConnection _db;
    private ConcurrentBag<BrokerModel> _brokers;
    public readonly ConcurrentDictionary<string, BrokerModel> BrokerByCode;

    public BrokerRepository(IConfiguration config)
    {
        _db = new NpgsqlConnection(config.GetConnectionString("Default"));
    }

    public async Task<IEnumerable<BrokerModel>> GetAllAsync()
    {
        var sql = "SELECT * FROM brokers";
        var brokers = await _db.QueryAsync<BrokerModel>(sql);
        _brokers = new ConcurrentBag<BrokerModel>(brokers);
        return brokers;
    }

    public async Task<BrokerModel> CreateAsync(BrokerModel cliente)
    {
        var sql = @"INSERT INTO brokers (Id, Nome, Email)
                    VALUES (@Id, @Nome, @Email)";
        cliente.Id = Guid.NewGuid();
        await _db.ExecuteAsync(sql, cliente);
        return cliente;
    }

    public async Task UpdateBrokerAsync(BrokerModel broker)
    {
        var sql = @"UPDATE brokers SET Nome = @Nome, Email = @Email WHERE Id = @Id";
        await _db.ExecuteAsync(sql, broker);
    }
}
