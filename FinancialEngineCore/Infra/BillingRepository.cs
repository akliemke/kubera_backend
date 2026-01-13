using FinancialEngineCore.Model;

namespace FinancialEngineCore.Infra;
public class BillingRepository
{
    public BillingRepository()
    {

    }

    public List<ProcessBilling> GetAll()
    {
        return new List<ProcessBilling>
        {
            new ProcessBilling
            {
                ProcessId = 1,
                ProcessDate = DateTime.UtcNow,
                Status = "Completed",
                ProcessBroker = "BrokerA"
            },
            new ProcessBilling
            {
                ProcessId = 2,
                ProcessDate = DateTime.UtcNow.AddDays(-1),
                Status = "Pending",
                ProcessBroker = "BrokerB"
            }
        };
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
