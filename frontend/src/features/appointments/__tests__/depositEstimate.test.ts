import { describe, it, expect } from "vitest";
import { estimateDeposit, type DepositEstimateInput } from "@/features/appointments/depositEstimate";

// The preview mirrors CreateAppointmentCommand's order (package, then the service's own deposit, then the
// studio's rule through DepositCalculator). These cases follow DepositCalculatorTests so the two can't drift
// apart unnoticed.
const base: DepositEstimateInput = {
  packageCovered:  false,
  serviceDeposit:  null,
  rule:            null,
  artistChosen:    true,
  studioAssigns:   false,
  hourlyRate:      100,
  durationMinutes: 60,
};

describe("estimateDeposit", () => {
  it("a package covers the booking before anything else is looked at", () => {
    expect(estimateDeposit({ ...base, packageCovered: true, serviceDeposit: 40, rule: { amountFixed: 50, amountPercent: null } }))
      .toEqual({ kind: "package" });
  });

  it("a service's own deposit replaces the studio's rule", () => {
    expect(estimateDeposit({ ...base, serviceDeposit: 40, rule: { amountFixed: 50, amountPercent: null } }))
      .toEqual({ kind: "amount", amount: 40, basis: "service" });
  });

  it("no active rule means no deposit", () => {
    expect(estimateDeposit(base)).toEqual({ kind: "none" });
  });

  it("a fixed rule is its amount, whatever the artist's rate", () => {
    expect(estimateDeposit({ ...base, hourlyRate: null, rule: { amountFixed: 50, amountPercent: null } }))
      .toEqual({ kind: "amount", amount: 50, basis: "rule" });
  });

  it.each([
    [20, 100, 60, 20],
    [20, 100, 90, 30],
    [30, 80, 60, 24],
  ])("a %i%% rule at %i an hour for %i minutes is %i", (percent, rate, minutes, expected) => {
    expect(estimateDeposit({ ...base, hourlyRate: rate, durationMinutes: minutes, rule: { amountFixed: null, amountPercent: percent } }))
      .toEqual({ kind: "amount", amount: expected, basis: "rule" });
  });

  it("a percentage rule with no artist yet waits for one, and says who picks", () => {
    const rule = { amountFixed: null, amountPercent: 20 };
    expect(estimateDeposit({ ...base, rule, artistChosen: false, studioAssigns: true }))
      .toEqual({ kind: "awaiting-artist", studioAssigns: true });
    expect(estimateDeposit({ ...base, rule, artistChosen: false, studioAssigns: false }))
      .toEqual({ kind: "awaiting-artist", studioAssigns: false });
  });

  it("a percentage of an artist with no rate is no deposit, as DepositCalculator returns 0 for it", () => {
    const rule = { amountFixed: null, amountPercent: 20 };
    expect(estimateDeposit({ ...base, rule, hourlyRate: null })).toEqual({ kind: "none" });
    expect(estimateDeposit({ ...base, rule, hourlyRate: 0 })).toEqual({ kind: "none" });
  });

  it("a zero amount is no deposit, not a deposit of zero", () => {
    expect(estimateDeposit({ ...base, serviceDeposit: 0 })).toEqual({ kind: "none" });
    expect(estimateDeposit({ ...base, rule: { amountFixed: 0, amountPercent: null } })).toEqual({ kind: "none" });
  });
});
