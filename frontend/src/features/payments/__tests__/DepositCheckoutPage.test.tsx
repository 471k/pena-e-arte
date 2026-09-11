import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { DepositCheckoutPage } from "@/features/payments/components/DepositCheckoutPage";
import type { ClientTokenResponse, PaymentCapabilitiesResponse } from "@/features/payments/payment.types";

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

const SECRET_RESP: ClientTokenResponse = {
  clientToken: "order-abc-xyz",
};

const CAPABILITIES_AVAILABLE: PaymentCapabilitiesResponse = { cardPaymentsAvailable: true, pokEnvironment: "staging" };

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/payments/:id/client-token", ({ params }) => {
    if (params.id === "pay-001") return HttpResponse.json(SECRET_RESP);
    return HttpResponse.json({ message: "Not found" }, { status: 404 });
  }),
  http.get("http://localhost/api/v1/payments/capabilities", () =>
    HttpResponse.json(CAPABILITIES_AVAILABLE),
  ),
  http.post("http://localhost/api/v1/payments/:id/confirm", () =>
    HttpResponse.json({ status: "Captured" }),
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
      ui:                        uiReducer,
      [paymentsApi.reducerPath]: paymentsApi.reducer,
    },
    middleware: (gd) => gd().concat(paymentsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u-002", email: "client@test.com" }, token: "fake-token", tenantId: "s-001", role: "client", pendingReferralCode: null, impersonation: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null, impersonationScopeError: null, impersonationSessionExpired: false },
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
      http.get("http://localhost/api/v1/payments/:id/client-token", async () => {
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

  it("shows the POK checkout widget with the order id from the client-token response", async () => {
    renderPage("pay-001");
    const form = await screen.findByTestId("pok-checkout-form");
    expect(form).toBeInTheDocument();
    expect(form.dataset.orderId).toBe("order-abc-xyz");
  });

  it("shows the amount in the form description when ?amount param is provided", async () => {
    renderPage("pay-001", "?amount=100.00+EUR");
    await screen.findByTestId("pok-checkout-form");
    expect(screen.getByText(/100\.00 EUR/)).toBeInTheDocument();
  });

  it("does NOT show the amount text when no ?amount param", async () => {
    renderPage("pay-001");
    await screen.findByTestId("pok-checkout-form");
    // The <p> with "authorising a deposit of" should not appear
    expect(screen.queryByText(/authorising a deposit of/i)).not.toBeInTheDocument();
  });

  it("shows the POK security footer", async () => {
    renderPage("pay-001");
    await screen.findByTestId("pok-checkout-form");
    expect(screen.getByText(/secured by pok/i)).toBeInTheDocument();
  });

  it("re-confirms with the backend before showing the success state — never trusts the widget alone", async () => {
    const { default: userEvent } = await import("@testing-library/user-event");
    let confirmCalled = false;
    server.use(
      http.post("http://localhost/api/v1/payments/:id/confirm", ({ params }) => {
        confirmCalled = true;
        expect(params.id).toBe("pay-001");
        return HttpResponse.json({ status: "Captured" });
      }),
    );
    renderPage("pay-001");
    const form = await screen.findByTestId("pok-checkout-form");
    await userEvent.click(within(form).getByRole("button", { name: /authorise deposit/i }));

    expect(await screen.findByText(/deposit authorised/i)).toBeInTheDocument();
    expect(confirmCalled).toBe(true);
  });

  it("shows an error instead of a false success when the backend confirm call fails", async () => {
    const { default: userEvent } = await import("@testing-library/user-event");
    server.use(
      http.post("http://localhost/api/v1/payments/:id/confirm", () =>
        new HttpResponse(null, { status: 500 }),
      ),
    );
    renderPage("pay-001");
    const form = await screen.findByTestId("pok-checkout-form");
    await userEvent.click(within(form).getByRole("button", { name: /authorise deposit/i }));

    expect(await screen.findByText(/couldn't confirm it yet/i)).toBeInTheDocument();
    expect(screen.queryByText(/deposit authorised/i)).not.toBeInTheDocument();
  });

  // ── Redirect complete state ──────────────────────────────────────────────────

  it("shows success state when ?status=complete without fetching the client secret", async () => {
    let secretFetched = false;
    server.use(
      http.get("http://localhost/api/v1/payments/:id/client-token", () => {
        secretFetched = true;
        return HttpResponse.json(SECRET_RESP);
      }),
    );

    renderPage("pay-001", "?status=complete");

    expect(await screen.findByText(/deposit authorised/i)).toBeInTheDocument();
    expect(screen.getByText(/studio will capture/i)).toBeInTheDocument();
    expect(secretFetched).toBe(false);
  });

  it("success state does NOT show the POK form", async () => {
    renderPage("pay-001", "?status=complete");
    await screen.findByText(/deposit authorised/i);
    expect(screen.queryByTestId("pok-checkout-form")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /authorise/i })).not.toBeInTheDocument();
  });

  // ── Probe: ?amount with status=complete ──────────────────────────────────────

  it("success state works even when ?amount is also present", async () => {
    renderPage("pay-001", "?status=complete&amount=75.00+EUR");
    expect(await screen.findByText(/deposit authorised/i)).toBeInTheDocument();
  });

  // ── Capabilities guard ───────────────────────────────────────────────────────

  it("shows an unavailable message instead of the POK form when card payments are unavailable", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments/capabilities", () =>
        HttpResponse.json({ cardPaymentsAvailable: false }),
      ),
    );
    renderPage("pay-001");
    expect(await screen.findByText(/temporarily unavailable for this link/i)).toBeInTheDocument();
    expect(screen.queryByTestId("pok-checkout-form")).not.toBeInTheDocument();
  });
});
