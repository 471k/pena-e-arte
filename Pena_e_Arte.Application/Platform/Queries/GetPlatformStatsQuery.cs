using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Platform.Queries;

public record GetPlatformStatsQuery : IRequest<PlatformStatsResponse>;

public class GetPlatformStatsHandler(IAppDbContext db)
    : IRequestHandler<GetPlatformStatsQuery, PlatformStatsResponse>
{
    public async Task<PlatformStatsResponse> Handle(GetPlatformStatsQuery query, CancellationToken ct)
    {
        int totalStudios = await db.Studios
            .IgnoreQueryFilters()
            .CountAsync(ct);

        int activeSubscriptions = await db.Subscriptions
            .IgnoreQueryFilters()
            .CountAsync(s => s.Status == SubscriptionStatus.Active, ct);

        int trialStudios = await db.Subscriptions
            .IgnoreQueryFilters()
            .CountAsync(s => s.Status == SubscriptionStatus.Trialing, ct);

        int suspendedStudios = await db.Studios
            .IgnoreQueryFilters()
            .CountAsync(s => !s.IsActive, ct);

        decimal mrr = await db.Subscriptions
            .IgnoreQueryFilters()
            .Where(s => s.Status == SubscriptionStatus.Active && s.Plan != null)
            .SumAsync(s => s.Plan!.PriceMonthly, ct);

        int totalReferralCodes = await db.ReferralCodes
            .IgnoreQueryFilters()
            .CountAsync(ct);

        int activeReferralCodes = await db.ReferralCodes
            .IgnoreQueryFilters()
            .CountAsync(r => r.IsActive, ct);

        return new PlatformStatsResponse(
            totalStudios,
            activeSubscriptions,
            trialStudios,
            suspendedStudios,
            mrr,
            totalReferralCodes,
            activeReferralCodes);
    }
}
