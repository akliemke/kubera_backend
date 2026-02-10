using Dapper;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Infra;

public class FixedMonthlyFeeRepository
{
    private readonly DbConnectionFactory _dbConnectionFactory;

    public FixedMonthlyFeeRepository(DbConnectionFactory connectionFactory)
    {
        _dbConnectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<FixedMonthlyFee>> GetAllActiveAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM financial_engine.fixed_monthly_fees
            WHERE IsActive = TRUE
            ORDER BY Broker, Description";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryAsync<FixedMonthlyFee>(sql);
    }

    public async Task<FixedMonthlyFee?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        const string sql = "SELECT * FROM financial_engine.fixed_monthly_fees WHERE Id = @Id";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryFirstOrDefaultAsync<FixedMonthlyFee>(sql, new { Id = id });
    }

    public async Task<FixedMonthlyFee> CreateAsync(FixedMonthlyFee fee, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO financial_engine.fixed_monthly_fees
                (Broker, Description, Amount, ValidFrom, ValidTo, IsActive)
            VALUES
                (@Broker, @Description, @Amount, @ValidFrom, @ValidTo, @IsActive)
            RETURNING *";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QuerySingleAsync<FixedMonthlyFee>(sql, fee);
    }

    public async Task<bool> UpdateAsync(FixedMonthlyFee fee, CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE financial_engine.fixed_monthly_fees
            SET Broker = @Broker, Description = @Description, Amount = @Amount,
                ValidFrom = @ValidFrom, ValidTo = @ValidTo, IsActive = @IsActive,
                UpdatedAt = NOW()
            WHERE Id = @Id";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await conn.ExecuteAsync(sql, fee);
        return rows > 0;
    }

    public async Task<bool> DeactivateAsync(long id, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE financial_engine.fixed_monthly_fees SET IsActive = FALSE, UpdatedAt = NOW() WHERE Id = @Id";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await conn.ExecuteAsync(sql, new { Id = id });
        return rows > 0;
    }

    public async Task<IEnumerable<FixedMonthlyFee>> GetByBrokerAndMonthAsync(
        int brokerId, int year, int month, CancellationToken cancellationToken)
    {
        var firstDay = new DateOnly(year, month, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);

        const string sql = @"
            SELECT * FROM financial_engine.fixed_monthly_fees
            WHERE Broker = @BrokerId
              AND IsActive = TRUE
              AND ValidFrom <= @LastDay
              AND ValidTo >= @FirstDay
            ORDER BY Description";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryAsync<FixedMonthlyFee>(sql, new { BrokerId = brokerId, FirstDay = firstDay, LastDay = lastDay });
    }
}
