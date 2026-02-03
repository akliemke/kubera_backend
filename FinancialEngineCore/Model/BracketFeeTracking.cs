namespace FinancialEngineCore.Model;

/// <summary>
/// Tracking do fee cobrado em cada faixa de EQUITY por dia.
/// Permite aplicar CAPs por faixa corretamente ao longo do mês.
/// </summary>
public class BracketFeeTracking
{
    public long Id { get; set; }

    /// <summary>ID do DailyBilling associado</summary>
    public long DailyBillingId { get; set; }

    /// <summary>ID da faixa (EquityFeeBracket)</summary>
    public long BracketId { get; set; }

    /// <summary>Volume (notional) que caiu nesta faixa neste dia</summary>
    public decimal NotionalInBracket { get; set; }

    /// <summary>Fee calculado antes de aplicar o CAP</summary>
    public decimal CalculatedFee { get; set; }

    /// <summary>Fee efetivamente cobrado (após aplicar CAP)</summary>
    public decimal ChargedFee { get; set; }

    /// <summary>Fee acumulado na faixa antes deste dia (no mês)</summary>
    public decimal AccumulatedFeeBefore { get; set; }

    /// <summary>CAP da faixa no momento do cálculo</summary>
    public decimal BracketCap { get; set; }
}
