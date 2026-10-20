/**
 * The client self-cancel / self-reschedule notice window. When a deposit rule leaves it empty the
 * backend uses AppointmentSelfServiceDefaults.CancellationWindowHours = 48 — keep this in step
 * with it (G-3: the client screen said 24 while the server applied 48).
 */
export const PLATFORM_DEFAULT_CANCELLATION_WINDOW_HOURS = 48;

/**
 * "48 hours", "36 hours" — or whole days for longer windows ("3 days", "30 days"), so a 30-day
 * policy doesn't read "720 hours" (G-11). Windows of 48 h or less stay in hours, matching how
 * the platform default is described everywhere ("48-hour").
 */
export function formatNoticeWindow(hours: number): string {
  if (hours > 48 && hours % 24 === 0) return `${hours / 24} days`;
  return hours === 1 ? "1 hour" : `${hours} hours`;
}
