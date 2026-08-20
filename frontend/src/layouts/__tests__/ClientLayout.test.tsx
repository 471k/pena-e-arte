import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import notificationsReducer from "@/features/notifications/notificationsSlice";
import { notificationsApi } from "@/features/notifications/notificationsApi";
import { authApi } from "@/features/auth/authApi";
import { onboardingApi } from "@/features/help/onboardingApi";
import { ClientLayout } from "@/layouts/ClientLayout";

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/notifications", () =>
    HttpResponse.json([]),
  ),
  http.get("http://localhost/api/v1/auth/my-studios", () =>
    HttpResponse.json([]),
  ),
  // Onboarding tour: complete by default so it doesn't interfere with unrelated assertions.
  http.get("http://localhost/api/v1/onboarding/tour-status", () =>
    HttpResponse.json({ hasCompletedTour: true }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

type StoreOverrides = {
  readOnlyError?:   string | null;
  studioSuspended?: boolean;
  planLimitError?:  string | null;
};

function makeStore(overrides: StoreOverrides = {}) {
  return configureStore({
    reducer: {
      auth:                            authReducer,
      ui:                              uiReducer,
      notifications:                   notificationsReducer,
      [notificationsApi.reducerPath]:  notificationsApi.reducer,
      [authApi.reducerPath]:           authApi.reducer,
      [onboardingApi.reducerPath]:     onboardingApi.reducer,
    },
    middleware: (gd) => gd().concat(notificationsApi.middleware, authApi.middleware, onboardingApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "client@test.com" }, token: "fake", tenantId: "t1", role: "client", pendingReferralCode: null } as any,
      ui:   { readOnlyError: overrides.readOnlyError ?? null, sessionExpired: false, studioSuspended: overrides.studioSuspended ?? false, planLimitError: overrides.planLimitError ?? null },
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

afterEach(() => { server.resetHandlers(); cleanup(); });

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("ClientLayout", () => {
  it("renders the brand name", () => {
    renderLayout();
    expect(screen.getByText("TattooOS")).toBeInTheDocument();
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

  it("does not show Log out as a persistent top-level button", () => {
    renderLayout();
    expect(screen.queryByRole("button", { name: /log out/i })).not.toBeInTheDocument();
  });

  it("reveals Log out inside the user menu dropdown on click", async () => {
    const user = userEvent.setup();
    renderLayout();
    await user.click(screen.getByRole("button", { name: /user menu/i }));
    expect(await screen.findByRole("button", { name: /log out/i })).toBeInTheDocument();
  });

  it("clicking Log out clears the Redux auth state", async () => {
    const user  = userEvent.setup();
    const store = renderLayout();

    await user.click(screen.getByRole("button", { name: /user menu/i }));
    await user.click(await screen.findByRole("button", { name: /log out/i }));

    expect(store.getState().auth.user).toBeNull();
    expect(store.getState().auth.token).toBeNull();
  });

  it("clicking Log out navigates to /login", async () => {
    const user = userEvent.setup();
    renderLayout();

    await user.click(screen.getByRole("button", { name: /user menu/i }));
    await user.click(await screen.findByRole("button", { name: /log out/i }));

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

  it("PlanLimitBanner is hidden when there is no plan limit error", () => {
    renderLayout({ planLimitError: null });
    expect(screen.queryByText(/upgrade the plan/i)).not.toBeInTheDocument();
  });

  it("PlanLimitBanner is visible for client role but with no upgrade link (client can't act on billing)", () => {
    renderLayout({ planLimitError: "This studio's plan allows up to 6 artists. Upgrade the plan to continue." });
    expect(screen.getByText(/allows up to 6 artists/i)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /manage subscription/i })).not.toBeInTheDocument();
    expect(screen.getByText(/ask the studio owner to upgrade the plan/i)).toBeInTheDocument();
  });

  it("active nav link gets the violet background class", () => {
    renderLayout({}, "/book");
    const bookLink = screen.getByRole("link", { name: /book appointment/i });
    expect(bookLink.className).toMatch(/bg-violet-600/);
    const designsLink = screen.getByRole("link", { name: /my designs/i });
    expect(designsLink.className).not.toMatch(/bg-violet-600/);
  });

  it("SuspensionBanner is hidden when studio is not suspended", () => {
    renderLayout({ studioSuspended: false });
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("SuspensionBanner is visible when studioSuspended is true in ui state", () => {
    renderLayout({ studioSuspended: true });
    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(screen.getByText(/studio.*suspended/i)).toBeInTheDocument();
  });

  it("SuspensionBanner shows client-role copy mentioning studio contact", () => {
    renderLayout({ studioSuspended: true });
    expect(screen.getByText(/contact the studio/i)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /reactivate your subscription/i })).not.toBeInTheDocument();
  });

  it("renders a mobile nav drawer trigger", () => {
    renderLayout();
    expect(screen.getByRole("button", { name: /open navigation menu/i })).toBeInTheDocument();
  });

  it("opening the drawer and clicking a nav item navigates and closes it", async () => {
    const user = userEvent.setup();
    renderLayout({}, "/book");
    await user.click(screen.getByRole("button", { name: /open navigation menu/i }));

    const drawerDesignsLink = await screen.findAllByRole("link", { name: /my designs/i });
    // Two matches once open: the desktop nav link (hidden lg:flex, still in DOM) and the drawer's.
    expect(drawerDesignsLink.length).toBeGreaterThanOrEqual(1);
    await user.click(drawerDesignsLink[drawerDesignsLink.length - 1]);

    await screen.findByTestId("outlet");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
});
