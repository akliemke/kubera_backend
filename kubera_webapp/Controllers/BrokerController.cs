using kubera_webapp.Infra;
using kubera_webapp.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace kubera_webapp.Controllers;

public static class BrokerController
{
    public static RouteGroupBuilder MapBrokerEndpoints(this IEndpointRouteBuilder app)
    {
        var brokerGroup = app.MapGroup("/broker");

        brokerGroup.MapGet("/", async (BrokerRepository brokerRepository, CancellationToken cts) =>
        {
            var brokers = await brokerRepository.GetAllAsync(cts);
            return Results.Ok(brokers);
        });

        brokerGroup.MapGet("/{id}", async (string id, BrokerRepository brokerRepository, CancellationToken cts) =>
        {
            var brokers = await brokerRepository.GetAllAsync(cts);
            var broker = brokers.Where( broker => broker.Id == Guid.Parse(id)).FirstOrDefault();
            return Results.Ok(brokers);
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
