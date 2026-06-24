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
import { depositRulesApi } from "@/features/deposit-rules/depositRulesApi";
import { DepositRuleListPage } from "@/features/deposit-rules/components/DepositRuleListPage";
import type { DepositRuleResponse } from "@/features/deposit-rules/depositRule.types";
import { Role } from "@/shared/types/roles";

// ── Seed data ──────────────────────────────────────────────────────────────────

const RULE_FIXED: DepositRuleResponse = {
  id:            "dr-001",
  studioId:      "s-001",
  name:          "Standard Deposit",
  amountFixed:   50,
  amountPercent: null,
  isActive:      true,
  createdAt:     "2024-01-15T10:00:00Z",
  updatedAt:     "2024-01-15T10:00:00Z",
};

const RULE_PERCENT: DepositRuleResponse = {
  id:            "dr-002",
  studioId:      "s-001",
  name:          "Large Pieces",
  amountFixed:   null,
  amountPercent: 20,
  isActive:      false,
  createdAt:     "2024-02-01T10:00:00Z",
  updatedAt:     "2024-02-01T10:00:00Z",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/deposit-rules", () =>
    HttpResponse.json([RULE_FIXED, RULE_PERCENT]),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore(role: Role = Role.Owner) {
  return configureStore({
    reducer: {
      auth:                          authReducer,
      ui:                            uiReducer,
      [depositRulesApi.reducerPath]: depositRulesApi.reducer,
    },
    middleware: (gd) => gd().concat(depositRulesApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "test@test.com" }, token: "fake-token", tenantId: "s-001", role, pendingReferralCode: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false },
    },
  });
}

function renderPage(role: Role = Role.Owner) {
  render(
    <Provider store={makeStore(role)}>
      <MemoryRouter initialEntries={["/deposit-rules"]}>
        <Routes>
          <Route path="/deposit-rules"     element={<DepositRuleListPage />} />
          <Route path="/deposit-rules/new" element={<div data-testid="create-page" />} />
          <Route path="/deposit-rules/:id" element={<div data-testid="detail-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("DepositRuleListPage", () => {

  it("renders the Deposit Rules header", () => {
    renderPage();
    expect(screen.getByText("Deposit Rules")).toBeInTheDocument();
  });

  it("shows an error message when the deposit rules fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/deposit-rules", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText("Failed to load deposit rules. Please try again.")).toBeInTheDocument();
  });

  it("shows rich empty state when no rules exist", async () => {
    server.use(
      http.get("http://localhost/api/v1/deposit-rules", () => HttpResponse.json([])),
    );
    renderPage();
    expect(await screen.findByText("No deposit rules yet")).toBeInTheDocument();
  });

  it("renders a card for each rule returned by the API", async () => {
    renderPage();
    expect(await screen.findByText("Standard Deposit")).toBeInTheDocument();
    expect(screen.getByText("Large Pieces")).toBeInTheDocument();
  });

  it("shows the fixed amount and Active badge for an active fixed rule", async () => {
    renderPage();
    await screen.findByText("Standard Deposit");
    expect(screen.getByText(/fixed · 50,00\s?€/i)).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("shows the percent amount and Inactive badge for an inactive percent rule", async () => {
    renderPage();
    await screen.findByText("Large Pieces");
    expect(screen.getByText(/percent · 20%/i)).toBeInTheDocument();
    expect(screen.getByText("Inactive")).toBeInTheDocument();
  });

  it("shows '2 rules' count in the header", async () => {
    renderPage();
    expect(await screen.findByText("2 rules")).toBeInTheDocument();
  });

  it("uses singular 'rule' in the count when only one rule exists", async () => {
    server.use(
      http.get("http://localhost/api/v1/deposit-rules", () => HttpResponse.json([RULE_FIXED])),
    );
    renderPage();
    expect(await screen.findByText("1 rule")).toBeInTheDocument();
  });

  it("'New Rule' button is visible for the Owner role", async () => {
    renderPage(Role.Owner);
    await screen.findByText("Standard Deposit");
    expect(screen.getByRole("button", { name: /new rule/i })).toBeInTheDocument();
  });

  it("'New Rule' button is NOT visible for the Artist role", async () => {
    renderPage(Role.Artist);
    await screen.findByText("Standard Deposit");
    expect(screen.queryByRole("button", { name: /new rule/i })).not.toBeInTheDocument();
  });

  it("'New Rule' button navigates to /deposit-rules/new", async () => {
    const user = userEvent.setup();
    renderPage(Role.Owner);
    await screen.findByText("Standard Deposit");
    await user.click(screen.getByRole("button", { name: /new rule/i }));
    expect(screen.getByTestId("create-page")).toBeInTheDocument();
  });

  it("clicking a rule card navigates to the detail page", async () => {
    const user = userEvent.setup();
    renderPage(Role.Owner);
    const link = await screen.findByRole("link", { name: /standard deposit/i });
    await user.click(link);
    expect(screen.getByTestId("detail-page")).toBeInTheDocument();
  });
});
