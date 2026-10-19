namespace FinancialEngineCore.Model;

/// <summary>
/// Usuário que não é cobrado em Market Data (administrativo, de API etc.).
/// </summary>
public class MarketDataExclusion
{
    public long Id { get; set; }

    /// <summary>NULL = excluído em todos os brokers</summary>
    public int? Broker { get; set; }

    public string Trader { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public bool IsActive { get; set; }
}
