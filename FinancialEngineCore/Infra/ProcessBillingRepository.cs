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

    public async Task<List<ProcessBilling>> GetPendingAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT * FROM management.process_billing
            WHERE Status = 'Pending'
            ORDER BY ProcessId";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var billings = await conn.QueryAsync<ProcessBilling>(sql);
        return billings.ToList();
    }

    /// <summary>
    /// Attempts to acquire a lock on a pending billing for processing.
    /// Uses SELECT FOR UPDATE SKIP LOCKED to avoid conflicts with other workers.
    /// Returns the billing if lock acquired, null otherwise.
    /// </summary>
    public async Task<ProcessBilling?> TryAcquireNextAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE management.process_billing
            SET Status = 'Processing', LockedAt = NOW()
            WHERE ProcessId = (
                SELECT ProcessId FROM management.process_billing
                WHERE Status = 'Pending'
                ORDER BY ProcessId
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            RETURNING *";

        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return await conn.QueryFirstOrDefaultAsync<ProcessBilling>(sql);
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
