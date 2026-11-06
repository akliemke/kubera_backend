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
                (Broker, Trader, KeyType, ValidFrom, ValidTo, ValorMini, ValorCheio,
                 DI1Venc1a4, DI1Venc5, DI1Venc6, DI1Venc7, DI1Venc8Plus, CapMensal, IsActive)
            VALUES
                (@Broker, @Trader, @KeyType, @ValidFrom, @ValidTo, @ValorMini, @ValorCheio,
                 @DI1Venc1a4, @DI1Venc5, @DI1Venc6, @DI1Venc7, @DI1Venc8Plus, @CapMensal, @IsActive)
            RETURNING *";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QuerySingleAsync<BmfFeeConfig>(sql, config);
    }

    public async Task<bool> UpdateAsync(BmfFeeConfig config, CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE financial_engine.bmf_fee_configs
            SET Broker = @Broker, Trader = @Trader, KeyType = @KeyType, ValidFrom = @ValidFrom, ValidTo = @ValidTo,
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

    /// <summary>
    /// Busca todas as configs ativas de um broker para uma data (todos os traders + default).
    /// Usado para pre-load bulk — resolução special/default é feita em memória.
    /// </summary>
    public async Task<IReadOnlyList<BmfFeeConfig>> GetAllByBrokerAndDateAsync(
        int brokerId,
        DateOnly tradeDate,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM financial_engine.bmf_fee_configs
            WHERE Broker = @BrokerId
              AND IsActive = TRUE
              AND @TradeDate >= ValidFrom
              AND @TradeDate < ValidTo
            ORDER BY Trader NULLS LAST";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var result = await conn.QueryAsync<BmfFeeConfig>(
            sql, new { BrokerId = brokerId, TradeDate = tradeDate });
        return result.ToList();
    }
}
