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
              AND @TradeDate <= ValidTo
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
                (Broker, Entity, ValidFrom, ValidTo, IsActive, FloorFee)
            VALUES
                (@Broker, @Entity, @ValidFrom, @ValidTo, @IsActive, @FloorFee)
            RETURNING *";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QuerySingleAsync<EntityBillingConfig>(sql, config);
    }

    public async Task<bool> UpdateAsync(EntityBillingConfig config, CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE financial_engine.entity_billing_configs
            SET Broker = @Broker, Entity = @Entity, ValidFrom = @ValidFrom,
                ValidTo = @ValidTo, IsActive = @IsActive, FloorFee = @FloorFee,
                UpdatedAt = NOW()
            WHERE Id = @Id";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await conn.ExecuteAsync(sql, config);
        return rows > 0;
    }

    public async Task<IEnumerable<EntityBillingConfig>> GetByBrokerAndMonthAsync(
        int brokerId, int year, int month, CancellationToken cancellationToken)
    {
        var firstDay = new DateOnly(year, month, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);

        const string sql = @"
            SELECT * FROM financial_engine.entity_billing_configs
            WHERE Broker = @BrokerId
              AND IsActive = TRUE
              AND ValidFrom <= @LastDay
              AND ValidTo >= @FirstDay
            ORDER BY Entity";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryAsync<EntityBillingConfig>(sql, new { BrokerId = brokerId, FirstDay = firstDay, LastDay = lastDay });
    }

    /// <summary>
    /// Configs de todos os brokers com vigência que toca o período [firstDay, lastDay].
    /// </summary>
    public async Task<IEnumerable<EntityBillingConfig>> GetByPeriodAsync(
        DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM financial_engine.entity_billing_configs
            WHERE IsActive = TRUE
              AND ValidFrom <= @LastDay
              AND ValidTo >= @FirstDay
            ORDER BY Broker, Entity";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryAsync<EntityBillingConfig>(sql, new { FirstDay = firstDay, LastDay = lastDay });
    }

    public async Task<bool> DeactivateAsync(long id, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE financial_engine.entity_billing_configs SET IsActive = FALSE, UpdatedAt = NOW() WHERE Id = @Id";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await conn.ExecuteAsync(sql, new { Id = id });
        return rows > 0;
    }
}
