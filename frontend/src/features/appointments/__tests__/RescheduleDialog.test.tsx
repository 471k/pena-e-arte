import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { RescheduleDialog } from "@/features/appointments/components/RescheduleDialog";
import type { AppointmentResponse, ReschedulePreviewResponse } from "@/features/appointments/appointment.types";
import { Role } from "@/shared/types/roles";

// E04b (A02): the dialog explains a move the save would refuse BEFORE a time is picked, using the
// server's own rule, and never lets the appointment block its own move.

const APPT: AppointmentResponse = {
  id: "appt-1", studioId: "s-001", artistId: "a-001", clientId: "c-001",
  date: "2030-12-10T09:00:00Z", endDate: "2030-12-10T11:00:00Z", durationMinutes: 120,
  status: "Confirmed", depositStatus: "Pending", depositAmount: 50,
  notes: null, createdAt: "2030-01-01T00:00:00Z",
};

function preview(overrides: Partial<ReschedulePreviewResponse> = {}): ReschedulePreviewResponse {
  return {
    appointmentId: "appt-1", canReschedule: true, block: null, message: null,
    noticeWindowHours: 48, noticeWindowApplies: false, ...overrides,
  };
}

let slotRequests: URL[] = [];
const server = setupServer(
  http.get("http://localhost/api/v1/appointments/:id/reschedule-preview", () => HttpResponse.json(preview())),
  http.get("http://localhost/api/v1/appointments/check-slot", ({ request }) => {
    slotRequests.push(new URL(request.url));
    return HttpResponse.json({ available: true, reason: null });
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); slotRequests = []; });
afterAll(() => server.close());

function renderDialog(appt: AppointmentResponse = APPT) {
  const store = configureStore({
    reducer: {
      auth: authReducer,
      ui: uiReducer,
      [appointmentsApi.reducerPath]: appointmentsApi.reducer,
      [paymentsApi.reducerPath]: paymentsApi.reducer,
    },
    middleware: (gd) => gd().concat(appointmentsApi.middleware).concat(paymentsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u-1", email: "a@test.com" }, token: "t", tenantId: "s-001", role: Role.Artist, pendingReferralCode: null, impersonation: null } as any,
      ui: { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null, impersonationScopeError: null, impersonationSessionExpired: false },
    },
  });
  render(
    <Provider store={store}>
      <RescheduleDialog appointment={appt} open onOpenChange={() => {}} />
    </Provider>,
  );
}

const confirm = () => screen.getByRole("button", { name: /confirm reschedule/i });

describe("RescheduleDialog — eligibility before a time is picked", () => {
  it("says it is checking, with confirm disabled, until the server answers", async () => {
    renderDialog();
    expect(screen.getByText(/checking whether this appointment can be moved/i)).toBeInTheDocument();
    expect(confirm()).toBeDisabled();
    expect(await screen.findByLabelText(/new date/i)).toBeInTheDocument();
  });

  it("a closed appointment: explains why straight away, shows no time picker, and confirm stays disabled", async () => {
    server.use(http.get("http://localhost/api/v1/appointments/:id/reschedule-preview", () => HttpResponse.json(preview({
      canReschedule: false, block: "AppointmentClosed", message: "Cannot reschedule a Completed appointment.",
    }))));
    renderDialog();
    expect(await screen.findByRole("alert")).toHaveTextContent("Cannot reschedule a Completed appointment.");
    expect(screen.queryByLabelText(/new date/i)).not.toBeInTheDocument();
    expect(confirm()).toBeDisabled();
    // The footer's safe action reads "Close" (the dialog's own X button is also named Close).
    expect(screen.getAllByRole("button", { name: "Close" }).some((b) => b.textContent === "Close")).toBe(true);
    expect(screen.queryByRole("button", { name: /^cancel$/i })).not.toBeInTheDocument();
  });

  it("a client inside the notice window: the save's own words, before any time is chosen", async () => {
    const msg = "This appointment is less than 48 hours away — please contact the studio directly to reschedule.";
    server.use(http.get("http://localhost/api/v1/appointments/:id/reschedule-preview", () => HttpResponse.json(preview({
      canReschedule: false, block: "InsideNoticeWindow", message: msg, noticeWindowApplies: true,
    }))));
    renderDialog();
    expect(await screen.findByRole("alert")).toHaveTextContent(msg);
    expect(screen.queryByLabelText(/new date/i)).not.toBeInTheDocument();
    expect(slotRequests).toHaveLength(0);
  });

  it("a client who may move it is told the window up front", async () => {
    server.use(http.get("http://localhost/api/v1/appointments/:id/reschedule-preview", () =>
      HttpResponse.json(preview({ noticeWindowApplies: true, noticeWindowHours: 72 }))));
    renderDialog();
    expect(await screen.findByText("Appointments can be moved up to 72 hours before they start.")).toBeInTheDocument();
  });

  it("staff are never shown the client notice window", async () => {
    renderDialog();
    await screen.findByLabelText(/new date/i);
    expect(screen.queryByText(/can be moved up to/i)).not.toBeInTheDocument();
  });

  it("if the check fails, confirm stays disabled and it can be retried", async () => {
    let calls = 0;
    server.use(http.get("http://localhost/api/v1/appointments/:id/reschedule-preview", () => {
      calls += 1;
      return calls === 1 ? HttpResponse.json({ message: "boom" }, { status: 500 }) : HttpResponse.json(preview());
    }));
    const user = userEvent.setup();
    renderDialog();
    expect(await screen.findByText(/couldn't check whether this appointment can be moved/i)).toBeInTheDocument();
    expect(confirm()).toBeDisabled();
    await user.click(screen.getByRole("button", { name: /try again/i }));
    expect(await screen.findByLabelText(/new date/i)).toBeInTheDocument();
  });
});

describe("RescheduleDialog — the appointment never blocks its own move", () => {
  it("asks the slot check to exclude the appointment being moved", async () => {
    renderDialog();
    await screen.findByLabelText(/new date/i);
    await waitFor(() => expect(slotRequests.length).toBeGreaterThan(0), { timeout: 3000 });
    expect(slotRequests[0].searchParams.get("excludeAppointmentId")).toBe("appt-1");
    expect(slotRequests[0].searchParams.get("artistId")).toBe("a-001");
  });
});
