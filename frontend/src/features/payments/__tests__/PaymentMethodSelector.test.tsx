import { describe, it, expect, vi, beforeAll, beforeEach, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { giftCardsApi } from "@/features/gift-cards/giftCardsApi";
import { savedPaymentMethodsApi } from "@/features/saved-payment-methods/savedPaymentMethodsApi";
import { PaymentMethodSelector } from "@/features/payments/components/PaymentMethodSelector";
import type {
  PaymentResponse,
  PaymentIntentResponse,
  PaymentCapabilitiesResponse,
} from "@/features/payments/payment.types";

// ── POK widget mock ──────────────────────────────────────────────────────────
// GuestCheckoutForm mounts a real card form against POK's servers — mock the whole module.
// usePOK is mocked like the real SDK 2.0.1 hook: it records the order id it was called with and
// returns `order` (null until "loaded"), and captures onSuccess/onError/payByCardToken so tests can
// drive the saved-card 3DS flow by hand (simulating what POK's real step-up modal would do).

const LOADED_ORDER = { finalAmount: 75, currencyCode: "EUR" };

const pokHookState: {
  onSuccess?: () => void;
  onError?: (e: { message?: string }) => void;
  payByCardToken: ReturnType<typeof vi.fn>;
  order: { finalAmount?: number; currencyCode?: string } | null;
  orderIds: string[];
} = { payByCardToken: vi.fn(), order: LOADED_ORDER, orderIds: [] };

vi.mock("@nebula-ltd/pok-payments-js/react", () => ({
  GuestCheckoutForm: (props: { orderId: string; onSuccess?: () => void; onError?: (e: { message?: string }) => void }) => (
    <div data-testid="pok-checkout-form" data-order-id={props.orderId}>
      <button type="button" onClick={() => props.onSuccess?.()}>Authorise deposit</button>
      <button type="button" onClick={() => props.onError?.({ message: "3DS processing failed. Please try again" })}>
        Fail payment
      </button>
    </div>
  ),
  usePOK: (
    orderId: string,
    onSuccess?: () => void,
    onError?: (e: { message?: string }) => void,
  ) => {
    pokHookState.orderIds.push(orderId);
    pokHookState.onSuccess = onSuccess;
    pokHookState.onError = onError;
    return { order: pokHookState.order, payByCardToken: pokHookState.payByCardToken, fetching: false, loading: false };
  },
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
  currency:              "EUR",
};

const CAPABILITIES_AVAILABLE: PaymentCapabilitiesResponse = {
  cardPaymentsAvailable: true, pokEnvironment: "staging", currency: "EUR", cardUnavailableReason: null,
  savedCardsAvailable: false,
};

const SAVED_METHOD = {
  id: "spm-001", cardBrand: "Visa", maskedPan: "**** 4242",
  expiryMonth: "12", expiryYear: "2030", isDefault: true, createdAt: "2024-01-01T00:00:00Z",
};

const SETUP_PENDING = {
  paymentId: PAYMENT_ID, status: "Pending", orderId: "order-saved-abc",
  cardTokenId: "card-abc", payerAuthSetupReferenceId: "ref-1", deviceDataCollection: null,
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
  http.get(
    "http://localhost/api/v1/payments/capabilities",
    () => HttpResponse.json(CAPABILITIES_AVAILABLE),
  ),
  http.get(
    "http://localhost/api/v1/saved-payment-methods",
    () => HttpResponse.json([]),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  server.resetHandlers();
  cleanup();
  pokHookState.payByCardToken.mockClear();
  pokHookState.onSuccess = undefined;
  pokHookState.onError = undefined;
  pokHookState.order = LOADED_ORDER;
  pokHookState.orderIds = [];
});
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                      authReducer,
      [paymentsApi.reducerPath]: paymentsApi.reducer,
      [giftCardsApi.reducerPath]: giftCardsApi.reducer,
      [savedPaymentMethodsApi.reducerPath]: savedPaymentMethodsApi.reducer,
    },
    middleware: (gd) => gd().concat(paymentsApi.middleware, giftCardsApi.middleware, savedPaymentMethodsApi.middleware),
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
  const tree = () => (
    <Provider store={store}>
      <PaymentMethodSelector
        appointmentId={APPOINTMENT_ID}
        amount={AMOUNT}
        onSuccess={onSuccess}
        onError={onError}
      />
    </Provider>
  );
  const utils = render(tree());
  return { rerender: () => utils.rerender(tree()) };
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
    expect(screen.getByText(/your card is charged now/i)).toBeInTheDocument();
  });

  // C-11 production test (28 Oct 2026): POK's card form doesn't show what it will charge.
  it("card tab shows the amount the server will charge, above the card form", async () => {
    server.use(
      http.post("http://localhost/api/v1/payments/deposit", () =>
        HttpResponse.json({ ...INTENT_RESP, amount: 100, currency: "ALL" })),
    );
    renderSelector();

    await screen.findByTestId("pok-checkout-form");
    const row = screen.getByText("Amount to pay").parentElement!;
    expect(row).toHaveTextContent(/100/);
    expect(row).toHaveTextContent(/ALL|L/);
  });

  it("card tab shows no amount line when the server sends none", async () => {
    renderSelector();
    await screen.findByTestId("pok-checkout-form");
    expect(screen.queryByText("Amount to pay")).not.toBeInTheDocument();
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

  it("widget onError replaces the form with a plain message and hides the 'charged now' note", async () => {
    const user    = userEvent.setup();
    const onError = vi.fn();
    renderSelector(vi.fn(), onError);

    const form = await screen.findByTestId("pok-checkout-form");
    await user.click(within(form).getByRole("button", { name: /fail payment/i }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Your bank couldn't verify this card. Try again, or use a different card.");
    expect(alert).toHaveTextContent(/nothing was charged/i);
    expect(onError).toHaveBeenCalledExactlyOnceWith(
      "Your bank couldn't verify this card. Try again, or use a different card.",
    );
    expect(screen.queryByTestId("pok-checkout-form")).not.toBeInTheDocument();
    expect(screen.queryByText(/charged now/i)).not.toBeInTheDocument();
  });

  it("Try again re-checks the deposit, then shows a fresh card form when it's still unpaid", async () => {
    const user = userEvent.setup();
    let depositCalls = 0;
    server.use(
      http.post("http://localhost/api/v1/payments/deposit", () => {
        depositCalls += 1;
        return HttpResponse.json(INTENT_RESP);
      }),
    );
    renderSelector();

    const form = await screen.findByTestId("pok-checkout-form");
    await user.click(within(form).getByRole("button", { name: /fail payment/i }));
    await user.click(await screen.findByRole("button", { name: /try again/i }));

    expect(await screen.findByTestId("pok-checkout-form")).toBeInTheDocument();
    expect(screen.getByText(/charged now/i)).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(depositCalls).toBe(2);
  });

  it("Try again reports success instead of a second payment when the deposit turns out authorised", async () => {
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
    await user.click(within(form).getByRole("button", { name: /fail payment/i }));
    await user.click(await screen.findByRole("button", { name: /try again/i }));

    await vi.waitFor(() => expect(onSuccess).toHaveBeenCalledExactlyOnceWith("card"));
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

    expect(screen.getByText("€75")).toBeInTheDocument();
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

  it("hides Card entirely and shows cash-only copy when the provider doesn't support the studio's currency", async () => {
    let depositRequested = false;
    server.use(
      http.get("http://localhost/api/v1/payments/capabilities", () =>
        HttpResponse.json({
          cardPaymentsAvailable: false, pokEnvironment: null,
          currency: "ALL", cardUnavailableReason: "provider_unsupported_currency",
        }),
      ),
      http.post("http://localhost/api/v1/payments/deposit", () => {
        depositRequested = true;
        return HttpResponse.json(INTENT_RESP);
      }),
    );

    renderSelector();

    expect(
      await screen.findByText(/card payments aren't available in ALL for this studio yet/i),
    ).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^card$/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /saved card/i })).not.toBeInTheDocument();
    expect(screen.getByText(/pay at the studio/i)).toBeInTheDocument();
    expect(depositRequested).toBe(false);
  });

  it("with saved cards switched off, shows no Saved card tab and never fetches saved cards, even if some exist", async () => {
    // C-9: Payments:SavedCardsEnabled off (the default) — CAPABILITIES_AVAILABLE has savedCardsAvailable: false.
    let savedMethodsRequested = false;
    server.use(
      http.get("http://localhost/api/v1/saved-payment-methods", () => {
        savedMethodsRequested = true;
        return HttpResponse.json([SAVED_METHOD]);
      }),
    );

    renderSelector();

    expect(await screen.findByTestId("pok-checkout-form")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^card$/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /saved card/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /new card/i })).not.toBeInTheDocument();
    expect(savedMethodsRequested).toBe(false);
  });

  describe("saved card tab", () => {
    beforeEach(() => {
      server.use(
        http.get("http://localhost/api/v1/payments/capabilities", () =>
          HttpResponse.json({ ...CAPABILITIES_AVAILABLE, savedCardsAvailable: true }),
        ),
        http.get("http://localhost/api/v1/saved-payment-methods", () => HttpResponse.json([SAVED_METHOD])),
      );
    });

    it("defaults to the saved-card tab when the client has a saved card", async () => {
      renderSelector();

      expect(await screen.findByText(/visa.*4242/i)).toBeInTheDocument();
      expect(screen.getByRole("button", { name: /saved card/i })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: /new card/i })).toBeInTheDocument();
    });

    it("selecting Pay sets up the 3DS challenge, and completing it re-confirms before calling onSuccess", async () => {
      const user = userEvent.setup();
      const onSuccess = vi.fn();
      let depositCalls = 0;
      server.use(
        http.post("http://localhost/api/v1/payments/deposit/pay-with-saved-card", () =>
          HttpResponse.json(SETUP_PENDING),
        ),
        http.post("http://localhost/api/v1/payments/deposit", () => {
          depositCalls += 1;
          return HttpResponse.json({ ...INTENT_RESP, status: "Captured" });
        }),
      );

      renderSelector(onSuccess);
      await screen.findByText(/visa.*4242/i);
      await user.click(screen.getByRole("button", { name: /pay with this card/i }));

      await vi.waitFor(() => {
        expect(pokHookState.payByCardToken).toHaveBeenCalledWith(
          expect.objectContaining({
            creditDebitCard: { id: "card-abc" },
            payerAuthSetupReferenceId: "ref-1",
          }),
        );
      });

      // Simulate POK's step-up modal completing successfully.
      pokHookState.onSuccess?.();

      await vi.waitFor(() => {
        expect(onSuccess).toHaveBeenCalledExactlyOnceWith("card");
      });
      expect(depositCalls).toBe(1);
    });

    it("mounts usePOK only with the real order id, never an empty one", async () => {
      const user = userEvent.setup();
      server.use(
        http.post("http://localhost/api/v1/payments/deposit/pay-with-saved-card", () =>
          HttpResponse.json(SETUP_PENDING),
        ),
      );

      renderSelector();
      await screen.findByText(/visa.*4242/i);
      expect(pokHookState.orderIds).toEqual([]);

      await user.click(screen.getByRole("button", { name: /pay with this card/i }));

      await vi.waitFor(() => expect(pokHookState.payByCardToken).toHaveBeenCalledOnce());
      expect(new Set(pokHookState.orderIds)).toEqual(new Set(["order-saved-abc"]));
    });

    it("waits for POK to load the order before starting the 3DS check", async () => {
      const user = userEvent.setup();
      pokHookState.order = null;
      server.use(
        http.post("http://localhost/api/v1/payments/deposit/pay-with-saved-card", () =>
          HttpResponse.json(SETUP_PENDING),
        ),
      );

      const { rerender } = renderSelector();
      await screen.findByText(/visa.*4242/i);
      await user.click(screen.getByRole("button", { name: /pay with this card/i }));

      expect(await screen.findByText(/verifying your card/i)).toBeInTheDocument();
      expect(pokHookState.payByCardToken).not.toHaveBeenCalled();

      pokHookState.order = LOADED_ORDER;
      rerender();

      await vi.waitFor(() => expect(pokHookState.payByCardToken).toHaveBeenCalledOnce());
    });

    it("already-settled response skips the 3DS setup and reports success directly", async () => {
      const user = userEvent.setup();
      const onSuccess = vi.fn();
      server.use(
        http.post("http://localhost/api/v1/payments/deposit/pay-with-saved-card", () =>
          HttpResponse.json({ ...SETUP_PENDING, status: "Paid", orderId: null, cardTokenId: null, payerAuthSetupReferenceId: null }),
        ),
      );

      renderSelector(onSuccess);
      await screen.findByText(/visa.*4242/i);
      await user.click(screen.getByRole("button", { name: /pay with this card/i }));

      await vi.waitFor(() => {
        expect(onSuccess).toHaveBeenCalledExactlyOnceWith("card");
      });
      expect(pokHookState.payByCardToken).not.toHaveBeenCalled();
    });

    it("POK challenge failure calls onError", async () => {
      const user = userEvent.setup();
      const onError = vi.fn();
      server.use(
        http.post("http://localhost/api/v1/payments/deposit/pay-with-saved-card", () =>
          HttpResponse.json(SETUP_PENDING),
        ),
      );

      renderSelector(vi.fn(), onError);
      await screen.findByText(/visa.*4242/i);
      await user.click(screen.getByRole("button", { name: /pay with this card/i }));

      await vi.waitFor(() => expect(pokHookState.onError).toBeDefined());
      pokHookState.onError?.({ message: "Card declined." });

      await vi.waitFor(() => {
        expect(onError).toHaveBeenCalledWith("The card payment didn't go through. Try again, or use a different card.");
      });
    });

    it("switching to New card tab shows the GuestCheckoutForm widget instead", async () => {
      const user = userEvent.setup();
      renderSelector();

      await screen.findByText(/visa.*4242/i);
      await user.click(screen.getByRole("button", { name: /new card/i }));

      expect(await screen.findByTestId("pok-checkout-form")).toBeInTheDocument();
    });
  });
});
