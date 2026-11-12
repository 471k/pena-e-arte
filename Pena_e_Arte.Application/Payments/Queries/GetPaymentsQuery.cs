using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments.Queries;

/// <summary>
/// The owner's payment history (E05b-1, O05): newest first, filtered in SQL, keyset-paged.
/// <list type="bullet">
/// <item><b>Order:</b> <c>PaidAt</c> descending, falling back to <c>CreatedAt</c> for a payment that hasn't
/// been paid yet, then <c>Id</c> descending as the tiebreak. It used to be <c>CreatedAt</c> ascending, so the
/// first page was the oldest payments.</item>
/// <item><b>Search:</b> the client's or the session artist's full name (see
/// <see cref="PaymentFilters.MatchingSearch"/>). Never the provider reference or client token.</item>
/// <item><b>Status:</b> one <see cref="PaymentStatus"/> by its exact name; anything else is a validation
/// error, never a silent "all".</item>
/// <item><b>From/To:</b> studio-local dates on <c>PaidAt</c> (see <see cref="StudioPeriod"/>). A payment with
/// no <c>PaidAt</c> is excluded whenever either date is set: it hasn't been paid, so it has no paid date to
/// fall inside a period.</item>
/// <item><b>Paging:</b> <paramref name="LastSeenId"/> is the last row of the previous page; the next page
/// starts strictly after its (sort key, Id), so a row is never repeated or skipped as pages load.</item>
/// </list>
/// </summary>
public record GetPaymentsQuery(
    Guid? LastSeenId = null,
    int PageSize = GetPaymentsQuery.DefaultPageSize,
    string? Search = null,
    string? Status = null,
    DateOnly? From = null,
    DateOnly? To = null) : IRequest<List<PaymentResponse>>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
}

public class GetPaymentsValidator : AbstractValidator<GetPaymentsQuery>
{
    public GetPaymentsValidator()
    {
        RuleFor(q => q.PageSize).InclusiveBetween(1, GetPaymentsQuery.MaxPageSize);
        RuleFor(q => q.Search).ValidSearch();
        RuleFor(q => q.Status).ValidPaymentStatus();
        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q.From)
            .When(q => q.From is not null && q.To is not null)
            .WithMessage("To must be on or after From.");
    }
}

public class GetPaymentsHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<GetPaymentsQuery, List<PaymentResponse>>
{
    public async Task<List<PaymentResponse>> Handle(GetPaymentsQuery query, CancellationToken ct)
    {
        IQueryable<Payment> q = db.Payments
            .Include(p => p.Client)
            .Include(p => p.Appointment)
            .MatchingSearch(query.Search);

        if (PaymentFilters.TryParseStatus(query.Status, out PaymentStatus status))
            q = q.Where(p => p.Status == status);

        if (query.From is not null || query.To is not null)
        {
            // Only read when a date is set, so an unfiltered page costs one statement.
            string? timezone = await db.Studios
                .Where(s => s.Id == tenant.StudioId)
                .Select(s => s.Timezone)
                .FirstOrDefaultAsync(ct);
            q = q.PaidWithin(StudioPeriod.Resolve(query.From, query.To, timezone));
        }

        if (query.LastSeenId.HasValue)
        {
            var cursor = await db.Payments
                .Where(p => p.Id == query.LastSeenId.Value)
                .Select(p => new { Key = p.PaidAt ?? p.CreatedAt, p.Id })
                .FirstOrDefaultAsync(ct);

            if (cursor is not null)
                q = q.Where(p => (p.PaidAt ?? p.CreatedAt) < cursor.Key
                               || ((p.PaidAt ?? p.CreatedAt) == cursor.Key && p.Id < cursor.Id));
        }

        List<Payment> payments = await q
            .OrderByDescending(p => p.PaidAt ?? p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(query.PageSize)
            .ToListAsync(ct);
        return payments.Select(p => p.ToResponse()).ToList();
    }
}
