using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;
using Microsoft.AspNetCore.Mvc;

namespace KuberaWebApp.Controllers;

public static class BmfFeeConfigController
{
    public static RouteGroupBuilder MapBmfFeeConfigEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/bmf-fee-config")
            .WithTags("BMF Fee Configs");

        group.MapGet("/", async (BmfFeeConfigRepository repository, CancellationToken ct) =>
        {
            var configs = await repository.GetAllActiveAsync(ct);
            return Results.Ok(new
            {
                Count = configs.Count(),
                Items = configs
            });
        })
        .WithName("GetAllBmfFeeConfigs")
        .WithDescription("Lista todas as configurações de cobrança BMF ativas");

        group.MapGet("/{id:long}", async (long id, BmfFeeConfigRepository repository, CancellationToken ct) =>
        {
            var config = await repository.GetByIdAsync(id, ct);
            return config is null
                ? Results.NotFound(new { Error = "Configuração não encontrada", Id = id })
                : Results.Ok(config);
        })
        .WithName("GetBmfFeeConfigById")
        .WithDescription("Busca uma configuração de cobrança pelo ID");

        group.MapGet("/broker/{brokerId:int}", async (
            int brokerId,
            BmfFeeConfigRepository repository,
            CancellationToken ct) =>
        {
            var configs = await repository.GetByBrokerAsync(brokerId, ct);
            var result = configs.Select(c => new
            {
                c.Id,
                Trader = c.Trader ?? "(Padrão)",
                c.KeyType,
                c.ValidFrom,
                c.ValidTo,
                Mini = c.ValorMini,
                Cheio = c.ValorCheio,
                Di1 = new
                {
                    Venc1a4 = c.DI1Venc1a4,
                    Venc5 = c.DI1Venc5,
                    Venc6 = c.DI1Venc6,
                    Venc7 = c.DI1Venc7,
                    Venc8Plus = c.DI1Venc8Plus
                },
                c.CapMensal
            });

            return Results.Ok(new
            {
                BrokerId = brokerId,
                Configs = result
            });
        })
        .WithName("GetBmfFeeConfigsByBroker")
        .WithDescription("Lista todas as configurações de cobrança BMF de um broker específico");

        group.MapPost("/", async (
            [FromBody] CreateBmfFeeConfigRequest request,
            BmfFeeConfigRepository repository,
            BrokerRepository brokerRepository,
            CancellationToken ct) =>
        {
            var validationErrors = ValidateCreateRequest(request);
            var (trader, keyType, keyError) = BillingKeyTypes.NormalizeRuleKey(request.Trader, request.KeyType);
            if (keyError is not null)
                validationErrors.Add(keyError);
            if (validationErrors.Count > 0)
                return Results.BadRequest(new { Errors = validationErrors });

            var broker = await brokerRepository.GetByCodeAsync(request.Broker, ct);
            if (broker is null)
                return Results.BadRequest(new { Error = "Broker não encontrado", Code = request.Broker });

            var config = new BmfFeeConfig
            {
                Broker = request.Broker,
                Trader = trader,
                KeyType = keyType,
                ValidFrom = request.ValidFrom,
                ValidTo = request.ValidTo,
                ValorMini = request.ValorMini,
                ValorCheio = request.ValorCheio,
                DI1Venc1a4 = request.DI1Venc1a4,
                DI1Venc5 = request.DI1Venc5,
                DI1Venc6 = request.DI1Venc6,
                DI1Venc7 = request.DI1Venc7,
                DI1Venc8Plus = request.DI1Venc8Plus,
                CapMensal = request.CapMensal,
                IsActive = true
            };

            var created = await repository.CreateAsync(config, ct);
            return Results.Created($"/bmf-fee-config/{created.Id}", created);
        })
        .WithName("CreateBmfFeeConfig")
        .WithDescription("Cria uma nova configuração de cobrança BMF");

