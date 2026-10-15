namespace Pena_e_Arte.Application.Common;

/// <summary>
/// Converts a UTC instant to a studio's local time for display/notification purposes only —
/// every DateTime stays UTC in storage and in every business-logic comparison; this is used
/// solely to format a string for a human to read (email/SMS bodies, subject lines). Public
/// (not EmailRenderer-private) because both Infrastructure's EmailRenderer and the
/// Application-layer appointment-notification command handlers need it for their own raw
/// date formatting (subject lines, SMS bodies) — found while wiring Phase 2 of the P1 Group 2
/// overnight prompt (2026-09-09): the notification handlers format dates inline themselves
/// rather than exclusively through EmailRenderer, so a private-to-EmailRenderer helper would
/// have missed them.
/// </summary>
public static class TimezoneUtils
{
    /// <summary>
    /// A booking instant as UTC. Every caller sends an ISO instant ("…Z"): JSON binds it as Utc,
    /// query-string binding may hand it over as server-Local (converted back here). Unspecified is
    /// treated as UTC — the stored-instant convention everywhere in this codebase.
    /// </summary>
    public static DateTime AsUtc(DateTime instant) => instant.Kind switch
    {
        DateTimeKind.Utc => instant,
        DateTimeKind.Local => instant.ToUniversalTime(),
        _ => DateTime.SpecifyKind(instant, DateTimeKind.Utc),
    };

    /// <summary>
    /// Studio-local wall-clock time for a UTC instant, for BUSINESS comparisons against
    /// studio-local data (StudioHours, ArtistSchedules, closures, time off) — G-1, found in the
    /// 2026-10-14 POK gate: comparing the UTC instant's TimeOfDay with local opening hours refused
    /// the first 1–2 hours of an Albanian studio's day and accepted 1–2 hours after closing.
    /// Unlike <see cref="ToStudioLocal"/>, an unusable timezone falls back to UTC (the previous
    /// behaviour) for any lookup failure, not only "not found".
    /// </summary>
    public static DateTime ToStudioLocalForRules(DateTime utcInstant, string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
            return utcInstant;
        try
        {
            TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utcInstant), tz);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return utcInstant;
        }
    }

    public static DateTime ToStudioLocal(DateTime utc, string timezone)
    {
        try
        {
            TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
        }
        catch (TimeZoneNotFoundException)
        {
            // Defensive — should be unreachable given UpdateMyStudioValidator's
            // TryFindSystemTimeZoneById check, but a notification must never throw and be
            // lost over a bad timezone string.
            return utc;
        }
    }
}
