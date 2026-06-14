using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Platform.Queries;

public record GetPlatformStatsQuery : IRequest<PlatformStatsResponse>;

public class GetPlatformStatsHandler(IAppDbContext db)
    : IRequestHandler<GetPlatformStatsQuery, PlatformStatsResponse>
{
    public async Task<PlatformStatsResponse> Handle(GetPlatformStatsQuery query, CancellationToken ct)
    {
        DateTime now          = DateTime.UtcNow;
        DateTime monthStart   = new(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // IgnoreQueryFilters approved: usage #4 — platform KPI aggregate, IssuerOnly. See architecture.md.
        List<Studio> studios = await db.Studios
            .IgnoreQueryFilters()
            .Include(s => s.Subscription)
                .ThenInclude(sub => sub!.Plan)
            .ToListAsync(ct);

        int totalStudios = studios.Count(s => s.IsActive);

        int activeSubscriptions = studios.Count(s =>
            s.Subscription?.Status == SubscriptionStatus.Active);

        int trialStudios = studios.Count(s =>
            s.Subscription?.Status == SubscriptionStatus.Trialing
            || (s.Subscription is null && s.TrialExpiresAt > now));

        int gracePeriodStudios = studios.Count(s =>
            s.Subscription?.Status == SubscriptionStatus.GracePeriod);

        decimal mrr = studios
            .Where(s => s.Subscription?.Status == SubscriptionStatus.Active
                     && s.Subscription.Plan is not null)
            .Sum(s => s.Subscription!.Plan!.PriceMonthly);

        int conversionDenominator = activeSubscriptions + trialStudios + gracePeriodStudios;
        double trialConversionRate = conversionDenominator > 0
            ? Math.Round((double)activeSubscriptions / conversionDenominator, 4)
            : 0;

        int newStudiosThisMonth = studios.Count(s => s.CreatedAt >= monthStart);

        return new PlatformStatsResponse(
            totalStudios,
            activeSubscriptions,
            trialStudios,
            gracePeriodStudios,
            mrr,
            trialConversionRate,
            newStudiosThisMonth);
    }
}
