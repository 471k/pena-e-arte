import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import notificationsReducer from "@/features/notifications/notificationsSlice";
import { notificationsApi } from "@/features/notifications/notificationsApi";
import { feedbackApi } from "@/features/feedback/feedbackApi";
import { authApi } from "@/features/auth/authApi";
import { onboardingApi } from "@/features/help/onboardingApi";
import { conductReportsApi } from "@/features/conduct-reports/conductReportsApi";
import { AdminLayout } from "@/layouts/AdminLayout";

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/notifications", () =>
    HttpResponse.json([]),
  ),
  http.get("http://localhost/api/v1/platform/feedback", () =>
    HttpResponse.json([]),
  ),
  http.get("http://localhost/api/v1/platform/conduct-reports", () =>
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

function makeStore() {
  return configureStore({
    reducer: {
      auth:                            authReducer,
      notifications:                   notificationsReducer,
      [notificationsApi.reducerPath]:  notificationsApi.reducer,
      [feedbackApi.reducerPath]:       feedbackApi.reducer,
      [onboardingApi.reducerPath]:     onboardingApi.reducer,
      [authApi.reducerPath]:           authApi.reducer,
      [conductReportsApi.reducerPath]: conductReportsApi.reducer,
    },
    middleware: (gd) => gd().concat(notificationsApi.middleware, feedbackApi.middleware, onboardingApi.middleware, authApi.middleware, conductReportsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u4", email: "admin@platform.test" }, token: "fake", tenantId: null, role: "admin", pendingReferralCode: null } as any,
    },
  });
}

function renderLayout(initialPath = "/platform") {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[initialPath]}>
        <Routes>
          <Route element={<AdminLayout />}>
            <Route path="/platform"                element={<div data-testid="outlet" />} />
            <Route path="/platform/studios"        element={<div data-testid="outlet" />} />
            <Route path="/platform/plans"          element={<div data-testid="outlet" />} />
            <Route path="/platform/subscriptions"  element={<div data-testid="outlet" />} />
            <Route path="/platform/referrals"      element={<div data-testid="outlet" />} />
            <Route path="/platform/reports"        element={<div data-testid="outlet" />} />
            <Route path="/platform/feedback"       element={<div data-testid="outlet" />} />
            <Route path="/platform/conduct-reports" element={<div data-testid="outlet" />} />
            <Route path="/notifications"           element={<div data-testid="outlet" />} />
          </Route>
          <Route path="/login" element={<div data-testid="login-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("AdminLayout", () => {
  it("renders 'Platform Admin' as the header title (not the studio brand)", () => {
    renderLayout();
    // Now appears only once — the UserChip role label is "Admin" (a shorter,
    // separate string), not "Platform Admin".
    expect(screen.getAllByText("Platform Admin")).toHaveLength(1);
  });

  it("does not render the studio brand name 'TattooOS'", () => {
    renderLayout();
    expect(screen.queryByText("TattooOS")).not.toBeInTheDocument();
  });

  it("renders all eight admin nav links (Notifications moved to bell icon)", () => {
    renderLayout();
    expect(screen.getByRole("link", { name: /^dashboard$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^studios$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^plans$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^subscriptions$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^referrals$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^reports$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^feedback$/i })).toBeInTheDocument();
  });

  it("does not render a 'Notifications' text link in the nav", () => {
    renderLayout();
    // NotificationBell renders a bell icon, not a "Notifications" text nav link
    expect(screen.queryByRole("link", { name: /^notifications$/i })).not.toBeInTheDocument();
  });

  it("renders the NotificationBell", () => {
    renderLayout();
    expect(screen.getByRole("button", { name: /view notifications/i })).toBeInTheDocument();
  });

  it("renders the UserChip with the admin's identifier and 'Admin' role label", () => {
    renderLayout();
    // email prefix: "admin"
    expect(screen.getByText("admin")).toBeInTheDocument();
    // ROLE_LABELS["admin"] = "Admin"
    expect(screen.getByText("Admin")).toBeInTheDocument();
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

  it("does not render a ReadOnlyBanner (AdminLayout has no banner)", () => {
    renderLayout();
    expect(screen.queryByRole("button", { name: /dismiss/i })).not.toBeInTheDocument();
  });

  it("renders a mobile nav drawer trigger", () => {
    renderLayout();
    expect(screen.getByRole("button", { name: /open navigation menu/i })).toBeInTheDocument();
  });

  it("opening the drawer and clicking a nav item navigates and closes it", async () => {
    const user = userEvent.setup();
    renderLayout("/platform");
    await user.click(screen.getByRole("button", { name: /open navigation menu/i }));

    const studiosLinks = await screen.findAllByRole("link", { name: /^studios$/i });
    await user.click(studiosLinks[studiosLinks.length - 1]);

    await screen.findByTestId("outlet");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("shows the open-feedback badge count on both the desktop nav's and the drawer's Feedback item", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/feedback", () =>
        HttpResponse.json([{ id: "fb-1" }, { id: "fb-2" }]),
      ),
    );
    const user = userEvent.setup();
    renderLayout();

    const desktopFeedbackLink = await screen.findByRole("link", { name: /^feedback/i });
    expect(await within(desktopFeedbackLink).findByText("2")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /open navigation menu/i }));
    // Radix marks background content aria-hidden while the sheet is open, so
    // only the drawer's own Feedback link is queryable by role at this point.
    const drawerFeedbackLink = await screen.findByRole("link", { name: /^feedback/i });
    expect(within(drawerFeedbackLink).getByText("2")).toBeInTheDocument();
  });
});
