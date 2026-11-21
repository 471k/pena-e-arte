import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Button } from "@/shared/components/ui/button";

// The 8-state contract's button half (slice 6a). Visibility of the reason on focus is CSS and is proved in the
// real browser (e2e/shared-state-contract.spec.ts); here: semantics and behaviour.
describe("Button states", () => {
  it("disabledReason keeps the button focusable, describes it with the reason, and refuses the click", async () => {
    const onClick = vi.fn();
    render(<Button disabledReason="Add a service first." onClick={onClick}>Book</Button>);
    const button = screen.getByRole("button", { name: "Book" });

    expect(button).not.toBeDisabled();
    expect(button).toHaveAttribute("aria-disabled", "true");
    expect(button).toHaveAccessibleDescription("Add a service first.");

    await userEvent.tab();
    expect(button).toHaveFocus();
    await userEvent.click(button);
    await userEvent.keyboard("{Enter}");
    expect(onClick).not.toHaveBeenCalled();
  });

  it("a reason wins over native disabled, so a gated control on a clean form can still be focused", async () => {
    render(<Button disabled disabledReason="Subscription inactive.">Save</Button>);
    const button = screen.getByRole("button", { name: "Save" });
    expect(button).not.toBeDisabled();
    expect(button).toHaveAttribute("aria-disabled", "true");
    await userEvent.tab();
    expect(button).toHaveFocus();
  });

  it("loading wins over native disabled too, so focus isn't dropped mid-action", () => {
    render(<Button disabled loading>Save</Button>);
    expect(screen.getByRole("button", { name: "Save" })).not.toBeDisabled();
  });

  it("disabledReason on a submit button stops the form from submitting", async () => {
    const onSubmit = vi.fn((e: React.FormEvent) => e.preventDefault());
    render(
      <form onSubmit={onSubmit}>
        <input aria-label="Name" />
        <Button type="submit" disabledReason="Not yet.">Save</Button>
      </form>,
    );
    await userEvent.click(screen.getByRole("button", { name: "Save" }));
    // Implicit submission (Enter in a field) goes through the submit button's click too.
    await userEvent.type(screen.getByLabelText("Name"), "x{Enter}");
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("keeps a caller's own aria-describedby alongside the reason", () => {
    render(
      <>
        <p id="hint">Hint</p>
        <Button aria-describedby="hint" disabledReason="Reason.">Go</Button>
      </>,
    );
    expect(screen.getByRole("button", { name: "Go" })).toHaveAccessibleDescription("Hint Reason.");
  });

  it("loading says it is busy, keeps focus and ignores clicks (no double submit)", async () => {
    const onClick = vi.fn();
    const { rerender } = render(<Button onClick={onClick}>Save</Button>);
    const button = screen.getByRole("button", { name: "Save" });
    button.focus();

    rerender(<Button onClick={onClick} loading>Save</Button>);
    expect(button).toHaveFocus();
    expect(button).toHaveAttribute("aria-busy", "true");
    expect(button).toHaveAttribute("aria-disabled", "true");
    await userEvent.click(button);
    expect(onClick).not.toHaveBeenCalled();

    rerender(<Button onClick={onClick}>Save</Button>);
    expect(button).not.toHaveAttribute("aria-busy");
    await userEvent.click(button);
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it("keeps a caller's own aria-busy and aria-disabled when it sets neither prop", () => {
    render(<Button aria-busy aria-disabled="true">Own</Button>);
    const button = screen.getByRole("button", { name: "Own" });
    expect(button).toHaveAttribute("aria-busy", "true");
    expect(button).toHaveAttribute("aria-disabled", "true");
  });

  it("without either prop it behaves exactly as before: no aria-disabled, no wrapper", () => {
    const { container } = render(<Button>Plain</Button>);
    const button = screen.getByRole("button", { name: "Plain" });
    expect(button).not.toHaveAttribute("aria-disabled");
    expect(button).not.toHaveAttribute("aria-describedby");
    expect(container.firstElementChild).toBe(button);
  });

  it("a full-width button keeps its width through the reason's wrapper", () => {
    render(<Button className="w-full" disabledReason="Why.">Wide</Button>);
    expect(screen.getByRole("button", { name: "Wide" }).parentElement).toHaveClass("w-full");
  });
});
