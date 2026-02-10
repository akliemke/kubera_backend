using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;
using Microsoft.AspNetCore.Mvc;

namespace KuberaWebApp.Controllers;

public static class EquityFeeBracketController
{
    public static RouteGroupBuilder MapEquityFeeBracketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/equity-fee-bracket")
            .WithTags("Equity Fee Brackets");

        group.MapGet("/", async (EquityFeeBracketRepository repository, CancellationToken ct) =>
        {
            var brackets = await repository.GetAllActiveAsync(ct);
            return Results.Ok(new
            {
                Count = brackets.Count(),
                Items = brackets
            });
        })
        .WithName("GetAllEquityFeeBrackets")
        .WithDescription("Lista todas as faixas de cobrança EQUITY ativas");

        group.MapGet("/{id:long}", async (long id, EquityFeeBracketRepository repository, CancellationToken ct) =>
        {
            var bracket = await repository.GetByIdAsync(id, ct);
            return bracket is null
                ? Results.NotFound(new { Error = "Faixa não encontrada", Id = id })
                : Results.Ok(bracket);
        })
        .WithName("GetEquityFeeBracketById")
        .WithDescription("Busca uma faixa de cobrança pelo ID");

        group.MapGet("/broker/{brokerId:int}", async (
            int brokerId,
            EquityFeeBracketRepository repository,
            CancellationToken ct) =>
        {
            var brackets = await repository.GetByBrokerAsync(brokerId, ct);
            var grouped = brackets.GroupBy(b => b.Trader ?? "(Padrão)").Select(g => new
            {
                Trader = g.Key,
                Brackets = g.OrderBy(b => b.NotionalFrom).Select(b => new
                {
                    b.Id,
                    b.NotionalFrom,
                    b.NotionalTo,
                    b.Bps,
                    b.Cap,
                    b.ValidFrom,
                    b.ValidTo
                })
            });

            return Results.Ok(new
            {
                BrokerId = brokerId,
                Configs = grouped
            });
        })
        .WithName("GetEquityFeeBracketsByBroker")
        .WithDescription("Lista todas as faixas de cobrança de um broker específico");

        group.MapPost("/", async (
            [FromBody] CreateEquityFeeBracketRequest request,
            EquityFeeBracketRepository repository,
            BrokerRepository brokerRepository,
            CancellationToken ct) =>
        {
            var validationErrors = ValidateCreateRequest(request);
            if (validationErrors.Count > 0)
                return Results.BadRequest(new { Errors = validationErrors });

            var broker = await brokerRepository.GetByCodeAsync(request.Broker, ct);
            if (broker is null)
                return Results.BadRequest(new { Error = "Broker não encontrado", Code = request.Broker });

            var bracket = new EquityFeeBracket
            {
                Broker = request.Broker,
                Trader = string.IsNullOrWhiteSpace(request.Trader) ? null : request.Trader,
                ValidFrom = request.ValidFrom,
                ValidTo = request.ValidTo,
                NotionalFrom = request.NotionalFrom,
                NotionalTo = request.NotionalTo,
                Bps = request.Bps,
                Cap = request.Cap,
                IsActive = true
            };

            var created = await repository.CreateAsync(bracket, ct);
            return Results.Created($"/equity-fee-bracket/{created.Id}", created);
        })
        .WithName("CreateEquityFeeBracket")
        .WithDescription("Cria uma nova faixa de cobrança EQUITY");

        group.MapPost("/copy-to-trader", async (
            [FromBody] CopyBracketsToTraderRequest request,
            EquityFeeBracketRepository repository,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Trader))
                return Results.BadRequest(new { Error = "Trader é obrigatório" });

            var defaultBrackets = (await repository.GetByBrokerAsync(request.BrokerId, ct))
                .Where(b => b.Trader == null)
                .ToList();

            if (defaultBrackets.Count == 0)
                return Results.BadRequest(new { Error = "Nenhuma faixa padrão encontrada para este broker" });

            var created = new List<EquityFeeBracket>();
            foreach (var bracket in defaultBrackets)
            {
                var newBracket = new EquityFeeBracket
                {
                    Broker = bracket.Broker,
                    Trader = request.Trader,
                    ValidFrom = bracket.ValidFrom,
                    ValidTo = bracket.ValidTo,
                    NotionalFrom = bracket.NotionalFrom,
                    NotionalTo = bracket.NotionalTo,
                    Bps = bracket.Bps,
                    Cap = bracket.Cap,
                    IsActive = true
                };
                var result = await repository.CreateAsync(newBracket, ct);
                created.Add(result);
            }

