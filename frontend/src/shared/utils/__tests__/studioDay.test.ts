import { describe, it, expect } from "vitest";
import {
  addWallDays, formatSessionSpan, formatStudioSessionSpan, formatWallDate, isSameStudioDay, resolveStudioWallTime, studioDayBounds,
  studioMidnight, studioWallDate, studioWeekStart, toStudioInputValue,
  checkStudioTime, studioTimeProblem, studioWallInstant, studioZoneFrom,
} from "../studioDay";
import { STUDIO_TIME_PENDING, STUDIO_TIME_UNAVAILABLE } from "../formatInStudioTimezone";
import { formatDateOnly } from "../studioDay";
import { withViewerZone } from "@/test/viewerZone";

describe("studioDayBounds", () => {
  it("gives the studio's midnight-to-midnight, not the runner's", () => {
    // 4 Nov 2026 23:30 UTC is already 5 Nov 00:30 in Tirana (CET, UTC+1).
    const { start, end } = studioDayBounds(new Date("2026-11-04T23:30:00Z"), "Europe/Tirane");
    expect(start.toISOString()).toBe("2026-11-04T23:00:00.000Z");
    expect(end.toISOString()).toBe("2026-11-05T23:00:00.000Z");
  });

  it("handles a zone behind UTC", () => {
    const { start, end } = studioDayBounds(new Date("2026-11-05T03:00:00Z"), "America/New_York");
    expect(start.toISOString()).toBe("2026-11-04T05:00:00.000Z");
    expect(end.toISOString()).toBe("2026-11-05T05:00:00.000Z");
  });

  it("is 23 hours on the spring-forward day and 25 on the fall-back day", () => {
    const spring = studioDayBounds(new Date("2026-03-29T12:00:00Z"), "Europe/Tirane");
    expect(spring.start.toISOString()).toBe("2026-03-28T23:00:00.000Z");
    expect((spring.end.getTime() - spring.start.getTime()) / 3_600_000).toBe(23);
    const fall = studioDayBounds(new Date("2026-10-25T12:00:00Z"), "Europe/Tirane");
    expect(fall.start.toISOString()).toBe("2026-10-24T22:00:00.000Z");
    expect((fall.end.getTime() - fall.start.getTime()) / 3_600_000).toBe(25);
  });

  it("rolls over the end of a month and a year", () => {
    const { start, end } = studioDayBounds(new Date("2026-12-31T12:00:00Z"), "Europe/Tirane");
    expect(start.toISOString()).toBe("2026-12-30T23:00:00.000Z");
    expect(end.toISOString()).toBe("2026-12-31T23:00:00.000Z");
  });
});

describe("formatSessionSpan", () => {
  it("names the date once for a same-day session", () => {
    expect(formatSessionSpan("2026-11-04T13:00:00Z", "2026-11-04T15:00:00Z", "Europe/Tirane"))
      .toBe("Wed 4 Nov, 14:00 – 16:00");
  });

  it("names both dates for a session that crosses midnight", () => {
    expect(formatSessionSpan("2026-11-04T22:00:00Z", "2026-11-05T01:00:00Z", "Europe/Tirane"))
      .toBe("Wed 4 Nov, 23:00 – Thu 5 Nov, 02:00");
  });

  it("decides 'same day' in the studio's zone, not UTC", () => {
    // 22:30–23:30 UTC on 4 Nov is 23:30 on the 4th to 00:30 on the 5th in Tirana.
    expect(isSameStudioDay(new Date("2026-11-04T22:30:00Z"), new Date("2026-11-04T23:30:00Z"), "Europe/Tirane")).toBe(false);
    expect(isSameStudioDay(new Date("2026-11-04T22:30:00Z"), new Date("2026-11-04T23:30:00Z"), "UTC")).toBe(true);
  });
});

// ── S01: the studio-zone calendar ──────────────────────────────────────────────────────────────────
// Paris changes clock on Sun 29 Mar 2026 (02:00 -> 03:00) and Sun 25 Oct 2026 (03:00 -> 02:00).
const PARIS = "Europe/Paris";

