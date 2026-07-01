import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { StarRating, InteractiveStarRating } from "@/shared/components/ui/StarRating";

describe("StarRating (display)", () => {
  it("renders correct number of stars", () => {
    render(<StarRating value={3} />);
    expect(screen.getByRole("img", { name: /rating: 3 out of 5/i })).toBeInTheDocument();
  });

  it("aria-label matches value", () => {
    render(<StarRating value={4} max={5} />);
    expect(screen.getByRole("img")).toHaveAttribute("aria-label", "Rating: 4 out of 5 stars");
  });
});

describe("InteractiveStarRating", () => {
  it("renders a radiogroup with 5 buttons", () => {
    render(<InteractiveStarRating value={0} onChange={() => {}} />);
    const group = screen.getByRole("radiogroup");
    expect(group).toBeInTheDocument();
    expect(screen.getAllByRole("radio")).toHaveLength(5);
  });

  it("each button has min 44px touch target", () => {
    render(<InteractiveStarRating value={0} onChange={() => {}} />);
    const buttons = screen.getAllByRole("radio");
    buttons.forEach((btn) => {
      expect(btn.className).toMatch(/min-w-\[44px\]/);
      expect(btn.className).toMatch(/min-h-\[44px\]/);
    });
  });

  it("calls onChange with the correct rating on click", () => {
    const onChange = vi.fn();
    render(<InteractiveStarRating value={0} onChange={onChange} />);
    fireEvent.click(screen.getByRole("radio", { name: /rate 3 of 5/i }));
    expect(onChange).toHaveBeenCalledWith(3);
  });

  it("aria-checked is true on selected star", () => {
    render(<InteractiveStarRating value={3} onChange={() => {}} />);
    expect(screen.getByRole("radio", { name: /rate 3 of 5/i }))
      .toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("radio", { name: /rate 4 of 5/i }))
      .toHaveAttribute("aria-checked", "false");
  });

  it("live text readout appears after selection", () => {
    const { rerender } = render(<InteractiveStarRating value={0} onChange={() => {}} />);
    rerender(<InteractiveStarRating value={4} onChange={() => {}} />);
    const liveRegion = document.querySelector("[aria-live='polite']");
    expect(liveRegion?.textContent).toMatch(/4 stars.*good/i);
  });

  it("shows hover preview — aria-checked doesn't change on hover (visual only)", () => {
    render(<InteractiveStarRating value={2} onChange={() => {}} />);
    fireEvent.mouseEnter(screen.getByRole("radio", { name: /rate 5 of 5/i }));
    expect(screen.getByRole("radio", { name: /rate 2 of 5/i }))
      .toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("radio", { name: /rate 5 of 5/i }))
      .toHaveAttribute("aria-checked", "false");
  });
});
