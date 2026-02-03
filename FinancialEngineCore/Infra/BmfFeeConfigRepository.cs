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

    public async Task<BmfFeeConfig?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        const string sql = "SELECT * FROM financial_engine.bmf_fee_configs WHERE Id = @Id";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryFirstOrDefaultAsync<BmfFeeConfig>(sql, new { Id = id });
    }

    public async Task<IEnumerable<BmfFeeConfig>> GetByBrokerAsync(int brokerId, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM financial_engine.bmf_fee_configs
            WHERE Broker = @BrokerId AND IsActive = TRUE
            ORDER BY Trader";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryAsync<BmfFeeConfig>(sql, new { BrokerId = brokerId });
    }

    public async Task<BmfFeeConfig> CreateAsync(BmfFeeConfig config, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO financial_engine.bmf_fee_configs
                (Broker, Trader, ValidFrom, ValidTo, ValorMini, ValorCheio,
                 DI1Venc1a4, DI1Venc5, DI1Venc6, DI1Venc7, DI1Venc8Plus, CapMensal, IsActive)
            VALUES
                (@Broker, @Trader, @ValidFrom, @ValidTo, @ValorMini, @ValorCheio,
                 @DI1Venc1a4, @DI1Venc5, @DI1Venc6, @DI1Venc7, @DI1Venc8Plus, @CapMensal, @IsActive)
            RETURNING *";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QuerySingleAsync<BmfFeeConfig>(sql, config);
    }

    public async Task<bool> UpdateAsync(BmfFeeConfig config, CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE financial_engine.bmf_fee_configs
            SET Broker = @Broker, Trader = @Trader, ValidFrom = @ValidFrom, ValidTo = @ValidTo,
                ValorMini = @ValorMini, ValorCheio = @ValorCheio,
                DI1Venc1a4 = @DI1Venc1a4, DI1Venc5 = @DI1Venc5, DI1Venc6 = @DI1Venc6,
                DI1Venc7 = @DI1Venc7, DI1Venc8Plus = @DI1Venc8Plus, CapMensal = @CapMensal, IsActive = @IsActive
            WHERE Id = @Id";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await conn.ExecuteAsync(sql, config);
        return rows > 0;
    }

    public async Task<bool> DeactivateAsync(long id, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE financial_engine.bmf_fee_configs SET IsActive = FALSE WHERE Id = @Id";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await conn.ExecuteAsync(sql, new { Id = id });
        return rows > 0;
    }
}
