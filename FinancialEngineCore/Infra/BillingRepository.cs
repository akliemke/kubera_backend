using Dapper;
using FinancialEngineCore.Model;
using KuberaWebApp.Infra;

namespace FinancialEngineCore.Infra;
public class BillingRepository
{
    private DbConnectionFactory _dbConnectionFactory;

    public BillingRepository(DbConnectionFactory sqlConnectionFactory)
    {
        _dbConnectionFactory = sqlConnectionFactory;

    }

    public async Task<List<ProcessBilling>> GetAll(CancellationToken ct)
    {
        var sql = "SELECT * FROM management.process_billing";
        using var conn = await _dbConnectionFactory.CreateOpenConnectionAsync(ct);
        var billings = await conn.QueryAsync<ProcessBilling>(sql);
        return new List<ProcessBilling>(billings);
    }

    public void Save(ProcessBilling processBilling)
    {
        // Simulate saving the ProcessBilling record
    }

    public void Update(ProcessBilling processBilling)
    {
        // Simulate updating the ProcessBilling record
    }

    public ProcessBilling GetById(int id)
    {
        return new ProcessBilling
        {
            ProcessId = 2,
            ProcessDate = DateTime.UtcNow.AddDays(-1),
            Status = "Pending",
            ProcessBroker = "BrokerB"
        };
    }
}
