import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

import { SubscriptionGatedButton } from "@/shared/components/SubscriptionGatedButton";

// ── Mock useSubscriptionGuard ─────────────────────────────────────────────────

const mockGuard = vi.fn();

vi.mock("@/features/billing/useSubscriptionGuard", () => ({
  useSubscriptionGuard: () => mockGuard(),
}));

// ── Helpers ───────────────────────────────────────────────────────────────────

function renderButton(disabled = false) {
  render(
    <MemoryRouter>
      <SubscriptionGatedButton disabled={disabled}>Click me</SubscriptionGatedButton>
    </MemoryRouter>,
  );
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("SubscriptionGatedButton", () => {
  beforeEach(() => {
    mockGuard.mockReturnValue({ isReadOnly: false, cause: null });
  });

  it("renders children when not gated", () => {
    renderButton();
    expect(screen.getByRole("button", { name: "Click me" })).toBeInTheDocument();
  });

  it("renders a tooltip when gated (isReadOnly=true)", () => {
    mockGuard.mockReturnValue({ isReadOnly: true, cause: "subscription" });
    renderButton();
    // tooltip span is in the DOM (opacity toggled by CSS)
    expect(screen.getByRole("tooltip")).toBeInTheDocument();
  });

  it("button refuses when gated, but stays focusable so it can explain itself (slice 6a)", () => {
    mockGuard.mockReturnValue({ isReadOnly: true, cause: "subscription" });
    renderButton();
    const button = screen.getByRole("button", { name: "Click me" });
    expect(button).not.toBeDisabled();
    expect(button).toHaveAttribute("aria-disabled", "true");
    expect(button).toHaveAccessibleDescription(/subscription is inactive/i);
  });

  it("button is not disabled when not gated and no disabled prop", () => {
    renderButton(false);
    expect(screen.getByRole("button", { name: "Click me" })).not.toBeDisabled();
  });

  it("tooltip text appears in the DOM when gated", () => {
    mockGuard.mockReturnValue({ isReadOnly: true, cause: "subscription" });
    renderButton();
    const tooltip = screen.getByRole("tooltip");
    expect(tooltip.textContent).toMatch(/subscription is inactive/i);
  });

  it("tooltip shows suspended message when cause is suspended", () => {
    mockGuard.mockReturnValue({ isReadOnly: true, cause: "suspended" });
    renderButton();
    const tooltip = screen.getByRole("tooltip");
    expect(tooltip.textContent).toMatch(/suspended/i);
  });

  it("in a support session it refuses with the read-only support reason (D05 phase 4b)", () => {
    mockGuard.mockReturnValue({ isReadOnly: true, cause: "support" });
    renderButton();
    const button = screen.getByRole("button", { name: "Click me" });
    expect(button).toHaveAttribute("aria-disabled", "true");
    expect(button).toHaveAccessibleDescription(
      "This is a read-only support session: changes are turned off. End the session to make changes as yourself.");
  });
});
