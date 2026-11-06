namespace FinancialEngineCore.Model;

/// <summary>
/// Resultado agregado do cálculo de billing por (Trader, Broker, Dia, AssetClass).
/// </summary>
public class DailyBilling
{
    public long Id { get; set; }

    public int Broker { get; set; }
    public required string Trader { get; set; }
    /// <summary>Tipo da chave: TRADER (Trader = EnteringTrader) ou ENTITY (Trader = Entity)</summary>
    public string KeyType { get; set; } = BillingKeyTypes.Trader;
    public DateOnly TradeDate { get; set; }

    /// <summary>"EQUITY" ou "BMF"</summary>
    public required string AssetClass { get; set; }

    // Totais do dia
    public decimal TotalNotional { get; set; }
    public decimal TotalQty { get; set; }
    public decimal TotalFee { get; set; }

    /// <summary>Acumulado do mês até o dia anterior</summary>
    public decimal AccumulatedBefore { get; set; }

    /// <summary>Acumulado do mês até este dia (inclusive)</summary>
    public decimal AccumulatedAfter { get; set; }

    /// <summary>ID da config de EQUITY usada (se AssetClass = EQUITY)</summary>
    public long? EquityConfigId { get; set; }

    /// <summary>ID da config de BMF usada (se AssetClass = BMF)</summary>
    public long? BmfConfigId { get; set; }

    /// <summary>ID do ProcessBilling que gerou este cálculo</summary>
    public int ProcessBillingId { get; set; }

    public DateTime CalculatedAt { get; set; }
}
