using Dapper;
using FinancialEngineCore.Model;

namespace FinancialEngineCore.Infra;

public class ProcessBillingRepository
{
    private readonly DbConnectionFactory _dbConnectionFactory;

    public ProcessBillingRepository(DbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<List<ProcessBilling>> GetAllAsync(CancellationToken cancellationToken)
    {
        const string sql = "SELECT * FROM management.process_billing";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var billings = await conn.QueryAsync<ProcessBilling>(sql);
        return billings.ToList();
    }

    public async Task<ProcessBilling?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        const string sql = "SELECT * FROM management.process_billing WHERE ProcessId = @Id";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var billing = await conn.QueryFirstOrDefaultAsync<ProcessBilling>(sql, new { Id = id });
        return billing;
    }

    public async Task SaveAsync(ProcessBilling processBilling, CancellationToken cancellationToken)
    {
        const string sql = @"INSERT INTO management.process_billing (ProcessId, ProcessDate, Status, ProcessBroker)
                             VALUES (@ProcessId, @ProcessDate, @Status, @ProcessBroker)";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync(sql, processBilling);
    }

    public async Task UpdateAsync(ProcessBilling processBilling, CancellationToken cancellationToken)
    {
        const string sql = @"UPDATE management.process_billing
                             SET ProcessDate = @ProcessDate, Status = @Status, ProcessBroker = @ProcessBroker
                             WHERE ProcessId = @ProcessId";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync(sql, processBilling);
    }
}
