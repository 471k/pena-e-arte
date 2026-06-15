import { describe, it, expect, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { ClientLayout } from "@/layouts/ClientLayout";

// ── Helpers ────────────────────────────────────────────────────────────────────

type StoreOverrides = {
  readOnlyError?: string | null;
};

function makeStore(overrides: StoreOverrides = {}) {
  return configureStore({
    reducer: {
      auth: authReducer,
      ui:   uiReducer,
    },
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "client@test.com" }, token: "fake", tenantId: "t1", role: "client", pendingReferralCode: null } as any,
      ui:   { readOnlyError: overrides.readOnlyError ?? null, sessionExpired: false },
    },
  });
}

function renderLayout(overrides: StoreOverrides = {}, initialPath = "/book") {
  const store = makeStore(overrides);
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[initialPath]}>
        <Routes>
          <Route element={<ClientLayout />}>
            <Route path="/book"          element={<div data-testid="outlet" />} />
            <Route path="/designs"       element={<div data-testid="outlet" />} />
            <Route path="/forms/intake"  element={<div data-testid="outlet" />} />
            <Route path="/forms/consent" element={<div data-testid="outlet" />} />
            <Route path="/clients/me"    element={<div data-testid="outlet" />} />
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

describe("ClientLayout", () => {
  it("renders the brand name", () => {
    renderLayout();
    expect(screen.getByText("Pena e Artë")).toBeInTheDocument();
  });

  it("renders all five client nav links", () => {
    renderLayout();
    expect(screen.getByRole("link", { name: /book appointment/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /my designs/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /intake forms/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /consent forms/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /my profile/i })).toBeInTheDocument();
  });

  it("renders the UserChip with the logged-in user's identifier", () => {
    renderLayout();
    // email prefix "client" becomes the display name (no name field)
    expect(screen.getByText("client")).toBeInTheDocument();
    expect(screen.getByText("Client")).toBeInTheDocument();
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
    renderLayout({}, "/book");
    expect(screen.getByTestId("outlet")).toBeInTheDocument();
  });

  it("ReadOnlyBanner is hidden when there is no read-only error", () => {
    renderLayout({ readOnlyError: null });
    expect(screen.queryByRole("button", { name: /dismiss/i })).not.toBeInTheDocument();
  });

  it("ReadOnlyBanner is visible when readOnlyError is set in ui state", () => {
    renderLayout({ readOnlyError: "This action is blocked in read-only mode." });
    expect(screen.getByText(/read-only mode/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /dismiss/i })).toBeInTheDocument();
  });

  it("active nav link gets the primary background class", () => {
    renderLayout({}, "/book");
    const bookLink = screen.getByRole("link", { name: /book appointment/i });
    expect(bookLink.className).toMatch(/bg-primary/);
    const designsLink = screen.getByRole("link", { name: /my designs/i });
    expect(designsLink.className).not.toMatch(/bg-primary/);
  });
});