        group.MapPost("/copy-to-trader", async (
            [FromBody] CopyConfigToTraderRequest request,
            BmfFeeConfigRepository repository,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Trader))
                return Results.BadRequest(new { Error = "Trader é obrigatório" });

            var (trader, keyType, keyError) = BillingKeyTypes.NormalizeRuleKey(request.Trader, request.KeyType ?? BillingKeyTypes.Trader);
            if (keyError is not null)
                return Results.BadRequest(new { Error = keyError });

            var defaultConfig = (await repository.GetByBrokerAsync(request.BrokerId, ct))
                .FirstOrDefault(c => c.Trader == null);

            if (defaultConfig is null)
                return Results.BadRequest(new { Error = "Nenhuma configuração padrão encontrada para este broker" });

            var newConfig = new BmfFeeConfig
            {
                Broker = defaultConfig.Broker,
                Trader = trader,
                KeyType = keyType,
                ValidFrom = defaultConfig.ValidFrom,
                ValidTo = defaultConfig.ValidTo,
                ValorMini = defaultConfig.ValorMini,
                ValorCheio = defaultConfig.ValorCheio,
                DI1Venc1a4 = defaultConfig.DI1Venc1a4,
                DI1Venc5 = defaultConfig.DI1Venc5,
                DI1Venc6 = defaultConfig.DI1Venc6,
                DI1Venc7 = defaultConfig.DI1Venc7,
                DI1Venc8Plus = defaultConfig.DI1Venc8Plus,
                CapMensal = defaultConfig.CapMensal,
                IsActive = true
            };

