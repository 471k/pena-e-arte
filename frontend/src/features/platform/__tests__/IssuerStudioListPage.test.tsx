import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor, within } from "@testing-library/react";
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
  phoneNumber:          null,
  instagramHandle:      null,
  nipt:                 null,
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
  isSuspended:     false,
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
];

function makeManyStudios(count: number): {
  studios: StudioResponse[];
  subs:    PlatformSubscriptionResponse[];
} {
  const studios: StudioResponse[] = [];
  const subs:    PlatformSubscriptionResponse[] = [];
  for (let i = 1; i <= count; i++) {
    const id   = `m${i}`;
    const name = `Studio ${String(i).padStart(2, "0")}`;
    const slug = `studio-${String(i).padStart(2, "0")}`;
    studios.push({ ...STUDIO_ACTIVE, id, name, slug });
    subs.push({
      ...SUB_ACTIVE,
      studioId:       id,
      studioName:     name,
      studioSlug:     slug,
      subscriptionId: `sub-${id}`,
    });
  }
  return { studios, subs };
}

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
    // "Active" also appears as a <option> in the status filter dropdown, and as a
    // group divider pill (Fix #4) in addition to the row badge — at least one <span> shows it.
    expect(screen.getAllByText("Active", { selector: "span" }).length).toBeGreaterThan(0);
  });

  it("shows Suspended status badge for suspended studio", async () => {
    renderPage();
    await screen.findByText("Suspended Studio");
    // "Suspended" also appears as a <option> in the status filter dropdown, and as a
    // group divider pill (Fix #4) in addition to the row badge — at least one <span> shows it.
    expect(screen.getAllByText("Suspended", { selector: "span" }).length).toBeGreaterThan(0);
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
    const suspendBtns = screen.getAllByRole("button", { name: /^suspend$/i });
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
    const suspendBtns = screen.getAllByRole("button", { name: /^suspend$/i });
    await user.click(suspendBtns[0]);

    expect(screen.getByText(/suspend\?/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /yes/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /no/i })).toBeInTheDocument();
  });

  it("cancelling Suspend hides the confirmation", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ink Soul");

    const suspendBtns = screen.getAllByRole("button", { name: /^suspend$/i });
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
    const suspendBtns = screen.getAllByRole("button", { name: /^suspend$/i });
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

  it("clicking Cancel Subscription shows a confirmation step, and Confirm calls the cancel mutation", async () => {
    let cancelledStudioId: string | null = null;
    server.use(
      http.patch("http://localhost/api/v1/platform/subscriptions/:studioId/cancel", ({ params }) => {
        cancelledStudioId = params.studioId as string;
        return HttpResponse.json({});
      }),
    );
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ink Soul");
    // Rows are sorted by risk priority, not array order — scope to Ink Soul's own row
    // (s1) via its stable data-studio-id attribute rather than assuming array/render order.
    const row = document.querySelector<HTMLElement>('[data-studio-id="s1"]')!;

    await user.click(within(row).getByRole("button", { name: /cancel subscription/i }));
    expect(within(row).getByText(/cancel subscription permanently\?/i)).toBeInTheDocument();

    await user.click(within(row).getByRole("button", { name: /^confirm$/i }));
    await waitFor(() => expect(cancelledStudioId).toBe("s1"));
  });

  it("shows 'No studios registered yet' when there are genuinely zero studios (not just filtered out)", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios", () => HttpResponse.json([])),
      http.get("http://localhost/api/v1/platform/subscriptions", () => HttpResponse.json([])),
    );
    renderPage();
    expect(await screen.findByText(/no studios registered yet/i)).toBeInTheDocument();
  });

  it("extend trial form: entering an out-of-range day count does not call the mutation", async () => {
    let extendCalled = false;
    server.use(
      http.patch("http://localhost/api/v1/platform/subscriptions/:studioId/trial", () => {
        extendCalled = true;
        return HttpResponse.json({});
      }),
    );
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ink Soul");

    const extendBtns = screen.getAllByRole("button", { name: /extend trial|grant extension/i });
    await user.click(extendBtns[0]);
    const dayInput = screen.getByRole("spinbutton");
    await user.clear(dayInput);
    await user.type(dayInput, "0");
    await user.click(screen.getByRole("button", { name: /^confirm$/i }));

    expect(extendCalled).toBe(false);
  });

  it("activate form: Activate button is disabled until a plan is selected", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Suspended Studio");

    const activateBtns = screen.getAllByRole("button", { name: /activate subscription/i });
    await user.click(activateBtns[0]);

    const confirmActivateBtn = screen.getByRole("button", { name: /activate subscription/i });
    expect(confirmActivateBtn).toBeDisabled();

    const planSelect = screen.getByLabelText(/^plan$/i);
    await user.selectOptions(planSelect, "plan-1");
    expect(confirmActivateBtn).not.toBeDisabled();
  });

  it("plan filter shows available plans in dropdown", async () => {
    renderPage();
    await screen.findByText("Ink Soul");
    const planSelect = screen.getByDisplayValue("All plans");
    expect(planSelect).toBeInTheDocument();
    // Plans are from MSW seed: "Starter"
    expect(screen.getByRole("option", { name: "Starter" })).toBeInTheDocument();
  });

  // ── Fix #1: z-index / header ──────────────────────────────────────────────────

  it("page header has z-10 class, below the global nav's z-20, to prevent it painting over the nav on scroll", () => {
    renderPage();
    const header = document.querySelector("header");
    expect(header?.className).toMatch(/z-10/);
  });

  // ── Fix #2: Plan display ──────────────────────────────────────────────────────

  it("shows 'No plan assigned' for Active studio with null planName", async () => {
    const STUDIO_NO_PLAN: StudioResponse = {
      ...STUDIO_ACTIVE,
      id:   "s-noplan",
      name: "No Plan Studio",
      slug: "no-plan-studio",
    };
    const SUB_NO_PLAN: PlatformSubscriptionResponse = {
      studioId:         "s-noplan",
      studioName:       "No Plan Studio",
      studioSlug:       "no-plan-studio",
      subscriptionId:   "sub-np",
      status:           "Active",
      planName:         null,
      trialExpiresAt:   "",
      currentPeriodEnd: new Date(Date.now() + 30 * 86_400_000).toISOString(),
      isSuspended:      false,
    };
    server.use(
      http.get("http://localhost/api/v1/studios", () =>
        HttpResponse.json([STUDIO_NO_PLAN]),
      ),
      http.get("http://localhost/api/v1/platform/subscriptions", () =>
        HttpResponse.json([SUB_NO_PLAN]),
      ),
    );
    renderPage();
    expect(await screen.findByText("No Plan Studio")).toBeInTheDocument();
    expect(screen.getByText(/no plan assigned/i)).toBeInTheDocument();
  });

  it("shows AlertTriangle icon for Active studio with null planName", async () => {
    const STUDIO_NO_PLAN: StudioResponse = {
      ...STUDIO_ACTIVE,
      id:   "s-noplan2",
      name: "No Plan Studio 2",
      slug: "no-plan-studio-2",
    };
    server.use(
      http.get("http://localhost/api/v1/studios", () =>
        HttpResponse.json([STUDIO_NO_PLAN]),
      ),
      http.get("http://localhost/api/v1/platform/subscriptions", () =>
        HttpResponse.json([{
          studioId: "s-noplan2", studioName: "No Plan Studio 2",
          studioSlug: "no-plan-studio-2", subscriptionId: "sub-np2",
          status: "Active", planName: null, trialExpiresAt: "",
          currentPeriodEnd: new Date(Date.now() + 30 * 86_400_000).toISOString(),
          isSuspended: false,
        }]),
      ),
    );
    renderPage();
    await screen.findByText("No Plan Studio 2");
    const warnEl = document.querySelector('[title*="no linked plan"]');
    expect(warnEl).not.toBeNull();
  });

  // ── Fix #3: aria-labels ───────────────────────────────────────────────────────

  it("status filter select has aria-label", async () => {
    renderPage();
    await screen.findByText("Ink Soul");
    expect(screen.getByRole("combobox", { name: /filter by status/i })).toBeInTheDocument();
  });

  it("plan filter select has aria-label", async () => {
    renderPage();
    await screen.findByText("Ink Soul");
    expect(screen.getByRole("combobox", { name: /filter by plan/i })).toBeInTheDocument();
  });

  // ── Fix #4: Group dividers ────────────────────────────────────────────────────

  it("shows status group divider headers when multiple status groups are present", async () => {
    renderPage();
    await screen.findByText("Ink Soul");
    // Seed has Suspended, Trialing, Active studios → 3 groups of 1 → each divider shows "1 studio"
    expect(screen.getAllByText("1 studio", { selector: "span" }).length).toBe(3);
  });

  it("does NOT show group divider when filtered to a single status", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ink Soul");

    const statusSelect = screen.getByRole("combobox", { name: /filter by status/i });
    await user.selectOptions(statusSelect, "Suspended");

    expect(screen.queryByText(/1 studio/, { selector: "span.rounded-full" })).not.toBeInTheDocument();
  });

  // ── Fix #5: Copy slug ─────────────────────────────────────────────────────────

  it("slug is wrapped in a button with accessible copy label", async () => {
    renderPage();
    await screen.findByText("Ink Soul");
    expect(screen.getByRole("button", { name: /copy slug ink-soul/i })).toBeInTheDocument();
  });

  // ── Fix #6: Button hierarchy ──────────────────────────────────────────────────

  it("Activate button for a Cancelled studio is an outline button, not filled", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios", () =>
        HttpResponse.json([STUDIO_ACTIVE]),
      ),
      http.get("http://localhost/api/v1/platform/subscriptions", () =>
        HttpResponse.json([{
          ...SUB_ACTIVE,
          status: "Cancelled",
          isSuspended: false,
        }]),
      ),
    );
    renderPage();
    await screen.findByText("Ink Soul");
    const activateBtn = screen.getByRole("button", { name: /activate/i });
    expect(activateBtn.className).toMatch(/border/);
    expect(activateBtn.className).not.toMatch(/bg-primary/);
  });

  it("Reactivate button for a Suspended studio is a filled (default) button", async () => {
    renderPage();
    await screen.findByText("Suspended Studio");
    const reactivateBtn = screen.getByRole("button", { name: /reactivate/i });
    expect(reactivateBtn.className).toMatch(/bg-primary/);
  });

  // ── Fix #8: Grant Extension label ─────────────────────────────────────────────

  it("expired trial shows 'Grant Extension (+7 days)' label", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios", () =>
        HttpResponse.json([{
          ...STUDIO_ACTIVE,
          id:             "s-expired",
          name:           "Expired Trial Studio",
          slug:           "expired-trial",
          trialExpiresAt: new Date(Date.now() - 5 * 86_400_000).toISOString(),
        }]),
      ),
      http.get("http://localhost/api/v1/platform/subscriptions", () =>
        HttpResponse.json([{
          ...SUB_ACTIVE,
          studioId:       "s-expired",
          studioName:     "Expired Trial Studio",
          studioSlug:     "expired-trial",
          status:         "Trialing",
          trialExpiresAt: new Date(Date.now() - 5 * 86_400_000).toISOString(),
          isSuspended:    false,
        }]),
      ),
    );
    renderPage();
    await screen.findByText("Expired Trial Studio");
    expect(
      screen.getByRole("button", { name: /grant extension \(\+7 days\)/i })
    ).toBeInTheDocument();
  });

  // ── Pagination ─────────────────────────────────────────────────────────────────

  it("does not show pagination controls when results fit on one page", async () => {
    renderPage();
    await screen.findByText("Ink Soul");
    expect(screen.queryByText(/page \d+ of \d+/i)).not.toBeInTheDocument();
  });

  it("shows pagination controls and correct page count when results exceed page size", async () => {
    const { studios, subs } = makeManyStudios(15);
    server.use(
      http.get("http://localhost/api/v1/studios", () => HttpResponse.json(studios)),
      http.get("http://localhost/api/v1/platform/subscriptions", () => HttpResponse.json(subs)),
    );
    renderPage();
    await screen.findByText("Studio 01");
    expect(screen.getByText("Page 1 of 2")).toBeInTheDocument();
    // Page size is 10 — item 11 should not be on the first page
    expect(screen.queryByText("Studio 11")).not.toBeInTheDocument();
  });

  it("Previous button is disabled on the first page", async () => {
    const { studios, subs } = makeManyStudios(15);
    server.use(
      http.get("http://localhost/api/v1/studios", () => HttpResponse.json(studios)),
      http.get("http://localhost/api/v1/platform/subscriptions", () => HttpResponse.json(subs)),
    );
    renderPage();
    await screen.findByText("Studio 01");
    expect(screen.getByRole("button", { name: /previous/i })).toBeDisabled();
  });

  it("clicking Next advances to the next page and Next becomes disabled on the last page", async () => {
    const user = userEvent.setup();
    const { studios, subs } = makeManyStudios(15);
    server.use(
      http.get("http://localhost/api/v1/studios", () => HttpResponse.json(studios)),
      http.get("http://localhost/api/v1/platform/subscriptions", () => HttpResponse.json(subs)),
    );
    renderPage();
    await screen.findByText("Studio 01");

    await user.click(screen.getByRole("button", { name: /^next$/i }));

    expect(await screen.findByText("Studio 11")).toBeInTheDocument();
    expect(screen.queryByText("Studio 01")).not.toBeInTheDocument();
    expect(screen.getByText("Page 2 of 2")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^next$/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /previous/i })).not.toBeDisabled();
  });

  it("changing the search term resets pagination back to page 1", async () => {
    const user = userEvent.setup();
    const { studios, subs } = makeManyStudios(15);
    server.use(
      http.get("http://localhost/api/v1/studios", () => HttpResponse.json(studios)),
      http.get("http://localhost/api/v1/platform/subscriptions", () => HttpResponse.json(subs)),
    );
    renderPage();
    await screen.findByText("Studio 01");

    await user.click(screen.getByRole("button", { name: /^next$/i }));
    expect(await screen.findByText("Studio 11")).toBeInTheDocument();

    await user.type(screen.getByPlaceholderText(/search by name or slug/i), "Studio 0");

    expect(await screen.findByText("Studio 01")).toBeInTheDocument();
    expect(screen.queryByText(/page \d+ of \d+/i)).not.toBeInTheDocument();
  });
});
