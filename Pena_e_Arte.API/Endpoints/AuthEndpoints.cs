using MediatR;
using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Contracts.Requests;

namespace Pena_e_Arte.API.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/auth");

        group.MapPost("/login",    Login).AllowAnonymous();
        group.MapPost("/register", Register).AllowAnonymous();
    }

    private static async Task<IResult> Login(
        LoginRequest    request,
        ISender         mediator,
        CancellationToken ct)
    {
        var response = await mediator.Send(new LoginCommand(request), ct);
        return Results.Ok(response);
    }

    private static async Task<IResult> Register(
        RegisterUserRequest request,
        ISender             mediator,
        CancellationToken   ct)
    {
        await mediator.Send(new RegisterUserCommand(request), ct);
        return Results.NoContent();
    }
}
