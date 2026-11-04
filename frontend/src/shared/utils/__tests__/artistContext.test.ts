import { describe, it, expect } from "vitest";
import { isInArtistContext } from "../artistContext";

describe("isInArtistContext", () => {
  it("treats /today as the owner's own artist work (it never shows the studio's)", () => {
    expect(isInArtistContext("/today", "", "a1")).toBe(true);
  });

  it("is never artist context without an artist profile", () => {
    expect(isInArtistContext("/today", "", undefined)).toBe(false);
  });

  it("keeps the existing rules", () => {
    expect(isInArtistContext("/earnings", "", "a1")).toBe(true);
    expect(isInArtistContext("/artists/a1", "", "a1")).toBe(true);
    expect(isInArtistContext("/schedule", "?artistId=a1", "a1")).toBe(true);
    expect(isInArtistContext("/schedule", "", "a1")).toBe(false);
  });
});
