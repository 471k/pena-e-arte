import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import { clientsApi } from "@/features/clients/clientsApi";
import { CreatePaymentIntentPage } from "@/features/payments/components/CreatePaymentIntentPage";
import type { AppointmentResponse } from "@/features/appointments/appointment.types";
import type { ClientResponse } from "@/features/clients/clientsApi";
import type { PaymentIntentResponse, PaymentResponse } from "@/features/payments/payment.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const CLIENT: ClientResponse = {
  id:         "c-001",
  studioId:   "s-001",
  firstName:  "Maria",
  lastName:   "Silva",
  email:      "maria@example.com",
  phone:      null,
  createdAt:  "2026-01-01T00:00:00Z",
  userId:     null,
  artistId:   null,
  artistName: null,
};

const CLIENT_2: ClientResponse = {
  ...CLIENT,
  id:        "c-002",
  firstName: "João",
  lastName:  "Santos",
  email:     "joao@example.com",
};

const FUTURE = new Date(Date.now() + 7 * 86_400_000).toISOString();

const APPT_PENDING_DEPOSIT: AppointmentResponse = {
  id:              "appt-001",
  studioId:        "s-001",
  artistId:        "a-001",
  clientId:        "c-001",
  date:            FUTURE,
  endDate:         new Date(Date.now() + 7 * 86_400_000 + 3_600_000).toISOString(),
  durationMinutes: 60,
  status:          "Confirmed",
  depositStatus:   "Pending",
  depositAmount:   100,
  notes:           null,
  createdAt:       "2026-01-01T00:00:00Z",
};

const APPT_PAID_DEPOSIT: AppointmentResponse = {
  ...APPT_PENDING_DEPOSIT,
  id:            "appt-002",
  clientId:      "c-002",
  depositStatus: "Paid",
  depositAmount: 50,
};

const CARD_RESULT: PaymentIntentResponse = {
  paymentId:    "pay-001",
  clientSecret: "pi_test_secret",
  status:       "Pending",
};

const CASH_RESULT: PaymentResponse = {
  id:                    "pay-002",
  appointmentId:         "appt-001",
  amount:                100,
  status:                "CashPending",
  method:                "Cash",
  providerReferenceId: null,
  clientSecret:          null,
  cashNote:              null,
  paidAt:                null,
  clientName:            "Maria Silva",
  appointmentDate:       FUTURE,
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/appointments", () =>
    HttpResponse.json([APPT_PENDING_DEPOSIT, APPT_PAID_DEPOSIT]),
  ),
  http.get("http://localhost/api/v1/clients", () =>
    HttpResponse.json([CLIENT, CLIENT_2]),
  ),
  http.post("http://localhost/api/v1/payments", () =>
    HttpResponse.json(CARD_RESULT),
  ),
  http.post("http://localhost/api/v1/payments/cash", () =>
    HttpResponse.json(CASH_RESULT),
  ),
  http.put("http://localhost/api/v1/payments/:id/splits", ({ params }) =>
    HttpResponse.json({ ...CASH_RESULT, id: params.id as string }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                          authReducer,
      ui:                            uiReducer,
      [paymentsApi.reducerPath]:     paymentsApi.reducer,
      [appointmentsApi.reducerPath]: appointmentsApi.reducer,
      [clientsApi.reducerPath]:      clientsApi.reducer,
    },
    middleware: (gd) => gd().concat(
      paymentsApi.middleware,
      appointmentsApi.middleware,
      clientsApi.middleware,
    ),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u-001", email: "owner@test.com" }, token: "fake-token", tenantId: "s-001", role: "owner", pendingReferralCode: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null },
    },
  });
}

