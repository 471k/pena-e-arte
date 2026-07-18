import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { Toaster } from "sonner";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { SessionSplitsEditor } from "@/features/payments/components/SessionSplitsEditor";
import type { SessionSplitResponse } from "@/features/payments/payment.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const SPLIT_1: SessionSplitResponse = {
  id:        "split-001",
  paymentId: "pay-001",
  label:     "Session 1",
  amount:    60,
  paidAt:    null,
};

const SPLIT_2: SessionSplitResponse = {
  id:        "split-002",
  paymentId: "pay-001",
  label:     "Session 2",
  amount:    40,
  paidAt:    "2026-06-14T10:00:00Z",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.put("http://localhost/api/v1/payments/:id/splits", ({ params }) =>
    HttpResponse.json({
      id:            params.id as string,
      appointmentId: "appt-001",
      amount:        100,
      status:        "Pending",
      method:        "Card",
      stripePaymentIntentId: null,
      clientSecret:  null,
      cashNote:      null,
      paidAt:        null,
      clientName:    "",
      appointmentDate: null,
    }),
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
      auth: { user: { id: "u-001", email: "owner@test.com" }, token: "fake-token", tenantId: "s-001", role: "owner", pendingReferralCode: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null },
    },
  });
}

function renderEditor(
  currentSplits: SessionSplitResponse[] = [],
  paymentId = "pay-001",
  paymentAmount = 100,
) {
  render(
    <Provider store={makeStore()}>
      <Toaster />
      <SessionSplitsEditor paymentId={paymentId} paymentAmount={paymentAmount} currentSplits={currentSplits} />
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("SessionSplitsEditor", () => {

  // ── View mode ────────────────────────────────────────────────────────────────

  it("shows 'No session splits defined.' when empty", () => {
    renderEditor([]);
    expect(screen.getByText("No session splits defined.")).toBeInTheDocument();
  });

  it("shows 'Add Splits' button when there are no existing splits", () => {
    renderEditor([]);
    expect(screen.getByRole("button", { name: /add splits/i })).toBeInTheDocument();
  });

  it("renders existing splits in view mode", () => {
    renderEditor([SPLIT_1, SPLIT_2]);
    expect(screen.getByText("Session 1")).toBeInTheDocument();
    expect(screen.getByText("Session 2")).toBeInTheDocument();
  });

  it("renders split amounts in view mode", () => {
    renderEditor([SPLIT_1, SPLIT_2]);
    expect(screen.getByText(/60/)).toBeInTheDocument();
    expect(screen.getByText(/40/)).toBeInTheDocument();
  });

  it("shows 'Paid' label for splits that have been paid", () => {
    renderEditor([SPLIT_1, SPLIT_2]);
    expect(screen.getByText("Paid")).toBeInTheDocument();
  });

  it("shows 'Edit' button when splits exist", () => {
    renderEditor([SPLIT_1]);
    expect(screen.getByRole("button", { name: /edit splits/i })).toBeInTheDocument();
  });

  // ── Edit mode — opening ──────────────────────────────────────────────────────

  it("clicking 'Add Splits' opens the editor with one blank row", async () => {
    const user = userEvent.setup();
    renderEditor([]);
    await user.click(screen.getByRole("button", { name: /add splits/i }));
    expect(screen.getByLabelText(/label/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /save splits/i })).toBeInTheDocument();
  });

  it("clicking 'Edit' opens the editor pre-populated with existing splits", async () => {
    const user = userEvent.setup();
    renderEditor([SPLIT_1, SPLIT_2]);
    await user.click(screen.getByRole("button", { name: /edit splits/i }));
    const labelInputs = screen.getAllByLabelText(/label/i);
    expect(labelInputs).toHaveLength(2);
    expect((labelInputs[0] as HTMLInputElement).value).toBe("Session 1");
    expect((labelInputs[1] as HTMLInputElement).value).toBe("Session 2");
  });

  // ── Edit mode — row manipulation ─────────────────────────────────────────────

  it("'Add Split' button adds a new row", async () => {
    const user = userEvent.setup();
    renderEditor([]);
    await user.click(screen.getByRole("button", { name: /add splits/i }));
    expect(screen.getAllByLabelText(/label/i)).toHaveLength(1);
    await user.click(screen.getByRole("button", { name: /^add split$/i }));
    expect(screen.getAllByLabelText(/label/i)).toHaveLength(2);
  });

  it("remove button deletes a row", async () => {
    const user = userEvent.setup();
    renderEditor([SPLIT_1, SPLIT_2]);
    await user.click(screen.getByRole("button", { name: /edit splits/i }));
    expect(screen.getAllByLabelText(/label/i)).toHaveLength(2);
    const removeBtns = screen.getAllByRole("button", { name: /remove split/i });
    await user.click(removeBtns[0]);
    expect(screen.getAllByLabelText(/label/i)).toHaveLength(1);
  });

  // ── Edit mode — Save ─────────────────────────────────────────────────────────

  it("Save button is disabled when no split has a label and amount", async () => {
    const user = userEvent.setup();
    renderEditor([], "pay-001", 80);
    await user.click(screen.getByRole("button", { name: /add splits/i }));
    expect(screen.getByRole("button", { name: /save splits/i })).toBeDisabled();
  });

  it("Save button stays disabled when the total doesn't match the payment amount", async () => {
    const user = userEvent.setup();
    renderEditor([], "pay-001", 100);
    await user.click(screen.getByRole("button", { name: /add splits/i }));
    await user.type(screen.getByLabelText(/label/i), "Outline");
    await user.type(screen.getByRole("spinbutton"), "80");
    expect(screen.getByRole("button", { name: /save splits/i })).toBeDisabled();
    expect(screen.getByRole("alert")).toHaveTextContent(/must add up to/i);
  });

  it("Save button enables after filling a label and amount that match the total", async () => {
    const user = userEvent.setup();
    renderEditor([], "pay-001", 80);
    await user.click(screen.getByRole("button", { name: /add splits/i }));
    await user.type(screen.getByLabelText(/label/i), "Outline");
    await user.type(screen.getByRole("spinbutton"), "80");
    expect(screen.getByRole("button", { name: /save splits/i })).not.toBeDisabled();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("saving calls updateSessionSplits with the correct payload", async () => {
    let capturedBody: unknown = null;
    server.use(
      http.put("http://localhost/api/v1/payments/:id/splits", async ({ request }) => {
        capturedBody = await request.json();
        return HttpResponse.json({
          id: "pay-001", appointmentId: "appt-001", amount: 100,
          status: "Pending", method: "Card",
          stripePaymentIntentId: null, clientSecret: null,
          cashNote: null, paidAt: null, clientName: "", appointmentDate: null,
        });
      }),
    );

    const user = userEvent.setup();
    renderEditor([], "pay-001", 80);
    await user.click(screen.getByRole("button", { name: /add splits/i }));
    await user.type(screen.getByLabelText(/label/i), "Outline");
    await user.type(screen.getByRole("spinbutton"), "80");
    await user.click(screen.getByRole("button", { name: /save splits/i }));

    await vi.waitFor(() => {
      expect(capturedBody).toEqual({ splits: [{ label: "Outline", amount: 80 }] });
    });
  });

  it("successful save closes the editor and returns to view mode", async () => {
    const user = userEvent.setup();
    renderEditor([], "pay-001", 80);
    await user.click(screen.getByRole("button", { name: /add splits/i }));
    await user.type(screen.getByLabelText(/label/i), "Outline");
    await user.type(screen.getByRole("spinbutton"), "80");
    await user.click(screen.getByRole("button", { name: /save splits/i }));
    expect(await screen.findByRole("button", { name: /add splits/i })).toBeInTheDocument();
  });

  it("save failure shows a toast error and keeps editor open (bug fix)", async () => {
    server.use(
      http.put("http://localhost/api/v1/payments/:id/splits", () =>
        HttpResponse.json({ message: "Payment is already finalised" }, { status: 422 }),
      ),
    );

    const user = userEvent.setup();
    renderEditor([], "pay-001", 80);
    await user.click(screen.getByRole("button", { name: /add splits/i }));
    await user.type(screen.getByLabelText(/label/i), "Outline");
    await user.type(screen.getByRole("spinbutton"), "80");
    await user.click(screen.getByRole("button", { name: /save splits/i }));

    // Toast error should appear
    expect(await screen.findByText(/failed to save splits/i)).toBeInTheDocument();
    // Editor should remain open (save splits button still visible)
    expect(screen.getByRole("button", { name: /save splits/i })).toBeInTheDocument();
  });

  // ── Edit mode — Cancel ───────────────────────────────────────────────────────

  it("Cancel discards changes and returns to view mode without calling the API", async () => {
    let apiCalled = false;
    server.use(
      http.put("http://localhost/api/v1/payments/:id/splits", () => {
        apiCalled = true;
        return HttpResponse.json({});
      }),
    );

    const user = userEvent.setup();
    renderEditor([SPLIT_1]);
    await user.click(screen.getByRole("button", { name: /edit splits/i }));
    await user.type(screen.getAllByLabelText(/label/i)[0], " modified");
    await user.click(screen.getByRole("button", { name: /cancel/i }));

    expect(screen.getByText("Session 1")).toBeInTheDocument();
    expect(apiCalled).toBe(false);
  });
});
