namespace FinancialEngineCore.Infra;

public class BillingRepository
{
    private readonly DbConnectionFactory _dbConnectionFactory;

    public BillingRepository(DbConnectionFactory connectionFactory)
    {
        _dbConnectionFactory = connectionFactory;
    }
}
