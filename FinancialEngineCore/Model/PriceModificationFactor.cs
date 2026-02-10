namespace FinancialEngineCore.Model;

public class PriceModificationFactor
{
    public long Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public decimal Factor { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly ValidTo { get; set; }
    public bool IsActive { get; set; }
}
