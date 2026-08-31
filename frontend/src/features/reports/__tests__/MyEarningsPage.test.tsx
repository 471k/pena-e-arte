import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { reportsApi } from "@/features/reports/reportsApi";
import { MyEarningsPage } from "@/features/reports/components/MyEarningsPage";
import type { ArtistEarningsResponse } from "@/features/reports/report.types";
import { Role } from "@/shared/types/roles";

// ── Seed data ──────────────────────────────────────────────────────────────────

const EARNINGS: ArtistEarningsResponse = {
  monthlyTrend: [
    { month: "2026-06", revenue: 240 },
    { month: "2026-07", revenue: 380 },
  ],
  periodTotal: 380,
  payments: [
    {
      paymentId: "p-001",
      appointmentId: "a-001",
      appointmentDate: "2026-07-15T10:00:00Z",
      clientName: "Besa Klienti",
      amount: 380,
      splits: [
        { id: "s-001", paymentId: "p-001", label: "Artist cut", amount: 300, paidAt: null },
        { id: "s-002", paymentId: "p-001", label: "Studio fee", amount: 80, paidAt: null },
      ],
    },
  ],
};

const EMPTY_EARNINGS: ArtistEarningsResponse = { monthlyTrend: [], periodTotal: 0, payments: [] };

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/reports/my-earnings", () => HttpResponse.json(EARNINGS)),
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
    },
    middleware: (gd) => gd().concat(reportsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "test@test.com" }, token: "fake-token", tenantId: "s-001", role, pendingReferralCode: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null },
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

  it("formats the period total as currency", async () => {
    renderPage();
    expect(await screen.findAllByText(/380,00\s?€/)).not.toHaveLength(0);
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
