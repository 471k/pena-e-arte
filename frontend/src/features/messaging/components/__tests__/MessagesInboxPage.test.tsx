import { describe, it, expect, vi, beforeAll, beforeEach, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
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
  const store = makeStore(role);
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/messages"]}>
        <MessagesInboxPage />
      </MemoryRouter>
    </Provider>,
  );
  return store;
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
  // A fixed clock (clock-flake class): the label adds the year when the appointment isn't in the current
  // year, so with the real clock these 3 Dec 2026 fixtures would render "Thu 3 Dec 2026, 14:30" from
  // 1 Jan 2027. Found by the clock sweep (.github/workflows/clock-sweep.yml).
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-11-04T12:00:00Z"));
  });
  afterEach(() => { vi.useRealTimers(); });
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

describe("MessagesInboxPage — drafts and load failures (S07)", () => {
  const conv = (id: string, name: string): ConversationResponse => ({
    id, otherUserId: `u-${id}`, otherRole: "artist", otherDisplayName: name, otherAvatarUrl: null,
    lastMessageAt: "2026-11-01T10:00:00Z", lastMessagePreview: "Hello", lastMessageFromMe: false,
    unreadCount: 0, createdAt: "2026-10-01T00:00:00Z",
  });
  const TWO = [conv("c1", "Marco Ink"), conv("c2", "Ana Lines")];

  function useThreadEndpoints(posted: (conversationId: string, body: string) => void) {
    server.use(
      http.get("http://localhost/api/v1/conversations/:id/messages", () => HttpResponse.json([])),
      http.post("http://localhost/api/v1/conversations/:id/read", () => new HttpResponse(null, { status: 204 })),
      http.post("http://localhost/api/v1/conversations/:id/messages", async ({ request, params }) => {
        const body = (await request.json()) as { body: string };
        posted(String(params.id), body.body);
        return HttpResponse.json({}, { status: 201 });
      }),
    );
  }

  it("never carries a draft typed for one person into another person's composer", async () => {
    const user = userEvent.setup();
    const posted = vi.fn();
    server.use(http.get("http://localhost/api/v1/conversations", () => HttpResponse.json(TWO)));
    useThreadEndpoints(posted);
    renderInbox(Role.Client);

    await user.click(await screen.findByRole("button", { name: "Conversation with Marco Ink" }));
    await user.type(await screen.findByRole("textbox", { name: "Message to Marco Ink" }), "Only for Marco");

    await user.click(screen.getByRole("button", { name: "Conversation with Ana Lines" }));
    const anasComposer = await screen.findByRole("textbox", { name: "Message to Ana Lines" });

    expect(anasComposer).toHaveValue("");
    expect(screen.queryByRole("textbox", { name: "Message to Marco Ink" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /send message/i })).toBeDisabled();
    // Enter in Ana's composer sends nothing — Marco's text is gone, not waiting to be delivered.
    await user.type(anasComposer, "{Enter}");
    expect(posted).not.toHaveBeenCalled();
  });

  it("shows a failed inbox load as an error with a working retry, never as an empty inbox", async () => {
    const user = userEvent.setup();
    let calls = 0;
    server.use(http.get("http://localhost/api/v1/conversations", () => {
      calls += 1;
      return calls === 1 ? new HttpResponse(null, { status: 500 }) : HttpResponse.json(TWO);
    }));
    renderInbox(Role.Client);

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Couldn't load your conversations.");
    expect(screen.queryByText(/no conversations yet/i)).not.toBeInTheDocument();

    await user.click(within(alert).getByRole("button", { name: /try again/i }));

    expect(await screen.findByRole("button", { name: "Conversation with Marco Ink" })).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.queryByText(/no conversations yet/i)).not.toBeInTheDocument();
  });

  it("keeps the last list after a failed refresh and says it may be out of date", async () => {
    server.use(http.get("http://localhost/api/v1/conversations", () => HttpResponse.json(TWO)));
    const store = renderInbox(Role.Owner);
    await screen.findByRole("button", { name: "Conversation with Marco Ink" });

    server.use(http.get("http://localhost/api/v1/conversations", () => new HttpResponse(null, { status: 500 })));
    store.dispatch(messagingApi.util.invalidateTags(["Conversation"]));

    expect(await screen.findByRole("alert")).toHaveTextContent(/couldn't refresh your conversations/i);
    expect(screen.getByRole("button", { name: "Conversation with Ana Lines" })).toBeInTheDocument();
    expect(screen.queryByText(/no conversations yet/i)).not.toBeInTheDocument();
  });

  it("still shows the true empty state when the inbox really is empty", async () => {
    renderInbox(Role.Artist);

    expect(await screen.findByText(/no conversations yet/i)).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole("alert")).not.toBeInTheDocument());
  });
});