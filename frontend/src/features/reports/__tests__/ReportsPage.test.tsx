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
import { ReportsPage } from "@/features/reports/components/ReportsPage";
import type { RevenueSummaryResponse } from "@/features/reports/report.types";
import { Role } from "@/shared/types/roles";

// ── Seed data ──────────────────────────────────────────────────────────────────

const SUMMARY: RevenueSummaryResponse = {
  monthlyTrend: [
    { month: "2026-06", revenue: 400 },
    { month: "2026-07", revenue: 650 },
  ],
  perArtist: [
    { artistId: "a-001", artistName: "Luna Artista", revenue: 500 },
    { artistId: "a-002", artistName: "Besa Klienti", revenue: 150 },
  ],
};

const EMPTY_SUMMARY: RevenueSummaryResponse = { monthlyTrend: [], perArtist: [] };

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/reports/revenue-summary", () => HttpResponse.json(SUMMARY)),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore(role: Role = Role.Owner) {
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

function renderPage(role: Role = Role.Owner) {
  render(
    <Provider store={makeStore(role)}>
      <MemoryRouter>
        <ReportsPage />
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("ReportsPage", () => {
  it("renders the 'Reports' heading", () => {
    renderPage();
    expect(screen.getByText("Reports")).toBeInTheDocument();
  });

  it("shows a loading skeleton while fetching", () => {
    renderPage();
    expect(document.querySelectorAll('[class*="animate-pulse"]').length).toBeGreaterThan(0);
  });

  it("shows an error state with a retry link when the fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/reports/revenue-summary", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText("Failed to load revenue report.")).toBeInTheDocument();
    expect(screen.getByText("Try again")).toBeInTheDocument();
  });

  it("retrying refetches the report", async () => {
    let callCount = 0;
    server.use(
      http.get("http://localhost/api/v1/reports/revenue-summary", () => {
        callCount += 1;
        return callCount === 1
          ? HttpResponse.json({ message: "error" }, { status: 500 })
          : HttpResponse.json(SUMMARY);
      }),
    );
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Failed to load revenue report.");
    await user.click(screen.getByText("Try again"));
    expect(await screen.findByText("Luna Artista")).toBeInTheDocument();
  });

  it("shows an empty state when there's no revenue data yet", async () => {
    server.use(
      http.get("http://localhost/api/v1/reports/revenue-summary", () => HttpResponse.json(EMPTY_SUMMARY)),
    );
    renderPage();
    expect(await screen.findAllByText("No revenue recorded yet.")).toHaveLength(2);
  });

  it("renders the revenue trend chart with real data", async () => {
    renderPage();
    expect(await screen.findByRole("img", { name: /revenue trend/i })).toBeInTheDocument();
  });

  it("renders the per-artist breakdown sorted highest revenue first", async () => {
    renderPage();
    const rows = await screen.findAllByRole("row");
    // rows[0] is the header row
    expect(rows[1]).toHaveTextContent("Luna Artista");
    expect(rows[2]).toHaveTextContent("Besa Klienti");
  });

  it("formats per-artist revenue as currency", async () => {
    renderPage();
    expect(await screen.findByText(/500,00\s?€/)).toBeInTheDocument();
  });
});
