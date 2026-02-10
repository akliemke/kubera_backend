namespace FinancialEngineCore.Model;

public class BrokerModel
{
    public long Id { get; set; }
    public int Code { get; set; }
    public required string Name { get; set; }
    public required string[] Contacts { get; set; }
    public required string Document { get; set; }
    public DateOnly? ContractDate { get; set; }
    public bool Status { get; set; }
    public required string BillingCurrency { get; set; }
}
