import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { platformApi } from "@/features/platform/platformApi";
import { billingApi } from "@/features/billing/billingApi";
import { SubscriptionOversightPage } from "@/features/platform/components/SubscriptionOversightPage";
import type { PlatformSubscriptionResponse } from "@/features/platform/platform.types";
import type { PlanResponse } from "@/features/billing/billing.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const SUBS: PlatformSubscriptionResponse[] = [
  {
    studioId:        "s1",
    studioName:      "Active Studio",
    studioSlug:      "active-studio",
    subscriptionId:  "sub-1",
    status:          "Active",
    planName:        "Pro",
    trialExpiresAt:  new Date(Date.now() + 30 * 86_400_000).toISOString(),
    currentPeriodEnd: new Date(Date.now() + 30 * 86_400_000).toISOString(),
  },
  {
    studioId:        "s2",
    studioName:      "Trialing Studio",
    studioSlug:      "trialing-studio",
    subscriptionId:  "sub-2",
    status:          "Trialing",
    planName:        "Starter",
    trialExpiresAt:  new Date(Date.now() + 7 * 86_400_000).toISOString(),
    currentPeriodEnd: new Date(Date.now() + 7 * 86_400_000).toISOString(),
  },
  {
    studioId:        "s3",
    studioName:      "Cancelled Studio",
    studioSlug:      "cancelled-studio",
    subscriptionId:  "sub-3",
    status:          "Cancelled",
    planName:        null,
    trialExpiresAt:  new Date(Date.now() - 30 * 86_400_000).toISOString(),
    currentPeriodEnd: new Date(Date.now() - 30 * 86_400_000).toISOString(),
  },
];

const PLANS: PlanResponse[] = [
  {
    id:                    "plan-1",
    name:                  "Starter",
    billingInterval:       "Monthly",
    priceMonthly:          29,
    priceYearly:           290,
    yearlyDiscountPercent: 17,
    allowBrandingRemoval:  false,
  },
];

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/platform/subscriptions", () =>
    HttpResponse.json(SUBS),
  ),
  http.get("http://localhost/api/v1/billing/plans", () =>
    HttpResponse.json(PLANS),
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
      [billingApi.reducerPath]:     billingApi.reducer,
    },
    middleware: (gd) =>
      gd().concat(platformApi.middleware, billingApi.middleware),
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
      <MemoryRouter>
        <SubscriptionOversightPage />
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("SubscriptionOversightPage", () => {

  it("shows a loading spinner while loading", () => {
    renderPage();
    expect(screen.getByText("Loading…")).toBeInTheDocument();
  });

  it("renders the Subscriptions header", async () => {
    renderPage();
    expect(await screen.findByText("Subscriptions")).toBeInTheDocument();
  });

  it("renders all studio names", async () => {
    renderPage();
    expect(await screen.findByText("Active Studio")).toBeInTheDocument();
    expect(screen.getByText("Trialing Studio")).toBeInTheDocument();
    expect(screen.getByText("Cancelled Studio")).toBeInTheDocument();
  });

  it("shows subscription count in the header", async () => {
    renderPage();
    await screen.findByText("Active Studio");
    expect(screen.getByText("(3)")).toBeInTheDocument();
  });

  it("shows the Active status badge", async () => {
    renderPage();
    await screen.findByText("Active Studio");
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("shows the Trialing status badge", async () => {
    renderPage();
    await screen.findByText("Trialing Studio");
    expect(screen.getByText("Trialing")).toBeInTheDocument();
  });

  it("shows the Cancelled status badge", async () => {
    renderPage();
    await screen.findByText("Cancelled Studio");
    expect(screen.getByText("Cancelled")).toBeInTheDocument();
  });

  it("shows Cancel subscription button for cancellable subscriptions", async () => {
    renderPage();
    await screen.findByText("Active Studio");
    const cancelBtns = screen.getAllByRole("button", { name: /cancel subscription/i });
    expect(cancelBtns.length).toBeGreaterThan(0);
  });

  it("does NOT show Cancel subscription for already-cancelled subscriptions", async () => {
    renderPage();
    await screen.findByText("Cancelled Studio");
    // Only Active and Trialing should have cancel button
    const cancelBtns = screen.getAllByRole("button", { name: /cancel subscription/i });
    expect(cancelBtns).toHaveLength(2); // Active + Trialing
  });

  it("shows Extend trial button for non-Active subscriptions", async () => {
    renderPage();
    await screen.findByText("Trialing Studio");
    const extendBtns = screen.getAllByRole("button", { name: /extend trial/i });
    expect(extendBtns.length).toBeGreaterThan(0);
  });

  it("clicking Extend trial shows the days input", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Trialing Studio");

    const extendBtns = screen.getAllByRole("button", { name: /extend trial/i });
    await user.click(extendBtns[0]);

    expect(screen.getByRole("spinbutton")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /confirm/i })).toBeInTheDocument();
  });

  it("calls PATCH subscriptions/:id/trial with additionalDays", async () => {
    const extendSpy = vi.fn();
    server.use(
      http.patch("http://localhost/api/v1/platform/subscriptions/s2/trial", async ({ request }) => {
        const body = await request.json();
        extendSpy(body);
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Trialing Studio");

    const extendBtns = screen.getAllByRole("button", { name: /extend trial/i });
    await user.click(extendBtns[0]);
    await user.click(screen.getByRole("button", { name: /confirm/i }));

    await waitFor(() => expect(extendSpy).toHaveBeenCalledWith({ additionalDays: 7 }));
  });

  it("shows cancel confirmation when clicking Cancel subscription", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Active Studio");

    const cancelBtns = screen.getAllByRole("button", { name: /cancel subscription/i });
    await user.click(cancelBtns[0]);

    expect(screen.getByText(/cancel this subscription\?/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /confirm/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /back/i })).toBeInTheDocument();
  });

  it("calls PATCH subscriptions/:id/cancel on confirm", async () => {
    const cancelSpy = vi.fn();
    server.use(
      http.patch("http://localhost/api/v1/platform/subscriptions/s1/cancel", () => {
        cancelSpy();
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Active Studio");

    const cancelBtns = screen.getAllByRole("button", { name: /cancel subscription/i });
    await user.click(cancelBtns[0]);
    await user.click(screen.getByRole("button", { name: /confirm/i }));

    await waitFor(() => expect(cancelSpy).toHaveBeenCalledOnce());
  });

  it("shows empty state when no subscriptions exist", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/subscriptions", () =>
        HttpResponse.json([]),
      ),
    );
    renderPage();
    expect(await screen.findByText(/no studios found/i)).toBeInTheDocument();
  });

  it("shows error state when subscriptions fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/subscriptions", () =>
        HttpResponse.json({ message: "Server error" }, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText(/failed to load subscriptions/i)).toBeInTheDocument();
  });
});
