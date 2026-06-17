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

  it("button is disabled when gated", () => {
    mockGuard.mockReturnValue({ isReadOnly: true, cause: "subscription" });
    renderButton();
    expect(screen.getByRole("button", { name: "Click me" })).toBeDisabled();
  });

  it("button is not disabled when not gated and no disabled prop", () => {
    renderButton(false);
    expect(screen.getByRole("button", { name: "Click me" })).not.toBeDisabled();
  });

  it("tooltip text appears in the DOM when gated", () => {
    mockGuard.mockReturnValue({ isReadOnly: true, cause: "subscription" });
    renderButton();
    const tooltip = screen.getByRole("tooltip");
    expect(tooltip.textContent).toMatch(/subscription inactive/i);
  });

  it("tooltip shows suspended message when cause is suspended", () => {
    mockGuard.mockReturnValue({ isReadOnly: true, cause: "suspended" });
    renderButton();
    const tooltip = screen.getByRole("tooltip");
    expect(tooltip.textContent).toMatch(/suspended/i);
  });
});
