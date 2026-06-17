using MediatR;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Application.Clients.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Models;

namespace Pena_e_Arte.API.Endpoints;

public static class ClientEndpoints
{
    public static void MapClientEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/clients")
            .RequireAuthorization();

        group.MapGet("/",             GetClients).RequireAuthorization("ArtistAndAbove");
        group.MapPost("/",            CreateClient).RequireAuthorization("ArtistAndAbove");
        group.MapGet("{clientId:guid}", GetClientById).RequireAuthorization("ArtistAndAbove");

        group.MapGet("{clientId:guid}/profile",           GetClientProfile).RequireAuthorization("ArtistAndAbove");
        group.MapPut("{clientId:guid}/profile",           UpsertClientProfile).RequireAuthorization("ArtistAndAbove");
        group.MapPatch("{clientId:guid}/profile/body-map", UpdateBodyMap).RequireAuthorization("ArtistAndAbove");

        group.MapGet("{clientId:guid}/tattoos",              GetTattooRecords).RequireAuthorization("ArtistAndAbove");
        group.MapPost("{clientId:guid}/tattoos",             AddTattooRecord).RequireAuthorization("ArtistAndAbove");
        group.MapGet("{clientId:guid}/tattoos/{id:guid}",    GetTattooRecord).RequireAuthorization("ArtistAndAbove");
        group.MapPatch("{clientId:guid}/tattoos/{id:guid}",  UpdateTattooRecord).RequireAuthorization("ArtistAndAbove");
        group.MapDelete("{clientId:guid}/tattoos/{id:guid}", DeleteTattooRecord).RequireAuthorization("ArtistAndAbove");

        group.MapGet("me",                         GetMyClient).RequireAuthorization("ClientAndAbove");
        group.MapGet("me/profile",                 GetMyClientProfile).RequireAuthorization("ClientAndAbove");
        group.MapPatch("me/profile/body-map",      UpdateMyBodyMap).RequireAuthorization("ClientAndAbove");
        group.MapGet("me/tattoos",                 GetMyTattooRecords).RequireAuthorization("ClientAndAbove");
        group.MapPatch("me/portable-profile",      UpdatePortableProfileOptIn).RequireAuthorization("ClientAndAbove");
        group.MapGet("{userId:guid}/portable-profile",  GetPortableProfile).RequireAuthorization("ArtistAndAbove");
    }

    private static async Task<IResult> GetMyClient(
        ISender           mediator,
        CancellationToken ct)
    {
        ClientResponse result = await mediator.Send(new GetMyClientQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetMyClientProfile(
        ISender           mediator,
        CancellationToken ct)
    {
        ClientProfileResponse result = await mediator.Send(new GetMyClientProfileQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetMyTattooRecords(
        ISender           mediator,
        CancellationToken ct)
    {
        List<TattooRecordResponse> result = await mediator.Send(new GetMyTattooRecordsQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetClientById(
        Guid              clientId,
        ISender           mediator,
        CancellationToken ct)
    {
        ClientResponse result = await mediator.Send(new GetClientQuery(clientId), ct);
        return Results.Ok(result);
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

    private static async Task<IResult> GetClientProfile(
        Guid              clientId,
        ISender           mediator,
        CancellationToken ct)
    {
        ClientProfileResponse result = await mediator.Send(new GetClientProfileQuery(clientId), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> UpsertClientProfile(
        Guid                        clientId,
        UpsertClientProfileRequest  request,
        ISender                     mediator,
        CancellationToken           ct)
    {
        ClientProfileResponse result = await mediator.Send(new UpsertClientProfileCommand(clientId, request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateBodyMap(
        Guid                 clientId,
        UpdateBodyMapRequest request,
        ISender              mediator,
        CancellationToken    ct)
    {
        ClientProfileResponse result = await mediator.Send(new UpdateBodyMapCommand(clientId, request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetTattooRecords(
        Guid              clientId,
        ISender           mediator,
        CancellationToken ct)
    {
        List<TattooRecordResponse> result = await mediator.Send(new GetTattooRecordsQuery(clientId), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> AddTattooRecord(
        Guid                 clientId,
        AddTattooRecordRequest request,
        ISender              mediator,
        CancellationToken    ct)
    {
        TattooRecordResponse result = await mediator.Send(new AddTattooRecordCommand(clientId, request), ct);
        return Results.Created($"/api/v1/clients/{clientId}/tattoos/{result.Id}", result);
    }

    private static async Task<IResult> GetTattooRecord(
        Guid              clientId,
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        TattooRecordResponse result = await mediator.Send(new GetTattooRecordQuery(clientId, id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateTattooRecord(
        Guid                        clientId,
        Guid                        id,
        UpdateTattooRecordRequest   request,
        ISender                     mediator,
        CancellationToken           ct)
    {
        TattooRecordResponse result = await mediator.Send(new UpdateTattooRecordCommand(clientId, id, request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> DeleteTattooRecord(
        Guid              clientId,
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        await mediator.Send(new DeleteTattooRecordCommand(clientId, id), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateMyBodyMap(
        UpdateBodyMapRequest request,
        ISender              mediator,
        CancellationToken    ct)
    {
        ClientProfileResponse result = await mediator.Send(new UpdateMyBodyMapCommand(request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> UpdatePortableProfileOptIn(
        UpdatePortableProfileOptInRequest request,
        ISender                           mediator,
        CancellationToken                 ct)
    {
        await mediator.Send(new UpdatePortableProfileOptInCommand(request), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetPortableProfile(
        Guid              userId,
        ISender           mediator,
        CancellationToken ct)
    {
        PortableClientProfile? result = await mediator.Send(new GetPortableProfileQuery(userId), ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }
}
