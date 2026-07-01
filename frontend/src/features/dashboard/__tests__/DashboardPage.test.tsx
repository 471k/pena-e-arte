import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { billingApi } from "@/features/billing/billingApi";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import { artistsApi } from "@/features/artists/artistsApi";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { depositRulesApi } from "@/features/deposit-rules/depositRulesApi";
import { DashboardPage } from "@/features/dashboard/components/DashboardPage";
import type { SubscriptionResponse } from "@/features/billing/billing.types";
import type { AppointmentResponse } from "@/features/appointments/appointment.types";
import type { ArtistResponse } from "@/features/artists/artistsApi";
import type { PaymentResponse } from "@/features/payments/payment.types";

// ── SignalR mock ──────────────────────────────────────────────────────────────

vi.mock("@microsoft/signalr", () => {
  function HubConnectionBuilder(this: Record<string, unknown>) {
    this.withUrl               = vi.fn().mockReturnValue(this);
    this.withAutomaticReconnect = vi.fn().mockReturnValue(this);
    this.configureLogging      = vi.fn().mockReturnValue(this);
    this.build                 = vi.fn(() => ({
      on:     vi.fn(),
      start:  vi.fn().mockResolvedValue(undefined),
      invoke: vi.fn().mockResolvedValue(undefined),
      stop:   vi.fn().mockResolvedValue(undefined),
    }));
  }
  return { HubConnectionBuilder, LogLevel: { Warning: 2 } };
});

// ── Seed data ─────────────────────────────────────────────────────────────────

const IN_7_DAYS  = new Date(Date.now() + 7  * 86_400_000).toISOString();
const IN_1_DAY   = new Date(Date.now() + 1  * 86_400_000).toISOString();
const IN_30_DAYS = new Date(Date.now() + 30 * 86_400_000).toISOString();

const BASE_SUB: SubscriptionResponse = {
  id:                   "sub-0001",
  studioId:             "stud-0001",
  planId:               "plan-1",
  pendingPlanId:        null,
  status:               "Active",
  trialExpiresAt:       IN_7_DAYS,
  currentPeriodEnd:     IN_30_DAYS,
  gracePeriodEnd:       IN_7_DAYS,
  stripeSubscriptionId: null,
};

const SUB_ACTIVE:      SubscriptionResponse = { ...BASE_SUB, status: "Active" };
const SUB_TRIALING:    SubscriptionResponse = { ...BASE_SUB, status: "Trialing",    planId: null, trialExpiresAt: IN_7_DAYS };
const SUB_TRIALING_1D: SubscriptionResponse = { ...BASE_SUB, status: "Trialing",    planId: null, trialExpiresAt: IN_1_DAY  };
const SUB_GRACE:       SubscriptionResponse = { ...BASE_SUB, status: "GracePeriod", planId: null, gracePeriodEnd: IN_7_DAYS };
const SUB_GRACE_1D:    SubscriptionResponse = { ...BASE_SUB, status: "GracePeriod", planId: null, gracePeriodEnd: IN_1_DAY  };
const SUB_PAST_DUE:    SubscriptionResponse = { ...BASE_SUB, status: "PastDue" };
const SUB_CANCELLED:   SubscriptionResponse = { ...BASE_SUB, status: "Cancelled",   planId: null };

const ARTIST: ArtistResponse = {
  id:              "artist-0001",
  studioId:        "stud-0001",
  firstName:       "Ana",
  lastName:        "Costa",
  email:           "ana@ink.test",
  specializations: null,
  hourlyRate:      null,
  portfolioImages: [],
  slug: null,
  userId:          null,
  createdAt:       "2024-01-01T00:00:00Z",
  updatedAt:       "2024-01-01T00:00:00Z",
};

const NOW_ISO = new Date().toISOString();

const APPOINTMENT: AppointmentResponse = {
  id:              "appt-0001",
  studioId:        "stud-0001",
  artistId:        "artist-0001",
  clientId:        "client-0001",
  date:            NOW_ISO,
  endDate:         NOW_ISO,
  durationMinutes: 60,
  status:          "Confirmed",
  depositStatus:   "Paid",
  depositAmount:   50,
  notes:           null,
  createdAt:       NOW_ISO,
};

const CASH_PAYMENT: PaymentResponse = {
  id:                    "pay-0001",
  appointmentId:         "appt-0001",
  amount:                75,
  status:                "CashPending",
  method:                "Cash",
  stripePaymentIntentId: null,
  clientSecret:          null,
  cashNote:              null,
  paidAt:                null,
  clientName:            "João Silva",
  appointmentDate:       null,
};

