using System.Data;
using Dapper;
using FinancialEngineCore.Model;
using Npgsql;
using NpgsqlTypes;

namespace FinancialEngineCore.Infra;

public class DailyBillingRepository
{
    private readonly DbConnectionFactory _dbConnectionFactory;

    public DailyBillingRepository(DbConnectionFactory connectionFactory)
    {
        _dbConnectionFactory = connectionFactory;
    }

    /// <summary>
    /// Busca o acumulado do mês para um (Trader, Broker, AssetClass) até uma data.
    /// </summary>
    public async Task<decimal> GetMonthAccumulatedAsync(
        int brokerId,
        string trader,
        string assetClass,
        DateOnly upToDate,
        CancellationToken cancellationToken)
    {
        var monthStart = new DateOnly(upToDate.Year, upToDate.Month, 1);

        const string sql = @"
            SELECT COALESCE(SUM(
                CASE WHEN @AssetClass = 'EQUITY' THEN TotalNotional ELSE TotalQty END
            ), 0)
            FROM financial_engine.daily_billing
            WHERE Broker = @BrokerId
              AND Trader = @Trader
              AND AssetClass = @AssetClass
              AND TradeDate >= @MonthStart
              AND TradeDate < @UpToDate";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QuerySingleAsync<decimal>(sql, new
        {
            BrokerId = brokerId,
            Trader = trader,
            AssetClass = assetClass,
            MonthStart = monthStart,
            UpToDate = upToDate
        });
    }

    /// <summary>
    /// Busca o fee já cobrado no mês para um (Trader, Broker, AssetClass) até uma data.
    /// Usado para aplicar CAPs mensais (BMF).
    /// </summary>
    public async Task<decimal> GetMonthFeeChargedAsync(
        int brokerId,
        string trader,
        string assetClass,
        DateOnly upToDate,
        CancellationToken cancellationToken)
    {
        var monthStart = new DateOnly(upToDate.Year, upToDate.Month, 1);

        const string sql = @"
            SELECT COALESCE(SUM(TotalFee), 0)
            FROM financial_engine.daily_billing
            WHERE Broker = @BrokerId
              AND Trader = @Trader
              AND AssetClass = @AssetClass
              AND TradeDate >= @MonthStart
              AND TradeDate < @UpToDate";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QuerySingleAsync<decimal>(sql, new
        {
            BrokerId = brokerId,
            Trader = trader,
            AssetClass = assetClass,
            MonthStart = monthStart,
            UpToDate = upToDate
        });
    }

    /// <summary>
    /// Insere um novo registro de billing diário.
    /// </summary>
    public async Task<long> InsertAsync(DailyBilling billing, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO financial_engine.daily_billing
                (Broker, Trader, TradeDate, AssetClass,
                 TotalNotional, TotalQty, TotalFee,
                 AccumulatedBefore, AccumulatedAfter,
                 EquityConfigId, BmfConfigId,
                 ProcessBillingId, CalculatedAt)
            VALUES
                (@Broker, @Trader, @TradeDate, @AssetClass,
                 @TotalNotional, @TotalQty, @TotalFee,
                 @AccumulatedBefore, @AccumulatedAfter,
                 @EquityConfigId, @BmfConfigId,
                 @ProcessBillingId, @CalculatedAt)
            RETURNING Id";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QuerySingleAsync<long>(sql, billing);
    }

    /// <summary>
    /// Deleta registros de um dia específico para reprocessamento.
    /// </summary>
    public async Task DeleteByDateAsync(
        int brokerId,
        DateOnly tradeDate,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            DELETE FROM financial_engine.daily_billing
            WHERE Broker = @BrokerId AND TradeDate = @TradeDate";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync(sql, new { BrokerId = brokerId, TradeDate = tradeDate });
    }

    /// <summary>
    /// Deleta registros de um dia específico (transactional overload).
    /// CASCADE handles order_billing and bracket_fee_tracking.
    /// </summary>
    public async Task DeleteByDateAsync(
        int brokerId,
        DateOnly tradeDate,
        IDbConnection conn,
        IDbTransaction txn)
    {
        const string sql = @"
            DELETE FROM financial_engine.daily_billing
            WHERE Broker = @BrokerId AND TradeDate = @TradeDate";

        await conn.ExecuteAsync(sql, new { BrokerId = brokerId, TradeDate = tradeDate }, transaction: txn);
    }

    /// <summary>
    /// Reserva N Ids da sequence de daily_billing em um único round-trip,
    /// permitindo inserir pais e filhos via COPY sem precisar de RETURNING.
    /// </summary>
    public async Task<long[]> ReserveIdsAsync(int count, IDbConnection conn, IDbTransaction txn)
    {
        if (count == 0)
            return [];

        const string sql = @"
            SELECT nextval(pg_get_serial_sequence('financial_engine.daily_billing', 'id'))
            FROM generate_series(1, @Count)";

        var ids = await conn.QueryAsync<long>(sql, new { Count = count }, transaction: txn);
        return ids.ToArray();
    }

    /// <summary>
    /// Insere registros de billing diário via binary COPY (Ids já reservados).
    /// Participa da transação aberta na conexão.
    /// </summary>
    public async Task CopyAsync(IReadOnlyList<DailyBilling> billings, NpgsqlConnection conn, CancellationToken cancellationToken)
    {
        if (billings.Count == 0)
            return;

        const string copy = @"
            COPY financial_engine.daily_billing
                (Id, Broker, Trader, TradeDate, AssetClass,
                 TotalNotional, TotalQty, TotalFee,
                 AccumulatedBefore, AccumulatedAfter,
                 EquityConfigId, BmfConfigId,
                 ProcessBillingId, CalculatedAt)
            FROM STDIN (FORMAT BINARY)";

        await using var writer = await conn.BeginBinaryImportAsync(copy, cancellationToken);
        foreach (var b in billings)
        {
            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(b.Id, NpgsqlDbType.Bigint, cancellationToken);
            await writer.WriteAsync(b.Broker, NpgsqlDbType.Integer, cancellationToken);
            await writer.WriteAsync(b.Trader, NpgsqlDbType.Varchar, cancellationToken);
            await writer.WriteAsync(b.TradeDate, NpgsqlDbType.Date, cancellationToken);
            await writer.WriteAsync(b.AssetClass, NpgsqlDbType.Varchar, cancellationToken);
            await writer.WriteAsync(b.TotalNotional, NpgsqlDbType.Numeric, cancellationToken);
            await writer.WriteAsync(b.TotalQty, NpgsqlDbType.Numeric, cancellationToken);
            await writer.WriteAsync(b.TotalFee, NpgsqlDbType.Numeric, cancellationToken);
            await writer.WriteAsync(b.AccumulatedBefore, NpgsqlDbType.Numeric, cancellationToken);
            await writer.WriteAsync(b.AccumulatedAfter, NpgsqlDbType.Numeric, cancellationToken);
            await WriteNullableAsync(writer, b.EquityConfigId, cancellationToken);
            await WriteNullableAsync(writer, b.BmfConfigId, cancellationToken);
            await writer.WriteAsync(b.ProcessBillingId, NpgsqlDbType.Integer, cancellationToken);
            // Coluna TIMESTAMP (sem timezone): grava o valor UTC como Unspecified
            await writer.WriteAsync(DateTime.SpecifyKind(b.CalculatedAt, DateTimeKind.Unspecified), NpgsqlDbType.Timestamp, cancellationToken);
        }
        await writer.CompleteAsync(cancellationToken);
    }

    private static async Task WriteNullableAsync(NpgsqlBinaryImporter writer, long? value, CancellationToken cancellationToken)
    {
        if (value.HasValue)
            await writer.WriteAsync(value.Value, NpgsqlDbType.Bigint, cancellationToken);
        else
            await writer.WriteNullAsync(cancellationToken);
    }

    /// <summary>
    /// Deleta registros de um trader/broker/dia específico para reprocessamento.
    /// </summary>
    public async Task DeleteAsync(
        int brokerId,
        string trader,
        DateOnly tradeDate,
        string assetClass,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            DELETE FROM financial_engine.daily_billing
            WHERE Broker = @BrokerId
              AND Trader = @Trader
              AND TradeDate = @TradeDate
              AND AssetClass = @AssetClass";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync(sql, new
        {
            BrokerId = brokerId,
            Trader = trader,
            TradeDate = tradeDate,
            AssetClass = assetClass
        });
    }

    /// <summary>
    /// Busca todos os registros de billing de um dia específico.
    /// </summary>
    public async Task<List<DailyBilling>> GetByDateAsync(
        DateOnly tradeDate,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM financial_engine.daily_billing
            WHERE TradeDate = @TradeDate
            ORDER BY Broker, Trader, AssetClass";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var result = await conn.QueryAsync<DailyBilling>(sql, new { TradeDate = tradeDate });
        return result.ToList();
    }

    /// <summary>
    /// Busca resumo de billing agrupado por Broker/AssetClass para um mês.
    /// </summary>
    public async Task<List<MonthlyBillingSummary>> GetMonthSummaryAsync(
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1);

        const string sql = @"
            SELECT
                Broker,
                AssetClass,
                SUM(TotalNotional) as TotalNotional,
                SUM(TotalQty) as TotalQty,
                SUM(TotalFee) as TotalFee,
                COUNT(DISTINCT Trader) as TraderCount,
                COUNT(DISTINCT TradeDate) as TradingDays
            FROM financial_engine.daily_billing
            WHERE TradeDate >= @MonthStart AND TradeDate < @MonthEnd
            GROUP BY Broker, AssetClass
            ORDER BY Broker, AssetClass";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var result = await conn.QueryAsync<MonthlyBillingSummary>(sql, new { MonthStart = monthStart, MonthEnd = monthEnd });
        return result.ToList();
    }

    /// <summary>
    /// Busca resumo do mês completo para um broker, agrupado por (Trader, AssetClass).
    /// Usado para relatório PDF mensal.
    /// </summary>
    public async Task<List<TraderMonthSummary>> GetFullMonthSummaryByBrokerAsync(
        int brokerId,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1);

        const string sql = @"
            SELECT Trader, AssetClass,
                   COALESCE(SUM(TotalNotional), 0) as TotalNotional,
                   COALESCE(SUM(TotalQty), 0) as TotalQty,
                   COALESCE(SUM(TotalFee), 0) as TotalFee
            FROM financial_engine.daily_billing
            WHERE Broker = @BrokerId
              AND TradeDate >= @MonthStart
              AND TradeDate < @MonthEnd
            GROUP BY Trader, AssetClass";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var result = await conn.QueryAsync<TraderMonthSummary>(sql, new
        {
            BrokerId = brokerId,
            MonthStart = monthStart,
            MonthEnd = monthEnd
        });
        return result.ToList();
    }

    /// <summary>
    /// Fee por (Broker, BillingKey, Mês) de todos os brokers num período dentro de um mesmo ano.
    /// Base para o ajuste de piso nos relatórios.
    /// </summary>
    public async Task<List<TraderMonthSummary>> GetFeeByBillingKeyAndMonthAsync(
        DateOnly from,
        DateOnly toExclusive,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT Broker, Trader, EXTRACT(MONTH FROM TradeDate)::int as Month, '' as AssetClass,
                   COALESCE(SUM(TotalNotional), 0) as TotalNotional,
                   COALESCE(SUM(TotalQty), 0) as TotalQty,
                   COALESCE(SUM(TotalFee), 0) as TotalFee
            FROM financial_engine.daily_billing
            WHERE TradeDate >= @From
              AND TradeDate < @ToExclusive
            GROUP BY Broker, Trader, EXTRACT(MONTH FROM TradeDate)";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var result = await conn.QueryAsync<TraderMonthSummary>(sql, new
        {
            From = from,
            ToExclusive = toExclusive
        });
        return result.ToList();
    }

    /// <summary>
    /// Busca resumo do mês inteiro para um broker até uma data (bulk).
    /// Retorna accumulated + fee per (Trader, AssetClass).
    /// Roda na transação informada para enxergar dias recém-reprocessados (cascata).
    /// </summary>
    public async Task<IReadOnlyList<TraderMonthSummary>> GetMonthSummaryByBrokerAsync(
        int brokerId,
        DateOnly upToDate,
        IDbConnection conn,
        IDbTransaction txn)
    {
        var monthStart = new DateOnly(upToDate.Year, upToDate.Month, 1);

        const string sql = @"
            SELECT Trader, AssetClass,
                   COALESCE(SUM(TotalNotional), 0) as TotalNotional,
                   COALESCE(SUM(TotalQty), 0) as TotalQty,
                   COALESCE(SUM(TotalFee), 0) as TotalFee
            FROM financial_engine.daily_billing
            WHERE Broker = @BrokerId
              AND TradeDate >= @MonthStart
              AND TradeDate < @UpToDate
            GROUP BY Trader, AssetClass";

        var result = await conn.QueryAsync<TraderMonthSummary>(sql, new
        {
            BrokerId = brokerId,
            MonthStart = monthStart,
            UpToDate = upToDate
        }, transaction: txn);
        return result.ToList();
    }

    /// <summary>
    /// Lock de processamento por broker (liberado no fim da transação).
    /// Serializa processamentos concorrentes do mesmo broker, cujos acumulados mensais se interligam.
    /// </summary>
    public async Task AcquireBrokerLockAsync(int brokerId, IDbConnection conn, IDbTransaction txn)
    {
        const int billingLockNamespace = 7001;
        await conn.ExecuteAsync(
            "SELECT pg_advisory_xact_lock(@Namespace, @BrokerId)",
            new { Namespace = billingLockNamespace, BrokerId = brokerId },
            transaction: txn);
    }

    /// <summary>
    /// Datas já processadas do broker no mesmo mês, posteriores a uma data.
    /// Usado para reprocessar em cascata (acumulados e CAPs dependem dos dias anteriores).
    /// </summary>
    public async Task<List<DateOnly>> GetProcessedDatesAfterAsync(
        int brokerId,
        DateOnly date,
        IDbConnection conn,
        IDbTransaction txn)
    {
        var nextMonthStart = new DateOnly(date.Year, date.Month, 1).AddMonths(1);

        const string sql = @"
            SELECT DISTINCT TradeDate
            FROM financial_engine.daily_billing
            WHERE Broker = @BrokerId
              AND TradeDate > @Date
              AND TradeDate < @NextMonthStart
            ORDER BY TradeDate";

        var result = await conn.QueryAsync<DateOnly>(sql, new
        {
            BrokerId = brokerId,
            Date = date,
            NextMonthStart = nextMonthStart
        }, transaction: txn);
        return result.ToList();
    }

    /// <summary>
    /// Busca resumo de billing agrupado por Broker/AssetClass/Mês para um ano.
    /// </summary>
    public async Task<List<AnnualBillingSummary>> GetYearSummaryAsync(
        int year,
        CancellationToken cancellationToken)
    {
        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year + 1, 1, 1);

        const string sql = @"
            SELECT
                Broker,
                AssetClass,
                EXTRACT(MONTH FROM TradeDate)::int as Month,
                SUM(TotalNotional) as TotalNotional,
                SUM(TotalQty) as TotalQty,
                SUM(TotalFee) as TotalFee
            FROM financial_engine.daily_billing
            WHERE TradeDate >= @YearStart AND TradeDate < @YearEnd
            GROUP BY Broker, AssetClass, EXTRACT(MONTH FROM TradeDate)
            ORDER BY Broker, AssetClass, Month";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var result = await conn.QueryAsync<AnnualBillingSummary>(sql, new { YearStart = yearStart, YearEnd = yearEnd });
        return result.ToList();
    }
}

