using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;
using Microsoft.AspNetCore.Mvc;

namespace KuberaWebApp.Controllers;

public static class PriceModificationFactorController
{
    public static RouteGroupBuilder MapPriceModificationFactorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/price-modification-factor")
            .WithTags("Price Modification Factors");

        group.MapGet("/", async (PriceModificationFactorRepository repository, CancellationToken ct) =>
        {
            var factors = await repository.GetAllActiveAsync(ct);
            return Results.Ok(new
            {
                Count = factors.Count(),
                Items = factors
            });
        })
        .WithName("GetAllPriceModificationFactors")
        .WithDescription("Lista todos os fatores de modificação de preço ativos");

        group.MapGet("/{id:long}", async (long id, PriceModificationFactorRepository repository, CancellationToken ct) =>
        {
            var factor = await repository.GetByIdAsync(id, ct);
            return factor is null
                ? Results.NotFound(new { Error = "Fator não encontrado", Id = id })
                : Results.Ok(factor);
        })
        .WithName("GetPriceModificationFactorById")
        .WithDescription("Busca um fator de modificação de preço pelo ID");

        group.MapPost("/", async (
            [FromBody] CreatePriceModificationFactorRequest request,
            PriceModificationFactorRepository repository,
            CancellationToken ct) =>
        {
            var validationErrors = ValidateCreateRequest(request);
            if (validationErrors.Count > 0)
                return Results.BadRequest(new { Errors = validationErrors });

            var factor = new PriceModificationFactor
            {
                Symbol = request.Symbol,
                Factor = request.Factor,
                ValidFrom = request.ValidFrom,
                ValidTo = request.ValidTo,
                IsActive = true
            };

            var created = await repository.CreateAsync(factor, ct);
            return Results.Created($"/price-modification-factor/{created.Id}", created);
        })
        .WithName("CreatePriceModificationFactor")
        .WithDescription("Cria um novo fator de modificação de preço");

        group.MapPut("/{id:long}", async (
            long id,
            [FromBody] UpdatePriceModificationFactorRequest request,
            PriceModificationFactorRepository repository,
            CancellationToken ct) =>
        {
            var existing = await repository.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound(new { Error = "Fator não encontrado", Id = id });

            var validationErrors = ValidateUpdateRequest(request);
            if (validationErrors.Count > 0)
                return Results.BadRequest(new { Errors = validationErrors });

            existing.Symbol = request.Symbol;
            existing.Factor = request.Factor;
            existing.ValidFrom = request.ValidFrom;
            existing.ValidTo = request.ValidTo;

            await repository.UpdateAsync(existing, ct);
            return Results.Ok(existing);
        })
        .WithName("UpdatePriceModificationFactor")
        .WithDescription("Atualiza um fator de modificação de preço existente");

        group.MapDelete("/{id:long}", async (
            long id,
            PriceModificationFactorRepository repository,
            CancellationToken ct) =>
        {
            var existing = await repository.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound(new { Error = "Fator não encontrado", Id = id });

            var deactivated = await repository.DeactivateAsync(id, ct);
            return deactivated
                ? Results.Ok(new { Message = "Fator desativado com sucesso", Id = id })
                : Results.Problem("Falha ao desativar fator");
        })
        .WithName("DeletePriceModificationFactor")
        .WithDescription("Desativa um fator de modificação de preço (soft delete)");

        return group;
    }

    private static List<string> ValidateCreateRequest(CreatePriceModificationFactorRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Symbol))
            errors.Add("Symbol é obrigatório");
        if (request.Factor <= 0)
            errors.Add("Factor deve ser maior que zero");
        if (request.ValidFrom >= request.ValidTo)
            errors.Add("ValidFrom deve ser anterior a ValidTo");
        return errors;
    }

    private static List<string> ValidateUpdateRequest(UpdatePriceModificationFactorRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Symbol))
            errors.Add("Symbol é obrigatório");
        if (request.Factor <= 0)
            errors.Add("Factor deve ser maior que zero");
        if (request.ValidFrom >= request.ValidTo)
            errors.Add("ValidFrom deve ser anterior a ValidTo");
        return errors;
    }
}

public record CreatePriceModificationFactorRequest(
    string Symbol,
    decimal Factor,
    DateTime ValidFrom,
    DateTime ValidTo
);

public record UpdatePriceModificationFactorRequest(
    string Symbol,
    decimal Factor,
    DateTime ValidFrom,
    DateTime ValidTo
);
