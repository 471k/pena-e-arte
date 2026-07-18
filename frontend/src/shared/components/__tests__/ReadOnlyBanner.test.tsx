import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";

import uiReducer from "@/features/ui/uiSlice";
import authReducer from "@/features/auth/authSlice";
import { ReadOnlyBanner } from "@/shared/components/ReadOnlyBanner";

function makeStore(readOnlyError: string | null = null) {
  return configureStore({
    reducer: { auth: authReducer, ui: uiReducer },
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: null, token: null, tenantId: null, role: null } as any,
      ui:   { readOnlyError, sessionExpired: false, studioSuspended: false, planLimitError: null },
    },
  });
}

function renderBanner(message: string | null = "Studio is in grace period — read-only mode.") {
  render(
    <Provider store={makeStore(message)}>
      <MemoryRouter>
        <ReadOnlyBanner />
      </MemoryRouter>
    </Provider>,
  );
}

describe("ReadOnlyBanner", () => {
  it("renders without crashing when message is set", () => {
    renderBanner();
    expect(screen.getByText(/grace period/i)).toBeInTheDocument();
  });

  it("does not render when message is null", () => {
    renderBanner(null);
    expect(screen.queryByRole("button", { name: /dismiss/i })).not.toBeInTheDocument();
  });

  it("contains text about read-only / grace period", () => {
    renderBanner("Studio is in grace period — read-only mode.");
    expect(screen.getByText(/grace period/i)).toBeInTheDocument();
  });

  it("contains a link to /billing", () => {
    renderBanner();
    const link = screen.getByRole("link", { name: /manage subscription/i });
    expect(link.getAttribute("href")).toContain("/billing");
  });

  it("dismiss button removes the banner", async () => {
    const user = userEvent.setup();
    renderBanner("Read-only mode.");
    await user.click(screen.getByRole("button", { name: /dismiss/i }));
    expect(screen.queryByText(/read-only mode/i)).not.toBeInTheDocument();
  });
});
