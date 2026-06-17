using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Platform.Queries;

public record GetMrrHistoryQuery : IRequest<List<MrrDataPointResponse>>;

public class GetMrrHistoryHandler(IAppDbContext db)
    : IRequestHandler<GetMrrHistoryQuery, List<MrrDataPointResponse>>
{
    public async Task<List<MrrDataPointResponse>> Handle(GetMrrHistoryQuery query, CancellationToken ct)
    {
        // IgnoreQueryFilters approved: usage #5 — platform MRR history, IssuerOnly. See architecture.md.
        // Subscriptions has no tenant filter but we load plans via Include.
        var subscriptions = await db.Subscriptions
            .AsNoTracking()
            .Include(s => s.Plan)
            .Where(s => s.Plan != null)
            .ToListAsync(ct);

        DateTime now    = DateTime.UtcNow;
        var      result = new List<MrrDataPointResponse>(12);

        for (int i = 11; i >= 0; i--)
        {
            DateTime monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-i);
            DateTime monthEnd   = monthStart.AddMonths(1);

            decimal mrr = subscriptions
                .Where(s => s.CreatedAt < monthEnd && s.CurrentPeriodEnd >= monthStart)
                .Sum(s => s.Plan!.PriceMonthly);

            result.Add(new MrrDataPointResponse(monthStart.ToString("yyyy-MM"), mrr));
        }

        return result;
    }
}
