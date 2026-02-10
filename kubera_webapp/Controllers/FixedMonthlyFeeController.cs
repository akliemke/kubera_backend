using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;
using Microsoft.AspNetCore.Mvc;

namespace KuberaWebApp.Controllers;

public static class FixedMonthlyFeeController
{
    public static RouteGroupBuilder MapFixedMonthlyFeeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/fixed-monthly-fee")
            .WithTags("Fixed Monthly Fees");

        group.MapGet("/", async (FixedMonthlyFeeRepository repository, CancellationToken ct) =>
        {
            var fees = await repository.GetAllActiveAsync(ct);
            return Results.Ok(new
            {
                Count = fees.Count(),
                Items = fees
            });
        })
        .WithName("GetAllFixedMonthlyFees")
        .WithDescription("Lista todas as cobrancas fixas mensais ativas");

        group.MapGet("/{id:long}", async (long id, FixedMonthlyFeeRepository repository, CancellationToken ct) =>
        {
            var fee = await repository.GetByIdAsync(id, ct);
            return fee is null
                ? Results.NotFound(new { Error = "Cobranca fixa nao encontrada", Id = id })
                : Results.Ok(fee);
        })
        .WithName("GetFixedMonthlyFeeById")
        .WithDescription("Busca uma cobranca fixa mensal pelo ID");

        group.MapPost("/", async (
            [FromBody] CreateFixedMonthlyFeeRequest request,
            FixedMonthlyFeeRepository repository,
            CancellationToken ct) =>
        {
            var validationErrors = ValidateCreateRequest(request);
            if (validationErrors.Count > 0)
                return Results.BadRequest(new { Errors = validationErrors });

            var fee = new FixedMonthlyFee
            {
                Broker = request.Broker,
                Description = request.Description,
                Amount = request.Amount,
                ValidFrom = request.ValidFrom,
                ValidTo = request.ValidTo,
                IsActive = true
            };

            var created = await repository.CreateAsync(fee, ct);
            return Results.Created($"/fixed-monthly-fee/{created.Id}", created);
        })
        .WithName("CreateFixedMonthlyFee")
        .WithDescription("Cria uma nova cobranca fixa mensal");

        group.MapPut("/{id:long}", async (
            long id,
            [FromBody] UpdateFixedMonthlyFeeRequest request,
            FixedMonthlyFeeRepository repository,
            CancellationToken ct) =>
        {
            var existing = await repository.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound(new { Error = "Cobranca fixa nao encontrada", Id = id });

            var validationErrors = ValidateUpdateRequest(request);
            if (validationErrors.Count > 0)
                return Results.BadRequest(new { Errors = validationErrors });

            existing.Broker = request.Broker;
            existing.Description = request.Description;
            existing.Amount = request.Amount;
            existing.ValidFrom = request.ValidFrom;
            existing.ValidTo = request.ValidTo;

            await repository.UpdateAsync(existing, ct);
            return Results.Ok(existing);
        })
        .WithName("UpdateFixedMonthlyFee")
        .WithDescription("Atualiza uma cobranca fixa mensal existente");

        group.MapDelete("/{id:long}", async (
            long id,
            FixedMonthlyFeeRepository repository,
            CancellationToken ct) =>
        {
            var existing = await repository.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound(new { Error = "Cobranca fixa nao encontrada", Id = id });

            var deactivated = await repository.DeactivateAsync(id, ct);
            return deactivated
                ? Results.Ok(new { Message = "Cobranca fixa desativada com sucesso", Id = id })
                : Results.Problem("Falha ao desativar cobranca fixa");
        })
        .WithName("DeleteFixedMonthlyFee")
        .WithDescription("Desativa uma cobranca fixa mensal (soft delete)");

        return group;
    }

    private static List<string> ValidateCreateRequest(CreateFixedMonthlyFeeRequest request)
    {
        var errors = new List<string>();
        if (request.Broker <= 0)
            errors.Add("Broker e obrigatorio");
        if (string.IsNullOrWhiteSpace(request.Description))
            errors.Add("Descricao e obrigatoria");
        if (request.Amount <= 0)
            errors.Add("Valor deve ser maior que zero");
        if (request.ValidFrom >= request.ValidTo)
            errors.Add("ValidFrom deve ser anterior a ValidTo");
        return errors;
    }

    private static List<string> ValidateUpdateRequest(UpdateFixedMonthlyFeeRequest request)
    {
        var errors = new List<string>();
        if (request.Broker <= 0)
            errors.Add("Broker e obrigatorio");
        if (string.IsNullOrWhiteSpace(request.Description))
            errors.Add("Descricao e obrigatoria");
        if (request.Amount <= 0)
            errors.Add("Valor deve ser maior que zero");
        if (request.ValidFrom >= request.ValidTo)
            errors.Add("ValidFrom deve ser anterior a ValidTo");
        return errors;
    }
}

public record CreateFixedMonthlyFeeRequest(
    int Broker,
    string Description,
    decimal Amount,
    DateOnly ValidFrom,
    DateOnly ValidTo
);

public record UpdateFixedMonthlyFeeRequest(
    int Broker,
    string Description,
    decimal Amount,
    DateOnly ValidFrom,
    DateOnly ValidTo
);
