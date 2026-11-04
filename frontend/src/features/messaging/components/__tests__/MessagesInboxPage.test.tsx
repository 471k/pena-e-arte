import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { messagingApi } from "../../messagingApi";
import { studiosApi } from "@/features/studios/studiosApi";
import type { ConversationResponse } from "../../messaging.types";
import { MessagesInboxPage } from "../MessagesInboxPage";
import { Role } from "@/shared/types/roles";

const server = setupServer(
  http.get("http://localhost/api/v1/conversations", () =>
    HttpResponse.json([]),
  ),
  http.get("http://localhost/api/v1/studios/me", () =>
    HttpResponse.json({ id: "t1", timezone: "Europe/Tirane" }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

function makeStore(role: Role) {
  return configureStore({
    reducer: {
      auth: authReducer,
      [messagingApi.reducerPath]: messagingApi.reducer,
      [studiosApi.reducerPath]: studiosApi.reducer,
    },
    middleware: (gd) => gd().concat(messagingApi.middleware, studiosApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "test@test.com" }, token: "fake", tenantId: "t1", role } as any,
    },
  });
}

function renderInbox(role: Role) {
  render(
    <Provider store={makeStore(role)}>
      <MemoryRouter initialEntries={["/messages"]}>
        <MessagesInboxPage />
      </MemoryRouter>
    </Provider>,
  );
}

describe("MessagesInboxPage", () => {
  it.each([Role.Client, Role.Artist, Role.Owner])(
    "shows an empty state when there are no conversations yet (%s)",
    async (role) => {
      renderInbox(role);

      expect(await screen.findByText(/no conversations yet/i)).toBeInTheDocument();
    },
  );
});

describe("MessagesInboxPage — which appointment the last message is about", () => {
  const row = (id: string, name: string, extra: Partial<ConversationResponse> = {}): ConversationResponse => ({
    id, otherUserId: `u-${id}`, otherRole: "artist", otherDisplayName: name, otherAvatarUrl: null,
    lastMessageAt: "2026-11-01T10:00:00Z", lastMessagePreview: "About my booking", lastMessageFromMe: true,
    unreadCount: 0, createdAt: "2026-10-01T00:00:00Z", ...extra,
  });
  const ROWS: ConversationResponse[] = [
    row("c1", "Marco Ink", {
      lastMessageAppointmentId: "appt-a",
      lastMessageAppointment: { appointmentId: "appt-a", startsAt: "2026-12-03T13:30:00Z", serviceName: "Fine line" },
    }),
    row("c2", "Ana Lines"),
  ];

  function useRows(rows: ConversationResponse[]) {
    server.use(http.get("http://localhost/api/v1/conversations", () => HttpResponse.json(rows)));
  }

  it.each([Role.Client, Role.Artist, Role.Owner])("shows the label on the row, in the studio's timezone (%s)", async (role) => {
    useRows(ROWS);
    renderInbox(role);

    const labelled = await screen.findByRole("button", { name: "Conversation with Marco Ink" });

    // The timezone loads after the conversations, hence findBy.
    expect(await within(labelled).findByText("Thu 3 Dec, 14:30 · Fine line")).toBeInTheDocument();
    expect(labelled).toHaveAccessibleDescription("Appointment: Thu 3 Dec, 14:30 · Fine line");
    expect(labelled.textContent).not.toContain("appt-a");
  });

  it("renders a row whose last message has no appointment exactly as before", async () => {
    useRows(ROWS);
    renderInbox(Role.Client);
    const labelled = await screen.findByRole("button", { name: "Conversation with Marco Ink" });
    await within(labelled).findByText("Thu 3 Dec, 14:30 · Fine line");

    const plain = screen.getByRole("button", { name: "Conversation with Ana Lines" });

    expect(within(plain).queryByText(/appointment:/i)).not.toBeInTheDocument();
    expect(plain).not.toHaveAttribute("aria-describedby");
    // Same structure as today: the name line and the preview line, nothing else.
    expect(plain.querySelector(".flex-1")?.children).toHaveLength(2);
  });

  it("puts no link inside a row — the row is already a button", async () => {
    useRows(ROWS);
    renderInbox(Role.Client);
    const labelled = await screen.findByRole("button", { name: "Conversation with Marco Ink" });
    await within(labelled).findByText("Thu 3 Dec, 14:30 · Fine line");

    expect(within(labelled).queryByRole("link")).not.toBeInTheDocument();
  });
});
