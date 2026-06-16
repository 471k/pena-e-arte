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
import { NotificationBell } from "@/features/notifications/components/NotificationBell";
import type { NotificationLogResponse } from "@/features/notifications/notification.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const LOGS: NotificationLogResponse[] = Array.from({ length: 7 }, (_, i) => ({
  id:          `log-${i}`,
  recipientId: "11111111-2222-3333-4444-555555555555",
  channel:     i % 2 === 0 ? "Email" : "Sms",
  subject:     i % 2 === 0 ? `Subject ${i}` : null,
  body:        `Body ${i}`,
  sentAt:      `2026-06-${String(10 - i).padStart(2, "0")}T10:00:00Z`,
  isSuccess:   true,
  createdAt:   `2026-06-${String(10 - i).padStart(2, "0")}T10:00:00Z`,
}));

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/notifications", () => HttpResponse.json(LOGS)),
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

function renderBell(store = makeStore()) {
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/dashboard"]}>
        <Routes>
          <Route path="/dashboard" element={<NotificationBell />} />
          <Route path="/notifications" element={<div data-testid="notifications-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("NotificationBell", () => {
  it("does not show a badge when unreadCount is 0", () => {
    renderBell();
    expect(screen.queryByText(/^[0-9]+\+?$/)).not.toBeInTheDocument();
  });

  it("shows the unread count badge when greater than 0", () => {
    const store = makeStore();
    store.dispatch(setUnreadCount(3));
    renderBell(store);
    expect(screen.getByText("3")).toBeInTheDocument();
  });

  it("caps the badge display at '9+' for large counts", () => {
    const store = makeStore();
    store.dispatch(setUnreadCount(42));
    renderBell(store);
    expect(screen.getByText("9+")).toBeInTheDocument();
  });

  it("uses a plain accessible name when there are no unread notifications", () => {
    renderBell();
    expect(screen.getByRole("button", { name: "View notifications" })).toBeInTheDocument();
  });

  it("includes the unread count in the accessible name, not just the visual badge", () => {
    const store = makeStore();
    store.dispatch(setUnreadCount(4));
    renderBell(store);
    // The visual badge text alone isn't enough — a screen reader only hears the
    // accessible name (aria-label), so the count must be embedded in it too.
    expect(screen.getByRole("button", { name: "View notifications, 4 unread" })).toBeInTheDocument();
  });

  it("accessible name is distinct from a page's own literal 'Notifications' link", () => {
    renderBell();
    expect(screen.queryByRole("button", { name: "Notifications" })).not.toBeInTheDocument();
  });

  it("does not fetch notifications until the panel is opened", () => {
    let called = false;
    server.use(
      http.get("http://localhost/api/v1/notifications", () => {
        called = true;
        return HttpResponse.json(LOGS);
      }),
    );
    renderBell();
    expect(called).toBe(false);
  });

  it("panel is closed by default", () => {
    renderBell();
    expect(screen.queryByText("View all")).not.toBeInTheDocument();
  });

  it("clicking the bell opens the panel and fetches notifications", async () => {
    const user = userEvent.setup();
    renderBell();
    await user.click(screen.getByRole("button", { name: /notifications/i }));
    expect(await screen.findByText("View all")).toBeInTheDocument();
  });

  it("shows at most 5 recent notifications even when more exist", async () => {
    const user = userEvent.setup();
    renderBell();
    await user.click(screen.getByRole("button", { name: /notifications/i }));
    await screen.findByText("View all");
    expect(screen.getAllByText(/^Body \d$/).length).toBe(5);
  });

  it("opening the panel clears the unread count", async () => {
    const user  = userEvent.setup();
    const store = makeStore();
    store.dispatch(setUnreadCount(4));
    renderBell(store);

    expect(screen.getByText("4")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /notifications/i }));

    expect(store.getState().notifications.unreadCount).toBe(0);
  });

  it("clicking the bell again closes the panel", async () => {
    const user = userEvent.setup();
    renderBell();
    const button = screen.getByRole("button", { name: /notifications/i });

    await user.click(button);
    await screen.findByText("View all");

    await user.click(button);
    expect(screen.queryByText("View all")).not.toBeInTheDocument();
  });

  it("clicking outside the panel closes it", async () => {
    const user = userEvent.setup();
    render(
      <Provider store={makeStore()}>
        <MemoryRouter initialEntries={["/dashboard"]}>
          <div>
            <button>Outside</button>
            <Routes>
              <Route path="/dashboard" element={<NotificationBell />} />
            </Routes>
          </div>
        </MemoryRouter>
      </Provider>,
    );

    await user.click(screen.getByRole("button", { name: /notifications/i }));
    await screen.findByText("View all");

    await user.click(screen.getByRole("button", { name: "Outside" }));
    expect(screen.queryByText("View all")).not.toBeInTheDocument();
  });

  it("shows an empty state when there are no notifications", async () => {
    server.use(
      http.get("http://localhost/api/v1/notifications", () => HttpResponse.json([])),
    );
    const user = userEvent.setup();
    renderBell();
    await user.click(screen.getByRole("button", { name: /notifications/i }));
    expect(await screen.findByText("No notifications yet.")).toBeInTheDocument();
  });

  it("shows an error state when the fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/notifications", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    renderBell();
    await user.click(screen.getByRole("button", { name: /notifications/i }));
    expect(await screen.findByText("Failed to load notifications.")).toBeInTheDocument();
  });

  it("'View all' link navigates to the full notification log and closes the panel", async () => {
    const user = userEvent.setup();
    renderBell();
    await user.click(screen.getByRole("button", { name: /notifications/i }));
    await user.click(await screen.findByText("View all"));

    expect(screen.getByTestId("notifications-page")).toBeInTheDocument();
  });
});
