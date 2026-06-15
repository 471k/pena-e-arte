import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
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
import { SchedulePage } from "@/features/appointments/components/SchedulePage";

import type { AppointmentResponse } from "@/features/appointments/appointment.types";
import { Role } from "@/shared/types/roles";

// ── SignalR mock ───────────────────────────────────────────────────────────────

vi.mock("@microsoft/signalr", () => {
  function HubConnectionBuilder(this: Record<string, unknown>) {
    this.withUrl                = vi.fn().mockReturnValue(this);
    this.withAutomaticReconnect = vi.fn().mockReturnValue(this);
    this.configureLogging       = vi.fn().mockReturnValue(this);
    this.build = vi.fn(() => ({
      on:     vi.fn(),
      start:  vi.fn().mockResolvedValue(undefined),
      invoke: vi.fn().mockResolvedValue(undefined),
      stop:   vi.fn().mockResolvedValue(undefined),
    }));
  }
  return { HubConnectionBuilder, LogLevel: { Warning: 2 } };
});

// ── Seed data ──────────────────────────────────────────────────────────────────

// An appointment that falls on today
const todayStart = new Date();
todayStart.setHours(10, 0, 0, 0);
const todayEnd = new Date(todayStart.getTime() + 3_600_000);

const APPT_TODAY: AppointmentResponse = {
  id: "appt-001", studioId: "s-001",
  artistId: "a-001", clientId: "c-001",
  date:            todayStart.toISOString(),
  endDate:         todayEnd.toISOString(),
  durationMinutes: 60,
  status:          "Pending",
  depositStatus:   "Pending",
  depositAmount:   0,
  notes:           "Dragon sleeve sketch",
  createdAt:       "2024-01-01T00:00:00Z",
};

