using Dapper;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Infra;

public class MarketDataExclusionRepository
{
    private readonly DbConnectionFactory _dbConnectionFactory;

    public MarketDataExclusionRepository(DbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<IEnumerable<MarketDataExclusion>> GetAllActiveAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM financial_engine.market_data_exclusions
            WHERE IsActive = TRUE
            ORDER BY Broker NULLS FIRST, Trader";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryAsync<MarketDataExclusion>(sql);
    }

    public async Task<MarketDataExclusion?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        const string sql = "SELECT * FROM financial_engine.market_data_exclusions WHERE Id = @Id";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryFirstOrDefaultAsync<MarketDataExclusion>(sql, new { Id = id });
    }

    /// <summary>
    /// Exclusão ativa para o mesmo (Broker, Trader), ignorando maiúsculas/minúsculas.
    /// </summary>
    public async Task<MarketDataExclusion?> FindActiveAsync(int? broker, string trader, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM financial_engine.market_data_exclusions
            WHERE IsActive = TRUE
              AND COALESCE(Broker, 0) = COALESCE(@Broker::int, 0)
              AND UPPER(Trader) = UPPER(@Trader)";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryFirstOrDefaultAsync<MarketDataExclusion>(sql, new { Broker = broker, Trader = trader });
    }

    public async Task<MarketDataExclusion> CreateAsync(MarketDataExclusion exclusion, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO financial_engine.market_data_exclusions
                (Broker, Trader, Reason, IsActive)
            VALUES
                (@Broker, @Trader, @Reason, @IsActive)
            RETURNING *";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QuerySingleAsync<MarketDataExclusion>(sql, exclusion);
    }

    public async Task<bool> UpdateAsync(MarketDataExclusion exclusion, CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE financial_engine.market_data_exclusions
            SET Broker = @Broker, Trader = @Trader, Reason = @Reason, IsActive = @IsActive,
                UpdatedAt = NOW()
            WHERE Id = @Id";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.ExecuteAsync(sql, exclusion) > 0;
    }

    public async Task<bool> DeactivateAsync(long id, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE financial_engine.market_data_exclusions SET IsActive = FALSE, UpdatedAt = NOW() WHERE Id = @Id";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.ExecuteAsync(sql, new { Id = id }) > 0;
    }
}
