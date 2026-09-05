import { describe, it, expect } from "vitest";
import { PHONE_COUNTRIES, flagEmoji } from "../phoneCountries";

describe("PHONE_COUNTRIES", () => {
  it("contains an entry for PT with callingCode 351", () => {
    const pt = PHONE_COUNTRIES.find((c) => c.code === "PT");
    expect(pt).toBeDefined();
    expect(pt?.callingCode).toBe("351");
  });

  it("is sorted by name", () => {
    const sorted = [...PHONE_COUNTRIES].sort((a, b) => a.name.localeCompare(b.name));
    expect(PHONE_COUNTRIES).toEqual(sorted);
  });

  it("has no duplicate code values", () => {
    const codes = PHONE_COUNTRIES.map((c) => c.code);
    expect(new Set(codes).size).toBe(codes.length);
  });
});

describe("flagEmoji", () => {
  it("converts an uppercase ISO code to its flag emoji", () => {
    expect(flagEmoji("PT")).toBe("🇵🇹");
  });

  it("converts a lowercase ISO code to its flag emoji", () => {
    expect(flagEmoji("us")).toBe("🇺🇸");
  });
});
