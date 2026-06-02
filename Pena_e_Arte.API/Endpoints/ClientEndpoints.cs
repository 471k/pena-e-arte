using MediatR;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Application.Clients.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class ClientEndpoints
{
    public static void MapClientEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/clients")
            .RequireAuthorization();

        group.MapGet("/",   GetClients).RequireAuthorization("ArtistAndAbove");
        group.MapPost("/",  CreateClient).RequireAuthorization("ArtistAndAbove");
    }

    private static async Task<IResult> GetClients(
        string?           search,
        ISender           mediator,
        CancellationToken ct)
    {
        List<ClientResponse> result = await mediator.Send(new GetClientsQuery(search), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateClient(
        CreateClientRequest request,
        ISender             mediator,
        CancellationToken   ct)
    {
        ClientResponse result = await mediator.Send(new CreateClientCommand(request), ct);
        return Results.Created($"/api/v1/clients/{result.Id}", result);
    }
}
