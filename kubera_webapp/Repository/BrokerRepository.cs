using Dapper;
using kubera_webapp.Models;
using Npgsql;
using System.Data;

namespace kubera_webapp.Repository;

public class BrokerRepository
{
    private readonly IDbConnection _db;

    public BrokerRepository(IConfiguration config)
    {
        _db = new NpgsqlConnection(config.GetConnectionString("Default"));
    }

    public async Task<IEnumerable<BrokerModel>> ObterTodosAsync()
    {
        var sql = "SELECT * FROM brokers";
        return await _db.QueryAsync<BrokerModel>(sql);
    }

    public async Task<BrokerModel> CriarAsync(BrokerModel cliente)
    {
        var sql = @"INSERT INTO brokers (Id, Nome, Email)
                    VALUES (@Id, @Nome, @Email)";
        cliente.Id = Guid.NewGuid();
        await _db.ExecuteAsync(sql, cliente);
        return cliente;
    }
}
