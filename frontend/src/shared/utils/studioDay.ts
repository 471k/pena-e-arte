import {
  STUDIO_TIME_PENDING, STUDIO_TIME_UNAVAILABLE, withStudioTimeZone, type StudioZone,
} from "./formatInStudioTimezone";

/**
 * Calendar helpers for "today" in the studio's timezone (the artist's Today view). These are wall-clock
 * calculations only: no policy, notice window or money is worked out here.
 */

/** A calendar date with no time or zone: a day as the studio's wall calendar names it. */
export interface WallDate { year: number; month: number; day: number }

function wallParts(instant: Date, timeZone: string): WallDate & { hour: number; minute: number; second: number } {
  const parts = new Intl.DateTimeFormat("en-US", {
    timeZone, hourCycle: "h23",
    year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", second: "2-digit",
  }).formatToParts(instant);
  const get = (type: Intl.DateTimeFormatPartTypes): number => Number(parts.find((p) => p.type === type)?.value);
  return { year: get("year"), month: get("month"), day: get("day"), hour: get("hour"), minute: get("minute"), second: get("second") };
}

/** The zone's offset from UTC at `instant`, in ms (wall clock minus UTC). */
function offsetMs(instant: Date, timeZone: string): number {
  const p = wallParts(instant, timeZone);
  const wallAsUtc = Date.UTC(p.year, p.month - 1, p.day, p.hour, p.minute, p.second);
  return wallAsUtc - Math.floor(instant.getTime() / 1000) * 1000;
}

/** The instant of local midnight at the start of the given wall date in `timeZone`. */
function midnight(date: WallDate, timeZone: string): Date {
  const guess = Date.UTC(date.year, date.month - 1, date.day);
  // Two passes settle a DST change between the guess and the real midnight.
  const first = guess - offsetMs(new Date(guess), timeZone);
  return new Date(guess - offsetMs(new Date(first), timeZone));
}

/**
 * The start (inclusive) and end (exclusive) of the studio's current day containing `now`. Without a
 * timezone (not loaded yet) the browser's own day is used.
 */
export function studioDayBounds(now: Date, timeZone: string | undefined): { start: Date; end: Date } {
  if (!timeZone) {
    const start = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    const end = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1);
    return { start, end };
  }
  const today = wallParts(now, timeZone);
  // Date.UTC rolls day 32 into the next month, so "day + 1" is always the next calendar day.
  const next = new Date(Date.UTC(today.year, today.month - 1, today.day + 1));
  return {
    start: midnight(today, timeZone),
    end: midnight({ year: next.getUTCFullYear(), month: next.getUTCMonth() + 1, day: next.getUTCDate() }, timeZone),
  };
}

// ── Studio-zone calendar (S01) ────────────────────────────────────────────────────────────────────
// Days and weeks are named by the studio's wall calendar and only turned into instants through the
// studio's zone, so a viewer in another zone sees the same studio day, and a week that contains a
// daylight-saving change is 167 or 169 hours long rather than shifted by an hour.

/** The studio's wall date containing `instant`. */
export function studioWallDate(instant: Date, timeZone: string): WallDate {
  const p = wallParts(instant, timeZone);
  return { year: p.year, month: p.month, day: p.day };
}

/** `date` moved by `days` calendar days (wall arithmetic, no zone involved). */
export function addWallDays(date: WallDate, days: number): WallDate {
  const d = new Date(Date.UTC(date.year, date.month - 1, date.day + days));
  return { year: d.getUTCFullYear(), month: d.getUTCMonth() + 1, day: d.getUTCDate() };
}

/** The instant the studio's day `date` begins (its local midnight; DST-safe). */
export function studioMidnight(date: WallDate, timeZone: string): Date {
  return midnight(date, timeZone);
}

/** The Monday of the studio's week containing `now`. */
export function studioWeekStart(now: Date, timeZone: string): WallDate {
  const today = studioWallDate(now, timeZone);
  const weekday = new Date(Date.UTC(today.year, today.month - 1, today.day)).getUTCDay(); // 0 = Sunday
  return addWallDays(today, weekday === 0 ? -6 : 1 - weekday);
}

export function sameWallDate(a: WallDate, b: WallDate): boolean {
  return a.year === b.year && a.month === b.month && a.day === b.day;
}

/** Formats a wall date as itself. It is pinned to UTC, so no zone can move it to another day. */
export function formatWallDate(date: WallDate, options: Intl.DateTimeFormatOptions): string {
  return new Date(Date.UTC(date.year, date.month - 1, date.day, 12))
    .toLocaleDateString("en-GB", { ...options, timeZone: "UTC" });
}

const pad2 = (n: number) => String(n).padStart(2, "0");

/** `instant` as a `datetime-local` value ("YYYY-MM-DDTHH:mm") on the studio's wall clock. */
export function toStudioInputValue(instant: Date, timeZone: string): string {
  const p = wallParts(instant, timeZone);
  return `${p.year}-${pad2(p.month)}-${pad2(p.day)}T${pad2(p.hour)}:${pad2(p.minute)}`;
}

/**
 * What a studio wall-clock time means as an instant. Daylight saving makes two cases explicit rather
 * than guessed: a time skipped when the clocks go forward does not exist (`gap`), and a time that
 * happens twice when they go back is `repeated`, with both instants.
 */
export type StudioWallTime =
  | { kind: "ok"; instant: Date }
  | { kind: "repeated"; first: Date; second: Date }
  | { kind: "gap" }
  | { kind: "invalid" };

