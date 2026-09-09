/** Injects the studio's IANA timezone into a set of Intl.DateTimeFormat options, so
 * appointment timestamps render in studio-local time rather than the viewer's own browser
 * timezone — correctness fix for cross-timezone bookings (a guest-artist/tattoo-tourism
 * client viewing a studio in a different zone than their own). Falls back to the options
 * unchanged (browser-local) when the studio's timezone hasn't loaded yet. */
export function withStudioTimeZone(
  options: Intl.DateTimeFormatOptions,
  studioTimezone: string | undefined,
): Intl.DateTimeFormatOptions {
  return studioTimezone ? { ...options, timeZone: studioTimezone } : options;
}
