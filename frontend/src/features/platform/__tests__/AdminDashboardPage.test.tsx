import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { platformApi } from "@/features/platform/platformApi";
import { AdminDashboardPage } from "@/features/platform/components/AdminDashboardPage";
import type { PlatformStatsResponse, PlatformSubscriptionResponse } from "@/features/platform/platform.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const STATS: PlatformStatsResponse = {
  totalStudios:        12,
  activeSubscriptions: 8,
  trialStudios:        3,
  gracePeriodStudios:  1,
  pastDueStudios:      0,
  cancelledStudios:    0,
  suspendedStudios:    0,
  mrr:                 392,
  mrrGrowthPercent:    12.5,
  trialConversionRate: 0.727,
  newStudiosThisMonth: 5, // chosen to avoid collision with atRisk badge count (2)
  payingStudios:       8,
  atRiskMrr:           0,
  scheduledChurnMrr:   0,
  pausedMrr:           0,
  discountsThisMonth:  0,
  refundsThisMonth:    0,
};

const SUBSCRIPTIONS: PlatformSubscriptionResponse[] = [
  {
    studioId:        "s1",
    studioName:      "GracePeriod Studio",
    studioSlug:      "grace-studio",
    subscriptionId:  "sub-1",
    status:          "GracePeriod",
    planName:        "Pro",
    trialExpiresAt:  new Date(Date.now() + 2 * 86_400_000).toISOString(),
    currentPeriodEnd: new Date(Date.now() + 2 * 86_400_000).toISOString(),
    isSuspended:     false,
    cancelAtPeriodEnd: false,
  },
  {
    studioId:        "s2",
    studioName:      "PastDue Studio",
    studioSlug:      "pastdue-studio",
    subscriptionId:  "sub-2",
    status:          "PastDue",
    planName:        "Starter",
    trialExpiresAt:  new Date(Date.now() - 5 * 86_400_000).toISOString(),
    currentPeriodEnd: new Date(Date.now() - 5 * 86_400_000).toISOString(),
    isSuspended:     false,
    cancelAtPeriodEnd: false,
  },
  {
    studioId:        "s3",
    studioName:      "Active Studio",
    studioSlug:      "active-studio",
    subscriptionId:  "sub-3",
    status:          "Active",
    planName:        "Pro",
    trialExpiresAt:  new Date(Date.now() + 30 * 86_400_000).toISOString(),
    currentPeriodEnd: new Date(Date.now() + 30 * 86_400_000).toISOString(),
    isSuspended:     false,
    cancelAtPeriodEnd: false,
  },
];

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/platform/stats", () =>
    HttpResponse.json(STATS),
  ),
  http.get("http://localhost/api/v1/platform/subscriptions", () =>
    HttpResponse.json(SUBSCRIPTIONS),
  ),
  http.get("http://localhost/api/v1/platform/mrr-history", () =>
    HttpResponse.json([]), // empty — MrrChart renders gracefully with no data
  ),
  http.get("http://localhost/api/v1/platform/mrr-movements", () =>
    HttpResponse.json([]), // empty — MrrMovementsChart renders its empty state
  ),
  http.get("http://localhost/api/v1/platform/revenue-retention", () =>
    HttpResponse.json({ grossRevenueRetention: null, netRevenueRetention: null, startMrr: 0, periodStart: "2026-08-01T00:00:00Z" }),
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
    },
    middleware: (gd) => gd().concat(platformApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u4", email: "admin@platform.test" }, token: "fake", tenantId: null, role: "admin", pendingReferralCode: null, impersonation: null } as any,
    },
  });
}

