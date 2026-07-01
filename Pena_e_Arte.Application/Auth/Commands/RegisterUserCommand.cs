using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Commands;

public record RegisterUserCommand(RegisterUserRequest Request) : IRequest;

public class RegisterUserHandler(
    IIdentityService             identity,
    IAppDbContext                db,
    IEmailRenderer               emailRenderer,
    INotificationService         notifications,
    IAppSettings                 appSettings,
    ILogger<RegisterUserHandler> logger)
    : IRequestHandler<RegisterUserCommand>
{
    public async Task Handle(RegisterUserCommand command, CancellationToken ct)
    {
        RegisterUserRequest req = command.Request;

        (bool success, Guid userId, string[] errors) = await identity.CreateUserAsync(
            req.Email, req.Password, req.Role, req.StudioId, req.FirstName);

        if (!success)
            throw new BusinessRuleViolationException(string.Join("; ", errors));

        // Client accounts must map to a tenant Client record, or they cannot book,
        // pay deposits, or see their bookings. Link a studio-created record by email,
        // or create a fresh one.
        // IgnoreQueryFilters is required here: registration is anonymous, so there is
        // no tenant JWT — the studio scope comes from the request and is applied
        // explicitly in the predicate below.
        if (req.Role == "client")
        {
            Client? existing = await db.Clients
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    c => c.StudioId == req.StudioId && c.Email == req.Email && c.UserId == null, ct);

            if (existing is not null)
            {
                existing.UserId    = userId;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                db.Clients.Add(new Client
                {
                    StudioId  = req.StudioId,
                    UserId    = userId,
                    FirstName = req.FirstName ?? req.Email.Split('@')[0],
                    LastName  = string.Empty,
                    Email     = req.Email,
                });
            }

            await db.SaveChangesAsync(ct);
        }

        // Send email verification (non-blocking; failure must not abort registration)
        try
        {
            string token           = await identity.GenerateEmailConfirmationTokenAsync(userId);
            string confirmationUrl = $"{appSettings.BaseUrl}/verify-email?token={Uri.EscapeDataString(token)}&userId={userId}";
            string body            = emailRenderer.RenderEmailVerification(confirmationUrl);

            await notifications.SendEmailAsync(
                req.Email,
                "Confirm your Pena e Artë account",
                body, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send email verification for user {@UserId}", userId);
        }
    }
}
