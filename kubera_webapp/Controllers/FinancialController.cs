namespace kubera_webapp.Controllers;


public static class FinancialController
{
    public static RouteGroupBuilder MapFinancialEndpoints(this IEndpointRouteBuilder app)
    {
        var financialGroup = app.MapGroup("/financial");
        financialGroup.MapGet("/balance", () =>
        {
            return "Saldo atual: R$ 10.000,00";
        });

        financialGroup.MapPost("/start-process/{day}", (string day) => { 

        });
        return financialGroup;
    }
}
