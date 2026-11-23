import { describe, it, expect, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";

import { OptionalCodes } from "@/features/appointments/components/BookingSections";

// The disclosure's only logic is where it starts. Nothing prefills a code today, so on both booking forms
// `defaultOpen` is false at mount; these pin the component for the day something does (a restored draft in
// slice 7 phase D, or a link carrying a code), when a code hidden behind a closed panel would be the bug.
describe("OptionalCodes", () => {
  afterEach(cleanup);

  function details(): HTMLElement {
    return screen.getByText("Promo or referral code (optional)").closest("details")!;
  }

  it("starts open when a code is already filled in", () => {
    render(<OptionalCodes defaultOpen><input aria-label="Promo code" defaultValue="SPRING" /></OptionalCodes>);
    expect(details()).toHaveAttribute("open");
  });

  it("starts closed when no code is filled in", () => {
    render(<OptionalCodes defaultOpen={false}><input aria-label="Promo code" /></OptionalCodes>);
    expect(details()).not.toHaveAttribute("open");
  });

  it("does not close itself when the prop changes after mount", () => {
    // The forms recompute defaultOpen on every render; clearing a typed code must not snap the panel shut
    // under the person's cursor. Only the mount value is read.
    const { rerender } = render(
      <OptionalCodes defaultOpen><input aria-label="Promo code" /></OptionalCodes>,
    );
    rerender(<OptionalCodes defaultOpen={false}><input aria-label="Promo code" /></OptionalCodes>);
    expect(details()).toHaveAttribute("open");
  });
});
