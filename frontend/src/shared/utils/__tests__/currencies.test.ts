import { describe, it, expect } from "vitest";
import { buildCurrencyOptions, STUDIO_CURRENCIES } from "@/shared/utils/currencies";

describe("currencies", () => {
  it("offers only ALL then EUR", () => {
    expect([...STUDIO_CURRENCIES]).toEqual(["ALL", "EUR"]);
    expect(buildCurrencyOptions().map((o) => o.code)).toEqual(["ALL", "EUR"]);
  });

  it("names each option for the picker", () => {
    expect(buildCurrencyOptions().map((o) => o.name)).toEqual(["Albanian Lek", "Euro"]);
  });

  it("does not repeat an offered current value", () => {
    expect(buildCurrencyOptions("EUR").map((o) => o.code)).toEqual(["ALL", "EUR"]);
    expect(buildCurrencyOptions("ALL").map((o) => o.code)).toEqual(["ALL", "EUR"]);
  });

  it("lists an older studio's other currency last so its real value still shows", () => {
    expect(buildCurrencyOptions("USD").map((o) => o.code)).toEqual(["ALL", "EUR", "USD"]);
  });
});
