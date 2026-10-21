// The platform default notice window itself is NOT kept here any more: screens read it from
// GET /api/v1/deposit-rules/defaults (useGetDepositRuleDefaultsQuery), which serves the backend's
// AppointmentSelfServiceDefaults. A frontend copy drifted to 24 h while the server applied 48
// (G-3, G-17, October 2026).

/**
 * "48 hours", "36 hours" — or whole days for longer windows ("3 days", "30 days"), so a 30-day
 * policy doesn't read "720 hours" (G-11). Windows of 48 h or less stay in hours, matching how
 * the platform default is described everywhere ("48-hour").
 */
export function formatNoticeWindow(hours: number): string {
  if (hours > 48 && hours % 24 === 0) return `${hours / 24} days`;
  return hours === 1 ? "1 hour" : `${hours} hours`;
}
