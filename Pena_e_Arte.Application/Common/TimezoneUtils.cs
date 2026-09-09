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