            return Results.Ok(new
            {
                Message = $"Copiadas {created.Count} faixas para o trader {request.Trader}",
                Brackets = created
            });
        })
        .WithName("CopyEquityBracketsToTrader")
        .WithDescription("Copia as faixas padrão de um broker para criar regra especial de um trader");

        group.MapPut("/{id:long}", async (
            long id,
            [FromBody] UpdateEquityFeeBracketRequest request,
            EquityFeeBracketRepository repository,
            CancellationToken ct) =>
        {
            var existing = await repository.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound(new { Error = "Faixa não encontrada", Id = id });

            var validationErrors = ValidateUpdateRequest(request);
            if (validationErrors.Count > 0)
                return Results.BadRequest(new { Errors = validationErrors });

            existing.ValidFrom = request.ValidFrom;
            existing.ValidTo = request.ValidTo;
            existing.NotionalFrom = request.NotionalFrom;
            existing.NotionalTo = request.NotionalTo;
            existing.Bps = request.Bps;
            existing.Cap = request.Cap;

            await repository.UpdateAsync(existing, ct);
            return Results.Ok(existing);
        })
        .WithName("UpdateEquityFeeBracket")
        .WithDescription("Atualiza uma faixa de cobrança existente");

        group.MapDelete("/{id:long}", async (
            long id,
            EquityFeeBracketRepository repository,
            CancellationToken ct) =>
        {
            var existing = await repository.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound(new { Error = "Faixa não encontrada", Id = id });

            var deactivated = await repository.DeactivateAsync(id, ct);
            return deactivated
                ? Results.Ok(new { Message = "Faixa desativada com sucesso", Id = id })
                : Results.Problem("Falha ao desativar faixa");
        })
        .WithName("DeleteEquityFeeBracket")
        .WithDescription("Desativa uma faixa de cobrança (soft delete)");

        return group;
    }

    private static List<string> ValidateCreateRequest(CreateEquityFeeBracketRequest request)
    {
        var errors = new List<string>();
        if (request.Broker <= 0)
            errors.Add("Broker deve ser maior que zero");
        if (request.ValidFrom >= request.ValidTo)
            errors.Add("ValidFrom deve ser anterior a ValidTo");
        if (request.NotionalFrom < 0)
            errors.Add("NotionalFrom deve ser maior ou igual a zero");
        if (request.NotionalTo.HasValue && request.NotionalTo <= request.NotionalFrom)
            errors.Add("NotionalTo deve ser maior que NotionalFrom");
        if (request.Bps < 0)
            errors.Add("Bps deve ser maior ou igual a zero");
        if (request.Cap < 0)
            errors.Add("Cap deve ser maior ou igual a zero");
        return errors;
    }

    private static List<string> ValidateUpdateRequest(UpdateEquityFeeBracketRequest request)
    {
        var errors = new List<string>();
        if (request.ValidFrom >= request.ValidTo)
            errors.Add("ValidFrom deve ser anterior a ValidTo");
        if (request.NotionalFrom < 0)
            errors.Add("NotionalFrom deve ser maior ou igual a zero");
        if (request.NotionalTo.HasValue && request.NotionalTo <= request.NotionalFrom)
            errors.Add("NotionalTo deve ser maior que NotionalFrom");
        if (request.Bps < 0)
            errors.Add("Bps deve ser maior ou igual a zero");
        if (request.Cap < 0)
            errors.Add("Cap deve ser maior ou igual a zero");
        return errors;
    }
}

public record CreateEquityFeeBracketRequest(
    int Broker,
    string? Trader,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    decimal NotionalFrom,
    decimal? NotionalTo,
    decimal Bps,
    decimal Cap
);

public record UpdateEquityFeeBracketRequest(
    DateOnly ValidFrom,
    DateOnly ValidTo,
    decimal NotionalFrom,
    decimal? NotionalTo,
    decimal Bps,
    decimal Cap
);

public record CopyBracketsToTraderRequest(int BrokerId, string Trader);
