import { describe, it, expect } from "vitest";
import { buildCurrencyOptions, EXCLUDED_CURRENCY_CODES, ALL_CURRENCY_CODES, MAJOR_CURRENCIES } from "@/shared/utils/currencies";

describe("currencies", () => {
  it("excludes non-tradable fund/metal codes from the full list", () => {
    for (const code of EXCLUDED_CURRENCY_CODES) {
      expect(ALL_CURRENCY_CODES).not.toContain(code);
    }
  });

  it("buildCurrencyOptions puts the country default first", () => {
    const options = buildCurrencyOptions("ALL");
    expect(options[0].code).toBe("ALL");
  });

  it("buildCurrencyOptions puts the majors right after the country default, with no duplicates", () => {
    const options = buildCurrencyOptions("PLN");
    const codes = options.map((o) => o.code);
    expect(codes[0]).toBe("PLN");
    expect(codes.slice(1, 1 + MAJOR_CURRENCIES.length)).toEqual([...MAJOR_CURRENCIES]);
    // No code appears twice.
    expect(new Set(codes).size).toBe(codes.length);
  });

  it("buildCurrencyOptions does not duplicate a country default that is itself a major currency", () => {
    const options = buildCurrencyOptions("EUR");
    const codes = options.map((o) => o.code);
    expect(codes[0]).toBe("EUR");
    expect(codes.filter((c) => c === "EUR")).toHaveLength(1);
  });

  it("buildCurrencyOptions with no country default still lists the majors first", () => {
    const options = buildCurrencyOptions(null);
    const codes = options.map((o) => o.code);
    expect(codes.slice(0, MAJOR_CURRENCIES.length)).toEqual([...MAJOR_CURRENCIES]);
  });

  it("the remainder after the ordered head is sorted alphabetically by display name", () => {
    const options = buildCurrencyOptions(null);
    const rest = options.slice(MAJOR_CURRENCIES.length);
    const names = rest.map((o) => o.name);
    expect(names).toEqual([...names].sort((a, b) => a.localeCompare(b)));
  });
});
