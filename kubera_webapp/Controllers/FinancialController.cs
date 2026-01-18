using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.HttpResults;

namespace KuberaWebApp.Controllers;


public static class FinancialController
{
    public static RouteGroupBuilder MapFinancialEndpoints(this IEndpointRouteBuilder app)
    {
        var financialGroup = app.MapGroup("/financial");
        financialGroup.MapGet("/balance", () =>
        {
            return "Saldo atual: R$ 10.000,00";
        });

        financialGroup.MapPost("/start-process/{day}", (string day, CancellationToken cts) =>
        {
            if (!DateTime.TryParse(day, out DateTime businessDate))
                return Results.BadRequest();

            return Results.Ok(businessDate);
        });
        return financialGroup;
    }
}
