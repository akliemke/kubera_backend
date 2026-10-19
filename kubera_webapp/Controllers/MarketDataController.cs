using System.Globalization;
using FinancialEngineCore.Domain;
using FinancialEngineCore.Domain.Services;
using FinancialEngineCore.Infra;
using FinancialEngineCore.Model;
using Microsoft.AspNetCore.Mvc;

namespace KuberaWebApp.Controllers;

/// <summary>
/// Cobrança de Market Data: valor mensal por usuário com conta vinculada ao broker (API externa).
/// </summary>
public static class MarketDataController
{
    public static IEndpointRouteBuilder MapMarketDataEndpoints(this IEndpointRouteBuilder app)
    {
        MapConfigEndpoints(app);
        MapExclusionEndpoints(app);
        MapOperationEndpoints(app);
        return app;
    }

    // ==========================================
    // Preços por broker
    // ==========================================
    private static void MapConfigEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/market-data-config")
            .WithTags("Market Data");

        group.MapGet("/", async (MarketDataFeeConfigRepository repository, CancellationToken ct) =>
        {
            var configs = await repository.GetAllActiveAsync(ct);
            return Results.Ok(new { Count = configs.Count(), Items = configs });
        })
        .WithName("GetAllMarketDataConfigs")
        .WithDescription("Lista os precos de Market Data ativos");

        group.MapGet("/{id:long}", async (long id, MarketDataFeeConfigRepository repository, CancellationToken ct) =>
        {
            var config = await repository.GetByIdAsync(id, ct);
            return config is null
                ? Results.NotFound(new { Error = "Preco de Market Data nao encontrado", Id = id })
                : Results.Ok(config);
        })
        .WithName("GetMarketDataConfigById");

        group.MapPost("/", async (
            [FromBody] MarketDataConfigRequest request,
            MarketDataFeeConfigRepository repository,
            CancellationToken ct) =>
        {
            var errors = ValidateConfig(request);
            if (errors.Count > 0)
                return Results.BadRequest(new { Errors = errors });

            var created = await repository.CreateAsync(new MarketDataFeeConfig
            {
                Broker = request.Broker,
                AmountPerUser = request.AmountPerUser,
                ValidFrom = request.ValidFrom,
                ValidTo = request.ValidTo,
                IsActive = true
            }, ct);
            return Results.Created($"/market-data-config/{created.Id}", created);
        })
        .WithName("CreateMarketDataConfig");

        group.MapPut("/{id:long}", async (
            long id,
            [FromBody] MarketDataConfigRequest request,
            MarketDataFeeConfigRepository repository,
            CancellationToken ct) =>
        {
            var existing = await repository.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound(new { Error = "Preco de Market Data nao encontrado", Id = id });

            var errors = ValidateConfig(request);
            if (errors.Count > 0)
                return Results.BadRequest(new { Errors = errors });

            existing.Broker = request.Broker;
            existing.AmountPerUser = request.AmountPerUser;
            existing.ValidFrom = request.ValidFrom;
            existing.ValidTo = request.ValidTo;

            await repository.UpdateAsync(existing, ct);
            return Results.Ok(existing);
        })
        .WithName("UpdateMarketDataConfig");

