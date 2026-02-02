using Dapper;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Infra;

public class BmfFeeConfigRepository
{
    private readonly DbConnectionFactory _dbConnectionFactory;

    public BmfFeeConfigRepository(DbConnectionFactory connectionFactory)
    {
        _dbConnectionFactory = connectionFactory;
    }

    /// <summary>
    /// Busca configuração BMF aplicável para um (Trader, Broker) em uma data específica.
    /// Prioridade: regra especial (Trader preenchido) > regra padrão (Trader NULL).
    /// Se existe regra especial, usa 100% ela (sem fallback).
    /// </summary>
    public async Task<BmfFeeConfig?> GetApplicableConfigAsync(
        int brokerId,
        string trader,
        DateTime tradeDate,
        CancellationToken cancellationToken)
    {
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);

        // Primeiro tenta buscar regra especial para o trader
        const string sqlSpecial = @"
            SELECT * FROM financial_engine.bmf_fee_configs
            WHERE Broker = @BrokerId
              AND Trader = @Trader
              AND IsActive = TRUE
              AND @TradeDate >= ValidFrom
              AND @TradeDate < ValidTo
            LIMIT 1";

        var specialConfig = await conn.QueryFirstOrDefaultAsync<BmfFeeConfig>(
            sqlSpecial,
            new { BrokerId = brokerId, Trader = trader, TradeDate = tradeDate });

        if (specialConfig != null)
        {
            return specialConfig;
        }

        // Se não existe regra especial, busca regra padrão do broker
        const string sqlDefault = @"
            SELECT * FROM financial_engine.bmf_fee_configs
            WHERE Broker = @BrokerId
              AND Trader IS NULL
              AND IsActive = TRUE
              AND @TradeDate >= ValidFrom
              AND @TradeDate < ValidTo
            LIMIT 1";

        var defaultConfig = await conn.QueryFirstOrDefaultAsync<BmfFeeConfig>(
            sqlDefault,
            new { BrokerId = brokerId, TradeDate = tradeDate });

        return defaultConfig;
    }

    /// <summary>
    /// Busca todas as configurações ativas (para administração/listagem).
    /// </summary>
    public async Task<IEnumerable<BmfFeeConfig>> GetAllActiveAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM financial_engine.bmf_fee_configs
            WHERE IsActive = TRUE
            ORDER BY Broker, Trader";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryAsync<BmfFeeConfig>(sql);
    }
}
