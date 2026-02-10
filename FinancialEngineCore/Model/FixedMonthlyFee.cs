namespace FinancialEngineCore.Model;

public class FixedMonthlyFee
{
    public long Id { get; set; }
    public int Broker { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly ValidTo { get; set; }
    public bool IsActive { get; set; }
}
