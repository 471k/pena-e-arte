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
import { depositRulesApi } from "@/features/deposit-rules/depositRulesApi";
import { DepositRuleDetailPage } from "@/features/deposit-rules/components/DepositRuleDetailPage";
import type { DepositRuleResponse } from "@/features/deposit-rules/depositRule.types";
import { Role } from "@/shared/types/roles";

// ── Seed data ──────────────────────────────────────────────────────────────────

const RULE_ID = "dr-001";

const RULE: DepositRuleResponse = {
  id:                        RULE_ID,
  studioId:                  "s-001",
  name:                      "Standard Deposit",
  amountFixed:               50,
  amountPercent:             null,
  isActive:                  true,
  createdAt:                 "2024-01-15T10:00:00Z",
  updatedAt:                 "2024-01-15T10:00:00Z",
  cancellationWindowHours:   null,
  refundPercentOnLateCancel: 0,
};

const UPDATED_RULE: DepositRuleResponse = {
  ...RULE,
  name:          "Updated Deposit",
  amountFixed:   75,
  updatedAt:     "2024-03-01T10:00:00Z",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/deposit-rules/:id", () => HttpResponse.json(RULE)),
  http.put("http://localhost/api/v1/deposit-rules/:id", () => HttpResponse.json(UPDATED_RULE)),
  http.delete("http://localhost/api/v1/deposit-rules/:id", () => new HttpResponse(null, { status: 204 })),
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
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null },
    },
  });
}

