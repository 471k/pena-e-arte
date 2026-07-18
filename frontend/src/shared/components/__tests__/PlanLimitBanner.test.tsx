import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";

import uiReducer from "@/features/ui/uiSlice";
import authReducer from "@/features/auth/authSlice";
import { PlanLimitBanner } from "@/shared/components/PlanLimitBanner";

function makeStore(planLimitError: string | null, role: "owner" | "artist" | "client" | null = "owner") {
  return configureStore({
    reducer: { auth: authReducer, ui: uiReducer },
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: null, token: null, tenantId: null, role } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError },
    },
  });
}

const DEFAULT_MESSAGE = "This studio's plan allows up to 6 artists. Upgrade the plan to continue.";

function renderBanner(
  message: string | null = DEFAULT_MESSAGE,
  role: "owner" | "artist" | "client" | null = "owner",
) {
  render(
    <Provider store={makeStore(message, role)}>
      <MemoryRouter>
        <PlanLimitBanner />
      </MemoryRouter>
    </Provider>,
  );
}

describe("PlanLimitBanner", () => {
  it("does not render when planLimitError is null", () => {
    renderBanner(null);
    expect(screen.queryByRole("button", { name: /dismiss/i })).not.toBeInTheDocument();
  });

  it("renders the message when planLimitError is set", () => {
    renderBanner("This studio's plan allows up to 6 artists. Upgrade the plan to continue.");
    expect(screen.getByText(/allows up to 6 artists/i)).toBeInTheDocument();
  });

  it("shows an upgrade link to /billing for role owner", () => {
    renderBanner(DEFAULT_MESSAGE, "owner");
    const link = screen.getByRole("link", { name: /manage subscription/i });
    expect(link.getAttribute("href")).toContain("/billing");
  });

  it("does not show an upgrade link for role artist", () => {
    renderBanner(DEFAULT_MESSAGE, "artist");
    expect(screen.queryByRole("link", { name: /manage subscription/i })).not.toBeInTheDocument();
    expect(screen.getByText(/ask the studio owner to upgrade the plan/i)).toBeInTheDocument();
  });

  it("does not show an upgrade link for role client", () => {
    renderBanner(DEFAULT_MESSAGE, "client");
    expect(screen.queryByRole("link", { name: /manage subscription/i })).not.toBeInTheDocument();
    expect(screen.getByText(/ask the studio owner to upgrade the plan/i)).toBeInTheDocument();
  });

  it("dismiss button clears the state", async () => {
    const user = userEvent.setup();
    renderBanner("Plan limit reached.");
    await user.click(screen.getByRole("button", { name: /dismiss/i }));
    expect(screen.queryByText(/plan limit reached/i)).not.toBeInTheDocument();
  });
});
