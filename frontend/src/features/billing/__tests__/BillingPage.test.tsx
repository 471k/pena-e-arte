import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { billingApi } from "@/features/billing/billingApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { BillingPage } from "@/features/billing/components/BillingPage";
import type { SubscriptionResponse, PlanResponse, PlanUsageResponse } from "@/features/billing/billing.types";
import type { StudioResponse } from "@/features/studios/studiosApi";
import { toast } from "sonner";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

// ── Seed data ──────────────────────────────────────────────────────────────────

const PLANS: PlanResponse[] = [
  {
    id:                    "plan-1",
    name:                  "Starter",
    yearlyDiscountPercent: 17,
    allowBrandingRemoval:  false,
    subscriberCount:       0,
    maxArtists:               null,
    maxAppointmentsPerMonth:  null,
    maxNotificationsPerMonth: null,
    maxStorageGb:             null,
    maxLocations:             null,
    allowApiAccess:           false,
    prioritySupport:          false,
    prices: [
      { id: "price-1-m", interval: "Monthly", price: 29, stripePriceId: null, isActive: true },
    ],
  },
  {
    id:                    "plan-2",
    name:                  "Pro",
    yearlyDiscountPercent: 17,
    allowBrandingRemoval:  true,
    subscriberCount:       0,
    maxArtists:               null,
    maxAppointmentsPerMonth:  null,
    maxNotificationsPerMonth: null,
    maxStorageGb:             null,
    maxLocations:             null,
    allowApiAccess:           false,
    prioritySupport:          false,
    prices: [
      { id: "price-2-m", interval: "Monthly", price: 49, stripePriceId: null, isActive: true },
      { id: "price-2-y", interval: "Yearly", price: 490, stripePriceId: null, isActive: true },
    ],
  },
];

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

const BASE_SUB: SubscriptionResponse = {
  id:                     "sub-0001",
  studioId:               "stud-0001",
  planId:                 "plan-1",
  billingInterval:        "Monthly",
  pendingPlanId:          null,
  pendingBillingInterval: null,
  status:                 "Active",
  trialExpiresAt:         new Date(Date.now() + 7 * 86_400_000).toISOString(),
  currentPeriodEnd:       new Date(Date.now() + 30 * 86_400_000).toISOString(),
  gracePeriodEnd:         new Date(Date.now() + 7 * 86_400_000).toISOString(),
  stripeSubscriptionId:   null,
  cancelAtPeriodEnd:      false,
};

const SUB_ACTIVE_CASH: SubscriptionResponse = { ...BASE_SUB, status: "Active", stripeSubscriptionId: null };
const SUB_ACTIVE_CARD: SubscriptionResponse = { ...BASE_SUB, status: "Active", stripeSubscriptionId: "sub_stripe_xxx" };
const SUB_ACTIVE_PENDING: SubscriptionResponse = {
  ...SUB_ACTIVE_CARD, pendingPlanId: "plan-2", pendingBillingInterval: "Monthly",
};
const SUB_ACTIVE_YEARLY_CARD: SubscriptionResponse = {
  ...SUB_ACTIVE_CARD, planId: "plan-2", billingInterval: "Yearly",
};
const SUB_ACTIVE_FREE: SubscriptionResponse = { ...BASE_SUB, status: "Active", planId: "plan-free", stripeSubscriptionId: null };

const FREE_PLAN: PlanResponse = {
  id:                    "plan-free",
  name:                  "Free",
  yearlyDiscountPercent: 0,
  allowBrandingRemoval:  false,
  subscriberCount:       0,
  maxArtists:               1,
  maxAppointmentsPerMonth:  15,
  maxNotificationsPerMonth: 50,
  maxStorageGb:             1,
  maxLocations:             1,
  allowApiAccess:           false,
  prioritySupport:          false,
  prices: [
    { id: "price-free-m", interval: "Monthly", price: 0, stripePriceId: null, isActive: true },
  ],
};
const SUB_TRIALING: SubscriptionResponse    = { ...BASE_SUB, status: "Trialing",    planId: null };
const SUB_GRACE: SubscriptionResponse       = { ...BASE_SUB, status: "GracePeriod", planId: null };
const SUB_PAST_DUE: SubscriptionResponse    = { ...BASE_SUB, status: "PastDue" };
const SUB_CANCELLED: SubscriptionResponse   = { ...BASE_SUB, status: "Cancelled",   planId: null };

