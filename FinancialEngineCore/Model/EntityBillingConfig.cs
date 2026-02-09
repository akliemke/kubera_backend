namespace FinancialEngineCore.Model;

public class EntityBillingConfig
{
    public long Id { get; set; }
    public int Broker { get; set; }
    public string Entity { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public bool IsActive { get; set; }
}
