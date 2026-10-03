import { describe, expect, it } from "vitest";
import {
  buildTimezoneOptions,
  defaultTimezoneId,
  formatOffset,
  isTimezoneSupported,
  listTimezoneIds,
  registrationTimezoneId,
  suggestedTimezoneIds,
} from "../timezones";

const WINTER = new Date("2026-01-15T12:00:00Z");
const SUMMER = new Date("2026-07-15T12:00:00Z");

describe("listTimezoneIds", () => {
  it("drops the confusing Etc/GMT±N aliases and always offers UTC", () => {
    const ids = listTimezoneIds();
    expect(ids.some((id) => id.startsWith("Etc/"))).toBe(false);
    expect(ids).toContain("UTC");
    expect(ids).toContain("Europe/Tirane");
  });
});

describe("formatOffset", () => {
  it.each([
    [0, "UTC+00:00"],
    [60, "UTC+01:00"],
    [-180, "UTC-03:00"],
    [330, "UTC+05:30"],
    [-570, "UTC-09:30"],
  ])("%i minutes -> %s", (minutes, expected) => {
    expect(formatOffset(minutes)).toBe(expected);
  });
});

describe("buildTimezoneOptions", () => {
  it("labels a zone with offset, city and the generic zone name", () => {
    const [tirane] = buildTimezoneOptions(["Europe/Tirane"], WINTER);
    expect(tirane.id).toBe("Europe/Tirane");
    expect(tirane.label).toMatch(/^\(UTC\+01:00\) Tirane — .*(European|Central)/);
  });

  it("follows daylight saving: the same zone is +02:00 in summer", () => {
    const [winter] = buildTimezoneOptions(["Europe/Tirane"], WINTER);
    const [summer] = buildTimezoneOptions(["Europe/Tirane"], SUMMER);
    expect(winter.offsetMinutes).toBe(60);
    expect(summer.offsetMinutes).toBe(120);
  });

  it("turns underscores into spaces and keeps only the last path segment as the city", () => {
    const [bue] = buildTimezoneOptions(["America/Argentina/Buenos_Aires"], WINTER);
    expect(bue.city).toBe("Buenos Aires");
    expect(bue.label).toContain("Buenos Aires");
  });

  it("shows UTC once, without a repeated name", () => {
    const [utc] = buildTimezoneOptions(["UTC"], WINTER);
    expect(utc.label.startsWith("(UTC+00:00) UTC")).toBe(true);
  });

  it("sorts by offset, then city", () => {
    const options = buildTimezoneOptions(
      ["Asia/Tokyo", "America/New_York", "Europe/Tirane", "Europe/Berlin", "UTC"],
      WINTER,
    );
    expect(options.map((o) => o.id)).toEqual([
      "America/New_York", "UTC", "Europe/Berlin", "Europe/Tirane", "Asia/Tokyo",
    ]);
  });

  it("builds an entry for every id without throwing on an unknown one", () => {
    const options = buildTimezoneOptions(["Europe/Tirane", "Not/AZone"], WINTER);
    expect(options).toHaveLength(2);
  });
});

describe("isTimezoneSupported", () => {
  it("accepts alias zones that the browser's canonical list omits", () => {
    // Regression: Intl.supportedValuesOf("timeZone") has no Europe/Tirane, but it is a valid zone.
    expect(isTimezoneSupported("Europe/Tirane")).toBe(true);
  });

  it("rejects nonsense", () => {
    expect(isTimezoneSupported("Mars/Olympus")).toBe(false);
    expect(isTimezoneSupported("")).toBe(false);
  });
});

describe("suggestedTimezoneIds", () => {
  it("puts the visitor's own IP zone first when the IP country matches the selected country", () => {
    const result = suggestedTimezoneIds("US", "US", "America/Chicago");
    expect(result[0]).toBe("America/Chicago");
  });

  it("keeps an alias zone such as Europe/Tirane that the canonical list lacks", () => {
    expect(suggestedTimezoneIds("AL", "AL", "Europe/Tirane")).toContain("Europe/Tirane");
  });

  it("ignores the IP zone when the selected country is somewhere else", () => {
    const result = suggestedTimezoneIds("AL", "US", "America/Chicago");
    expect(result).not.toContain("America/Chicago");
  });

  it("drops a zone the browser cannot format", () => {
    expect(suggestedTimezoneIds("AL", "AL", "Mars/Olympus")).not.toContain("Mars/Olympus");
  });

  it("returns nothing for a missing or malformed country", () => {
    expect(suggestedTimezoneIds(undefined)).toEqual([]);
    expect(suggestedTimezoneIds("ALB")).toEqual([]);
  });
});

describe("defaultTimezoneId", () => {
  it("prefers the first suggestion", () => {
    expect(defaultTimezoneId(["Europe/Tirane"], "Europe/Paris")).toBe("Europe/Tirane");
  });

  it("falls back to the browser's zone when it is valid", () => {
    expect(defaultTimezoneId([], "Europe/Paris")).toBe("Europe/Paris");
  });

  it("returns undefined rather than guessing when nothing is known", () => {
    expect(defaultTimezoneId([], "Mars/Olympus")).toBeUndefined();
    expect(defaultTimezoneId([], "")).toBeUndefined();
  });
});

describe("registrationTimezoneId", () => {
  it("uses the visitor's own zone when their IP is in the selected country", () => {
    expect(registrationTimezoneId("PT", "PT", "Europe/Lisbon")).toBe("Europe/Lisbon");
  });

  it("does not use the IP zone for a different selected country", () => {
    expect(registrationTimezoneId("PT", "US", "America/Chicago")).not.toBe("America/Chicago");
  });

  it("returns undefined for an unknown country so the server keeps its default, with no browser-zone guess", () => {
    expect(registrationTimezoneId("ZZ", null, null)).toBeUndefined();
    expect(registrationTimezoneId(undefined, "PT", "Europe/Lisbon")).toBeUndefined();
  });
});
