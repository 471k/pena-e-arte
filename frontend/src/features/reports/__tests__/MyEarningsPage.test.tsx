import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { reportsApi } from "@/features/reports/reportsApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { boothRentApi } from "@/features/booth-rent/boothRentApi";
import { MyEarningsPage } from "@/features/reports/components/MyEarningsPage";
import type { ArtistEarningsResponse } from "@/features/reports/report.types";
import { Role } from "@/shared/types/roles";
import { STUDIO_TIME_PENDING, STUDIO_TIME_UNAVAILABLE } from "@/shared/utils/formatInStudioTimezone";

// ── Seed data ──────────────────────────────────────────────────────────────────

// An artist on 60% commission: the client paid 380, the artist's share is 228.
const EARNINGS: ArtistEarningsResponse = {
  monthlyTrend: [
    { month: "2026-06", artistAmount: 144, clientPaid: 240, shareIsKnown: true },
    { month: "2026-07", artistAmount: 228, clientPaid: 380, shareIsKnown: true },
  ],
  periodTotal: 228,
  clientPaidTotal: 380,
  shareIsKnown: true,
  arrangement: "Commission",
  commissionRate: 60,
  flatFeePerSession: null,
  payments: [
    {
      paymentId: "p-001",
      appointmentId: "a-001",
      appointmentDate: "2026-07-15T10:00:00Z",
      paidAt: "2026-06-20T09:00:00Z",
      clientName: "Besa Klienti",
      clientPaidAmount: 380,
      artistAmount: 228,
      shareIsKnown: true,
      status: "Paid",
      amount: 380,
      refundedAmount: null,
      splits: [
        { id: "s-001", paymentId: "p-001", label: "Artist cut", amount: 300, paidAt: null },
        { id: "s-002", paymentId: "p-001", label: "Studio fee", amount: 80, paidAt: null },
      ],
    },
  ],
  currency: "EUR",
  excludedOtherCurrencyCount: 0,
};

const EMPTY_EARNINGS: ArtistEarningsResponse = {
  monthlyTrend: [], periodTotal: 0, clientPaidTotal: 0, payments: [], currency: "EUR", excludedOtherCurrencyCount: 0,
  shareIsKnown: true, arrangement: "Commission", commissionRate: 60, flatFeePerSession: null,
};

// Registration's default: Commission with no rate. The share can't be worked out.
const UNKNOWN_SHARE: ArtistEarningsResponse = {
  ...EARNINGS,
  monthlyTrend: [{ month: "2026-07", artistAmount: null, clientPaid: 380, shareIsKnown: false }],
  periodTotal: null,
  shareIsKnown: false,
  commissionRate: null,
  payments: [{ ...EARNINGS.payments[0], artistAmount: null, shareIsKnown: false }],
};

function respondWith(body: ArtistEarningsResponse) {
  server.use(http.get("http://localhost/api/v1/reports/my-earnings", () => HttpResponse.json(body)));
}

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/reports/my-earnings", () => HttpResponse.json(EARNINGS)),
  http.get("http://localhost/api/v1/studios/me", () =>
    HttpResponse.json({ id: "s-001", timezone: "Europe/Tirane", currency: "EUR" }),
  ),
  // MyEarningsPage embeds MyBoothRentSection — empty schedules means it renders nothing,
  // matching the typical "no booth-rent schedule" artist and leaving existing assertions
  // about the earnings content itself unaffected.
  http.get("http://localhost/api/v1/booth-rent/schedules", () => HttpResponse.json([])),
  http.get("http://localhost/api/v1/booth-rent/charges", () => HttpResponse.json([])),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore(role: Role = Role.Artist) {
  return configureStore({
    reducer: {
      auth:                       authReducer,
      ui:                         uiReducer,
      [reportsApi.reducerPath]:   reportsApi.reducer,
      [studiosApi.reducerPath]:   studiosApi.reducer,
      [boothRentApi.reducerPath]: boothRentApi.reducer,
    },
    middleware: (gd) => gd().concat(reportsApi.middleware, studiosApi.middleware, boothRentApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "test@test.com" }, token: "fake-token", tenantId: "s-001", role, pendingReferralCode: null, impersonation: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null, impersonationScopeError: null, impersonationSessionExpired: false },
    },
  });
}

