import { describe, it, expect, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";

import authReducer from "@/features/auth/authSlice";
import { IssuerLayout } from "@/layouts/IssuerLayout";

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: { auth: authReducer },
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u4", email: "issuer@platform.test" }, token: "fake", tenantId: null, role: "issuer", pendingReferralCode: null } as any,
    },
  });
}

function renderLayout(initialPath = "/platform") {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[initialPath]}>
        <Routes>
          <Route element={<IssuerLayout />}>
            <Route path="/platform"                element={<div data-testid="outlet" />} />
            <Route path="/platform/studios"        element={<div data-testid="outlet" />} />
            <Route path="/platform/plans"          element={<div data-testid="outlet" />} />
            <Route path="/platform/subscriptions"  element={<div data-testid="outlet" />} />
            <Route path="/platform/referrals"      element={<div data-testid="outlet" />} />
            <Route path="/platform/reports"        element={<div data-testid="outlet" />} />
          </Route>
          <Route path="/login" element={<div data-testid="login-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

afterEach(cleanup);

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("IssuerLayout", () => {
  it("renders 'Platform Admin' as the header title (not the studio brand)", () => {
    renderLayout();
    // Appears in both the header title span and the UserChip role label span
    expect(screen.getAllByText("Platform Admin")).toHaveLength(2);
  });

  it("does not render the studio brand name 'Pena e Artë'", () => {
    renderLayout();
    expect(screen.queryByText("Pena e Artë")).not.toBeInTheDocument();
  });

  it("renders all six issuer nav links", () => {
    renderLayout();
    expect(screen.getByRole("link", { name: /^dashboard$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^studios$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^plans$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^subscriptions$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^referrals$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^reports$/i })).toBeInTheDocument();
  });

  it("renders the UserChip with the issuer's identifier and 'Platform Admin' role label", () => {
    renderLayout();
    // email prefix: "issuer"
    expect(screen.getByText("issuer")).toBeInTheDocument();
    // ROLE_LABELS["issuer"] = "Platform Admin" — present once in chip, once in header title
    expect(screen.getAllByText("Platform Admin").length).toBeGreaterThanOrEqual(1);
  });

  it("shows the Log out button", () => {
    renderLayout();
    expect(screen.getByRole("button", { name: /log out/i })).toBeInTheDocument();
  });

  it("clicking Log out clears the Redux auth state", async () => {
    const user  = userEvent.setup();
    const store = renderLayout();

    await user.click(screen.getByRole("button", { name: /log out/i }));

    expect(store.getState().auth.user).toBeNull();
    expect(store.getState().auth.token).toBeNull();
  });

  it("clicking Log out navigates to /login", async () => {
    const user = userEvent.setup();
    renderLayout();

    await user.click(screen.getByRole("button", { name: /log out/i }));

    expect(screen.getByTestId("login-page")).toBeInTheDocument();
  });

  it("outlet renders its child route", () => {
    renderLayout("/platform");
    expect(screen.getByTestId("outlet")).toBeInTheDocument();
  });

  it("Dashboard link is active at exact /platform path", () => {
    renderLayout("/platform");
    const dashboardLink = screen.getByRole("link", { name: /^dashboard$/i });
    expect(dashboardLink.className).toMatch(/bg-primary/);
  });

  it("Dashboard link is NOT active when on a sub-path like /platform/studios (end prop)", () => {
    renderLayout("/platform/studios");
    const dashboardLink = screen.getByRole("link", { name: /^dashboard$/i });
    expect(dashboardLink.className).not.toMatch(/bg-primary/);
    // Studios link should be active instead
    const studiosLink = screen.getByRole("link", { name: /^studios$/i });
    expect(studiosLink.className).toMatch(/bg-primary/);
  });

  it("does not render a ReadOnlyBanner (IssuerLayout has no banner)", () => {
    renderLayout();
    expect(screen.queryByRole("button", { name: /dismiss/i })).not.toBeInTheDocument();
  });
});