describe("studio wall dates and weeks", () => {
  it("names the studio's day, not the runner's: 23:30 UTC on 19 Nov is Fri 20 Nov in Paris", () => {
    expect(studioWallDate(new Date("2026-11-19T23:30:00Z"), PARIS)).toEqual({ year: 2026, month: 11, day: 20 });
    expect(studioWallDate(new Date("2026-11-19T23:30:00Z"), "America/New_York")).toEqual({ year: 2026, month: 11, day: 19 });
  });

  it("starts the week on the studio's Monday, including from a Sunday", () => {
    expect(studioWeekStart(new Date("2026-11-18T12:00:00Z"), PARIS)).toEqual({ year: 2026, month: 11, day: 16 });
    expect(studioWeekStart(new Date("2026-11-22T22:30:00Z"), PARIS)).toEqual({ year: 2026, month: 11, day: 16 });
    // 23:30 UTC Sunday is already Monday 00:30 in Paris: the next week.
    expect(studioWeekStart(new Date("2026-11-22T23:30:00Z"), PARIS)).toEqual({ year: 2026, month: 11, day: 23 });
  });

  it("makes the spring-forward week 167 hours and the fall-back week 169", () => {
    const spring = { year: 2026, month: 3, day: 23 };
    const springFrom = studioMidnight(spring, PARIS);
    const springTo = studioMidnight(addWallDays(spring, 7), PARIS);
    expect(springFrom.toISOString()).toBe("2026-03-22T23:00:00.000Z");
    expect(springTo.toISOString()).toBe("2026-03-29T22:00:00.000Z");
    expect((springTo.getTime() - springFrom.getTime()) / 3_600_000).toBe(167);

    const fall = { year: 2026, month: 10, day: 19 };
    const fallFrom = studioMidnight(fall, PARIS);
    const fallTo = studioMidnight(addWallDays(fall, 7), PARIS);
    expect(fallFrom.toISOString()).toBe("2026-10-18T22:00:00.000Z");
    expect(fallTo.toISOString()).toBe("2026-10-25T23:00:00.000Z");
    expect((fallTo.getTime() - fallFrom.getTime()) / 3_600_000).toBe(169);
  });

  it("formats a wall date as itself whatever zone the runner is in", () => {
    expect(formatWallDate({ year: 2026, month: 3, day: 29 }, { weekday: "short", day: "numeric", month: "short" }))
      .toBe("Sun 29 Mar");
  });
});

describe("studio wall clock <-> instant", () => {
  it("writes an instant as the studio's datetime-local value", () => {
    // 09:00 UTC is 10:00 in Paris in November; the old UTC-string minimum would have said 09:00.
    expect(toStudioInputValue(new Date("2026-11-15T09:00:00Z"), PARIS)).toBe("2026-11-15T10:00");
    expect(toStudioInputValue(new Date("2026-11-15T09:00:00Z"), "America/New_York")).toBe("2026-11-15T04:00");
  });

  it("reads a studio wall time as one instant on an ordinary day", () => {
    expect(resolveStudioWallTime("2026-11-20T15:00", PARIS)).toEqual({ kind: "ok", instant: new Date("2026-11-20T14:00:00Z") });
    expect(resolveStudioWallTime("2026-07-01T09:00", PARIS)).toEqual({ kind: "ok", instant: new Date("2026-07-01T07:00:00Z") });
  });

  it("reads local midnight as the start of that studio day", () => {
    expect(resolveStudioWallTime("2026-11-20T00:00", PARIS)).toEqual({ kind: "ok", instant: new Date("2026-11-19T23:00:00Z") });
  });

  it("refuses a time skipped when the clocks go forward", () => {
    expect(resolveStudioWallTime("2026-03-29T02:30", PARIS)).toEqual({ kind: "gap" });
    // Either side of the gap still resolves.
    expect(resolveStudioWallTime("2026-03-29T01:59", PARIS)).toEqual({ kind: "ok", instant: new Date("2026-03-29T00:59:00Z") });
    expect(resolveStudioWallTime("2026-03-29T03:00", PARIS)).toEqual({ kind: "ok", instant: new Date("2026-03-29T01:00:00Z") });
  });

  it("reports both instants of a time repeated when the clocks go back", () => {
    expect(resolveStudioWallTime("2026-10-25T02:30", PARIS)).toEqual({
      kind: "repeated", first: new Date("2026-10-25T00:30:00Z"), second: new Date("2026-10-25T01:30:00Z"),
    });
  });

  it("handles a zone behind UTC and its own transitions", () => {
    // New York: 8 Mar 2026 02:00 -> 03:00, 1 Nov 2026 02:00 -> 01:00.
    expect(resolveStudioWallTime("2026-03-08T02:30", "America/New_York")).toEqual({ kind: "gap" });
    expect(resolveStudioWallTime("2026-11-01T01:30", "America/New_York").kind).toBe("repeated");
  });

  it("rejects anything that isn't a datetime-local value", () => {
    expect(resolveStudioWallTime("", PARIS)).toEqual({ kind: "invalid" });
    expect(resolveStudioWallTime("2026-11-20", PARIS)).toEqual({ kind: "invalid" });
  });
});

