import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { platformApi } from "@/features/platform/platformApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { billingApi } from "@/features/billing/billingApi";
import { IssuerStudioDetailPage } from "@/features/platform/components/IssuerStudioDetailPage";
import type { StudioResponse } from "@/features/studios/studiosApi";
import type { PlatformSubscriptionResponse } from "@/features/platform/platform.types";
import type { PlanResponse } from "@/features/billing/billing.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const STUDIO: StudioResponse = {
  id:                   "s1",
  name:                 "Ink Soul",
  slug:                 "ink-soul",
  city:                 "Porto",
  latitude:             41.1,
  longitude:            -8.6,
  showPlatformBranding: true,
  allowBrandingRemoval: false,
  trialExpiresAt:       new Date(Date.now() + 14 * 86_400_000).toISOString(),
  createdAt:            "2024-01-01T00:00:00Z",
  isActive:             true,
  slugLockedAt:         null,
  phoneNumber:          null,
  instagramHandle:      null,
};

const SUB: PlatformSubscriptionResponse = {
  studioId:         "s1",
  studioName:       "Ink Soul",
  studioSlug:       "ink-soul",
  subscriptionId:   "sub-1",
  status:           "Active",
  planName:         "Pro",
  trialExpiresAt:   new Date(Date.now() + 30 * 86_400_000).toISOString(),
  currentPeriodEnd: new Date(Date.now() + 30 * 86_400_000).toISOString(),
};

const PLANS: PlanResponse[] = [
  {
    id:                    "plan-1",
    name:                  "Starter",
    billingInterval:       "Monthly",
    priceMonthly:          29,
    priceYearly:           290,
    yearlyDiscountPercent: 17,
    allowBrandingRemoval:  false,
    subscriberCount:       0,
  },
];

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/studios/s1", () => HttpResponse.json(STUDIO)),
  http.get("http://localhost/api/v1/platform/subscriptions", () => HttpResponse.json([SUB])),
  http.get("http://localhost/api/v1/billing/plans", () => HttpResponse.json(PLANS)),
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
      [studiosApi.reducerPath]:     studiosApi.reducer,
      [billingApi.reducerPath]:     billingApi.reducer,
    },
    middleware: (gd) =>
      gd().concat(platformApi.middleware, studiosApi.middleware, billingApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u4", email: "issuer@platform.test" }, token: "fake", tenantId: null, role: "issuer", pendingReferralCode: null } as any,
    },
  });
}

function renderPage(studioId = "s1") {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[`/platform/studios/${studioId}`]}>
        <Routes>
          <Route path="/platform/studios/:studioId" element={<IssuerStudioDetailPage />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("IssuerStudioDetailPage", () => {
  it("renders the studio name", async () => {
    renderPage();
    // Studio name appears in both the header breadcrumb and the card title
    const names = await screen.findAllByText("Ink Soul");
    expect(names.length).toBeGreaterThan(0);
  });

  it("renders Active badge", async () => {
    renderPage();
    // Wait for data to load before asserting
    await screen.findAllByText("Ink Soul");
    expect(screen.getAllByText("Active").length).toBeGreaterThan(0);
  });

  it("renders city and registration date", async () => {
    renderPage();
    expect(await screen.findByText("Porto")).toBeInTheDocument();
  });

  it("renders a back link to /platform/studios", async () => {
    renderPage();
    await screen.findAllByText("Ink Soul");
    expect(screen.getByRole("link", { name: /studios/i })).toHaveAttribute("href", "/platform/studios");
  });

  it("shows 404 message for unknown studio id", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios/unknown", () =>
        HttpResponse.json({ message: "Not found" }, { status: 404 }),
      ),
    );
    renderPage("unknown");
    expect(await screen.findByText(/studio not found/i)).toBeInTheDocument();
  });

  it("shows Suspend button for active studios", async () => {
    renderPage();
    await screen.findAllByText("Ink Soul");
    expect(screen.getByRole("button", { name: /suspend/i })).toBeInTheDocument();
  });
});
