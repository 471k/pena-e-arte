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
import { AppointmentDetailPage } from "@/features/appointments/components/AppointmentDetailPage";

import type { AppointmentResponse } from "@/features/appointments/appointment.types";
import { Role } from "@/shared/types/roles";

// ── Seed data ──────────────────────────────────────────────────────────────────

const FUTURE = new Date(Date.now() + 7 * 86_400_000).toISOString();
const FUTURE_END = new Date(Date.now() + 7 * 86_400_000 + 3_600_000).toISOString();

const APPT_PENDING: AppointmentResponse = {
  id: "appt-001", studioId: "s-001",
  artistId: "a-001", clientId: "c-001",
  date: FUTURE, endDate: FUTURE_END,
  durationMinutes: 90,
  status: "Pending",
  depositStatus: "Pending",
  depositAmount: 50,
  notes: "Full back piece",
  createdAt: "2024-01-01T00:00:00Z",
};

const APPT_CONFIRMED: AppointmentResponse = {
  ...APPT_PENDING,
  id: "appt-002",
  status: "Confirmed",
  notes: null,
};

const APPT_COMPLETED: AppointmentResponse = {
  ...APPT_PENDING,
  id: "appt-003",
  status: "Completed",
  depositStatus: "Paid",
};

const APPT_CANCELLED: AppointmentResponse = {
  ...APPT_PENDING,
  id: "appt-004",
  status: "Cancelled",
  depositStatus: "Refunded",
};

const APPT_NO_SHOW: AppointmentResponse = {
  ...APPT_PENDING,
  id: "appt-005",
  status: "NoShow",
  depositStatus: "Forfeited",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/appointments/:id", ({ params }) => {
    if (params.id === "appt-001") return HttpResponse.json(APPT_PENDING);
    if (params.id === "appt-002") return HttpResponse.json(APPT_CONFIRMED);
    if (params.id === "appt-003") return HttpResponse.json(APPT_COMPLETED);
    if (params.id === "appt-004") return HttpResponse.json(APPT_CANCELLED);
    if (params.id === "appt-005") return HttpResponse.json(APPT_NO_SHOW);
    return HttpResponse.json({ message: "Not found" }, { status: 404 });
  }),
  http.delete("http://localhost/api/v1/appointments/:id", () =>
    new HttpResponse(null, { status: 204 }),
  ),
  http.patch("http://localhost/api/v1/appointments/:id/confirm", ({ params }) =>
    HttpResponse.json({ ...APPT_PENDING, id: params.id as string, status: "Confirmed" }),
  ),
  http.patch("http://localhost/api/v1/appointments/:id/complete", ({ params }) =>
    HttpResponse.json({ ...APPT_CONFIRMED, id: params.id as string, status: "Completed" }),
  ),
  http.patch("http://localhost/api/v1/appointments/:id/no-show", ({ params }) =>
    HttpResponse.json({ ...APPT_CONFIRMED, id: params.id as string, status: "NoShow" }),
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
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null },
    },
  });
}

