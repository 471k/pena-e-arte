using System.Security.Claims;
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

        group.MapPost("/login",                Login).AllowAnonymous().RequireRateLimiting("auth");
        group.MapPost("/register",             Register).AllowAnonymous().RequireRateLimiting("auth");
        group.MapPost("/forgot-password",      ForgotPassword).AllowAnonymous().RequireRateLimiting("auth");
        group.MapPost("/reset-password",       ResetPassword).AllowAnonymous();
        group.MapPost("/refresh",              Refresh).AllowAnonymous();
        group.MapPatch("/change-password",     ChangePassword).RequireAuthorization("ClientAndAbove");
        group.MapGet ("/verify-email",         VerifyEmail).AllowAnonymous();
        group.MapPost("/resend-verification",  ResendVerification).RequireAuthorization("ClientAndAbove");
    }

    private static async Task<IResult> Login(
        LoginRequest      request,
        ISender           mediator,
        CancellationToken ct)
    {
        AuthResponse response = await mediator.Send(new LoginCommand(request), ct);
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

    private static async Task<IResult> ChangePassword(
        ChangePasswordRequest body,
        ClaimsPrincipal       user,
        ISender               mediator,
        CancellationToken     ct)
    {
        Guid userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await mediator.Send(new ChangePasswordCommand(userId, body.CurrentPassword, body.NewPassword), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> VerifyEmail(
        Guid              userId,
        string            token,
        ISender           mediator,
        CancellationToken ct)
    {
        await mediator.Send(new ConfirmEmailCommand(userId, token), ct);
        return Results.Redirect("/login?verified=true");
    }

    private static async Task<IResult> ResendVerification(
        ClaimsPrincipal user,
        ISender         mediator,
        CancellationToken ct)
    {
        Guid userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await mediator.Send(new ResendVerificationEmailCommand(userId), ct);
        return Results.NoContent();
    }
}
