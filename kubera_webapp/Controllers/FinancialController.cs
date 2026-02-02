using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;

namespace KuberaWebApp.Controllers;

public static class FinancialController
{
    public static RouteGroupBuilder MapFinancialEndpoints(this IEndpointRouteBuilder app)
    {
        var financialGroup = app.MapGroup("/financial");

        financialGroup.MapGet("/status", async (
            ProcessBillingRepository repository,
            CancellationToken ct) =>
        {
            var pending = await repository.GetPendingAsync(ct);
            return Results.Ok(new
            {
                PendingCount = pending.Count,
                PendingItems = pending.Select(p => new
                {
                    p.ProcessId,
                    ProcessDate = p.ProcessDate.ToString("yyyy-MM-dd"),
                    p.ProcessBroker
                })
            });
        });

        financialGroup.MapPost("/start-process", async (
            StartProcessRequest request,
            ProcessBillingRepository repository,
            CancellationToken ct) =>
        {
            if (!DateTime.TryParse(request.Date, out var businessDate))
                return Results.BadRequest("Formato de data inválido. Use yyyy-MM-dd.");

            var processBilling = new ProcessBilling
            {
                ProcessDate = businessDate,
                Status = "Pending",
                ProcessBroker = request.Brokers ?? ""
            };

            await repository.SaveAsync(processBilling, ct);

            return Results.Ok(new
            {
                Message = "Processamento agendado com sucesso.",
                ProcessDate = businessDate.ToString("yyyy-MM-dd"),
                Brokers = request.Brokers
            });
        });

        financialGroup.MapPost("/start-process/{day}", async (
            string day,
            ProcessBillingRepository repository,
            CancellationToken ct) =>
        {
            if (!DateTime.TryParse(day, out var businessDate))
                return Results.BadRequest("Formato de data inválido. Use yyyy-MM-dd.");

            var processBilling = new ProcessBilling
            {
                ProcessDate = businessDate,
                Status = "Pending",
                ProcessBroker = ""
            };

            await repository.SaveAsync(processBilling, ct);

            return Results.Ok(new
            {
                Message = "Processamento agendado com sucesso.",
                ProcessDate = businessDate.ToString("yyyy-MM-dd")
            });
        });

        return financialGroup;
    }
}

public record StartProcessRequest(string Date, string? Brokers);
