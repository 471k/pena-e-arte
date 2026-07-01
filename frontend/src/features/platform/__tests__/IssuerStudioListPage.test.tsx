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
import { studiosApi } from "@/features/studios/studiosApi";
import { billingApi } from "@/features/billing/billingApi";
import { IssuerStudioListPage } from "@/features/platform/components/IssuerStudioListPage";
import type { StudioResponse } from "@/features/studios/studiosApi";
import type { PlatformSubscriptionResponse } from "@/features/platform/platform.types";
import type { PlanResponse } from "@/features/billing/billing.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const STUDIO_ACTIVE: StudioResponse = {
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
};

const STUDIO_SUSPENDED: StudioResponse = {
  ...STUDIO_ACTIVE,
  id:      "s2",
  name:    "Suspended Studio",
  slug:    "suspended-studio",
  isActive: false,
};

// Active studio with Trialing subscription — used to verify "In Trial" badge
const STUDIO_TRIALING: StudioResponse = {
  ...STUDIO_ACTIVE,
  id:   "s3",
  name: "Trialing Studio",
  slug: "trialing-studio",
};

const SUB_ACTIVE: PlatformSubscriptionResponse = {
  studioId:        "s1",
  studioName:      "Ink Soul",
  studioSlug:      "ink-soul",
  subscriptionId:  "sub-1",
  status:          "Active",
  planName:        "Pro",
  trialExpiresAt:  new Date(Date.now() + 30 * 86_400_000).toISOString(),
  currentPeriodEnd: new Date(Date.now() + 30 * 86_400_000).toISOString(),
};

