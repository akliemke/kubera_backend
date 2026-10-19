namespace FinancialEngineCore.Model;

/// <summary>
/// Faixa de cobrança para EQUITY (ações, opções, FIIs, etc.)
/// Cobrança progressiva por volume financeiro com BPS + CAP por faixa.
/// </summary>
public class EquityFeeBracket
{
    public long Id { get; set; }
    public int Broker { get; set; }

    /// <summary>
    /// Se NULL = regra padrão do broker.
    /// Se preenchido = regra especial 100% custom para este trader.
    /// </summary>
    public string? Trader { get; set; }

    public DateOnly ValidFrom { get; set; }
    public DateOnly ValidTo { get; set; }

    /// <summary>Volume mínimo da faixa (inclusive)</summary>
    public decimal NotionalFrom { get; set; }

    /// <summary>Volume máximo da faixa (exclusive, NULL = sem limite)</summary>
    public decimal? NotionalTo { get; set; }

    /// <summary>Basis points (ex: 0.5 bps = 0.00005)</summary>
    public decimal Bps { get; set; }

    /// <summary>Teto máximo de cobrança nesta faixa no mês (NULL = sem teto)</summary>
    public decimal? Cap { get; set; }

    public bool IsActive { get; set; }
}
