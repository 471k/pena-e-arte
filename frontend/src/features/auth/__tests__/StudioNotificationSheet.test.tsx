import { describe, it, expect, beforeAll, afterEach, afterAll, vi } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { toast } from "sonner";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { authApi } from "@/features/auth/authApi";
import { StudioNotificationSheet } from "@/features/auth/components/StudioNotificationSheet";
import { Role } from "@/shared/types/roles";

vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));

const DEFAULT_PREFERENCES = [
  { type: "AppointmentCreated",   channel: "Email", isEnabled: true },
  { type: "AppointmentCreated",   channel: "Sms",   isEnabled: true },
  { type: "AppointmentConfirmed", channel: "Email", isEnabled: true },
  { type: "AppointmentConfirmed", channel: "Sms",   isEnabled: true },
  { type: "AppointmentCancelled", channel: "Email", isEnabled: true },
  { type: "AppointmentCancelled", channel: "Sms",   isEnabled: true },
  { type: "DepositCaptured",      channel: "Email", isEnabled: true },
  { type: "DepositCaptured",      channel: "Sms",   isEnabled: true },
  { type: "PaymentRefunded",      channel: "Email", isEnabled: true },
  { type: "PaymentRefunded",      channel: "Sms",   isEnabled: true },
];

let capturedStudioId: string | undefined;

const server = setupServer(
  http.get(
    "http://localhost/api/v1/auth/my-studios/:studioId/notification-preferences",
    ({ params }) => {
      capturedStudioId = params.studioId as string;
      return HttpResponse.json({ preferences: DEFAULT_PREFERENCES });
    },
  ),
  http.put(
    "http://localhost/api/v1/auth/my-studios/:studioId/notification-preferences",
    () => HttpResponse.json(null, { status: 204 }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); capturedStudioId = undefined; });
afterAll(() => server.close());

function makeStore() {
  return configureStore({
    reducer: {
      auth: authReducer,
      ui: uiReducer,
      [authApi.reducerPath]: authApi.reducer,
    },
    middleware: (gd) => gd().concat(authApi.middleware),
    preloadedState: {
      auth: {
        user: { id: "u-001", email: "test@test.com" },
        token: "fake-token",
        refreshToken: null,
        tenantId: "studio-aaa",
        role: Role.Client,
        pendingReferralCode: null,
      },
      ui: { readOnlyError: null, sessionExpired: false, studioSuspended: false },
    },
  });
}

function renderSheet(overrides: Partial<React.ComponentProps<typeof StudioNotificationSheet>> = {}) {
  const onClose = vi.fn();
  const utils = render(
    <Provider store={makeStore()}>
      <StudioNotificationSheet
        studioId="studio-aaa"
        studioName="Alpha Ink"
        open
        onClose={onClose}
        {...overrides}
      />
    </Provider>,
  );
  return { ...utils, onClose };
}

describe("StudioNotificationSheet", () => {
  it("renders the sheet title with the studio name when open", async () => {
    renderSheet();
    expect(await screen.findByText(/notifications — alpha ink/i)).toBeInTheDocument();
  });

  it("fetches preferences for the correct studioId when open", async () => {
    renderSheet();
    await screen.findByText(/notifications — alpha ink/i);
    await vi.waitFor(() => expect(capturedStudioId).toBe("studio-aaa"));
  });

  it("does not fetch preferences when closed", () => {
    renderSheet({ open: false });
    expect(capturedStudioId).toBeUndefined();
  });

  it("renders all 5 notification type rows", async () => {
    renderSheet();
    expect(await screen.findByText("Appointment confirmed")).toBeInTheDocument();
    expect(screen.getByText("Appointment reminder")).toBeInTheDocument();
    expect(screen.getByText("Appointment cancelled")).toBeInTheDocument();
    expect(screen.getByText("Deposit captured")).toBeInTheDocument();
    expect(screen.getByText("Payment refunded")).toBeInTheDocument();
  });

  it("renders Email and SMS column headers", async () => {
    renderSheet();
    await screen.findByText("Appointment confirmed");
    expect(screen.getByText("Email")).toBeInTheDocument();
    expect(screen.getByText("SMS")).toBeInTheDocument();
  });

  it("disables the Save button until a toggle is changed", async () => {
    renderSheet();
    await screen.findByText("Appointment confirmed");
    expect(screen.getByRole("button", { name: /save preferences/i })).toBeDisabled();
  });

  it("enables the Save button after a toggle is changed", async () => {
    const user = userEvent.setup();
    renderSheet();
    await screen.findByText("Appointment confirmed");

    const toggle = screen.getByLabelText(/appointment confirmed via email/i);
    await user.click(toggle);

    expect(screen.getByRole("button", { name: /save preferences/i })).toBeEnabled();
  });

  it("calls the update mutation with the toggled preferences on Save", async () => {
    let capturedBody: unknown;
    server.use(
      http.put(
        "http://localhost/api/v1/auth/my-studios/:studioId/notification-preferences",
        async ({ request }) => {
          capturedBody = await request.json();
          return HttpResponse.json(null, { status: 204 });
        },
      ),
    );
    const user = userEvent.setup();
    renderSheet();
    await screen.findByText("Appointment confirmed");

    await user.click(screen.getByLabelText(/appointment confirmed via email/i));
    await user.click(screen.getByRole("button", { name: /save preferences/i }));

    await vi.waitFor(() => expect(capturedBody).toBeDefined());
    const body = capturedBody as { preferences: { type: string; channel: string; isEnabled: boolean }[] };
    // The "Appointment confirmed" row label maps to the AppointmentCreated type.
    const toggled = body.preferences.find(
      (p) => p.type === "AppointmentCreated" && p.channel === "Email",
    );
    expect(toggled?.isEnabled).toBe(false);
  });

  it("shows a success toast and closes the sheet on successful save", async () => {
    const user = userEvent.setup();
    const { onClose } = renderSheet();
    await screen.findByText("Appointment confirmed");

    await user.click(screen.getByLabelText(/appointment confirmed via email/i));
    await user.click(screen.getByRole("button", { name: /save preferences/i }));

    await vi.waitFor(() => expect(toast.success).toHaveBeenCalledWith("Notification preferences saved."));
    expect(onClose).toHaveBeenCalled();
  });

  it("shows an error toast on save failure", async () => {
    server.use(
      http.put(
        "http://localhost/api/v1/auth/my-studios/:studioId/notification-preferences",
        () => HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    renderSheet();
    await screen.findByText("Appointment confirmed");

    await user.click(screen.getByLabelText(/appointment confirmed via email/i));
    await user.click(screen.getByRole("button", { name: /save preferences/i }));

    await vi.waitFor(() => expect(toast.error).toHaveBeenCalledWith("Failed to save preferences."));
  });
});
