using System.Data;
using Dapper;
using FinancialEngineCore.Model;

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
        DateTime upToDate,
        CancellationToken cancellationToken)
    {
        var monthStart = new DateTime(upToDate.Year, upToDate.Month, 1);

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
        DateTime upToDate,
        CancellationToken cancellationToken)
    {
        var monthStart = new DateTime(upToDate.Year, upToDate.Month, 1);

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
        DateTime tradeDate,
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
        DateTime tradeDate,
        IDbConnection conn,
        IDbTransaction txn)
    {
        const string sql = @"
            DELETE FROM financial_engine.daily_billing
            WHERE Broker = @BrokerId AND TradeDate = @TradeDate";

        await conn.ExecuteAsync(sql, new { BrokerId = brokerId, TradeDate = tradeDate }, transaction: txn);
    }

    /// <summary>
    /// Insere um novo registro de billing diário (transactional overload).
    /// </summary>
    public async Task<long> InsertAsync(DailyBilling billing, IDbConnection conn, IDbTransaction txn)
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

        return await conn.QuerySingleAsync<long>(sql, billing, transaction: txn);
    }

    /// <summary>
    /// Deleta registros de um trader/broker/dia específico para reprocessamento.
    /// </summary>
    public async Task DeleteAsync(
        int brokerId,
        string trader,
        DateTime tradeDate,
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
        DateTime tradeDate,
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
        var monthStart = new DateTime(year, month, 1);
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
    /// Busca resumo do mês inteiro para um broker até uma data (bulk).
    /// Retorna accumulated + fee per (Trader, AssetClass).
    /// </summary>
    public async Task<IReadOnlyList<TraderMonthSummary>> GetMonthSummaryByBrokerAsync(
        int brokerId,
        DateTime upToDate,
        CancellationToken cancellationToken)
    {
        var monthStart = new DateTime(upToDate.Year, upToDate.Month, 1);

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

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var result = await conn.QueryAsync<TraderMonthSummary>(sql, new
        {
            BrokerId = brokerId,
            MonthStart = monthStart,
            UpToDate = upToDate
        });
        return result.ToList();
    }

    /// <summary>
    /// Busca resumo de billing agrupado por Broker/AssetClass/Mês para um ano.
    /// </summary>
    public async Task<List<AnnualBillingSummary>> GetYearSummaryAsync(
        int year,
        CancellationToken cancellationToken)
    {
        var yearStart = new DateTime(year, 1, 1);
        var yearEnd = yearStart.AddYears(1);

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
    public required string Trader { get; set; }
    public required string AssetClass { get; set; }
    public decimal TotalNotional { get; set; }
    public decimal TotalQty { get; set; }
    public decimal TotalFee { get; set; }
}
