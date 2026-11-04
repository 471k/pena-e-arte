import { withStudioTimeZone } from "./formatInStudioTimezone";

/**
 * Calendar helpers for "today" in the studio's timezone (the artist's Today view). These are wall-clock
 * calculations only: no policy, notice window or money is worked out here.
 */

interface WallDate { year: number; month: number; day: number }

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