/// <summary>
/// Resumo mensal de billing por Broker/AssetClass.
/// </summary>
public class MonthlyBillingSummary
{
    public int Broker { get; set; }
    public required string AssetClass { get; set; }
    public decimal TotalNotional { get; set; }
    public decimal TotalQty { get; set; }
    public decimal TotalFee { get; set; }
    public int TraderCount { get; set; }
    public int TradingDays { get; set; }
}

/// <summary>
/// Resumo anual de billing por Broker/AssetClass/Mês.
/// </summary>
public class AnnualBillingSummary
{
    public int Broker { get; set; }
    public required string AssetClass { get; set; }
    public int Month { get; set; }
    public decimal TotalNotional { get; set; }
    public decimal TotalQty { get; set; }
    public decimal TotalFee { get; set; }
}

/// <summary>
/// Resumo mensal por (Trader, AssetClass) — usado para pre-load bulk.
/// </summary>
public class TraderMonthSummary
{
    /// <summary>Preenchido apenas em consultas multi-broker</summary>
    public int Broker { get; set; }
    /// <summary>Preenchido apenas em consultas por período</summary>
    public int Month { get; set; }
    public required string Trader { get; set; }
    public required string AssetClass { get; set; }
    public decimal TotalNotional { get; set; }
    public decimal TotalQty { get; set; }
    public decimal TotalFee { get; set; }
}
