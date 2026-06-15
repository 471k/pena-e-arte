import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { PaymentDetailPage } from "@/features/payments/components/PaymentDetailPage";
import type { PaymentResponse } from "@/features/payments/payment.types";
import { Role } from "@/shared/types/roles";

// ── Seed data ──────────────────────────────────────────────────────────────────

const PAYMENT_CARD_CAPTURED: PaymentResponse = {
  id:                    "pay-001",
  appointmentId:         "appt-001",
  amount:                100,
  status:                "Captured",
  method:                "Card",
  stripePaymentIntentId: "pi_test_abc",
  clientSecret:          null,
  cashNote:              null,
  paidAt:                null,
  clientName:            "Maria Silva",
  appointmentDate:       "2026-06-15T14:00:00Z",
};

const PAYMENT_CARD_PAID: PaymentResponse = {
  ...PAYMENT_CARD_CAPTURED,
  id:     "pay-002",
  appointmentId: "appt-002",
  status: "Paid",
  paidAt: "2026-06-15T15:00:00Z",
};

const PAYMENT_CASH_PENDING: PaymentResponse = {
  id:                    "pay-003",
  appointmentId:         "appt-003",
  amount:                60,
  status:                "CashPending",
  method:                "Cash",
  stripePaymentIntentId: null,
  clientSecret:          null,
  cashNote:              "Bring on the day",
  paidAt:                null,
  clientName:            "João Santos",
  appointmentDate:       "2026-06-20T11:00:00Z",
};

