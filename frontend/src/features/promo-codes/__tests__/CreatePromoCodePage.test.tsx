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
import { CreatePromoCodePage } from "@/features/promo-codes/components/CreatePromoCodePage";
import type { PromoCodeResponse } from "@/features/promo-codes/promoCode.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const CREATED_CODE: PromoCodeResponse = {
  id:              "pc-new",
  studioId:        "s-001",
  code:            "SUMMER20",
  amountFixed:     20,
  amountPercent:   null,
  isActive:        true,
  expiresAt:       null,
  maxRedemptions:  null,
  redemptionCount: 0,
  createdAt:       "2024-06-01T10:00:00Z",
  updatedAt:       "2024-06-01T10:00:00Z",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.post("http://localhost/api/v1/promo-codes", () =>
    HttpResponse.json(CREATED_CODE, { status: 201 }),
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
      <Toaster />
      <MemoryRouter initialEntries={["/promo-codes/new"]}>
        <Routes>
          <Route path="/promo-codes/new" element={<CreatePromoCodePage />} />
          <Route path="/promo-codes"     element={<div data-testid="list-page" />} />
          <Route path="/promo-codes/:id" element={<div data-testid="detail-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("CreatePromoCodePage", () => {

  it("renders the 'New Promo Code' heading", () => {
    renderPage();
    expect(screen.getByText("New Promo Code")).toBeInTheDocument();
  });

  it("renders Code, Discount type, Amount and Active fields", () => {
    renderPage();
    expect(screen.getByLabelText("Code")).toBeInTheDocument();
    expect(screen.getByText("Fixed amount")).toBeInTheDocument();
    expect(screen.getByText("Percentage")).toBeInTheDocument();
    expect(screen.getByLabelText("Discount (€)")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("defaults to fixed amount with the Discount (€) label", () => {
    renderPage();
    expect(screen.getByLabelText("Discount (€)")).toBeInTheDocument();
  });

  it("switching to Percentage updates the amount label", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole("radio", { name: /percentage/i }));
    expect(screen.getByLabelText("Discount (%)")).toBeInTheDocument();
  });

  it("shows validation errors when submitting an empty form", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole("button", { name: /create code/i }));
    expect(await screen.findByText("Code is required")).toBeInTheDocument();
    expect(screen.getByText("Amount is required")).toBeInTheDocument();
  });

  it("rejects a percentage amount above 100", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText("Code"), "BIGONE");
    await user.click(screen.getByRole("radio", { name: /percentage/i }));
    await user.type(screen.getByLabelText("Discount (%)"), "150");
    await user.click(screen.getByRole("button", { name: /create code/i }));
    expect(await screen.findByText("Must be between 0.01 and 100")).toBeInTheDocument();
  });

  it("submits the code uppercased along with max redemptions", async () => {
    let capturedBody: Record<string, unknown> | undefined;
    server.use(
      http.post("http://localhost/api/v1/promo-codes", async ({ request }) => {
        capturedBody = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(CREATED_CODE, { status: 201 });
      }),
    );
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText("Code"), "summer20");
    await user.type(screen.getByLabelText("Discount (€)"), "20");
    await user.type(screen.getByLabelText(/max redemptions/i), "100");
    await user.click(screen.getByRole("button", { name: /create code/i }));
    await screen.findByTestId("detail-page");
    expect(capturedBody?.code).toBe("SUMMER20");
    expect(capturedBody?.maxRedemptions).toBe(100);
  });

  it("creates a fixed-amount code and navigates to its detail page", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText("Code"), "SUMMER20");
    await user.type(screen.getByLabelText("Discount (€)"), "20");
    await user.click(screen.getByRole("button", { name: /create code/i }));
    expect(await screen.findByTestId("detail-page")).toBeInTheDocument();
  });

  it("shows a success toast after creating a code", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText("Code"), "SUMMER20");
    await user.type(screen.getByLabelText("Discount (€)"), "20");
    await user.click(screen.getByRole("button", { name: /create code/i }));
    expect(await screen.findByText("Promo code created.")).toBeInTheDocument();
  });

  it("shows an error toast and stays on the page when the API call fails", async () => {
    server.use(
      http.post("http://localhost/api/v1/promo-codes", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText("Code"), "SUMMER20");
    await user.type(screen.getByLabelText("Discount (€)"), "20");
    await user.click(screen.getByRole("button", { name: /create code/i }));
    expect(await screen.findByText("Failed to create promo code.")).toBeInTheDocument();
    expect(screen.queryByTestId("detail-page")).not.toBeInTheDocument();
  });

  it("'Promo Codes' back button navigates to /promo-codes", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole("button", { name: /promo codes/i }));
    expect(screen.getByTestId("list-page")).toBeInTheDocument();
  });
});
