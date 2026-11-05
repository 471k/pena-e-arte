import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { messagingApi } from "@/features/messaging/messagingApi";
import { ClientCommunicationCard } from "@/features/appointments/components/ClientCommunicationCard";
import {
  CHANNEL_LABEL, NO_ACCOUNT, aftercareSummary, channelLine, rescheduleNotSent, rescheduleSent,
} from "@/features/appointments/components/communicationCopy";
import type {
  AftercareCommunication, AppointmentCommunicationResponse, AppointmentResponse,
} from "@/features/appointments/appointment.types";
import { Role } from "@/shared/types/roles";

// E04b (A08): communication state shows not sent / sent / failed, from the server's records, and never
// claims a message was delivered, received or read — nothing in the data can prove that.

const FORBIDDEN = /\b(delivered|delivery|received|read)\b/i;

const APPT: AppointmentResponse = {
  id: "appt-1", studioId: "s-001", artistId: "a-001", clientId: "c-001", clientName: "Mira Hoxha", clientUserId: "u-client",
  date: "2030-12-10T09:00:00Z", endDate: "2030-12-10T11:00:00Z", durationMinutes: 120,
  status: "Confirmed", depositStatus: "Pending", depositAmount: 0, notes: null, createdAt: "2030-01-01T00:00:00Z",
};

const MOVED = "2030-12-01T08:00:00Z";
const MESSAGED = "2030-12-01T09:30:00Z";

function comms(overrides: Partial<AppointmentCommunicationResponse>): AppointmentCommunicationResponse {
  return { appointmentId: "appt-1", reschedule: null, aftercare: null, ...overrides };
}

let commsBody: AppointmentCommunicationResponse = comms({});
let sentMessages: unknown[] = [];
const server = setupServer(
  http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json({ id: "s-001", timezone: "UTC", currency: "EUR" })),
  http.get("http://localhost/api/v1/appointments/:id/communication", () => HttpResponse.json(commsBody)),
  http.post("http://localhost/api/v1/conversations", () => HttpResponse.json({ id: "conv-1" })),
  http.post("http://localhost/api/v1/conversations/:id/messages", async ({ request }) => {
    sentMessages.push(await request.json());
    commsBody = comms({ reschedule: { state: "Sent", rescheduledAt: MOVED, messagedAt: MESSAGED } });
    return HttpResponse.json({ id: "m-1" });
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); commsBody = comms({}); sentMessages = []; });
afterAll(() => server.close());

function renderCard(appt: AppointmentResponse = APPT) {
  const store = configureStore({
    reducer: {
      auth: authReducer, ui: uiReducer,
      [appointmentsApi.reducerPath]: appointmentsApi.reducer,
      [paymentsApi.reducerPath]: paymentsApi.reducer,
      [studiosApi.reducerPath]: studiosApi.reducer,
      [messagingApi.reducerPath]: messagingApi.reducer,
    },
    middleware: (gd) => gd().concat(appointmentsApi.middleware).concat(paymentsApi.middleware)
      .concat(studiosApi.middleware).concat(messagingApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u-1", email: "a@test.com" }, token: "t", tenantId: "s-001", role: Role.Artist, pendingReferralCode: null, impersonation: null } as any,
      ui: { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null, impersonationScopeError: null, impersonationSessionExpired: false },
    },
  });
  render(<Provider store={store}><MemoryRouter><ClientCommunicationCard appt={appt} /></MemoryRouter></Provider>);
}

