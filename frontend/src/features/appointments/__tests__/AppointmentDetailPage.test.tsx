import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { toast } from "sonner";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import { remindersApi } from "@/features/reminders/remindersApi";
import { artistsApi } from "@/features/artists/artistsApi";
import { AppointmentDetailPage } from "@/features/appointments/components/AppointmentDetailPage";

import type { AppointmentResponse } from "@/features/appointments/appointment.types";
import { Role } from "@/shared/types/roles";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

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

const APPT_UNASSIGNED: AppointmentResponse = {
  ...APPT_PENDING,
  id: "appt-006",
  artistId: null,
  artistName: null,
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/appointments/:id", ({ params }) => {
    if (params.id === "appt-001") return HttpResponse.json(APPT_PENDING);
    if (params.id === "appt-002") return HttpResponse.json(APPT_CONFIRMED);
    if (params.id === "appt-003") return HttpResponse.json(APPT_COMPLETED);
    if (params.id === "appt-004") return HttpResponse.json(APPT_CANCELLED);
    if (params.id === "appt-005") return HttpResponse.json(APPT_NO_SHOW);
    if (params.id === "appt-006") return HttpResponse.json(APPT_UNASSIGNED);
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
  http.patch("http://localhost/api/v1/appointments/:id/reschedule", async ({ params, request }) => {
    const body = await request.json() as { newDate: string; newDurationMinutes: number };
    return HttpResponse.json({
      ...APPT_PENDING, id: params.id as string,
      date: body.newDate, durationMinutes: body.newDurationMinutes,
    });
  }),
  http.get("http://localhost/api/v1/appointments/check-slot", () =>
    HttpResponse.json({ available: true, reason: null }),
  ),
  http.get("http://localhost/api/v1/reminders", () => HttpResponse.json([])),
  http.get("http://localhost/api/v1/artists", () => HttpResponse.json([
    { id: "a-002", studioId: "s-001", firstName: "New", lastName: "Artist", slug: "new-artist", email: "new@a.com", specializations: null, hourlyRate: null, isActive: true },
  ])),
  http.patch("http://localhost/api/v1/appointments/:id/artist", async ({ params, request }) => {
    const body = await request.json() as { artistId: string };
    return HttpResponse.json({
      ...APPT_PENDING, id: params.id as string,
      artistId: body.artistId, artistName: "New Artist",
    });
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); vi.clearAllMocks(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

// Mirrors RescheduleDialog's own toDatetimeLocalValue — needed to assert against the rendered <input> value.
function toDatetimeLocalValueForTest(iso: string): string {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
function makeStore(role: Role = Role.Artist) {
  return configureStore({
    reducer: {
      auth:                          authReducer,
      ui:                            uiReducer,
      [appointmentsApi.reducerPath]: appointmentsApi.reducer,
      [remindersApi.reducerPath]:    remindersApi.reducer,
      [artistsApi.reducerPath]:      artistsApi.reducer,
    },
    middleware: (gd) => gd().concat(appointmentsApi.middleware, remindersApi.middleware, artistsApi.middleware),
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

  it("renders reference images as a thumbnail gallery", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments/appt-001", () =>
        HttpResponse.json({
          ...APPT_PENDING,
          imageUrls: ["https://cdn.example.com/1.png", "https://cdn.example.com/2.png"],
        })),
    );
    renderPage("appt-001");
    expect(await screen.findByText("Reference images")).toBeInTheDocument();
    const thumbnails = screen.getAllByAltText("Reference image");
    expect(thumbnails).toHaveLength(2);
    expect(thumbnails[0]).toHaveAttribute("src", "https://cdn.example.com/1.png");
    expect(thumbnails[0].closest("a")).toHaveAttribute("href", "https://cdn.example.com/1.png");
    expect(thumbnails[0].closest("a")).toHaveAttribute("target", "_blank");
  });

  it("does NOT render the reference images section when there are none", async () => {
    renderPage("appt-001");
    await screen.findByText("90 min");
    expect(screen.queryByText("Reference images")).not.toBeInTheDocument();
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

  // ── Reschedule ──────────────────────────────────────────────────────────────

  it("artist sees 'Reschedule' button for a non-terminal appointment", async () => {
    renderPage("appt-001", Role.Artist);
    expect(await screen.findByRole("button", { name: /^reschedule$/i })).toBeInTheDocument();
  });

  it("artist does NOT see 'Reschedule' for a Completed appointment", async () => {
    renderPage("appt-003", Role.Artist);
    await screen.findByText("90 min");
    expect(screen.queryByRole("button", { name: /^reschedule$/i })).not.toBeInTheDocument();
  });

  it("client role does NOT see 'Reschedule'", async () => {
    renderPage("appt-001", Role.Client);
    await screen.findByText("90 min");
    expect(screen.queryByRole("button", { name: /^reschedule$/i })).not.toBeInTheDocument();
  });

  it("clicking 'Reschedule' opens the reschedule dialog pre-filled with the current date and duration", async () => {
    const user = userEvent.setup();
    renderPage("appt-001", Role.Artist);
    await user.click(await screen.findByRole("button", { name: /^reschedule$/i }));

    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByText(/reschedule appointment/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/new date/i)).toHaveValue(toDatetimeLocalValueForTest(FUTURE));
  });

  it("dialog 'Cancel' button closes without calling the mutation", async () => {
    const user = userEvent.setup();
    renderPage("appt-001", Role.Artist);
    await user.click(await screen.findByRole("button", { name: /^reschedule$/i }));
    const dialog = screen.getByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: /^cancel$/i }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("submitting a new duration calls the reschedule mutation and closes the dialog", async () => {
    const user = userEvent.setup();
    renderPage("appt-001", Role.Artist);
    await user.click(await screen.findByRole("button", { name: /^reschedule$/i }));

    const durationTrigger = screen.getByLabelText(/duration/i);
    await user.click(durationTrigger);
    await user.click(await screen.findByRole("option", { name: /1 hour$/i }));

    await user.click(screen.getByRole("button", { name: /confirm reschedule/i }));

    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("confirming a pending appointment shows a success toast", async () => {
    const user = userEvent.setup();
    renderPage("appt-001", Role.Artist);

    await user.click(await screen.findByRole("button", { name: /confirm appointment/i }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Appointment confirmed."));
    expect(toast.error).not.toHaveBeenCalled();
  });

  it("a failed confirm shows an error toast, not a silent no-op", async () => {
    server.use(
      http.patch("http://localhost/api/v1/appointments/:id/confirm", () =>
        HttpResponse.json({ message: "Server error" }, { status: 500 })),
    );
    const user = userEvent.setup();
    renderPage("appt-001", Role.Artist);

    await user.click(await screen.findByRole("button", { name: /confirm appointment/i }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Failed to confirm appointment."));
    expect(toast.success).not.toHaveBeenCalled();
  });

  // ── Send Reminder ───────────────────────────────────────────────────────────

  it("artist sees 'Send Reminder' button for a non-terminal appointment", async () => {
    renderPage("appt-001", Role.Artist);
    expect(await screen.findByRole("button", { name: /send reminder/i })).toBeInTheDocument();
  });

  it("clicking 'Send Reminder' opens the reminder dialog scoped to this appointment", async () => {
    const user = userEvent.setup();
    renderPage("appt-001", Role.Artist);
    await user.click(await screen.findByRole("button", { name: /send reminder/i }));

    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByText(/send reminder/i)).toBeInTheDocument();
    // Appointment-linked mode never shows the raw-contact name/phone inputs.
    expect(within(dialog).queryByLabelText(/^name$/i)).not.toBeInTheDocument();
  });

  // ── Artist assignment ───────────────────────────────────────────────────────

  it("owner sees an editable Artist select and assigning one calls the mutation and shows a success toast", async () => {
    const user = userEvent.setup();
    renderPage("appt-006", Role.Owner);

    const trigger = await screen.findByLabelText(/assigned artist/i);
    await user.click(trigger);
    await user.click(await screen.findByRole("option", { name: /new artist/i }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Artist assigned."));
  });

  it("non-owner sees the artist's name as plain text, not a select", async () => {
    renderPage("appt-001", Role.Artist);
    await screen.findByText("90 min");
    expect(screen.queryByLabelText(/assigned artist/i)).not.toBeInTheDocument();
  });

  it("non-owner sees amber 'Unassigned' when no artist is assigned", async () => {
    renderPage("appt-006", Role.Artist);
    expect(await screen.findByText(/unassigned/i)).toBeInTheDocument();
  });

  it("Confirm button is replaced with a hint when the appointment has no artist assigned", async () => {
    renderPage("appt-006", Role.Owner);
    await screen.findByText("90 min");
    expect(screen.queryByRole("button", { name: /confirm appointment/i })).not.toBeInTheDocument();
    expect(screen.getByText(/assign an artist above before this can be confirmed/i)).toBeInTheDocument();
  });

  it("Send Reminder button is absent when the appointment has no artist assigned", async () => {
    renderPage("appt-006", Role.Artist);
    await screen.findByText("90 min");
    expect(screen.queryByRole("button", { name: /send reminder/i })).not.toBeInTheDocument();
  });
});
