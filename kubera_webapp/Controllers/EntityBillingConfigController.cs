using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;
using Microsoft.AspNetCore.Mvc;

namespace KuberaWebApp.Controllers;

public static class EntityBillingConfigController
{
    public static RouteGroupBuilder MapEntityBillingConfigEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/entity-billing-config")
            .WithTags("Entity Billing Config");

        group.MapGet("/", async (EntityBillingConfigRepository repository, CancellationToken ct) =>
        {
            var configs = await repository.GetAllActiveAsync(ct);
            return Results.Ok(new
            {
                Count = configs.Count(),
                Items = configs
            });
        })
        .WithName("GetAllEntityBillingConfigs")
        .WithDescription("Lista todas as configurações de billing por entity ativas");

        group.MapGet("/{id:long}", async (long id, EntityBillingConfigRepository repository, CancellationToken ct) =>
        {
            var config = await repository.GetByIdAsync(id, ct);
            return config is null
                ? Results.NotFound(new { Error = "Configuração não encontrada", Id = id })
                : Results.Ok(config);
        })
        .WithName("GetEntityBillingConfigById")
        .WithDescription("Busca uma configuração de billing por entity pelo ID");

        group.MapPost("/", async (
            [FromBody] CreateEntityBillingConfigRequest request,
            EntityBillingConfigRepository repository,
            CancellationToken ct) =>
        {
            var validationErrors = ValidateCreateRequest(request);
            if (validationErrors.Count > 0)
                return Results.BadRequest(new { Errors = validationErrors });

            var config = new EntityBillingConfig
            {
                Broker = request.Broker,
                Entity = request.Entity,
                ValidFrom = request.ValidFrom,
                ValidTo = request.ValidTo,
                IsActive = true
            };

            var created = await repository.CreateAsync(config, ct);
            return Results.Created($"/entity-billing-config/{created.Id}", created);
        })
        .WithName("CreateEntityBillingConfig")
        .WithDescription("Cria uma nova configuração de billing por entity");

        group.MapPut("/{id:long}", async (
            long id,
            [FromBody] UpdateEntityBillingConfigRequest request,
            EntityBillingConfigRepository repository,
            CancellationToken ct) =>
        {
            var existing = await repository.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound(new { Error = "Configuração não encontrada", Id = id });

            var validationErrors = ValidateUpdateRequest(request);
            if (validationErrors.Count > 0)
                return Results.BadRequest(new { Errors = validationErrors });

            existing.Broker = request.Broker;
            existing.Entity = request.Entity;
            existing.ValidFrom = request.ValidFrom;
            existing.ValidTo = request.ValidTo;

            await repository.UpdateAsync(existing, ct);
            return Results.Ok(existing);
        })
        .WithName("UpdateEntityBillingConfig")
        .WithDescription("Atualiza uma configuração de billing por entity existente");

        group.MapDelete("/{id:long}", async (
            long id,
            EntityBillingConfigRepository repository,
            CancellationToken ct) =>
        {
            var existing = await repository.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound(new { Error = "Configuração não encontrada", Id = id });

            var deactivated = await repository.DeactivateAsync(id, ct);
            return deactivated
                ? Results.Ok(new { Message = "Configuração desativada com sucesso", Id = id })
                : Results.Problem("Falha ao desativar configuração");
        })
        .WithName("DeleteEntityBillingConfig")
        .WithDescription("Desativa uma configuração de billing por entity (soft delete)");

        return group;
    }

    private static List<string> ValidateCreateRequest(CreateEntityBillingConfigRequest request)
    {
        var errors = new List<string>();
        if (request.Broker <= 0)
            errors.Add("Broker deve ser maior que zero");
        if (string.IsNullOrWhiteSpace(request.Entity))
            errors.Add("Entity é obrigatório");
        if (request.ValidFrom >= request.ValidTo)
            errors.Add("ValidFrom deve ser anterior a ValidTo");
        return errors;
    }

    private static List<string> ValidateUpdateRequest(UpdateEntityBillingConfigRequest request)
    {
        var errors = new List<string>();
        if (request.Broker <= 0)
            errors.Add("Broker deve ser maior que zero");
        if (string.IsNullOrWhiteSpace(request.Entity))
            errors.Add("Entity é obrigatório");
        if (request.ValidFrom >= request.ValidTo)
            errors.Add("ValidFrom deve ser anterior a ValidTo");
        return errors;
    }
}

public record CreateEntityBillingConfigRequest(
    int Broker,
    string Entity,
    DateTime ValidFrom,
    DateTime ValidTo
);

public record UpdateEntityBillingConfigRequest(
    int Broker,
    string Entity,
    DateTime ValidFrom,
    DateTime ValidTo
);
