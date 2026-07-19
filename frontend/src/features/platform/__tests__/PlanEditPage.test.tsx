import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { billingApi } from "@/features/billing/billingApi";
import { PlanEditPage } from "@/features/platform/components/PlanEditPage";
import type { PlanResponse } from "@/features/billing/billing.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const PLANS: PlanResponse[] = [
  {
    id:                    "plan-1",
    name:                  "Starter",
    yearlyDiscountPercent: 17,
    allowBrandingRemoval:  false,
    subscriberCount:       4,
    maxArtists:               5,
    maxAppointmentsPerMonth:  null,
    maxNotificationsPerMonth: null,
    maxStorageGb:             null,
    maxLocations:             null,
    allowApiAccess:           false,
    prioritySupport:          false,
    prices: [
      { id: "price-1-m", interval: "Monthly", price: 29, stripePriceId: "price_monthly_starter", isActive: true },
    ],
  },
];

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/billing/plans", () => HttpResponse.json(PLANS)),
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

// useBlocker only works inside a data router, so tests must use createMemoryRouter +
// RouterProvider rather than the plain <MemoryRouter> component.
function renderEditPage(initialPath: string) {
  const store = makeStore();
  const router = createMemoryRouter(
    [
      { path: "/platform/plans",             element: <div>Plans list</div> },
      { path: "/platform/plans/new",         element: <PlanEditPage /> },
      { path: "/platform/plans/:planId/edit", element: <PlanEditPage /> },
    ],
    { initialEntries: [initialPath] },
  );
  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );
  return { store, router };
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("PlanEditPage", () => {

  it("pre-fills all fields when editing an existing plan", async () => {
    renderEditPage("/platform/plans/plan-1/edit");

    expect(await screen.findByLabelText(/^name$/i)).toHaveValue("Starter");
    expect(screen.getByLabelText(/yearly discount/i)).toHaveValue(17);
    expect(screen.getByLabelText("Monthly price (€)")).toHaveValue(29);
    expect(screen.getByLabelText(/stripe monthly price id/i)).toHaveValue("price_monthly_starter");
    expect(screen.getByLabelText("Artists")).toHaveValue(5);
  });

  it("submits create mutation when no planId param is present", async () => {
    const createSpy = vi.fn();
    server.use(
      http.post("http://localhost/api/v1/billing/plans", async ({ request }) => {
        const body = await request.json();
        createSpy(body);
        return HttpResponse.json({ id: "plan-new", ...(body as object), subscriberCount: 0 });
      }),
    );

    const user = userEvent.setup();
    renderEditPage("/platform/plans/new");

    await user.type(await screen.findByLabelText(/^name$/i), "Enterprise");
    await user.clear(screen.getByLabelText("Monthly price (€)"));
    await user.type(screen.getByLabelText("Monthly price (€)"), "99");
    // Both the desktop header Save and the mobile sticky-footer Save render in the DOM
    // at once (they're toggled via a CSS "hidden" class, which jsdom doesn't apply) —
    // either one submits the same form, so just click the first match.
    await user.click(screen.getAllByRole("button", { name: /^save$/i })[0]);

    await waitFor(() => expect(createSpy).toHaveBeenCalledOnce());
    const body = createSpy.mock.calls[0][0] as { name: string };
    expect(body.name).toBe("Enterprise");

    // Successful save navigates back to the list.
    expect(await screen.findByText("Plans list")).toBeInTheDocument();
  });

  it("submits update mutation when a planId param is present", async () => {
    const updateSpy = vi.fn();
    server.use(
      http.put("http://localhost/api/v1/billing/plans/plan-1", async ({ request }) => {
        const body = await request.json();
        updateSpy(body);
        return HttpResponse.json({ ...PLANS[0], ...(body as object) });
      }),
    );

    const user = userEvent.setup();
    renderEditPage("/platform/plans/plan-1/edit");
    await screen.findByLabelText(/^name$/i);

    await user.click(screen.getAllByRole("button", { name: /^save$/i })[0]);

    await waitFor(() => expect(updateSpy).toHaveBeenCalledOnce());
    expect(await screen.findByText("Plans list")).toBeInTheDocument();
  });

  it("shows the API's validation message as a form-level banner on a failed save", async () => {
    server.use(
      http.post("http://localhost/api/v1/billing/plans", () =>
        HttpResponse.json({ status: 422, message: "A plan with this name already exists." }, { status: 422 }),
      ),
    );

    const user = userEvent.setup();
    renderEditPage("/platform/plans/new");

    // Fill in a valid monthly price so client-side zod validation passes and the
    // request actually reaches the (mocked) server.
    await user.type(await screen.findByLabelText(/^name$/i), "Enterprise");
    await user.clear(screen.getByLabelText("Monthly price (€)"));
    await user.type(screen.getByLabelText("Monthly price (€)"), "99");
    await user.click(screen.getAllByRole("button", { name: /^save$/i })[0]);

    expect(await screen.findByRole("alert")).toHaveTextContent(/a plan with this name already exists/i);
    // Failed save must not navigate away.
    expect(screen.queryByText("Plans list")).not.toBeInTheDocument();
  });

  it("hides Stripe price ID inputs when the corresponding price toggle is off", async () => {
    renderEditPage("/platform/plans/new");

    await screen.findByLabelText(/^name$/i);
    expect(screen.getByLabelText(/stripe monthly price id/i)).toBeInTheDocument();
    expect(screen.queryByLabelText("Yearly price (€)")).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/stripe yearly price id/i)).not.toBeInTheDocument();
  });

  it("enabling the Yearly toggle reveals the Yearly price and Stripe ID fields", async () => {
    const user = userEvent.setup();
    renderEditPage("/platform/plans/new");

    await user.click(await screen.findByRole("switch", { name: /^yearly price$/i }));

    expect(screen.getByLabelText("Yearly price (€)")).toBeInTheDocument();
    expect(screen.getByLabelText(/stripe yearly price id/i)).toBeInTheDocument();
  });

  it("the Unlimited checkbox toggles the numeric input's disabled state", async () => {
    const user = userEvent.setup();
    renderEditPage("/platform/plans/new");

    const artistsInput = await screen.findByLabelText("Artists");
    // A new plan defaults every limit to null (unlimited), so the input starts disabled.
    expect(artistsInput).toBeDisabled();

    // Multiple "Unlimited" checkboxes exist (one per limit field) — the first one in
    // document order corresponds to Artists, the first field rendered.
    const unlimitedCheckboxes = screen.getAllByRole("checkbox", { name: /unlimited/i });
    await user.click(unlimitedCheckboxes[0]);
    expect(artistsInput).not.toBeDisabled();

    await user.click(unlimitedCheckboxes[0]);
    expect(artistsInput).toBeDisabled();
  });

  it("shows a 404-style state for an unknown planId", async () => {
    renderEditPage("/platform/plans/does-not-exist/edit");

    expect(await screen.findByText(/plan not found/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /back to plans/i })).toHaveAttribute("href", "/platform/plans");
  });

  it("blocks navigation away when the form is dirty, and the confirm dialog can cancel or discard", async () => {
    const user = userEvent.setup();
    renderEditPage("/platform/plans/plan-1/edit");

    await user.type(await screen.findByLabelText(/^name$/i), " Plus");
    await user.click(screen.getByRole("link", { name: /^plans$/i }));

    expect(await screen.findByText(/discard unsaved changes/i)).toBeInTheDocument();

    // "Keep editing" dismisses the dialog and stays on the form.
    await user.click(screen.getByRole("button", { name: /keep editing/i }));
    expect(screen.queryByText(/discard unsaved changes/i)).not.toBeInTheDocument();
    expect(screen.getByLabelText(/^name$/i)).toBeInTheDocument();

    // Navigating away again and discarding proceeds to the list.
    await user.click(screen.getByRole("link", { name: /^plans$/i }));
    await user.click(await screen.findByRole("button", { name: /discard changes/i }));
    expect(await screen.findByText("Plans list")).toBeInTheDocument();
  });

  it("does not block navigation when the form has no unsaved changes", async () => {
    const user = userEvent.setup();
    renderEditPage("/platform/plans/plan-1/edit");

    await screen.findByLabelText(/^name$/i);
    await user.click(screen.getByRole("link", { name: /^plans$/i }));

    expect(await screen.findByText("Plans list")).toBeInTheDocument();
  });
});
