import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { Toaster } from "sonner";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { promoCodesApi } from "@/features/promo-codes/promoCodesApi";
import { PromoCodeDetailPage } from "@/features/promo-codes/components/PromoCodeDetailPage";
import type { PromoCodeResponse } from "@/features/promo-codes/promoCode.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const CODE_ID = "pc-001";

const CODE: PromoCodeResponse = {
  id:              CODE_ID,
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

const UPDATED_CODE: PromoCodeResponse = {
  ...CODE,
  code:        "UPDATED20",
  amountFixed: 75,
  updatedAt:   "2024-03-01T10:00:00Z",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/promo-codes/:id", () => HttpResponse.json(CODE)),
  http.put("http://localhost/api/v1/promo-codes/:id", () => HttpResponse.json(UPDATED_CODE)),
  http.delete("http://localhost/api/v1/promo-codes/:id", () => new HttpResponse(null, { status: 204 })),
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

function renderPage(codeId = CODE_ID) {
  render(
    <Provider store={makeStore()}>
      <Toaster />
      <MemoryRouter initialEntries={[`/promo-codes/${codeId}`]}>
        <Routes>
          <Route path="/promo-codes/:id" element={<PromoCodeDetailPage />} />
          <Route path="/promo-codes"     element={<div data-testid="list-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("PromoCodeDetailPage", () => {

  // ── Loading / error ────────────────────────────────────────────────────────

  it("shows a loading skeleton while the code is fetching", () => {
    renderPage();
    expect(screen.getByLabelText(/loading promo code/i)).toBeInTheDocument();
  });

  it("shows a not-found message when the code fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/promo-codes/:id", () =>
        HttpResponse.json({ message: "error" }, { status: 404 }),
      ),
    );
    renderPage();
    expect(await screen.findByText("Promo code not found.")).toBeInTheDocument();
  });

  // ── View mode ─────────────────────────────────────────────────────────────

  it("renders the code and Active badge", async () => {
    renderPage();
    expect(await screen.findByText("SUMMER20")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("renders the fixed amount", async () => {
    renderPage();
    await screen.findByText("SUMMER20");
    expect(screen.getByText(/fixed · 50,00\s?€/i)).toBeInTheDocument();
  });

  it("renders a percent code correctly", async () => {
    server.use(
      http.get("http://localhost/api/v1/promo-codes/:id", () =>
        HttpResponse.json({ ...CODE, amountFixed: null, amountPercent: 20, isActive: false }),
      ),
    );
    renderPage();
    await screen.findByText("SUMMER20");
    expect(screen.getByText(/percentage · 20%/i)).toBeInTheDocument();
    expect(screen.getByText("Inactive")).toBeInTheDocument();
  });

  it("shows 'Never' expiry and the redemption count when unlimited", async () => {
    renderPage();
    await screen.findByText("SUMMER20");
    expect(screen.getByText(/expires: never/i)).toBeInTheDocument();
    expect(screen.getByText(/redemptions: 3$/i)).toBeInTheDocument();
  });

  it("shows redemption count out of max when limited", async () => {
    server.use(
      http.get("http://localhost/api/v1/promo-codes/:id", () =>
        HttpResponse.json({ ...CODE, maxRedemptions: 10 }),
      ),
    );
    renderPage();
    await screen.findByText("SUMMER20");
    expect(screen.getByText(/redemptions: 3 \/ 10/i)).toBeInTheDocument();
  });

  // ── Edit flow ─────────────────────────────────────────────────────────────

  it("Edit and Delete buttons are visible", async () => {
    renderPage();
    await screen.findByText("SUMMER20");
    expect(screen.getByRole("button", { name: /edit/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /delete/i })).toBeInTheDocument();
  });

  it("clicking Edit shows the edit form pre-filled with the code's values", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /edit/i }));
    expect(screen.getByLabelText("Code")).toHaveValue("SUMMER20");
    expect(screen.getByLabelText("Discount (€)")).toHaveValue(50);
  });

  it("Cancel exits the edit form without saving", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /edit/i }));
    await user.click(screen.getByRole("button", { name: /cancel/i }));
    expect(screen.getByText("SUMMER20")).toBeInTheDocument();
    expect(screen.queryByLabelText("Code")).not.toBeInTheDocument();
  });

  it("saving valid changes shows a success toast and returns to view mode", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /edit/i }));
    await user.clear(screen.getByLabelText("Code"));
    await user.type(screen.getByLabelText("Code"), "UPDATED20");
    await user.click(screen.getByRole("button", { name: /save changes/i }));
    expect(await screen.findByText("Promo code updated.")).toBeInTheDocument();
    expect(screen.queryByLabelText("Code")).not.toBeInTheDocument();
  });

  it("shows an error toast and stays in edit mode when the update fails", async () => {
    server.use(
      http.put("http://localhost/api/v1/promo-codes/:id", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /edit/i }));
    await user.click(screen.getByRole("button", { name: /save changes/i }));
    expect(await screen.findByText("Failed to update promo code.")).toBeInTheDocument();
    expect(screen.getByLabelText("Code")).toBeInTheDocument();
  });

  // ── Delete flow ──────────────────────────────────────────────────────────────

  it("clicking Delete shows a delete confirmation", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /delete/i }));
    expect(screen.getByText('Delete "SUMMER20"?')).toBeInTheDocument();
  });

  it("confirming delete navigates back to the list", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /delete/i }));
    await user.click(screen.getByRole("button", { name: /^delete$/i }));
    expect(await screen.findByTestId("list-page")).toBeInTheDocument();
  });

  // ── Back navigation ──────────────────────────────────────────────────────────

  it("'Promo Codes' back button navigates to /promo-codes", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("SUMMER20");
    await user.click(screen.getByRole("button", { name: /promo codes/i }));
    expect(screen.getByTestId("list-page")).toBeInTheDocument();
  });
});
