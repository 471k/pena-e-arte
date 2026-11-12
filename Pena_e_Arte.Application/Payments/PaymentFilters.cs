using FluentValidation;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Payments;

/// <summary>
/// The filters the owner's payments list and the revenue export share (E05b-1, O05 + O08), in one place so
/// an export run with the list's filters selects the same payments the list shows.
/// </summary>
public static class PaymentFilters
{
    public const int MaxSearchLength = 100;

    /// <summary>
    /// A <see cref="PaymentStatus"/> by its exact enum name. "paid", "1" or " Paid" are refused rather than
    /// matched loosely (Enum.TryParse alone would accept the number), the same rule as the pay-model
    /// arrangement names.
    /// </summary>
    public static bool TryParseStatus(string? value, out PaymentStatus status)
    {
        status = default;
        return value is not null
               && Enum.GetNames<PaymentStatus>().Contains(value, StringComparer.Ordinal)
               && Enum.TryParse(value, ignoreCase: false, out status);
    }

    public static IRuleBuilderOptions<T, string?> ValidPaymentStatus<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(s => s is null || TryParseStatus(s, out _))
            .WithMessage($"Status must be one of: {string.Join(", ", Enum.GetNames<PaymentStatus>())}.");

    public static IRuleBuilderOptions<T, string?> ValidSearch<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(MaxSearchLength);

    /// <summary>
    /// Matches the client's full name or the session's artist's full name, case- and accent-insensitively
    /// (the database's default utf8mb4_0900_ai_ci collation). Never the provider reference or client token:
    /// those are stripped from responses elsewhere and must not become searchable. Blank means no filter.
    /// </summary>
    public static IQueryable<Payment> MatchingSearch(this IQueryable<Payment> q, string? search)
    {
        string? term = search?.Trim();
        if (string.IsNullOrEmpty(term)) return q;
        return q.Where(p =>
            (p.Client.FirstName + " " + p.Client.LastName).Contains(term)
            || (p.Appointment.Artist != null
                && (p.Appointment.Artist.FirstName + " " + p.Appointment.Artist.LastName).Contains(term)));
    }

    /// <summary>
    /// Payments whose <c>PaidAt</c> falls in the period. A payment with no <c>PaidAt</c> (not paid yet) is
    /// excluded whenever either bound is set, because it has no date to compare.
    /// </summary>
    public static IQueryable<Payment> PaidWithin(this IQueryable<Payment> q, StudioPeriod period)
    {
        if (period.FromUtc is null && period.ToUtcExclusive is null) return q;
        q = q.Where(p => p.PaidAt != null);
        if (period.FromUtc is DateTime from) q = q.Where(p => p.PaidAt >= from);
        if (period.ToUtcExclusive is DateTime to) q = q.Where(p => p.PaidAt < to);
        return q;
    }
}

/// <summary>
/// A period of studio-local calendar dates as the UTC instants that bound it: <c>From</c> means the studio's
/// local midnight at the start of that day, <c>To</c> means up to (not including) local midnight at the start
/// of the following day. Resolved through the studio's timezone each time, so a daylight-saving change inside
/// or at the edge of the period moves the instant, never the date (Europe/Tirane is UTC+1 in winter, UTC+2 in
/// summer, so a fixed offset would be an hour wrong half the year).
/// </summary>
public sealed record StudioPeriod(DateOnly? From, DateOnly? To, DateTime? FromUtc, DateTime? ToUtcExclusive, string TimezoneId)
{
    public static StudioPeriod Resolve(DateOnly? from, DateOnly? to, string? timezone)
    {
        DateTime? fromUtc = from is DateOnly f
            ? TimezoneUtils.StudioLocalToUtcForRules(f.ToDateTime(TimeOnly.MinValue), timezone)
            : null;
        DateTime? toUtc = to is DateOnly t
            ? TimezoneUtils.StudioLocalToUtcForRules(t.AddDays(1).ToDateTime(TimeOnly.MinValue), timezone)
            : null;
        return new StudioPeriod(from, to, fromUtc, toUtc,
            string.IsNullOrWhiteSpace(timezone) ? "UTC" : timezone);
    }
}
