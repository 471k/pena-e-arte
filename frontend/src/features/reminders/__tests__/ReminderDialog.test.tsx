import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { toast } from "sonner";

import authReducer from "@/features/auth/authSlice";
import { remindersApi } from "@/features/reminders/remindersApi";
import { ReminderDialog } from "@/features/reminders/components/ReminderDialog";
import type { ManualReminderResponse } from "@/features/reminders/reminder.types";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const SCHEDULED: ManualReminderResponse = {
  id: "rem-001", appointmentId: "appt-001", clientId: "client-001",
  recipientName: "Ana Silva", recipientPhone: "+351910000001",
  message: null, scheduledFor: "2026-09-01T10:00:00.000Z",
  status: "Scheduled", sentAt: null, createdAt: "2026-08-01T00:00:00.000Z",
};

const SENT: ManualReminderResponse = {
  ...SCHEDULED, id: "rem-002", status: "Sent", sentAt: "2026-08-01T10:00:00.000Z",
};

const server = setupServer(
  http.get("http://localhost/api/v1/reminders", () => HttpResponse.json([])),
  http.post("http://localhost/api/v1/reminders", async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    return HttpResponse.json({
      id: "rem-new", appointmentId: body.appointmentId ?? null, clientId: body.clientId ?? null,
      recipientName: body.recipientName ?? "Ana Silva", recipientPhone: body.recipientPhone ?? "+351910000001",
      message: body.message ?? null, scheduledFor: body.scheduledFor ?? new Date().toISOString(),
      status: "Scheduled", sentAt: null, createdAt: new Date().toISOString(),
    });
  }),
  http.delete("http://localhost/api/v1/reminders/:id", () => new HttpResponse(null, { status: 204 })),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); vi.clearAllMocks(); });
afterAll(() => server.close());

// eslint-disable-next-line @typescript-eslint/no-explicit-any
function makeStore() {
  return configureStore({
    reducer: {
      auth: authReducer,
      [remindersApi.reducerPath]: remindersApi.reducer,
    },
    middleware: (gd) => gd().concat(remindersApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "test@test.com" }, token: "fake", tenantId: "t1", role: "artist" } as any,
    },
  });
}

function renderDialog(props: Partial<React.ComponentProps<typeof ReminderDialog>> = {}) {
  const onOpenChange = vi.fn();
  render(
    <Provider store={makeStore()}>
      <ReminderDialog open onOpenChange={onOpenChange} {...props} />
    </Provider>,
  );
  return { onOpenChange };
}

describe("ReminderDialog", () => {
  // ── Raw-contact vs linked mode ──────────────────────────────────────────────

  it("raw-contact mode (no appointmentId/clientId) shows name/phone inputs", () => {
    renderDialog();
    expect(screen.getByLabelText(/^name$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^phone$/i)).toBeInTheDocument();
  });

  it("appointment-linked mode does not show name/phone inputs", () => {
    renderDialog({ appointmentId: "appt-001" });
    expect(screen.queryByLabelText(/^name$/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/^phone$/i)).not.toBeInTheDocument();
  });

  it("client-linked mode does not show name/phone inputs", () => {
    renderDialog({ clientId: "client-001" });
    expect(screen.queryByLabelText(/^name$/i)).not.toBeInTheDocument();
  });

  // ── Send now / Schedule toggle ──────────────────────────────────────────────

  it("date picker is hidden until 'Schedule for later' is toggled on", () => {
    renderDialog({ appointmentId: "appt-001" });
    expect(screen.queryByLabelText(/send at/i)).not.toBeInTheDocument();
  });

  it("toggling 'Schedule for later' reveals the date picker", async () => {
    const user = userEvent.setup();
    renderDialog({ appointmentId: "appt-001" });

    await user.click(screen.getByRole("switch"));

    expect(screen.getByLabelText(/send at/i)).toBeInTheDocument();
  });

  it("submit button reads 'Send now' by default and 'Schedule reminder' once scheduled", async () => {
    const user = userEvent.setup();
    renderDialog({ appointmentId: "appt-001" });

    expect(screen.getByRole("button", { name: /send now/i })).toBeInTheDocument();

    await user.click(screen.getByRole("switch"));

    expect(screen.getByRole("button", { name: /schedule reminder/i })).toBeInTheDocument();
  });

  // ── Submit gating ────────────────────────────────────────────────────────────

  it("submit is disabled in raw-contact mode until both name and phone are filled", async () => {
    const user = userEvent.setup();
    renderDialog();

    expect(screen.getByRole("button", { name: /send now/i })).toBeDisabled();

    await user.type(screen.getByLabelText(/^name$/i), "Wendy");
    expect(screen.getByRole("button", { name: /send now/i })).toBeDisabled();

    await user.type(screen.getByLabelText(/^phone$/i), "+351900000000");
    expect(screen.getByRole("button", { name: /send now/i })).not.toBeDisabled();
  });

  it("submit is enabled immediately in appointment-linked mode (recipient is implicit)", () => {
    renderDialog({ appointmentId: "appt-001" });
    expect(screen.getByRole("button", { name: /send now/i })).not.toBeDisabled();
  });

  // ── Submit flow ──────────────────────────────────────────────────────────────

  it("submitting a raw-contact reminder calls the create mutation and shows a success toast", async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/^name$/i), "Wendy");
    await user.type(screen.getByLabelText(/^phone$/i), "+351900000000");
    await user.click(screen.getByRole("button", { name: /send now/i }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Reminder sent."));
  });

  it("a failed create shows an error toast", async () => {
    server.use(
      http.post("http://localhost/api/v1/reminders", () =>
        HttpResponse.json({ message: "Quota exceeded" }, { status: 429 })),
    );
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/^name$/i), "Wendy");
    await user.type(screen.getByLabelText(/^phone$/i), "+351900000000");
    await user.click(screen.getByRole("button", { name: /send now/i }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Quota exceeded"));
  });

  // ── History list ─────────────────────────────────────────────────────────────

  it("renders the history list with status badges", async () => {
    server.use(
      http.get("http://localhost/api/v1/reminders", () => HttpResponse.json([SCHEDULED, SENT])),
    );
    renderDialog({ appointmentId: "appt-001" });

    expect(await screen.findByText("Scheduled")).toBeInTheDocument();
    expect(screen.getByText("Sent")).toBeInTheDocument();
  });

  it("shows a Cancel action only for Scheduled rows, not Sent ones", async () => {
    server.use(
      http.get("http://localhost/api/v1/reminders", () => HttpResponse.json([SCHEDULED, SENT])),
    );
    renderDialog({ appointmentId: "appt-001" });

    await screen.findByText("Scheduled");
    const cancelButtons = screen.getAllByRole("button", { name: /cancel reminder/i });
    expect(cancelButtons).toHaveLength(1);
  });

  it("clicking Cancel on a Scheduled row calls the cancel mutation", async () => {
    server.use(
      http.get("http://localhost/api/v1/reminders", () => HttpResponse.json([SCHEDULED])),
    );
    const user = userEvent.setup();
    renderDialog({ appointmentId: "appt-001" });

    await user.click(await screen.findByRole("button", { name: /cancel reminder/i }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Reminder cancelled."));
  });

  it("shows an empty-history message when no reminders exist yet", async () => {
    renderDialog({ appointmentId: "appt-001" });
    expect(await screen.findByText(/no reminders sent yet/i)).toBeInTheDocument();
  });

  it("raw-contact mode does not render a history section", () => {
    renderDialog();
    expect(screen.queryByText(/history/i)).not.toBeInTheDocument();
  });
});
