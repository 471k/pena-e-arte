using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Payments;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Reports.Queries;

/// <summary>
/// Artist-facing counterpart to GetRevenueSummaryQuery: the same 12-month trend and
/// retained-amount computation as the owner's revenue report, scoped to the calling
/// artist's own appointments only, plus a per-payment breakdown (including session
/// splits, shown as the studio recorded them — this handler does not guess which split
/// line is "the artist's cut"). Standard tenant-scoped read — Payment/Appointment/
/// SessionSplit are TenantEntity, the global query filter already scopes this to the
/// caller's studio; no IgnoreQueryFilters().
/// </summary>
public record GetMyEarningsQuery(DateTime? From = null, DateTime? To = null) : IRequest<ArtistEarningsResponse>;

public class GetMyEarningsHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetMyEarningsQuery, ArtistEarningsResponse>
{
    public async Task<ArtistEarningsResponse> Handle(GetMyEarningsQuery query, CancellationToken ct)
    {
        Artist? artist = await db.Artists
            .FirstOrDefaultAsync(a => a.UserId == currentUser.UserId, ct);
        if (artist is null)
            throw new NotFoundException(nameof(Artist), currentUser.UserId);

        DateTime now = DateTime.UtcNow;

        Dictionary<Guid, DateTime> appointmentDates = await db.Appointments
            .Where(a => a.ArtistId == artist.Id)
            .Select(a => new { a.Id, a.Date })
            .ToDictionaryAsync(a => a.Id, a => a.Date, ct);

        List<Payment> collectedPayments = appointmentDates.Count == 0
            ? []
            : await db.Payments
                .AsNoTracking()
                .Include(p => p.Client)
                .Where(p => appointmentDates.Keys.Contains(p.AppointmentId)
                    && (p.Status == PaymentStatus.Paid || p.Status == PaymentStatus.Refunded)
                    && p.PaidAt != null)
                .ToListAsync(ct);

        List<MonthlyRevenuePoint> monthlyTrend = new(12);
        for (int i = 11; i >= 0; i--)
        {
            DateTime monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-i);
            DateTime monthEnd = monthStart.AddMonths(1);

            decimal earnings = collectedPayments
                .Where(p => p.PaidAt!.Value >= monthStart && p.PaidAt!.Value < monthEnd)
                .Sum(p => p.RetainedAmount());

            monthlyTrend.Add(new MonthlyRevenuePoint(monthStart.ToString("yyyy-MM"), earnings));
        }

        DateTime periodFrom = query.From ?? now.AddDays(-30);
        DateTime periodTo = query.To ?? now;

        List<Payment> periodPayments = collectedPayments
            .Where(p => p.PaidAt!.Value >= periodFrom && p.PaidAt!.Value <= periodTo)
            .ToList();

        if (periodPayments.Count == 0)
            return new ArtistEarningsResponse(monthlyTrend, 0m, []);

        List<Guid> paymentIds = periodPayments.Select(p => p.Id).ToList();
        List<SessionSplit> splits = await db.SessionSplits
            .Where(s => paymentIds.Contains(s.PaymentId))
            .ToListAsync(ct);
        ILookup<Guid, SessionSplit> splitsByPayment = splits.ToLookup(s => s.PaymentId);

        List<EarningsPaymentLine> lines = periodPayments
            .OrderByDescending(p => p.PaidAt)
            .Select(p => new EarningsPaymentLine(
                p.Id,
                p.AppointmentId,
                appointmentDates.GetValueOrDefault(p.AppointmentId),
                $"{p.Client?.FirstName} {p.Client?.LastName}".Trim(),
                p.RetainedAmount(),
                splitsByPayment[p.Id]
                    .Select(s => new SessionSplitResponse(s.Id, s.PaymentId, s.Label, s.Amount, s.PaidAt))
                    .ToList()))
            .ToList();

        decimal periodTotal = periodPayments.Sum(p => p.RetainedAmount());

        return new ArtistEarningsResponse(monthlyTrend, periodTotal, lines);
    }
}