function renderPage(role: Role = Role.Artist) {
  render(
    <Provider store={makeStore(role)}>
      <MemoryRouter>
        <MyEarningsPage />
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("MyEarningsPage", () => {
  it("renders the 'My Earnings' heading", () => {
    renderPage();
    expect(screen.getByText("My Earnings")).toBeInTheDocument();
  });

  it("first paint: while the studio's zone loads, the dates read as pending, never the browser's date", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios/me", async () => {
        await new Promise((r) => setTimeout(r, 60_000));
        return HttpResponse.json({ id: "s-001", timezone: "Europe/Tirane", currency: "EUR" });
      }),
    );
    renderPage();
    expect(await screen.findAllByText(STUDIO_TIME_PENDING)).not.toHaveLength(0);
    // 09:00 UTC on 20 Jun: any browser-zone rendering says "20 Jun 2026".
    expect(screen.queryByText("20 Jun 2026")).not.toBeInTheDocument();
  });

  it("when the studio's zone can't be read, the dates say so instead of falling back to the browser's date", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios/me", async () => {
        await new Promise((r) => setTimeout(r, 150));
        return HttpResponse.json({ message: "error" }, { status: 500 });
      }),
    );
    renderPage();
    // The earnings still load; only the two studio-calendar dates per line read as unavailable.
    expect(await screen.findByText("Besa Klienti")).toBeInTheDocument();
    // The earnings can arrive before the studio's error (the delay above makes that the case every run, as it was
    // on main's CI on 28 Nov): the line first reads pending, so wait for the text, not just the element.
    await waitFor(() => expect(screen.getByTestId("earnings-line-dates"))
      .toHaveTextContent(`Paid ${STUDIO_TIME_UNAVAILABLE} · Session ${STUDIO_TIME_UNAVAILABLE}`));
    // 09:00 UTC on 20 Jun: any browser-zone rendering says "20 Jun 2026".
    expect(screen.queryByText("20 Jun 2026")).not.toBeInTheDocument();
  });

  it("renders the payment line dates in the studio's timezone, not the test environment's local timezone", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios/me", () =>
        HttpResponse.json({ id: "s-001", timezone: "America/New_York", currency: "EUR" }),
      ),
    );
    renderPage();

    const fmt = (iso: string) => new Date(iso).toLocaleDateString(
      "en-GB", { day: "numeric", month: "short", year: "numeric", timeZone: "America/New_York" });
    expect(await screen.findByText(fmt(EARNINGS.payments[0].appointmentDate!))).toBeInTheDocument();
    expect(screen.getByText(fmt(EARNINGS.payments[0].paidAt))).toBeInTheDocument();
  });

  // A07a cleanup, finding 5: the list is filtered on the payment date, so each line says when it was
  // paid and when the session is, each labelled.
  it("shows when each payment was received and when its session is, both labelled", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios/me", () =>
        HttpResponse.json({ id: "s-001", timezone: "Europe/Tirane", currency: "EUR" }),
      ),
    );
    renderPage();

    expect(await screen.findByRole("heading", { name: "Payments received (last 30 days)" })).toBeInTheDocument();
    expect(screen.getByTestId("earnings-line-dates")).toHaveTextContent("Paid 20 Jun 2026 · Session 15 Jul 2026");
    const times = screen.getByTestId("earnings-line-dates").querySelectorAll("time");
    expect(Array.from(times, (t) => t.getAttribute("datetime"))).toEqual(["2026-06-20T09:00:00Z", "2026-07-15T10:00:00Z"]);
  });

  it("shows a loading skeleton while fetching", () => {
    renderPage();
    expect(document.querySelectorAll('[class*="animate-pulse"]').length).toBeGreaterThan(0);
  });

  it("shows an error state with a retry link when the fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/reports/my-earnings", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText("Failed to load your earnings.")).toBeInTheDocument();
    expect(screen.getByText("Try again")).toBeInTheDocument();
  });

  it("retrying refetches the earnings", async () => {
    let callCount = 0;
    server.use(
      http.get("http://localhost/api/v1/reports/my-earnings", () => {
        callCount += 1;
        return callCount === 1
          ? HttpResponse.json({ message: "error" }, { status: 500 })
          : HttpResponse.json(EARNINGS);
      }),
    );
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Failed to load your earnings.");
    await user.click(screen.getByText("Try again"));
    expect(await screen.findByText("Besa Klienti")).toBeInTheDocument();
  });

  it("shows an empty state when there's no earnings data yet", async () => {
    server.use(
      http.get("http://localhost/api/v1/reports/my-earnings", () => HttpResponse.json(EMPTY_EARNINGS)),
    );
    renderPage();
    expect(await screen.findByText("No earnings recorded yet.")).toBeInTheDocument();
    expect(await screen.findByText("No payments recorded yet.")).toBeInTheDocument();
  });

  it("renders the earnings trend chart with real data", async () => {
    renderPage();
    expect(await screen.findByRole("img", { name: /revenue trend/i })).toBeInTheDocument();
  });

  it("renders the payment line with client name and session splits", async () => {
    renderPage();
    expect(await screen.findByText("Besa Klienti")).toBeInTheDocument();
    expect(screen.getByText("Artist cut")).toBeInTheDocument();
    expect(screen.getByText("Studio fee")).toBeInTheDocument();
  });

  it("headlines the artist's share, labelled with the arrangement, with what clients paid beside it", async () => {
    renderPage();
    const share = await screen.findByTestId("earnings-share");
    expect(within(share).getByTestId("earnings-period-total")).toHaveTextContent("€228");
    expect(within(share).getByText(/60% commission/)).toBeInTheDocument();
    expect(within(share).getByTestId("earnings-client-paid-total")).toHaveTextContent("€380");
    expect(screen.getByTestId("earnings-line-share")).toHaveTextContent("€228");
    expect(screen.getByText("Client paid €380")).toBeInTheDocument();
  });

  // E05b-1, carried finding 1: a fully refunded payment read "Client paid ALL 0".
  it("a fully refunded payment shows what the client paid, struck through and marked Refunded, never paid 0", async () => {
    respondWith({
      ...EARNINGS,
      payments: [{ ...EARNINGS.payments[0], status: "Refunded", clientPaidAmount: 0, refundedAmount: 380, artistAmount: 0 }],
    });
    renderPage();
    const line = await screen.findByTestId("earnings-line-refunded");
    expect(line).toHaveTextContent("Client paid €380 Refunded");
    expect(within(line).getByText("€380").tagName).toBe("S");
    expect(within(line).getByLabelText("€380, refunded")).toBeInTheDocument();
    expect(screen.queryByText(/Client paid €0/)).not.toBeInTheDocument();
  });

  it("a partly refunded payment shows both the amount paid and the amount refunded", async () => {
    respondWith({
      ...EARNINGS,
      payments: [{ ...EARNINGS.payments[0], status: "Refunded", clientPaidAmount: 300, refundedAmount: 80, artistAmount: 180 }],
    });
    renderPage();
    expect(await screen.findByTestId("earnings-line-refunded")).toHaveTextContent("Client paid €380 · €80 refunded");
  });

  // The defect A07a fixes: an unconfigured studio must never show a number as "your share".
  it("shows no total at all when the share is unknown: not €0 and not what clients paid", async () => {
    respondWith(UNKNOWN_SHARE);
    renderPage();
    const unknown = await screen.findByTestId("earnings-share-unknown");
    expect(within(unknown).getByText(/Your studio hasn't set how artists get paid yet/)).toBeInTheDocument();
    expect(screen.queryByTestId("earnings-period-total")).not.toBeInTheDocument();
    expect(screen.queryByTestId("earnings-share")).not.toBeInTheDocument();
    expect(screen.queryByTestId("earnings-line-share")).not.toBeInTheDocument();
    expect(screen.queryByText("€0")).not.toBeInTheDocument();
    expect(screen.queryByRole("img", { name: /revenue trend/i })).not.toBeInTheDocument();
    // The payment itself is still listed, as what the client paid, never as the artist's.
    expect(screen.getByText("Client paid €380")).toBeInTheDocument();
  });

  it("booth rent: the artist's share is the full amount, and the label says why", async () => {
    respondWith({ ...EARNINGS, arrangement: "BoothRent", commissionRate: null, periodTotal: 380 });
    renderPage();
    const share = await screen.findByTestId("earnings-share");
    expect(within(share).getByTestId("earnings-period-total")).toHaveTextContent("€380");
    expect(within(share).getByText(/booth rent: you keep what clients pay/)).toBeInTheDocument();
  });

  it("salaried: a known zero, labelled, not the unknown state", async () => {
    respondWith({ ...EARNINGS, arrangement: "Salaried", commissionRate: null, periodTotal: 0 });
    renderPage();
    const share = await screen.findByTestId("earnings-share");
    expect(within(share).getByTestId("earnings-period-total")).toHaveTextContent("€0");
    expect(within(share).getByText(/salaried: nothing is earned per session/)).toBeInTheDocument();
    expect(screen.queryByTestId("earnings-share-unknown")).not.toBeInTheDocument();
  });

  it("shows a specific message and no futile retry when the caller has no artist profile", async () => {
    server.use(
      http.get("http://localhost/api/v1/reports/my-earnings", () =>
        HttpResponse.json({ message: "Artist not found" }, { status: 404 }),
      ),
    );
    renderPage(Role.Owner);
    expect(await screen.findByText(/enable your artist profile/i)).toBeInTheDocument();
    expect(screen.queryByText("Try again")).not.toBeInTheDocument();
  });
});
