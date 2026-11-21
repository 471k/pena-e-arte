import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { SlotAvailabilityIndicator } from "@/features/appointments/components/SlotAvailabilityIndicator";

// S06. What a screen reader actually says is proved in the real browser; here: which region holds which words.
describe("SlotAvailabilityIndicator announcements", () => {
  it("is a polite live region even before any check, so the region exists before its text changes", () => {
    render(<SlotAvailabilityIndicator checking={false} status={undefined} />);
    const region = screen.getByRole("status");
    expect(region).toHaveAttribute("aria-live", "polite");
    expect(region).toBeEmptyDOMElement();
  });

  it("does not announce 'Checking…' and empties the region while a check runs", () => {
    const { rerender } = render(<SlotAvailabilityIndicator checking={false} status={{ available: true, reason: null }} />);
    rerender(<SlotAvailabilityIndicator checking status={{ available: true, reason: null }} />);
    expect(screen.getByText(/checking availability/i)).toBeInTheDocument();
    expect(screen.getByRole("status")).toBeEmptyDOMElement();
  });

  it("announces 'available' politely", () => {
    render(<SlotAvailabilityIndicator checking={false} status={{ available: true, reason: null }} />);
    expect(screen.getByRole("status")).toHaveTextContent("This slot is available");
  });

  it("announces 'not available' politely, not as an alert", () => {
    render(<SlotAvailabilityIndicator checking={false} status={{ available: false, reason: "The artist is booked then." }} />);
    expect(screen.getByRole("status")).toHaveTextContent("The artist is booked then.");
    expect(screen.queryByRole("alert")).toBeNull();
  });
});
