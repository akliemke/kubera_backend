using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;
using Microsoft.AspNetCore.Mvc;

namespace KuberaWebApp.Controllers;

public static class BrokerController
{
    public static RouteGroupBuilder MapBrokerEndpoints(this IEndpointRouteBuilder app)
    {
        var brokerGroup = app.MapGroup("/broker")
            .WithTags("Brokers");

        brokerGroup.MapGet("/", async (BrokerRepository repository, CancellationToken ct) =>
        {
            var brokers = await repository.GetAllAsync(ct);
            return Results.Ok(brokers);
        })
        .WithName("GetAllBrokers")
        .WithDescription("Lista todos os brokers cadastrados");

        brokerGroup.MapGet("/{id:long}", async (long id, BrokerRepository repository, CancellationToken ct) =>
        {
            var broker = await repository.GetByIdAsync(id, ct);
            return broker is null
                ? Results.NotFound(new { Error = "Broker não encontrado", Id = id })
                : Results.Ok(broker);
        })
        .WithName("GetBrokerById")
        .WithDescription("Busca um broker pelo ID");

        brokerGroup.MapGet("/code/{code:int}", async (int code, BrokerRepository repository, CancellationToken ct) =>
        {
            var broker = await repository.GetByCodeAsync(code, ct);
            return broker is null
                ? Results.NotFound(new { Error = "Broker não encontrado", Code = code })
                : Results.Ok(broker);
        })
        .WithName("GetBrokerByCode")
        .WithDescription("Busca um broker pelo código");

        brokerGroup.MapPost("/", async (
            [FromBody] CreateBrokerRequest request,
            BrokerRepository repository,
            CancellationToken ct) =>
        {
            var validationErrors = ValidateCreateRequest(request);
            if (validationErrors.Count > 0)
                return Results.BadRequest(new { Errors = validationErrors });

            var existing = await repository.GetByCodeAsync(request.Code, ct);
            if (existing is not null)
                return Results.Conflict(new { Error = "Já existe um broker com este código", Code = request.Code });

            var broker = new BrokerModel
            {
                Code = request.Code,
                Name = request.Name,
                Document = request.Document,
                Contacts = request.Contacts?.ToArray() ?? [],
                ContractDate = request.ContractDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                Status = true,
                BillingCurrency = request.BillingCurrency ?? "BRL"
            };

            var created = await repository.CreateAsync(broker, ct);
            return Results.Created($"/broker/{created.Id}", created);
        })
        .WithName("CreateBroker")
        .WithDescription("Cria um novo broker");

        brokerGroup.MapPut("/{id:long}", async (
            long id,
            [FromBody] UpdateBrokerRequest request,
            BrokerRepository repository,
            CancellationToken ct) =>
        {
            var existing = await repository.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound(new { Error = "Broker não encontrado", Id = id });

            var validationErrors = ValidateUpdateRequest(request);
            if (validationErrors.Count > 0)
                return Results.BadRequest(new { Errors = validationErrors });

            if (request.Code != existing.Code)
            {
                var codeExists = await repository.GetByCodeAsync(request.Code, ct);
                if (codeExists is not null)
                    return Results.Conflict(new { Error = "Já existe outro broker com este código", Code = request.Code });
            }

            existing.Code = request.Code;
            existing.Name = request.Name;
            existing.Document = request.Document;
            existing.Contacts = request.Contacts?.ToArray() ?? existing.Contacts;
            existing.ContractDate = request.ContractDate ?? existing.ContractDate;
            existing.BillingCurrency = request.BillingCurrency ?? existing.BillingCurrency;

            await repository.UpdateAsync(existing, ct);
            return Results.Ok(existing);
        })
        .WithName("UpdateBroker")
        .WithDescription("Atualiza um broker existente");

        brokerGroup.MapPatch("/{id:long}/status", async (
            long id,
            [FromBody] UpdateStatusRequest request,
            BrokerRepository repository,
            CancellationToken ct) =>
        {
            var existing = await repository.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound(new { Error = "Broker não encontrado", Id = id });

            var updated = await repository.SetStatusAsync(id, request.Status, ct);
            return updated
                ? Results.Ok(new { Id = id, Status = request.Status })
                : Results.Problem("Falha ao atualizar status");
        })
        .WithName("UpdateBrokerStatus")
        .WithDescription("Ativa ou desativa um broker");

        brokerGroup.MapDelete("/{id:long}", async (
            long id,
            BrokerRepository repository,
            CancellationToken ct) =>
        {
            var existing = await repository.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound(new { Error = "Broker não encontrado", Id = id });

            var deleted = await repository.DeleteAsync(id, ct);
            return deleted
                ? Results.NoContent()
                : Results.Problem("Falha ao deletar broker");
        })
        .WithName("DeleteBroker")
        .WithDescription("Remove um broker permanentemente");

        return brokerGroup;
    }

    private static List<string> ValidateCreateRequest(CreateBrokerRequest request)
    {
        var errors = new List<string>();
        if (request.Code <= 0)
            errors.Add("Code deve ser maior que zero");
        if (string.IsNullOrWhiteSpace(request.Name))
            errors.Add("Name é obrigatório");
        if (string.IsNullOrWhiteSpace(request.Document))
            errors.Add("Document é obrigatório");
        return errors;
    }

    private static List<string> ValidateUpdateRequest(UpdateBrokerRequest request)
    {
        var errors = new List<string>();
        if (request.Code <= 0)
            errors.Add("Code deve ser maior que zero");
        if (string.IsNullOrWhiteSpace(request.Name))
            errors.Add("Name é obrigatório");
        if (string.IsNullOrWhiteSpace(request.Document))
            errors.Add("Document é obrigatório");
        return errors;
    }
}

public record CreateBrokerRequest(
    int Code,
    string Name,
    string Document,
    List<string>? Contacts = null,
    DateOnly? ContractDate = null,
    string? BillingCurrency = null
);

public record UpdateBrokerRequest(
    int Code,
    string Name,
    string Document,
    List<string>? Contacts = null,
    DateOnly? ContractDate = null,
    string? BillingCurrency = null
);

public record UpdateStatusRequest(bool Status);
