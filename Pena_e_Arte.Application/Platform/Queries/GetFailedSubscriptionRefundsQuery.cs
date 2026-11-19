using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Platform.Queries;

/// <summary>
/// Failed yearly-cancellation refunds across all studios, newest first (slice 8 phase 3, D07). Before this, a failed
/// refund was an error log line and a pill on one studio's page, with its reason in a hover-only tooltip: nobody could
/// find the failures without already knowing which studio to open.
/// </summary>
public record GetFailedSubscriptionRefundsQuery : IRequest<FailedSubscriptionRefundsResponse>
{
    /// <summary>The list is a work queue, not an archive: failures are rare, and a capped list says it is capped.</summary>
    public const int Limit = 50;
}

public class GetFailedSubscriptionRefundsHandler(IAppDbContext db)
    : IRequestHandler<GetFailedSubscriptionRefundsQuery, FailedSubscriptionRefundsResponse>
{
    public async Task<FailedSubscriptionRefundsResponse> Handle(GetFailedSubscriptionRefundsQuery query, CancellationToken ct)
    {
        // SubscriptionRefund and Studio carry no tenant query filter (SubscriptionRefund is deliberately not a
        // TenantEntity; Studio is the tenant itself), so this AdminOnly read needs no IgnoreQueryFilters.
        IQueryable<Domain.Entities.SubscriptionRefund> failed = db.SubscriptionRefunds
            .Where(r => r.Status == RefundStatus.Failed);

        int total = await failed.CountAsync(ct);

        // Join first, then order and take: ordering applied before a join is not guaranteed to survive it.
        List<FailedSubscriptionRefundResponse> items = await failed
            .Join(db.Studios, r => r.StudioId, s => s.Id, (r, s) => new { Refund = r, StudioName = s.Name })
            .OrderByDescending(x => x.Refund.UpdatedAt ?? x.Refund.CreatedAt)
            .ThenByDescending(x => x.Refund.Id)
            .Take(GetFailedSubscriptionRefundsQuery.Limit)
            .Select(x => new FailedSubscriptionRefundResponse(
                x.Refund.Id,
                x.Refund.StudioId,
                x.StudioName,
                x.Refund.Amount,
                x.Refund.Currency,
                x.Refund.Rule.ToString(),
                x.Refund.CreatedAt,
                x.Refund.UpdatedAt ?? x.Refund.CreatedAt,
                x.Refund.FailureReason,
                x.Refund.StripeInvoiceId,
                x.Refund.StripeRefundId != null))
            .ToListAsync(ct);

        return new FailedSubscriptionRefundsResponse(items, total);
    }
}
