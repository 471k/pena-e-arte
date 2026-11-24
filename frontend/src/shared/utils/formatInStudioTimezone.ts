/** Injects the studio's IANA timezone into a set of Intl.DateTimeFormat options, so
 * appointment timestamps render in studio-local time rather than the viewer's own browser
 * timezone — correctness fix for cross-timezone bookings (a guest-artist/tattoo-tourism
 * client viewing a studio in a different zone than their own). Falls back to the options
 * unchanged (browser-local) when the studio's timezone hasn't loaded yet. */
/*
 * WHICH ZONE A DATE IS SHOWN IN (S01, the rule, 24 Nov 2026):
 *
 *   A date that locates something in the studio's operating day follows the STUDIO's zone.
 *   A date that records when something happened follows the READER's.
 *
 * Studio zone: an appointment, a slot, a session, studio hours, a waitlist offer, and any date used to put money
 * into a studio-calendar period. That last one is why MyEarningsPage's "Paid" and the payment list's paid date are
 * studio-zone although they look like record timestamps. Artists reconcile earnings against the studio's books by
 * the studio's operating day, and the payment list's From/To filter is studio-local dates on PaidAt
 * (GetPaymentsQuery). The question is what the date is used for, not whether it is a timestamp.
 * Reader zone: "Saved", "Created", "Last synced", a message's time, "erasure requested on", "generated".
 *
 * A third kind follows NEITHER zone: a date with no time at all (a date of birth, a closure day, a tattoo record's
 * completion date). The API stores it as UTC midnight, so print it with formatDateOnly (studioDay.ts), which pins
 * UTC. Shown in any zone west of UTC, it would read as the day before.
 *
 * Studio-zone dates go through formatStudioTime with a StudioZone (useStudioZone), never withStudioTimeZone's
 * browser fallback. The per-file audit: docs/claude/s01-zone-audit-2026-11-24.md.
 */

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
