import { describe, it, expect } from "vitest";
import { formatSessionSpan, isSameStudioDay, studioDayBounds } from "../studioDay";

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
