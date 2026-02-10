namespace FinancialEngineCore.Domain;

/// <summary>
/// Resultado do cálculo de billing para um grupo (Trader, Broker, Dia, AssetClass).
/// </summary>
public class BillingCalculationResult
{
    public required string Trader { get; init; }
    public int Broker { get; init; }
    public DateOnly TradeDate { get; init; }
    public required string AssetClass { get; init; }

    /// <summary>Volume financeiro total do dia (EQUITY)</summary>
    public decimal TotalNotional { get; init; }

    /// <summary>Quantidade total de contratos do dia (BMF)</summary>
    public decimal TotalQty { get; init; }

    /// <summary>Fee total calculado para o dia</summary>
    public decimal TotalFee { get; init; }

    /// <summary>Acumulado do mês antes deste dia</summary>
    public decimal AccumulatedBefore { get; init; }

    /// <summary>Acumulado do mês após este dia</summary>
    public decimal AccumulatedAfter { get; init; }

    /// <summary>ID da config usada (EquityFeeBracket ou BmfFeeConfig)</summary>
    public long? ConfigId { get; init; }

    /// <summary>Fee distribuído proporcionalmente por ordem</summary>
    public required List<OrderFeeResult> OrderFees { get; init; }

    /// <summary>Fee cobrado por faixa (apenas EQUITY)</summary>
    public List<BracketFeeResult> BracketFees { get; init; } = new();
}

/// <summary>
/// Fee calculado para uma ordem individual.
/// </summary>
public class OrderFeeResult
{
    public long OrderId { get; init; }
    public decimal Notional { get; init; }
    public decimal Qty { get; init; }
    public decimal FeeAmount { get; init; }
}

/// <summary>
/// Fee calculado para uma faixa específica (EQUITY).
/// </summary>
public class BracketFeeResult
{
    public long BracketId { get; init; }
    public decimal NotionalInBracket { get; init; }
    public decimal CalculatedFee { get; init; }
    public decimal ChargedFee { get; init; }
    public decimal AccumulatedFeeBefore { get; init; }
    public decimal BracketCap { get; init; }
}
