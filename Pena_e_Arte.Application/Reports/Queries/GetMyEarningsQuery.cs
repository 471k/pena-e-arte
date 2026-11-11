using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Payments;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.Application.Reports.Queries;

/// <summary>
/// GET /reports/my-earnings: the calling artist's own payments, with their share of each worked out by
/// <see cref="ArtistShare"/> under their arrangement (their own terms, else the studio's). Before A07a
/// this summed the whole client payment and called it earnings, which overstated every commission
/// artist. When the terms can't give a share (Commission with no rate, a flat fee with no fee), the
/// share is reported as unknown (null), never as 0 and never as the gross.
///
/// What clients paid (minus refunds) is still returned alongside, so the artist can reconcile. Session
/// splits are listed as the studio recorded them, unchanged. The artist's share is a liability of the
/// studio, not studio revenue; owner revenue (GetRevenueSummaryQuery) is untouched.
///
/// Standard tenant-scoped read: Payment/Appointment/SessionSplit are TenantEntity, the global query
/// filter already scopes this to the caller's studio; no IgnoreQueryFilters().
/// </summary>
public record GetMyEarningsQuery(DateTime? From = null, DateTime? To = null) : IRequest<ArtistEarningsResponse>;

public class GetMyEarningsHandler(IAppDbContext db, ICurrentUser currentUser, ICurrentTenant tenant, ILogger<GetMyEarningsHandler> logger)
    : IRequestHandler<GetMyEarningsQuery, ArtistEarningsResponse>
{
    private sealed record StudioTerms(
        string Currency, ArtistPayArrangement Arrangement, decimal? CommissionRate, decimal? FlatFeePerSession);

    public async Task<ArtistEarningsResponse> Handle(GetMyEarningsQuery query, CancellationToken ct)
    {
        Artist? artist = await db.Artists
            .FirstOrDefaultAsync(a => a.UserId == currentUser.UserId, ct);
        if (artist is null)
            throw new NotFoundException(nameof(Artist), currentUser.UserId);

        StudioTerms studio = await db.Studios
            .Where(s => s.Id == tenant.StudioId)
            .Select(s => new StudioTerms(s.Currency, s.DefaultPayArrangement, s.DefaultCommissionRate, s.DefaultFlatFeePerSession))
            .SingleAsync(ct);
        string studioCurrency = studio.Currency;

        ArtistPayTerms terms = ArtistShare.Resolve(
            artist.PayArrangement, artist.CommissionRate, artist.FlatFeePerSession,
            studio.Arrangement, studio.CommissionRate, studio.FlatFeePerSession);
        // The terms are the artist's, not the payment's, so whether a share can be worked out is the
        // same for every payment.
        bool shareIsKnown = ArtistShare.Compute(0m, terms, studioCurrency).IsKnown;

        DateTime now = DateTime.UtcNow;

        Dictionary<Guid, DateTime> appointmentDates = await db.Appointments
            .Where(a => a.ArtistId == artist.Id)
            .Select(a => new { a.Id, a.Date })
            .ToDictionaryAsync(a => a.Id, a => a.Date, ct);

        List<Payment> allCollectedPayments = appointmentDates.Count == 0
            ? []
            : await db.Payments
                .AsNoTracking()
                .Include(p => p.Client)
                .Where(p => appointmentDates.Keys.Contains(p.AppointmentId)
                    && (p.Status == PaymentStatus.Paid || p.Status == PaymentStatus.Refunded)
                    && p.PaidAt != null)
                .ToListAsync(ct);

        // Never sum across currencies — same rule as GetRevenueSummaryQuery.
        List<Payment> collectedPayments = allCollectedPayments
            .Where(p => string.Equals(p.Currency, studioCurrency, StringComparison.OrdinalIgnoreCase))
            .ToList();
        int excludedOtherCurrencyCount = allCollectedPayments.Count - collectedPayments.Count;
        if (excludedOtherCurrencyCount > 0)
        {
            string[] excludedCurrencies = allCollectedPayments
                .Where(p => !string.Equals(p.Currency, studioCurrency, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Currency)
                .Distinct()
                .ToArray();
            logger.LogWarning(
                "Revenue query {Query} excluded {ExcludedCount} payment(s) not in studio currency {StudioCurrency}: {ExcludedCurrencies}",
                nameof(GetMyEarningsQuery), excludedOtherCurrencyCount, studioCurrency, excludedCurrencies);
        }

        decimal ShareOf(Payment p) => ArtistShare.Compute(p.RetainedAmount(), terms, studioCurrency).ArtistAmount;

        List<EarningsMonthPoint> monthlyTrend = new(12);
        for (int i = 11; i >= 0; i--)
        {
            DateTime monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-i);
            DateTime monthEnd = monthStart.AddMonths(1);

            List<Payment> inMonth = collectedPayments
                .Where(p => p.PaidAt!.Value >= monthStart && p.PaidAt!.Value < monthEnd)
                .ToList();
            bool monthKnown = shareIsKnown || inMonth.Count == 0;

            monthlyTrend.Add(new EarningsMonthPoint(
                monthStart.ToString("yyyy-MM"),
                monthKnown ? inMonth.Sum(ShareOf) : null,
                inMonth.Sum(p => p.RetainedAmount()),
                monthKnown));
        }

        DateTime periodFrom = query.From ?? now.AddDays(-30);
        DateTime periodTo = query.To ?? now;

        List<Payment> periodPayments = collectedPayments
            .Where(p => p.PaidAt!.Value >= periodFrom && p.PaidAt!.Value <= periodTo)
            .ToList();

        string arrangement = terms.Arrangement.ToString();
        decimal? rate = terms.Arrangement == ArtistPayArrangement.Commission ? terms.CommissionRate : null;
        decimal? fee = terms.Arrangement == ArtistPayArrangement.FlatFeePerSession ? terms.FlatFeePerSession : null;

        if (periodPayments.Count == 0)
            return new ArtistEarningsResponse(
                monthlyTrend, shareIsKnown ? 0m : null, 0m, [], studioCurrency, excludedOtherCurrencyCount,
                shareIsKnown, arrangement, rate, fee);

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
                p.PaidAt!.Value,
                $"{p.Client?.FirstName} {p.Client?.LastName}".Trim(),
                p.RetainedAmount(),
                shareIsKnown ? ShareOf(p) : null,
                shareIsKnown,
                splitsByPayment[p.Id]
                    .Select(s => new SessionSplitResponse(s.Id, s.PaymentId, s.Label, s.Amount, s.PaidAt))
                    .ToList()))
            .ToList();

        return new ArtistEarningsResponse(
            monthlyTrend,
            shareIsKnown ? periodPayments.Sum(ShareOf) : null,
            periodPayments.Sum(p => p.RetainedAmount()),
            lines,
            studioCurrency,
            excludedOtherCurrencyCount,
            shareIsKnown,
            arrangement,
            rate,
            fee);
    }
}
