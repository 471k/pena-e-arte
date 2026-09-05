using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Utilities;

namespace Pena_e_Arte.Application.Auth.Commands;

public record RegisterSoloArtistCommand(RegisterSoloArtistRequest Request) : IRequest;

public class RegisterSoloArtistValidator : AbstractValidator<RegisterSoloArtistCommand>
{
    public RegisterSoloArtistValidator()
    {
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Request.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x.Request.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.LastName).NotEmpty().MaximumLength(100);
    }
}

public class RegisterSoloArtistHandler(
    IAppDbContext db,
    IIdentityService identity,
    IEmailRenderer emailRenderer,
    INotificationService notifications,
    IAppSettings appSettings,
    ILogger<RegisterSoloArtistHandler> logger)
    : IRequestHandler<RegisterSoloArtistCommand>
{
    public async Task Handle(RegisterSoloArtistCommand command, CancellationToken ct)
    {
        RegisterSoloArtistRequest req = command.Request;

        // Studio auto-provisioning mirrors RegisterStudioHandler's slug-uniqueness loop,
        // minus the NIPT/City/Lat-Long requirements a solo artist hasn't provided yet.
        string baseSlug = SlugHelper.GenerateSlug($"{req.FirstName} {req.LastName}");
        string slug = baseSlug;
        int suffix = 2;
        while (await db.Studios.AnyAsync(s => s.Slug == slug, ct))
            slug = $"{baseSlug}-{suffix++}";

        Studio studio = new()
        {
            Name = $"{req.FirstName} {req.LastName}",
            Slug = slug,
            City = string.Empty,
            OwnerEmail = req.Email,
            Nipt = null,
            Latitude = 0,
            Longitude = 0,
            IsActive = true,
            IsSolo = true,
            IsPublished = false,
            // Not trialing — Subscription below is Active immediately on the Free plan.
            // Kept non-null only because the column is non-nullable.
            TrialExpiresAt = DateTime.UtcNow,
        };

        Plan freePlan = await db.Plans.FirstOrDefaultAsync(p => p.Name == "Free", ct)
            ?? throw new InvalidOperationException("Free plan not seeded — DataSeeder must run first.");

        Subscription subscription = new()
        {
            StudioId = studio.Id,
            PlanId = freePlan.Id,
            BillingInterval = BillingInterval.Monthly,
            Status = SubscriptionStatus.Active,
            TrialExpiresAt = null,
            // Mirrors CreateSubscriptionHandler's Free-plan "never expires" sentinel exactly.
            CurrentPeriodEnd = DateTime.UtcNow.AddYears(50),
            GracePeriodEnd = DateTime.UtcNow.AddYears(50),
        };

        // Identity user created BEFORE the Studio/Subscription are persisted — studio.Id is
        // already generated in memory (Studio.Id defaults to Guid.NewGuid() at construction), so
        // it can be passed here without saving first. This mirrors CreateArtistHandler's
        // identity-first ordering: if CreateUserAsync fails (duplicate email, transient Identity
        // error), nothing has been written to db yet, so there's no orphaned Studio/Subscription
        // left behind with no owning user. "owner" role, not "artist" — mirrors the existing
        // "owner who is also an artist" pattern via CreateOwnArtistProfileCommand, rather than
        // inventing a second role model. No owner-email-must-match-an-existing-studio check here
        // (unlike RegisterUserHandler's owner branch): this handler IS what creates the studio,
        // in the same request, so there is nothing to cross-check against.
        (bool success, Guid userId, string[] errors) =
            await identity.CreateUserAsync(req.Email, req.Password, "owner", studio.Id, req.FirstName);

        if (!success)
            throw new BusinessRuleViolationException(string.Join("; ", errors));

        db.Studios.Add(studio);
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync(ct);

        // Send email verification (non-blocking; failure must not abort registration).
        // Duplicated verbatim from RegisterUserHandler rather than extracted into a shared
        // helper — the two handlers' surrounding flow differs enough (this one has no
        // Client-linking step) that a clean extraction would have meant touching
        // RegisterUserHandler's own tested behavior for no functional gain.
        try
        {
            string token = await identity.GenerateEmailConfirmationTokenAsync(userId);
            string confirmationUrl = $"{appSettings.BaseUrl}/verify-email?token={Uri.EscapeDataString(token)}&userId={userId}";
            string body = emailRenderer.RenderEmailVerification(confirmationUrl);

            await notifications.SendEmailAsync(
                req.Email,
                "Confirm your TattooOS account",
                body, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send email verification for user {@UserId}", userId);
        }

        logger.LogInformation(
            "Solo artist registered {@UserId} studio {@StudioId}", userId, studio.Id);
    }
}