function renderPage(role: Role = Role.Owner, ruleId = RULE_ID) {
  render(
    <Provider store={makeStore(role)}>
      <Toaster />
      <MemoryRouter initialEntries={[`/deposit-rules/${ruleId}`]}>
        <Routes>
          <Route path="/deposit-rules/:id" element={<DepositRuleDetailPage />} />
          <Route path="/deposit-rules"     element={<div data-testid="list-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("DepositRuleDetailPage", () => {

  // ── Loading / error ────────────────────────────────────────────────────────

  it("shows a loading skeleton while the rule is fetching", () => {
    renderPage();
    expect(screen.getByLabelText(/loading deposit rule/i)).toBeInTheDocument();
  });

  it("shows a not-found message when the rule fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/deposit-rules/:id", () =>
        HttpResponse.json({ message: "error" }, { status: 404 }),
      ),
    );
    renderPage();
    expect(await screen.findByText("Deposit rule not found.")).toBeInTheDocument();
  });

  // ── View mode ─────────────────────────────────────────────────────────────

  it("renders the rule name and Active badge", async () => {
    renderPage();
    expect(await screen.findByText("Standard Deposit")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("renders the fixed amount", async () => {
    renderPage();
    await screen.findByText("Standard Deposit");
    expect(screen.getByText(/fixed · 50,00\s?€/i)).toBeInTheDocument();
  });

  it("renders a percent rule correctly", async () => {
    server.use(
      http.get("http://localhost/api/v1/deposit-rules/:id", () =>
        HttpResponse.json({ ...RULE, amountFixed: null, amountPercent: 20, isActive: false }),
      ),
    );
    renderPage();
    await screen.findByText("Standard Deposit");
    expect(screen.getByText(/percentage · 20%/i)).toBeInTheDocument();
    expect(screen.getByText("Inactive")).toBeInTheDocument();
  });

  it("shows the platform default cancellation window when null", async () => {
    renderPage();
    await screen.findByText("Standard Deposit");
    expect(screen.getByText(/cancellation notice: 24 hours \(platform default\)/i)).toBeInTheDocument();
    expect(screen.getByText(/late cancellation refund: 0%/i)).toBeInTheDocument();
  });

  it("shows a configured cancellation window without the default label", async () => {
    server.use(
      http.get("http://localhost/api/v1/deposit-rules/:id", () =>
        HttpResponse.json({ ...RULE, cancellationWindowHours: 48, refundPercentOnLateCancel: 50 }),
      ),
    );
    renderPage();
    await screen.findByText("Standard Deposit");
    expect(screen.getByText(/cancellation notice: 48 hours/i)).toBeInTheDocument();
    expect(screen.queryByText(/platform default/i)).not.toBeInTheDocument();
    expect(screen.getByText(/late cancellation refund: 50%/i)).toBeInTheDocument();
  });

  // ── Role-gated actions ───────────────────────────────────────────────────────

  it("Edit and Delete buttons are visible for the Owner role", async () => {
    renderPage(Role.Owner);
    await screen.findByText("Standard Deposit");
    expect(screen.getByRole("button", { name: /edit/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /delete/i })).toBeInTheDocument();
  });

  it("Edit and Delete buttons are NOT visible for the Artist role", async () => {
    renderPage(Role.Artist);
    await screen.findByText("Standard Deposit");
    expect(screen.queryByRole("button", { name: /edit/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /delete/i })).not.toBeInTheDocument();
  });

  // ── Edit flow ─────────────────────────────────────────────────────────────

  it("clicking Edit shows the edit form pre-filled with the rule's values", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /edit/i }));
    expect(screen.getByLabelText("Rule name")).toHaveValue("Standard Deposit");
    expect(screen.getByLabelText("Amount (€)")).toHaveValue(50);
    expect(screen.getByLabelText(/cancellation notice window/i)).toHaveValue(null);
    expect(screen.getByLabelText(/refund if cancelled late/i)).toHaveValue(0);
  });

  it("Cancel exits the edit form without saving", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /edit/i }));
    await user.click(screen.getByRole("button", { name: /cancel/i }));
    expect(screen.getByText("Standard Deposit")).toBeInTheDocument();
    expect(screen.queryByLabelText("Rule name")).not.toBeInTheDocument();
  });

  it("saving valid changes shows a success toast and returns to view mode", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /edit/i }));
    await user.clear(screen.getByLabelText("Rule name"));
    await user.type(screen.getByLabelText("Rule name"), "Updated Deposit");
    await user.click(screen.getByRole("button", { name: /save changes/i }));
    expect(await screen.findByText("Deposit rule updated.")).toBeInTheDocument();
    expect(screen.queryByLabelText("Rule name")).not.toBeInTheDocument();
  });

  it("shows an error toast and stays in edit mode when the update fails", async () => {
    server.use(
      http.put("http://localhost/api/v1/deposit-rules/:id", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /edit/i }));
    await user.click(screen.getByRole("button", { name: /save changes/i }));
    expect(await screen.findByText("Failed to update deposit rule.")).toBeInTheDocument();
    expect(screen.getByLabelText("Rule name")).toBeInTheDocument();
  });

  // ── Delete flow ──────────────────────────────────────────────────────────────

  it("clicking Delete shows a delete confirmation", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /delete/i }));
    expect(screen.getByText('Delete "Standard Deposit"?')).toBeInTheDocument();
    expect(screen.getByText(/this action cannot be undone/i)).toBeInTheDocument();
  });

  it("confirming delete navigates back to the list", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /delete/i }));
    await user.click(screen.getByRole("button", { name: /^delete$/i }));
    expect(await screen.findByTestId("list-page")).toBeInTheDocument();
  });

  it("Cancel on the delete confirmation returns to view mode", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /delete/i }));
    await user.click(screen.getByRole("button", { name: /cancel/i }));
    expect(screen.queryByText('Delete "Standard Deposit"?')).not.toBeInTheDocument();
    expect(screen.getByText("Standard Deposit")).toBeInTheDocument();
  });

  it("shows an error toast and stays on the confirmation when delete fails", async () => {
    server.use(
      http.delete("http://localhost/api/v1/deposit-rules/:id", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /delete/i }));
    await user.click(screen.getByRole("button", { name: /^delete$/i }));
    expect(await screen.findByText("Failed to delete deposit rule.")).toBeInTheDocument();
    expect(screen.getByText('Delete "Standard Deposit"?')).toBeInTheDocument();
  });

  // ── Back navigation ──────────────────────────────────────────────────────────

  it("'Deposit Rules' back button navigates to /deposit-rules", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Standard Deposit");
    await user.click(screen.getByRole("button", { name: /deposit rules/i }));
    expect(screen.getByTestId("list-page")).toBeInTheDocument();
  });
});
