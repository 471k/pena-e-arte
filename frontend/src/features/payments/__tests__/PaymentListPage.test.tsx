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
import { PaymentListPage } from "@/features/payments/components/PaymentListPage";
import type { PaymentResponse } from "@/features/payments/payment.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const PAYMENT_CARD: PaymentResponse = {
  id:                    "pay-001",
  appointmentId:         "appt-001",
  amount:                100,
  status:                "Paid",
  method:                "Card",
  providerReferenceId: "pi_test_abc",
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
  providerReferenceId: null,
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
    // Appears twice per payment: once in the mobileCard, once in the table (dual-render).
    expect(await screen.findAllByText("Maria Silva")).toHaveLength(2);
    expect(screen.getAllByText("João Santos")).toHaveLength(2);
  });

  it("renders the formatted amount in each row", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json([PAYMENT_CARD]),
      ),
    );
    renderPage();
    // Amount 100 → "100,00 €" in pt-PT locale (or similar); appears in both the
    // mobileCard and the table (dual-render).
    expect(await screen.findAllByText(/100/)).not.toHaveLength(0);
  });

  it("renders a status badge for each payment", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json([PAYMENT_CARD, PAYMENT_CASH]),
      ),
    );
    renderPage();
    await screen.findAllByText("Maria Silva");
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
    await screen.findAllByText("Maria Silva");
    // PAYMENT_CARD has a paidAt, so its mobileCard text combines "Card · Paid …" —
    // distinct from the table's plain "Card" cell, so this one stays a single match.
    expect(screen.getByText("Card")).toBeInTheDocument();
    // PAYMENT_CASH has no paidAt, so its mobileCard text is just "Cash" — identical
    // to the table's plain "Cash" cell, so this one legitimately duplicates.
    expect(screen.getAllByText("Cash")).toHaveLength(2);
  });

  it("clicking a row navigates to /payments/:appointmentId", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json([PAYMENT_CARD]),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    const [, tableNameCell] = await screen.findAllByText("Maria Silva");
    await user.click(tableNameCell);
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
    await screen.findAllByText("Maria Silva");
    expect(screen.queryByRole("button", { name: /load more/i })).not.toBeInTheDocument();
  });

  it("shows 'Load more' when exactly 20 results are returned", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json(PAGE_OF_20),
      ),
    );
    renderPage();
    await screen.findAllByText("Client 0");
    expect(screen.getByRole("button", { name: /load more/i })).toBeInTheDocument();
  });

  it("shows '20 payments' counter with a full page", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json(PAGE_OF_20),
      ),
    );
    renderPage();
    await screen.findAllByText("Client 0");
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
    await screen.findAllByText("Client 0");

    await user.click(screen.getByRole("button", { name: /load more/i }));

    expect(await screen.findAllByText("Extra Client")).toHaveLength(2);
    expect(screen.getAllByText("Client 0")).toHaveLength(2); // previous page still shown
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
    await screen.findAllByText("Maria Silva");
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
    await screen.findAllByText("Maria Silva");
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
    await screen.findAllByText("Maria Silva");
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
    await screen.findAllByText("Maria Silva");

    await user.type(screen.getByPlaceholderText(/search by client name/i), "Maria");

    expect(screen.getAllByText("Maria Silva")).toHaveLength(2);
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
    await screen.findAllByText("João Santos");

    await user.type(screen.getByPlaceholderText(/search by client name/i), "joão");

    expect(screen.getAllByText("João Santos")).toHaveLength(2);
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
    await screen.findAllByText("Maria Silva");

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
    await screen.findAllByText("Maria Silva");

    await user.click(screen.getByRole("button", { name: "Paid" }));

    expect(screen.getAllByText("Maria Silva")).toHaveLength(2);   // Paid
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
    await screen.findAllByText("Maria Silva");

    await user.click(screen.getByRole("button", { name: "Paid" }));
    expect(screen.queryByText("João Santos")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Paid" }));
    expect(screen.getAllByText("João Santos")).toHaveLength(2);
  });

  // ── mobileCard ─────────────────────────────────────────────────────────────────

  it("renders a mobileCard combining method and paid date on one line when paidAt is set", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json([PAYMENT_CARD]),
      ),
    );
    renderPage();
    await screen.findAllByText("Maria Silva");

    const cardList = screen.getByRole("list");
    expect(within(cardList).getByText("Maria Silva")).toBeInTheDocument();
    expect(within(cardList).getByText(/Card · Paid/)).toBeInTheDocument();
  });
});
