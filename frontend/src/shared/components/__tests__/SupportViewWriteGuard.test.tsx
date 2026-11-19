import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import authReducer from "@/features/auth/authSlice";
import { SupportViewWriteGuard } from "@/shared/components/SupportViewWriteGuard";

function renderGuard(impersonating: boolean) {
  const store = configureStore({
    reducer: { auth: authReducer },
    preloadedState: {
      auth: {
        user: null, token: "t", refreshToken: null, tenantId: "studio-1", role: null, pendingReferralCode: null,
        impersonation: impersonating
          ? { sessionId: "s", studioId: "studio-1", studioName: "Ink & Iron", expiresAt: new Date(Date.now() + 60_000).toISOString(), reason: "r" }
          : null,
      },
    },
  });
  render(
    <Provider store={store}>
      <MemoryRouter>
        <SupportViewWriteGuard><form aria-label="New client form" /></SupportViewWriteGuard>
      </MemoryRouter>
    </Provider>,
  );
}

describe("SupportViewWriteGuard (D05)", () => {
  it("shows the page as usual outside a support session", () => {
    renderGuard(false);
    expect(screen.getByRole("form", { name: "New client form" })).toBeInTheDocument();
  });

  it("in a support session replaces a page that changes things with the reason and a way back", () => {
    renderGuard(true);
    expect(screen.queryByRole("form", { name: "New client form" })).not.toBeInTheDocument();
    expect(screen.getByText(/read-only support session: changes are turned off/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /back to the schedule/i })).toHaveAttribute("href", "/schedule");
  });
});
