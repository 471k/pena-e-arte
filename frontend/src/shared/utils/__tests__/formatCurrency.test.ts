import { describe, it, expect } from "vitest";
import { formatCurrency } from "@/shared/utils/formatCurrency";

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
});
