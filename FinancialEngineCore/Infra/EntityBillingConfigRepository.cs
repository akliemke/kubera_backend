using Dapper;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Infra;

public class EntityBillingConfigRepository
{
    private readonly DbConnectionFactory _dbConnectionFactory;

    public EntityBillingConfigRepository(DbConnectionFactory connectionFactory)
    {
        _dbConnectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<EntityBillingConfig>> GetByBrokerAndDateAsync(
        int brokerId, DateOnly tradeDate, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM financial_engine.entity_billing_configs
            WHERE Broker = @Broker
              AND IsActive = TRUE
              AND @TradeDate >= ValidFrom
              AND @TradeDate < ValidTo
            ORDER BY Entity";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryAsync<EntityBillingConfig>(
            sql, new { Broker = brokerId, TradeDate = tradeDate });
    }

    public async Task<IEnumerable<EntityBillingConfig>> GetAllActiveAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM financial_engine.entity_billing_configs
            WHERE IsActive = TRUE
            ORDER BY Broker, Entity";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryAsync<EntityBillingConfig>(sql);
    }

    public async Task<EntityBillingConfig?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        const string sql = "SELECT * FROM financial_engine.entity_billing_configs WHERE Id = @Id";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryFirstOrDefaultAsync<EntityBillingConfig>(sql, new { Id = id });
    }

    public async Task<EntityBillingConfig> CreateAsync(
        EntityBillingConfig config, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO financial_engine.entity_billing_configs
                (Broker, Entity, ValidFrom, ValidTo, IsActive)
            VALUES
                (@Broker, @Entity, @ValidFrom, @ValidTo, @IsActive)
            RETURNING *";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QuerySingleAsync<EntityBillingConfig>(sql, config);
    }

    public async Task<bool> UpdateAsync(EntityBillingConfig config, CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE financial_engine.entity_billing_configs
            SET Broker = @Broker, Entity = @Entity, ValidFrom = @ValidFrom,
                ValidTo = @ValidTo, IsActive = @IsActive, UpdatedAt = NOW()
            WHERE Id = @Id";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await conn.ExecuteAsync(sql, config);
        return rows > 0;
    }

    public async Task<bool> DeactivateAsync(long id, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE financial_engine.entity_billing_configs SET IsActive = FALSE, UpdatedAt = NOW() WHERE Id = @Id";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await conn.ExecuteAsync(sql, new { Id = id });
        return rows > 0;
    }
}
