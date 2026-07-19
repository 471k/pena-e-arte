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
    isSuspended:     false,
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
    isSuspended:     false,
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
    isSuspended:     false,
  },
];

const PLANS: PlanResponse[] = [
  {
    id:                    "plan-1",
    name:                  "Starter",
    yearlyDiscountPercent: 17,
    allowBrandingRemoval:  false,
    subscriberCount:       2,
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

  it("shows skeleton cards while loading, not a spinner", () => {
    renderPage();
    expect(document.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
    expect(screen.queryByText("Loading…")).not.toBeInTheDocument();
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

  it("shows the 'In Trial' status badge for trialing subscriptions", async () => {
    renderPage();
    await screen.findByText("Trialing Studio");
    // STATUS_LABELS["Trialing"] = "In Trial"
    const badges = screen.getAllByText("In Trial", { selector: "span" });
    expect(badges.length).toBeGreaterThan(0);
    // The raw string "Trialing" must not appear as a badge
    expect(screen.queryByText("Trialing", { selector: "span" })).not.toBeInTheDocument();
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

    await user.click(
      screen.getByRole("button", { name: /cancel subscription for active studio/i })
    );

    // Text spans a <strong> child; check via the confirmation panel's textContent
    const yesBtn = screen.getByRole("button", { name: /yes, cancel/i });
    expect(yesBtn.closest("div")?.textContent).toMatch(/cancel subscription for active studio/i);
    expect(screen.getByRole("button", { name: /keep/i })).toBeInTheDocument();
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

    await user.click(
      screen.getByRole("button", { name: /cancel subscription for active studio/i })
    );
    await user.click(screen.getByRole("button", { name: /yes, cancel/i }));

    await waitFor(() => expect(cancelSpy).toHaveBeenCalledOnce());
  });

  it("shows empty state when no subscriptions exist", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/subscriptions", () =>
        HttpResponse.json([]),
      ),
    );
    renderPage();
    expect(await screen.findByText(/no subscriptions yet/i)).toBeInTheDocument();
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

  it("shows aria-label with studio name on Cancel Subscription button", async () => {
    renderPage();
    await screen.findByText("Active Studio");
    expect(
      screen.getByRole("button", { name: /cancel subscription for active studio/i })
    ).toBeInTheDocument();
  });

  it("shows aria-label with studio name on Extend Trial button", async () => {
    renderPage();
    await screen.findByText("Trialing Studio");
    expect(
      screen.getByRole("button", { name: /extend trial for trialing studio/i })
    ).toBeInTheDocument();
  });

  it("search input filters by studio name", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Active Studio");

    await user.type(
      screen.getByPlaceholderText(/search by studio name or slug/i),
      "active"
    );

    expect(screen.getByText("Active Studio")).toBeInTheDocument();
    expect(screen.queryByText("Trialing Studio")).not.toBeInTheDocument();
    expect(screen.queryByText("Cancelled Studio")).not.toBeInTheDocument();
  });

  it("shows 'No subscriptions matching' when search has no results", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Active Studio");

    await user.type(
      screen.getByPlaceholderText(/search by studio name or slug/i),
      "zzznomatch"
    );

    expect(screen.getByText(/no subscriptions matching/i)).toBeInTheDocument();
  });

  it("shows 'Clear filters' button when search yields no results", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Active Studio");

    await user.type(
      screen.getByPlaceholderText(/search by studio name or slug/i),
      "zzznomatch"
    );

    const clearBtn = screen.getByRole("button", { name: /clear filters/i });
    expect(clearBtn).toBeInTheDocument();

    await user.click(clearBtn);

    expect(screen.getByText("Active Studio")).toBeInTheDocument();
  });

  it("sort dropdown is present and defaults to trial end (soonest first)", async () => {
    renderPage();
    await screen.findByText("Active Studio");
    const sortSelect = screen.getByDisplayValue(/trial end/i);
    expect(sortSelect).toBeInTheDocument();
  });

  it("filter pills show human-readable labels, not raw enum values", async () => {
    renderPage();
    await screen.findByText("Active Studio");
    // "In Trial" pill, not "Trialing" pill
    const trialPill = screen.getByRole("button", { name: /in trial/i });
    expect(trialPill).toBeInTheDocument();
    // "GracePeriod" pill must not appear
    expect(screen.queryByRole("button", { name: /^GracePeriod/i })).not.toBeInTheDocument();
  });

  it("shows 'Grace Period' label in status badge, not 'GracePeriod'", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/subscriptions", () =>
        HttpResponse.json([
          {
            studioId:        "sg1",
            studioName:      "Grace Studio",
            studioSlug:      "grace-studio",
            subscriptionId:  "sub-g1",
            status:          "GracePeriod",
            planName:        "Pro",
            trialExpiresAt:  new Date(Date.now() - 14 * 86_400_000).toISOString(),
            currentPeriodEnd: new Date(Date.now() + 3 * 86_400_000).toISOString(),
            isSuspended:     false,
          },
        ]),
      ),
    );
    renderPage();
    await screen.findByText("Grace Studio");
    expect(screen.getByText("Grace Period", { selector: "span" })).toBeInTheDocument();
    expect(screen.queryByText("GracePeriod", { selector: "span" })).not.toBeInTheDocument();
  });

  it("extend trial form shows 'Extend trial by' label", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Trialing Studio");

    const extendBtns = screen.getAllByRole("button", { name: /extend trial/i });
    await user.click(extendBtns[0]);

    expect(screen.getByText(/extend trial by/i)).toBeInTheDocument();
  });

  it("'Record Cash Payment' form header appears when Activate is clicked", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/subscriptions", () =>
        HttpResponse.json([
          {
            studioId:        "sg1",
            studioName:      "Grace Studio",
            studioSlug:      "grace-studio",
            subscriptionId:  "sub-g1",
            status:          "GracePeriod",
            planName:        "Pro",
            trialExpiresAt:  new Date(Date.now() - 14 * 86_400_000).toISOString(),
            currentPeriodEnd: new Date(Date.now() + 3 * 86_400_000).toISOString(),
            isSuspended:     false,
          },
        ]),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Grace Studio");

    await user.click(screen.getByRole("button", { name: /activate subscription for grace studio/i }));

    expect(screen.getByText(/record cash payment/i)).toBeInTheDocument();
  });

  it("shows Suspended (amber pill) for a studio with isSuspended: true", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/subscriptions", () =>
        HttpResponse.json([{ ...SUBS[0], status: "Active", isSuspended: true }]),
      ),
    );
    renderPage();
    const badge = await screen.findByText("Suspended");
    expect(badge).toBeInTheDocument();
    expect(badge.className).toMatch(/amber/);
  });

  it("does NOT show Active badge for a suspended studio", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/subscriptions", () =>
        HttpResponse.json([{ ...SUBS[0], status: "Active", isSuspended: true }]),
      ),
    );
    renderPage();
    await screen.findByText("Suspended");
    // "Active" pill should not be present
    expect(screen.queryByText("Active")).not.toBeInTheDocument();
  });

  it("does NOT show trial expiry text for an Active (paid) studio", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/subscriptions", () =>
        HttpResponse.json([{
          ...SUBS[0],
          status: "Active",
          isSuspended: false,
          trialExpiresAt: new Date(Date.now() - 60 * 86_400_000).toISOString(),
        }]),
      ),
    );
    renderPage();
    await screen.findByText(SUBS[0].studioName);
    expect(screen.queryByText(/trial expired/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/trial ends/i)).not.toBeInTheDocument();
  });

  // ── Header / layout parity with IssuerStudioListPage ───────────────────────────

  it("page header has z-10 class, below the global nav's z-20, to prevent it painting over the nav on scroll", async () => {
    renderPage();
    await screen.findByText("Active Studio");
    const header = document.querySelector("header");
    expect(header?.className).toMatch(/z-10/);
  });

  // ── Copy slug button ─────────────────────────────────────────────────────────

  it("slug is wrapped in a button with accessible copy label", async () => {
    renderPage();
    await screen.findByText("Active Studio");
    expect(screen.getByRole("button", { name: /copy slug active-studio/i })).toBeInTheDocument();
  });

  // ── No-plan fallback + warning ───────────────────────────────────────────────

  it("shows 'No plan assigned' and a warning icon for an Active subscription with null planName", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/subscriptions", () =>
        HttpResponse.json([{ ...SUBS[0], planName: null }]),
      ),
    );
    renderPage();
    await screen.findByText("Active Studio");
    expect(screen.getByText(/no plan assigned/i)).toBeInTheDocument();
    expect(document.querySelector('[title*="no linked plan"]')).not.toBeNull();
  });

  // ── Grant Extension label ────────────────────────────────────────────────────

  it("expired trial shows 'Grant Extension (+7 days)' label", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/subscriptions", () =>
        HttpResponse.json([{
          ...SUBS[1],
          trialExpiresAt: new Date(Date.now() - 5 * 86_400_000).toISOString(),
        }]),
      ),
    );
    renderPage();
    await screen.findByText("Trialing Studio");
    // aria-label ("Grant extension for Trialing Studio") overrides accessible name,
    // so assert on the visible button text directly.
    expect(screen.getByText("Grant Extension (+7 days)")).toBeInTheDocument();
  });

  // ── Activate button hierarchy ────────────────────────────────────────────────

  it("Activate button for a Cancelled subscription is an outline button, not filled", async () => {
    renderPage();
    await screen.findByText("Cancelled Studio");
    const activateBtn = screen.getByRole("button", { name: /activate subscription for cancelled studio/i });
    expect(activateBtn.className).toMatch(/border/);
    expect(activateBtn.className).not.toMatch(/bg-primary/);
  });

  // ── Pagination ───────────────────────────────────────────────────────────────

  it("does not show pagination controls when results fit on one page", async () => {
    renderPage();
    await screen.findByText("Active Studio");
    expect(screen.queryByText(/page \d+ of \d+/i)).not.toBeInTheDocument();
  });

  it("shows pagination controls when results exceed page size, and Next advances the page", async () => {
    const user = userEvent.setup();
    const many: PlatformSubscriptionResponse[] = Array.from({ length: 15 }, (_, i) => ({
      ...SUBS[0],
      studioId:   `m${i + 1}`,
      studioName: `Sub Studio ${String(i + 1).padStart(2, "0")}`,
      studioSlug: `sub-studio-${String(i + 1).padStart(2, "0")}`,
      subscriptionId: `sub-m${i + 1}`,
    }));
    server.use(
      http.get("http://localhost/api/v1/platform/subscriptions", () => HttpResponse.json(many)),
    );
    renderPage();
    await screen.findByText("Sub Studio 01");
    expect(screen.getByText("Page 1 of 2")).toBeInTheDocument();
    expect(screen.queryByText("Sub Studio 11")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /previous/i })).toBeDisabled();

    await user.click(screen.getByRole("button", { name: /^next$/i }));

    expect(await screen.findByText("Sub Studio 11")).toBeInTheDocument();
    expect(screen.queryByText("Sub Studio 01")).not.toBeInTheDocument();
    expect(screen.getByText("Page 2 of 2")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^next$/i })).toBeDisabled();
  });
});