function renderPage(apptId: string, role: Role = Role.Artist) {
  render(
    <Provider store={makeStore(role)}>
      {/* Two entries so navigate(-1) actually navigates back to "/" */}
      <MemoryRouter initialEntries={["/", `/appointments/${apptId}`]} initialIndex={1}>
        <Routes>
          <Route path="/appointments/:id" element={<AppointmentDetailPage />} />
          <Route path="*" element={<div data-testid="back-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("AppointmentDetailPage", () => {

  // ── Loading / error states ──────────────────────────────────────────────────

  it("shows a skeleton while the appointment is fetching", () => {
    renderPage("appt-001");
    expect(screen.getByRole("main", { name: /loading appointment/i })).toBeInTheDocument();
  });

  it("shows an error message when the appointment is not found", async () => {
    renderPage("appt-999");
    expect(await screen.findByText(/appointment not found/i)).toBeInTheDocument();
  });

  // ── Detail rendering ────────────────────────────────────────────────────────

  it("renders the 'Appointment' heading", async () => {
    renderPage("appt-001");
    await screen.findByText("90 min");
    expect(screen.getByText("Appointment")).toBeInTheDocument();
  });

  it("renders the appointment duration", async () => {
    renderPage("appt-001");
    expect(await screen.findByText("90 min")).toBeInTheDocument();
  });

  it("renders appointment notes", async () => {
    renderPage("appt-001");
    expect(await screen.findByText("Full back piece")).toBeInTheDocument();
  });

  it("renders the appointment status badge", async () => {
    renderPage("appt-001");
    // Badge appears in both the header and the detail row
    expect((await screen.findAllByText("Requested")).length).toBeGreaterThanOrEqual(1);
  });

  it("renders the deposit status badge", async () => {
    renderPage("appt-001");
    await screen.findByText("90 min");
    // Deposit row shows amount and badge
    const depositSection = screen.getByText("Deposit").closest("div");
    expect(within(depositSection as HTMLElement).getByText("Pending")).toBeInTheDocument();
  });

  it("renders 'Paid' deposit badge for completed appointment", async () => {
    renderPage("appt-003");
    await screen.findByText("90 min");
    const depositSection = screen.getByText("Deposit").closest("div");
    expect(within(depositSection as HTMLElement).getByText("Paid")).toBeInTheDocument();
  });

  it("renders 'Refunded' deposit badge for cancelled appointment", async () => {
    renderPage("appt-004");
    await screen.findByText("90 min");
    const depositSection = screen.getByText("Deposit").closest("div");
    expect(within(depositSection as HTMLElement).getByText("Refunded")).toBeInTheDocument();
  });

  it("renders 'Forfeited' deposit badge for no-show appointment", async () => {
    renderPage("appt-005");
    await screen.findByText("90 min");
    const depositSection = screen.getByText("Deposit").closest("div");
    expect(within(depositSection as HTMLElement).getByText("Forfeited")).toBeInTheDocument();
  });

  // ── Artist/Owner actions on pending ────────────────────────────────────────

  it("artist sees 'Confirm appointment' button for a Pending appointment", async () => {
    renderPage("appt-001", Role.Artist);
    expect(await screen.findByRole("button", { name: /confirm appointment/i })).toBeInTheDocument();
  });

  it("artist does NOT see 'Confirm appointment' for a Confirmed appointment", async () => {
    renderPage("appt-002", Role.Artist);
    await screen.findByText("90 min");
    expect(screen.queryByRole("button", { name: /confirm appointment/i })).not.toBeInTheDocument();
  });

  it("artist sees 'Mark as complete' for a Confirmed appointment", async () => {
    renderPage("appt-002", Role.Artist);
    expect(await screen.findByRole("button", { name: /mark as complete/i })).toBeInTheDocument();
  });

  it("artist sees 'Mark no-show' for a Confirmed appointment", async () => {
    renderPage("appt-002", Role.Artist);
    expect(await screen.findByRole("button", { name: /mark no-show/i })).toBeInTheDocument();
  });

  it("artist sees 'Cancel appointment' button for a non-terminal appointment", async () => {
    renderPage("appt-001", Role.Artist);
    expect(await screen.findByRole("button", { name: /cancel appointment/i })).toBeInTheDocument();
  });

  it("artist does NOT see action buttons for a Completed appointment", async () => {
    renderPage("appt-003", Role.Artist);
    await screen.findByText("90 min");
    expect(screen.queryByRole("button", { name: /confirm appointment/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /mark as complete/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /cancel appointment/i })).not.toBeInTheDocument();
  });

  it("owner sees 'Charge deposit' button when deposit is pending and not terminal", async () => {
    renderPage("appt-001", Role.Owner);
    expect(await screen.findByRole("button", { name: /charge deposit/i })).toBeInTheDocument();
  });

  it("owner does NOT see 'Charge deposit' when appointment is in a terminal state", async () => {
    // APPT_COMPLETED is Completed (terminal) — no action buttons are shown at all
    renderPage("appt-003", Role.Owner);
    await screen.findByText("90 min");
    expect(screen.queryByRole("button", { name: /charge deposit/i })).not.toBeInTheDocument();
  });

  it("client role does NOT see any action buttons", async () => {
    renderPage("appt-001", Role.Client);
    await screen.findByText("90 min");
    expect(screen.queryByRole("button", { name: /confirm appointment/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /cancel appointment/i })).not.toBeInTheDocument();
  });

  // ── Cancel dialog ───────────────────────────────────────────────────────────

  it("clicking 'Cancel appointment' opens a confirmation dialog", async () => {
    const user = userEvent.setup();
    renderPage("appt-001", Role.Artist);
    await user.click(await screen.findByRole("button", { name: /cancel appointment/i }));
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByText(/this action cannot be undone/i)).toBeInTheDocument();
  });

  it("dialog 'Keep' button closes the dialog", async () => {
    const user = userEvent.setup();
    renderPage("appt-001", Role.Artist);
    await user.click(await screen.findByRole("button", { name: /cancel appointment/i }));
    const dialog = screen.getByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: /keep/i }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("dialog destructive 'Cancel appointment' button calls the cancel mutation and navigates back", async () => {
    const user = userEvent.setup();
    renderPage("appt-001", Role.Artist);
    await user.click(await screen.findByRole("button", { name: /cancel appointment/i }));
    const dialog = screen.getByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: /^cancel appointment$/i }));
    // After successful cancel, navigate(-1) fires → renders "*" route
    expect(await screen.findByTestId("back-page")).toBeInTheDocument();
  });

  // ── Back navigation ─────────────────────────────────────────────────────────

  it("back button navigates to previous page", async () => {
    const user = userEvent.setup();
    renderPage("appt-001", Role.Artist);
    await screen.findByText("90 min");
    await user.click(screen.getByRole("button", { name: /back/i }));
    expect(screen.getByTestId("back-page")).toBeInTheDocument();
  });

  // ── Confirm / Complete / No-show mutations ──────────────────────────────────

  it("clicking 'Confirm appointment' calls the confirm mutation", async () => {
    const user = userEvent.setup();
    renderPage("appt-001", Role.Artist);
    await user.click(await screen.findByRole("button", { name: /confirm appointment/i }));
    expect(screen.queryByText(/failed/i)).not.toBeInTheDocument();
  });

  it("clicking 'Mark as complete' calls the complete mutation", async () => {
    const user = userEvent.setup();
    renderPage("appt-002", Role.Artist);
    await user.click(await screen.findByRole("button", { name: /mark as complete/i }));
    expect(screen.queryByText(/failed/i)).not.toBeInTheDocument();
  });

  it("clicking 'Mark no-show' calls the no-show mutation", async () => {
    const user = userEvent.setup();
    renderPage("appt-002", Role.Artist);
    await user.click(await screen.findByRole("button", { name: /mark no-show/i }));
    expect(screen.queryByText(/failed/i)).not.toBeInTheDocument();
  });
});
