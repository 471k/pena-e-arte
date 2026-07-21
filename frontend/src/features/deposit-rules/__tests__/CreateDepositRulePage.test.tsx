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
import { CreateDepositRulePage } from "@/features/deposit-rules/components/CreateDepositRulePage";
import type { DepositRuleResponse } from "@/features/deposit-rules/depositRule.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const CREATED_RULE: DepositRuleResponse = {
  id:                        "dr-new",
  studioId:                  "s-001",
  name:                      "Standard Deposit",
  amountFixed:               50,
  amountPercent:             null,
  isActive:                  true,
  createdAt:                 "2024-06-01T10:00:00Z",
  updatedAt:                 "2024-06-01T10:00:00Z",
  cancellationWindowHours:   null,
  refundPercentOnLateCancel: 0,
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.post("http://localhost/api/v1/deposit-rules", () =>
    HttpResponse.json(CREATED_RULE, { status: 201 }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                          authReducer,
      ui:                            uiReducer,
      [depositRulesApi.reducerPath]: depositRulesApi.reducer,
    },
    middleware: (gd) => gd().concat(depositRulesApi.middleware),
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
      <MemoryRouter initialEntries={["/deposit-rules/new"]}>
        <Routes>
          <Route path="/deposit-rules/new" element={<CreateDepositRulePage />} />
          <Route path="/deposit-rules"     element={<div data-testid="list-page" />} />
          <Route path="/deposit-rules/:id" element={<div data-testid="detail-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("CreateDepositRulePage", () => {

  it("renders the 'New Deposit Rule' heading", () => {
    renderPage();
    expect(screen.getByText("New Deposit Rule")).toBeInTheDocument();
  });

  it("renders Rule name, Deposit type, Amount and Active fields", () => {
    renderPage();
    expect(screen.getByLabelText("Rule name")).toBeInTheDocument();
    expect(screen.getByText("Fixed amount")).toBeInTheDocument();
    expect(screen.getByText("Percentage")).toBeInTheDocument();
    expect(screen.getByLabelText("Amount (€)")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("renders the cancellation window and late-refund fields with defaults", () => {
    renderPage();
    const window = screen.getByLabelText(/cancellation notice window/i);
    const refund = screen.getByLabelText(/refund if cancelled late/i);
    expect(window).toHaveValue(null);
    expect(refund).toHaveValue(0);
  });

  it("defaults to fixed amount with the Amount (€) label", () => {
    renderPage();
    expect(screen.getByLabelText("Amount (€)")).toBeInTheDocument();
  });

  it("switching to Percentage updates the amount label", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole("radio", { name: /percentage/i }));
    expect(screen.getByLabelText("Percentage (%)")).toBeInTheDocument();
  });

  it("shows validation errors when submitting an empty form", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole("button", { name: /create rule/i }));
    expect(await screen.findByText("Name is required")).toBeInTheDocument();
    expect(screen.getByText("Amount is required")).toBeInTheDocument();
  });

  it("rejects a percentage amount above 100", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText("Rule name"), "Big Deposit");
    await user.click(screen.getByRole("radio", { name: /percentage/i }));
    await user.type(screen.getByLabelText("Percentage (%)"), "150");
    await user.click(screen.getByRole("button", { name: /create rule/i }));
    expect(await screen.findByText("Must be between 0.01 and 100")).toBeInTheDocument();
  });

  it("submits the cancellation policy fields along with the rule", async () => {
    let capturedBody: Record<string, unknown> | undefined;
    server.use(
      http.post("http://localhost/api/v1/deposit-rules", async ({ request }) => {
        capturedBody = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(CREATED_RULE, { status: 201 });
      }),
    );
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText("Rule name"), "Standard Deposit");
    await user.type(screen.getByLabelText("Amount (€)"), "50");
    await user.type(screen.getByLabelText(/cancellation notice window/i), "48");
    await user.clear(screen.getByLabelText(/refund if cancelled late/i));
    await user.type(screen.getByLabelText(/refund if cancelled late/i), "50");
    await user.click(screen.getByRole("button", { name: /create rule/i }));
    await screen.findByTestId("detail-page");
    expect(capturedBody?.cancellationWindowHours).toBe(48);
    expect(capturedBody?.refundPercentOnLateCancel).toBe(50);
  });

  it("creates a fixed-amount rule and navigates to its detail page", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText("Rule name"), "Standard Deposit");
    await user.type(screen.getByLabelText("Amount (€)"), "50");
    await user.click(screen.getByRole("button", { name: /create rule/i }));
    expect(await screen.findByTestId("detail-page")).toBeInTheDocument();
  });

  it("shows a success toast after creating a rule", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText("Rule name"), "Standard Deposit");
    await user.type(screen.getByLabelText("Amount (€)"), "50");
    await user.click(screen.getByRole("button", { name: /create rule/i }));
    expect(await screen.findByText("Deposit rule created.")).toBeInTheDocument();
  });

  it("shows an error toast and stays on the page when the API call fails", async () => {
    server.use(
      http.post("http://localhost/api/v1/deposit-rules", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText("Rule name"), "Standard Deposit");
    await user.type(screen.getByLabelText("Amount (€)"), "50");
    await user.click(screen.getByRole("button", { name: /create rule/i }));
    expect(await screen.findByText("Failed to create deposit rule.")).toBeInTheDocument();
    expect(screen.queryByTestId("detail-page")).not.toBeInTheDocument();
  });

  it("disables the back button and shows 'Creating…' while the mutation is in-flight", async () => {
    let resolvePost!: (v: Response) => void;
    server.use(
      http.post("http://localhost/api/v1/deposit-rules", () =>
        new Promise<Response>((r) => { resolvePost = r; }),
      ),
    );

    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText("Rule name"), "Standard Deposit");
    await user.type(screen.getByLabelText("Amount (€)"), "50");
    await user.click(screen.getByRole("button", { name: /create rule/i }));

    expect(await screen.findByText(/creating…/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /deposit rules/i })).toBeDisabled();

    resolvePost(HttpResponse.json(CREATED_RULE, { status: 201 }) as unknown as Response);
  });

  it("'Deposit Rules' back button navigates to /deposit-rules", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole("button", { name: /deposit rules/i }));
    expect(screen.getByTestId("list-page")).toBeInTheDocument();
  });
});
