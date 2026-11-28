import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { Button } from "@/shared/components/ui/button";

// wrapLabel (28 Nov 2026): a label that must never push past its container wraps instead (200% text on a 320px
// phone ran "Book an appointment" 27px off-screen). Whether it really wraps is real layout and is checked in
// e2e/reflow-200-text.spec.ts; here: the classes, that each size keeps its height as a minimum, and that the
// prop never reaches the DOM.
describe("Button wrapLabel", () => {
  it("lets the label wrap and caps the button at its container's width", () => {
    render(<Button wrapLabel>Book an appointment</Button>);
    const button = screen.getByRole("button", { name: "Book an appointment" });
    expect(button).toHaveClass("whitespace-normal", "max-w-full", "h-auto", "text-center", "min-h-10");
    expect(button).not.toHaveClass("whitespace-nowrap", "h-10");
    expect(button).not.toHaveAttribute("wraplabel");
  });

  it.each([
    ["sm", "h-9", "min-h-9"],
    ["lg", "h-11", "min-h-11"],
  ] as const)("size %s keeps its own height as the minimum", (size, fixed, min) => {
    render(<Button size={size} wrapLabel>Go</Button>);
    const button = screen.getByRole("button", { name: "Go" });
    expect(button).toHaveClass(min, "h-auto");
    expect(button).not.toHaveClass(fixed);
  });

  it("without wrapLabel a button is unchanged: one line at its fixed height", () => {
    render(<Button size="sm">Go</Button>);
    const button = screen.getByRole("button", { name: "Go" });
    expect(button).toHaveClass("whitespace-nowrap", "h-9");
    expect(button).not.toHaveClass("max-w-full", "h-auto", "min-h-9");
  });

  it("works through asChild, as the empty states' links use it", () => {
    render(<Button asChild size="sm" wrapLabel><a href="/book">Book an appointment</a></Button>);
    const link = screen.getByRole("link", { name: "Book an appointment" });
    expect(link).toHaveClass("whitespace-normal", "max-w-full", "min-h-9");
    expect(link).not.toHaveAttribute("wraplabel");
  });
});
