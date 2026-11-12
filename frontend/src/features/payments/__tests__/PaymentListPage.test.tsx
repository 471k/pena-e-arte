import { describe, it, expect, beforeAll, afterEach, afterAll, vi } from "vitest";
import { fireEvent, render, screen, cleanup, waitFor, within } from "@testing-library/react";
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
  clientToken:          null,
  cashNote:              null,
  paidAt:                "2026-06-10T10:00:00Z",
  clientName:            "Maria Silva",
  appointmentDate:       "2026-06-15T14:00:00Z",
  currency:              "EUR",
};

const PAYMENT_CASH: PaymentResponse = {
  id:                    "pay-002",
  appointmentId:         "appt-002",
  amount:                50,
  status:                "CashPending",
  method:                "Cash",
  providerReferenceId: null,
  clientToken:          null,
  cashNote:              null,
  paidAt:                null,
  clientName:            "João Santos",
  appointmentDate:       "2026-06-20T11:00:00Z",
  currency:              "EUR",
};

// Card deposit authorised but not yet charged — the API calls it "Captured"; owners see "On hold".
const PAYMENT_HELD: PaymentResponse = {
  ...PAYMENT_CARD,
  id:            "pay-003",
  appointmentId: "appt-003",
  status:        "Captured",
  paidAt:        null,
  clientName:    "Ana Held",
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
  // E05b-2: the "Not settled yet" section above the history; its own behaviour is in UnsettledPaymentsSection.test.tsx.
  http.get("http://localhost/api/v1/payments/unsettled", () =>
    HttpResponse.json({ generatedAt: new Date().toISOString(), checkOverdueAfterHours: 26, maxItemsPerGroup: 10, groups: [] }),
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
      auth: { user: { id: "u-001", email: "owner@test.com" }, token: "fake-token", tenantId: "s-001", role: "owner", pendingReferralCode: null, impersonation: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null, impersonationScopeError: null, impersonationSessionExpired: false },
    },
  });
}

