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
import { SubscribePage } from "@/features/billing/components/SubscribePage";
import type { SubscriptionResponse, PlanResponse } from "@/features/billing/billing.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const PLANS: PlanResponse[] = [
  {
    id:                    "plan-monthly",
    name:                  "Starter",
    billingInterval:       "Monthly",
    priceMonthly:          29,
    priceYearly:           290,
    yearlyDiscountPercent: 17,
    allowBrandingRemoval:  false,
    subscriberCount:       0,
  },
  {
    id:                    "plan-yearly",
    name:                  "Pro Annual",
    billingInterval:       "Yearly",
    priceMonthly:          49,
    priceYearly:           490,
    yearlyDiscountPercent: 17,
    allowBrandingRemoval:  true,
    subscriberCount:       0,
  },
];

const BASE_SUB: SubscriptionResponse = {
  id:                   "sub-0001",
  studioId:             "stud-0001",
  planId:               null,
  pendingPlanId:        null,
  status:               "Trialing",
  trialExpiresAt:       new Date(Date.now() + 7 * 86_400_000).toISOString(),
  currentPeriodEnd:     new Date(Date.now() + 30 * 86_400_000).toISOString(),
  gracePeriodEnd:       new Date(Date.now() + 7 * 86_400_000).toISOString(),
  stripeSubscriptionId: null,
};

const SUB_TRIALING: SubscriptionResponse = { ...BASE_SUB, status: "Trialing" };

const SUB_ACTIVE_CARD: SubscriptionResponse = {
  ...BASE_SUB,
  status:               "Active",
  planId:               "plan-monthly",
  stripeSubscriptionId: "sub_stripe_xxx",
};

const SUB_ACTIVE_CASH: SubscriptionResponse = {
  ...BASE_SUB,
  status:               "Active",
  planId:               "plan-monthly",
  stripeSubscriptionId: null,
};

const SUB_ACTIVE_PENDING: SubscriptionResponse = {
  ...SUB_ACTIVE_CARD,
  pendingPlanId: "plan-yearly",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/billing/plans", () =>
    HttpResponse.json(PLANS),
  ),
  http.get("http://localhost/api/v1/billing/subscription", () =>
    HttpResponse.json(SUB_TRIALING),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                     authReducer,
      ui:                       uiReducer,
      [billingApi.reducerPath]: billingApi.reducer,
    },
    middleware: (gd) => gd().concat(billingApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u3", email: "owner@ink.test" }, token: "fake-token", tenantId: "t1", role: "owner", pendingReferralCode: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false },
    },
  });
}

