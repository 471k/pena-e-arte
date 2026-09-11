import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { giftCardsApi } from "@/features/gift-cards/giftCardsApi";
import { PaymentMethodSelector } from "@/features/payments/components/PaymentMethodSelector";
import type {
  PaymentResponse,
  PaymentIntentResponse,
  PaymentCapabilitiesResponse,
} from "@/features/payments/payment.types";

// ── POK widget mock ──────────────────────────────────────────────────────────
// GuestCheckoutForm mounts a real card form against POK's servers — mock the whole module.

vi.mock("@nebula-ltd/pok-payments-js/react", () => ({
  GuestCheckoutForm: (props: { orderId: string; onSuccess?: () => void }) => (
    <div data-testid="pok-checkout-form" data-order-id={props.orderId}>
      <button type="button" onClick={() => props.onSuccess?.()}>Authorise deposit</button>
    </div>
  ),
}));

// ── Seed data ──────────────────────────────────────────────────────────────────

const PAYMENT_ID     = "pay-sel-0001-0000-0000-000000000001";
const APPOINTMENT_ID = "appt-sel-0001";
const AMOUNT         = 75;

const INTENT_RESP: PaymentIntentResponse = {
  paymentId:    PAYMENT_ID,
  clientToken: "order-test-abcdefg",
  status:       "Pending",
};

const CASH_PAYMENT: PaymentResponse = {
  id:                    PAYMENT_ID,
  appointmentId:         APPOINTMENT_ID,
  amount:                AMOUNT,
  status:                "CashPending",
  method:                "Cash",
  providerReferenceId: null,
  clientToken:          null,
  cashNote:              null,
  paidAt:                null,
  clientName:            "",
  appointmentDate:       null,
};

const CAPABILITIES_AVAILABLE: PaymentCapabilitiesResponse = { cardPaymentsAvailable: true, pokEnvironment: "staging" };

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
  http.get(
    "http://localhost/api/v1/payments/capabilities",
    () => HttpResponse.json(CAPABILITIES_AVAILABLE),
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
      [giftCardsApi.reducerPath]: giftCardsApi.reducer,
    },
    middleware: (gd) => gd().concat(paymentsApi.middleware, giftCardsApi.middleware),
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

  it("card tab creates the deposit intent and shows the POK checkout widget", async () => {
    renderSelector();

    const form = await screen.findByTestId("pok-checkout-form");
    expect(form).toBeInTheDocument();
    expect(form.dataset.orderId).toBe("order-test-abcdefg");
    expect(within(form).getByRole("button", { name: /authorise deposit/i })).toBeInTheDocument();
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

    expect(screen.queryByTestId("pok-checkout-form")).not.toBeInTheDocument();
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

  it("widget onSuccess re-confirms the deposit with the backend before calling onSuccess", async () => {
    const user      = userEvent.setup();
    const onSuccess = vi.fn();
    let depositCalls = 0;
    server.use(
      http.post("http://localhost/api/v1/payments/deposit", () => {
        depositCalls += 1;
        return HttpResponse.json({ ...INTENT_RESP, status: depositCalls > 1 ? "Captured" : "Pending" });
      }),
    );
    renderSelector(onSuccess);

    const form = await screen.findByTestId("pok-checkout-form");
    await user.click(within(form).getByRole("button", { name: /authorise deposit/i }));

    await vi.waitFor(() => {
      expect(onSuccess).toHaveBeenCalledExactlyOnceWith("card");
    });
    // Once on tab open, once more from the widget's onSuccess re-confirm — never trusts the
    // client-side callback alone (ADR-0001).
    expect(depositCalls).toBe(2);
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

  it("shows 'temporarily unavailable' and never creates a deposit intent when capabilities say card is unavailable", async () => {
    let depositRequested = false;
    server.use(
      http.get("http://localhost/api/v1/payments/capabilities", () =>
        HttpResponse.json({ cardPaymentsAvailable: false }),
      ),
      http.post("http://localhost/api/v1/payments/deposit", () => {
        depositRequested = true;
        return HttpResponse.json(INTENT_RESP);
      }),
    );

    renderSelector();

    expect(await screen.findByText(/temporarily unavailable/i)).toBeInTheDocument();
    expect(screen.queryByTestId("pok-checkout-form")).not.toBeInTheDocument();
    expect(depositRequested).toBe(false);
  });
});
