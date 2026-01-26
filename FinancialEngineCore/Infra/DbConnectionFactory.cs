using Npgsql;
using System.Data;

namespace FinancialEngineCore.Infra;

public sealed class DbConnectionFactory(IConfiguration config)
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