function renderPage() {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/billing/subscribe"]}>
        <Routes>
          <Route path="/billing/subscribe" element={<SubscribePage />} />
          <Route path="/billing"           element={<div data-testid="billing-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("SubscribePage", () => {

  // --- Error / empty states ---

  it("shows an error message when the plans fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/plans", () =>
        HttpResponse.json({ message: "Server error" }, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText(/failed to load plans/i)).toBeInTheDocument();
  });

  it("shows the no-plans message when the plans list is empty", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/plans", () =>
        HttpResponse.json([]),
      ),
    );
    renderPage();
    expect(await screen.findByText(/no plans available/i)).toBeInTheDocument();
  });

  // --- Billing cycle toggle ---

  it("renders Monthly and Yearly toggle buttons", async () => {
    renderPage();
    await screen.findByRole("button", { name: /^monthly/i });
    expect(screen.getByRole("button", { name: /^monthly/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^yearly/i })).toBeInTheDocument();
  });

  it("defaults to the Monthly billing cycle", async () => {
    renderPage();
    await screen.findByText("Starter");
    expect(screen.getByRole("button", { name: /^monthly/i })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("button", { name: /^yearly/i })).toHaveAttribute("aria-pressed", "false");
  });

  it("Yearly toggle button always shows the discount badge", async () => {
    renderPage();
    await screen.findByRole("button", { name: /^monthly/i });
    expect(screen.getByRole("button", { name: /^yearly/i })).toHaveTextContent(/save 17%/i);
  });

  // --- Plan listing ---

  it("shows only monthly plans by default", async () => {
    renderPage();
    expect(await screen.findByText("Starter")).toBeInTheDocument();
    expect(screen.queryByText("Pro Annual")).not.toBeInTheDocument();
  });

  it("shows only yearly plans after clicking the Yearly toggle", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("button", { name: /^monthly/i });

    await user.click(screen.getByRole("button", { name: /^yearly/i }));

    expect(await screen.findByText("Pro Annual")).toBeInTheDocument();
    expect(screen.queryByText("Starter")).not.toBeInTheDocument();
  });

  it("shows Billed monthly label on monthly plan cards", async () => {
    renderPage();
    await screen.findByText("Starter");
    expect(screen.getByText("Billed monthly")).toBeInTheDocument();
  });

  it("shows Billed yearly label on yearly plan cards after switching cycle", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("button", { name: /^monthly/i });

    await user.click(screen.getByRole("button", { name: /^yearly/i }));
    await screen.findByText("Pro Annual");

    expect(screen.getByText("Billed yearly")).toBeInTheDocument();
  });

  it("shows the per-month breakdown on yearly plan cards", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("button", { name: /^monthly/i });

    await user.click(screen.getByRole("button", { name: /^yearly/i }));
    await screen.findByText("Pro Annual");

    // $490 / 12 ≈ $40.83 shown in the plan card (not the toggle badge)
    expect(screen.getByText(/\$40\.83\/mo/i)).toBeInTheDocument();
    expect(screen.getByText(/save 17%/i, { selector: "p" })).toBeInTheDocument();
  });

  it("monthly plan cards show no per-month breakdown line", async () => {
    renderPage();
    await screen.findByText("Starter");
    // No "save X%"  inside a <p> (only the toggle badge which is inside a <span> in a <button>)
    expect(screen.queryByText(/save \d+%/i, { selector: "p" })).not.toBeInTheDocument();
  });

  it("switching billing cycle resets the plan selection", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("button", { name: /^monthly/i });

    await user.click(screen.getByRole("button", { name: /^yearly/i }));
    await screen.findByText("Pro Annual");
    await user.click(screen.getByRole("button", { name: /pro annual/i }));
    expect(screen.getByText("Selected")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /^monthly/i }));
    await screen.findByText("Starter");

    expect(screen.queryByText("Selected")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /continue to checkout/i })).toBeDisabled();
  });

  it("shows a no-plans message when no plans exist for the selected cycle", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/plans", () =>
        HttpResponse.json([PLANS[1]]), // only the yearly plan
      ),
    );
    const user = userEvent.setup();
    renderPage();
    // Monthly is default — no monthly plans in this fixture
    expect(await screen.findByText(/no monthly plans available/i)).toBeInTheDocument();
    // Switch to yearly → Pro Annual appears
    await user.click(screen.getByRole("button", { name: /^yearly/i }));
    expect(await screen.findByText("Pro Annual")).toBeInTheDocument();
  });

  // --- Plan selection ---

  it("submit button is disabled before any plan is selected", async () => {
    renderPage();
    const button = await screen.findByRole("button", { name: /continue to checkout/i });
    expect(button).toBeDisabled();
  });

  it("submit button is enabled after a plan is selected", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Starter");

    await user.click(screen.getByRole("button", { name: /starter/i }));

    expect(screen.getByRole("button", { name: /continue to checkout/i })).not.toBeDisabled();
  });

  it("shows the Selected indicator on the chosen plan card", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Starter");

    await user.click(screen.getByRole("button", { name: /starter/i }));

    expect(screen.getByText("Selected")).toBeInTheDocument();
  });

  // --- Header & context text ---

  it("renders the Choose a Plan header for non-active subscriptions", async () => {
    renderPage();
    expect(await screen.findByText("Choose a Plan")).toBeInTheDocument();
  });

  it("shows contextual description for non-active users", async () => {
    renderPage();
    expect(await screen.findByText(/select a plan to unlock full access/i)).toBeInTheDocument();
  });

  it("back button navigates to /billing", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Starter");

    await user.click(screen.getByRole("button", { name: /billing/i }));

    expect(screen.getByTestId("billing-page")).toBeInTheDocument();
  });

  // --- Trialing user → Stripe Checkout flow ---

  it("renders the Continue to checkout button for Trialing users", async () => {
    renderPage();
    expect(await screen.findByRole("button", { name: /continue to checkout/i })).toBeInTheDocument();
  });

  it("calls createCheckout with the selected planId", async () => {
    const checkoutSpy = vi.fn();
    server.use(
      http.post(
        "http://localhost/api/v1/billing/subscription/checkout",
        async ({ request }) => {
          const body = await request.json();
          checkoutSpy(body);
          return HttpResponse.json({ url: "https://checkout.stripe.com/test" });
        },
      ),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Starter");

    await user.click(screen.getByRole("button", { name: /starter/i }));
    await user.click(screen.getByRole("button", { name: /continue to checkout/i }));

    await waitFor(() =>
      expect(checkoutSpy).toHaveBeenCalledWith(
        expect.objectContaining({ planId: "plan-monthly" }),
      ),
    );
  });

  it("shows the server error message when createCheckout fails with a message", async () => {
    server.use(
      http.post(
        "http://localhost/api/v1/billing/subscription/checkout",
        () =>
          HttpResponse.json({ message: "Checkout unavailable." }, { status: 500 }),
      ),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Starter");

    await user.click(screen.getByRole("button", { name: /starter/i }));
    await user.click(screen.getByRole("button", { name: /continue to checkout/i }));

    expect(await screen.findByText("Checkout unavailable.")).toBeInTheDocument();
  });

  it("shows a generic error when createCheckout fails without a message body", async () => {
    server.use(
      http.post(
        "http://localhost/api/v1/billing/subscription/checkout",
        () => HttpResponse.json({}, { status: 500 }),
      ),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Starter");

    await user.click(screen.getByRole("button", { name: /starter/i }));
    await user.click(screen.getByRole("button", { name: /continue to checkout/i }));

    expect(await screen.findByText(/could not start checkout/i)).toBeInTheDocument();
  });

  // --- Card-billed active user → changePlan flow ---

  it("renders the Change Plan header for card-billed active users", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_CARD),
      ),
    );
    renderPage();
    expect(await screen.findByText("Change Plan")).toBeInTheDocument();
  });

  it("shows the Switch plan button for card-billed active users", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_CARD),
      ),
    );
    renderPage();
    expect(await screen.findByRole("button", { name: /switch plan/i })).toBeInTheDocument();
  });

  it("marks the current plan card as disabled for card-billed active users", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_CARD),
      ),
    );
    renderPage();
    await screen.findByText("Current plan");
    // The Starter button is the current plan → disabled
    expect(screen.getByRole("button", { name: /starter/i })).toBeDisabled();
  });

  it("shows the change-plan contextual description for card-billed active users", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_CARD),
      ),
    );
    renderPage();
    expect(await screen.findByText(/upgrades apply immediately/i)).toBeInTheDocument();
  });

  it("calls changePlan with the selected planId and navigates to /billing", async () => {
    const changeSpy = vi.fn();
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_CARD),
      ),
      http.put(
        "http://localhost/api/v1/billing/subscription/plan",
        async ({ request }) => {
          const body = await request.json();
          changeSpy(body);
          return HttpResponse.json({ ...SUB_ACTIVE_CARD, planId: "plan-yearly", pendingPlanId: null });
        },
      ),
    );

    const user = userEvent.setup();
    renderPage();
    // Card-billed: Starter is current (monthly). Switch to Yearly to see Pro Annual.
    await screen.findByRole("button", { name: /^monthly/i });
    await user.click(screen.getByRole("button", { name: /^yearly/i }));
    await screen.findByText("Pro Annual");

    await user.click(screen.getByRole("button", { name: /pro annual/i }));
    await user.click(screen.getByRole("button", { name: /switch plan/i }));

    await waitFor(() =>
      expect(changeSpy).toHaveBeenCalledWith({ planId: "plan-yearly" }),
    );
    await screen.findByTestId("billing-page");
  });

  it("shows the server error message when changePlan fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_CARD),
      ),
      http.put(
        "http://localhost/api/v1/billing/subscription/plan",
        () =>
          HttpResponse.json({ message: "Plan switch failed." }, { status: 400 }),
      ),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("button", { name: /^monthly/i });
    await user.click(screen.getByRole("button", { name: /^yearly/i }));
    await screen.findByText("Pro Annual");

    await user.click(screen.getByRole("button", { name: /pro annual/i }));
    await user.click(screen.getByRole("button", { name: /switch plan/i }));

    expect(await screen.findByText("Plan switch failed.")).toBeInTheDocument();
  });

  it("shows a generic error when changePlan fails without a message", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_CARD),
      ),
      http.put(
        "http://localhost/api/v1/billing/subscription/plan",
        () => HttpResponse.json({}, { status: 400 }),
      ),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("button", { name: /^monthly/i });
    await user.click(screen.getByRole("button", { name: /^yearly/i }));
    await screen.findByText("Pro Annual");

    await user.click(screen.getByRole("button", { name: /pro annual/i }));
    await user.click(screen.getByRole("button", { name: /switch plan/i }));

    expect(await screen.findByText(/failed to change plan/i)).toBeInTheDocument();
  });

  // --- Cash-billed active user ---

  it("renders the Set up card billing header for cash-billed active users", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_CASH),
      ),
    );
    renderPage();
    expect(await screen.findByText("Set up card billing")).toBeInTheDocument();
  });

  it("shows the checkout flow (not changePlan) for cash-billed active users", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_CASH),
      ),
    );
    renderPage();
    await screen.findByText("Set up card billing");
    expect(screen.getByRole("button", { name: /continue to checkout/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /switch plan/i })).not.toBeInTheDocument();
  });

  it("shows the cash switch contextual description for cash-billed active users", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_CASH),
      ),
    );
    renderPage();
    expect(await screen.findByText(/switch from cash to automatic card billing/i)).toBeInTheDocument();
  });

  // --- Pending plan change gate ---

  it("shows the pending-change warning card when pendingPlanId is set", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_PENDING),
      ),
    );
    renderPage();
    expect(await screen.findByText(/plan change is already scheduled/i)).toBeInTheDocument();
  });

  it("hides plan cards and submit button when there is a pending change", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_PENDING),
      ),
    );
    renderPage();
    await screen.findByText(/plan change is already scheduled/i);
    expect(screen.queryByRole("button", { name: /starter/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /switch plan/i })).not.toBeInTheDocument();
  });

  // --- Cash info section ---

  it("shows the cash payment info section when not Active", async () => {
    renderPage();
    await screen.findByText("Starter");
    expect(screen.getByText(/prefer to pay cash/i)).toBeInTheDocument();
  });

  it("hides the cash payment info section when Active", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_ACTIVE_CARD),
      ),
    );
    renderPage();
    await screen.findByText("Change Plan");
    expect(screen.queryByText(/prefer to pay cash/i)).not.toBeInTheDocument();
  });
});
