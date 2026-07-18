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
import { PaymentListPage } from "@/features/payments/components/PaymentListPage";
import type { PaymentResponse } from "@/features/payments/payment.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const PAYMENT_CARD: PaymentResponse = {
  id:                    "pay-001",
  appointmentId:         "appt-001",
  amount:                100,
  status:                "Paid",
  method:                "Card",
  stripePaymentIntentId: "pi_test_abc",
  clientSecret:          null,
  cashNote:              null,
  paidAt:                "2026-06-10T10:00:00Z",
  clientName:            "Maria Silva",
  appointmentDate:       "2026-06-15T14:00:00Z",
};

const PAYMENT_CASH: PaymentResponse = {
  id:                    "pay-002",
  appointmentId:         "appt-002",
  amount:                50,
  status:                "CashPending",
  method:                "Cash",
  stripePaymentIntentId: null,
  clientSecret:          null,
  cashNote:              null,
  paidAt:                null,
  clientName:            "João Santos",
  appointmentDate:       "2026-06-20T11:00:00Z",
};

// 20 identical payments to trigger "Load more"
const PAGE_OF_20: PaymentResponse[] = Array.from({ length: 20 }, (_, i) => ({
  ...PAYMENT_CARD,
  id:           `pay-page-${String(i).padStart(3, "0")}`,
  appointmentId: `appt-page-${i}`,
  clientName:   `Client ${i}`,
}));

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/payments", () =>
    HttpResponse.json([]),
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
      auth: { user: { id: "u-001", email: "owner@test.com" }, token: "fake-token", tenantId: "s-001", role: "owner", pendingReferralCode: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null },
    },
  });
}

