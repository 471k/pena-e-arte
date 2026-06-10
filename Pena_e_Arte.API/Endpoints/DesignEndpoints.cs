using MediatR;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Application.Designs.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using System.Collections.Generic;

namespace Pena_e_Arte.API.Endpoints;

public static class DesignEndpoints
{
    public static void MapDesignEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/designs")
            .RequireAuthorization();

        group.MapGet("/",                                          GetDesigns).RequireAuthorization("ArtistAndAbove");
        group.MapPost("/",                                         CreateDesign).RequireAuthorization("ArtistAndAbove");
        group.MapGet("{id:guid}/revisions",                        GetRevisions).RequireAuthorization("ClientAndAbove");
        group.MapPost("{id:guid}/revisions",                       UploadRevision).RequireAuthorization("ArtistAndAbove");
        group.MapDelete("{id:guid}/revisions/{revisionId:guid}",   DeleteRevision).RequireAuthorization("ArtistAndAbove");
        group.MapPost("revisions/{revisionId:guid}/review",        ReviewDesign).RequireAuthorization("ClientAndAbove");
    }

    private static async Task<IResult> GetDesigns(
        Guid?             clientId,
        Guid?             artistId,
        ISender           mediator,
        CancellationToken ct)
    {
        List<DesignResponse> result = await mediator.Send(new GetDesignsQuery(clientId, artistId), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetRevisions(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        List<DesignRevisionResponse> result = await mediator.Send(new GetDesignRevisionsQuery(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateDesign(
        CreateDesignRequest request,
        ISender             mediator,
        CancellationToken   ct)
    {
        DesignResponse result = await mediator.Send(new CreateDesignCommand(request), ct);
        return Results.Created($"/api/v1/designs/{result.Id}", result);
    }

    private static async Task<IResult> UploadRevision(
        Guid                        id,
        UploadDesignRevisionRequest request,
        ISender                     mediator,
        CancellationToken           ct)
    {
        UploadDesignRevisionRequest withDesignId = request with { DesignId = id };
        DesignRevisionResponse result = await mediator.Send(new UploadDesignRevisionCommand(withDesignId), ct);
        return Results.Created($"/api/v1/designs/{id}/revisions/{result.Id}", result);
    }

    private static async Task<IResult> DeleteRevision(
        Guid              id,
        Guid              revisionId,
        ISender           mediator,
        CancellationToken ct)
    {
        await mediator.Send(new DeleteDesignRevisionCommand(id, revisionId), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ReviewDesign(
        Guid                revisionId,
        ReviewDesignRequest request,
        ISender             mediator,
        CancellationToken   ct)
    {
        ReviewDesignRequest withRevisionId = request with { DesignRevisionId = revisionId };
        DesignRevisionResponse result = await mediator.Send(new ReviewDesignCommand(withRevisionId), ct);
        return Results.Ok(result);
    }
}
