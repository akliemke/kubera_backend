namespace FinancialEngineCore.Model;

/// <summary>
/// Apuração mensal de Market Data: um registro por usuário cobrado no mês.
/// </summary>
public class MarketDataBilling
{
    public long Id { get; set; }
    public int Broker { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string Trader { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public long ConfigId { get; set; }
    public DateTime CalculatedAt { get; set; }
}

/// <summary>
/// Vínculo usuário ↔ broker vindo da API externa.
/// </summary>
public record UserExchangeAccount(int Broker, string Trader);