const USAGE: PlanUsageResponse = {
  planName:              "Starter",
  artists:               { current: 2,   max: 6 },
  appointmentsPerMonth:  { current: 12,  max: 40 },
  notificationsPerMonth: { current: 30,  max: 150 },
  storageGb:             { current: 1.2, max: 2 },
  locations:             { current: 1,   max: 1 },
};

// Portal session redirects use window.location.href — mock it for testing
Object.defineProperty(window, "location", {
  value: { href: "", assign: vi.fn() },
  writable: true,
});

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/billing/subscription", () =>
    HttpResponse.json(SUB_ACTIVE_CASH),
  ),
  http.get("http://localhost/api/v1/billing/plans", () =>
    HttpResponse.json(PLANS),
  ),
  http.get("http://localhost/api/v1/studios/me", () =>
    HttpResponse.json(ACTIVE_STUDIO),
  ),
  http.post("http://localhost/api/v1/billing/portal", () =>
    HttpResponse.json({ url: "https://billing.stripe.com/session/test_xxx" }),
  ),
  http.get("http://localhost/api/v1/billing/usage", () =>
    HttpResponse.json(null),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); vi.clearAllMocks(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                      authReducer,
      ui:                        uiReducer,
      [billingApi.reducerPath]:  billingApi.reducer,
      [studiosApi.reducerPath]:  studiosApi.reducer,
    },
    middleware: (gd) => gd().concat(billingApi.middleware, studiosApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u3", email: "owner@ink.test" }, token: "fake-token", tenantId: "t1", role: "owner", pendingReferralCode: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null },
    },
  });
}

