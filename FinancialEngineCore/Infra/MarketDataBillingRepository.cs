using System.Data;
using Dapper;
using FinancialEngineCore.Domain;
using FinancialEngineCore.Model;
using Npgsql;
using NpgsqlTypes;

namespace FinancialEngineCore.Infra;

/// <summary>
/// Apuração mensal de Market Data (congelada por mês).
/// </summary>
public class MarketDataBillingRepository
{
    private readonly DbConnectionFactory _dbConnectionFactory;

    public MarketDataBillingRepository(DbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    /// <summary>
    /// Substitui a apuração de um broker no mês (delete + binary COPY) na transação informada.
    /// </summary>
    public async Task ReplaceMonthAsync(
        int brokerId,
        int year,
        int month,
        IReadOnlyList<MarketDataCharge> charges,
        NpgsqlConnection conn,
        IDbTransaction txn,
        CancellationToken cancellationToken)
    {
        await conn.ExecuteAsync(@"
            DELETE FROM financial_engine.market_data_billing
            WHERE Broker = @BrokerId AND Year = @Year AND Month = @Month",
            new { BrokerId = brokerId, Year = year, Month = month }, transaction: txn);

        if (charges.Count == 0)
            return;

        var calculatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        await using var writer = await conn.BeginBinaryImportAsync(@"
            COPY financial_engine.market_data_billing
                (Broker, Year, Month, Trader, Amount, ConfigId, CalculatedAt)
            FROM STDIN (FORMAT BINARY)", cancellationToken);

        foreach (var charge in charges)
        {
            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(brokerId, NpgsqlDbType.Integer, cancellationToken);
            await writer.WriteAsync(year, NpgsqlDbType.Integer, cancellationToken);
            await writer.WriteAsync(month, NpgsqlDbType.Integer, cancellationToken);
            await writer.WriteAsync(charge.Trader, NpgsqlDbType.Varchar, cancellationToken);
            await writer.WriteAsync(charge.Amount, NpgsqlDbType.Numeric, cancellationToken);
            await writer.WriteAsync(charge.ConfigId, NpgsqlDbType.Bigint, cancellationToken);
            await writer.WriteAsync(calculatedAt, NpgsqlDbType.Timestamp, cancellationToken);
        }
        await writer.CompleteAsync(cancellationToken);
    }

    /// <summary>
    /// Apuração de todos os brokers num intervalo de meses de um ano. Broker opcional.
    /// </summary>
    public async Task<List<MarketDataBilling>> GetByPeriodAsync(
        int year, int fromMonth, int toMonth, int? brokerId, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM financial_engine.market_data_billing
            WHERE Year = @Year
              AND Month BETWEEN @FromMonth AND @ToMonth
              AND (@BrokerId::int IS NULL OR Broker = @BrokerId)
            ORDER BY Broker, Month, Trader";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var result = await conn.QueryAsync<MarketDataBilling>(sql, new
        {
            Year = year,
            FromMonth = fromMonth,
            ToMonth = toMonth,
            BrokerId = brokerId
        });
        return result.ToList();
    }
}
