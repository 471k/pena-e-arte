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
import { billingApi } from "@/features/billing/billingApi";
import { studiosApi } from "@/features/studios/studiosApi";
import type { StudioResponse } from "@/features/studios/studiosApi";
import { notificationsApi } from "@/features/notifications/notificationsApi";
import { authApi } from "@/features/auth/authApi";
import { onboardingApi } from "@/features/help/onboardingApi";
import { OwnerLayout } from "@/layouts/OwnerLayout";

// ── Seed data ──────────────────────────────────────────────────────────────────

const ACTIVE_STUDIO: StudioResponse = {
  id:                   "stud-0001",
  name:                 "Ink Soul",
  slug:                 "ink-soul",
  city:                 "Porto",
  latitude:             41.1,
  longitude:            -8.6,
  showPlatformBranding: true,
  allowBrandingRemoval: false,
  trialExpiresAt:       "2099-01-01T00:00:00Z",
  createdAt:            "2024-01-01T00:00:00Z",
  isActive:             true,
  slugLockedAt:         null,
  phoneNumber:          null,
  instagramHandle:      null,
  nipt:                 null,
};

const SUSPENDED_STUDIO: StudioResponse = { ...ACTIVE_STUDIO, isActive: false };

const SUBSCRIPTION_ACTIVE = {
  id:                   "sub-0001",
  studioId:             "stud-0001",
  planId:               null,
  pendingPlanId:        null,
  status:               "Active",
  trialExpiresAt:       "2099-01-01T00:00:00Z",
  currentPeriodEnd:     "2099-02-01T00:00:00Z",
  gracePeriodEnd:       "2099-02-08T00:00:00Z",
  stripeSubscriptionId: null,
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/billing/subscription", () =>
    HttpResponse.json(SUBSCRIPTION_ACTIVE),
  ),
  http.get("http://localhost/api/v1/studios/me", () =>
    HttpResponse.json(ACTIVE_STUDIO),
  ),
  http.get("http://localhost/api/v1/notifications", () =>
    HttpResponse.json([]),
  ),
  // Onboarding tour: complete by default so it doesn't interfere with unrelated assertions.
  http.get("http://localhost/api/v1/onboarding/tour-status", () =>
    HttpResponse.json({ hasCompletedTour: true }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

type StoreOverrides = {
  readOnlyError?:  string | null;
  planLimitError?: string | null;
};

function makeStore(overrides: StoreOverrides = {}) {
  return configureStore({
    reducer: {
      auth:                            authReducer,
      ui:                              uiReducer,
      notifications:                   notificationsReducer,
      [billingApi.reducerPath]:        billingApi.reducer,
      [studiosApi.reducerPath]:        studiosApi.reducer,
      [notificationsApi.reducerPath]:  notificationsApi.reducer,
      [onboardingApi.reducerPath]:     onboardingApi.reducer,
      [authApi.reducerPath]:           authApi.reducer,
    },
    middleware: (gd) => gd().concat(billingApi.middleware, studiosApi.middleware, notificationsApi.middleware, onboardingApi.middleware, authApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u3", email: "owner@ink.test" }, token: "fake", tenantId: "t1", role: "owner", pendingReferralCode: null } as any,
      ui:   { readOnlyError: overrides.readOnlyError ?? null, sessionExpired: false, studioSuspended: false, planLimitError: overrides.planLimitError ?? null },
    },
  });
}

function renderLayout(overrides: StoreOverrides = {}, initialPath = "/dashboard") {
  const store = makeStore(overrides);
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[initialPath]}>
        <Routes>
          <Route element={<OwnerLayout />}>
            <Route path="/dashboard"   element={<div data-testid="outlet" />} />
            <Route path="/schedule"    element={<div data-testid="outlet" />} />
            <Route path="/artists"     element={<div data-testid="outlet" />} />
            <Route path="/clients"     element={<div data-testid="outlet" />} />
            <Route path="/designs"     element={<div data-testid="outlet" />} />
            <Route path="/payments"    element={<div data-testid="outlet" />} />
            <Route path="/billing"     element={<div data-testid="outlet" />} />
            <Route path="/studios/me"  element={<div data-testid="outlet" />} />
          </Route>
          <Route path="/login" element={<div data-testid="login-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("OwnerLayout", () => {
  it("renders the brand name", () => {
    renderLayout();
    expect(screen.getByText("TattooOS")).toBeInTheDocument();
  });

  it("renders all eight owner nav links", () => {
    renderLayout();
    expect(screen.getByRole("link", { name: /^dashboard$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^schedule$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^artists$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^clients$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^designs$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^payments$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^billing$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /studio settings/i })).toBeInTheDocument();
  });

  it("Notifications is not a top-nav link (access via header bell icon)", () => {
    renderLayout();
    // The NotificationBell in the header handles notifications — no nav link
    expect(screen.queryByRole("link", { name: /^notifications$/i })).not.toBeInTheDocument();
  });

  it("renders the UserChip with the logged-in owner's identifier", () => {
    renderLayout();
    expect(screen.getByText("owner")).toBeInTheDocument();
    expect(screen.getByText("Owner")).toBeInTheDocument();
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
    renderLayout({}, "/dashboard");
    expect(screen.getByTestId("outlet")).toBeInTheDocument();
  });

  it("ReadOnlyBanner is hidden when there is no read-only error", () => {
    renderLayout({ readOnlyError: null });
    expect(screen.queryByRole("button", { name: /dismiss/i })).not.toBeInTheDocument();
  });

  it("ReadOnlyBanner is visible when readOnlyError is set in ui state", () => {
    renderLayout({ readOnlyError: "Studio is in grace period — read-only mode." });
    expect(screen.getByText(/read-only mode/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /dismiss/i })).toBeInTheDocument();
  });

  it("PlanLimitBanner is hidden when there is no plan limit error", () => {
    renderLayout({ planLimitError: null });
    expect(screen.queryByText(/upgrade the plan/i)).not.toBeInTheDocument();
  });

  it("PlanLimitBanner is visible when planLimitError is set in ui state", () => {
    renderLayout({ planLimitError: "This studio's plan allows up to 6 artists. Upgrade the plan to continue." });
    expect(screen.getByText(/allows up to 6 artists/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /manage subscription/i })).toBeInTheDocument();
  });

  it("SuspensionBanner is hidden when the studio is active", async () => {
    renderLayout();
    // Wait for the getMyStudio query to settle, then assert banner absent
    await screen.findByTestId("outlet");
    expect(screen.queryByText(/studio has been suspended/i)).not.toBeInTheDocument();
  });

  it("SuspensionBanner is visible when the studio is suspended", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios/me", () =>
        HttpResponse.json(SUSPENDED_STUDIO),
      ),
    );

    renderLayout();

    expect(await screen.findByText(/studio has been suspended/i)).toBeInTheDocument();
  });

  it("SuspensionBanner stays hidden when the studios/me query fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios/me", () =>
        HttpResponse.json({ message: "Server error" }, { status: 500 }),
      ),
    );

    renderLayout();
    await screen.findByTestId("outlet");

    expect(screen.queryByText(/studio has been suspended/i)).not.toBeInTheDocument();
  });

  it("SuspensionBanner stays hidden when isActive is absent from the response", async () => {
    // Simulates a partial/unexpected API response shape — isActive omitted
    server.use(
      http.get("http://localhost/api/v1/studios/me", () =>
        HttpResponse.json({ id: "stud-0001", name: "Ink Soul" }),
      ),
    );

    renderLayout();
    await screen.findByTestId("outlet");

    expect(screen.queryByText(/studio has been suspended/i)).not.toBeInTheDocument();
  });

  it("active nav link gets the primary background class", () => {
    renderLayout({}, "/dashboard");
    const dashboardLink = screen.getByRole("link", { name: /^dashboard$/i });
    expect(dashboardLink.className).toMatch(/bg-primary/);
    const artistsLink = screen.getByRole("link", { name: /^artists$/i });
    expect(artistsLink.className).not.toMatch(/bg-primary/);
  });
});
