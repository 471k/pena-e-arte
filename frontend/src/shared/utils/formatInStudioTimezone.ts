/** Injects the studio's IANA timezone into a set of Intl.DateTimeFormat options, so
 * appointment timestamps render in studio-local time rather than the viewer's own browser
 * timezone — correctness fix for cross-timezone bookings (a guest-artist/tattoo-tourism
 * client viewing a studio in a different zone than their own). Falls back to the options
 * unchanged (browser-local) when the studio's timezone hasn't loaded yet. */
/** The studio's timezone as a state, so "not known yet" and "couldn't be read" can't pass for a time. */
export type StudioZone =
  | { status: "loading" }
  | { status: "error" }
  | { status: "ready"; timeZone: string };

export const STUDIO_TIME_PENDING = "…";
export const STUDIO_TIME_UNAVAILABLE = "Time unavailable";

/**
 * Formats an instant in the studio's zone. Unlike withStudioTimeZone there is no browser-zone fallback:
 * a zone that hasn't loaded reads as pending, and one that couldn't be read says so (S01, and the
 * state contract's "never coerce an error into an empty record").
 */
export function formatStudioTime(
  iso: string | Date,
  options: Intl.DateTimeFormatOptions,
  zone: StudioZone,
): string {
  if (zone.status === "loading") return STUDIO_TIME_PENDING;
  if (zone.status === "error") return STUDIO_TIME_UNAVAILABLE;
  return new Date(iso).toLocaleString("en-GB", { ...options, timeZone: zone.timeZone });
}

export function withStudioTimeZone(
  options: Intl.DateTimeFormatOptions,
  studioTimezone: string | undefined,
): Intl.DateTimeFormatOptions {
  return studioTimezone ? { ...options, timeZone: studioTimezone } : options;
}
