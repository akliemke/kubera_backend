namespace FinancialEngineCore.Model;

/// <summary>
/// Fee calculado por ordem individual (proporcional ao volume/quantidade).
/// Usado para rastreabilidade da origem do cálculo.
/// </summary>
public class OrderBilling
{
    public long Id { get; set; }

    /// <summary>ID da ordem original</summary>
    public required string OrderId { get; set; }

    /// <summary>Raiz do ativo. Sintético gera uma linha por perna (mesmo OrderId, Asset WDO/DOL)</summary>
    public string? Asset { get; set; }

    /// <summary>Vínculo com o cálculo diário agregado</summary>
    public long DailyBillingId { get; set; }

    public int Broker { get; set; }
    public required string Trader { get; set; }
    /// <summary>Tipo da chave: TRADER (Trader = EnteringTrader) ou ENTITY (Trader = Entity)</summary>
    public string KeyType { get; set; } = BillingKeyTypes.Trader;
    public DateOnly TradeDate { get; set; }

    /// <summary>"EQUITY" ou "BMF"</summary>
    public required string AssetClass { get; set; }

    /// <summary>Volume financeiro da ordem (para EQUITY)</summary>
    public decimal Notional { get; set; }

    /// <summary>Quantidade de contratos (para BMF)</summary>
    public decimal Qty { get; set; }

    /// <summary>Fee calculado proporcional: (Notional ou Qty) / Total × FeeTotalDia</summary>
    public decimal FeeAmount { get; set; }
}
