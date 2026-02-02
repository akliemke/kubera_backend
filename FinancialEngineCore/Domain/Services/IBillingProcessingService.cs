using FinancialEngineCore.Model;

namespace FinancialEngineCore.Domain.Services;

/// <summary>
/// Service responsible for processing billing calculations.
/// Orchestrates the calculation flow for brokers/traders.
/// </summary>
public interface IBillingProcessingService
{
    /// <summary>
    /// Processes a billing request for multiple brokers.
    /// </summary>
    Task ProcessBillingAsync(ProcessBilling billing, CancellationToken ct);

    /// <summary>
    /// Processes billing for a single broker on a specific date.
    /// </summary>
    Task ProcessBrokerAsync(int brokerId, DateTime tradeDate, int processBillingId, CancellationToken ct);
}