export function resolveStudioWallTime(value: string, timeZone: string): StudioWallTime {
  const m = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(value);
  if (!m) return { kind: "invalid" };
  const [year, month, day, hour, minute] = m.slice(1).map(Number);
  const wallAsUtc = Date.UTC(year, month - 1, day, hour, minute);
  // Every offset the zone uses within a day either side; a wall time can only map through one of them.
  const offsets = new Set([-86_400_000, 0, 86_400_000].map((d) => offsetMs(new Date(wallAsUtc + d), timeZone)));
  const matches: number[] = [];
  for (const offset of offsets) {
    const candidate = new Date(wallAsUtc - offset);
    const p = wallParts(candidate, timeZone);
    if (p.year === year && p.month === month && p.day === day && p.hour === hour && p.minute === minute)
      matches.push(candidate.getTime());
  }
  const sorted = [...new Set(matches)].sort((a, b) => a - b);
  if (sorted.length === 0) return { kind: "gap" };
  if (sorted.length === 1) return { kind: "ok", instant: new Date(sorted[0]) };
  return { kind: "repeated", first: new Date(sorted[0]), second: new Date(sorted[sorted.length - 1]) };
}

/** Whether two instants fall on the same calendar day in the studio's timezone. */
export function isSameStudioDay(a: Date, b: Date, timeZone: string | undefined): boolean {
  const key = (d: Date) => d.toLocaleDateString("en-CA", withStudioTimeZone({}, timeZone));
  return key(a) === key(b);
}

/**
 * A session's span with the date on both ends when it crosses midnight, so a 23:00–02:00 session can't
 * be read as the same day: "Wed 4 Nov, 14:00 – 16:00" or "Wed 4 Nov, 23:00 – Thu 5 Nov, 02:00".
 */
export function formatSessionSpan(startIso: string, endIso: string, timeZone: string | undefined): string {
  const start = new Date(startIso);
  const end = new Date(endIso);
  const day = (d: Date) => d.toLocaleDateString(
    "en-GB", withStudioTimeZone({ weekday: "short", day: "numeric", month: "short" }, timeZone));
  const time = (d: Date) => d.toLocaleTimeString(
    "en-GB", withStudioTimeZone({ hour: "2-digit", minute: "2-digit", hourCycle: "h23" }, timeZone));
  return isSameStudioDay(start, end, timeZone)
    ? `${day(start)}, ${time(start)} – ${time(end)}`
    : `${day(start)}, ${time(start)} – ${day(end)}, ${time(end)}`;
}

/**
 * formatSessionSpan for a zone that may not have loaded yet: pending or unavailable rather than the browser's
 * zone (S01). Use this wherever the session can render before the studio does.
 */
export function formatStudioSessionSpan(startIso: string, endIso: string, zone: StudioZone): string {
  if (zone.status === "loading") return STUDIO_TIME_PENDING;
  if (zone.status === "error") return STUDIO_TIME_UNAVAILABLE;
  return formatSessionSpan(startIso, endIso, zone.timeZone);
}

/**
 * The instant a studio wall-clock input means, by the app's one rule (S01): a time that happens twice when the
 * clocks go back uses its first occurrence (recorded in the Decisions Log so it isn't reopened). A time the
 * clocks skip, an invalid value or an unknown zone give null — never a guess in the viewer's own zone.
 */
export function studioWallInstant(value: string, timeZone: string | null | undefined): Date | null {
  if (!timeZone || !value) return null;
  const wall = resolveStudioWallTime(value, timeZone);
  return wall.kind === "ok" ? wall.instant : wall.kind === "repeated" ? wall.first : null;
}

/** A studio's zone as an explicit state, from a public studio response (guest booking has no session studio). */
export function studioZoneFrom(timeZone: string | null | undefined, failed: boolean): StudioZone {
  if (timeZone) return { status: "ready", timeZone };
  return failed ? { status: "error" } : { status: "loading" };
}

export type StudioTimeCheck =
  | { ok: true; instant: Date }
  | { ok: false; reason: "zone-loading" | "zone-error" | "invalid" | "gap" | "past" };

/** Whether a picked studio wall time can be sent: the zone is known, the time exists, and it's in the future. */
export function checkStudioTime(value: string, zone: StudioZone, now: Date = new Date()): StudioTimeCheck {
  if (zone.status === "loading") return { ok: false, reason: "zone-loading" };
  if (zone.status === "error") return { ok: false, reason: "zone-error" };
  if (!value) return { ok: false, reason: "invalid" };
  const wall = resolveStudioWallTime(value, zone.timeZone);
  if (wall.kind === "invalid") return { ok: false, reason: "invalid" };
  if (wall.kind === "gap") return { ok: false, reason: "gap" };
  const instant = wall.kind === "ok" ? wall.instant : wall.first;
  if (instant.getTime() <= now.getTime()) return { ok: false, reason: "past" };
  return { ok: true, instant };
}

/** The sentence for a failed {@link checkStudioTime}; `past` is the caller's own ("Appointment must be in the future"). */
export function studioTimeProblem(reason: Exclude<StudioTimeCheck, { ok: true }>["reason"], past: string): string {
  switch (reason) {
    case "zone-loading": return "Still loading the studio's time zone. Try again in a moment.";
    case "zone-error":   return "Couldn't load the studio's time zone, so this time can't be used safely. Reload the page and try again.";
    case "invalid":      return "Select date and time";
    case "gap":          return "That time doesn't exist on that day: the clocks go forward. Pick another time.";
    case "past":         return past;
  }
}