function renderPage(initialPath = "/billing") {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[initialPath]}>
        <Routes>
          <Route path="/billing"           element={<BillingPage />} />
          <Route path="/billing/subscribe" element={<div data-testid="subscribe-page" />} />
          <Route path="/login"             element={<div data-testid="login-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("BillingPage", () => {

  // --- Loading / error states ---

  it("shows a skeleton loading state while data is loading", () => {
    renderPage();
    expect(screen.getByLabelText("Loading billing information")).toBeInTheDocument();
  });

  it("shows an error message when the subscription fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json({ message: "Server error" }, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText(/failed to load subscription/i)).toBeInTheDocument();
  });

  // --- Header ---

  it("renders the Billing header", async () => {
    renderPage();
    // The header contains a span with "Billing" text (not just any text with "billing")
    expect(await screen.findByText("Billing")).toBeInTheDocument();
  });

  // --- Active (cash-billed) ---

  it("shows the Active status label", async () => {
    renderPage();
    expect(await screen.findByText("Active")).toBeInTheDocument();
  });

  it("shows Active Until text for cash-billed subscription", async () => {
    renderPage();
    expect(await screen.findByText(/active until/i)).toBeInTheDocument();
  });

  it("shows the current plan name when planId resolves to a known plan", async () => {
    renderPage();
    // plan-1 = "Starter"
    expect(await screen.findByText("Starter")).toBeInTheDocument();
  });

  it("shows the cash-billed subscription card for Active + no stripeSubscriptionId", async () => {
    renderPage();
    expect(await screen.findByText(/cash-billed subscription/i)).toBeInTheDocument();
  });

  it("Switch to card billing button navigates to /billing/subscribe", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText(/cash-billed subscription/i);

    await user.click(screen.getByRole("button", { name: /switch to card billing/i }));

    expect(screen.getByTestId("subscribe-page")).toBeInTheDocument();
  });

  it("does not show the Subscribe/Reactivate button when subscription is Active", async () => {
    renderPage();
    await screen.findByText("Active");
    expect(screen.queryByRole("button", { name: /^subscribe$/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^reactivate$/i })).not.toBeInTheDocument();
  });

  // --- Active (card-billed) ---

  it("shows Change plan button for Active + card-billed subscription", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_CARD),
      ),
    );
    renderPage();
    expect(await screen.findByRole("button", { name: /change plan/i })).toBeInTheDocument();
  });

  it("shows Next charge text (not Active until) for card-billed subscription", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_CARD),
      ),
    );
    renderPage();
    await screen.findByText("Active");
    expect(screen.getByText(/next charge/i)).toBeInTheDocument();
    expect(screen.queryByText(/active until/i)).not.toBeInTheDocument();
  });

  it("does not show the cash-billed card for card-billed subscription", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_CARD),
      ),
    );
    renderPage();
    await screen.findByText("Active");
    expect(screen.queryByText(/cash-billed subscription/i)).not.toBeInTheDocument();
  });

  it("Change plan button navigates to /billing/subscribe", async () => {
    const user = userEvent.setup();
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_CARD),
      ),
    );
    renderPage();
    await screen.findByRole("button", { name: /change plan/i });

    await user.click(screen.getByRole("button", { name: /change plan/i }));

    expect(screen.getByTestId("subscribe-page")).toBeInTheDocument();
  });

  // --- Trialing ---

  it("shows the Trial status label when Trialing", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_TRIALING),
      ),
    );
    renderPage();
    expect(await screen.findByText("Trial")).toBeInTheDocument();
  });

  it("shows the Subscribe button when Trialing", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_TRIALING),
      ),
    );
    renderPage();
    expect(await screen.findByRole("button", { name: /^subscribe$/i })).toBeInTheDocument();
  });

  it("shows trial end date and days remaining when Trialing", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_TRIALING),
      ),
    );
    renderPage();
    expect(await screen.findByText(/trial ends/i)).toBeInTheDocument();
    expect(screen.getByText(/days remaining/i)).toBeInTheDocument();
  });

  it("Subscribe button (Trialing) navigates to /billing/subscribe", async () => {
    const user = userEvent.setup();
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_TRIALING),
      ),
    );
    renderPage();
    await screen.findByRole("button", { name: /^subscribe$/i });

    await user.click(screen.getByRole("button", { name: /^subscribe$/i }));

    expect(screen.getByTestId("subscribe-page")).toBeInTheDocument();
  });

  // --- GracePeriod ---

  it("shows the Grace Period status label when in GracePeriod", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_GRACE),
      ),
    );
    renderPage();
    expect(await screen.findByText("Grace Period")).toBeInTheDocument();
  });

  it("shows the Subscribe button when in GracePeriod", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_GRACE),
      ),
    );
    renderPage();
    expect(await screen.findByRole("button", { name: /^subscribe$/i })).toBeInTheDocument();
  });

  it("shows amber read-only warning with days left when in GracePeriod", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_GRACE),
      ),
    );
    renderPage();
    expect(await screen.findByText(/read-only mode/i)).toBeInTheDocument();
    expect(screen.getByText(/days left/i)).toBeInTheDocument();
  });

  // --- PastDue ---

  it("shows the Payment Failed status label when PastDue", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_PAST_DUE),
      ),
    );
    renderPage();
    expect(await screen.findByText("Payment Failed")).toBeInTheDocument();
  });

  it("shows the Reactivate button when PastDue", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_PAST_DUE),
      ),
    );
    renderPage();
    expect(await screen.findByRole("button", { name: /reactivate/i })).toBeInTheDocument();
  });

  it("shows the red payment-failed warning when PastDue", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_PAST_DUE),
      ),
    );
    renderPage();
    expect(await screen.findByText(/your last payment failed/i)).toBeInTheDocument();
  });

  // --- Cancelled ---

  it("shows the Cancelled status label when Cancelled", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_CANCELLED),
      ),
    );
    renderPage();
    expect(await screen.findByText("Cancelled")).toBeInTheDocument();
  });

  it("shows the Reactivate button when Cancelled", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_CANCELLED),
      ),
    );
    renderPage();
    expect(await screen.findByRole("button", { name: /reactivate/i })).toBeInTheDocument();
  });

  it("shows subscription-cancelled message when Cancelled", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_CANCELLED),
      ),
    );
    renderPage();
    expect(await screen.findByText(/subscription has been cancelled/i)).toBeInTheDocument();
  });

  // --- Pending plan change ---

  it("shows the scheduled plan change card when pendingPlanId is set", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_PENDING),
      ),
    );
    renderPage();
    expect(await screen.findByText(/scheduled plan change/i)).toBeInTheDocument();
  });

  it("shows the pending plan name in the scheduled change card", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_PENDING),
      ),
    );
    renderPage();
    // plan-2 = "Pro"
    expect(await screen.findByText(/your plan changes to/i)).toBeInTheDocument();
    expect(await screen.findByText(/\bpro\b/i)).toBeInTheDocument();
  });

  it("Keep current plan button calls the cancelPlanChange mutation", async () => {
    const cancelSpy = vi.fn();
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_PENDING),
      ),
      http.delete(
        "http://localhost/api/v1/billing/subscription/plan/pending",
        () => {
          cancelSpy();
          return HttpResponse.json(SUB_ACTIVE_CARD);
        },
      ),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByText(/scheduled plan change/i);

    await user.click(screen.getByRole("button", { name: /keep current plan/i }));

    await waitFor(() => expect(cancelSpy).toHaveBeenCalledOnce());
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Scheduled plan change cancelled."));
  });

  it("a failed cancel of the scheduled plan change shows an error toast, not a silent no-op", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_PENDING),
      ),
      http.delete(
        "http://localhost/api/v1/billing/subscription/plan/pending",
        () => HttpResponse.json({ message: "Server error" }, { status: 500 }),
      ),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByText(/scheduled plan change/i);

    await user.click(screen.getByRole("button", { name: /keep current plan/i }));

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Failed to cancel the scheduled plan change."));
    expect(toast.success).not.toHaveBeenCalled();
  });

  // --- Studio suspension ---

  it("shows the studio-suspended card when the studio is not active", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios/me", () =>
        HttpResponse.json(SUSPENDED_STUDIO),
      ),
    );
    renderPage();
    expect(await screen.findByText(/studio suspended/i)).toBeInTheDocument();
  });

  it("does not show the studio-suspended card when the studio is active", async () => {
    renderPage();
    await screen.findByText("Active");
    expect(screen.queryByText(/studio suspended/i)).not.toBeInTheDocument();
  });

  // --- Stripe Checkout finalisation ---

  it("calls finalizeCheckout with the session_id from the URL", async () => {
    const finalizeSpy = vi.fn();
    server.use(
      http.post(
        "http://localhost/api/v1/billing/subscription/checkout/finalize",
        async ({ request }) => {
          const body = await request.json();
          finalizeSpy(body);
          return HttpResponse.json(SUB_ACTIVE_CARD);
        },
      ),
    );

    renderPage("/billing?session_id=cs_test_xxx");

    await waitFor(() =>
      expect(finalizeSpy).toHaveBeenCalledWith({ sessionId: "cs_test_xxx" }),
    );
  });

  it("does not call finalizeCheckout when session_id is absent from the URL", async () => {
    const finalizeSpy = vi.fn();
    server.use(
      http.post(
        "http://localhost/api/v1/billing/subscription/checkout/finalize",
        () => {
          finalizeSpy();
          return HttpResponse.json(SUB_ACTIVE_CARD);
        },
      ),
    );

    renderPage("/billing");
    await screen.findByText("Active");

    expect(finalizeSpy).not.toHaveBeenCalled();
  });

  // ── Plan badge and price display ──────────────────────────────────────────────

  it("shows plan name as a badge (not 'Plan: Starter')", async () => {
    renderPage();
    expect(await screen.findByText("Starter")).toBeInTheDocument();
    expect(screen.queryByText(/^plan:/i)).not.toBeInTheDocument();
  });

  it("shows monthly price for Active subscription", async () => {
    renderPage();
    await screen.findByText("Active");
    const priceEl = screen.getByText(/29/);
    expect(priceEl).toBeInTheDocument();
  });

  it("shows 'Next charge' with amount for Active card-billed subscription", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_CARD),
      ),
    );
    renderPage();
    await screen.findByText("Active");
    expect(screen.getByText(/next charge/i)).toBeInTheDocument();
  });

  it("shows 'Active until' (not 'Next charge') for Active cash-billed subscription", async () => {
    renderPage();
    await screen.findByText("Active");
    expect(await screen.findByText(/active until/i)).toBeInTheDocument();
    expect(screen.queryByText(/next charge/i)).not.toBeInTheDocument();
  });

  it("shows '/ year' and the yearly price for a yearly-billed subscription, not '/ month'", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_YEARLY_CARD),
      ),
    );
    renderPage();
    await screen.findByText("Active");
    expect(screen.getAllByText(/490/).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/\/\s*year/i).length).toBeGreaterThan(0);
    expect(screen.queryByText(/\/\s*month/i)).not.toBeInTheDocument();
  });

  // ── Status badge visual indicator ─────────────────────────────────────────────

  it("renders the Active status as a pill element (not just colored text)", async () => {
    renderPage();
    await screen.findByText("Active");
    const activePill = screen.getByText("Active");
    expect(activePill.tagName.toLowerCase()).toBe("span");
  });

  // ── Change plan button relocation ─────────────────────────────────────────────

  it("Change plan button is NOT in the page header for Active card-billed", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_CARD),
      ),
    );
    renderPage();
    const changePlanBtn = await screen.findByRole("button", { name: /change plan/i });
    const header = document.querySelector("header");
    expect(header).not.toContainElement(changePlanBtn);
  });

  // ── Manage billing (Stripe Customer Portal) ───────────────────────────────────

  it("shows Manage billing button for Active card-billed subscription", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_CARD),
      ),
    );
    renderPage();
    expect(await screen.findByRole("button", { name: /manage billing/i })).toBeInTheDocument();
  });

  it("does NOT show Manage billing button for Active cash-billed subscription", async () => {
    renderPage();
    await screen.findByText("Active");
    expect(screen.queryByRole("button", { name: /manage billing/i })).not.toBeInTheDocument();
  });

  it("does NOT show Manage billing button when subscription is Trialing", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_TRIALING),
      ),
    );
    renderPage();
    await screen.findByText("Trial");
    expect(screen.queryByRole("button", { name: /manage billing/i })).not.toBeInTheDocument();
  });

  it("does NOT show Manage billing button when subscription is Cancelled", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_CANCELLED),
      ),
    );
    renderPage();
    await screen.findByText("Cancelled");
    expect(screen.queryByRole("button", { name: /manage billing/i })).not.toBeInTheDocument();
  });

  it("clicking Manage billing calls the portal mutation and redirects", async () => {
    const portalSpy = vi.fn();
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_CARD),
      ),
      http.post("http://localhost/api/v1/billing/portal", async ({ request }) => {
        const body = await request.json() as { returnUrl: string };
        portalSpy(body);
        return HttpResponse.json({ url: "https://billing.stripe.com/session/test_xyz" });
      }),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("button", { name: /manage billing/i });

    await user.click(screen.getByRole("button", { name: /manage billing/i }));

    await waitFor(() => expect(portalSpy).toHaveBeenCalledOnce());
    expect(window.location.href).toBe("https://billing.stripe.com/session/test_xyz");
  });

  // ── Plan usage panel ───────────────────────────────────────────────────────────

  it("does not show the usage panel when the usage endpoint returns null", async () => {
    renderPage();
    await screen.findByText("Active");
    expect(screen.queryByText(/plan usage/i)).not.toBeInTheDocument();
  });

  it("shows the usage panel with all five dimensions when usage data is present", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/usage", () => HttpResponse.json(USAGE)),
    );
    renderPage();
    await screen.findByText(/plan usage/i);

    expect(screen.getByText("Artists")).toBeInTheDocument();
    expect(screen.getByText("Appointments this month")).toBeInTheDocument();
    expect(screen.getByText("Notifications this month")).toBeInTheDocument();
    expect(screen.getByText("Storage")).toBeInTheDocument();
    expect(screen.getByText("Locations")).toBeInTheDocument();
  });

  it("shows current/max for a capped dimension", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/usage", () => HttpResponse.json(USAGE)),
    );
    renderPage();
    await screen.findByText(/plan usage/i);

    expect(screen.getByText("2 / 6")).toBeInTheDocument();
  });

  it("shows 'Unlimited' for a dimension with a null max", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/usage", () =>
        HttpResponse.json({ ...USAGE, appointmentsPerMonth: { current: 12, max: null } }),
      ),
    );
    renderPage();
    await screen.findByText(/plan usage/i);

    expect(screen.getByText(/12 · Unlimited/)).toBeInTheDocument();
  });

  it("shows the fractional storage value with one decimal", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/usage", () => HttpResponse.json(USAGE)),
    );
    renderPage();
    await screen.findByText(/plan usage/i);

    expect(screen.getByText("1.2 / 2 GB")).toBeInTheDocument();
  });

  // ── Free plan ──────────────────────────────────────────────────────────────────

  it("shows 'Upgrade' button when the studio is on an active Free plan", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/plans", () =>
        HttpResponse.json([...PLANS, FREE_PLAN]),
      ),
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_FREE),
      ),
    );
    renderPage();
    expect(await screen.findByRole("button", { name: /^upgrade$/i })).toBeInTheDocument();
  });

  it("hides the 'Cash-billed subscription' card when the studio is on a Free plan", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/plans", () =>
        HttpResponse.json([...PLANS, FREE_PLAN]),
      ),
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_FREE),
      ),
    );
    renderPage();
    await screen.findByText("Active");
    expect(screen.queryByText(/cash-billed subscription/i)).not.toBeInTheDocument();
  });

  it("shows the 'Free plan' info card with an Upgrade CTA when on a Free plan", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/plans", () =>
        HttpResponse.json([...PLANS, FREE_PLAN]),
      ),
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_FREE),
      ),
    );
    renderPage();
    expect(await screen.findByText(/permanent Free plan/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /upgrade plan/i })).toBeInTheDocument();
  });

  it("shows a 'Free' price label (not €0) when on a Free plan", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/plans", () =>
        HttpResponse.json([...PLANS, FREE_PLAN]),
      ),
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_FREE),
      ),
    );
    renderPage();
    await screen.findByText("Active");
    // "Free" appears twice: the plan-name badge + the price line
    expect(screen.getAllByText("Free")).toHaveLength(2);
    expect(screen.queryByText(/€\s?0/)).not.toBeInTheDocument();
  });

  it("does not show a renewal date when on a Free plan", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/plans", () =>
        HttpResponse.json([...PLANS, FREE_PLAN]),
      ),
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_FREE),
      ),
    );
    renderPage();
    await screen.findByText("Active");
    expect(screen.queryByText(/active until/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/next charge/i)).not.toBeInTheDocument();
  });
});