        group.MapDelete("/{id:long}", async (long id, MarketDataFeeConfigRepository repository, CancellationToken ct) =>
        {
            var existing = await repository.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound(new { Error = "Preco de Market Data nao encontrado", Id = id });

            return await repository.DeactivateAsync(id, ct)
                ? Results.Ok(new { Message = "Preco de Market Data desativado com sucesso", Id = id })
                : Results.Problem("Falha ao desativar preco de Market Data");
        })
        .WithName("DeleteMarketDataConfig")
        .WithDescription("Desativa um preco de Market Data (soft delete)");
    }

    // ==========================================
    // Exclusões (usuários não cobrados)
    // ==========================================
    private static void MapExclusionEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/market-data-exclusion")
            .WithTags("Market Data");

        group.MapGet("/", async (MarketDataExclusionRepository repository, CancellationToken ct) =>
        {
            var exclusions = await repository.GetAllActiveAsync(ct);
            return Results.Ok(new { Count = exclusions.Count(), Items = exclusions });
        })
        .WithName("GetAllMarketDataExclusions")
        .WithDescription("Lista os usuarios excluidos da cobranca de Market Data");

        group.MapPost("/", async (
            [FromBody] MarketDataExclusionRequest request,
            MarketDataExclusionRepository repository,
            CancellationToken ct) =>
        {
            var errors = ValidateExclusion(request);
            if (errors.Count > 0)
                return Results.BadRequest(new { Errors = errors });

            var trader = request.Trader.Trim();
            if (await repository.FindActiveAsync(request.Broker, trader, ct) is not null)
                return Results.Conflict(new { Error = "Usuario ja esta excluido para este broker" });

            var created = await repository.CreateAsync(new MarketDataExclusion
            {
                Broker = request.Broker,
                Trader = trader,
                Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
                IsActive = true
            }, ct);
            return Results.Created($"/market-data-exclusion/{created.Id}", created);
        })
        .WithName("CreateMarketDataExclusion")
        .WithDescription("Exclui um usuario da cobranca de Market Data (Broker vazio = todos os brokers)");

        group.MapPut("/{id:long}", async (
            long id,
            [FromBody] MarketDataExclusionRequest request,
            MarketDataExclusionRepository repository,
            CancellationToken ct) =>
        {
            var existing = await repository.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound(new { Error = "Exclusao nao encontrada", Id = id });

            var errors = ValidateExclusion(request);
            if (errors.Count > 0)
                return Results.BadRequest(new { Errors = errors });

            var trader = request.Trader.Trim();
            var duplicate = await repository.FindActiveAsync(request.Broker, trader, ct);
            if (duplicate is not null && duplicate.Id != id)
                return Results.Conflict(new { Error = "Usuario ja esta excluido para este broker" });

            existing.Broker = request.Broker;
            existing.Trader = trader;
            existing.Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();

            await repository.UpdateAsync(existing, ct);
            return Results.Ok(existing);
        })
        .WithName("UpdateMarketDataExclusion");

        group.MapDelete("/{id:long}", async (long id, MarketDataExclusionRepository repository, CancellationToken ct) =>
        {
            var existing = await repository.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound(new { Error = "Exclusao nao encontrada", Id = id });

            return await repository.DeactivateAsync(id, ct)
                ? Results.Ok(new { Message = "Exclusao desativada com sucesso", Id = id })
                : Results.Problem("Falha ao desativar exclusao");
        })
        .WithName("DeleteMarketDataExclusion")
        .WithDescription("Remove a exclusao (soft delete): o usuario volta a ser cobrado no proximo recalculo");
    }

    // ==========================================
    // Sync, recálculo e consulta da apuração
    // ==========================================
    private static void MapOperationEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/market-data")
            .WithTags("Market Data");

        group.MapPost("/sync", async (MarketDataService service, CancellationToken ct) =>
        {
            if (!service.IsApiConfigured)
                return Results.BadRequest(new { Error = "API de contas nao configurada (UserExchangeAccountsApi:BaseUrl)" });

            var today = DateOnly.FromDateTime(DateTime.Now);
            var count = await service.SyncAsync(today, ct);
            return Results.Ok(new { Date = today.ToString("yyyy-MM-dd"), Accounts = count });
        })
        .WithName("SyncMarketDataAccounts")
        .WithDescription("Busca os vinculos atuais na API externa, grava o snapshot do dia e recalcula o mes");

        group.MapPost("/recalculate", async (
            [FromBody] MarketDataRecalculateRequest request,
            MarketDataService service,
            CancellationToken ct) =>
        {
            if (!TryParseMonth(request.Month, out var year, out var month))
                return Results.BadRequest(new { Error = "Formato invalido. Use yyyy-MM (ex: 2024-01)." });

            var result = await service.RecalculateMonthAsync(year, month, request.Brokers, ct);
            return Results.Ok(new
            {
                Month = request.Month,
                Charged = result.Charges.Count,
                Excluded = result.Excluded.Count,
                TotalAmount = result.Charges.Sum(c => c.Amount),
                result.BrokersWithoutConfig
            });
        })
        .WithName("RecalculateMarketData")
        .WithDescription("Recalcula a Market Data de um mes (ex.: apos cadastrar exclusoes ou precos)");

        group.MapGet("/month/{ano_mes}", async (
            string ano_mes,
            MarketDataBillingRepository billingRepository,
            UserExchangeAccountSnapshotRepository snapshotRepository,
            MarketDataExclusionRepository exclusionRepository,
            MarketDataFeeConfigRepository configRepository,
            CancellationToken ct) =>
        {
            if (!TryParseMonth(ano_mes, out var year, out var month))
                return Results.BadRequest(new { Error = "Formato invalido. Use yyyy-MM (ex: 2024-01)." });

            var firstDay = new DateOnly(year, month, 1);

            var billingTask = billingRepository.GetByPeriodAsync(year, month, month, null, ct);
            var accountsTask = snapshotRepository.GetAccountsInMonthAsync(year, month, null, ct);
            var exclusionsTask = exclusionRepository.GetAllActiveAsync(ct);
            var configsTask = configRepository.GetByPeriodAsync(firstDay, firstDay.AddMonths(1).AddDays(-1), null, ct);
            var lastSnapshotTask = snapshotRepository.GetLastSnapshotDateAsync(ct);

            await Task.WhenAll(billingTask, accountsTask, exclusionsTask, configsTask, lastSnapshotTask);

            // Excluídos e brokers sem preço calculados com os dados atuais (conferência)
            var preview = MarketDataFeeCalculator.Calculate(
                year, month, await accountsTask, await exclusionsTask, await configsTask);
            var billing = await billingTask;

            return Results.Ok(new
            {
                Month = ano_mes,
                LastSnapshotDate = (await lastSnapshotTask)?.ToString("yyyy-MM-dd"),
                Brokers = billing.GroupBy(b => b.Broker).OrderBy(g => g.Key).Select(g => new
                {
                    Broker = g.Key,
                    Users = g.Count(),
                    TotalAmount = g.Sum(b => b.Amount),
                    Charges = g.Select(b => new { b.Trader, b.Amount })
                }),
                Excluded = preview.Excluded,
                preview.BrokersWithoutConfig,
                Summary = new
                {
                    Users = billing.Count,
                    TotalAmount = billing.Sum(b => b.Amount)
                }
            });
        })
        .WithName("GetMarketDataMonth")
        .WithDescription("Usuarios cobrados por broker no mes, usuarios excluidos e brokers sem preco");
    }

    private static bool TryParseMonth(string? value, out int year, out int month)
    {
        year = month = 0;
        if (!DateTime.TryParseExact(value + "-01", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;

        year = date.Year;
        month = date.Month;
        return true;
    }

    private static List<string> ValidateConfig(MarketDataConfigRequest request)
    {
        var errors = new List<string>();
        if (request.Broker <= 0)
            errors.Add("Broker e obrigatorio");
        if (request.AmountPerUser <= 0)
            errors.Add("Valor por usuario deve ser maior que zero");
        if (request.ValidFrom >= request.ValidTo)
            errors.Add("ValidFrom deve ser anterior a ValidTo");
        return errors;
    }

    private static List<string> ValidateExclusion(MarketDataExclusionRequest request)
    {
        var errors = new List<string>();
        if (request.Broker is <= 0)
            errors.Add("Broker deve ser maior que zero (ou vazio para todos os brokers)");
        if (string.IsNullOrWhiteSpace(request.Trader))
            errors.Add("Usuario e obrigatorio");
        return errors;
    }
}

public record MarketDataConfigRequest(
    int Broker,
    decimal AmountPerUser,
    DateOnly ValidFrom,
    DateOnly ValidTo
);

public record MarketDataExclusionRequest(
    int? Broker, // NULL = todos os brokers
    string Trader,
    string? Reason
);

public record MarketDataRecalculateRequest(
    string Month,
    List<int>? Brokers
);
