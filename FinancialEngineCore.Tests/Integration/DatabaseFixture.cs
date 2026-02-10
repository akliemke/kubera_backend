using System.Data;
using Dapper;
using Npgsql;
using Testcontainers.PostgreSql;

namespace FinancialEngineCore.Tests.Integration;

public class DatabaseFixture : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithUsername("admin")
            .WithPassword("admin")
            .WithDatabase("kubera_test")
            .Build();

        await _container.StartAsync();
        await RunInitScriptAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    private async Task RunInitScriptAsync()
    {
        var initSqlPath = Path.Combine(AppContext.BaseDirectory, "Integration", "init.sql");
        var sql = await File.ReadAllTextAsync(initSqlPath);

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(sql);
    }

    /// Cleans all data from all tables (for test isolation).
    public async Task CleanupAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            TRUNCATE financial_engine.daily_billing RESTART IDENTITY CASCADE;
            TRUNCATE financial_engine.equity_fee_brackets RESTART IDENTITY CASCADE;
            TRUNCATE financial_engine.bmf_fee_configs RESTART IDENTITY CASCADE;
            TRUNCATE financial_engine.price_modification_factors RESTART IDENTITY CASCADE;
            TRUNCATE financial_engine.entity_billing_configs RESTART IDENTITY CASCADE;
            TRUNCATE history.orders RESTART IDENTITY CASCADE;
            TRUNCATE management.brokers RESTART IDENTITY CASCADE;
        ");
    }
}

public class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override DateOnly Parse(object value) => value switch
    {
        DateTime dt => DateOnly.FromDateTime(dt),
        DateOnly d => d,
        _ => DateOnly.FromDateTime(Convert.ToDateTime(value))
    };

    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.Value = value.ToDateTime(TimeOnly.MinValue);
        parameter.DbType = DbType.Date;
    }
}
