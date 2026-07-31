import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { PaymentMethodSelector } from "@/features/payments/components/PaymentMethodSelector";
import type { PaymentResponse, PaymentIntentResponse } from "@/features/payments/payment.types";

// ── Stripe mock ────────────────────────────────────────────────────────────────
// Stripe Elements require a real browser context; mock the entire module.

vi.mock("@stripe/react-stripe-js", () => ({
  loadStripe:     vi.fn().mockResolvedValue({}),
  Elements:       ({ children }: { children: React.ReactNode }) => <>{children}</>,
  PaymentElement: () => <div data-testid="stripe-payment-element" />,
  useStripe:      () => ({
    confirmPayment: vi.fn().mockResolvedValue({ error: null }),
  }),
  useElements: () => ({}),
}));

vi.mock("@stripe/stripe-js", () => ({
  loadStripe: vi.fn().mockResolvedValue({}),
}));

// ── Seed data ──────────────────────────────────────────────────────────────────

const PAYMENT_ID     = "pay-sel-0001-0000-0000-000000000001";
const APPOINTMENT_ID = "appt-sel-0001";
const AMOUNT         = 75;

const INTENT_RESP: PaymentIntentResponse = {
  paymentId:    PAYMENT_ID,
  clientSecret: "pi_test_secret_abcdefg",
  status:       "Pending",
};

const CASH_PAYMENT: PaymentResponse = {
  id:                    PAYMENT_ID,
  appointmentId:         APPOINTMENT_ID,
  amount:                AMOUNT,
  status:                "CashPending",
  method:                "Cash",
  providerReferenceId: null,
  clientSecret:          null,
  cashNote:              null,
  paidAt:                null,
  clientName:            "",
  appointmentDate:       null,
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.post(
    "http://localhost/api/v1/payments/deposit",
    () => HttpResponse.json(INTENT_RESP),
  ),
  http.post(
    "http://localhost/api/v1/payments/cash",
    () => HttpResponse.json(CASH_PAYMENT),
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
      auth: { user: { id: "u1", email: "client@test.com" }, token: "fake", tenantId: "t1", role: "Client" } as any,
    },
  });
}

function renderSelector(
  onSuccess = vi.fn(),
  onError   = vi.fn(),
) {
  const store = makeStore();
  render(
    <Provider store={store}>
      <PaymentMethodSelector
        appointmentId={APPOINTMENT_ID}
        amount={AMOUNT}
        onSuccess={onSuccess}
        onError={onError}
      />
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("PaymentMethodSelector", () => {
  it("renders card tab by default", () => {
    renderSelector();
    expect(screen.getByRole("button", { name: /card/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /cash/i })).toBeInTheDocument();
  });

  it("card tab creates the deposit intent and shows Stripe PaymentElement", async () => {
    renderSelector();

    const paymentElement = await screen.findByTestId("stripe-payment-element");
    expect(paymentElement).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /authorise deposit/i })).toBeInTheDocument();
  });

  it("card tab shows loading state before the intent is ready", () => {
    server.use(
      http.post(
        "http://localhost/api/v1/payments/deposit",
        async () => {
          await new Promise((r) => setTimeout(r, 50_000));
          return HttpResponse.json(INTENT_RESP);
        },
      ),
    );

    renderSelector();

    expect(screen.queryByTestId("stripe-payment-element")).not.toBeInTheDocument();
    expect(screen.getByText(/preparing payment form/i)).toBeInTheDocument();
  });

  it("card tab shows server message when intent creation fails", async () => {
    server.use(
      http.post("http://localhost/api/v1/payments/deposit", () =>
        HttpResponse.json(
          { status: 422, message: "This appointment does not require a deposit." },
          { status: 422 },
        ),
      ),
    );

    renderSelector();

    expect(
      await screen.findByText(/does not require a deposit/i),
    ).toBeInTheDocument();
  });

  it("switches to cash tab on click", async () => {
    const user = userEvent.setup();
    renderSelector();

    await user.click(screen.getByRole("button", { name: /cash/i }));

    expect(screen.getByText(/pay at the studio/i)).toBeInTheDocument();
  });

  it("cash tab shows 'pay at studio' info panel with amount", async () => {
    const user = userEvent.setup();
    renderSelector();

    await user.click(screen.getByRole("button", { name: /cash/i }));

    expect(screen.getByText(/75\.00/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /confirm.*cash/i })).toBeInTheDocument();
  });

  it("cash tab confirm button calls declareCashDeposit and triggers onSuccess with cash", async () => {
    const user      = userEvent.setup();
    const onSuccess = vi.fn();
    renderSelector(onSuccess);

    await user.click(screen.getByRole("button", { name: /cash/i }));
    await user.click(screen.getByRole("button", { name: /confirm.*cash/i }));

    await vi.waitFor(() => {
      expect(onSuccess).toHaveBeenCalledExactlyOnceWith("cash");
    });
  });

  it("cash tab calls onError when declareCashDeposit mutation fails", async () => {
    const user    = userEvent.setup();
    const onError = vi.fn();

    server.use(
      http.post("http://localhost/api/v1/payments/cash", () =>
        new HttpResponse(null, { status: 422 }),
      ),
    );

    renderSelector(vi.fn(), onError);

    await user.click(screen.getByRole("button", { name: /cash/i }));
    await user.click(screen.getByRole("button", { name: /confirm.*cash/i }));

    await vi.waitFor(() => {
      expect(onError).toHaveBeenCalledOnce();
    });
  });
});
