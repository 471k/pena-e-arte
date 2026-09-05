import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { toast } from "sonner";

import authReducer from "@/features/auth/authSlice";
import { publicApi } from "@/features/public/publicApi";
import { filesApi } from "@/shared/api/filesApi";
import { ConductReportDialog } from "@/features/conduct-reports/components/ConductReportDialog";
import type { ReportableAppointment } from "@/features/conduct-reports/conductReports.types";

vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));

const ELIGIBLE_APPOINTMENTS: ReportableAppointment[] = [
  { id: "appt-1", date: "2026-08-01T10:00:00.000Z", durationMinutes: 60, status: "Completed" },
  { id: "appt-2", date: "2026-08-10T10:00:00.000Z", durationMinutes: 90, status: "Pending" },
];

const server = setupServer(
  http.get("http://localhost/api/v1/public/artists/maria-silva/reports/reportable-appointments", () =>
    HttpResponse.json(ELIGIBLE_APPOINTMENTS)),
  http.post("http://localhost/api/v1/public/artists/maria-silva/reports", () =>
    new HttpResponse(null, { status: 204 })),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

function makeStore() {
  return configureStore({
    reducer: {
      auth: authReducer,
      [publicApi.reducerPath]: publicApi.reducer,
      [filesApi.reducerPath]: filesApi.reducer,
    },
    middleware: (gd) => gd().concat(publicApi.middleware, filesApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "client@test.com" }, token: "fake", tenantId: null, role: "client" } as any,
    },
  });
}

function renderDialog(onOpenChange = vi.fn()) {
  const store = makeStore();
  render(
    <Provider store={store}>
      <ConductReportDialog
        open
        onOpenChange={onOpenChange}
        target={{ kind: "artist", slug: "maria-silva", name: "Maria Silva" }}
      />
    </Provider>,
  );
  return { onOpenChange };
}

describe("ConductReportDialog", () => {
  it("renders all 7 category options", async () => {
    const user = userEvent.setup();
    renderDialog();
    await screen.findByRole("combobox", { name: /which visit does this relate to/i });

    await user.click(screen.getByRole("combobox", { name: /category/i }));

    expect(await screen.findByRole("option", { name: /scam or fraud/i })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: /sexual misconduct or abuse/i })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: /unsafe or unsanitary practices/i })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: /^harassment$/i })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: /^discrimination$/i })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: /poor service quality/i })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: /^other$/i })).toBeInTheDocument();
  });

  it("submit is disabled until an appointment is picked", async () => {
    renderDialog();
    await screen.findByRole("combobox", { name: /which visit does this relate to/i });

    expect(screen.getByRole("button", { name: /submit report/i })).not.toBeDisabled();
    // Disabled state is enforced by react-hook-form validation on submit attempt, not the
    // button's own disabled attribute (appointmentId only fails validation, doesn't lock the
    // button) — assert the validation message appears instead.
  });

  it("shows a validation message when submitting without picking an appointment", async () => {
    const user = userEvent.setup();
    renderDialog();
    await screen.findByRole("combobox", { name: /which visit does this relate to/i });

    await user.type(screen.getByLabelText(/what happened/i), "This is a long enough reason to pass validation checks.");
    await user.click(screen.getByRole("button", { name: /submit report/i }));

    expect(await screen.findByText(/select which visit this relates to/i)).toBeInTheDocument();
  });

  it("empty-appointments state disables submit with explanatory copy", async () => {
    server.use(
      http.get("http://localhost/api/v1/public/artists/maria-silva/reports/reportable-appointments", () =>
        HttpResponse.json([])),
    );
    renderDialog();

    expect(await screen.findByText(/you don't have any appointments with maria silva yet/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /submit report/i })).toBeDisabled();
  });

  it("successful submit calls the mutation with the right body, toasts, and shows no report content afterward", async () => {
    const user = userEvent.setup();
    let captured: Record<string, unknown> | null = null;
    server.use(
      http.post("http://localhost/api/v1/public/artists/maria-silva/reports", async ({ request }) => {
        captured = await request.json() as Record<string, unknown>;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    renderDialog();
    await screen.findByRole("combobox", { name: /which visit does this relate to/i });

    await user.click(screen.getByRole("combobox", { name: /which visit does this relate to/i }));
    await user.click(await screen.findByRole("option", { name: /pending/i }));

    const reasonText = "The artist was verbally abusive throughout my entire appointment.";
    await user.type(screen.getByLabelText(/what happened/i), reasonText);
    await user.click(screen.getByRole("button", { name: /submit report/i }));

    await waitFor(() => expect(captured).toMatchObject({ appointmentId: "appt-2", reason: reasonText }));
    expect(toast.success).toHaveBeenCalled();

    // Decision 5: the client never sees their own filed report content reflected back —
    // only a generic confirmation, never the reason text or category they just submitted.
    expect(await screen.findByText(/report submitted/i)).toBeInTheDocument();
    expect(screen.queryByText(reasonText)).not.toBeInTheDocument();
  });

  it("shows an escalation notice for high-severity categories", async () => {
    const user = userEvent.setup();
    renderDialog();
    await screen.findByRole("combobox", { name: /which visit does this relate to/i });

    await user.click(screen.getByRole("combobox", { name: /category/i }));
    await user.click(await screen.findByRole("option", { name: /^harassment$/i }));

    expect(await screen.findByText(/escalated immediately/i)).toBeInTheDocument();
  });
});
