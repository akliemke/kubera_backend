using KuberaWebApp.Infra;

namespace FinancialEngineCore.Infra;
public class BillingRepository
{
    private DbConnectionFactory _dbConnectionFactory;

    public BillingRepository(DbConnectionFactory connectionFactory)
    {
        _dbConnectionFactory = connectionFactory; 
    }
}
