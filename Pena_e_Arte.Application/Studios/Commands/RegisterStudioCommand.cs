using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Studios.Commands;

public record RegisterStudioCommand(RegisterStudioRequest Request) : IRequest<StudioResponse>;

public class RegisterStudioHandler(
    IAppDbContext                db,
    IJobScheduler                jobs,
    ILogger<RegisterStudioHandler> logger)
    : IRequestHandler<RegisterStudioCommand, StudioResponse>
{
    public async Task<StudioResponse> Handle(RegisterStudioCommand command, CancellationToken ct)
    {
        RegisterStudioRequest req = command.Request;

        string slug   = req.Slug;
        int    suffix = 2;
        while (await db.Studios.AnyAsync(s => s.Slug == slug, ct))
            slug = $"{req.Slug}-{suffix++}";

        // Validate referral code if provided
        Guid? pendingReferralCodeId = null;
        if (!string.IsNullOrWhiteSpace(req.ReferralCode))
        {
            ReferralCode? referralCode = await db.ReferralCodes
                .FirstOrDefaultAsync(r => r.Code == req.ReferralCode && r.IsActive, ct);

            if (referralCode is null)
                throw new BusinessRuleViolationException("Referral code is invalid or no longer active.");

            if (referralCode.ExpiresAt.HasValue && referralCode.ExpiresAt < DateTime.UtcNow)
                throw new BusinessRuleViolationException("Referral code has expired.");

            pendingReferralCodeId = referralCode.Id;
            logger.LogInformation("Applying referral code {@ReferralCodeId} to new studio registration",
                referralCode.Id);
        }

        DateTime now      = DateTime.UtcNow;
        DateTime trialEnd = now.AddDays(14);
        DateTime graceEnd = trialEnd.AddDays(7);

        Studio studio = new()
        {
            Name                  = req.Name,
            Slug                  = slug,
            City                  = req.City,
            OwnerEmail            = req.OwnerEmail,
            Latitude              = req.Latitude,
            Longitude             = req.Longitude,
            IsActive              = true,
            TrialExpiresAt        = trialEnd,
            PendingReferralCodeId = pendingReferralCodeId,
        };

        Subscription subscription = new()
        {
            StudioId         = studio.Id,
            Status           = SubscriptionStatus.Trialing,
            TrialExpiresAt   = trialEnd,
            GracePeriodEnd   = graceEnd,
            CurrentPeriodEnd = trialEnd
        };

        db.Studios.Add(studio);
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync(ct);

        jobs.ScheduleTrialExpiryWarning(studio.Id, trialEnd.AddHours(-48));
        jobs.ScheduleTrialExpiry(studio.Id, trialEnd);
        jobs.ScheduleGracePeriodEnd(studio.Id, graceEnd);

        return new StudioResponse(
            studio.Id, studio.Name, studio.Slug, studio.City,
            studio.Latitude, studio.Longitude,
            studio.ShowPlatformBranding,
            AllowBrandingRemoval: false,
            studio.TrialExpiresAt, studio.CreatedAt, studio.IsActive);
    }
}
