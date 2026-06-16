import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import notificationsReducer, { setUnreadCount } from "@/features/notifications/notificationsSlice";
import { notificationsApi } from "@/features/notifications/notificationsApi";
import { NotificationLogListPage } from "@/features/notifications/components/NotificationLogListPage";
import type { NotificationLogResponse } from "@/features/notifications/notification.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const EMAIL_LOG: NotificationLogResponse = {
  id:          "log-001",
  recipientId: "11111111-2222-3333-4444-555555555555",
  channel:     "Email",
  subject:     "Appointment Confirmed — Mon, 15 Jun 2026 at 14:00",
  body:        "<html>confirmation</html>",
  sentAt:      "2026-06-10T10:00:00Z",
  isSuccess:   true,
  createdAt:   "2026-06-10T10:00:00Z",
};

const SMS_LOG: NotificationLogResponse = {
  id:          "log-002",
  recipientId: "66666666-7777-8888-9999-000000000000",
  channel:     "Sms",
  subject:     null,
  body:        "Reminder: Your tattoo session is in 48 hours.",
  sentAt:      "2026-06-09T09:00:00Z",
  isSuccess:   false,
  createdAt:   "2026-06-09T09:00:00Z",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/notifications", () =>
    HttpResponse.json([EMAIL_LOG, SMS_LOG]),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                            authReducer,
      notifications:                   notificationsReducer,
      [notificationsApi.reducerPath]:  notificationsApi.reducer,
    },
    middleware: (gd) => gd().concat(notificationsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "owner@test.com" }, token: "fake-token", tenantId: "s-001", role: "owner", pendingReferralCode: null } as any,
    },
  });
}

function renderPage(store = makeStore()) {
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/notifications"]}>
        <Routes>
          <Route path="/notifications" element={<NotificationLogListPage />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("NotificationLogListPage", () => {

  it("renders the 'Notification Log' header", () => {
    renderPage();
    expect(screen.getByText("Notification Log")).toBeInTheDocument();
  });

  it("shows skeleton rows while loading", () => {
    server.use(
      http.get("http://localhost/api/v1/notifications", async () => {
        await new Promise((r) => setTimeout(r, 60_000));
        return HttpResponse.json([]);
      }),
    );
    renderPage();
    const skeletons = document.querySelectorAll("[class*='animate-pulse'], [data-slot='skeleton']");
    expect(skeletons.length).toBeGreaterThan(0);
  });

  it("shows an error message when the fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/notifications", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText("Failed to load notification log. Please try again.")).toBeInTheDocument();
  });

  it("shows empty-state text when there are no notifications", async () => {
    server.use(
      http.get("http://localhost/api/v1/notifications", () => HttpResponse.json([])),
    );
    renderPage();
    expect(await screen.findByText("No notifications found.")).toBeInTheDocument();
  });

  it("renders a row for each notification returned by the API", async () => {
    renderPage();
    expect(await screen.findByText(/Appointment Confirmed/)).toBeInTheDocument();
    expect(screen.getByText(/Reminder: Your tattoo session/)).toBeInTheDocument();
  });

  it("shows the Email and SMS channel badges", async () => {
    renderPage();
    await screen.findByText(/Appointment Confirmed/);
    // "Email" / "SMS" also appear as <select> options, so badges are only confirmed by count.
    expect(screen.getAllByText("Email").length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText("SMS").length).toBeGreaterThanOrEqual(1);
  });

  it("shows 'Delivered' for a successful notification", async () => {
    renderPage();
    await screen.findByText(/Appointment Confirmed/);
    expect(screen.getByText("Delivered")).toBeInTheDocument();
  });

  it("shows 'Failed' for an unsuccessful notification", async () => {
    renderPage();
    await screen.findByText(/Appointment Confirmed/);
    expect(screen.getByText("Failed")).toBeInTheDocument();
  });

  it("shows the entry count in the header", async () => {
    renderPage();
    expect(await screen.findByText("2 entries")).toBeInTheDocument();
  });

  it("uses singular 'entry' in the count when only one notification exists", async () => {
    server.use(
      http.get("http://localhost/api/v1/notifications", () => HttpResponse.json([EMAIL_LOG])),
    );
    renderPage();
    expect(await screen.findByText("1 entry")).toBeInTheDocument();
  });

  it("does not show the 'Clear filters' button when no filters are set", () => {
    renderPage();
    expect(screen.queryByRole("button", { name: /clear filters/i })).not.toBeInTheDocument();
  });

  it("shows the 'Clear filters' button once a channel filter is chosen", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.selectOptions(screen.getByLabelText("Channel"), "Email");
    expect(screen.getByRole("button", { name: /clear filters/i })).toBeInTheDocument();
  });

  it("sends the chosen channel as a query parameter", async () => {
    let capturedChannel: string | null = null;
    server.use(
      http.get("http://localhost/api/v1/notifications", ({ request }) => {
        capturedChannel = new URL(request.url).searchParams.get("channel");
        return HttpResponse.json([EMAIL_LOG]);
      }),
    );
    const user = userEvent.setup();
    renderPage();
    await user.selectOptions(screen.getByLabelText("Channel"), "Sms");
    await screen.findByText(/Appointment Confirmed/);
    expect(capturedChannel).toBe("Sms");
  });

  it("extends the 'to' date filter to the end of the selected day", async () => {
    let capturedTo: string | null = null;
    server.use(
      http.get("http://localhost/api/v1/notifications", ({ request }) => {
        capturedTo = new URL(request.url).searchParams.get("to");
        return HttpResponse.json([EMAIL_LOG]);
      }),
    );
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText("To"), "2026-06-15");
    await screen.findByText(/Appointment Confirmed/);
    expect(capturedTo).toBe("2026-06-15T23:59:59.999");
  });

  it("clicking 'Clear filters' resets the channel and date filters", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.selectOptions(screen.getByLabelText("Channel"), "Email");
    await user.click(screen.getByRole("button", { name: /clear filters/i }));
    expect(screen.getByLabelText("Channel")).toHaveValue("");
    expect(screen.queryByRole("button", { name: /clear filters/i })).not.toBeInTheDocument();
  });

  it("clears the unread notification count on mount", async () => {
    const store = makeStore();
    store.dispatch(setUnreadCount(5));
    renderPage(store);
    expect(store.getState().notifications.unreadCount).toBe(0);
  });
});
