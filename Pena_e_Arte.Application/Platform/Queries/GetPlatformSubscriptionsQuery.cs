using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Platform.Queries;

public record GetPlatformSubscriptionsQuery : IRequest<List<PlatformSubscriptionResponse>>;

public class GetPlatformSubscriptionsHandler(IAppDbContext db)
    : IRequestHandler<GetPlatformSubscriptionsQuery, List<PlatformSubscriptionResponse>>
{
    public async Task<List<PlatformSubscriptionResponse>> Handle(
        GetPlatformSubscriptionsQuery query, CancellationToken ct)
    {
        List<Domain.Entities.Studio> studios = await db.Studios
            .IgnoreQueryFilters()
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
            s.Subscription?.TrialExpiresAt ?? DateTime.MinValue,
            s.Subscription?.CurrentPeriodEnd ?? DateTime.MinValue)).ToList();
    }
}