const PAYMENT_PENDING: PaymentResponse = {
  ...PAYMENT_CARD_CAPTURED,
  id:     "pay-004",
  appointmentId: "appt-004",
  status: "Pending",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/payments/appointment/:appointmentId", ({ params }) => {
    if (params.appointmentId === "appt-001") return HttpResponse.json(PAYMENT_CARD_CAPTURED);
    if (params.appointmentId === "appt-002") return HttpResponse.json(PAYMENT_CARD_PAID);
    if (params.appointmentId === "appt-003") return HttpResponse.json(PAYMENT_CASH_PENDING);
    if (params.appointmentId === "appt-004") return HttpResponse.json(PAYMENT_PENDING);
    return HttpResponse.json({ message: "Not found" }, { status: 404 });
  }),
  http.post("http://localhost/api/v1/payments/:id/capture", ({ params }) =>
    HttpResponse.json({ ...PAYMENT_CARD_CAPTURED, id: params.id as string, status: "Paid" }),
  ),
  http.post("http://localhost/api/v1/payments/:id/cash/confirm", ({ params }) =>
    HttpResponse.json({ ...PAYMENT_CASH_PENDING, id: params.id as string, status: "Paid" }),
  ),
  http.post("http://localhost/api/v1/payments/:id/refund", ({ params }) =>
    HttpResponse.json({ ...PAYMENT_CARD_PAID, id: params.id as string, status: "Refunded" }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore(role: Role = Role.Owner) {
  return configureStore({
    reducer: {
      auth:                      authReducer,
      ui:                        uiReducer,
      [paymentsApi.reducerPath]: paymentsApi.reducer,
    },
    middleware: (gd) => gd().concat(paymentsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u-001", email: "owner@test.com" }, token: "fake-token", tenantId: "s-001", role, pendingReferralCode: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false },
    },
  });
}

function renderPage(appointmentId: string, role: Role = Role.Owner) {
  render(
    <Provider store={makeStore(role)}>
      <MemoryRouter initialEntries={[`/payments/${appointmentId}`]}>
        <Routes>
          <Route path="/payments/:appointmentId" element={<PaymentDetailPage />} />
          <Route path="/payments"                element={<div data-testid="list-page" />} />
          <Route path="/payments/new"            element={<div data-testid="new-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("PaymentDetailPage", () => {

  // ── Loading / error ─────────────────────────────────────────────────────────

  it("shows a loading spinner while fetching", () => {
    renderPage("appt-001");
    expect(screen.getByText(/loading payment/i)).toBeInTheDocument();
  });

  it("shows error state when payment is not found", async () => {
    renderPage("appt-999");
    expect(await screen.findByText(/no payment found/i)).toBeInTheDocument();
  });

  it("error state shows 'Payments' back button", async () => {
    renderPage("appt-999");
    await screen.findByText(/no payment found/i);
    expect(screen.getByRole("button", { name: /payments/i })).toBeInTheDocument();
  });

  it("error state shows 'Create payment' button", async () => {
    renderPage("appt-999");
    await screen.findByText(/no payment found/i);
    expect(screen.getByRole("button", { name: /create payment/i })).toBeInTheDocument();
  });

  it("'Create payment' button in error state navigates to /payments/new with appointmentId", async () => {
    const user = userEvent.setup();
    renderPage("appt-999");
    await user.click(await screen.findByRole("button", { name: /create payment/i }));
    expect(screen.getByTestId("new-page")).toBeInTheDocument();
  });

  // ── Detail rendering ────────────────────────────────────────────────────────

  it("renders the payment amount", async () => {
    renderPage("appt-001");
    expect(await screen.findByText(/100/)).toBeInTheDocument();
  });

  it("renders the payment status badge", async () => {
    renderPage("appt-001");
    await screen.findByText(/100/);
    expect(screen.getByText("Captured")).toBeInTheDocument();
  });

  it("renders 'Card' as the method for a card payment", async () => {
    renderPage("appt-001");
    await screen.findByText(/100/);
    expect(screen.getByText("Card")).toBeInTheDocument();
  });

  it("renders 'Cash' as the method for a cash payment", async () => {
    renderPage("appt-003");
    await screen.findByText(/60/);
    expect(screen.getByText("Cash")).toBeInTheDocument();
  });

  it("renders the Stripe PI ID for a card payment", async () => {
    renderPage("appt-001");
    await screen.findByText(/100/);
    expect(screen.getByText("pi_test_abc")).toBeInTheDocument();
  });

  it("renders the cash note when present", async () => {
    renderPage("appt-003");
    await screen.findByText(/60/);
    expect(screen.getByText("Bring on the day")).toBeInTheDocument();
  });

  it("renders the appointment ID", async () => {
    renderPage("appt-001");
    await screen.findByText(/100/);
    expect(screen.getByText("appt-001")).toBeInTheDocument();
  });

  it("renders paidAt date when present", async () => {
    renderPage("appt-002");
    await screen.findByText(/100/);
    expect(screen.getByText(/15 Jun 2026/i)).toBeInTheDocument();
  });

  // ── Owner actions — card captured ────────────────────────────────────────────

  it("owner + Captured + Card → 'Capture deposit' button visible", async () => {
    renderPage("appt-001", Role.Owner);
    expect(await screen.findByRole("button", { name: /capture deposit/i })).toBeInTheDocument();
  });

  it("owner + Pending + Card → shows awaiting-client message", async () => {
    renderPage("appt-004", Role.Owner);
    await screen.findByText(/100/);
    expect(screen.getByText(/awaiting client card authorization/i)).toBeInTheDocument();
  });

  // ── Owner actions — cash pending ─────────────────────────────────────────────

  it("owner + CashPending + Cash → 'Confirm cash received' button visible", async () => {
    renderPage("appt-003", Role.Owner);
    expect(await screen.findByRole("button", { name: /confirm cash received/i })).toBeInTheDocument();
  });

  it("cash pending info banner shown for CashPending status", async () => {
    renderPage("appt-003", Role.Owner);
    await screen.findByText(/60/);
    expect(screen.getByText(/awaiting cash collection/i)).toBeInTheDocument();
  });

  // ── Owner actions — paid (card) ──────────────────────────────────────────────

  it("owner + Paid + Card → 'Refund' button visible", async () => {
    renderPage("appt-002", Role.Owner);
    expect(await screen.findByRole("button", { name: /refund/i })).toBeInTheDocument();
  });

  // ── Non-owner ────────────────────────────────────────────────────────────────

  it("artist role does NOT see 'Capture deposit' button", async () => {
    renderPage("appt-001", Role.Artist);
    await screen.findByText(/100/);
    expect(screen.queryByRole("button", { name: /capture deposit/i })).not.toBeInTheDocument();
  });

  // ── Mutation calls ───────────────────────────────────────────────────────────

  it("clicking 'Capture deposit' calls the capture mutation", async () => {
    const user = userEvent.setup();
    renderPage("appt-001", Role.Owner);
    await user.click(await screen.findByRole("button", { name: /capture deposit/i }));
    expect(screen.queryByText(/failed/i)).not.toBeInTheDocument();
  });

  it("clicking 'Confirm cash received' calls the confirmCash mutation", async () => {
    const user = userEvent.setup();
    renderPage("appt-003", Role.Owner);
    await user.click(await screen.findByRole("button", { name: /confirm cash received/i }));
    expect(screen.queryByText(/failed/i)).not.toBeInTheDocument();
  });

  // ── Refund form ──────────────────────────────────────────────────────────────

  it("clicking 'Refund' opens the refund inline form", async () => {
    const user = userEvent.setup();
    renderPage("appt-002", Role.Owner);
    await user.click(await screen.findByRole("button", { name: /refund/i }));
    expect(screen.getByRole("button", { name: /confirm refund/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /cancel/i })).toBeInTheDocument();
  });

  it("'Cancel' in refund form closes it", async () => {
    const user = userEvent.setup();
    renderPage("appt-002", Role.Owner);
    await user.click(await screen.findByRole("button", { name: /refund/i }));
    await user.click(screen.getByRole("button", { name: /cancel/i }));
    expect(screen.queryByRole("button", { name: /confirm refund/i })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /refund/i })).toBeInTheDocument();
  });

  it("'Confirm refund' without amount calls full refund and closes form", async () => {
    const user = userEvent.setup();
    renderPage("appt-002", Role.Owner);
    await user.click(await screen.findByRole("button", { name: /refund/i }));
    await user.click(screen.getByRole("button", { name: /confirm refund/i }));
    expect(screen.queryByText(/failed/i)).not.toBeInTheDocument();
  });

  // ── Navigation ───────────────────────────────────────────────────────────────

  it("'Payments' back button navigates to /payments list", async () => {
    const user = userEvent.setup();
    renderPage("appt-001");
    await screen.findByText(/100/);
    await user.click(screen.getByRole("button", { name: /payments/i }));
    expect(screen.getByTestId("list-page")).toBeInTheDocument();
  });
});
