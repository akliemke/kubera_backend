using Dapper;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Infra;

public class MarketDataFeeConfigRepository
{
    private readonly DbConnectionFactory _dbConnectionFactory;

    public MarketDataFeeConfigRepository(DbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<IEnumerable<MarketDataFeeConfig>> GetAllActiveAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM financial_engine.market_data_fee_configs
            WHERE IsActive = TRUE
            ORDER BY Broker, ValidFrom";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryAsync<MarketDataFeeConfig>(sql);
    }

    public async Task<MarketDataFeeConfig?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        const string sql = "SELECT * FROM financial_engine.market_data_fee_configs WHERE Id = @Id";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryFirstOrDefaultAsync<MarketDataFeeConfig>(sql, new { Id = id });
    }

    public async Task<MarketDataFeeConfig> CreateAsync(MarketDataFeeConfig config, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO financial_engine.market_data_fee_configs
                (Broker, AmountPerUser, ValidFrom, ValidTo, IsActive)
            VALUES
                (@Broker, @AmountPerUser, @ValidFrom, @ValidTo, @IsActive)
            RETURNING *";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QuerySingleAsync<MarketDataFeeConfig>(sql, config);
    }

    public async Task<bool> UpdateAsync(MarketDataFeeConfig config, CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE financial_engine.market_data_fee_configs
            SET Broker = @Broker, AmountPerUser = @AmountPerUser,
                ValidFrom = @ValidFrom, ValidTo = @ValidTo, IsActive = @IsActive,
                UpdatedAt = NOW()
            WHERE Id = @Id";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.ExecuteAsync(sql, config) > 0;
    }

    public async Task<bool> DeactivateAsync(long id, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE financial_engine.market_data_fee_configs SET IsActive = FALSE, UpdatedAt = NOW() WHERE Id = @Id";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.ExecuteAsync(sql, new { Id = id }) > 0;
    }

    /// <summary>
    /// Preços ativos com vigência que toca o período [firstDay, lastDay]. Broker opcional.
    /// </summary>
    public async Task<IEnumerable<MarketDataFeeConfig>> GetByPeriodAsync(
        DateOnly firstDay, DateOnly lastDay, int? brokerId, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM financial_engine.market_data_fee_configs
            WHERE IsActive = TRUE
              AND ValidFrom <= @LastDay
              AND ValidTo >= @FirstDay
              AND (@BrokerId::int IS NULL OR Broker = @BrokerId)
            ORDER BY Broker, ValidFrom";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryAsync<MarketDataFeeConfig>(sql, new { FirstDay = firstDay, LastDay = lastDay, BrokerId = brokerId });
    }
}
