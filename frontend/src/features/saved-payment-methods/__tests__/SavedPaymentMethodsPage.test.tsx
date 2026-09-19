import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { Toaster } from "sonner";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { savedPaymentMethodsApi } from "@/features/saved-payment-methods/savedPaymentMethodsApi";
import { SavedPaymentMethodsPage } from "@/features/saved-payment-methods/components/SavedPaymentMethodsPage";
import type { PaymentCapabilitiesResponse } from "@/features/payments/payment.types";
import type { SavedPaymentMethodResponse } from "@/features/saved-payment-methods/savedPaymentMethod.types";

// ── POK widget mock ──────────────────────────────────────────────────────────

vi.mock("@nebula-ltd/pok-payments-js/react", () => ({
  AddCardForm: (props: {
    buttonTitle?: string;
    onSuccess?: (data: unknown) => void;
    onError?: (e: { message?: string }) => void;
  }) => (
    <div data-testid="add-card-form">
      <button
        type="button"
        onClick={() =>
          props.onSuccess?.({
            csFlexCard: { jwe: "jwe-value" },
            securityCode: "123",
            billingInfo: {
              firstName: "Jamie", lastName: "Client", email: "jamie@test.com",
              countryCode: "AL", administrativeArea: "Tirana", locality: "Tirana",
              address1: "Rr. Myslym Shyri 10", postalCode: "1001", phoneNumber: "+355691234567",
            },
          })
        }
      >
        {props.buttonTitle ?? "Save card"}
      </button>
      <button type="button" onClick={() => props.onError?.({ message: "Card rejected." })}>
        Trigger error
      </button>
    </div>
  ),
}));

// ── Seed data ──────────────────────────────────────────────────────────────────

const METHOD_1: SavedPaymentMethodResponse = {
  id: "spm-001", cardBrand: "Visa", maskedPan: "**** 4242",
  expiryMonth: "12", expiryYear: "2030", isDefault: true, createdAt: "2024-01-01T00:00:00Z",
};

const METHOD_2: SavedPaymentMethodResponse = {
  id: "spm-002", cardBrand: "Mastercard", maskedPan: "**** 1111",
  expiryMonth: "6", expiryYear: "2028", isDefault: false, createdAt: "2024-02-01T00:00:00Z",
};

const CAPABILITIES_AVAILABLE: PaymentCapabilitiesResponse = { cardPaymentsAvailable: true, pokEnvironment: "staging" };

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/saved-payment-methods", () => HttpResponse.json([METHOD_1, METHOD_2])),
  http.get("http://localhost/api/v1/payments/capabilities", () => HttpResponse.json(CAPABILITIES_AVAILABLE)),
  http.post("http://localhost/api/v1/saved-payment-methods", () => HttpResponse.json(METHOD_1, { status: 201 })),
  http.delete("http://localhost/api/v1/saved-payment-methods/:id", () => new HttpResponse(null, { status: 204 })),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore(tenantId: string | null = "t1") {
  return configureStore({
    reducer: {
      auth: authReducer,
      [savedPaymentMethodsApi.reducerPath]: savedPaymentMethodsApi.reducer,
      [paymentsApi.reducerPath]: paymentsApi.reducer,
    },
    middleware: (gd) => gd().concat(savedPaymentMethodsApi.middleware, paymentsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "client@test.com" }, token: "fake", tenantId, role: "Client" } as any,
    },
  });
}

