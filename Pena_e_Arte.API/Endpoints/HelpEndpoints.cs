using MediatR;
using Pena_e_Arte.Application.Help.Commands;
using Pena_e_Arte.Contracts.Requests;

namespace Pena_e_Arte.API.Endpoints;

public static class HelpEndpoints
{
    public static void MapHelpEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/help/search-log", LogHelpSearch)
            .RequireAuthorization("ClientAndAbove");
    }

    private static async Task<IResult> LogHelpSearch(
        LogHelpSearchRequest request,
        ISender              mediator,
        CancellationToken    ct)
    {
        await mediator.Send(new LogHelpSearchCommand(request), ct);
        return Results.NoContent();
    }
}
