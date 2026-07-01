import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { DepositCheckoutPage } from "@/features/payments/components/DepositCheckoutPage";
import type { ClientSecretResponse } from "@/features/payments/payment.types";

// ── Stripe mock ────────────────────────────────────────────────────────────────
// Stripe Elements require a real browser environment; mock the whole module.

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

const SECRET_RESP: ClientSecretResponse = {
  clientSecret: "pi_test_secret_xyz",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/payments/:id/client-secret", ({ params }) => {
    if (params.id === "pay-001") return HttpResponse.json(SECRET_RESP);
    return HttpResponse.json({ message: "Not found" }, { status: 404 });
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                      authReducer,
      ui:                        uiReducer,
      [paymentsApi.reducerPath]: paymentsApi.reducer,
    },
    middleware: (gd) => gd().concat(paymentsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u-002", email: "client@test.com" }, token: "fake-token", tenantId: "s-001", role: "client", pendingReferralCode: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false },
    },
  });
}

function renderPage(paymentId: string, search = "") {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter initialEntries={[`/pay/${paymentId}${search}`]}>
        <Routes>
          <Route path="/pay/:paymentId" element={<DepositCheckoutPage />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("DepositCheckoutPage", () => {

  // ── Loading / error states ───────────────────────────────────────────────────

  it("shows 'Deposit payment' heading in the header", async () => {
    renderPage("pay-001");
    await screen.findByText(/loading payment details/i);
    expect(screen.getByText("Deposit payment")).toBeInTheDocument();
  });

  it("shows loading state while fetching the client secret", () => {
    server.use(
      http.get("http://localhost/api/v1/payments/:id/client-secret", async () => {
        await new Promise((r) => setTimeout(r, 60_000));
        return HttpResponse.json(SECRET_RESP);
      }),
    );
    renderPage("pay-001");
    expect(screen.getByText(/loading payment details/i)).toBeInTheDocument();
  });

  it("shows error when payment is not found / access denied", async () => {
    renderPage("pay-999");
    expect(await screen.findByText(/payment not found/i)).toBeInTheDocument();
  });

  // ── Card form ────────────────────────────────────────────────────────────────

  it("shows Stripe PaymentElement when client secret is available", async () => {
    renderPage("pay-001");
    expect(await screen.findByTestId("stripe-payment-element")).toBeInTheDocument();
  });

  it("shows 'Authorise deposit' submit button", async () => {
    renderPage("pay-001");
    await screen.findByTestId("stripe-payment-element");
    expect(screen.getByRole("button", { name: /authorise deposit/i })).toBeInTheDocument();
  });

  it("shows the amount in the form description when ?amount param is provided", async () => {
    renderPage("pay-001", "?amount=100.00+EUR");
    await screen.findByTestId("stripe-payment-element");
    expect(screen.getByText(/100\.00 EUR/)).toBeInTheDocument();
  });

  it("does NOT show the amount text when no ?amount param", async () => {
    renderPage("pay-001");
    await screen.findByTestId("stripe-payment-element");
    // The <p> with "authorising a deposit of" should not appear
    expect(screen.queryByText(/authorising a deposit of/i)).not.toBeInTheDocument();
  });

  it("shows the Stripe security footer", async () => {
    renderPage("pay-001");
    await screen.findByTestId("stripe-payment-element");
    expect(screen.getByText(/secured by stripe/i)).toBeInTheDocument();
  });

  // ── Redirect complete state ──────────────────────────────────────────────────

  it("shows success state when ?status=complete without fetching the client secret", async () => {
    let secretFetched = false;
    server.use(
      http.get("http://localhost/api/v1/payments/:id/client-secret", () => {
        secretFetched = true;
        return HttpResponse.json(SECRET_RESP);
      }),
    );

    renderPage("pay-001", "?status=complete");

    expect(await screen.findByText(/deposit authorised/i)).toBeInTheDocument();
    expect(screen.getByText(/studio will capture/i)).toBeInTheDocument();
    expect(secretFetched).toBe(false);
  });

  it("success state does NOT show Stripe form elements", async () => {
    renderPage("pay-001", "?status=complete");
    await screen.findByText(/deposit authorised/i);
    expect(screen.queryByTestId("stripe-payment-element")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /authorise/i })).not.toBeInTheDocument();
  });

  // ── Probe: ?amount with status=complete ──────────────────────────────────────

  it("success state works even when ?amount is also present", async () => {
    renderPage("pay-001", "?status=complete&amount=75.00+EUR");
    expect(await screen.findByText(/deposit authorised/i)).toBeInTheDocument();
  });
});