function renderPage(tenantId: string | null = "t1") {
  render(
    <Provider store={makeStore(tenantId)}>
      <MemoryRouter>
        <Toaster />
        <SavedPaymentMethodsPage />
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("SavedPaymentMethodsPage", () => {
  it("renders the Payment Methods header", () => {
    renderPage();
    expect(screen.getByText("Payment Methods")).toBeInTheDocument();
  });

  it("lists each saved card with brand, masked PAN, and expiry", async () => {
    renderPage();
    expect(await screen.findByText(/visa.*4242/i)).toBeInTheDocument();
    expect(screen.getByText(/mastercard.*1111/i)).toBeInTheDocument();
    expect(screen.getByText(/expires 12\/2030/i)).toBeInTheDocument();
  });

  it("shows a Default badge only on the default card", async () => {
    renderPage();
    await screen.findByText(/visa.*4242/i);
    expect(screen.getAllByText("Default")).toHaveLength(1);
  });

  it("shows empty state when there are no saved cards", async () => {
    server.use(http.get("http://localhost/api/v1/saved-payment-methods", () => HttpResponse.json([])));
    renderPage();
    expect(await screen.findByText(/no saved cards yet/i)).toBeInTheDocument();
  });

  it("mounts the AddCardForm widget when card payments are available", async () => {
    renderPage();
    expect(await screen.findByTestId("add-card-form")).toBeInTheDocument();
  });

  it("shows 'temporarily unavailable' instead of the form when capabilities say cards are unavailable", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments/capabilities", () =>
        HttpResponse.json({ cardPaymentsAvailable: false, pokEnvironment: null }),
      ),
    );
    renderPage();
    expect(await screen.findByText(/temporarily unavailable/i)).toBeInTheDocument();
    expect(screen.queryByTestId("add-card-form")).not.toBeInTheDocument();
  });

  it("saving a card calls addSavedPaymentMethod with the widget's payload and shows a success toast", async () => {
    const user = userEvent.setup();
    let capturedBody: Record<string, unknown> | undefined;
    server.use(
      http.post("http://localhost/api/v1/saved-payment-methods", async ({ request }) => {
        capturedBody = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(METHOD_1, { status: 201 });
      }),
    );

    renderPage();
    await user.click(await screen.findByRole("button", { name: /save card/i }));

    expect(await screen.findByText("Card saved.")).toBeInTheDocument();
    expect(capturedBody?.jwe).toBe("jwe-value");
    expect(capturedBody?.securityCode).toBe("123");
    expect(capturedBody?.email).toBe("jamie@test.com");
    expect(capturedBody?.address1).toBe("Rr. Myslym Shyri 10");
  });

  it("shows an error toast when saving a card fails", async () => {
    const user = userEvent.setup();
    server.use(
      http.post("http://localhost/api/v1/saved-payment-methods", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );

    renderPage();
    await user.click(await screen.findByRole("button", { name: /save card/i }));

    expect(await screen.findByText("Failed to save card. Please try again.")).toBeInTheDocument();
  });

  it("shows the widget's own error message when AddCardForm reports an error", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /trigger error/i }));

    expect(await screen.findByText("Card rejected.")).toBeInTheDocument();
  });

  it("removing a card calls deleteSavedPaymentMethod and shows a success toast", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText(/visa.*4242/i);

    const removeButtons = screen.getAllByRole("button", { name: /remove/i });
    await user.click(removeButtons[0]);

    expect(await screen.findByText("Card removed.")).toBeInTheDocument();
  });

  // A card is saved against a specific studio's POK merchant — a client who hasn't joined
  // any studio yet has nowhere to route one to. Must show an actionable empty state, not
  // the misleading "temporarily unavailable" capabilities message (which implies an outage).
  it("shows a 'join a studio' empty state instead of querying capabilities when the client has no studio", async () => {
    let capabilitiesCalled = false;
    server.use(
      http.get("http://localhost/api/v1/payments/capabilities", () => {
        capabilitiesCalled = true;
        return HttpResponse.json(CAPABILITIES_AVAILABLE);
      }),
    );

    renderPage(null);

    expect(await screen.findByText(/haven't joined a studio yet/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /browse studios/i })).toHaveAttribute("href", "/discover");
    expect(screen.queryByText(/temporarily unavailable/i)).not.toBeInTheDocument();
    expect(capabilitiesCalled).toBe(false);
  });

  it("shows a generic error with a retry option when the saved-methods fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/saved-payment-methods", () =>
        new HttpResponse(null, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText("Failed to load saved payment methods.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /try again/i })).toBeInTheDocument();
  });

  it("shows an error toast when removing a card fails", async () => {
    const user = userEvent.setup();
    server.use(
      http.delete("http://localhost/api/v1/saved-payment-methods/:id", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );

    renderPage();
    await screen.findByText(/visa.*4242/i);
    const removeButtons = screen.getAllByRole("button", { name: /remove/i });
    await user.click(removeButtons[0]);

    expect(await screen.findByText("Failed to remove card. Please try again.")).toBeInTheDocument();
  });
});
