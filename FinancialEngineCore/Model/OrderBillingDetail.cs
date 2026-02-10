namespace FinancialEngineCore.Model;

/// <summary>
/// Order billing com dados da ordem original (Symbol, Side) para relatórios.
/// </summary>
public class OrderBillingDetail
{
    public long OrderId { get; set; }
    public int Broker { get; set; }
    public required string Trader { get; set; }
    public DateOnly TradeDate { get; set; }
    public required string AssetClass { get; set; }
    public decimal Notional { get; set; }
    public decimal Qty { get; set; }
    public decimal FeeAmount { get; set; }
    public string? Symbol { get; set; }
    public string? Side { get; set; }
}