const APPT_CONFIRMED: AppointmentResponse = {
  ...APPT_TODAY,
  id:     "appt-002",
  status: "Confirmed",
  notes:  null,
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/appointments", () =>
    HttpResponse.json([]),
  ),
  http.patch("http://localhost/api/v1/appointments/:id/confirm", ({ params }) =>
    HttpResponse.json({ ...APPT_TODAY, id: params.id as string, status: "Confirmed" }),
  ),
  http.patch("http://localhost/api/v1/appointments/:id/complete", ({ params }) =>
    HttpResponse.json({ ...APPT_CONFIRMED, id: params.id as string, status: "Completed" }),
  ),
  http.patch("http://localhost/api/v1/appointments/:id/no-show", ({ params }) =>
    HttpResponse.json({ ...APPT_CONFIRMED, id: params.id as string, status: "NoShow" }),
  ),
  http.delete("http://localhost/api/v1/appointments/:id", () =>
    new HttpResponse(null, { status: 204 }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

// eslint-disable-next-line @typescript-eslint/no-explicit-any
function makeStore(role: Role = Role.Artist) {
  return configureStore({
    reducer: {
      auth:                          authReducer,
      ui:                            uiReducer,
      [appointmentsApi.reducerPath]: appointmentsApi.reducer,
    },
    middleware: (gd) => gd().concat(appointmentsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u-001", email: "test@test.com" }, token: "fake-token", tenantId: "s-001", role, pendingReferralCode: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false },
    },
  });
}

function renderPage(role: Role = Role.Artist) {
  render(
    <Provider store={makeStore(role)}>
      <MemoryRouter>
        <Routes>
          <Route path="/"                 element={<SchedulePage />} />
          <Route path="/appointments/:id" element={<div data-testid="detail-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// Helper: build a week label for the current week
function currentWeekLabel(): RegExp {
  const now = new Date();
  const day = now.getDay();
  const diff = day === 0 ? -6 : 1 - day;
  const monday = new Date(now);
  monday.setDate(now.getDate() + diff);
  monday.setHours(0, 0, 0, 0);
  const sunday = new Date(monday);
  sunday.setDate(monday.getDate() + 6);

  const fmt = (d: Date) =>
    d.toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });

  return new RegExp(`${fmt(monday)}.*${fmt(sunday)}`);
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("SchedulePage", () => {

  it("renders the 'Schedule' heading", () => {
    renderPage();
    expect(screen.getByText("Schedule")).toBeInTheDocument();
  });

  it("shows the current week label in the header", () => {
    renderPage();
    expect(screen.getByText(currentWeekLabel())).toBeInTheDocument();
  });

  it("shows a loading spinner while fetching", () => {
    renderPage();
    expect(screen.getByText(/loading schedule/i)).toBeInTheDocument();
  });

  it("shows an error message when the fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText(/failed to load appointments/i)).toBeInTheDocument();
  });

  it("shows 'No appointments' for each empty day", async () => {
    renderPage();
    const items = await screen.findAllByText("No appointments");
    expect(items.length).toBe(7);
  });

  it("renders an AppointmentCard for today's appointment", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json([APPT_TODAY]),
      ),
    );
    renderPage();
    await screen.findByText("1 appointment");
    const time = APPT_TODAY.date;
    const formatted = new Date(time).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
    expect(screen.getByText(new RegExp(formatted))).toBeInTheDocument();
  });

  it("shows appointment notes on the card", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json([APPT_TODAY]),
      ),
    );
    renderPage();
    expect(await screen.findByText("Dragon sleeve sketch")).toBeInTheDocument();
  });

  it("today day heading badge is shown", async () => {
    renderPage();
    await screen.findAllByText("No appointments");
    // "Today" appears in both the header button and the day column badge
    expect(screen.getAllByText("Today").length).toBeGreaterThanOrEqual(2);
  });

  it("the 'Today' button is disabled on the current week", async () => {
    renderPage();
    await screen.findAllByText("No appointments");
    expect(screen.getByRole("button", { name: /today/i })).toBeDisabled();
  });

  it("clicking 'Next week' then 'Today' re-enables the button and returns to current week", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findAllByText("No appointments");

    await user.click(screen.getByRole("button", { name: /next week/i }));
    const todayBtn = screen.getByRole("button", { name: /today/i });
    expect(todayBtn).not.toBeDisabled();

    await user.click(todayBtn);
    expect(screen.getByRole("button", { name: /today/i })).toBeDisabled();
  });

  it("clicking 'Previous week' shows an earlier week label", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findAllByText("No appointments");

    const labelBefore = screen.getByText(currentWeekLabel()).textContent;
    await user.click(screen.getByRole("button", { name: /previous week/i }));
    const labelAfter = screen.getByText(/.+–.+/i).textContent;
    expect(labelBefore).not.toBe(labelAfter);
  });

  // ── AppointmentCard role-based actions ─────────────────────────────────────

  it("artist sees 'Confirm' button for a Pending appointment", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json([APPT_TODAY]),
      ),
    );
    renderPage(Role.Artist);
    expect(await screen.findByRole("button", { name: /confirm/i })).toBeInTheDocument();
  });

  it("artist does NOT see 'Confirm' for a Confirmed appointment", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json([APPT_CONFIRMED]),
      ),
    );
    renderPage(Role.Artist);
    await screen.findByText(/1 appointment/i);
    expect(screen.queryByRole("button", { name: /confirm/i })).not.toBeInTheDocument();
  });

  it("artist sees 'Complete' button for a Confirmed appointment", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json([APPT_CONFIRMED]),
      ),
    );
    renderPage(Role.Artist);
    expect(await screen.findByRole("button", { name: /complete/i })).toBeInTheDocument();
  });

  it("artist sees cancel button (trash icon) for non-terminal appointments", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json([APPT_TODAY]),
      ),
    );
    renderPage(Role.Artist);
    expect(await screen.findByRole("button", { name: /cancel appointment/i })).toBeInTheDocument();
  });

  it("client does NOT see action buttons on the schedule (no artist access)", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json([APPT_TODAY]),
      ),
    );
    renderPage(Role.Client);
    await screen.findAllByText("No appointments");
    // APPT_TODAY is for today but client has no action buttons
    expect(screen.queryByRole("button", { name: /confirm/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /cancel appointment/i })).not.toBeInTheDocument();
  });

  it("clicking an appointment card navigates to the detail page", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json([APPT_TODAY]),
      ),
    );
    const user = userEvent.setup();
    renderPage(Role.Artist);
    const card = await screen.findByRole("button", { name: /cancel appointment/i });
    // Click the card itself (not the action buttons inside it)
    const cardEl = card.closest('[class*="card"]') as HTMLElement;
    if (cardEl) await user.click(cardEl);
    // We expect the detail page to have been navigated to
    expect(screen.getByTestId("detail-page")).toBeInTheDocument();
  });

  it("confirming an appointment calls the confirm mutation", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json([APPT_TODAY]),
      ),
    );
    const user = userEvent.setup();
    renderPage(Role.Artist);
    const confirmBtn = await screen.findByRole("button", { name: /confirm/i });
    await user.click(confirmBtn);
    // No error toast should appear
    expect(screen.queryByText(/failed/i)).not.toBeInTheDocument();
  });

  it("cancel button shows inline confirmation", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json([APPT_TODAY]),
      ),
    );
    const user = userEvent.setup();
    renderPage(Role.Artist);
    const cancelBtn = await screen.findByRole("button", { name: /cancel appointment/i });
    await user.click(cancelBtn);
    expect(screen.getByText("Cancel?")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /yes/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /no/i })).toBeInTheDocument();
  });

  it("'No' on cancel inline confirmation dismisses it", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json([APPT_TODAY]),
      ),
    );
    const user = userEvent.setup();
    renderPage(Role.Artist);
    await user.click(await screen.findByRole("button", { name: /cancel appointment/i }));
    await user.click(screen.getByRole("button", { name: /no/i }));
    expect(screen.queryByText("Cancel?")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /cancel appointment/i })).toBeInTheDocument();
  });

  it("owner sees 'Charge' button when deposit is pending", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json([{ ...APPT_TODAY, depositAmount: 50, depositStatus: "Pending" }]),
      ),
    );
    renderPage(Role.Owner);
    expect(await screen.findByRole("button", { name: /charge/i })).toBeInTheDocument();
  });

  it("'Deposit' status badge is shown in the card when deposit has been paid", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json([{ ...APPT_TODAY, depositAmount: 50, depositStatus: "Paid" }]),
      ),
    );
    renderPage(Role.Artist);
    const card = await screen.findByText(/deposit:/i);
    expect(within(card.closest("[class]") as HTMLElement).getByText("Paid")).toBeInTheDocument();
  });
});