            var created = await repository.CreateAsync(newConfig, ct);
            return Results.Ok(new
            {
                Message = $"Configuração copiada para {BillingKeyTypes.Label(keyType)} {trader}",
                Config = created
            });
        })
        .WithName("CopyBmfConfigToTrader")
        .WithDescription("Copia a configuração padrão de um broker para criar regra especial de um trader");

        group.MapPut("/{id:long}", async (
            long id,
            [FromBody] UpdateBmfFeeConfigRequest request,
            BmfFeeConfigRepository repository,
            CancellationToken ct) =>
        {
            var existing = await repository.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound(new { Error = "Configuração não encontrada", Id = id });

            var validationErrors = ValidateUpdateRequest(request);
            var (trader, keyType, keyError) = BillingKeyTypes.NormalizeRuleKey(request.Trader, request.KeyType);
            if (keyError is not null)
                validationErrors.Add(keyError);
            if (validationErrors.Count > 0)
                return Results.BadRequest(new { Errors = validationErrors });

            var siblings = await repository.GetByBrokerAsync(existing.Broker, ct);
            var conflict = siblings.FirstOrDefault(c =>
                c.Id != existing.Id &&
                string.Equals(c.Trader, trader, StringComparison.OrdinalIgnoreCase) &&
                c.KeyType == keyType &&
                c.ValidFrom <= request.ValidTo && request.ValidFrom <= c.ValidTo);

            if (conflict is not null)
                return Results.BadRequest(new
                {
                    Errors = new[]
                    {
                        trader is null
                            ? "Já existe uma configuração padrão para este broker no período informado"
                            : $"Já existe uma configuração para {BillingKeyTypes.Label(keyType)} {trader} neste broker no período informado"
                    }
                });

            existing.Trader = trader;
            existing.KeyType = keyType;
            existing.ValidFrom = request.ValidFrom;
            existing.ValidTo = request.ValidTo;
            existing.ValorMini = request.ValorMini;
            existing.ValorCheio = request.ValorCheio;
            existing.DI1Venc1a4 = request.DI1Venc1a4;
            existing.DI1Venc5 = request.DI1Venc5;
            existing.DI1Venc6 = request.DI1Venc6;
            existing.DI1Venc7 = request.DI1Venc7;
            existing.DI1Venc8Plus = request.DI1Venc8Plus;
            existing.CapMensal = request.CapMensal;

            await repository.UpdateAsync(existing, ct);
            return Results.Ok(existing);
        })
        .WithName("UpdateBmfFeeConfig")
        .WithDescription("Atualiza uma configuração de cobrança existente");

        group.MapDelete("/{id:long}", async (
            long id,
            BmfFeeConfigRepository repository,
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
        .WithName("DeleteBmfFeeConfig")
        .WithDescription("Desativa uma configuração de cobrança (soft delete)");

        return group;
    }

    private static List<string> ValidateCreateRequest(CreateBmfFeeConfigRequest request)
    {
        var errors = new List<string>();
        if (request.Broker <= 0)
            errors.Add("Broker deve ser maior que zero");
        if (request.ValidFrom >= request.ValidTo)
            errors.Add("ValidFrom deve ser anterior a ValidTo");
        if (request.ValorMini < 0)
            errors.Add("ValorMini deve ser maior ou igual a zero");
        if (request.ValorCheio < 0)
            errors.Add("ValorCheio deve ser maior ou igual a zero");
        if (request.DI1Venc1a4 < 0)
            errors.Add("DI1Venc1a4 deve ser maior ou igual a zero");
        if (request.DI1Venc5 < 0)
            errors.Add("DI1Venc5 deve ser maior ou igual a zero");
        if (request.DI1Venc6 < 0)
            errors.Add("DI1Venc6 deve ser maior ou igual a zero");
        if (request.DI1Venc7 < 0)
            errors.Add("DI1Venc7 deve ser maior ou igual a zero");
        if (request.DI1Venc8Plus < 0)
            errors.Add("DI1Venc8Plus deve ser maior ou igual a zero");
        if (request.CapMensal < 0)
            errors.Add("CapMensal deve ser maior ou igual a zero (vazio = sem teto)");
        return errors;
    }

    private static List<string> ValidateUpdateRequest(UpdateBmfFeeConfigRequest request)
    {
        var errors = new List<string>();
        if (request.ValidFrom >= request.ValidTo)
            errors.Add("ValidFrom deve ser anterior a ValidTo");
        if (request.ValorMini < 0)
            errors.Add("ValorMini deve ser maior ou igual a zero");
        if (request.ValorCheio < 0)
            errors.Add("ValorCheio deve ser maior ou igual a zero");
        if (request.DI1Venc1a4 < 0)
            errors.Add("DI1Venc1a4 deve ser maior ou igual a zero");
        if (request.DI1Venc5 < 0)
            errors.Add("DI1Venc5 deve ser maior ou igual a zero");
        if (request.DI1Venc6 < 0)
            errors.Add("DI1Venc6 deve ser maior ou igual a zero");
        if (request.DI1Venc7 < 0)
            errors.Add("DI1Venc7 deve ser maior ou igual a zero");
        if (request.DI1Venc8Plus < 0)
            errors.Add("DI1Venc8Plus deve ser maior ou igual a zero");
        if (request.CapMensal < 0)
            errors.Add("CapMensal deve ser maior ou igual a zero (vazio = sem teto)");
        return errors;
    }
}

public record CreateBmfFeeConfigRequest(
    int Broker,
    string? Trader,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    decimal ValorMini,
    decimal ValorCheio,
    decimal DI1Venc1a4,
    decimal DI1Venc5,
    decimal DI1Venc6,
    decimal DI1Venc7,
    decimal DI1Venc8Plus,
    decimal? CapMensal, // NULL = sem teto
    string? KeyType = null // TRADER | ENTITY, obrigatório quando Trader é informado
);

public record UpdateBmfFeeConfigRequest(
    string? Trader,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    decimal ValorMini,
    decimal ValorCheio,
    decimal DI1Venc1a4,
    decimal DI1Venc5,
    decimal DI1Venc6,
    decimal DI1Venc7,
    decimal DI1Venc8Plus,
    decimal? CapMensal, // NULL = sem teto
    string? KeyType = null // TRADER | ENTITY, obrigatório quando Trader é informado
);

public record CopyConfigToTraderRequest(int BrokerId, string Trader, string? KeyType = null); // KeyType padrão: TRADER
