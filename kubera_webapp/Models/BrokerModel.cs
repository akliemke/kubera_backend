namespace kubera_webapp.Models;

public class BrokerModel
{
    public long Id { get; set; }
    public int Code { get; set; }
    public required string Name { get; set; }
    public required List<string> Contacts { get; set; }
    public required string Document { get; set; }
    public DateTime ContractDate { get; set; }
    public bool Status { get; set; }
    public required string BillingCurrency { get; set; }
}