function renderPage() {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/platform"]}>
        <Routes>
          <Route path="/platform"              element={<AdminDashboardPage />} />
          <Route path="/platform/studios"      element={<div data-testid="studios-page" />} />
          <Route path="/platform/plans"        element={<div data-testid="plans-page" />} />
          <Route path="/platform/subscriptions" element={<div data-testid="subscriptions-page" />} />
          <Route path="/platform/referrals"    element={<div data-testid="referrals-page" />} />
          <Route path="/platform/reports"      element={<div data-testid="reports-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("AdminDashboardPage", () => {

  it("shows a loading skeleton while data is loading", () => {
    renderPage();
    // Skeleton cards are present before data resolves
    const skeletons = document.querySelectorAll(".animate-pulse");
    expect(skeletons.length).toBeGreaterThan(0);
  });

  it("renders the Platform Overview heading", () => {
    renderPage();
    expect(screen.getByText("Platform Overview")).toBeInTheDocument();
  });

  it("renders KPI card values once stats load", async () => {
    renderPage();
    expect(await screen.findByText("12")).toBeInTheDocument(); // totalStudios
    expect(screen.getByText("8")).toBeInTheDocument();         // activeSubscriptions
    expect(screen.getByText("3")).toBeInTheDocument();         // trialStudios
    expect(screen.getByText("1")).toBeInTheDocument();         // gracePeriodStudios
    expect(screen.getByText("5")).toBeInTheDocument();         // newStudiosThisMonth
  });

  it("shows MRR formatted as currency", async () => {
    renderPage();
    expect(await screen.findByText(/392/)).toBeInTheDocument();
  });

  it("shows trial conversion rate as percentage", async () => {
    renderPage();
    // 0.727 → 72.7%
    expect(await screen.findByText("72.7%")).toBeInTheDocument();
  });

  it("renders at-risk studios (GracePeriod and PastDue)", async () => {
    renderPage();
    expect(await screen.findByText("GracePeriod Studio")).toBeInTheDocument();
    expect(screen.getByText("PastDue Studio")).toBeInTheDocument();
  });

  it("does NOT show Active studios in the at-risk widget", async () => {
    renderPage();
    await screen.findByText("GracePeriod Studio");
    expect(screen.queryByText("Active Studio")).not.toBeInTheDocument();
  });

  it("shows 'No at-risk studios' when all subscriptions are healthy", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/subscriptions", () =>
        HttpResponse.json([SUBSCRIPTIONS[2]]), // only Active
      ),
    );
    renderPage();
    expect(await screen.findByText("No at-risk studios.")).toBeInTheDocument();
  });

  it("KPI card 'Active Subscriptions' links to subscriptions filtered by Active", async () => {
    renderPage();
    await screen.findByText("8"); // active subscriptions value
    const link = screen.getByRole("link", { name: /active subscriptions/i });
    expect(link).toHaveAttribute("href", "/platform/subscriptions?status=Active");
  });

  it("KPI card 'Past Due' links to subscriptions filtered by PastDue", async () => {
    renderPage();
    await screen.findByText("8");
    const link = screen.getByRole("link", { name: /past due/i });
    expect(link).toHaveAttribute("href", "/platform/subscriptions?status=PastDue");
  });

  it("shows MRR growth percentage in the MRR card subtitle", async () => {
    renderPage();
    expect(await screen.findByText(/\+12\.5% vs last month/i)).toBeInTheDocument();
  });

  it("shows 'No MRR last month' when mrrGrowthPercent is null", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/stats", () =>
        HttpResponse.json({ ...STATS, mrrGrowthPercent: null }),
      ),
    );
    renderPage();
    expect(await screen.findByText(/no mrr last month/i)).toBeInTheDocument();
  });

  it("shows 'cancelling at period end' in the MRR card subtitle when scheduledChurnMrr > 0", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/stats", () =>
        HttpResponse.json({ ...STATS, scheduledChurnMrr: 49 }),
      ),
    );
    renderPage();
    expect(await screen.findByText(/cancelling at period end/i)).toBeInTheDocument();
  });

  it("shows 'discounted this month' in the MRR card subtitle when discountsThisMonth > 0", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/stats", () =>
        HttpResponse.json({ ...STATS, discountsThisMonth: 15 }),
      ),
    );
    renderPage();
    expect(await screen.findByText(/discounted this month/i)).toBeInTheDocument();
  });

  it("renders the ARPA card (not ARPU) as MRR ÷ paying studios", async () => {
    renderPage();
    expect(await screen.findByText("ARPA")).toBeInTheDocument();
    expect(screen.getByText("MRR ÷ paying studios")).toBeInTheDocument();
  });

  it("shows '€X MRR at risk' on the Past Due card when atRiskMrr > 0", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/stats", () =>
        HttpResponse.json({ ...STATS, pastDueStudios: 1, atRiskMrr: 79 }),
      ),
    );
    renderPage();
    expect(await screen.findByText(/mrr at risk/i)).toBeInTheDocument();
  });

  it("shows '€X MRR paused' on the Suspended card when pausedMrr > 0", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/stats", () =>
        HttpResponse.json({ ...STATS, suspendedStudios: 1, pausedMrr: 79 }),
      ),
    );
    renderPage();
    expect(await screen.findByText(/mrr paused/i)).toBeInTheDocument();
  });

  it("shows 'Payment overdue' label for PastDue studios in at-risk widget", async () => {
    renderPage();
    expect(await screen.findByText("Payment overdue")).toBeInTheDocument();
  });

  it("shows count badge on At-Risk section title", async () => {
    renderPage();
    await screen.findByText("GracePeriod Studio");
    // atRisk.length = 2 (one GracePeriod + one PastDue); newStudiosThisMonth = 5 (no conflict)
    expect(screen.getByText("2")).toBeInTheDocument();
  });

  it("shows error state when stats load fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/stats", () =>
        HttpResponse.json({ message: "Server error" }, { status: 500 }),
      ),
    );
    renderPage();
    // Loading state resolves — no KPI data shown, no crash
    await screen.findByText("Platform Overview");
    expect(screen.queryByText("12")).not.toBeInTheDocument();
  });

  it("KPI card 'Total Studios' links to /platform/studios", async () => {
    renderPage();
    await screen.findByText("8");
    const link = screen.getByRole("link", { name: /total studios/i });
    expect(link).toHaveAttribute("href", "/platform/studios");
  });

  it("MRR chart renders 'No MRR data yet.' without crashing on empty history", async () => {
    renderPage();
    expect(await screen.findByText(/no mrr data yet/i)).toBeInTheDocument();
  });

  it("MRR chart shows the estimation caption when at least one visible month is estimated", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/mrr-history", () =>
        HttpResponse.json([
          { month: "2026-05", mrr: 300, isEstimated: true },
          { month: "2026-06", mrr: 392, isEstimated: false },
        ]),
      ),
    );
    renderPage();
    expect(await screen.findByText(/estimated from current subscriptions/i)).toBeInTheDocument();
    expect(screen.queryByText(/all figures recorded/i)).not.toBeInTheDocument();
  });

  it("MRR chart shows a neutral 'All figures recorded' note when no visible month is estimated", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/mrr-history", () =>
        HttpResponse.json([
          { month: "2026-05", mrr: 300, isEstimated: false },
          { month: "2026-06", mrr: 392, isEstimated: false },
        ]),
      ),
    );
    renderPage();
    expect(await screen.findByText(/all figures recorded/i)).toBeInTheDocument();
    expect(screen.queryByText(/estimated from current subscriptions/i)).not.toBeInTheDocument();
  });

  it("MRR chart draws estimated points hollow and recorded points filled", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/mrr-history", () =>
        HttpResponse.json([
          { month: "2026-05", mrr: 300, isEstimated: true },
          { month: "2026-06", mrr: 392, isEstimated: false },
        ]),
      ),
    );
    renderPage();
    await screen.findByText(/estimated from current subscriptions/i);
    expect(document.querySelectorAll("circle[data-estimated='true']")).toHaveLength(1);
  });

  it("both charts use the theme's real color tokens — hsl(var(--primary)) is invalid CSS here and silently renders black/nothing", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/mrr-history", () =>
        HttpResponse.json([
          { month: "2026-05", mrr: 300, isEstimated: true },
          { month: "2026-06", mrr: 392, isEstimated: false },
        ]),
      ),
      http.get("http://localhost/api/v1/platform/mrr-movements", () =>
        HttpResponse.json([
          { month: "2026-05", new: 59, expansion: 0, reactivation: 0, contraction: 0, churn: 0, net: 59 },
          { month: "2026-06", new: 0, expansion: 20, reactivation: 0, contraction: -10, churn: 0, net: 10 },
        ]),
      ),
    );
    renderPage();
    const movements = await screen.findByRole("img", { name: /mrr movements by month/i });
    const trend = screen.getByRole("img", { name: /mrr trend/i });
    for (const svg of [movements, trend]) {
      expect(svg.outerHTML).not.toContain("hsl(var(--");
      expect(svg.outerHTML).toContain("var(--color-primary)");
    }
  });

  it("MRR movements chart shows its empty state when nothing has been recorded", async () => {
    renderPage();
    expect(await screen.findByText(/no revenue movements recorded yet/i)).toBeInTheDocument();
  });

  it("MRR movements chart renders a bar group per month with the net figure in its tooltip", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/mrr-movements", () =>
        HttpResponse.json([
          { month: "2026-05", new: 59, expansion: 0, reactivation: 0, contraction: 0, churn: 0, net: 59 },
          { month: "2026-06", new: 0, expansion: 20, reactivation: 0, contraction: 0, churn: 0, net: 20 },
          { month: "2026-07", new: 0, expansion: 0, reactivation: 0, contraction: -20, churn: -49.17, net: -69.17 },
        ]),
      ),
    );
    renderPage();
    const chart = await screen.findByRole("img", { name: /mrr movements by month/i });
    const titles = Array.from(chart.querySelectorAll("title")).map((t) => t.textContent);
    expect(titles).toHaveLength(3);
    expect(titles[0]).toContain("New €59.00");
    expect(titles[1]).toContain("Expansion €20.00");
    expect(titles[2]).toContain("Churn -€49.17");
    expect(titles[2]).toContain("Net -€69.17");
    // Legend names every segment type
    for (const label of ["New", "Expansion", "Reactivation", "Contraction", "Churn", "Net"]) {
      expect(within(screen.getByRole("list", { name: /legend/i })).getByText(label)).toBeInTheDocument();
    }
  });

  it("retention tiles show '—' when there was no MRR at the start of the measured month", async () => {
    renderPage();
    const grr = (await screen.findByText("Gross Revenue Retention")).closest("div") as HTMLElement;
    const nrr = (await screen.findByText("Net Revenue Retention")).closest("div") as HTMLElement;
    expect(within(grr).getByText("—")).toBeInTheDocument();
    expect(within(nrr).getByText("—")).toBeInTheDocument();
  });

  it("retention tiles show formatted percentages when the ledger has a start MRR", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/revenue-retention", () =>
        HttpResponse.json({ grossRevenueRetention: 0.9474, netRevenueRetention: 1.0526, startMrr: 209, periodStart: "2026-08-01T00:00:00Z" }),
      ),
    );
    renderPage();
    expect(await screen.findByText("94.7%")).toBeInTheDocument();
    expect(screen.getByText("105.3%")).toBeInTheDocument();
  });

  it("retention tiles name the completed month they cover, not 'this month'", async () => {
    renderPage();
    expect(await screen.findByText("August 2026 · excludes expansion")).toBeInTheDocument();
    expect(screen.getByText("August 2026 · incl. expansion")).toBeInTheDocument();
    expect(screen.queryByText(/this month · (excludes|incl\.) expansion/)).not.toBeInTheDocument();
  });

  it("At-Risk row: clicking 'Extend trial' reveals the days input and Confirm button", async () => {
    const user = userEvent.setup();
    renderPage();
    const row = (await screen.findByText("GracePeriod Studio")).closest(".border-b") as HTMLElement;
    await user.click(within(row).getByRole("button", { name: /extend trial/i }));
    expect(within(row).getByRole("spinbutton")).toBeInTheDocument();
    expect(within(row).getByRole("button", { name: /confirm/i })).toBeInTheDocument();
  });

  it("At-Risk row: Confirm calls extendTrial with the row's studioId and entered days", async () => {
    let captured: { studioId: string; body: unknown } | null = null;
    server.use(
      http.patch("http://localhost/api/v1/platform/subscriptions/:studioId/trial", async ({ params, request }) => {
        captured = { studioId: params.studioId as string, body: await request.json() };
        return HttpResponse.json({});
      }),
    );
    const user = userEvent.setup();
    renderPage();
    const row = (await screen.findByText("GracePeriod Studio")).closest(".border-b") as HTMLElement;
    await user.click(within(row).getByRole("button", { name: /extend trial/i }));
    await user.clear(within(row).getByRole("spinbutton"));
    await user.type(within(row).getByRole("spinbutton"), "14");
    await user.click(within(row).getByRole("button", { name: /confirm/i }));

    await waitFor(() => expect(captured).not.toBeNull());
    expect(captured!.studioId).toBe("s1");
    expect(captured!.body).toEqual({ additionalDays: 14 });
  });

  it("At-Risk row: the '→' link navigates to /platform/studios with the row highlighted", async () => {
    renderPage();
    const row = (await screen.findByText("GracePeriod Studio")).closest(".border-b") as HTMLElement;
    const link = within(row).getByRole("link", { name: "→" });
    expect(link).toHaveAttribute("href", "/platform/studios");
  });
});
