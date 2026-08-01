using MediatR;
using Pena_e_Arte.Application.Contact.Commands;
using Pena_e_Arte.Contracts.Requests;

namespace Pena_e_Arte.API.Endpoints;

public static class ContactEndpoints
{
    public static void MapContactEndpoints(this IEndpointRouteBuilder app)
    {
        // Public contact form. AllowAnonymous by design — it is on the unauthenticated
        // /contact page, same justified public-endpoint posture as the Studio Map and the
        // public portfolio pages. Rate-limited with the shared anonymous-write policy
        // (30/min per IP) to blunt spam/abuse. The handler relays by email only; nothing
        // is persisted, so there is no tenant data to scope.
        app.MapPost("/api/v1/contact", SubmitContact)
           .AllowAnonymous()
           .RequireRateLimiting("public-write");
    }

    private static async Task<IResult> SubmitContact(
        SubmitContactRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new SubmitContactRequestCommand(request), ct);
        return Results.Accepted();
    }
}
