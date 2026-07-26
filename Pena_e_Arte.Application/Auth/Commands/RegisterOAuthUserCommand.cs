using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Commands;

public record RegisterOAuthUserCommand(RegisterOAuthUserRequest Request) : IRequest;

public class RegisterOAuthUserHandler(
    IOAuthTokenValidator validator,
    IIdentityService identity,
    IAppDbContext db) : IRequestHandler<RegisterOAuthUserCommand>
{
    public async Task Handle(RegisterOAuthUserCommand command, CancellationToken ct)
    {
        RegisterOAuthUserRequest req = command.Request;

        OAuthUserInfo info = req.Provider switch
        {
            "google" => await validator.ValidateGoogleTokenAsync(req.IdToken, ct),
            "apple" => await validator.ValidateAppleTokenAsync(req.IdToken, ct),
            _ => throw new BusinessRuleViolationException(
                             $"Unsupported OAuth provider: {req.Provider}"),
        };

        // Same rule as RegisterUserHandler: owner self-registration must be bound to the
        // studio's declared OwnerEmail, so a caller cannot attach an "owner" account to a
        // studio they didn't create by guessing/reusing a publicly-visible studioId.
        if (string.Equals(req.Role, "owner", StringComparison.OrdinalIgnoreCase))
        {
            Studio? studio = await db.Studios
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Id == req.StudioId, ct);

            if (studio is null ||
                !string.Equals(studio.OwnerEmail, info.Email, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException(
                    "You are not authorized to register as the owner of this studio.");
        }

        (bool success, Guid userId, string[] errors) =
            await identity.CreateOAuthUserAsync(
                info.Email, req.Role, req.StudioId, info.FirstName);

        if (!success)
            throw new BusinessRuleViolationException(string.Join("; ", errors));

        // Mirror the same Client-record logic as RegisterUserCommand for "client" role.
        // IgnoreQueryFilters required: registration is anonymous, no tenant JWT.
        if (req.Role == "client")
        {
            Client? existing = await db.Clients
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    c => c.StudioId == req.StudioId && c.Email == info.Email && c.UserId == null, ct);

            if (existing is not null)
            {
                existing.UserId = userId;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                db.Clients.Add(new Client
                {
                    StudioId = req.StudioId,
                    UserId = userId,
                    FirstName = info.FirstName ?? info.Email.Split('@')[0],
                    LastName = string.Empty,
                    Email = info.Email,
                });
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