// ── MSW server ────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/billing/subscription", () =>
    HttpResponse.json(SUB_ACTIVE),
  ),
  http.get("http://localhost/api/v1/appointments", () =>
    HttpResponse.json([]),
  ),
  http.get("http://localhost/api/v1/artists", () =>
    HttpResponse.json([ARTIST]),
  ),
  http.get("http://localhost/api/v1/payments", () =>
    HttpResponse.json([]),
  ),
  http.get("http://localhost/api/v1/deposit-rules", () =>
    HttpResponse.json([]),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Store / render helpers ────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                              authReducer,
      ui:                                uiReducer,
      [billingApi.reducerPath]:          billingApi.reducer,
      [appointmentsApi.reducerPath]:     appointmentsApi.reducer,
      [artistsApi.reducerPath]:          artistsApi.reducer,
      [paymentsApi.reducerPath]:         paymentsApi.reducer,
      [depositRulesApi.reducerPath]:     depositRulesApi.reducer,
    },
    middleware: (gd) =>
      gd().concat(
        billingApi.middleware,
        appointmentsApi.middleware,
        artistsApi.middleware,
        paymentsApi.middleware,
        depositRulesApi.middleware,
      ),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "owner@ink.test" }, token: "fake-token", tenantId: "stud-0001", role: "owner", pendingReferralCode: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false },
    },
  });
}

