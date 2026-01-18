namespace FinancialEngineCore.Model;

public class Order
{
    public decimal CumQty { get; set; }
    public decimal AvgPx { get; set; }
    public string Symbol { get; set; }
    public string EnteringTrader { get; set; }
    public string Entity { get; set; }
    public DateTime TradeDate { get; set; }
    public string EnteringFirm { get; set; }
    public string OmsInstance { get; set; }
    public string Side { get; set; }
}