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
import { promoCodesApi } from "@/features/promo-codes/promoCodesApi";
import { PromoCodeListPage } from "@/features/promo-codes/components/PromoCodeListPage";
import type { PromoCodeResponse } from "@/features/promo-codes/promoCode.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const CODE_FIXED: PromoCodeResponse = {
  id:              "pc-001",
  studioId:        "s-001",
  code:            "SUMMER20",
  amountFixed:     50,
  amountPercent:   null,
  isActive:        true,
  expiresAt:       null,
  maxRedemptions:  null,
  redemptionCount: 3,
  createdAt:       "2024-01-15T10:00:00Z",
  updatedAt:       "2024-01-15T10:00:00Z",
};

const CODE_PERCENT: PromoCodeResponse = {
  id:              "pc-002",
  studioId:        "s-001",
  code:            "WINTERBIG",
  amountFixed:     null,
  amountPercent:   20,
  isActive:        false,
  expiresAt:       null,
  maxRedemptions:  10,
  redemptionCount: 2,
  createdAt:       "2024-02-01T10:00:00Z",
  updatedAt:       "2024-02-01T10:00:00Z",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/promo-codes", () =>
    HttpResponse.json([CODE_FIXED, CODE_PERCENT]),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                        authReducer,
      ui:                          uiReducer,
      [promoCodesApi.reducerPath]: promoCodesApi.reducer,
    },
    middleware: (gd) => gd().concat(promoCodesApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "owner@test.com" }, token: "fake-token", tenantId: "s-001", role: "owner", pendingReferralCode: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null },
    },
  });
}

function renderPage() {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter initialEntries={["/promo-codes"]}>
        <Routes>
          <Route path="/promo-codes"     element={<PromoCodeListPage />} />
          <Route path="/promo-codes/new" element={<div data-testid="create-page" />} />
          <Route path="/promo-codes/:id" element={<div data-testid="detail-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("PromoCodeListPage", () => {

  it("renders the Promo Codes header", () => {
    renderPage();
    expect(screen.getByText("Promo Codes")).toBeInTheDocument();
  });

  it("shows an error message when the promo codes fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/promo-codes", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText("Failed to load promo codes. Please try again.")).toBeInTheDocument();
  });

  it("shows rich empty state when no codes exist", async () => {
    server.use(
      http.get("http://localhost/api/v1/promo-codes", () => HttpResponse.json([])),
    );
    renderPage();
    expect(await screen.findByText("No promo codes yet")).toBeInTheDocument();
  });

  it("renders a card for each code returned by the API", async () => {
    renderPage();
    expect(await screen.findByText("SUMMER20")).toBeInTheDocument();
    expect(screen.getByText("WINTERBIG")).toBeInTheDocument();
  });

  it("shows the fixed amount and Active badge for an active fixed code", async () => {
    renderPage();
    await screen.findByText("SUMMER20");
    expect(screen.getByText(/fixed · 50,00\s?€/i)).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("shows the percent amount, redemption count, and Inactive badge", async () => {
    renderPage();
    await screen.findByText("WINTERBIG");
    expect(screen.getByText(/percent · 20% · 2\/10 used/i)).toBeInTheDocument();
    expect(screen.getByText("Inactive")).toBeInTheDocument();
  });

  it("shows '2 codes' count in the header", async () => {
    renderPage();
    expect(await screen.findByText("2 codes")).toBeInTheDocument();
  });

  it("'New Code' button navigates to /promo-codes/new", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("SUMMER20");
    await user.click(screen.getByRole("button", { name: /new code/i }));
    expect(screen.getByTestId("create-page")).toBeInTheDocument();
  });

  it("clicking a code card navigates to the detail page", async () => {
    const user = userEvent.setup();
    renderPage();
    const link = await screen.findByRole("link", { name: /summer20/i });
    await user.click(link);
    expect(screen.getByTestId("detail-page")).toBeInTheDocument();
  });
});
