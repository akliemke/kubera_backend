using Dapper;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Infra;

public class PriceModificationFactorRepository
{
    private readonly DbConnectionFactory _dbConnectionFactory;

    public PriceModificationFactorRepository(DbConnectionFactory connectionFactory)
    {
        _dbConnectionFactory = connectionFactory;
    }

    public async Task<PriceModificationFactor?> GetBySymbolAndDateAsync(
        string symbol, DateOnly tradeDate, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM financial_engine.price_modification_factors
            WHERE Symbol = @Symbol
              AND IsActive = TRUE
              AND @TradeDate >= ValidFrom
              AND @TradeDate <= ValidTo
            LIMIT 1";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryFirstOrDefaultAsync<PriceModificationFactor>(
            sql, new { Symbol = symbol, TradeDate = tradeDate });
    }

    public async Task<IEnumerable<PriceModificationFactor>> GetAllByDateAsync(
        DateOnly tradeDate, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM financial_engine.price_modification_factors
            WHERE IsActive = TRUE
              AND @TradeDate >= ValidFrom
              AND @TradeDate <= ValidTo
            ORDER BY Symbol";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryAsync<PriceModificationFactor>(
            sql, new { TradeDate = tradeDate });
    }

    public async Task<IEnumerable<PriceModificationFactor>> GetAllActiveAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM financial_engine.price_modification_factors
            WHERE IsActive = TRUE
            ORDER BY Symbol";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryAsync<PriceModificationFactor>(sql);
    }

    public async Task<PriceModificationFactor?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        const string sql = "SELECT * FROM financial_engine.price_modification_factors WHERE Id = @Id";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryFirstOrDefaultAsync<PriceModificationFactor>(sql, new { Id = id });
    }

    public async Task<PriceModificationFactor> CreateAsync(
        PriceModificationFactor factor, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO financial_engine.price_modification_factors
                (Symbol, Factor, ValidFrom, ValidTo, IsActive)
            VALUES
                (@Symbol, @Factor, @ValidFrom, @ValidTo, @IsActive)
            RETURNING *";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QuerySingleAsync<PriceModificationFactor>(sql, factor);
    }

    public async Task<bool> UpdateAsync(PriceModificationFactor factor, CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE financial_engine.price_modification_factors
            SET Symbol = @Symbol, Factor = @Factor, ValidFrom = @ValidFrom,
                ValidTo = @ValidTo, IsActive = @IsActive
            WHERE Id = @Id";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await conn.ExecuteAsync(sql, factor);
        return rows > 0;
    }

    public async Task<bool> DeactivateAsync(long id, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE financial_engine.price_modification_factors SET IsActive = FALSE WHERE Id = @Id";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await conn.ExecuteAsync(sql, new { Id = id });
        return rows > 0;
    }
}
