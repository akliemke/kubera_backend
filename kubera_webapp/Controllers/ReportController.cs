namespace KuberaWebApp.Controllers;

public static class ReportController
{
    public static RouteGroupBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var reportGroup = app.MapGroup("/report");

        reportGroup.MapGet("/billing/month/{ano_mes}", (string ano_mes) =>
        {
            return $"Relatório mensal para o ano e mês: {ano_mes}";
        });
        reportGroup.MapGet("/billing/anual/{ano}", (string ano) =>
        {
            return $"Detalhes do relatório anual para o ano: {ano}";
        });
        reportGroup.MapGet("/billing/daily/{data}", (string data) =>
        {
            return $"Detalhes do relatório diário para a data: {data}";
        });
        return reportGroup;
    }
}
