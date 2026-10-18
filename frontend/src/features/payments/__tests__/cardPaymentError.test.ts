import { describe, it, expect } from "vitest";
import { describeCardPaymentError } from "@/features/payments/cardPaymentError";

describe("describeCardPaymentError", () => {
  it("maps POK's failed / rejected 3-D Secure text to a bank-verification message", () => {
    expect(describeCardPaymentError("3DS processing failed. Please try again"))
      .toBe("Your bank couldn't verify this card. Try again, or use a different card.");
  });

  it("maps POK's refused-card text to a check-the-details message", () => {
    expect(describeCardPaymentError("Your card was not accepted. Please change the details and try again!"))
      .toBe("This card wasn't accepted. Check the card details, or use a different card.");
  });

  it("falls back to a generic next step for anything else, including no message", () => {
    const generic = "The card payment didn't go through. Try again, or use a different card.";
    expect(describeCardPaymentError("Something went wrong!")).toBe(generic);
    expect(describeCardPaymentError(undefined)).toBe(generic);
    expect(describeCardPaymentError(null)).toBe(generic);
  });
});
