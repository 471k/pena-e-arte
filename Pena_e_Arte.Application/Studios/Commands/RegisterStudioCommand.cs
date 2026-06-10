using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Studios.Commands;

public record RegisterStudioCommand(RegisterStudioRequest Request) : IRequest<StudioResponse>;

public class RegisterStudioHandler(IAppDbContext db, IJobScheduler jobs)
    : IRequestHandler<RegisterStudioCommand, StudioResponse>
{
    public async Task<StudioResponse> Handle(RegisterStudioCommand command, CancellationToken ct)
    {
        RegisterStudioRequest req = command.Request;

        bool slugTaken = await db.Studios.AnyAsync(s => s.Slug == req.Slug, ct);
        if (slugTaken) throw new BusinessRuleViolationException("Studio slug is already taken.");

        DateTime now      = DateTime.UtcNow;
        DateTime trialEnd = now.AddDays(14);
        DateTime graceEnd = trialEnd.AddDays(7);

        Studio studio = new()
        {
            Name           = req.Name,
            Slug           = req.Slug,
            City           = req.City,
            OwnerEmail     = req.OwnerEmail,
            Latitude       = req.Latitude,
            Longitude      = req.Longitude,
            IsActive       = true,
            TrialExpiresAt = trialEnd
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
            studio.TrialExpiresAt, studio.CreatedAt);
    }
}