function renderPage() {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/dashboard"]}>
        <Routes>
          <Route path="/dashboard"          element={<DashboardPage />} />
          <Route path="/billing"            element={<div data-testid="billing-page" />} />
          <Route path="/billing/subscribe"  element={<div data-testid="subscribe-page" />} />
          <Route path="/schedule"           element={<div data-testid="schedule-page" />} />
          <Route path="/appointments/new"  element={<div data-testid="new-appointment-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("DashboardPage", () => {

  // ── Header ──────────────────────────────────────────────────────────────────

  it("renders the Dashboard header text", () => {
    renderPage();
    expect(screen.getByText("Dashboard")).toBeInTheDocument();
  });

  // ── Today section ────────────────────────────────────────────────────────────

  it("shows the Today section heading", async () => {
    renderPage();
    // The TodaySection heading is a <span>; the stat card label is a <div> — use selector to disambiguate
    expect(await screen.findByText("Today", { selector: "span" })).toBeInTheDocument();
  });

  it("shows skeleton rows while appointments are fetching", () => {
    renderPage();
    expect(screen.getAllByTestId("appointment-skeleton")).toHaveLength(3);
  });

  it("shows 'No appointments today.' when the appointment list is empty", async () => {
    renderPage();
    expect(await screen.findByText("No appointments today.")).toBeInTheDocument();
  });

  it("shows an error message when the appointments fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json({ message: "Server error" }, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText("Failed to load appointments.")).toBeInTheDocument();
  });

  it("shows appointment count in Today header when appointments exist", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json([APPOINTMENT]),
      ),
    );
    renderPage();
    expect(await screen.findByText("1 appointment")).toBeInTheDocument();
  });

  it("shows the appointment status badge when appointments exist", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json([APPOINTMENT]),
      ),
    );
    renderPage();
    expect(await screen.findByText("Confirmed")).toBeInTheDocument();
  });

  it("shows the artist name resolved from the appointment's artistId", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json([APPOINTMENT]),
      ),
    );
    renderPage();
    expect(await screen.findByText("Ana Costa")).toBeInTheDocument();
  });

  it("shows '—' for an appointment with an unknown artistId", async () => {
    const unknownArtistAppt: AppointmentResponse = { ...APPOINTMENT, artistId: "artist-unknown" };
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json([unknownArtistAppt]),
      ),
    );
    renderPage();
    expect(await screen.findByText("—")).toBeInTheDocument();
  });

  it("View schedule button navigates to /schedule", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("No appointments today.");

    await user.click(screen.getByRole("button", { name: /view schedule/i }));

    expect(screen.getByTestId("schedule-page")).toBeInTheDocument();
  });

  // ── Cash pending section ──────────────────────────────────────────────────

  it("does not show the Awaiting Cash section when there are no CashPending payments", async () => {
    renderPage();
    await screen.findByText("No appointments today.");
    expect(screen.queryByText("Awaiting Cash")).not.toBeInTheDocument();
  });

  it("shows the Awaiting Cash section when CashPending payments exist", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json([CASH_PAYMENT]),
      ),
    );
    renderPage();
    expect(await screen.findByText("Awaiting Cash")).toBeInTheDocument();
  });

  it("shows the client name and amount for each CashPending payment", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json([CASH_PAYMENT]),
      ),
    );
    renderPage();
    await screen.findByText("Awaiting Cash");
    expect(screen.getByText("João Silva")).toBeInTheDocument();
    expect(screen.getByText(/75\.00/)).toBeInTheDocument();
  });

  it("shows the count of CashPending payments in the section header", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json([CASH_PAYMENT]),
      ),
    );
    renderPage();
    await screen.findByText("Awaiting Cash");
    // The count badge sits right after the section label
    expect(screen.getByText("1")).toBeInTheDocument();
  });

  // ── Subscription banner — Active ───────────────────────────────────────────

  it("Active → no subscription banner is shown", async () => {
    renderPage();
    await screen.findByText("No appointments today.");
    // None of the banner CTAs should appear
    expect(screen.queryByRole("button", { name: /^subscribe$/i     })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /subscribe now/i   })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /update billing/i  })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /reactivate/i      })).not.toBeInTheDocument();
  });

  // ── Subscription banner — Trialing ────────────────────────────────────────

  it("Trialing → renders the trial-ends banner", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_TRIALING),
      ),
    );
    renderPage();
    expect(await screen.findByText(/Trial ends in/i)).toBeInTheDocument();
  });

  it("Trialing → countdown shows 7 days when 7 days remain", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_TRIALING),
      ),
    );
    renderPage();
    expect(await screen.findByText(/Trial ends in 7 days\./i)).toBeInTheDocument();
  });

  it("Trialing (1 day left) → countdown uses the singular 'day'", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_TRIALING_1D),
      ),
    );
    renderPage();
    const el = await screen.findByText(/Trial ends in 1 day\./i);
    expect(el.textContent).not.toMatch(/days/);
  });

  it("Trialing → CTA button is labelled 'Subscribe'", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_TRIALING),
      ),
    );
    renderPage();
    expect(await screen.findByRole("button", { name: /^Subscribe$/i })).toBeInTheDocument();
  });

  it("Trialing → 'Subscribe' CTA navigates to /billing/subscribe", async () => {
    const user = userEvent.setup();
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_TRIALING),
      ),
    );
    renderPage();
    await screen.findByRole("button", { name: /^Subscribe$/i });

    await user.click(screen.getByRole("button", { name: /^Subscribe$/i }));

    expect(screen.getByTestId("subscribe-page")).toBeInTheDocument();
  });

  // ── Subscription banner — GracePeriod ────────────────────────────────────

  it("GracePeriod → banner mentions read-only mode", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_GRACE),
      ),
    );
    renderPage();
    expect(await screen.findByText(/read-only mode/i)).toBeInTheDocument();
  });

  it("GracePeriod → banner text includes days remaining", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_GRACE),
      ),
    );
    renderPage();
    expect(await screen.findByText(/7 days left/i)).toBeInTheDocument();
  });

  it("GracePeriod (1 day left) → countdown uses the singular 'day'", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_GRACE_1D),
      ),
    );
    renderPage();
    const el = await screen.findByText(/1 day left\./i);
    expect(el.textContent).not.toMatch(/days/);
  });

  it("GracePeriod → CTA button is labelled 'Subscribe now'", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_GRACE),
      ),
    );
    renderPage();
    expect(await screen.findByRole("button", { name: /subscribe now/i })).toBeInTheDocument();
  });

  it("GracePeriod → 'Subscribe now' CTA navigates to /billing/subscribe", async () => {
    const user = userEvent.setup();
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_GRACE),
      ),
    );
    renderPage();
    await screen.findByRole("button", { name: /subscribe now/i });

    await user.click(screen.getByRole("button", { name: /subscribe now/i }));

    expect(screen.getByTestId("subscribe-page")).toBeInTheDocument();
  });

  // ── Subscription banner — PastDue ────────────────────────────────────────

  it("PastDue → banner mentions the failed payment", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_PAST_DUE),
      ),
    );
    renderPage();
    expect(await screen.findByText(/Last payment failed/i)).toBeInTheDocument();
  });

  it("PastDue → CTA button is labelled 'Update billing'", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_PAST_DUE),
      ),
    );
    renderPage();
    expect(await screen.findByRole("button", { name: /update billing/i })).toBeInTheDocument();
  });

  it("PastDue → 'Update billing' CTA navigates to /billing", async () => {
    const user = userEvent.setup();
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_PAST_DUE),
      ),
    );
    renderPage();
    await screen.findByRole("button", { name: /update billing/i });

    await user.click(screen.getByRole("button", { name: /update billing/i }));

    expect(screen.getByTestId("billing-page")).toBeInTheDocument();
  });

  // ── Subscription banner — Cancelled ──────────────────────────────────────

  it("Cancelled → banner mentions the cancelled subscription", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_CANCELLED),
      ),
    );
    renderPage();
    expect(await screen.findByText(/Subscription cancelled/i)).toBeInTheDocument();
  });

  it("Cancelled → CTA button is labelled 'Reactivate'", async () => {
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_CANCELLED),
      ),
    );
    renderPage();
    expect(await screen.findByRole("button", { name: /reactivate/i })).toBeInTheDocument();
  });

  it("Cancelled → 'Reactivate' CTA navigates to /billing/subscribe", async () => {
    const user = userEvent.setup();
    server.use(
      http.get("http://localhost/api/v1/billing/subscription", () =>
        HttpResponse.json(SUB_CANCELLED),
      ),
    );
    renderPage();
    await screen.findByRole("button", { name: /reactivate/i });

    await user.click(screen.getByRole("button", { name: /reactivate/i }));

    expect(screen.getByTestId("subscribe-page")).toBeInTheDocument();
  });

  // ── Empty state CTAs ────────────────────────────────────────────────────────

  it("empty state shows 'Book Appointment' button", async () => {
    renderPage();
    expect(await screen.findByRole("button", { name: /^Book Appointment$/i })).toBeInTheDocument();
  });

  it("'Book Appointment' button navigates to /appointments/new", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("No appointments today.");

    await user.click(screen.getByRole("button", { name: /^Book Appointment$/i }));

    expect(screen.getByTestId("new-appointment-page")).toBeInTheDocument();
  });

  it("empty state shows 'View this week' button", async () => {
    renderPage();
    expect(await screen.findByRole("button", { name: /view this week/i })).toBeInTheDocument();
  });

  it("'View this week →' button navigates to /schedule", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("button", { name: /view this week/i });

    await user.click(screen.getByRole("button", { name: /view this week/i }));

    expect(screen.getByTestId("schedule-page")).toBeInTheDocument();
  });

  // ── Header CTA ──────────────────────────────────────────────────────────────

  it("header shows '+ Book Appointment' button always", async () => {
    // With appointments present, the empty state is gone but header CTA stays
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json([APPOINTMENT]),
      ),
    );
    renderPage();
    await screen.findByText("Ana Costa");
    expect(screen.getByRole("button", { name: /\+ book appointment/i })).toBeInTheDocument();
  });

  it("header '+ Book Appointment' button navigates to /appointments/new", async () => {
    const user = userEvent.setup();
    renderPage();
    // Wait for page to settle (no appointments)
    await screen.findByText("No appointments today.");

    await user.click(screen.getByRole("button", { name: /\+ book appointment/i }));

    expect(screen.getByTestId("new-appointment-page")).toBeInTheDocument();
  });

  // ── KPI stat cards ──────────────────────────────────────────────────────────

  it("stat cards section renders Today, This Week, and Deposits Due labels", async () => {
    renderPage();
    await screen.findByText("No appointments today.");
    expect(screen.getByTestId("stat-today")).toBeInTheDocument();
    expect(screen.getByTestId("stat-week")).toBeInTheDocument();
    expect(screen.getByTestId("stat-deposits")).toBeInTheDocument();
  });

  it("Today stat shows 0 when no appointments", async () => {
    renderPage();
    await screen.findByText("No appointments today.");
    expect(within(screen.getByTestId("stat-today")).getByText("0")).toBeInTheDocument();
  });

  it("Today stat shows correct count when appointments exist", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json([APPOINTMENT]),
      ),
    );
    renderPage();
    await screen.findByText("Ana Costa");
    expect(within(screen.getByTestId("stat-today")).getByText("1")).toBeInTheDocument();
  });

  it("stat cards show skeleton while appointments are loading", () => {
    renderPage();
    // Before data arrives, stat-today should show a skeleton (not the number)
    const todayCard = screen.getByTestId("stat-today");
    // The Skeleton component renders — the number span is absent
    expect(within(todayCard).queryByRole("heading")).not.toBeInTheDocument();
    // And no numeric text yet
    expect(within(todayCard).queryByText("0")).not.toBeInTheDocument();
  });

  it("Deposits Due stat shows count of Pending-deposit appointments", async () => {
    const pendingDepositAppt: AppointmentResponse = {
      ...APPOINTMENT,
      id:            "appt-deposit-pending",
      depositStatus: "Pending",
    };
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json([pendingDepositAppt]),
      ),
    );
    renderPage();
    await screen.findByText("Ana Costa");
    expect(within(screen.getByTestId("stat-deposits")).getByText("1")).toBeInTheDocument();
  });
});
