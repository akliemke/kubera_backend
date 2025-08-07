using kubera_webapp.Models;
using Microsoft.AspNetCore.Mvc;

namespace kubera_webapp.Controllers;

public static class BrokerController
{
    public static RouteGroupBuilder MapBrokerEndpoints(this IEndpointRouteBuilder app)
    {
        var brokerGroup = app.MapGroup("/broker");

        brokerGroup.MapGet("/", () =>
        {
            return "Lista de corretores";
        });

        brokerGroup.MapGet("/details/{code}", (string code) =>
        {
            BrokerModel n = new()
            {
                CNPJ = "aaaa",
                BillingCurrency = "BRL",
                Contacts = new List<string>() { "albertkliemke@gmail.com" },
                Name = "BTG",
                Code = 85
            };
            return n;
        });

        brokerGroup.MapPost("/", ([FromBody] BrokerModel broker) =>
        {
            return $"Broker {broker.Code} criado com sucesso!";
        });

        brokerGroup.MapPut("/{id}", (string id) =>
        {
            return $"Corretor com ID {id}";
        });

        brokerGroup.MapDelete("/{id}", (string id) =>
        {
            return $"Corretor com ID {id} deletado com sucesso!";
        });

        return brokerGroup;
    }
}
