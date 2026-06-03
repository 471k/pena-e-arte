using MediatR;
using Pena_e_Arte.Application.Files.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class FileEndpoints
{
    public static void MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/files")
            .RequireAuthorization();

        group.MapPost("presign", GetPresignedUrl).RequireAuthorization("ClientAndAbove");
    }

    private static async Task<IResult> GetPresignedUrl(
        PresignUploadRequest request,
        ISender              mediator,
        CancellationToken    ct)
    {
        PresignUploadResponse result = await mediator.Send(new GetPresignedUploadUrlQuery(request), ct);
        return Results.Ok(result);
    }
}
