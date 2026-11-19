using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Platform.Queries;

/// <summary>
/// Every studio with its subscription, cross-tenant (AdminOnly). StudioId narrows it to one studio, so the
/// admin studio detail page reads one row instead of downloading the whole platform's (D08).
/// </summary>
public record GetPlatformSubscriptionsQuery(Guid? StudioId = null) : IRequest<List<PlatformSubscriptionResponse>>;

public class GetPlatformSubscriptionsValidator : AbstractValidator<GetPlatformSubscriptionsQuery>
{
    public GetPlatformSubscriptionsValidator() =>
        RuleFor(x => x.StudioId).NotEqual(Guid.Empty).When(x => x.StudioId.HasValue);
}

public class GetPlatformSubscriptionsHandler(IAppDbContext db)
    : IRequestHandler<GetPlatformSubscriptionsQuery, List<PlatformSubscriptionResponse>>
{
    public async Task<List<PlatformSubscriptionResponse>> Handle(
        GetPlatformSubscriptionsQuery query, CancellationToken ct)
    {
        // IgnoreQueryFilters approved: usage #5 — all subscriptions cross-tenant, AdminOnly. See architecture.md.
        List<Domain.Entities.Studio> studios = await db.Studios
            .IgnoreQueryFilters()
            .Where(s => query.StudioId == null || s.Id == query.StudioId)
            .Include(s => s.Subscription)
                .ThenInclude(sub => sub!.Plan)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        return studios.Select(s => new PlatformSubscriptionResponse(
            s.Id,
            s.Name,
            s.Slug,
            s.Subscription?.Id,
            s.Subscription?.Status.ToString() ?? "NoSubscription",
            s.Subscription?.Plan?.Name,
            s.Subscription?.TrialExpiresAt,
            s.Subscription?.CurrentPeriodEnd ?? DateTime.MinValue,
            !s.IsActive,
            s.Subscription?.CancelAtPeriodEnd ?? false,
            s.Subscription?.PastDueSince,
            s.Subscription?.DunningExcludedManually ?? false)).ToList();
    }
}
