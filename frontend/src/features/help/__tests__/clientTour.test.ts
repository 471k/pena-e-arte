import { describe, it, expect } from "vitest";
import { getClientTourSteps } from "../tours/clientTour";

const PAYMENT_METHODS_STEP = '[data-tour="client-payment-methods-nav"]';

describe("getClientTourSteps", () => {
  it("leaves out the Payment Methods step while saved cards are switched off (C-9)", () => {
    const selectors = getClientTourSteps(false, false).map((s) => s.targetSelector);
    expect(selectors).not.toContain(PAYMENT_METHODS_STEP);
    expect(selectors.at(-1)).toBe('[data-tour="client-help-button"]');
  });

  it("includes the Payment Methods step, just before Help, while saved cards are on", () => {
    const selectors = getClientTourSteps(false, true).map((s) => s.targetSelector);
    expect(selectors.slice(-2)).toEqual([PAYMENT_METHODS_STEP, '[data-tour="client-help-button"]']);
  });

  it("puts the appointment-page step right after the booking step, on My bookings (After Hours slice 2)", () => {
    const steps = getClientTourSteps(false, false);
    expect(steps[0].targetSelector).toBe('[data-tour="client-book-nav"]');
    expect(steps[1].targetSelector).toBe('[data-tour="client-appointment-details-link"]');
    expect(steps[1].route).toBe("/book");
  });

  it("still adds the studios step independently of saved cards", () => {
    const selectors = getClientTourSteps(true, false).map((s) => s.targetSelector);
    expect(selectors).toContain('[data-tour="client-my-studios-nav"]');
    expect(selectors).not.toContain(PAYMENT_METHODS_STEP);
  });
});
