using System.Globalization;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Payments;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Money;

namespace Pena_e_Arte.Application.Reports.Queries;

/// <summary>Owner-only CSV export of the studio's payment-level revenue ledger — one row per
/// Payment, not the aggregated GetRevenueSummaryQuery view. Same Paid/Refunded inclusion rule
/// as GetRevenueSummaryHandler (a partially-refunded payment still retains money). Standard
/// tenant-scoped read — Payment is a TenantEntity, the global query filter already scopes
/// this; no IgnoreQueryFilters().
/// <para>E05b-1 (O08): <paramref name="From"/>/<paramref name="To"/> are studio-local dates, resolved through
/// the studio's timezone the same way the rows' timestamps are printed (<see cref="StudioPeriod"/>); they
/// used to be compared as raw UTC, so a row printed as 1 Oct 00:30 could be left out of an October export.
/// <paramref name="Search"/> and <paramref name="Status"/> are the payments list's filters
/// (<see cref="PaymentFilters"/>), so an export can reproduce what the list shows, limited to Paid and
/// Refunded. The file opens with a block stating the period (local dates and UTC instants), timezone,
/// filters and currency rule it was produced with, then a blank line, then the header row.</para></summary>
public record ExportRevenueCsvQuery(DateOnly? From, DateOnly? To, string? Search = null, string? Status = null)
    : IRequest<string>;

public class ExportRevenueCsvHandler(IAppDbContext db, ICurrentTenant tenant, TimeProvider clock)
    : IRequestHandler<ExportRevenueCsvQuery, string>
{
    public const string HeaderFirstColumn = "Paid At";

    public async Task<string> Handle(ExportRevenueCsvQuery query, CancellationToken ct)
    {
        string? timezone = await db.Studios
            .Where(s => s.Id == tenant.StudioId)
            .Select(s => s.Timezone)
            .FirstOrDefaultAsync(ct);
        // Stored instants are UTC; the owner reads the studio's wall-clock time (Help: the timezone
        // "controls how appointment times are shown in emails, texts, and reports").
        string Local(DateTime utc) => TimezoneUtils.ToStudioLocalForRules(utc, timezone)
            .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        static string Utc(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm'Z'", CultureInfo.InvariantCulture);

        StudioPeriod period = StudioPeriod.Resolve(query.From, query.To, timezone);
        bool hasStatus = PaymentFilters.TryParseStatus(query.Status, out PaymentStatus status);
        string? search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();

        StringBuilder sb = new();
        sb.Append(CsvUtils.Bom);
        AppendScope(sb, period, hasStatus ? status : null, search, Local(clock.GetUtcNow().UtcDateTime), Utc);
        CsvUtils.AppendRow(sb, HeaderFirstColumn, "Client", "Artist", "Appointment Date", "Amount",
            "Retained Amount", "Currency", "Status", "Method", "Provider");

        IQueryable<Payment> paymentsQuery = db.Payments
            .AsNoTracking()
            .Include(p => p.Client)
            .Include(p => p.Appointment).ThenInclude(a => a.Artist)
            .Where(p => p.StudioId == tenant.StudioId
                && (p.Status == PaymentStatus.Paid || p.Status == PaymentStatus.Refunded)
                && p.PaidAt != null)
            .MatchingSearch(search)
            .PaidWithin(period);

        if (hasStatus) paymentsQuery = paymentsQuery.Where(p => p.Status == status);

        paymentsQuery = paymentsQuery.OrderBy(p => p.PaidAt);

        await foreach (Payment p in paymentsQuery.AsAsyncEnumerable().WithCancellation(ct))
        {
            // Each row is a single payment, so it always formats in its own currency — this
            // export never sums rows together (unlike GetRevenueSummaryQuery's totals), so there
            // is nothing to exclude here, just the right number of decimals per row.
            string amountFormat = "F" + CurrencyCatalog.MinorUnits(p.Currency).ToString(CultureInfo.InvariantCulture);
            CsvUtils.AppendRow(sb,
                Local(p.PaidAt!.Value),
                $"{p.Client.FirstName} {p.Client.LastName}",
                p.Appointment.Artist is null ? "" : $"{p.Appointment.Artist.FirstName} {p.Appointment.Artist.LastName}",
                Local(p.Appointment.Date),
                p.Amount.ToString(amountFormat, CultureInfo.InvariantCulture),
                p.RetainedAmount().ToString(amountFormat, CultureInfo.InvariantCulture),
                p.Currency,
                p.Status.ToString(),
                p.Method.ToString(),
                p.Provider);
        }

        return sb.ToString();
    }

    /// <summary>
    /// What the file covers, as two-column rows a spreadsheet shows above the table, so whoever receives the
    /// file can check it without asking how it was produced.
    /// </summary>
    private static void AppendScope(StringBuilder sb, StudioPeriod period, PaymentStatus? status, string? search,
        string exportedAtLocal, Func<DateTime, string> utc)
    {
        static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        string localPeriod = (period.From, period.To) switch
        {
            (null, null) => "All payments up to the export time",
            ({ } f, null) => $"From {Day(f)} (start of day) up to the export time",
            (null, { } t) => $"Up to the end of {Day(t)}",
            ({ } f, { } t) => $"{Day(f)} to {Day(t)}, both days included",
        };
        string utcPeriod = (period.FromUtc, period.ToUtcExclusive) switch
        {
            (null, null) => "No bounds",
            ({ } f, null) => $"From {utc(f)}",
            (null, { } t) => $"Before {utc(t)}",
            ({ } f, { } t) => $"From {utc(f)} up to, not including, {utc(t)}",
        };

        CsvUtils.AppendRow(sb, "Revenue export", "One row per payment, by the date it was paid");
        CsvUtils.AppendRow(sb, "Period (studio dates)", localPeriod);
        CsvUtils.AppendRow(sb, "Period (UTC)", utcPeriod);
        CsvUtils.AppendRow(sb, "Timezone", $"{period.TimezoneId} (every date and time below is in this timezone)");
        CsvUtils.AppendRow(sb, "Statuses", status is null ? "Paid, Refunded" : status.Value.ToString());
        CsvUtils.AppendRow(sb, "Search", search ?? "None");
        CsvUtils.AppendRow(sb, "Amounts",
            "Each row is in its own Currency and is never converted or added to another. Retained Amount is Amount minus refunds.");
        CsvUtils.AppendRow(sb, "Exported at", exportedAtLocal);
        sb.AppendLine(); // the blank line before the header row
    }
}
