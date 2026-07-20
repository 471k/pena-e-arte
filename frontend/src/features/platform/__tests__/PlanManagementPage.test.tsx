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
import { toast } from "sonner";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

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
};

const PLANS: PlanResponse[] = [
  {
    id:                    "plan-1",
    name:                  "Starter",
    yearlyDiscountPercent: 17,
    allowBrandingRemoval:  false,
    subscriberCount:       4,
    ...NO_LIMITS,
    prices: [
      { id: "price-1-m", interval: "Monthly", price: 29, stripePriceId: "price_monthly_starter", isActive: true },
    ],
  },
  {
    id:                    "plan-2",
    name:                  "Pro",
    yearlyDiscountPercent: 17,
    allowBrandingRemoval:  true,
    subscriberCount:       0,
    ...NO_LIMITS,
    prices: [
      { id: "price-2-m", interval: "Monthly", price: 49, stripePriceId: null, isActive: true },
      { id: "price-2-y", interval: "Yearly", price: 490, stripePriceId: "price_yearly_pro", isActive: true },
    ],
  },
];

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/billing/plans", () =>
    HttpResponse.json(PLANS),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); vi.clearAllMocks(); });
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
  });

  it("renders the New plan button", async () => {
    renderPage();
    expect(await screen.findByRole("link", { name: /^new plan$/i })).toBeInTheDocument();
  });

  it("'New plan' header button links to /platform/plans/new", async () => {
    renderPage();
    const link = await screen.findByRole("link", { name: /^new plan$/i });
    expect(link).toHaveAttribute("href", "/platform/plans/new");
  });

  it("edit icon links to the dedicated edit page for that plan", async () => {
    renderPage();
    await screen.findByText("Starter");
    const editLink = screen.getByRole("link", { name: /edit starter plan/i });
    expect(editLink).toHaveAttribute("href", "/platform/plans/plan-1/edit");
  });

  it("no form fields (Name, price inputs) are rendered on the management page", async () => {
    renderPage();
    await screen.findByText("Starter");
    expect(screen.queryByLabelText(/^name$/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/monthly price/i)).not.toBeInTheDocument();
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

  it("a failed delete shows the backend's specific error message, not a generic one", async () => {
    server.use(
      http.delete("http://localhost/api/v1/billing/plans/plan-1", () =>
        HttpResponse.json(
          { message: "Cannot delete a plan that has active subscriptions." },
          { status: 409 },
        )),
    );
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Starter");

    await user.click(screen.getByRole("button", { name: /delete starter plan/i }));
    await user.click(screen.getByRole("button", { name: /yes, delete/i }));

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith(
        "Cannot delete a plan that has active subscriptions.",
      ));
    expect(toast.success).not.toHaveBeenCalled();
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

  it("shows computed savings badge for a plan with both Monthly and Yearly prices", async () => {
    renderPage();
    await screen.findByText("Pro");
    // Pro: monthly=49, yearly=490 → computed = Math.round((1-490/588)*100) = 17%
    expect(screen.getByText(/save 17% annually/i)).toBeInTheDocument();
  });

  it("does not show a savings badge for a Monthly-only plan", async () => {
    renderPage();
    await screen.findByText("Starter");
    const starterCard = screen.getByText("Starter", { selector: "p" }).closest(".space-y-3");
    expect(starterCard?.textContent).not.toMatch(/save \d+% annually/i);
  });

  it("clicking empty state CTA links to /platform/plans/new", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/plans", () =>
        HttpResponse.json([]),
      ),
    );
    renderPage();
    await screen.findByText(/no plans yet/i);

    const link = screen.getByRole("link", { name: /create first plan/i });
    expect(link).toHaveAttribute("href", "/platform/plans/new");
  });

  // ── Card header layout ───────────────────────────────────────────────

  it("plan name and billing interval label are in separate DOM elements", async () => {
    renderPage();
    await screen.findByText("Pro");
    const nameEl = screen.getByText("Pro", { selector: "p" });
    expect(nameEl.textContent).toBe("Pro");
    expect(nameEl.textContent).not.toContain("Billing");
  });

  // ── Trash icon destructive color ─────────────────────────────────────

  it("delete button uses text-red class, not text-destructive (dark-mode contrast fix)", async () => {
    renderPage();
    await screen.findByText("Starter");
    const deleteBtn = screen.getByRole("button", { name: /delete starter plan/i });
    expect(deleteBtn.className).toMatch(/text-red-/);
    expect(deleteBtn.className).not.toMatch(/text-destructive/);
  });

  it("edit link does NOT have destructive color (it stays neutral)", async () => {
    renderPage();
    await screen.findByText("Starter");
    const editLink = screen.getByRole("link", { name: /edit starter plan/i });
    expect(editLink.className).not.toMatch(/text-destructive/);
  });

  // ── Ghost tile ────────────────────────────────────────────────────────

  it("ghost 'New plan' tile always appears regardless of plan count modulo 3", async () => {
    renderPage();
    await screen.findByText("Starter");
    expect(screen.getByRole("link", { name: /add a new plan/i })).toBeInTheDocument();
  });

  it("ghost tile appears even when plan count is a multiple of 3", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/plans", () =>
        HttpResponse.json([
          ...PLANS,
          {
            id:                    "plan-3",
            name:                  "Enterprise",
            yearlyDiscountPercent: 17,
            allowBrandingRemoval:  true,
            subscriberCount:       0,
            ...NO_LIMITS,
            prices: [
              { id: "price-3-m", interval: "Monthly", price: 99, stripePriceId: null, isActive: true },
            ],
          },
        ]),
      ),
    );
    renderPage();
    await screen.findByText("Enterprise");
    expect(screen.getByRole("link", { name: /add a new plan/i })).toBeInTheDocument();
  });

  it("ghost tile visible text is 'New plan', not 'Add plan'", async () => {
    renderPage();
    await screen.findByText("Starter");
    const ghostTile = screen.getByRole("link", { name: /add a new plan/i });
    expect(ghostTile.textContent?.trim()).toBe("New plan");
  });

  it("ghost tile links to /platform/plans/new", async () => {
    renderPage();
    await screen.findByText("Starter");

    const ghostTile = screen.getByRole("link", { name: /add a new plan/i });
    expect(ghostTile).toHaveAttribute("href", "/platform/plans/new");
  });

  // ── Subscriber count accessibility ────────────────────────────────────

  it("subscriber count has an accessible aria-label describing plan name and count", async () => {
    renderPage();
    await screen.findByText("Starter");
    const spans = document.querySelectorAll('[aria-label*="studios subscribed to"]');
    expect(spans.length).toBe(2); // One for Starter, one for Pro
    const starterSpan = document.querySelector('[aria-label*="subscribed to Starter"]');
    expect(starterSpan?.getAttribute("aria-label")).toMatch(/4 studios subscribed to Starter/i);
  });

  // ── Price display ──────────────────────────────────────────────────

  it("shows both Monthly and Yearly prices for a dual-interval plan, both prominent (no 'ref.' marker)", async () => {
    renderPage();
    await screen.findByText("Pro");
    expect(document.querySelector('[title*="Reference only"]')).toBeNull();
  });

  it("Monthly-only plan shows 'Billed monthly' label", async () => {
    renderPage();
    await screen.findByText("Starter");
    expect(screen.getByText("Billed monthly")).toBeInTheDocument();
  });

  it("dual-interval plan shows 'Monthly & yearly' label", async () => {
    renderPage();
    await screen.findByText("Pro");
    expect(screen.getByText("Monthly & yearly")).toBeInTheDocument();
  });

  it("Yearly-only plan shows 'Billed yearly only' label", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/plans", () =>
        HttpResponse.json([{
          id:                    "plan-yearly",
          name:                  "Annual",
          yearlyDiscountPercent: 44,
          allowBrandingRemoval:  false,
          subscriberCount:       0,
          ...NO_LIMITS,
          prices: [
            { id: "price-yearly", interval: "Yearly", price: 200, stripePriceId: "price_yr", isActive: true },
          ],
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

  // ── Icon cluster hit targets ───────────────────────────────────────

  it("edit and delete controls are h-8 w-8 (32px), not h-7 w-7 (28px)", async () => {
    renderPage();
    await screen.findByText("Starter");
    const editLink = screen.getByRole("link", { name: /edit starter plan/i });
    const deleteBtn = screen.getByRole("button", { name: /delete starter plan/i });
    expect(editLink.className).toMatch(/\bh-8\b/);
    expect(deleteBtn.className).toMatch(/\bh-8\b/);
    expect(editLink.className).not.toMatch(/\bh-7\b/);
    expect(deleteBtn.className).not.toMatch(/\bh-7\b/);
  });

  // ── New plan button primary styling ─────────────────────────────────

  it("'New plan' header link is a solid/filled button (variant default)", async () => {
    renderPage();
    await screen.findByText("Starter");
    const headerLink = screen.getAllByRole("link", { name: /^new plan$/i })
      .find(b => b.closest("header"));
    expect(headerLink).toBeTruthy();
    expect(headerLink?.className).toMatch(/bg-primary|bg-\[hsl/);
    expect(headerLink?.className).not.toMatch(/border-input/);
  });
});
