using Dapper;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Infra;

public class EquityFeeBracketRepository
{
    private readonly DbConnectionFactory _dbConnectionFactory;

    public EquityFeeBracketRepository(DbConnectionFactory connectionFactory)
    {
        _dbConnectionFactory = connectionFactory;
    }

    /// <summary>
    /// Busca todas as faixas ativas (para administração/listagem).
    /// </summary>
    public async Task<IEnumerable<EquityFeeBracket>> GetAllActiveAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM financial_engine.equity_fee_brackets
            WHERE IsActive = TRUE
            ORDER BY Broker, Trader, NotionalFrom ASC";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryAsync<EquityFeeBracket>(sql);
    }

    public async Task<EquityFeeBracket?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        const string sql = "SELECT * FROM financial_engine.equity_fee_brackets WHERE Id = @Id";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryFirstOrDefaultAsync<EquityFeeBracket>(sql, new { Id = id });
    }

    public async Task<IEnumerable<EquityFeeBracket>> GetByBrokerAsync(int brokerId, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM financial_engine.equity_fee_brackets
            WHERE Broker = @BrokerId AND IsActive = TRUE
            ORDER BY Trader, NotionalFrom ASC";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryAsync<EquityFeeBracket>(sql, new { BrokerId = brokerId });
    }

    public async Task<EquityFeeBracket> CreateAsync(EquityFeeBracket bracket, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO financial_engine.equity_fee_brackets
                (Broker, Trader, KeyType, ValidFrom, ValidTo, NotionalFrom, NotionalTo, Bps, Cap, IsActive)
            VALUES
                (@Broker, @Trader, @KeyType, @ValidFrom, @ValidTo, @NotionalFrom, @NotionalTo, @Bps, @Cap, @IsActive)
            RETURNING *";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QuerySingleAsync<EquityFeeBracket>(sql, bracket);
    }

    public async Task<bool> UpdateAsync(EquityFeeBracket bracket, CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE financial_engine.equity_fee_brackets
            SET Broker = @Broker, Trader = @Trader, KeyType = @KeyType, ValidFrom = @ValidFrom, ValidTo = @ValidTo,
                NotionalFrom = @NotionalFrom, NotionalTo = @NotionalTo, Bps = @Bps, Cap = @Cap, IsActive = @IsActive
            WHERE Id = @Id";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await conn.ExecuteAsync(sql, bracket);
        return rows > 0;
    }

    public async Task<bool> DeactivateAsync(long id, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE financial_engine.equity_fee_brackets SET IsActive = FALSE WHERE Id = @Id";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await conn.ExecuteAsync(sql, new { Id = id });
        return rows > 0;
    }

    /// <summary>
    /// Busca todas as faixas ativas de um broker para uma data (todos os traders + default).
    /// Usado para pre-load bulk — resolução special/default é feita em memória.
    /// </summary>
    public async Task<IReadOnlyList<EquityFeeBracket>> GetAllByBrokerAndDateAsync(
        int brokerId,
        DateOnly tradeDate,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM financial_engine.equity_fee_brackets
            WHERE Broker = @BrokerId
              AND IsActive = TRUE
              AND @TradeDate >= ValidFrom
              AND @TradeDate <= ValidTo
            ORDER BY Trader NULLS LAST, NotionalFrom ASC";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var result = await conn.QueryAsync<EquityFeeBracket>(
            sql, new { BrokerId = brokerId, TradeDate = tradeDate });
        return result.ToList();
    }
}
