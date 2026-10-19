using System.Data;
using Dapper;
using FinancialEngineCore.Model;
using Npgsql;
using NpgsqlTypes;

namespace FinancialEngineCore.Infra;

public class BracketFeeTrackingRepository
{
    private readonly DbConnectionFactory _dbConnectionFactory;

    public BracketFeeTrackingRepository(DbConnectionFactory connectionFactory)
    {
        _dbConnectionFactory = connectionFactory;
    }

    /// <summary>
    /// Busca o fee acumulado por faixa no mês para um (Trader, Broker) até uma data.
    /// Retorna dicionário BracketId -> FeeAcumulado.
    /// </summary>
    public async Task<Dictionary<long, decimal>> GetMonthFeePerBracketAsync(
        int brokerId,
        string trader,
        DateOnly upToDate,
        CancellationToken cancellationToken)
    {
        var monthStart = new DateOnly(upToDate.Year, upToDate.Month, 1);

        const string sql = @"
            SELECT bft.BracketId, SUM(bft.ChargedFee) as TotalFee
            FROM financial_engine.bracket_fee_tracking bft
            INNER JOIN financial_engine.daily_billing db ON db.Id = bft.DailyBillingId
            WHERE db.Broker = @BrokerId
              AND db.Trader = @Trader
              AND db.AssetClass = 'EQUITY'
              AND db.TradeDate >= @MonthStart
              AND db.TradeDate < @UpToDate
            GROUP BY bft.BracketId";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var results = await conn.QueryAsync<(long BracketId, decimal TotalFee)>(sql, new
        {
            BrokerId = brokerId,
            Trader = trader,
            MonthStart = monthStart,
            UpToDate = upToDate
        });

        return results.ToDictionary(r => r.BracketId, r => r.TotalFee);
    }

    /// <summary>
    /// Insere múltiplos registros de tracking de fee por faixa.
    /// </summary>
    public async Task InsertBatchAsync(
        IEnumerable<BracketFeeTracking> trackings,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO financial_engine.bracket_fee_tracking
                (DailyBillingId, BracketId, NotionalInBracket, CalculatedFee, ChargedFee, AccumulatedFeeBefore, BracketCap)
            VALUES
                (@DailyBillingId, @BracketId, @NotionalInBracket, @CalculatedFee, @ChargedFee, @AccumulatedFeeBefore, @BracketCap)";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync(sql, trackings);
    }

    /// <summary>
    /// Insere múltiplos registros de tracking de fee por faixa via binary COPY (1 round-trip).
    /// Participa da transação aberta na conexão.
    /// </summary>
    public async Task CopyAsync(
        IReadOnlyList<BracketFeeTracking> trackings,
        NpgsqlConnection conn,
        CancellationToken cancellationToken)
    {
        if (trackings.Count == 0)
            return;

        const string copy = @"
            COPY financial_engine.bracket_fee_tracking
                (DailyBillingId, BracketId, NotionalInBracket, CalculatedFee, ChargedFee, AccumulatedFeeBefore, BracketCap)
            FROM STDIN (FORMAT BINARY)";

        await using var writer = await conn.BeginBinaryImportAsync(copy, cancellationToken);
        foreach (var t in trackings)
        {
            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(t.DailyBillingId, NpgsqlDbType.Bigint, cancellationToken);
            await writer.WriteAsync(t.BracketId, NpgsqlDbType.Bigint, cancellationToken);
            await writer.WriteAsync(t.NotionalInBracket, NpgsqlDbType.Numeric, cancellationToken);
            await writer.WriteAsync(t.CalculatedFee, NpgsqlDbType.Numeric, cancellationToken);
            await writer.WriteAsync(t.ChargedFee, NpgsqlDbType.Numeric, cancellationToken);
            await writer.WriteAsync(t.AccumulatedFeeBefore, NpgsqlDbType.Numeric, cancellationToken);
            if (t.BracketCap is { } cap)
                await writer.WriteAsync(cap, NpgsqlDbType.Numeric, cancellationToken);
            else
                await writer.WriteNullAsync(cancellationToken);
        }
        await writer.CompleteAsync(cancellationToken);
    }

    /// <summary>
    /// Deleta registros de tracking associados a um DailyBilling.
    /// </summary>
    public async Task DeleteByDailyBillingIdAsync(long dailyBillingId, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM financial_engine.bracket_fee_tracking WHERE DailyBillingId = @DailyBillingId";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync(sql, new { DailyBillingId = dailyBillingId });
    }

    /// <summary>
    /// Deleta registros de tracking para um broker/data (para reprocessamento).
    /// </summary>
    public async Task DeleteByDateAsync(int brokerId, DateOnly tradeDate, CancellationToken cancellationToken)
    {
        const string sql = @"
            DELETE FROM financial_engine.bracket_fee_tracking
            WHERE DailyBillingId IN (
                SELECT Id FROM financial_engine.daily_billing
                WHERE Broker = @BrokerId AND TradeDate = @TradeDate
            )";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync(sql, new { BrokerId = brokerId, TradeDate = tradeDate });
    }

    /// <summary>
    /// Busca o fee acumulado por faixa para TODOS os traders de um broker no mês (bulk).
    /// Agrupa por NotionalFrom da faixa (e não pelo Id) para que o CAP continue valendo
    /// quando a faixa muda de versão no meio do mês.
    /// Roda na transação informada para enxergar dias recém-reprocessados (cascata).
    /// </summary>
    public async Task<IReadOnlyList<TraderBracketFee>> GetMonthFeePerBracketByBrokerAsync(
        int brokerId,
        DateOnly upToDate,
        IDbConnection conn,
        IDbTransaction txn)
    {
        var monthStart = new DateOnly(upToDate.Year, upToDate.Month, 1);

        const string sql = @"
            SELECT db.Trader, efb.NotionalFrom, SUM(bft.ChargedFee) as TotalFee
            FROM financial_engine.bracket_fee_tracking bft
            INNER JOIN financial_engine.daily_billing db ON db.Id = bft.DailyBillingId
            INNER JOIN financial_engine.equity_fee_brackets efb ON efb.Id = bft.BracketId
            WHERE db.Broker = @BrokerId
              AND db.AssetClass = 'EQUITY'
              AND db.TradeDate >= @MonthStart
              AND db.TradeDate < @UpToDate
            GROUP BY db.Trader, efb.NotionalFrom";

        var result = await conn.QueryAsync<TraderBracketFee>(sql, new
        {
            BrokerId = brokerId,
            MonthStart = monthStart,
            UpToDate = upToDate
        }, transaction: txn);
        return result.ToList();
    }

    /// <summary>
    /// Busca tracking por DailyBillingId (para investigação/debug).
    /// </summary>
    public async Task<List<BracketFeeTracking>> GetByDailyBillingIdAsync(
        long dailyBillingId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM financial_engine.bracket_fee_tracking
            WHERE DailyBillingId = @DailyBillingId
            ORDER BY BracketId";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var results = await conn.QueryAsync<BracketFeeTracking>(sql, new { DailyBillingId = dailyBillingId });
        return results.ToList();
    }
}

/// <summary>
/// Fee acumulado por (Trader, NotionalFrom da faixa) — usado para pre-load bulk.
/// </summary>
public class TraderBracketFee
{
    public required string Trader { get; set; }
    public decimal NotionalFrom { get; set; }
    public decimal TotalFee { get; set; }
}
