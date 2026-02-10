namespace FinancialEngineCore.Model;

public record ProcessBilling
{
    public int ProcessId { get; set; }
    public DateOnly ProcessDate { get; set; }
    public required string Status { get; set; }
    public required string ProcessBroker { get; set; }
    public DateTime? LockedAt { get; set; }
}
