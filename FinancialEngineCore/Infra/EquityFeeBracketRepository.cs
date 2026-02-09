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
    /// Busca faixas aplicáveis para um (Trader, Broker) em uma data específica.
    /// Prioridade: regra especial (Trader preenchido) > regra padrão (Trader NULL).
    /// Se existe regra especial, usa 100% ela (sem fallback).
    /// </summary>
    public async Task<IEnumerable<EquityFeeBracket>> GetApplicableBracketsAsync(
        int brokerId,
        string trader,
        DateTime tradeDate,
        CancellationToken cancellationToken)
    {
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);

        // Primeiro tenta buscar regra especial para o trader
        const string sqlSpecial = @"
            SELECT * FROM financial_engine.equity_fee_brackets
            WHERE Broker = @BrokerId
              AND Trader = @Trader
              AND IsActive = TRUE
              AND @TradeDate >= ValidFrom
              AND @TradeDate < ValidTo
            ORDER BY NotionalFrom ASC";

        var specialBrackets = await conn.QueryAsync<EquityFeeBracket>(
            sqlSpecial,
            new { BrokerId = brokerId, Trader = trader, TradeDate = tradeDate });

        if (specialBrackets.Any())
        {
            return specialBrackets;
        }

        // Se não existe regra especial, busca regra padrão do broker
        const string sqlDefault = @"
            SELECT * FROM financial_engine.equity_fee_brackets
            WHERE Broker = @BrokerId
              AND Trader IS NULL
              AND IsActive = TRUE
              AND @TradeDate >= ValidFrom
              AND @TradeDate < ValidTo
            ORDER BY NotionalFrom ASC";

        var defaultBrackets = await conn.QueryAsync<EquityFeeBracket>(
            sqlDefault,
            new { BrokerId = brokerId, TradeDate = tradeDate });

        return defaultBrackets;
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
                (Broker, Trader, ValidFrom, ValidTo, NotionalFrom, NotionalTo, Bps, Cap, IsActive)
            VALUES
                (@Broker, @Trader, @ValidFrom, @ValidTo, @NotionalFrom, @NotionalTo, @Bps, @Cap, @IsActive)
            RETURNING *";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QuerySingleAsync<EquityFeeBracket>(sql, bracket);
    }

    public async Task<bool> UpdateAsync(EquityFeeBracket bracket, CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE financial_engine.equity_fee_brackets
            SET Broker = @Broker, Trader = @Trader, ValidFrom = @ValidFrom, ValidTo = @ValidTo,
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
        DateTime tradeDate,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM financial_engine.equity_fee_brackets
            WHERE Broker = @BrokerId
              AND IsActive = TRUE
              AND @TradeDate >= ValidFrom
              AND @TradeDate < ValidTo
            ORDER BY Trader NULLS LAST, NotionalFrom ASC";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var result = await conn.QueryAsync<EquityFeeBracket>(
            sql, new { BrokerId = brokerId, TradeDate = tradeDate });
        return result.ToList();
    }
}
