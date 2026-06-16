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

  it("shows a loading spinner while plans are loading", () => {
    renderPage();
    expect(screen.getByText("Loading…")).toBeInTheDocument();
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

  it("shows 'no-branding' badge for plans with allowBrandingRemoval", async () => {
    renderPage();
    await screen.findByText("Pro");
    expect(screen.getByText("no-branding")).toBeInTheDocument();
  });

  it("renders the New plan button", async () => {
    renderPage();
    expect(await screen.findByRole("button", { name: /new plan/i })).toBeInTheDocument();
  });

  it("clicking New plan shows the create form", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("button", { name: /new plan/i });

    await user.click(screen.getByRole("button", { name: /new plan/i }));

    expect(screen.getByLabelText(/^name$/i)).toBeInTheDocument();
    // Use exact strings — /monthly price/i also matches "Stripe Monthly Price ID"
    expect(screen.getByLabelText("Monthly price (€)")).toBeInTheDocument();
    expect(screen.getByLabelText("Yearly price (€)")).toBeInTheDocument();
  });

  it("shows Stripe price ID fields in the form", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("button", { name: /new plan/i });

    await user.click(screen.getByRole("button", { name: /new plan/i }));

    expect(screen.getByLabelText(/stripe monthly price id/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/stripe yearly price id/i)).toBeInTheDocument();
  });

  it("calls POST /billing/plans with the correct body", async () => {
    const createSpy = vi.fn();
    server.use(
      http.post("http://localhost/api/v1/billing/plans", async ({ request }) => {
        const body = await request.json();
        createSpy(body);
        return HttpResponse.json({ id: "plan-new", ...(body as object) });
      }),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("button", { name: /new plan/i });
    await user.click(screen.getByRole("button", { name: /new plan/i }));

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

    // Click the edit (pencil) button — first ghost icon button in the first plan card
    const editBtns = screen.getAllByRole("button", { name: "" });
    await user.click(editBtns[0]);

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

    const editBtns = screen.getAllByRole("button", { name: "" });
    await user.click(editBtns[0]);

    await user.click(screen.getByRole("button", { name: /^save$/i }));

    await waitFor(() => expect(updateSpy).toHaveBeenCalledOnce());
    expect(updateSpy.mock.calls[0][0]).toMatchObject({
      stripePriceIdMonthly: "price_monthly_starter",
    });
  });

  it("shows delete confirmation on first delete click", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Starter");

    // Trash icon button is the second icon button per card
    const trashBtns = screen.getAllByRole("button", { name: "" });
    await user.click(trashBtns[1]);

    expect(screen.getByText(/delete\?/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /yes/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /no/i })).toBeInTheDocument();
  });

  it("cancels delete when clicking No", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Starter");

    const trashBtns = screen.getAllByRole("button", { name: "" });
    await user.click(trashBtns[1]);
    await user.click(screen.getByRole("button", { name: /no/i }));

    expect(screen.queryByText(/delete\?/i)).not.toBeInTheDocument();
  });

  it("calls DELETE /billing/plans/:id on confirm", async () => {
    const deleteSpy = vi.fn();
    // Only add the DELETE handler; no GET override so initial load still gets PLANS
    server.use(
      http.delete("http://localhost/api/v1/billing/plans/plan-1", () => {
        deleteSpy();
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Starter");

    const trashBtns = screen.getAllByRole("button", { name: "" });
    await user.click(trashBtns[1]);
    await user.click(screen.getByRole("button", { name: /yes/i }));

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
});