describe("studioWallInstant (the one rule for a picked studio time)", () => {
  it("reads the value on the studio's clock", () => {
    expect(studioWallInstant("2030-03-05T22:00", "Asia/Tokyo")!.toISOString()).toBe("2030-03-05T13:00:00.000Z");
  });
  it("a repeated hour uses its first occurrence", () => {
    // 25 Oct 2026, Paris: 02:30 happens at 00:30Z (CEST) and again at 01:30Z (CET).
    expect(studioWallInstant("2026-10-25T02:30", "Europe/Paris")!.toISOString()).toBe("2026-10-25T00:30:00.000Z");
  });
  it("a skipped hour, an invalid value or an unknown zone give null, never a browser-zone guess", () => {
    expect(studioWallInstant("2026-03-29T02:30", "Europe/Paris")).toBeNull();
    expect(studioWallInstant("2026-03-29", "Europe/Paris")).toBeNull();
    expect(studioWallInstant("2026-11-20T10:00", null)).toBeNull();
    expect(studioWallInstant("", "Europe/Paris")).toBeNull();
  });
});

describe("studioZoneFrom", () => {
  it("is ready with a zone, an error when the lookup failed, loading otherwise", () => {
    expect(studioZoneFrom("Europe/Tirane", false)).toEqual({ status: "ready", timeZone: "Europe/Tirane" });
    expect(studioZoneFrom(undefined, true)).toEqual({ status: "error" });
    expect(studioZoneFrom(null, false)).toEqual({ status: "loading" });
  });
});

describe("checkStudioTime", () => {
  const now = new Date("2030-03-05T12:00:00Z");
  const tokyo = { status: "ready", timeZone: "Asia/Tokyo" } as const;

  it("gives the instant for a future studio time", () => {
    expect(checkStudioTime("2030-03-05T22:00", tokyo, now)).toEqual({ ok: true, instant: new Date("2030-03-05T13:00:00Z") });
  });
  it("names each reason it can't be sent", () => {
    expect(checkStudioTime("2030-03-05T22:00", { status: "loading" }, now)).toEqual({ ok: false, reason: "zone-loading" });
    expect(checkStudioTime("2030-03-05T22:00", { status: "error" }, now)).toEqual({ ok: false, reason: "zone-error" });
    expect(checkStudioTime("", tokyo, now)).toEqual({ ok: false, reason: "invalid" });
    expect(checkStudioTime("2030-03-31T02:30", { status: "ready", timeZone: "Europe/Paris" }, now)).toEqual({ ok: false, reason: "gap" });
    // 20:00 in Tokyo is 11:00Z, an hour before "now".
    expect(checkStudioTime("2030-03-05T20:00", tokyo, now)).toEqual({ ok: false, reason: "past" });
  });
  it("the past message is the caller's own; the rest are shared", () => {
    expect(studioTimeProblem("past", "Appointment must be in the future")).toBe("Appointment must be in the future");
    expect(studioTimeProblem("gap", "x")).toMatch(/clocks go forward/);
  });
});

describe("formatStudioSessionSpan (S01: a zone that may not have loaded)", () => {
  const start = "2026-11-04T13:00:00Z";
  const end = "2026-11-04T15:00:00Z";

  it("reads as pending while the zone loads, never as the viewer's zone", () => {
    expect(formatStudioSessionSpan(start, end, { status: "loading" })).toBe(STUDIO_TIME_PENDING);
  });

  it("says the time is unavailable when the zone couldn't be read", () => {
    expect(formatStudioSessionSpan(start, end, { status: "error" })).toBe(STUDIO_TIME_UNAVAILABLE);
  });

  it("is formatSessionSpan in the studio's zone once it has loaded", () => {
    expect(formatStudioSessionSpan(start, end, { status: "ready", timeZone: "Europe/Tirane" }))
      .toBe("Wed 4 Nov, 14:00 – 16:00");
  });
});

describe("formatDateOnly", () => {
  it("prints a date stored as UTC midnight as that date in any viewer zone", async () => {
    const opts: Intl.DateTimeFormatOptions = { day: "numeric", month: "short", year: "numeric" };
    for (const tz of ["Pacific/Pago_Pago", "America/New_York", "UTC", "Europe/Tirane", "Pacific/Kiritimati"]) {
      await withViewerZone(tz, () => {
        expect(formatDateOnly("2026-12-24T00:00:00.000Z", opts)).toBe("24 Dec 2026");
        // A date of birth arrives as a bare date, which Date also reads as UTC midnight.
        expect(formatDateOnly("1990-05-01", opts)).toBe("1 May 1990");
      });
    }
  });
});
