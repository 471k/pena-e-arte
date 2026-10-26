import { describe, it, expect } from "vitest";
import {
  formatCurrency, currencyLabel, currencyMinorUnits, formatWithReference, lekReferenceApplies, toWholeLek,
} from "@/shared/utils/formatCurrency";

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

describe("lek next to euro (Albanian EUR studios)", () => {
  // Intl puts a no-break space between "ALL" and the number; compare as plain text.
  const plain = (s: string) => s.replaceAll(String.fromCharCode(160), " ");

  it("toWholeLek converts then rounds once, half away from zero", () => {
    expect(toWholeLek(50, 90.74)).toBe(4537);
    expect(toWholeLek(0.5, 90.74)).toBe(45);   // 45.37
    expect(toWholeLek(1, 90.5)).toBe(91);      // a .5 midpoint rounds up
    expect(toWholeLek(-1, 90.5)).toBe(-91);    // and away from zero for negatives
  });

  it("lekReferenceApplies only to studios in Albania that price in euros", () => {
    expect(lekReferenceApplies("AL", "EUR")).toBe(true);
    expect(lekReferenceApplies("al", "eur")).toBe(true);
    expect(lekReferenceApplies("AL", "ALL")).toBe(false);
    expect(lekReferenceApplies("XK", "EUR")).toBe(false);
    expect(lekReferenceApplies(undefined, "EUR")).toBe(false);
  });

  it("formatWithReference marks a pre-payment lek amount approximate", () => {
    expect(plain(formatWithReference(50, "EUR", { amount: 4537, currency: "ALL", approximate: true })))
      .toBe("€50 (≈ ALL 4,537)");
  });

  it("formatWithReference shows a frozen lek amount without the ≈", () => {
    expect(plain(formatWithReference(50, "EUR", { amount: 4537, currency: "ALL", approximate: false })))
      .toBe("€50 (ALL 4,537)");
  });

  it("formatWithReference names the day of a stale rate", () => {
    expect(plain(formatWithReference(50, "EUR",
      { amount: 4537, currency: "ALL", approximate: true, staleRateDate: "2026-10-23" })))
      .toBe("€50 (≈ ALL 4,537 at the 23 Oct rate)");
  });

  it("formatWithReference with no reference is exactly formatCurrency", () => {
    expect(formatWithReference(98.6, "EUR", null)).toBe(formatCurrency(98.6, "EUR"));
    expect(formatWithReference(5000, "ALL", null)).toBe(formatCurrency(5000, "ALL"));
  });
});
