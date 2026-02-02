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
}
