using MediatR;
using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/auth");

        group.MapPost("/login",           Login).AllowAnonymous();
        group.MapPost("/register",        Register).AllowAnonymous();
        group.MapPost("/forgot-password", ForgotPassword).AllowAnonymous();
        group.MapPost("/reset-password",  ResetPassword).AllowAnonymous();
        group.MapPost("/refresh",         Refresh).AllowAnonymous();
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

    private static async Task<IResult> ForgotPassword(
        ForgotPasswordRequest request,
        ISender               mediator,
        CancellationToken     ct)
    {
        string? token = await mediator.Send(new ForgotPasswordCommand(request), ct);
        // In production the token is emailed. Dev: return it for testing.
        return Results.Ok(new { resetToken = token });
    }

    private static async Task<IResult> ResetPassword(
        ResetPasswordRequest request,
        ISender              mediator,
        CancellationToken    ct)
    {
        await mediator.Send(new ResetPasswordCommand(request), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Refresh(
        RefreshTokenRequest request,
        ISender             mediator,
        CancellationToken   ct)
    {
        AuthResponse response = await mediator.Send(new RefreshTokenCommand(request), ct);
        return Results.Ok(response);
    }
}
