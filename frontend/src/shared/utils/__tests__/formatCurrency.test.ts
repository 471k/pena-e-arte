import { describe, it, expect } from "vitest";
import { formatCurrency, currencyLabel, currencyMinorUnits } from "@/shared/utils/formatCurrency";

describe("formatCurrency", () => {
  it("formats a whole EUR amount with no decimals", () => {
    expect(formatCurrency(29, "EUR")).toBe("€29");
  });

  it("formats a fractional amount with exactly two decimals, never one", () => {
    expect(formatCurrency(98.6, "EUR")).toBe("€98.60");
  });

  it("formats zero as a whole amount", () => {
    expect(formatCurrency(0, "EUR")).toBe("€0");
  });

  it("respects whatever currency code it is given, not a hardcoded one", () => {
    expect(formatCurrency(29, "USD")).toBe("$29");
    expect(formatCurrency(29, "GBP")).toBe("£29");
  });

  it("rounds to at most two decimal places", () => {
    expect(formatCurrency(40.8333, "EUR")).toBe("€40.83");
  });

  // ── Studio currency (2026-09-27) — non-EUR/USD/GBP minor units ──────────────
  // A three-letter ISO code prefix (no native symbol, e.g. ALL/KWD) is separated from the
  // number by a non-breaking space (U+00A0) per CLDR — a plain space would never match.
  it("formats a whole ALL amount with the ISO code prefix", () => {
    expect(formatCurrency(5000, "ALL")).toBe("ALL 5,000");
  });

  it("formats a JPY amount with zero decimals (JPY has no minor unit)", () => {
    expect(formatCurrency(3000, "JPY")).toBe("¥3,000");
  });

  it("formats a KWD amount with its full three-decimal minor unit", () => {
    expect(formatCurrency(12.345, "KWD")).toBe("KWD 12.345");
  });

  it("currencyLabel returns the currency's symbol or code for an input adornment", () => {
    expect(currencyLabel("EUR")).toBe("€");
    expect(currencyLabel("ALL")).toBe("ALL");
    expect(currencyLabel("JPY")).toBe("¥");
  });

  it("currencyMinorUnits returns the browser's resolved decimal-place count", () => {
    expect(currencyMinorUnits("EUR")).toBe(2);
    expect(currencyMinorUnits("JPY")).toBe(0);
    expect(currencyMinorUnits("KWD")).toBe(3);
  });
});
