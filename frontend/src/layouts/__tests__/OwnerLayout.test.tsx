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
import { artistsApi } from "@/features/artists/artistsApi";
import { conductReportsApi } from "@/features/conduct-reports/conductReportsApi";
import { messagingApi } from "@/features/messaging/messagingApi";
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
  allowBrandingRemoval: false, allowApiAccess: false,
  trialExpiresAt:       "2099-01-01T00:00:00Z",
  createdAt:            "2024-01-01T00:00:00Z",
  isActive:             true,
  slugLockedAt:         null,
  phoneNumber:          null,
  instagramHandle:      null,
  nipt:                 null,
  isSolo:               false,
  isPublished:          true,
  timezone:             "Europe/Tirane",
  addressLine1:         null,
  addressLine2:         null,
  postalCode:           null,
};

const SUSPENDED_STUDIO: StudioResponse = { ...ACTIVE_STUDIO, isActive: false };

const MY_ARTIST_PROFILE = {
  id:              "art-owner-1",
  studioId:        "stud-0001",
  userId:          "u3",
  firstName:       "Owner",
  lastName:        "Artist",
  email:           "owner@ink.test",
  specializations: [],
  hourlyRate:      null,
  isActive:        true,
  avatarUrl:       null,
  portfolioImages: [],
  slug:            "owner-artist",
  createdAt:       "2026-01-01T00:00:00Z",
  updatedAt:       "2026-01-01T00:00:00Z",
};

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
  // No linked artist profile by default — a normal 404, same as ArtistLayout expects for every artist.
  http.get("http://localhost/api/v1/artists/me", () =>
    HttpResponse.json({ message: "Not found" }, { status: 404 }),
  ),
  http.get("http://localhost/api/v1/studios/me/conduct-reports", () =>
    HttpResponse.json([]),
  ),
  http.get("http://localhost/api/v1/conversations/unread-count", () =>
    HttpResponse.json(0),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

type StoreOverrides = {
  readOnlyError?:  string | null;
  planLimitError?: string | null;
  impersonationScopeError?:     string | null;
  impersonationSessionExpired?: boolean;
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
      [artistsApi.reducerPath]:        artistsApi.reducer,
      [conductReportsApi.reducerPath]: conductReportsApi.reducer,
      [messagingApi.reducerPath]:      messagingApi.reducer,
    },
    middleware: (gd) => gd().concat(
      billingApi.middleware, studiosApi.middleware, notificationsApi.middleware, onboardingApi.middleware,
      authApi.middleware, artistsApi.middleware, conductReportsApi.middleware, messagingApi.middleware,
    ),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u3", email: "owner@ink.test" }, token: "fake", tenantId: "t1", role: "owner", pendingReferralCode: null, impersonation: null } as any,
      ui:   { readOnlyError: overrides.readOnlyError ?? null, sessionExpired: false, studioSuspended: false, planLimitError: overrides.planLimitError ?? null, impersonationScopeError: overrides.impersonationScopeError ?? null, impersonationSessionExpired: overrides.impersonationSessionExpired ?? false },
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
            <Route path="/artists/:id" element={<div data-testid="artist-outlet" />} />
            <Route path="/earnings"    element={<div data-testid="earnings-outlet" />} />
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

  it("renders all ten owner nav links", () => {
    renderLayout();
    expect(screen.getByRole("link", { name: /^dashboard$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^schedule$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^artists$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^clients$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^messages$/i })).toBeInTheDocument();
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
    renderLayout({ planLimitError: null, impersonationScopeError: null, impersonationSessionExpired: false });
    expect(screen.queryByText(/upgrade the plan/i)).not.toBeInTheDocument();
  });

  it("PlanLimitBanner is visible when planLimitError is set in ui state", () => {
    renderLayout({ planLimitError: "This studio's plan allows up to 6 artists. Upgrade the plan to continue.", impersonationScopeError: null, impersonationSessionExpired: false });
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

  it("renders a mobile nav drawer trigger", () => {
    renderLayout();
    expect(screen.getByRole("button", { name: /open navigation menu/i })).toBeInTheDocument();
  });

  it("opening the drawer and clicking a nav item navigates and closes it", async () => {
    const user = userEvent.setup();
    renderLayout({}, "/dashboard");
    await user.click(screen.getByRole("button", { name: /open navigation menu/i }));

    const artistsLinks = await screen.findAllByRole("link", { name: /^artists$/i });
    await user.click(artistsLinks[artistsLinks.length - 1]);

    await screen.findByTestId("outlet");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  // ── Owner/Artist mode switcher (2026-09-17) ──────────────────────────────────

  it("does not render the Owner/Artist switcher when the owner has no linked artist profile", async () => {
    renderLayout();
    await screen.findByTestId("outlet");
    expect(screen.queryByRole("tablist", { name: /switch between owner and artist/i })).not.toBeInTheDocument();
  });

  it("renders the Owner/Artist switcher once the owner has a linked artist profile", async () => {
    server.use(
      http.get("http://localhost/api/v1/artists/me", () => HttpResponse.json(MY_ARTIST_PROFILE)),
    );

    renderLayout();

    expect(await screen.findByRole("tab", { name: /artist/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /owner/i })).toBeInTheDocument();
  });

  it("clicking Artist in the switcher navigates to the owner's own artist profile", async () => {
    server.use(
      http.get("http://localhost/api/v1/artists/me", () => HttpResponse.json(MY_ARTIST_PROFILE)),
    );
    const user = userEvent.setup();
    renderLayout();

    await user.click(await screen.findByRole("tab", { name: /artist/i }));

    expect(await screen.findByTestId("artist-outlet")).toBeInTheDocument();
  });

  it("clicking Owner in the switcher navigates back to the dashboard", async () => {
    server.use(
      http.get("http://localhost/api/v1/artists/me", () => HttpResponse.json(MY_ARTIST_PROFILE)),
    );
    const user = userEvent.setup();
    renderLayout({}, "/artists/art-owner-1");
    await screen.findByTestId("artist-outlet");

    await user.click(await screen.findByRole("tab", { name: /owner/i }));

    expect(await screen.findByTestId("outlet")).toBeInTheDocument();
  });
});
