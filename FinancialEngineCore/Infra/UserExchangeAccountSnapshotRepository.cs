using Dapper;
using FinancialEngineCore.Model;
using NpgsqlTypes;

namespace FinancialEngineCore.Infra;

/// <summary>
/// Snapshots diários dos vínculos usuário ↔ broker vindos da API externa (dado bruto, sem exclusões).
/// </summary>
public class UserExchangeAccountSnapshotRepository
{
    private readonly DbConnectionFactory _dbConnectionFactory;

    public UserExchangeAccountSnapshotRepository(DbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    /// <summary>
    /// Substitui o snapshot de uma data (delete + binary COPY). Idempotente.
    /// </summary>
    public async Task ReplaceSnapshotAsync(DateOnly date, IEnumerable<UserExchangeAccount> accounts, CancellationToken cancellationToken)
    {
        var rows = accounts
            .Where(a => a.Broker > 0 && !string.IsNullOrWhiteSpace(a.Trader))
            .Select(a => a with { Trader = a.Trader.Trim() })
            .DistinctBy(a => (a.Broker, a.Trader))
            .ToList();

        await using var conn = await _dbConnectionFactory.CreateOpenNpgsqlConnectionAsync(cancellationToken);
        await using var txn = await conn.BeginTransactionAsync(cancellationToken);

        await conn.ExecuteAsync(
            "DELETE FROM financial_engine.user_exchange_account_snapshots WHERE SnapshotDate = @Date",
            new { Date = date }, transaction: txn);

        await using (var writer = await conn.BeginBinaryImportAsync(
            "COPY financial_engine.user_exchange_account_snapshots (SnapshotDate, Broker, Trader) FROM STDIN (FORMAT BINARY)",
            cancellationToken))
        {
            foreach (var row in rows)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(date, NpgsqlDbType.Date, cancellationToken);
                await writer.WriteAsync(row.Broker, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(row.Trader, NpgsqlDbType.Varchar, cancellationToken);
            }
            await writer.CompleteAsync(cancellationToken);
        }

        await txn.CommitAsync(cancellationToken);
    }

    public async Task<bool> HasSnapshotAsync(DateOnly date, CancellationToken cancellationToken)
    {
        const string sql = "SELECT EXISTS (SELECT 1 FROM financial_engine.user_exchange_account_snapshots WHERE SnapshotDate = @Date)";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.ExecuteScalarAsync<bool>(sql, new { Date = date });
    }

    /// <summary>
    /// Vínculos (Broker, Trader) vistos em qualquer snapshot do mês. Broker opcional.
    /// </summary>
    public async Task<List<UserExchangeAccount>> GetAccountsInMonthAsync(
        int year, int month, int? brokerId, CancellationToken cancellationToken)
    {
        var monthStart = new DateOnly(year, month, 1);

        const string sql = @"
            SELECT DISTINCT Broker, Trader
            FROM financial_engine.user_exchange_account_snapshots
            WHERE SnapshotDate >= @MonthStart
              AND SnapshotDate < @MonthEnd
              AND (@BrokerId::int IS NULL OR Broker = @BrokerId)
            ORDER BY Broker, Trader";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await conn.QueryAsync<(int Broker, string Trader)>(sql, new
        {
            MonthStart = monthStart,
            MonthEnd = monthStart.AddMonths(1),
            BrokerId = brokerId
        });
        return rows.Select(r => new UserExchangeAccount(r.Broker, r.Trader)).ToList();
    }

    /// <summary>Última data com snapshot (para exibir status do sync).</summary>
    public async Task<DateOnly?> GetLastSnapshotDateAsync(CancellationToken cancellationToken)
    {
        const string sql = "SELECT MAX(SnapshotDate) FROM financial_engine.user_exchange_account_snapshots";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var result = await conn.ExecuteScalarAsync<DateTime?>(sql);
        return result.HasValue ? DateOnly.FromDateTime(result.Value) : null;
    }
}
