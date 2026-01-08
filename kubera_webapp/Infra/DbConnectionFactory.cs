using Npgsql;
using System.Data;

namespace kubera_webapp.Infra;

public class DbConnectionFactory
{
}

public interface IDbConnectionFactory
{
    Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken ct);
}

public sealed class SqlConnectionFactory(IConfiguration config) : IDbConnectionFactory
{
    private readonly string _cs = config.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionString 'Default' não configurada.");

    public async Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        return conn;
    }
}
