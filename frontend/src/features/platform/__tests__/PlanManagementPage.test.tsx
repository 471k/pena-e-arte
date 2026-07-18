import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { billingApi } from "@/features/billing/billingApi";
import { PlanManagementPage } from "@/features/platform/components/PlanManagementPage";
import type { PlanResponse } from "@/features/billing/billing.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

// Shared defaults for the new limit/feature fields — tests below only care about the
// fields they explicitly set, so every fixture spreads this in.
const NO_LIMITS = {
  maxArtists:               null,
  maxAppointmentsPerMonth:  null,
  maxNotificationsPerMonth: null,
  maxStorageGb:             null,
  maxLocations:             null,
  allowApiAccess:           false,
  prioritySupport:          false,
  pairedPlanId:             null,
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
    stripePriceIdMonthly:  "price_monthly_starter",
    stripePriceIdYearly:   null,
    subscriberCount:       4,
    ...NO_LIMITS,
  },
  {
    id:                    "plan-2",
    name:                  "Pro",
    billingInterval:       "Yearly",
    priceMonthly:          49,
    priceYearly:           490,
    yearlyDiscountPercent: 17,
    allowBrandingRemoval:  true,
    stripePriceIdMonthly:  null,
    stripePriceIdYearly:   "price_yearly_pro",
    subscriberCount:       0,
    ...NO_LIMITS,
  },
];

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
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
      auth:                       authReducer,
      [billingApi.reducerPath]:   billingApi.reducer,
    },
    middleware: (gd) => gd().concat(billingApi.middleware),
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
        <PlanManagementPage />
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("PlanManagementPage", () => {

  it("shows skeleton cards while plans are loading", () => {
    renderPage();
    expect(document.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
    expect(screen.queryByText("Loading…")).not.toBeInTheDocument();
  });

  it("renders the Plans header", async () => {
    renderPage();
    expect(await screen.findByText("Plans")).toBeInTheDocument();
  });

  it("renders all plan names after loading", async () => {
    renderPage();
    expect(await screen.findByText("Starter")).toBeInTheDocument();
    expect(screen.getByText("Pro")).toBeInTheDocument();
  });

  it("shows 'White-label' badge for plans with allowBrandingRemoval", async () => {
    renderPage();
    await screen.findByText("Pro");
    expect(screen.getByText("White-label")).toBeInTheDocument();
    expect(screen.queryByText("no-branding")).not.toBeInTheDocument();
  });

  it("renders the New plan button", async () => {
    renderPage();
    expect(await screen.findByRole("button", { name: /^new plan$/i })).toBeInTheDocument();
  });

  it("clicking New plan shows the create form", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("button", { name: /^new plan$/i });

    await user.click(screen.getByRole("button", { name: /^new plan$/i }));

    expect(screen.getByLabelText(/^name$/i)).toBeInTheDocument();
    expect(screen.getByLabelText("Monthly price (€)")).toBeInTheDocument();
    expect(screen.getByLabelText("Yearly price (€)")).toBeInTheDocument();
  });

  it("shows Stripe price ID fields in the form", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("button", { name: /^new plan$/i });

    await user.click(screen.getByRole("button", { name: /^new plan$/i }));

    expect(screen.getByLabelText(/stripe monthly price id/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/stripe yearly price id/i)).toBeInTheDocument();
  });

  it("calls POST /billing/plans with the correct body", async () => {
    const createSpy = vi.fn();
    server.use(
      http.post("http://localhost/api/v1/billing/plans", async ({ request }) => {
        const body = await request.json();
        createSpy(body);
        return HttpResponse.json({ id: "plan-new", ...(body as object), subscriberCount: 0 });
      }),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("button", { name: /^new plan$/i });
    await user.click(screen.getByRole("button", { name: /^new plan$/i }));

    await user.clear(screen.getByLabelText(/^name$/i));
    await user.type(screen.getByLabelText(/^name$/i), "Enterprise");
    await user.clear(screen.getByLabelText("Monthly price (€)"));
    await user.type(screen.getByLabelText("Monthly price (€)"), "99");
    await user.clear(screen.getByLabelText("Yearly price (€)"));
    await user.type(screen.getByLabelText("Yearly price (€)"), "990");

    await user.click(screen.getByRole("button", { name: /^save$/i }));

    await waitFor(() => expect(createSpy).toHaveBeenCalledOnce());
    expect(createSpy.mock.calls[0][0]).toMatchObject({
      name:         "Enterprise",
      priceMonthly: 99,
      priceYearly:  990,
    });
  });

  it("preserves existing Stripe price IDs in edit form defaults", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Starter");

    await user.click(screen.getByRole("button", { name: /edit starter plan/i }));

    const monthlyInput = screen.getByLabelText(/stripe monthly price id/i);
    expect((monthlyInput as HTMLInputElement).value).toBe("price_monthly_starter");
  });

  it("calls PUT /billing/plans/:id and sends Stripe IDs in the body", async () => {
    const updateSpy = vi.fn();
    server.use(
      http.put("http://localhost/api/v1/billing/plans/plan-1", async ({ request }) => {
        const body = await request.json();
        updateSpy(body);
        return HttpResponse.json({ ...PLANS[0], ...(body as object) });
      }),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Starter");

    await user.click(screen.getByRole("button", { name: /edit starter plan/i }));
    await user.click(screen.getByRole("button", { name: /^save$/i }));

    await waitFor(() => expect(updateSpy).toHaveBeenCalledOnce());
    expect(updateSpy.mock.calls[0][0]).toMatchObject({
      stripePriceIdMonthly: "price_monthly_starter",
    });
  });

  it("shows delete confirmation on trash button click", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Starter");

    await user.click(screen.getByRole("button", { name: /delete starter plan/i }));

    expect(screen.getByText(/delete "starter" permanently\?/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /yes, delete/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /cancel/i })).toBeInTheDocument();
  });

  it("cancels delete when clicking Cancel", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Starter");

    await user.click(screen.getByRole("button", { name: /delete starter plan/i }));
    await user.click(screen.getByRole("button", { name: /cancel/i }));

    expect(screen.queryByText(/delete "starter" permanently\?/i)).not.toBeInTheDocument();
  });

  it("calls DELETE /billing/plans/:id on confirm", async () => {
    const deleteSpy = vi.fn();
    server.use(
      http.delete("http://localhost/api/v1/billing/plans/plan-1", () => {
        deleteSpy();
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Starter");

    await user.click(screen.getByRole("button", { name: /delete starter plan/i }));
    await user.click(screen.getByRole("button", { name: /yes, delete/i }));

    await waitFor(() => expect(deleteSpy).toHaveBeenCalledOnce());
  });

  it("shows error state when plans fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/plans", () =>
        HttpResponse.json({ message: "Server error" }, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText(/failed to load plans/i)).toBeInTheDocument();
  });

  it("shows empty state when no plans exist", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/plans", () =>
        HttpResponse.json([]),
      ),
    );
    renderPage();
    expect(await screen.findByText(/no plans yet/i)).toBeInTheDocument();
  });

  it("shows subscriber count badge on plan cards", async () => {
    renderPage();
    await screen.findByText("Starter");
    expect(screen.getByText("4")).toBeInTheDocument();
    expect(screen.getAllByText("0").length).toBeGreaterThan(0);
  });

  it("shows subscriber warning in delete dialog when plan has subscribers", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Starter");

    await user.click(screen.getByRole("button", { name: /delete starter plan/i }));

    // "4 studios" is in a <strong> element; "are on this plan" is a sibling text node
    expect(screen.getByText("4 studios")).toBeInTheDocument();
    expect(screen.getByText(/are on this plan/i)).toBeInTheDocument();
  });

  it("shows safe-to-delete message when plan has no subscribers", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Pro");

    await user.click(screen.getByRole("button", { name: /delete pro plan/i }));

    expect(screen.getByText(/no active subscribers/i)).toBeInTheDocument();
  });

  it("shows computed savings badge using actual prices", async () => {
    renderPage();
    await screen.findByText("Starter");
    // Seed: Starter priceMonthly=29, priceYearly=290 → computed = Math.round((1-290/348)*100) = 17%
    //       Pro    priceMonthly=49, priceYearly=490 → computed = Math.round((1-490/588)*100) = 17%
    // Both match — two badges should appear
    expect(screen.getAllByText(/save 17% annually/i).length).toBe(2);
    // Confirm the old "vs monthly billing" copy is gone
    expect(screen.queryByText(/vs monthly billing/i)).not.toBeInTheDocument();
  });

  it("shows 'White-label' badge for plans with allowBrandingRemoval", async () => {
    renderPage();
    await screen.findByText("Pro");
    expect(screen.getByText("White-label")).toBeInTheDocument();
    expect(screen.queryByText("no-branding")).not.toBeInTheDocument();
  });

  it("clicking empty state CTA opens the create form", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/plans", () =>
        HttpResponse.json([]),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await screen.findByText(/no plans yet/i);

    await user.click(screen.getByRole("button", { name: /create first plan/i }));

    expect(screen.getByLabelText(/^name$/i)).toBeInTheDocument();
  });

  // ── Fix #1: Computed savings badge ──────────────────────────────────────────

  it("badge shows computed savings even when yearlyDiscountPercent is wrong", async () => {
    // Simulate the Premium bug: stored discount=17% but actual math gives 44%
    server.use(
      http.get("http://localhost/api/v1/billing/plans", () =>
        HttpResponse.json([{
          id:                    "plan-premium",
          name:                  "Premium",
          billingInterval:       "Yearly",
          priceMonthly:          30,
          priceYearly:           200,        // 200 / (30*12) = 55.6% of monthly → 44% saving
          yearlyDiscountPercent: 17,         // stored value is wrong
          allowBrandingRemoval:  false,
          stripePriceIdMonthly:  null,
          stripePriceIdYearly:   null,
          subscriberCount:       1,
          ...NO_LIMITS,
        }]),
      ),
    );
    renderPage();
    await screen.findByText("Premium");
    // Badge should show computed 44%, NOT stored 17%
    expect(screen.getByText(/save 44% annually/i)).toBeInTheDocument();
    expect(screen.queryByText(/save 17%/i)).not.toBeInTheDocument();
  });

  it("does not show savings badge when yearly price is not cheaper than 12x monthly", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/plans", () =>
        HttpResponse.json([{
          id:                    "plan-odd",
          name:                  "Odd",
          billingInterval:       "Monthly",
          priceMonthly:          10,
          priceYearly:           130,        // 130 vs 120 → actually MORE expensive yearly
          yearlyDiscountPercent: 8,          // stored value claims a discount
          allowBrandingRemoval:  false,
          stripePriceIdMonthly:  null,
          stripePriceIdYearly:   null,
          subscriberCount:       0,
          ...NO_LIMITS,
        }]),
      ),
    );
    renderPage();
    await screen.findByText("Odd");
    // computedSavingsPct = Math.round((1 - 130/120) * 100) = Math.round(-0.083*100) = -8 < 0 → no badge
    expect(screen.queryByText(/save/i)).not.toBeInTheDocument();
  });

  it("badge copy never contains 'vs monthly billing'", async () => {
    renderPage();
    await screen.findByText("Starter");
    expect(screen.queryByText(/vs monthly billing/i)).not.toBeInTheDocument();
  });

  // ── Fix #2: Card header layout ───────────────────────────────────────────────

  it("plan name and billing interval are in separate DOM elements", async () => {
    renderPage();
    await screen.findByText("Pro");
    // Plan name "Pro" should be in a <p> element
    const nameEl = screen.getByText("Pro", { selector: "p" });
    expect(nameEl).toBeInTheDocument();
    // Billing label should NOT be inside the same element as the name
    // (it's a sibling <span>, not a child of the <p>)
    expect(nameEl.textContent).toBe("Pro");
    expect(nameEl.textContent).not.toContain("Billing");
  });

  // ── Fix #3: Trash icon destructive color ─────────────────────────────────────

  it("delete button uses text-red class, not text-destructive (dark-mode contrast fix)", async () => {
    renderPage();
    await screen.findByText("Starter");
    const deleteBtn = screen.getByRole("button", { name: /delete starter plan/i });
    expect(deleteBtn.className).toMatch(/text-red-/);
    expect(deleteBtn.className).not.toMatch(/text-destructive/);
  });

  it("edit button does NOT have destructive color (it stays neutral)", async () => {
    renderPage();
    await screen.findByText("Starter");
    const editBtn = screen.getByRole("button", { name: /edit starter plan/i });
    expect(editBtn.className).not.toMatch(/text-destructive/);
  });

  // ── Fix #4: Ghost tile ────────────────────────────────────────────────────────

  it("ghost 'New plan' tile always appears regardless of plan count modulo 3", async () => {
    // PLANS seed has 2 plans (2 % 3 !== 0)
    renderPage();
    await screen.findByText("Starter");
    expect(screen.getByRole("button", { name: /add a new plan/i })).toBeInTheDocument();
  });

  it("ghost tile appears even when plan count is a multiple of 3", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/plans", () =>
        HttpResponse.json([
          ...PLANS,
          {
            id:                    "plan-3",
            name:                  "Enterprise",
            billingInterval:       "Monthly",
            priceMonthly:          99,
            priceYearly:           990,
            yearlyDiscountPercent: 17,
            allowBrandingRemoval:  true,
            stripePriceIdMonthly:  null,
            stripePriceIdYearly:   null,
            subscriberCount:       0,
            ...NO_LIMITS,
          },
        ]),
      ),
    );
    renderPage();
    await screen.findByText("Enterprise");
    // 3 plans → 3 % 3 === 0 → OLD logic would hide ghost; NEW logic always shows it
    expect(screen.getByRole("button", { name: /add a new plan/i })).toBeInTheDocument();
  });

  it("ghost tile visible text is 'New plan', not 'Add plan'", async () => {
    renderPage();
    await screen.findByText("Starter");
    const ghostTile = screen.getByRole("button", { name: /add a new plan/i });
    expect(ghostTile.textContent?.trim()).toBe("New plan");
  });

  it("clicking ghost tile opens the create form", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Starter");

    const ghostTile = screen.getByRole("button", { name: /add a new plan/i });
    await user.click(ghostTile);

    expect(screen.getByLabelText(/^name$/i)).toBeInTheDocument();
  });

  // ── Fix #5: Subscriber count accessibility ────────────────────────────────────

  it("subscriber count has an accessible aria-label describing plan name and count", async () => {
    renderPage();
    await screen.findByText("Starter");
    // Check the aria-label directly
    const spans = document.querySelectorAll('[aria-label*="studios subscribed to"]');
    expect(spans.length).toBe(2); // One for Starter, one for Pro
    const starterSpan = document.querySelector('[aria-label*="subscribed to Starter"]');
    expect(starterSpan?.getAttribute("aria-label")).toMatch(/4 studios subscribed to Starter/i);
  });

  // ── Fix #2: Dual-price display ──────────────────────────────────────────────

  it("Monthly plan shows monthly price prominently and yearly as reference", async () => {
    renderPage();
    await screen.findByText("Starter");
    // Monthly price displayed normally — no "ref." suffix
    // Yearly price shown with "ref." marker indicating it's not the charged price
    const refText = document.querySelector('[title*="Reference only"]');
    expect(refText).not.toBeNull();
    expect(refText?.textContent).toMatch(/\/yr ref\./);
  });

  it("Yearly plan shows yearly price prominently and monthly as reference", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/plans", () =>
        HttpResponse.json([{
          id:                    "plan-yearly",
          name:                  "Annual",
          billingInterval:       "Yearly",
          priceMonthly:          30,
          priceYearly:           200,
          yearlyDiscountPercent: 44,
          allowBrandingRemoval:  false,
          stripePriceIdMonthly:  null,
          stripePriceIdYearly:   "price_yearly_annual",
          subscriberCount:       0,
          ...NO_LIMITS,
        }]),
      ),
    );
    renderPage();
    await screen.findByText("Annual");
    // The reference entry should be the /mo price
    const refText = document.querySelector('[title*="Reference only"]');
    expect(refText?.textContent).toMatch(/\/mo ref\./);
  });

  it("'ref.' prices appear in a muted element, not a prominent one", async () => {
    renderPage();
    await screen.findByText("Starter");
    const refEl = document.querySelector('[title*="Reference only"]');
    // muted-foreground/50 + text-[11px] = clearly secondary
    expect(refEl?.className).toMatch(/muted-foreground/);
  });

  // ── Fix #4: "New plan" is primary action ────────────────────────────────────

  it("'New plan' header button is a solid/filled button (variant default)", async () => {
    renderPage();
    await screen.findByText("Starter");
    // Header button "New plan" (not "Add a new plan" which is the ghost tile)
    const headerBtn = screen.getAllByRole("button", { name: /^new plan$/i })
      .find(b => b.closest("header"));
    expect(headerBtn).toBeTruthy();
    // Default shadcn variant uses bg-primary — check for bg- class
    expect(headerBtn?.className).toMatch(/bg-primary|bg-\[hsl/);
    // Outline variant is "border border-input bg-background …" — that class must be absent
    expect(headerBtn?.className).not.toMatch(/border-input/);
  });

  // ── Fix #6: Billing interval label ─────────────────────────────────────────

  it("Monthly plan shows 'Billed monthly' label", async () => {
    renderPage();
    await screen.findByText("Starter");
    expect(screen.getByText("Billed monthly")).toBeInTheDocument();
  });

  it("Yearly plan shows 'Billed yearly only' label", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/plans", () =>
        HttpResponse.json([{
          id:                    "plan-yearly",
          name:                  "Annual",
          billingInterval:       "Yearly",
          priceMonthly:          30,
          priceYearly:           200,
          yearlyDiscountPercent: 44,
          allowBrandingRemoval:  false,
          stripePriceIdMonthly:  null,
          stripePriceIdYearly:   "price_yr",
          subscriberCount:       0,
          ...NO_LIMITS,
        }]),
      ),
    );
    renderPage();
    await screen.findByText("Annual");
    expect(screen.getByText("Billed yearly only")).toBeInTheDocument();
  });

  it("'Billing: Monthly' text no longer appears anywhere on the page", async () => {
    renderPage();
    await screen.findByText("Starter");
    expect(screen.queryByText(/billing: monthly/i)).not.toBeInTheDocument();
  });

  // ── Fix #3: Icon cluster hit targets ───────────────────────────────────────

  it("edit and delete buttons are h-8 w-8 (32px), not h-7 w-7 (28px)", async () => {
    renderPage();
    await screen.findByText("Starter");
    const editBtn = screen.getByRole("button", { name: /edit starter plan/i });
    const deleteBtn = screen.getByRole("button", { name: /delete starter plan/i });
    // Both should have h-8 class
    expect(editBtn.className).toMatch(/\bh-8\b/);
    expect(deleteBtn.className).toMatch(/\bh-8\b/);
    // Neither should have h-7
    expect(editBtn.className).not.toMatch(/\bh-7\b/);
    expect(deleteBtn.className).not.toMatch(/\bh-7\b/);
  });
});