describe("ClientCommunicationCard — reschedule", () => {
  it("not sent: the task is open, says the client isn't told automatically, and offers the message box", async () => {
    commsBody = comms({ reschedule: { state: "NotSent", rescheduledAt: MOVED, messagedAt: null } });
    renderCard();
    expect(await screen.findByText(/isn't told automatically, and nobody has messaged them/i)).toBeInTheDocument();
    expect(screen.getByLabelText("Message Mira Hoxha about this session")).toBeInTheDocument();
  });

  it("sending a message about the appointment closes the task, from the server's refreshed record", async () => {
    commsBody = comms({ reschedule: { state: "NotSent", rescheduledAt: MOVED, messagedAt: null } });
    const user = userEvent.setup();
    renderCard();
    await user.type(await screen.findByLabelText("Message Mira Hoxha about this session"), "Your session moved to Tuesday.");
    await user.click(screen.getByRole("button", { name: "Send" }));
    expect(await screen.findByText(/a message about it was sent to the client/i)).toBeInTheDocument();
    expect(sentMessages).toEqual([{ body: "Your session moved to Tuesday.", appointmentId: "appt-1" }]);
  });

  it("a client without an account: says to tell them another way, no message box", async () => {
    commsBody = comms({ reschedule: { state: "NotSent", rescheduledAt: MOVED, messagedAt: null } });
    renderCard({ ...APPT, clientUserId: null });
    expect(await screen.findByText(NO_ACCOUNT)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Send" })).not.toBeInTheDocument();
  });
});

describe("ClientCommunicationCard — aftercare", () => {
  it.each([
    ["NotSent", [], /aftercare hasn't been sent/i],
    ["Sent", [{ channel: "Email", sent: true, at: "2030-12-10T12:00:00Z" }, { channel: "Sms", sent: false, at: "2030-12-10T12:00:00Z" }], /aftercare was sent/i],
    ["Failed", [{ channel: "Email", sent: false, at: "2030-12-10T12:00:00Z" }], /couldn't be sent: every attempt failed/i],
    ["NotRecorded", [], /no record of a message going out/i],
  ] as const)("%s", async (state, channels, words) => {
    commsBody = comms({ aftercare: { state, ranAt: state === "NotSent" ? null : "2030-12-10T12:00:00Z", channels: [...channels] } });
    renderCard({ ...APPT, status: "Completed" });
    expect(await screen.findByText(words)).toBeInTheDocument();
    for (const c of channels) {
      expect(screen.getByText(new RegExp(`^${CHANNEL_LABEL[c.channel]}: ${c.sent ? "sent" : "failed to send"}`))).toBeInTheDocument();
    }
  });

  it("an artist on a colleague's session (404) sees nothing at all", async () => {
    server.use(http.get("http://localhost/api/v1/appointments/:id/communication", () =>
      HttpResponse.json({ message: "not found" }, { status: 404 })));
    renderCard();
    await new Promise((r) => setTimeout(r, 50));
    expect(screen.queryByTestId("client-communication")).not.toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});

describe("Communication copy never claims delivery or reading", () => {
  const when = (iso: string) => iso;

  it("no communication-state string contains delivered, received or read", () => {
    const states: AftercareCommunication["state"][] = ["NotSent", "Sent", "Failed", "NotRecorded"];
    const strings = [
      rescheduleNotSent(MOVED, when),
      rescheduleSent(MOVED, MESSAGED, when),
      NO_ACCOUNT,
      ...states.map((state) => aftercareSummary({ state, ranAt: MOVED, channels: [] }, when)),
      ...(["Email", "Sms"] as const).flatMap((channel) => [true, false].map((sent) => channelLine({ channel, sent, at: MOVED }, when))),
    ];
    for (const s of strings) expect(s).not.toMatch(FORBIDDEN);
  });

  it("nor does anything rendered in the card, in every state", async () => {
    commsBody = comms({
      reschedule: { state: "NotSent", rescheduledAt: MOVED, messagedAt: null },
      aftercare: { state: "Failed", ranAt: MOVED, channels: [{ channel: "Email", sent: false, at: MOVED }, { channel: "Sms", sent: true, at: MOVED }] },
    });
    renderCard({ ...APPT, status: "Completed" });
    const card = await screen.findByTestId("client-communication");
    expect(within(card).getByText(/every attempt failed/i)).toBeInTheDocument();
    expect(card.textContent ?? "").not.toMatch(FORBIDDEN);
  });
});
