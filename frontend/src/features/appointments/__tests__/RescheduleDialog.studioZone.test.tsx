// S01: the reschedule input runs on the studio's wall clock. A viewer in America/New_York moving an
// appointment at a studio in Europe/Paris picks Paris times, the minimum is Paris "now", and the saved
// instant is the Paris wall time, including across both daylight-saving changes.
import { describe, it, expect, vi, beforeAll, beforeEach, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { RescheduleDialog } from "@/features/appointments/components/RescheduleDialog";
import type { AppointmentResponse } from "@/features/appointments/appointment.types";
import { Role } from "@/shared/types/roles";

function appt(dateIso: string): AppointmentResponse {
  return {
    id: "appt-1", studioId: "s-001", artistId: "a-001", clientId: "c-001",
    date: dateIso, endDate: new Date(new Date(dateIso).getTime() + 3_600_000).toISOString(), durationMinutes: 60,
    status: "Confirmed", depositStatus: "Pending", depositAmount: 0, notes: null, createdAt: "2026-01-01T00:00:00Z",
  };
}

let saved: { newDate: string } | null = null;

const server = setupServer(
  http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json({ id: "s-001", timezone: "Europe/Paris" })),
  http.get("http://localhost/api/v1/appointments/:id/reschedule-preview", () => HttpResponse.json({
    appointmentId: "appt-1", canReschedule: true, block: null, message: null, noticeWindowHours: 48, noticeWindowApplies: false,
  })),
  http.get("http://localhost/api/v1/appointments/check-slot", () => HttpResponse.json({ available: true, reason: null })),
  http.patch("http://localhost/api/v1/appointments/:id/reschedule", async ({ request }) => {
    saved = (await request.json()) as { newDate: string };
    return HttpResponse.json(appt(saved.newDate));
  }),
);

// The viewer's zone is this process's zone (Node applies a TZ assignment immediately).
vi.stubEnv("TZ", "America/New_York");

beforeAll(() => {
  expect(Intl.DateTimeFormat().resolvedOptions().timeZone).toBe("America/New_York");
  server.listen({ onUnhandledRequest: "error" });
});
beforeEach(() => { saved = null; });
afterEach(() => { server.resetHandlers(); cleanup(); vi.useRealTimers(); });
afterAll(() => { server.close(); vi.unstubAllEnvs(); });

function at(iso: string) {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date(iso));
}

function renderDialog(appointment: AppointmentResponse) {
  const store = configureStore({
    reducer: {
      auth: authReducer, ui: uiReducer,
      [appointmentsApi.reducerPath]: appointmentsApi.reducer,
      [paymentsApi.reducerPath]: paymentsApi.reducer,
      [studiosApi.reducerPath]: studiosApi.reducer,
    },
    middleware: (gd) => gd().concat(appointmentsApi.middleware, paymentsApi.middleware, studiosApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u-1", email: "a@test.com" }, token: "t", tenantId: "s-001", role: Role.Artist, pendingReferralCode: null, impersonation: null } as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      ui: { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null } as any,
    },
  });
  render(<Provider store={store}><RescheduleDialog appointment={appointment} open onOpenChange={() => {}} /></Provider>);
}

const dateInput = async () => (await screen.findByLabelText(/new date/i)) as HTMLInputElement;
const confirm = () => screen.getByRole("button", { name: /confirm reschedule/i });

describe("RescheduleDialog on the studio's clock (S01)", () => {
  it("the minimum is Paris 'now', not a UTC string; the field starts at the appointment's Paris time", async () => {
    at("2026-11-15T09:00:00Z"); // 10:00 in Paris, 04:00 in New York
    renderDialog(appt("2026-11-20T13:00:00Z")); // 14:00 in Paris
    const input = await dateInput();
    await waitFor(() => expect(input.value).toBe("2026-11-20T14:00"));
    // The old min, new Date().toISOString().slice(0, 16), would be "2026-11-15T09:00": an hour of the
    // studio's past still allowed.
    expect(input.min).toBe("2026-11-15T10:00");
    expect(screen.getByText(/studio time, Europe\/Paris/)).toBeInTheDocument();
  });

  it("saves the Paris wall time as its instant, and tells a New York viewer what that is locally", async () => {
    at("2026-11-15T09:00:00Z");
    renderDialog(appt("2026-11-20T13:00:00Z"));
    const input = await dateInput();
    await waitFor(() => expect(input.value).toBe("2026-11-20T14:00"));
    fireEvent.change(input, { target: { value: "2026-11-20T15:00" } });
    // 15:00 Paris (CET) = 14:00 UTC = 09:00 New York (EST).
    expect(screen.getByText(/that.s fri 20 nov, 09:00 your time/i)).toBeInTheDocument();
    await userEvent.click(confirm());
    await waitFor(() => expect(saved?.newDate).toBe("2026-11-20T14:00:00.000Z"));
  });

  it("spring forward: a Paris time the clocks skip is refused and can't be saved", async () => {
    at("2026-03-01T12:00:00Z");
    renderDialog(appt("2026-03-30T10:00:00Z"));
    const input = await dateInput();
    await waitFor(() => expect(input.value).toBe("2026-03-30T12:00"));
    fireEvent.change(input, { target: { value: "2026-03-29T02:30" } });
    expect(screen.getByRole("alert")).toHaveTextContent(/doesn.t exist on that day/i);
    expect(confirm()).toBeDisabled();
  });

  it("fall back: a repeated Paris time says so and uses the first occurrence", async () => {
    at("2026-10-01T12:00:00Z");
    renderDialog(appt("2026-10-26T10:00:00Z"));
    const input = await dateInput();
    await waitFor(() => expect(input.value).toBe("2026-10-26T11:00"));
    fireEvent.change(input, { target: { value: "2026-10-25T02:30" } });
    expect(screen.getByText(/happens twice on that day/i)).toBeInTheDocument();
    await userEvent.click(confirm());
    // 02:30 CEST (UTC+2), the first of the two, is 00:30 UTC.
    await waitFor(() => expect(saved?.newDate).toBe("2026-10-25T00:30:00.000Z"));
  });

  it("if the studio's zone can't be loaded, no time can be picked and confirm stays disabled", async () => {
    at("2026-11-15T09:00:00Z");
    server.use(http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json({}, { status: 500 })));
    renderDialog(appt("2026-11-20T13:00:00Z"));
    expect(await screen.findByText(/couldn.t load the studio.s time zone/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/new date/i, { selector: "input" })).not.toBeInTheDocument();
    expect(confirm()).toBeDisabled();
  });
});
