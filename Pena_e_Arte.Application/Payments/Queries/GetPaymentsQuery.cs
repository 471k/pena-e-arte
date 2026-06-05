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
            .OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.Id);

        if (query.LastSeenId.HasValue)
        {
            var cursor = await db.Payments
                .Where(p => p.Id == query.LastSeenId.Value)
                .Select(p => new { p.CreatedAt, p.Id })
                .FirstOrDefaultAsync(ct);

            if (cursor is not null)
                q = q.Where(p => p.CreatedAt > cursor.CreatedAt
                               || (p.CreatedAt == cursor.CreatedAt && p.Id > cursor.Id));
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