function renderPage() {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/payments"]}>
        <Routes>
          <Route path="/payments"               element={<PaymentListPage />} />
          <Route path="/payments/new"           element={<div data-testid="new-page" />} />
          <Route path="/payments/:appointmentId" element={<div data-testid="detail-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
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

  it("counts the rows shown, not a total it doesn't know", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json(PAGE_OF_20),
      ),
    );
    renderPage();
    await screen.findAllByText("Client 0");
    expect(screen.getByTestId("payments-shown")).toHaveTextContent("20 shown");
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

  // ── Server-side search and filters (E05b-1, O05) ──────────────────────────────
  // Every filter goes to the server as a query parameter. Filtering the loaded page in the browser could
  // only ever find a payment among the 20 already loaded.

  /** Records every request's query string and answers each with the given rows. */
  function captureRequests(rows: (params: URLSearchParams) => PaymentResponse[]) {
    const seen: URLSearchParams[] = [];
    server.use(
      http.get("http://localhost/api/v1/payments", ({ request }) => {
        const params = new URL(request.url).searchParams;
        seen.push(params);
        return HttpResponse.json(rows(params));
      }),
    );
    return seen;
  }

  it("search is sent to the server, debounced into one request, and finds client or artist names", async () => {
    const seen = captureRequests((q) => (q.get("search") === "maria" ? [PAYMENT_CARD] : [PAYMENT_CARD, PAYMENT_CASH]));
    const user = userEvent.setup();
    renderPage();
    await screen.findAllByText("João Santos");

    await user.type(screen.getByRole("searchbox", { name: /search payments by client or artist name/i }), "maria");

    await waitFor(() => expect(screen.queryByText("João Santos")).not.toBeInTheDocument());
    expect(await screen.findAllByText("Maria Silva")).toHaveLength(2);
    const searches = seen.map((q) => q.get("search")).filter(Boolean);
    expect(searches).toEqual(["maria"]); // one request for the settled term, not one per keystroke
  });

  it("every status has a filter pill, whatever the loaded rows contain", async () => {
    captureRequests(() => [PAYMENT_CARD]);
    renderPage();
    await screen.findAllByText("Maria Silva");

    for (const label of ["Pending", "Cash Pending", "On hold", "Paid", "Refunded or released", "Failed"]) {
      expect(screen.getByRole("button", { name: label })).toHaveAttribute("aria-pressed", "false");
    }
  });

  it("a status pill sends its exact status name, and pressing it again clears it", async () => {
    const seen = captureRequests((q) => (q.get("status") === "CashPending" ? [PAYMENT_CASH] : [PAYMENT_CARD, PAYMENT_CASH]));
    const user = userEvent.setup();
    renderPage();
    await screen.findAllByText("Maria Silva");

    await user.click(screen.getByRole("button", { name: "Cash Pending" }));
    await waitFor(() => expect(screen.queryByText("Maria Silva")).not.toBeInTheDocument());
    expect(seen.at(-1)?.get("status")).toBe("CashPending");

    await user.click(screen.getByRole("button", { name: "Cash Pending" }));
    // Back to the unfiltered list (served from RTK Query's cache for those arguments, so no new request).
    expect(await screen.findAllByText("Maria Silva")).toHaveLength(2);
    expect(screen.getByRole("button", { name: "Cash Pending" })).toHaveAttribute("aria-pressed", "false");
  });

  it("a date range is sent as studio-local dates", async () => {
    const seen = captureRequests(() => [PAYMENT_CARD]);
    renderPage();
    await screen.findAllByText("Maria Silva");

    fireEvent.change(screen.getByLabelText("Paid from"), { target: { value: "2026-10-01" } });
    fireEvent.change(screen.getByLabelText("Paid to"), { target: { value: "2026-10-31" } });

    await waitFor(() => expect(seen.at(-1)?.get("to")).toBe("2026-10-31"));
    expect(seen.at(-1)?.get("from")).toBe("2026-10-01");
  });

  it("a 'to' date before the 'from' date is not sent, and says why", async () => {
    const seen = captureRequests(() => [PAYMENT_CARD]);
    renderPage();
    await screen.findAllByText("Maria Silva");

    fireEvent.change(screen.getByLabelText("Paid from"), { target: { value: "2026-10-31" } });
    fireEvent.change(screen.getByLabelText("Paid to"), { target: { value: "2026-10-01" } });

    expect(await screen.findByRole("alert")).toHaveTextContent(/before .Paid from./);
    expect(seen.every((q) => !q.has("to"))).toBe(true);
  });

  it("changing a filter starts again from the first page", async () => {
    const seen = captureRequests((q) => (q.get("lastSeenId") ? [{ ...PAYMENT_CASH, id: "pay-x", appointmentId: "appt-x", clientName: "Extra Client" }] : PAGE_OF_20));
    const user = userEvent.setup();
    renderPage();
    await screen.findAllByText("Client 0");
    await user.click(screen.getByRole("button", { name: /load more/i }));
    await screen.findAllByText("Extra Client");

    await user.click(screen.getByRole("button", { name: "Paid" }));

    await waitFor(() => expect(seen.at(-1)?.get("status")).toBe("Paid"));
    expect(seen.at(-1)?.has("lastSeenId")).toBe(false);
    await waitFor(() => expect(screen.queryByText("Extra Client")).not.toBeInTheDocument());
  });

  // ── Four distinct states ─────────────────────────────────────────────────────

  it("nothing matching the filters is its own state, not 'No payments yet'", async () => {
    captureRequests((q) => (q.get("status") === "Failed" ? [] : [PAYMENT_CARD]));
    const user = userEvent.setup();
    renderPage();
    await screen.findAllByText("Maria Silva");

    await user.click(screen.getByRole("button", { name: "Failed" }));

    expect(await screen.findByTestId("payments-no-matches")).toHaveTextContent("No payments match these filters");
    expect(screen.queryByText("No payments yet")).not.toBeInTheDocument();

    await user.click(within(screen.getByTestId("payments-no-matches")).getByRole("button", { name: /clear filters/i }));
    expect(await screen.findAllByText("Maria Silva")).toHaveLength(2);
  });

  it("loading, error, empty and no-matches never show at the same time", async () => {
    renderPage(); // default handler: []
    expect(await screen.findByText("No payments yet")).toBeInTheDocument();
    expect(screen.queryByLabelText("Loading payments")).not.toBeInTheDocument();
    expect(screen.queryByTestId("payments-no-matches")).not.toBeInTheDocument();
    expect(screen.queryByText(/failed to load payments/i)).not.toBeInTheDocument();
  });

  it("shows a held (Captured) card deposit as 'On hold' in both its badge and its filter pill", async () => {
    server.use(
      http.get("http://localhost/api/v1/payments", () =>
        HttpResponse.json([PAYMENT_CARD, PAYMENT_HELD]),
      ),
    );
    renderPage();
    await screen.findAllByText("Ana Held");

    expect(screen.getByRole("button", { name: "On hold" })).toBeInTheDocument();
    // pill + desktop badge + mobile-card badge
    expect(screen.getAllByText("On hold").length).toBeGreaterThanOrEqual(2);
    expect(screen.queryByText("Captured")).not.toBeInTheDocument();
  });

  // ── Export with the list's filters (E05b-1, O08) ──────────────────────────────

  it("Export CSV downloads the revenue export with the list's current filters", async () => {
    captureRequests(() => [PAYMENT_CARD]);
    const exportUrls: string[] = [];
    server.use(
      http.get("http://localhost/api/v1/reports/revenue/export.csv", ({ request }) => {
        exportUrls.push(request.url);
        return new HttpResponse("csv", { headers: { "Content-Type": "text/csv" } });
      }),
    );
    URL.createObjectURL = vi.fn(() => "blob:x");
    URL.revokeObjectURL = vi.fn();
    const user = userEvent.setup();
    renderPage();
    await screen.findAllByText("Maria Silva");

    await user.click(screen.getByRole("button", { name: "Paid" }));
    fireEvent.change(screen.getByLabelText("Paid from"), { target: { value: "2026-10-01" } });
    await user.click(screen.getByRole("button", { name: /export csv/i }));

    await waitFor(() => expect(exportUrls).toHaveLength(1));
    const q = new URL(exportUrls[0]).searchParams;
    expect(q.get("status")).toBe("Paid");
    expect(q.get("from")).toBe("2026-10-01");
  });

  it("Export CSV is off for a status the revenue export can't hold, and says why", async () => {
    captureRequests(() => [PAYMENT_CASH]);
    const user = userEvent.setup();
    renderPage();
    await screen.findAllByText("João Santos");

    await user.click(screen.getByRole("button", { name: "Cash Pending" }));

    expect(screen.getByRole("button", { name: /export csv/i })).toBeDisabled();
    expect(screen.getByText(/holds only paid and refunded payments/)).toBeInTheDocument();
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

  // ── Outcome wording (G-5 / G-6 / G-13) ─────────────────────────────────────────

  it("labels refunded-status rows by what happened, under one 'Refunded or released' filter", async () => {
    const partlyRefunded: PaymentResponse = {
      ...PAYMENT_CARD, id: "pay-r1", appointmentId: "appt-r1", clientName: "Rita Partial",
      status: "Refunded", refundedAmount: 20, appointmentStatus: "Completed",
    };
    const released: PaymentResponse = {
      ...PAYMENT_CARD, id: "pay-r2", appointmentId: "appt-r2", clientName: "Rui Released",
      status: "Refunded", paidAt: null, refundedAmount: PAYMENT_CARD.amount, appointmentStatus: "Cancelled",
    };
    server.use(
      http.get("http://localhost/api/v1/payments", () => HttpResponse.json([PAYMENT_CARD, partlyRefunded, released])),
    );
    renderPage();
    await screen.findAllByText("Rita Partial");

    expect(screen.getByRole("button", { name: "Refunded or released" })).toBeInTheDocument();
    expect(screen.getAllByText("Partly refunded").length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText("Released").length).toBeGreaterThanOrEqual(1);
    expect(screen.queryByText("Refunded")).not.toBeInTheDocument();
  });
});
