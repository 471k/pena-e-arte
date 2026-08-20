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
import { artistsApi } from "@/features/artists/artistsApi";
import type { ArtistResponse } from "@/features/artists/artistsApi";
import { authApi } from "@/features/auth/authApi";
import { onboardingApi } from "@/features/help/onboardingApi";
import { ArtistLayout } from "@/layouts/ArtistLayout";

// ── Seed data ──────────────────────────────────────────────────────────────────

const MY_ARTIST: ArtistResponse = {
  id:              "artist-u2",
  studioId:        "t1",
  userId:          "u2",
  firstName:       "Test",
  lastName:        "Artist",
  email:           "artist@ink.test",
  specializations: null,
  hourlyRate:      null,
  isActive:        true,
  avatarUrl:       null,
  portfolioImages: [],
  slug: null,
  createdAt:       "2024-01-01T00:00:00Z",
  updatedAt:       "2024-01-01T00:00:00Z",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/notifications", () =>
    HttpResponse.json([]),
  ),
  http.get("http://localhost/api/v1/artists/me", () =>
    HttpResponse.json(MY_ARTIST),
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
      [artistsApi.reducerPath]:        artistsApi.reducer,
      [onboardingApi.reducerPath]:     onboardingApi.reducer,
      [authApi.reducerPath]:           authApi.reducer,
    },
    middleware: (gd) => gd().concat(notificationsApi.middleware, artistsApi.middleware, onboardingApi.middleware, authApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u2", email: "artist@ink.test" }, token: "fake", tenantId: "t1", role: "artist", pendingReferralCode: null } as any,
      ui:   { readOnlyError: overrides.readOnlyError ?? null, sessionExpired: false, studioSuspended: overrides.studioSuspended ?? false, planLimitError: overrides.planLimitError ?? null },
    },
  });
}

function renderLayout(overrides: StoreOverrides = {}, initialPath = "/schedule") {
  const store = makeStore(overrides);
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[initialPath]}>
        <Routes>
          <Route element={<ArtistLayout />}>
            <Route path="/schedule"                element={<div data-testid="outlet" />} />
            <Route path="/clients"                 element={<div data-testid="outlet" />} />
            <Route path="/designs"                 element={<div data-testid="outlet" />} />
            <Route path="/forms/intake"            element={<div data-testid="outlet" />} />
            <Route path="/forms/consent"           element={<div data-testid="outlet" />} />
            <Route path="/deposit-rules"           element={<div data-testid="outlet" />} />
            <Route path="/notifications"           element={<div data-testid="outlet" />} />
            <Route path="/artists/:id"             element={<div data-testid="portfolio-page" />} />
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

describe("ArtistLayout", () => {
  it("renders the brand name", () => {
    renderLayout();
    expect(screen.getByText("TattooOS")).toBeInTheDocument();
  });

  it("renders the seven static artist nav links", () => {
    renderLayout();
    expect(screen.getByRole("link", { name: /^schedule$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^clients$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^designs$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /intake forms/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /consent forms/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /deposit rules/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /notifications/i })).toBeInTheDocument();
  });

  it("shows 'My Portfolio' nav link once the artist record loads", async () => {
    renderLayout();
    expect(await screen.findByRole("link", { name: /my portfolio/i })).toBeInTheDocument();
  });

  it("'My Portfolio' link points to the artist's own profile page", async () => {
    renderLayout();
    const link = await screen.findByRole("link", { name: /my portfolio/i });
    expect(link).toHaveAttribute("href", `/artists/${MY_ARTIST.id}`);
  });

  it("renders the UserChip with the logged-in artist's identifier", () => {
    renderLayout();
    expect(screen.getByText("artist")).toBeInTheDocument();
    expect(screen.getByText("Artist")).toBeInTheDocument();
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
    renderLayout({}, "/schedule");
    expect(screen.getByTestId("outlet")).toBeInTheDocument();
  });

  it("ReadOnlyBanner is hidden when there is no read-only error", () => {
    renderLayout({ readOnlyError: null });
    expect(screen.queryByRole("button", { name: /dismiss/i })).not.toBeInTheDocument();
  });

  it("ReadOnlyBanner is visible when readOnlyError is set in ui state", () => {
    renderLayout({ readOnlyError: "Grace period: studio is in read-only mode." });
    expect(screen.getByText(/read-only mode/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /dismiss/i })).toBeInTheDocument();
  });

  it("PlanLimitBanner is hidden when there is no plan limit error", () => {
    renderLayout({ planLimitError: null });
    expect(screen.queryByText(/upgrade the plan/i)).not.toBeInTheDocument();
  });

  it("PlanLimitBanner is visible for artist role but with no upgrade link (artist can't act on billing)", () => {
    renderLayout({ planLimitError: "This studio's plan allows up to 6 artists. Upgrade the plan to continue." });
    expect(screen.getByText(/allows up to 6 artists/i)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /manage subscription/i })).not.toBeInTheDocument();
    expect(screen.getByText(/ask the studio owner to upgrade the plan/i)).toBeInTheDocument();
  });

  it("active nav link gets the primary background class", () => {
    renderLayout({}, "/schedule");
    const scheduleLink = screen.getByRole("link", { name: /^schedule$/i });
    expect(scheduleLink.className).toMatch(/bg-primary/);
    const clientsLink = screen.getByRole("link", { name: /^clients$/i });
    expect(clientsLink.className).not.toMatch(/bg-primary/);
  });

  it("SuspensionBanner is hidden when studio is not suspended", () => {
    renderLayout({ studioSuspended: false });
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("SuspensionBanner is visible when studioSuspended is true in ui state", () => {
    renderLayout({ studioSuspended: true });
    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(screen.getByText(/studio's account has been suspended/i)).toBeInTheDocument();
  });

  it("SuspensionBanner shows artist-role copy (not owner reactivation link)", () => {
    renderLayout({ studioSuspended: true });
    expect(screen.queryByRole("link", { name: /reactivate your subscription/i })).not.toBeInTheDocument();
    expect(screen.getByText(/contact your studio owner/i)).toBeInTheDocument();
  });

  it("renders a mobile nav drawer trigger", () => {
    renderLayout();
    expect(screen.getByRole("button", { name: /open navigation menu/i })).toBeInTheDocument();
  });

  it("opening the drawer includes the conditional 'My Portfolio' item once the artist record loads", async () => {
    const user = userEvent.setup();
    renderLayout();
    await user.click(screen.getByRole("button", { name: /open navigation menu/i }));

    const portfolioLinks = await screen.findAllByRole("link", { name: /my portfolio/i });
    expect(portfolioLinks.length).toBeGreaterThanOrEqual(1);
  });

  it("opening the drawer and clicking a nav item navigates and closes it", async () => {
    const user = userEvent.setup();
    renderLayout({}, "/schedule");
    await user.click(screen.getByRole("button", { name: /open navigation menu/i }));

    const clientsLinks = await screen.findAllByRole("link", { name: /^clients$/i });
    await user.click(clientsLinks[clientsLinks.length - 1]);

    await screen.findByTestId("outlet");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
});
