import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { CashDepositConfirmButton } from "@/features/payments/components/CashDepositConfirmButton";
import type { PaymentResponse } from "@/features/payments/payment.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const CONFIRMED_PAYMENT: PaymentResponse = {
  id:                    "pay-0001-0000-0000-0000-000000000001",
  appointmentId:         "appt-0001",
  amount:                60,
  status:                "Paid",
  method:                "Cash",
  stripePaymentIntentId: null,
  clientSecret:          null,
  cashNote:              null,
  paidAt:                "2026-06-11T14:00:00.000Z",
  clientName:            "",
  appointmentDate:       null,
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.post(
    "http://localhost/api/v1/payments/:id/cash/confirm",
    () => HttpResponse.json(CONFIRMED_PAYMENT),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                      authReducer,
      [paymentsApi.reducerPath]: paymentsApi.reducer,
    },
    middleware: (gd) => gd().concat(paymentsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "owner@test.com" }, token: "fake", tenantId: "t1", role: "Owner" } as any,
    },
  });
}

function renderButton(
  paymentId = "pay-0001-0000-0000-0000-000000000001",
  clientName = "Ana Costa",
  amount = 60,
) {
  const store = makeStore();
  render(
    <Provider store={store}>
      <CashDepositConfirmButton
        paymentId={paymentId}
        clientName={clientName}
        amount={amount}
      />
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("CashDepositConfirmButton", () => {
  it("shows 'Mark cash received' button initially", () => {
    renderButton();
    expect(screen.getByRole("button", { name: /mark cash received/i })).toBeInTheDocument();
  });

  it("clicking shows confirmation prompt with client name and amount", async () => {
    const user = userEvent.setup();
    renderButton("pay-0001-0000-0000-0000-000000000001", "Ana Costa", 60);

    await user.click(screen.getByRole("button", { name: /mark cash received/i }));

    expect(screen.getByText(/Ana Costa/)).toBeInTheDocument();
    expect(screen.getByText(/60\.00/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /yes/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /cancel/i })).toBeInTheDocument();
  });

  it("confirming calls confirmCashDeposit with correct paymentId", async () => {
    const user = userEvent.setup();
    const paymentId = "pay-0001-0000-0000-0000-000000000001";
    let capturedId: string | null = null;

    server.use(
      http.post(
        "http://localhost/api/v1/payments/:id/cash/confirm",
        ({ params }) => {
          capturedId = params.id as string;
          return HttpResponse.json(CONFIRMED_PAYMENT);
        },
      ),
    );

    renderButton(paymentId);

    await user.click(screen.getByRole("button", { name: /mark cash received/i }));
    await user.click(screen.getByRole("button", { name: /yes/i }));

    expect(capturedId).toBe(paymentId);
  });

  it("cancel returns to initial state without calling API", async () => {
    const user = userEvent.setup();
    let apiCalled = false;

    server.use(
      http.post("http://localhost/api/v1/payments/:id/cash/confirm", () => {
        apiCalled = true;
        return HttpResponse.json(CONFIRMED_PAYMENT);
      }),
    );

    renderButton();

    await user.click(screen.getByRole("button", { name: /mark cash received/i }));
    expect(screen.getByRole("button", { name: /cancel/i })).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /cancel/i }));

    expect(screen.getByRole("button", { name: /mark cash received/i })).toBeInTheDocument();
    expect(apiCalled).toBe(false);
  });
});