function renderPage() {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter initialEntries={["/payments"]}>
        <Routes>
          <Route path="/payments"               element={<PaymentListPage />} />
          <Route path="/payments/new"           element={<div data-testid="new-page" />} />
          <Route path="/payments/:appointmentId" element={<div data-testid="detail-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("PaymentListPage", () => {

  it("renders the 'Payments' heading", () => {
    renderPage();
    expect(screen.getByText("Payments")).toBeInTheDocument();
  });

  it("shows skeleton rows while loading", () => {
    server.use(
      http.get("http://localhost/api/v1/payments", async () => {
        await new Promise((r) => setTimeout(r, 60_000));
        return HttpResponse.json([]);
      }),
    );
    renderPage();
    // At least one skeleton div should be present in the loading state
    const skeletons = document.querySelectorAll("[class*='animate-pulse'], [data-slot='skeleton']");
    expect(skeletons.length).toBeGreaterThan(0);
  });

  it("shows an error message when the fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json({ message: "Server error" }, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText(/failed to load payments/i)).toBeInTheDocument();
  });

  it("shows rich empty state when no payments exist", async () => {
    renderPage(); // default handler returns []
    expect(await screen.findByText("No payments yet")).toBeInTheDocument();
    expect(screen.getByText(/record your first payment/i)).toBeInTheDocument();
  });

  it("renders a row for each returned payment", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json([PAYMENT_CARD, PAYMENT_CASH]),
      ),
    );
    renderPage();
    expect(await screen.findByText("Maria Silva")).toBeInTheDocument();
    expect(screen.getByText("João Santos")).toBeInTheDocument();
  });

  it("renders the formatted amount in each row", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json([PAYMENT_CARD]),
      ),
    );
    renderPage();
    // Amount 100 → "100,00 €" in pt-PT locale (or similar)
    expect(await screen.findByText(/100/)).toBeInTheDocument();
  });

  it("renders a status badge for each payment", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json([PAYMENT_CARD, PAYMENT_CASH]),
      ),
    );
    renderPage();
    await screen.findByText("Maria Silva");
    // "Paid" / "Cash Pending" may appear in both the filter pill and the badge
    expect(screen.getAllByText("Paid").length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText("Cash Pending").length).toBeGreaterThanOrEqual(1);
  });

  it("shows the payment method in each row", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json([PAYMENT_CARD, PAYMENT_CASH]),
      ),
    );
    renderPage();
    await screen.findByText("Maria Silva");
    expect(screen.getByText("Card")).toBeInTheDocument();
    expect(screen.getByText("Cash")).toBeInTheDocument();
  });

  it("clicking a row navigates to /payments/:appointmentId", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json([PAYMENT_CARD]),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByText("Maria Silva"));
    expect(screen.getByTestId("detail-page")).toBeInTheDocument();
  });

  it("'New payment' button navigates to /payments/new", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Payments");
    await user.click(screen.getByRole("button", { name: /new payment/i }));
    expect(screen.getByTestId("new-page")).toBeInTheDocument();
  });

  it("does NOT show 'Load more' when fewer than 20 results", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json([PAYMENT_CARD, PAYMENT_CASH]),
      ),
    );
    renderPage();
    await screen.findByText("Maria Silva");
    expect(screen.queryByRole("button", { name: /load more/i })).not.toBeInTheDocument();
  });

  it("shows 'Load more' when exactly 20 results are returned", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json(PAGE_OF_20),
      ),
    );
    renderPage();
    await screen.findByText("Client 0");
    expect(screen.getByRole("button", { name: /load more/i })).toBeInTheDocument();
  });

  it("shows '20 payments' counter with a full page", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json(PAGE_OF_20),
      ),
    );
    renderPage();
    await screen.findByText("Client 0");
    expect(screen.getByText("20 payments")).toBeInTheDocument();
  });

  it("clicking 'Load more' fetches the next page and accumulates results", async () => {
    const SECOND_PAGE: PaymentResponse[] = [
      { ...PAYMENT_CASH, id: "pay-page-extra", appointmentId: "appt-extra", clientName: "Extra Client" },
    ];

    server.use(
      http.get("http://localhost/api/v1/payments", ({ request }) => {
        const url = new URL(request.url);
        if (url.searchParams.get("lastSeenId")) {
          return HttpResponse.json(SECOND_PAGE);
        }
        return HttpResponse.json(PAGE_OF_20);
      }),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Client 0");

    await user.click(screen.getByRole("button", { name: /load more/i }));

    expect(await screen.findByText("Extra Client")).toBeInTheDocument();
    expect(screen.getByText("Client 0")).toBeInTheDocument(); // previous page still shown
  });

  // ── Rich empty state ──────────────────────────────────────────────────────────

  it("rich empty state shows a Record payment CTA button", async () => {
    renderPage(); // default handler returns []
    await screen.findByText("No payments yet");
    expect(screen.getByRole("button", { name: /record payment/i })).toBeInTheDocument();
  });

  it("rich empty state Record payment button navigates to /payments/new", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("No payments yet");
    await user.click(screen.getByRole("button", { name: /record payment/i }));
    expect(screen.getByTestId("new-page")).toBeInTheDocument();
  });

  // ── View action button ────────────────────────────────────────────────────────

  it("renders a View button for each loaded payment", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json([PAYMENT_CARD, PAYMENT_CASH]),
      ),
    );
    renderPage();
    await screen.findByText("Maria Silva");
    expect(screen.getAllByRole("button", { name: /^view$/i })).toHaveLength(2);
  });

  it("clicking the View button navigates to /payments/:appointmentId", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json([PAYMENT_CARD]),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Maria Silva");
    await user.click(screen.getByRole("button", { name: /^view$/i }));
    expect(screen.getByTestId("detail-page")).toBeInTheDocument();
  });

  // ── Client-name search ────────────────────────────────────────────────────────

  it("search input is present on the page", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json([PAYMENT_CARD, PAYMENT_CASH]),
      ),
    );
    renderPage();
    await screen.findByText("Maria Silva");
    expect(screen.getByPlaceholderText(/search by client name/i)).toBeInTheDocument();
  });

  it("typing a client name filters the visible payments", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json([PAYMENT_CARD, PAYMENT_CASH]),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Maria Silva");

    await user.type(screen.getByPlaceholderText(/search by client name/i), "Maria");

    expect(screen.getByText("Maria Silva")).toBeInTheDocument();
    expect(screen.queryByText("João Santos")).not.toBeInTheDocument();
  });

  it("search is case-insensitive", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json([PAYMENT_CARD, PAYMENT_CASH]),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("João Santos");

    await user.type(screen.getByPlaceholderText(/search by client name/i), "joão");

    expect(screen.getByText("João Santos")).toBeInTheDocument();
    expect(screen.queryByText("Maria Silva")).not.toBeInTheDocument();
  });

  // ── Status filter pills ───────────────────────────────────────────────────────

  it("status filter pills appear when multiple statuses are present", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json([PAYMENT_CARD, PAYMENT_CASH]),
      ),
    );
    renderPage();
    await screen.findByText("Maria Silva");

    // PAYMENT_CARD is "Paid", PAYMENT_CASH is "CashPending" (displayed as "Cash Pending")
    expect(screen.getByRole("button", { name: "Paid" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Cash Pending" })).toBeInTheDocument();
  });

  it("clicking a status filter pill shows only matching payments", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json([PAYMENT_CARD, PAYMENT_CASH]),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Maria Silva");

    await user.click(screen.getByRole("button", { name: "Paid" }));

    expect(screen.getByText("Maria Silva")).toBeInTheDocument();   // Paid
    expect(screen.queryByText("João Santos")).not.toBeInTheDocument(); // CashPending
  });

  it("clicking the active status pill again clears the filter", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json([PAYMENT_CARD, PAYMENT_CASH]),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Maria Silva");

    await user.click(screen.getByRole("button", { name: "Paid" }));
    expect(screen.queryByText("João Santos")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Paid" }));
    expect(screen.getByText("João Santos")).toBeInTheDocument();
  });
});