const SUB_TRIALING: PlatformSubscriptionResponse = {
  ...SUB_ACTIVE,
  studioId:        "s3",
  studioName:      "Trialing Studio",
  studioSlug:      "trialing-studio",
  subscriptionId:  "sub-2",
  status:          "Trialing",
  planName:        null,
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
  http.get("http://localhost/api/v1/studios", () =>
    HttpResponse.json([STUDIO_ACTIVE, STUDIO_SUSPENDED, STUDIO_TRIALING]),
  ),
  http.get("http://localhost/api/v1/platform/subscriptions", () =>
    HttpResponse.json([SUB_ACTIVE, SUB_TRIALING]),
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

function renderPage() {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter>
        <IssuerStudioListPage />
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("IssuerStudioListPage", () => {

  it("shows skeleton cards while loading, not a spinner", () => {
    renderPage();
    expect(document.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
    expect(screen.queryByText("Loading…")).not.toBeInTheDocument();
  });

  it("renders the Studios header", async () => {
    renderPage();
    expect(await screen.findByText("Studios")).toBeInTheDocument();
  });

  it("renders all studio names", async () => {
    renderPage();
    expect(await screen.findByText("Ink Soul")).toBeInTheDocument();
    expect(screen.getByText("Suspended Studio")).toBeInTheDocument();
    expect(screen.getByText("Trialing Studio")).toBeInTheDocument();
  });

  it("shows studio count in the header", async () => {
    renderPage();
    await screen.findByText("Ink Soul");
    expect(screen.getByText("3 studios")).toBeInTheDocument();
  });

  it("shows Active status badge", async () => {
    renderPage();
    await screen.findByText("Ink Soul");
    // "Active" also appears as a <option> in the status filter dropdown
    expect(screen.getByText("Active", { selector: "span" })).toBeInTheDocument();
  });

  it("shows Suspended status badge for suspended studio", async () => {
    renderPage();
    await screen.findByText("Suspended Studio");
    // "Suspended" also appears as a <option> in the status filter dropdown
    expect(screen.getByText("Suspended", { selector: "span" })).toBeInTheDocument();
  });

  it("renders search input", async () => {
    renderPage();
    await screen.findByText("Ink Soul");
    expect(screen.getByPlaceholderText(/search by name or slug/i)).toBeInTheDocument();
  });

  it("filters studios by search term", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ink Soul");

    await user.type(screen.getByPlaceholderText(/search by name or slug/i), "ink");

    expect(screen.getByText("Ink Soul")).toBeInTheDocument();
    expect(screen.queryByText("Suspended Studio")).not.toBeInTheDocument();
  });

  it("shows 'No studios match your filters' when search has no results", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ink Soul");

    await user.type(screen.getByPlaceholderText(/search by name or slug/i), "zzznomatch");

    expect(screen.getByText(/no studios match your filters/i)).toBeInTheDocument();
  });

  it("shows Suspend button for active studios", async () => {
    renderPage();
    await screen.findByText("Ink Soul");
    // Multiple active studios (s1 and s3) both have a Suspend button
    const suspendBtns = screen.getAllByRole("button", { name: /suspend/i });
    expect(suspendBtns.length).toBeGreaterThan(0);
  });

  it("shows Reactivate button for suspended studios", async () => {
    renderPage();
    await screen.findByText("Suspended Studio");
    expect(screen.getByRole("button", { name: /reactivate/i })).toBeInTheDocument();
  });

  it("clicking Suspend shows a confirmation prompt", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ink Soul");

    // Multiple active studios have Suspend buttons — click the first one
    const suspendBtns = screen.getAllByRole("button", { name: /suspend/i });
    await user.click(suspendBtns[0]);

    expect(screen.getByText(/suspend\?/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /yes/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /no/i })).toBeInTheDocument();
  });

  it("cancelling Suspend hides the confirmation", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ink Soul");

    const suspendBtns = screen.getAllByRole("button", { name: /suspend/i });
    await user.click(suspendBtns[0]);
    await user.click(screen.getByRole("button", { name: /no/i }));

    expect(screen.queryByText(/suspend\?/i)).not.toBeInTheDocument();
  });

  it("confirming Suspend calls PATCH studios/:id/suspend", async () => {
    const suspendSpy = vi.fn();
    // After sort (Suspended→Trialing→Active), s3 (Trialing) is the first row with a Suspend button.
    server.use(
      http.patch("http://localhost/api/v1/studios/s3/suspend", () => {
        suspendSpy();
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ink Soul");

    // After sort: s2 (Suspended → Reactivate), s3 (Trialing → Suspend), s1 (Active → Suspend)
    const suspendBtns = screen.getAllByRole("button", { name: /suspend/i });
    await user.click(suspendBtns[0]);
    await user.click(screen.getByRole("button", { name: /yes/i }));

    await waitFor(() => expect(suspendSpy).toHaveBeenCalledOnce());
  });

  it("confirming Reactivate calls PATCH studios/:id/unsuspend", async () => {
    const unsuspendSpy = vi.fn();
    server.use(
      http.patch("http://localhost/api/v1/studios/s2/unsuspend", () => {
        unsuspendSpy();
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Suspended Studio");

    await user.click(screen.getByRole("button", { name: /reactivate/i }));
    await user.click(screen.getByRole("button", { name: /yes/i }));

    await waitFor(() => expect(unsuspendSpy).toHaveBeenCalledOnce());
  });

  it("shows Extend trial button for non-active studios", async () => {
    renderPage();
    await screen.findByText("Ink Soul");
    // Suspended and Trialing studios both have canExtendTrial=true
    const extendBtns = screen.getAllByRole("button", { name: /extend trial|grant extension/i });
    expect(extendBtns.length).toBeGreaterThan(0);
  });

  it("shows extend trial form when clicking Extend trial", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ink Soul");

    const extendBtns = screen.getAllByRole("button", { name: /extend trial|grant extension/i });
    await user.click(extendBtns[0]);

    expect(screen.getByText(/extend trial by/i)).toBeInTheDocument();
  });

  it("calls PATCH subscriptions/:id/trial when confirming trial extension", async () => {
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
    await screen.findByText("Suspended Studio");

    const extendBtns = screen.getAllByRole("button", { name: /extend trial|grant extension/i });
    await user.click(extendBtns[0]);

    await user.click(screen.getByRole("button", { name: /confirm/i }));

    await waitFor(() => expect(extendSpy).toHaveBeenCalledWith({ additionalDays: 7 }));
  });

  it("shows error state when studios fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios", () =>
        HttpResponse.json({ message: "Server error" }, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText(/failed to load studios/i)).toBeInTheDocument();
  });

  it("shows 'In Trial' badge instead of 'Trialing'", async () => {
    renderPage();
    await screen.findByText("Trialing Studio");
    // STUDIO_TRIALING is active + Trialing subscription → badge shows "In Trial"
    const badges = screen.getAllByText("In Trial", { selector: "span" });
    expect(badges.length).toBeGreaterThan(0);
  });

  it("does not render 'No plan' in studio meta lines for studios in trial", async () => {
    renderPage();
    await screen.findByText("Trialing Studio");
    // The meta line (p element) should show "In Trial" not "No plan"
    // The plan filter dropdown option "No plan" is excluded by the p selector
    expect(screen.queryByText(/no plan/i, { selector: "p" })).not.toBeInTheDocument();
  });

  it("View button links to studio detail page", async () => {
    renderPage();
    await screen.findByText("Ink Soul");
    // After sort: s2 (Suspended) is first row; its View link points to /platform/studios/s2
    const viewLinks = screen.getAllByRole("link", { name: /view/i });
    expect(viewLinks[0]).toHaveAttribute("href", `/platform/studios/s2`);
  });

  it("Cancel Subscription button appears last in the button group for Active studios", async () => {
    renderPage();
    await screen.findByText("Ink Soul");
    const cancelBtns = screen.getAllByRole("button", { name: /cancel subscription/i });
    expect(cancelBtns.length).toBeGreaterThan(0);
    // Verify Suspend appears in the DOM before Cancel (button ordering)
    const allButtons = screen.getAllByRole("button");
    const suspendIdx = allButtons.findIndex((b) => /^suspend$/i.test(b.textContent?.trim() ?? ""));
    const cancelIdx  = allButtons.findIndex((b) => /cancel subscription/i.test(b.textContent ?? ""));
    expect(suspendIdx).toBeLessThan(cancelIdx);
  });

  it("plan filter shows available plans in dropdown", async () => {
    renderPage();
    await screen.findByText("Ink Soul");
    const planSelect = screen.getByDisplayValue("All plans");
    expect(planSelect).toBeInTheDocument();
    // Plans are from MSW seed: "Starter"
    expect(screen.getByRole("option", { name: "Starter" })).toBeInTheDocument();
  });
});
