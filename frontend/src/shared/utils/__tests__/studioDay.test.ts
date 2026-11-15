import { describe, it, expect } from "vitest";
import {
  addWallDays, formatSessionSpan, formatWallDate, isSameStudioDay, resolveStudioWallTime, studioDayBounds,
  studioMidnight, studioWallDate, studioWeekStart, toStudioInputValue,
} from "../studioDay";

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