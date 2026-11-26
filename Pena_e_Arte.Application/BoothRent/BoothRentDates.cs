namespace Pena_e_Arte.Application.BoothRent;

/// <summary>
/// Booth-rent charge dates are calendar dates. The column stays <see cref="DateTime"/>, and every
/// value written to it is that date's midnight UTC, which is what the screens' date-only formatting
/// and <c>BoothRentChargeJob</c> (whole days and months only) both assume.
/// </summary>
/// <remarks>
/// Why the request takes <see cref="DateOnly"/> rather than truncating a <see cref="DateTime"/>:
/// <c>2026-12-01T00:00:00+01:00</c> binds as <c>Kind=Local</c> and is stored as
/// <c>2026-11-30T23:00Z</c>, so truncation keeps the wrong day. See
/// docs/claude/spec-booth-rent-date-only-write-guard-2026-11-25.md.
/// </remarks>
public static class BoothRentDates
{
    public static DateTime ToChargeDate(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
}
