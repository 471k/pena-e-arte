import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { OwnerExceptions, VISIBLE_PER_GROUP } from "@/features/dashboard/components/OwnerExceptions";
import {
  OwnerExceptionKind,
  type OwnerExceptionGroup,
  type OwnerExceptionItem,
  type OwnerExceptionsResponse,
} from "@/features/dashboard/dashboard.types";
import type { PaymentResponse } from "@/features/payments/payment.types";

// E05a (O01): the "Needs you" panel. Every group in loading / error / empty / populated, the empty
// panel as a designed state, the real counts, and the §6 disclosure rules.

// 13:00 UTC is 14:00 in Tirana in November (CET): the studio's timezone, not the test machine's.
const WHEN = "2026-11-12T13:00:00Z";

function item(overrides: Partial<OwnerExceptionItem> = {}): OwnerExceptionItem {
  return {
    appointmentId: "appt-1", appointmentDate: WHEN, clientName: "Mira Hoxha", artistName: "Ana Costa",
    appointmentStatus: "Pending", readiness: null, deposit: null, ...overrides,
  };
}

function group(kind: OwnerExceptionKind, items: OwnerExceptionItem[], total = items.length): OwnerExceptionGroup {
  return { kind, total, items };
}

function response(groups: OwnerExceptionGroup[]): OwnerExceptionsResponse {
  return { generatedAt: "2026-11-10T10:00:00Z", windowDays: 7, maxItemsPerGroup: 10, groups };
}

const CARD_PENDING: PaymentResponse = {
  id: "pay-1", appointmentId: "appt-1", amount: 50, status: "Pending", method: "Card",
  providerReferenceId: null, clientToken: null, cashNote: null, paidAt: null,
  clientName: "Mira Hoxha", appointmentDate: WHEN, currency: "EUR",
};
const CASH_PENDING: PaymentResponse = { ...CARD_PENDING, id: "pay-2", status: "CashPending", method: "Cash" };

let body: OwnerExceptionsResponse = response([]);
let status = 200;

const server = setupServer(
  http.get("http://localhost/api/v1/dashboard/exceptions", () =>
    status === 200 ? HttpResponse.json(body) : HttpResponse.json({ title: "boom" }, { status }),
  ),
  http.get("http://localhost/api/v1/studios/me", () =>
    HttpResponse.json({ id: "stud-0001", timezone: "Europe/Tirane", currency: "EUR", countryCode: "PT" }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); body = response([]); status = 200; });
afterAll(() => server.close());

