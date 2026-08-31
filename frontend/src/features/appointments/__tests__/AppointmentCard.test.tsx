import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
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
import { AppointmentCard } from "@/features/appointments/components/AppointmentCard";

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
  durationMinutes: 60,
  status: "Pending",
  depositStatus: "Pending",
  depositAmount: 0,
  notes: null,
  createdAt: "2024-01-01T00:00:00Z",
};

const APPT_COMPLETED: AppointmentResponse = {
  ...APPT_PENDING,
  id: "appt-002",
  status: "Completed",
  depositStatus: "Paid",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/appointments/check-slot", () =>
    HttpResponse.json({ available: true, reason: null }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); vi.clearAllMocks(); });
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

function renderCard(appointment: AppointmentResponse, role: Role = Role.Artist) {
  render(
    <Provider store={makeStore(role)}>
      <MemoryRouter>
        <Routes>
          <Route path="/" element={<AppointmentCard appointment={appointment} />} />
          <Route path="/appointments/:id" element={<div data-testid="detail-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("AppointmentCard", () => {

  it("artist sees the Reschedule icon button for a non-terminal appointment", () => {
    renderCard(APPT_PENDING, Role.Artist);
    expect(screen.getByRole("button", { name: /reschedule appointment/i })).toBeInTheDocument();
  });

  it("artist does NOT see the Reschedule icon button for a Completed appointment", () => {
    renderCard(APPT_COMPLETED, Role.Artist);
    expect(screen.queryByRole("button", { name: /reschedule appointment/i })).not.toBeInTheDocument();
  });

  it("client role does NOT see the Reschedule icon button", () => {
    renderCard(APPT_PENDING, Role.Client);
    expect(screen.queryByRole("button", { name: /reschedule appointment/i })).not.toBeInTheDocument();
  });

  it("clicking the Reschedule icon button opens the dialog and does not navigate to the detail page", async () => {
    const user = userEvent.setup();
    renderCard(APPT_PENDING, Role.Artist);

    await user.click(screen.getByRole("button", { name: /reschedule appointment/i }));

    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.queryByTestId("detail-page")).not.toBeInTheDocument();
  });

  it("confirming a pending appointment shows a success toast", async () => {
    server.use(
      http.patch("http://localhost/api/v1/appointments/appt-001/confirm", () =>
        HttpResponse.json({ ...APPT_PENDING, status: "Confirmed" })),
    );
    const user = userEvent.setup();
    renderCard(APPT_PENDING, Role.Artist);

    await user.click(screen.getByRole("button", { name: /^confirm$/i }));

    await vi.waitFor(() => expect(toast.success).toHaveBeenCalledWith("Appointment confirmed."));
    expect(toast.error).not.toHaveBeenCalled();
  });

  it("a failed confirm shows an error toast, not a silent no-op", async () => {
    server.use(
      http.patch("http://localhost/api/v1/appointments/appt-001/confirm", () =>
        HttpResponse.json({ message: "Server error" }, { status: 500 })),
    );
    const user = userEvent.setup();
    renderCard(APPT_PENDING, Role.Artist);

    await user.click(screen.getByRole("button", { name: /^confirm$/i }));

    await vi.waitFor(() => expect(toast.error).toHaveBeenCalledWith("Failed to confirm appointment."));
    expect(toast.success).not.toHaveBeenCalled();
  });

  it("shows an image count badge when the appointment has attachments", () => {
    // Count now covers both attachment categories combined (Area + Reference), not just
    // "reference images" — imageUrls is the deprecated flat mirror, still supported as a
    // fallback for pre-migration appointments.
    renderCard({ ...APPT_PENDING, imageUrls: ["https://cdn.example.com/1.png", "https://cdn.example.com/2.png"] });
    expect(screen.getByText("2 images")).toBeInTheDocument();
  });

  it("uses singular 'image' for exactly one attachment", () => {
    renderCard({ ...APPT_PENDING, imageUrls: ["https://cdn.example.com/1.png"] });
    expect(screen.getByText("1 image")).toBeInTheDocument();
  });

  it("does NOT show the image count badge when there are no attachments", () => {
    renderCard(APPT_PENDING);
    expect(screen.queryByText(/\d+ images?/i)).not.toBeInTheDocument();
  });

  // ── Needs artist (studio-choice booking) ────────────────────────────────────

  it("shows the 'Needs artist' badge for a Pending appointment with no assigned artist", () => {
    renderCard({ ...APPT_PENDING, artistId: null });
    expect(screen.getByText(/needs artist/i)).toBeInTheDocument();
  });

  it("does NOT show the 'Needs artist' badge when an artist is assigned", () => {
    renderCard(APPT_PENDING);
    expect(screen.queryByText(/needs artist/i)).not.toBeInTheDocument();
  });

  it("does NOT show the 'Needs artist' badge for a non-Pending unassigned appointment", () => {
    renderCard({ ...APPT_COMPLETED, artistId: null });
    expect(screen.queryByText(/needs artist/i)).not.toBeInTheDocument();
  });

  it("does NOT show the Confirm button for a Pending appointment with no assigned artist", () => {
    renderCard({ ...APPT_PENDING, artistId: null }, Role.Artist);
    expect(screen.queryByRole("button", { name: /^confirm$/i })).not.toBeInTheDocument();
  });
});
