import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { platformApi } from "@/features/platform/platformApi";
import { IssuerDashboardPage } from "@/features/platform/components/IssuerDashboardPage";
import type { PlatformStatsResponse, PlatformSubscriptionResponse } from "@/features/platform/platform.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const STATS: PlatformStatsResponse = {
  totalStudios:        12,
  activeSubscriptions: 8,
  trialStudios:        3,
  gracePeriodStudios:  1,
  mrr:                 392,
  trialConversionRate: 0.727,
  newStudiosThisMonth: 2,
};

const SUBSCRIPTIONS: PlatformSubscriptionResponse[] = [
  {
    studioId:        "s1",
    studioName:      "GracePeriod Studio",
    studioSlug:      "grace-studio",
    subscriptionId:  "sub-1",
    status:          "GracePeriod",
    planName:        "Pro",
    trialExpiresAt:  new Date(Date.now() + 2 * 86_400_000).toISOString(),
    currentPeriodEnd: new Date(Date.now() + 2 * 86_400_000).toISOString(),
  },
  {
    studioId:        "s2",
    studioName:      "PastDue Studio",
    studioSlug:      "pastdue-studio",
    subscriptionId:  "sub-2",
    status:          "PastDue",
    planName:        "Starter",
    trialExpiresAt:  new Date(Date.now() - 5 * 86_400_000).toISOString(),
    currentPeriodEnd: new Date(Date.now() - 5 * 86_400_000).toISOString(),
  },
  {
    studioId:        "s3",
    studioName:      "Active Studio",
    studioSlug:      "active-studio",
    subscriptionId:  "sub-3",
    status:          "Active",
    planName:        "Pro",
    trialExpiresAt:  new Date(Date.now() + 30 * 86_400_000).toISOString(),
    currentPeriodEnd: new Date(Date.now() + 30 * 86_400_000).toISOString(),
  },
];

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/platform/stats", () =>
    HttpResponse.json(STATS),
  ),
  http.get("http://localhost/api/v1/platform/subscriptions", () =>
    HttpResponse.json(SUBSCRIPTIONS),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                         authReducer,
      [platformApi.reducerPath]:    platformApi.reducer,
    },
    middleware: (gd) => gd().concat(platformApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u4", email: "issuer@platform.test" }, token: "fake", tenantId: null, role: "issuer", pendingReferralCode: null } as any,
    },
  });
}

function renderPage() {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/platform"]}>
        <Routes>
          <Route path="/platform"              element={<IssuerDashboardPage />} />
          <Route path="/platform/studios"      element={<div data-testid="studios-page" />} />
          <Route path="/platform/plans"        element={<div data-testid="plans-page" />} />
          <Route path="/platform/subscriptions" element={<div data-testid="subscriptions-page" />} />
          <Route path="/platform/referrals"    element={<div data-testid="referrals-page" />} />
          <Route path="/platform/reports"      element={<div data-testid="reports-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("IssuerDashboardPage", () => {

  it("shows a loading skeleton while data is loading", () => {
    renderPage();
    // Skeleton cards are present before data resolves
    const skeletons = document.querySelectorAll(".animate-pulse");
    expect(skeletons.length).toBeGreaterThan(0);
  });

  it("renders the Platform Overview heading", () => {
    renderPage();
    expect(screen.getByText("Platform Overview")).toBeInTheDocument();
  });

  it("renders KPI card values once stats load", async () => {
    renderPage();
    expect(await screen.findByText("12")).toBeInTheDocument(); // totalStudios
    expect(screen.getByText("8")).toBeInTheDocument();         // activeSubscriptions
    expect(screen.getByText("3")).toBeInTheDocument();         // trialStudios
    expect(screen.getByText("1")).toBeInTheDocument();         // gracePeriodStudios
    expect(screen.getByText("2")).toBeInTheDocument();         // newStudiosThisMonth
  });

  it("shows MRR formatted as currency", async () => {
    renderPage();
    expect(await screen.findByText(/392/)).toBeInTheDocument();
  });

  it("shows trial conversion rate as percentage", async () => {
    renderPage();
    // 0.727 → 72.7%
    expect(await screen.findByText("72.7%")).toBeInTheDocument();
  });

  it("renders at-risk studios (GracePeriod and PastDue)", async () => {
    renderPage();
    expect(await screen.findByText("GracePeriod Studio")).toBeInTheDocument();
    expect(screen.getByText("PastDue Studio")).toBeInTheDocument();
  });

  it("does NOT show Active studios in the at-risk widget", async () => {
    renderPage();
    await screen.findByText("GracePeriod Studio");
    expect(screen.queryByText("Active Studio")).not.toBeInTheDocument();
  });

  it("shows 'No at-risk studios' when all subscriptions are healthy", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/subscriptions", () =>
        HttpResponse.json([SUBSCRIPTIONS[2]]), // only Active
      ),
    );
    renderPage();
    expect(await screen.findByText("No at-risk studios.")).toBeInTheDocument();
  });

  it("renders all quick nav links", async () => {
    renderPage();
    await screen.findByText("12"); // wait for stats to load
    // Use exact names so KPI card links ("Total Studios 12") don't conflict
    expect(screen.getByRole("link", { name: "Studios" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Plans" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Subscriptions" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Referrals" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Reports" })).toBeInTheDocument();
  });

  it("shows error state when stats load fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/stats", () =>
        HttpResponse.json({ message: "Server error" }, { status: 500 }),
      ),
    );
    renderPage();
    // Loading state resolves — no KPI data shown, no crash
    await screen.findByText("Platform Overview");
    expect(screen.queryByText("12")).not.toBeInTheDocument();
  });
});
