namespace FinancialEngineCore.Model;

public class EntityBillingConfig
{
    public long Id { get; set; }
    public int Broker { get; set; }
    public string Entity { get; set; } = string.Empty;
    public DateOnly ValidFrom { get; set; }
    public DateOnly ValidTo { get; set; }
    public bool IsActive { get; set; }
    public decimal? FloorFee { get; set; }
}
