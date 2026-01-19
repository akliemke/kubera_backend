namespace FinancialEngineCore.Model;
public class FeeBracket
{
    public long Id { get; set; }
    public int Broker { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public decimal NotionalFrom { get; set; }
    public decimal NotionalTo { get; set; }
    public decimal Bps { get; set; }
    public bool IsActive { get; set; }

}