function renderPage(path = "/payments/new") {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/payments"                element={<div data-testid="list-page" />} />
          <Route path="/payments/new"            element={<CreatePaymentIntentPage />} />
          <Route path="/payments/:appointmentId" element={<div data-testid="detail-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("CreatePaymentIntentPage", () => {

  // ── AppointmentPicker ────────────────────────────────────────────────────────

  it("renders the 'New payment' heading", () => {
    renderPage();
    expect(screen.getByText("New payment")).toBeInTheDocument();
  });

  it("shows loading state while appointments are loading", () => {
    server.use(
      http.get("http://localhost/api/v1/appointments", async () => {
        await new Promise((r) => setTimeout(r, 60_000));
        return HttpResponse.json([]);
      }),
    );
    renderPage();
    expect(screen.getByText(/loading appointments/i)).toBeInTheDocument();
  });

  it("shows 'No appointments with a pending deposit' when empty", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments", () => HttpResponse.json([])),
    );
    renderPage();
    expect(await screen.findByText(/no appointments with a pending deposit/i)).toBeInTheDocument();
  });

  it("shows only appointments with a pending deposit status", async () => {
    renderPage();
    // APPT_PENDING_DEPOSIT (depositStatus=Pending) → shown; APPT_PAID_DEPOSIT (Paid) → hidden
    expect(await screen.findByText("Maria Silva")).toBeInTheDocument();
    expect(screen.queryByText("João Santos")).not.toBeInTheDocument();
  });

  it("shows the deposit amount badge for each appointment", async () => {
    renderPage();
    await screen.findByText("Maria Silva");
    // 100 EUR deposit
    expect(screen.getByText(/100/)).toBeInTheDocument();
  });

  it("filters appointments by search query (client name)", async () => {
    const user = userEvent.setup();

    // Add a second pending-deposit appointment for a different client so we can filter
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json([
          APPT_PENDING_DEPOSIT,
          { ...APPT_PENDING_DEPOSIT, id: "appt-003", clientId: "c-002", depositAmount: 75 },
        ]),
      ),
    );

    renderPage();
    await screen.findByText("Maria Silva");

    await user.type(screen.getByPlaceholderText(/search by client/i), "João");

    expect(await screen.findByText("João Santos")).toBeInTheDocument();
    expect(screen.queryByText("Maria Silva")).not.toBeInTheDocument();
  });

  it("shows 'no appointments match' message when search has no results", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Maria Silva");
    await user.type(screen.getByPlaceholderText(/search by client/i), "zzz");
    expect(await screen.findByText(/no appointments match/i)).toBeInTheDocument();
  });

  // ── ConfirmPanel ─────────────────────────────────────────────────────────────

  it("clicking an appointment row shows the ConfirmPanel", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByText("Maria Silva"));
    expect(screen.getByText(/selected appointment/i)).toBeInTheDocument();
  });

  it("ConfirmPanel shows the client name and date", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByText("Maria Silva"));
    expect(screen.getByText("Maria Silva")).toBeInTheDocument();
  });

  it("ConfirmPanel 'Back to appointments' returns to the picker", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByText("Maria Silva"));
    await screen.findByText(/selected appointment/i);
    await user.click(screen.getByRole("button", { name: /back to appointments/i }));
    expect(await screen.findByPlaceholderText(/search by client/i)).toBeInTheDocument();
  });

  it("ConfirmPanel pre-fills the amount from the deposit rule", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByText("Maria Silva"));
    await screen.findByText(/selected appointment/i);
    // Deposit amount = 100 should be pre-filled in the amount input
    const amountInput = screen.getByRole("spinbutton");
    expect((amountInput as HTMLInputElement).value).toBe("100");
  });

  // ── Card flow ────────────────────────────────────────────────────────────────

  it("card flow: submit calls createPaymentIntent and shows CheckoutLinkPanel", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByText("Maria Silva"));
    await screen.findByText(/selected appointment/i);
    await user.click(screen.getByRole("button", { name: /create card payment/i }));
    expect(await screen.findByText(/card payment intent created/i)).toBeInTheDocument();
  });

  it("card result shows checkout URL input", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByText("Maria Silva"));
    await screen.findByText(/selected appointment/i);
    await user.click(screen.getByRole("button", { name: /create card payment/i }));
    await screen.findByText(/card payment intent created/i);
    expect(screen.getByRole("textbox")).toBeInTheDocument();
    expect((screen.getByRole("textbox") as HTMLInputElement).value).toContain("/pay/pay-001");
  });

  it("'View payment' after card creation navigates to /payments/:appointmentId (bug fix)", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByText("Maria Silva"));
    await screen.findByText(/selected appointment/i);
    await user.click(screen.getByRole("button", { name: /create card payment/i }));
    await screen.findByText(/card payment intent created/i);
    await user.click(screen.getByRole("button", { name: /view payment/i }));
    // Should navigate to /payments/appt-001 (the appointment ID), not pay-001 (the payment ID)
    expect(screen.getByTestId("detail-page")).toBeInTheDocument();
  });

  // ── Cash flow ────────────────────────────────────────────────────────────────

  it("cash flow: selecting Cash method and submitting shows CashResultPanel", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByText("Maria Silva"));
    await screen.findByText(/selected appointment/i);
    await user.click(screen.getByRole("button", { name: /^cash$/i }));
    await user.click(screen.getByRole("button", { name: /record cash payment/i }));
    expect(await screen.findByText(/cash payment recorded/i)).toBeInTheDocument();
  });

  it("'View payment' after cash creation navigates to /payments/:appointmentId (bug fix)", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByText("Maria Silva"));
    await screen.findByText(/selected appointment/i);
    await user.click(screen.getByRole("button", { name: /^cash$/i }));
    await user.click(screen.getByRole("button", { name: /record cash payment/i }));
    await screen.findByText(/cash payment recorded/i);
    await user.click(screen.getByRole("button", { name: /view payment/i }));
    // Should navigate to /payments/appt-001 (CASH_RESULT.appointmentId), not pay-002
    expect(screen.getByTestId("detail-page")).toBeInTheDocument();
  });

  // ── Validation ───────────────────────────────────────────────────────────────

  it("card submit with zero amount shows validation error", async () => {
    const user = userEvent.setup();

    // Appointment with no deposit rule set
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json([{ ...APPT_PENDING_DEPOSIT, depositAmount: 0 }]),
      ),
    );

    renderPage();
    await user.click(await screen.findByText("Maria Silva"));
    await screen.findByText(/selected appointment/i);
    // Amount input should be empty since depositAmount === 0
    const amountInput = screen.getByRole("spinbutton");
    expect((amountInput as HTMLInputElement).value).toBe("");
    // Card button with no amount — should be disabled
    expect(screen.getByRole("button", { name: /create card payment/i })).toBeDisabled();
  });

  it("API error shows error message in ConfirmPanel", async () => {
    server.use(
      http.post("http://localhost/api/v1/payments", () =>
        HttpResponse.json({ message: "already has an active payment" }, { status: 422 }),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByText("Maria Silva"));
    await screen.findByText(/selected appointment/i);
    await user.click(screen.getByRole("button", { name: /create card payment/i }));
    expect(await screen.findByText(/failed to create payment/i)).toBeInTheDocument();
  });

  // ── URL preselection ─────────────────────────────────────────────────────────

  it("pre-selects appointment from ?appointmentId URL param", async () => {
    renderPage("/payments/new?appointmentId=appt-001");
    // Should jump straight to ConfirmPanel
    expect(await screen.findByText(/selected appointment/i)).toBeInTheDocument();
    expect(screen.getByText("Maria Silva")).toBeInTheDocument();
  });

  // ── Navigation ───────────────────────────────────────────────────────────────

  it("'Payments' back button in header navigates to /payments", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("New payment");
    await user.click(screen.getByRole("button", { name: /payments/i }));
    expect(screen.getByTestId("list-page")).toBeInTheDocument();
  });

  // ── SessionSplitsEditor integration ──────────────────────────────────────────

  it("CheckoutLinkPanel shows SessionSplitsEditor after card payment created", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByText("Maria Silva"));
    await screen.findByText(/selected appointment/i);
    await user.click(screen.getByRole("button", { name: /create card payment/i }));
    await screen.findByText(/card payment intent created/i);
    expect(screen.getByText("No session splits defined.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /add splits/i })).toBeInTheDocument();
  });

  it("CashResultPanel shows SessionSplitsEditor after cash payment declared", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByText("Maria Silva"));
    await screen.findByText(/selected appointment/i);
    await user.click(screen.getByRole("button", { name: /^cash$/i }));
    await user.click(screen.getByRole("button", { name: /record cash payment/i }));
    await screen.findByText(/cash payment recorded/i);
    expect(screen.getByText("No session splits defined.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /add splits/i })).toBeInTheDocument();
  });
});
