namespace FinancialEngineCore.Model;

/// <summary>
/// Preço mensal de Market Data por usuário com conta vinculada ao broker.
/// </summary>
public class MarketDataFeeConfig
{
    public long Id { get; set; }
    public int Broker { get; set; }
    public decimal AmountPerUser { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly ValidTo { get; set; }
    public bool IsActive { get; set; }
}