function renderPanel() {
  const store = configureStore({
    reducer: {
      auth: authReducer,
      ui:   uiReducer,
      [appointmentsApi.reducerPath]: appointmentsApi.reducer,
      [paymentsApi.reducerPath]:     paymentsApi.reducer,
      [studiosApi.reducerPath]:      studiosApi.reducer,
    },
    middleware: (gd) => gd().concat(appointmentsApi.middleware, paymentsApi.middleware, studiosApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "owner@ink.test" }, token: "t", tenantId: "stud-0001", role: "owner", pendingReferralCode: null, impersonation: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null, impersonationScopeError: null, impersonationSessionExpired: false },
    },
  });
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/dashboard"]}>
        <Routes>
          <Route path="/dashboard" element={<OwnerExceptions />} />
          <Route path="/appointments/:id" element={<div data-testid="appointment-page" />} />
          <Route path="/schedule" element={<div data-testid="schedule-page" />} />
          <Route path="/payments" element={<div data-testid="payments-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

describe("OwnerExceptions", () => {
  // ── loading / error / empty ───────────────────────────────────────────────

  it("shows a loading state, not an empty one, while the read is in flight", () => {
    renderPanel();
    expect(screen.getByTestId("exceptions-loading")).toBeInTheDocument();
    expect(screen.queryByText("Nothing needs you right now")).not.toBeInTheDocument();
  });

  it("says it couldn't load, rather than 'nothing needs you', when the read fails", async () => {
    status = 500;
    renderPanel();
    expect(await screen.findByRole("alert")).toHaveTextContent("We couldn't load what needs you.");
    expect(screen.getByRole("button", { name: /try again/i })).toBeInTheDocument();
    expect(screen.queryByText("Nothing needs you right now")).not.toBeInTheDocument();
  });

  it("treats a response without a groups list as a failed read, never as 'nothing needs you'", async () => {
    server.use(http.get("http://localhost/api/v1/dashboard/exceptions", () => HttpResponse.json([])));
    renderPanel();
    expect(await screen.findByRole("alert")).toHaveTextContent("We couldn't load what needs you.");
    expect(screen.queryByText("Nothing needs you right now")).not.toBeInTheDocument();
  });

  it("renders nothing to act on as a designed good state", async () => {
    renderPanel();
    const empty = await screen.findByTestId("exceptions-empty");
    expect(within(empty).getByText("Nothing needs you right now")).toBeInTheDocument();
    expect(within(empty).getByText(/will show up here/)).toBeInTheDocument();
    expect(screen.queryByRole("heading", { level: 3 })).not.toBeInTheDocument();
  });

  // ── each group, populated ─────────────────────────────────────────────────

  it("Unassigned: names the request and links to the appointment to assign it", async () => {
    const user = userEvent.setup();
    body = response([group("Unassigned", [item({ artistName: null })])]);
    renderPanel();
    const g = await screen.findByTestId("exception-group-Unassigned");
    expect(within(g).getByRole("heading", { name: "Requests with no artist (1)" })).toBeInTheDocument();
    expect(within(g).getByText("No artist assigned")).toBeInTheDocument();
    await user.click(within(g).getByRole("link", { name: "Assign: Mira Hoxha's booking" }));
    expect(screen.getByTestId("appointment-page")).toBeInTheDocument();
  });

  it("AwaitingConfirmation: shows the artist and offers Review", async () => {
    body = response([group("AwaitingConfirmation", [item()])]);
    renderPanel();
    const g = await screen.findByTestId("exception-group-AwaitingConfirmation");
    expect(within(g).getByText("Ana Costa")).toBeInTheDocument();
    expect(within(g).getByRole("link", { name: "Review: Mira Hoxha's booking" })).toHaveAttribute("href", "/appointments/appt-1");
  });

  it("CashToConfirm: describes the promise the way every deposit is described and offers the confirm button", async () => {
    body = response([group("CashToConfirm", [item({ deposit: { amount: 50, currency: "EUR", payment: CASH_PENDING } })])]);
    renderPanel();
    const g = await screen.findByTestId("exception-group-CashToConfirm");
    expect(within(g).getByText(/to collect in cash/)).toBeInTheDocument();
    expect(within(g).getByRole("button", { name: "Mark cash received" })).toBeInTheDocument();
  });

  // generatedAt is 2026-11-10T10:00:00Z (the server's clock), so these are 12 and 2 days before it.
  it("CashToConfirm: a promise a week or more past its session shows its age in the warning token", async () => {
    body = response([group("CashToConfirm", [
      item({ appointmentId: "old", appointmentDate: "2026-10-29T10:00:00Z", deposit: { amount: 50, currency: "EUR", payment: CASH_PENDING } }),
      item({ appointmentId: "new", clientName: "Arben Kola", appointmentDate: "2026-11-08T09:00:00Z", deposit: { amount: 50, currency: "EUR", payment: CASH_PENDING } }),
    ])]);
    renderPanel();
    const g = await screen.findByTestId("exception-group-CashToConfirm");
    const [old, recent] = within(g).getAllByTestId("cash-promise-age");
    expect(old).toHaveTextContent("Session was 12 days ago");
    expect(old).toHaveClass("text-warning-text");
    expect(old).toHaveAttribute("data-aged", "true");
    expect(recent).toHaveTextContent("Session was 2 days ago");
    expect(recent).not.toHaveClass("text-warning-text");
    expect(within(g).getByTestId("cash-aged-note")).toHaveTextContent("1 still unconfirmed a week or more after the session");
    expect(within(g).getByTestId("cash-aged-note")).toHaveClass("text-warning-text");
  });

  it("CashToConfirm: a promise for a session that hasn't happened yet shows no age and no warning", async () => {
    body = response([group("CashToConfirm", [item({ deposit: { amount: 50, currency: "EUR", payment: CASH_PENDING } })])]);
    renderPanel();
    const g = await screen.findByTestId("exception-group-CashToConfirm");
    expect(within(g).queryByTestId("cash-promise-age")).not.toBeInTheDocument();
    expect(within(g).queryByTestId("cash-aged-note")).not.toBeInTheDocument();
  });

  it("CashToConfirm: says 'at least' when every listed promise is aged and the server holds more", async () => {
    const items = Array.from({ length: 10 }, (_, i) =>
      item({ appointmentId: `c${i}`, appointmentDate: "2026-10-01T10:00:00Z", deposit: { amount: 50, currency: "EUR", payment: CASH_PENDING } }));
    body = response([group("CashToConfirm", items, 14)]);
    renderPanel();
    const g = await screen.findByTestId("exception-group-CashToConfirm");
    expect(within(g).getByTestId("cash-aged-note")).toHaveTextContent("At least 10 still unconfirmed");
  });

  it("UnpaidDeposit: a started payment uses describeDeposit; a not-started one says no payment exists", async () => {
    body = response([group("UnpaidDeposit", [
      item({ appointmentId: "a", deposit: { amount: 50, currency: "EUR", payment: CARD_PENDING } }),
      item({ appointmentId: "b", clientName: "Arben Kola", deposit: { amount: 50, currency: "EUR", payment: null } }),
    ])]);
    renderPanel();
    const g = await screen.findByTestId("exception-group-UnpaidDeposit");
    expect(within(g).getByRole("heading", { name: "Deposits not paid (next 7 days) (2)" })).toBeInTheDocument();
    expect(within(g).getByText(/Waiting for the client to pay/)).toBeInTheDocument();
    expect(within(g).getByText(/No payment started yet/)).toBeInTheDocument();
  });

  it("Preparation: shows how much of the preparation is done", async () => {
    body = response([group("Preparation", [item({ readiness: { required: 3, satisfied: 1 } })])]);
    renderPanel();
    const g = await screen.findByTestId("exception-group-Preparation");
    expect(within(g).getByText("Preparation: 1 of 3 done")).toBeInTheDocument();
  });

  it("shows appointment times in the studio's timezone", async () => {
    body = response([group("AwaitingConfirmation", [item()])]);
    renderPanel();
    expect(await screen.findByText(/14:00/)).toBeInTheDocument();
  });

  it("keeps the server's group order", async () => {
    body = response([
      group("CashToConfirm", [item({ appointmentId: "c", deposit: { amount: 50, currency: "EUR", payment: CASH_PENDING } })]),
      group("Unassigned", [item({ appointmentId: "u", artistName: null })]),
    ]);
    renderPanel();
    await screen.findByTestId("exception-group-CashToConfirm");
    expect(screen.getAllByRole("heading", { level: 3 }).map((h) => h.textContent)).toEqual([
      "Cash to confirm (1)", "Requests with no artist (1)",
    ]);
  });

  // ── counts and progressive disclosure (§6) ────────────────────────────────

  it("shows the server's total, and says how many more exist when it sent fewer", async () => {
    const items = Array.from({ length: 10 }, (_, i) => item({ appointmentId: `u${i}`, artistName: null }));
    body = response([group("Unassigned", items, 23)]);
    renderPanel();
    const g = await screen.findByTestId("exception-group-Unassigned");
    expect(within(g).getByRole("heading", { name: "Requests with no artist (23)" })).toBeInTheDocument();
    expect(within(g).getByText(/13 more not listed here/)).toBeInTheDocument();
    expect(within(g).getByRole("link", { name: "the schedule" })).toHaveAttribute("href", "/schedule");
  });

  it("shows the most urgent items and puts the rest behind a real, keyboard-operable disclosure", async () => {
    const user = userEvent.setup();
    const items = Array.from({ length: 5 }, (_, i) =>
      item({ appointmentId: `p${i}`, clientName: `Client ${i}` }));
    body = response([group("AwaitingConfirmation", items)]);
    renderPanel();
    const g = await screen.findByTestId("exception-group-AwaitingConfirmation");

    expect(within(g).getAllByRole("link", { name: /^Review:/ })).toHaveLength(VISIBLE_PER_GROUP);
    const toggle = within(g).getByRole("button", { name: "Show 2 more" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    const region = document.getElementById(toggle.getAttribute("aria-controls") ?? "");
    expect(region).not.toBeNull();
    expect(region).toHaveAttribute("hidden");
    // Collapsed contents are not reachable by Tab.
    await user.tab();
    let tabbed = 0;
    while (document.activeElement !== toggle && tabbed < 10) { await user.tab(); tabbed += 1; }
    expect(document.activeElement).toBe(toggle);
    await user.tab();
    expect(region?.contains(document.activeElement)).toBe(false);

    toggle.focus();
    await user.keyboard("{Enter}");
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    expect(region).not.toHaveAttribute("hidden");
    expect(within(g).getAllByRole("link", { name: /^Review:/ })).toHaveLength(5);
  });

  it("has no hover-only action: every item's action is a visible link or button", async () => {
    body = response([
      group("Unassigned", [item({ appointmentId: "u", artistName: null })]),
      group("CashToConfirm", [item({ appointmentId: "c", deposit: { amount: 50, currency: "EUR", payment: CASH_PENDING } })]),
    ]);
    renderPanel();
    await screen.findByTestId("exception-group-Unassigned");
    for (const li of screen.getAllByRole("listitem")) {
      expect(within(li).queryAllByRole("link").length + within(li).queryAllByRole("button").length).toBeGreaterThan(0);
    }
  });

  it("never truncates a long client name silently: it wraps", async () => {
    const long = "Maria Antonieta de las Nieves Hoxhallari-Kolaveri e Gjirokastrës";
    body = response([group("AwaitingConfirmation", [item({ clientName: long })])]);
    renderPanel();
    const name = await screen.findByText(long);
    expect(name.closest("p")).toHaveClass("break-words");
    expect(name.closest("p")).not.toHaveClass("truncate");
  });
});
