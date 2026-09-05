import { describe, it, expect } from "vitest";
import { buildGoogleMapsDirectionsUrl, hasPinnedLocation } from "../googleMaps";

describe("buildGoogleMapsDirectionsUrl", () => {
  it("produces a valid Google Maps directions URL for a pinned lat/lng", () => {
    const expected = `https://www.google.com/maps/dir/?${new URLSearchParams({
      api: "1",
      destination: "41.1579,-8.6291",
    }).toString()}`;

    expect(buildGoogleMapsDirectionsUrl(41.1579, -8.6291)).toBe(expected);
  });

  it("URL-encodes the destination comma", () => {
    expect(buildGoogleMapsDirectionsUrl(41.1579, -8.6291)).toContain("destination=41.1579%2C-8.6291");
  });
});

describe("hasPinnedLocation", () => {
  it("returns false for the exact origin (unset sentinel)", () => {
    expect(hasPinnedLocation(0, 0)).toBe(false);
  });

  it("returns true for a real pinned location", () => {
    expect(hasPinnedLocation(41.1579, -8.6291)).toBe(true);
  });

  it("returns false when latitude is NaN", () => {
    expect(hasPinnedLocation(NaN, -8.6291)).toBe(false);
  });

  it("returns true when only longitude is 0 (equator/prime-meridian studios are legitimate)", () => {
    expect(hasPinnedLocation(0, -8.6291)).toBe(true);
  });

  it("returns true when only latitude is 0", () => {
    expect(hasPinnedLocation(41.1579, 0)).toBe(true);
  });
});
