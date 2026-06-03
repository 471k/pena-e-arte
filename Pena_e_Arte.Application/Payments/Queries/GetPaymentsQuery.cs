using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Payments.Queries;

public record GetPaymentsQuery(Guid? LastSeenId = null, int PageSize = 20) : IRequest<List<PaymentResponse>>;

public class GetPaymentsHandler(IAppDbContext db)
    : IRequestHandler<GetPaymentsQuery, List<PaymentResponse>>
{
    public async Task<List<PaymentResponse>> Handle(GetPaymentsQuery query, CancellationToken ct)
    {
        IQueryable<Payment> q = db.Payments
            .Include(p => p.SessionSplits.Where(ss => ss.DeletedAt == null))
            .OrderBy(p => p.CreatedAt);

        if (query.LastSeenId.HasValue)
        {
            DateTime? cursor = await db.Payments
                .Where(p => p.Id == query.LastSeenId.Value)
                .Select(p => (DateTime?)p.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (cursor.HasValue)
                q = q.Where(p => p.CreatedAt > cursor.Value);
        }

        List<Payment> payments = await q.Take(query.PageSize).ToListAsync(ct);

        return payments.Select(p =>
        {
            List<SessionSplitResponse> splits = p.SessionSplits
                .Select(CreatePaymentIntentHandler.MapSplit).ToList();
            return CreatePaymentIntentHandler.Map(p, splits);
        }).ToList();
    }
}
