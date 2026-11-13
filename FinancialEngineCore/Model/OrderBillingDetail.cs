namespace FinancialEngineCore.Model;

/// <summary>
/// Order billing com dados da ordem original (Symbol, Side) para relatórios.
/// </summary>
public class OrderBillingDetail
{
    public required string OrderId { get; set; }
    public int Broker { get; set; }
    public required string Trader { get; set; }
    /// <summary>Tipo da chave: TRADER (Trader = EnteringTrader) ou ENTITY (Trader = Entity)</summary>
    public string KeyType { get; set; } = BillingKeyTypes.Trader;
    public DateOnly TradeDate { get; set; }
    public required string AssetClass { get; set; }
    public decimal Notional { get; set; }
    public decimal Qty { get; set; }
    public decimal FeeAmount { get; set; }
    public string? Symbol { get; set; }
    /// <summary>Raiz do ativo / perna do sintético (ex: WDO em BTGDOLZ26)</summary>
    public string? Asset { get; set; }
    public string? Side { get; set; }
}
