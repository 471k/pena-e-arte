using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Payments;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Reports.Queries;

/// <summary>
/// Owner-facing revenue report: a 12-month trend (same lookback window as
/// GetMrrHistoryQuery, for consistency) plus a per-artist breakdown for a selectable
/// period. Standard tenant-scoped read — Payment/Appointment are TenantEntity, the
/// global query filter already scopes this to the caller's studio; no IgnoreQueryFilters().
/// </summary>
public record GetRevenueSummaryQuery(DateTime? From = null, DateTime? To = null)
    : IRequest<RevenueSummaryResponse>;

public class GetRevenueSummaryHandler(IAppDbContext db, ICurrentTenant tenant, ILogger<GetRevenueSummaryHandler> logger)
    : IRequestHandler<GetRevenueSummaryQuery, RevenueSummaryResponse>
{
    public async Task<RevenueSummaryResponse> Handle(GetRevenueSummaryQuery query, CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;

        string studioCurrency = await db.Studios
            .Where(s => s.Id == tenant.StudioId)
            .Select(s => s.Currency)
            .SingleAsync(ct);

        // Paid = fully retained. Refunded also counts here because a partial refund (e.g. a
        // late self-cancellation with a studio-configured partial-refund percentage) still
        // leaves Status == Refunded — there is no separate "PartiallyRefunded" status — so a
        // payment retaining money must not be excluded outright. RetainedAmount below is what
        // actually contributes to revenue; a fully-refunded payment naturally contributes 0.
        List<Payment> allCollectedPayments = await db.Payments
            .AsNoTracking()
            .Where(p => (p.Status == PaymentStatus.Paid || p.Status == PaymentStatus.Refunded) && p.PaidAt != null)
            .ToListAsync(ct);

        // Never sum across currencies (same bug class as the pre-July MRR one) — a studio's
        // currency can change (before its first payment locks it) or a legacy row can predate
        // the lock; either way, mixing currencies into one total would be meaningless.
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
                nameof(GetRevenueSummaryQuery), excludedOtherCurrencyCount, studioCurrency, excludedCurrencies);
        }

        List<MonthlyRevenuePoint> monthlyTrend = new(12);
        for (int i = 11; i >= 0; i--)
        {
            DateTime monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-i);
            DateTime monthEnd = monthStart.AddMonths(1);

            decimal revenue = collectedPayments
                .Where(p => p.PaidAt!.Value >= monthStart && p.PaidAt!.Value < monthEnd)
                .Sum(p => p.RetainedAmount());

            monthlyTrend.Add(new MonthlyRevenuePoint(monthStart.ToString("yyyy-MM"), revenue));
        }

        DateTime periodFrom = query.From ?? now.AddDays(-30);
        DateTime periodTo = query.To ?? now;

        List<Payment> periodPayments = collectedPayments
            .Where(p => p.PaidAt!.Value >= periodFrom && p.PaidAt!.Value <= periodTo)
            .ToList();

        if (periodPayments.Count == 0)
            return new RevenueSummaryResponse(monthlyTrend, [], studioCurrency, excludedOtherCurrencyCount);

        List<Guid> appointmentIds = periodPayments.Select(p => p.AppointmentId).Distinct().ToList();
        Dictionary<Guid, Guid?> artistIdByAppointment = await db.Appointments
            .Where(a => appointmentIds.Contains(a.Id))
            .Select(a => new { a.Id, a.ArtistId })
            .ToDictionaryAsync(a => a.Id, a => a.ArtistId, ct);

        Dictionary<Guid, decimal> revenueByArtist = new();
        foreach (Payment payment in periodPayments)
        {
            // Unassigned (studio-choice, not yet assigned) appointments have no artist to
            // attribute revenue to — excluded from the per-artist breakdown, same as any
            // other appointment whose id isn't found at all.
            if (!artistIdByAppointment.TryGetValue(payment.AppointmentId, out Guid? artistId) || artistId is null)
                continue;

            revenueByArtist[artistId.Value] = revenueByArtist.GetValueOrDefault(artistId.Value) + payment.RetainedAmount();
        }

        Dictionary<Guid, string> artistNames = await db.Artists
            .Where(a => revenueByArtist.Keys.Contains(a.Id))
            .Select(a => new { a.Id, Name = a.FirstName + " " + a.LastName })
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        // A fully-refunded payment retains $0 — excluded here rather than shown as a
        // zero-revenue row, since it contributed nothing for this period.
        List<ArtistRevenuePoint> perArtist = revenueByArtist
            .Where(kv => kv.Value > 0m)
            .Select(kv => new ArtistRevenuePoint(
                kv.Key, artistNames.GetValueOrDefault(kv.Key, "—"), kv.Value))
            .OrderByDescending(a => a.Revenue)
            .ToList();

        return new RevenueSummaryResponse(monthlyTrend, perArtist, studioCurrency, excludedOtherCurrencyCount);
    }
}
