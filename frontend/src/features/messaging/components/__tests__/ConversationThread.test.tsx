import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { toast } from "sonner";

import authReducer from "@/features/auth/authSlice";
import { messagingApi } from "../../messagingApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { ConversationThread } from "../ConversationThread";
import type { ConversationResponse, ChatMessageResponse } from "../../messaging.types";
import { Role } from "@/shared/types/roles";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const CONVERSATION: ConversationResponse = {
  id: "conv-1",
  otherUserId: "u2",
  otherRole: "artist",
  otherDisplayName: "Marco Ink",
  otherAvatarUrl: null,
  lastMessageAt: "2026-08-01T10:00:00Z",
  lastMessagePreview: "Hey there",
  lastMessageFromMe: false,
  unreadCount: 0,
  createdAt: "2026-07-01T00:00:00Z",
};

const MESSAGES: ChatMessageResponse[] = [
  {
    id: "m1", conversationId: "conv-1", senderUserId: "u2", senderRole: "artist",
    body: "Hey there", createdAt: "2026-08-01T09:00:00Z", readAt: null,
  },
  {
    id: "m2", conversationId: "conv-1", senderUserId: "u1", senderRole: "client",
    body: "Hi! Looking forward to it", createdAt: "2026-08-01T10:00:00Z", readAt: null,
  },
];

const server = setupServer(
  http.get("http://localhost/api/v1/conversations/:id/messages", () =>
    HttpResponse.json(MESSAGES),
  ),
  http.get("http://localhost/api/v1/studios/me", () =>
    HttpResponse.json({ id: "t1", timezone: "Europe/Tirane" }),
  ),
  http.post("http://localhost/api/v1/conversations/:id/read", () =>
    new HttpResponse(null, { status: 204 }),
  ),
  http.post("http://localhost/api/v1/conversations/:id/messages", async ({ request }) => {
    const body = (await request.json()) as { body: string };
    return HttpResponse.json({
      id: "m3", conversationId: "conv-1", senderUserId: "u1", senderRole: "client",
      body: body.body, createdAt: "2026-08-01T11:00:00Z", readAt: null,
    }, { status: 201 });
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

function makeStore(role: Role = Role.Client) {
  return configureStore({
    reducer: {
      auth: authReducer,
      [messagingApi.reducerPath]: messagingApi.reducer,
      [studiosApi.reducerPath]: studiosApi.reducer,
    },
    middleware: (gd) => gd().concat(messagingApi.middleware, studiosApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "client@test.com" }, token: "fake", tenantId: "t1", role } as any,
    },
  });
}

function renderThread(role: Role = Role.Client) {
  return render(
    <Provider store={makeStore(role)}>
      <MemoryRouter>
        <ConversationThread conversation={CONVERSATION} />
      </MemoryRouter>
    </Provider>,
  );
}

describe("ConversationThread", () => {
  it("renders the current user's own messages on the right, the other participant's on the left", async () => {
    renderThread();

    const mine = await screen.findByText("Hi! Looking forward to it");
    const theirs = await screen.findByText("Hey there");

    expect(mine.closest("div")?.className).toContain("ml-auto");
    expect(theirs.closest("div")?.className).not.toContain("ml-auto");
  });

  it("disables send when the composer is empty or whitespace-only", async () => {
    const user = userEvent.setup();
    renderThread();
    await screen.findByText("Hey there");

    const sendButton = screen.getByRole("button", { name: /send message/i });
    expect(sendButton).toBeDisabled();

    const textarea = screen.getByPlaceholderText(/type a message/i);
    await user.type(textarea, "   ");
    expect(sendButton).toBeDisabled();

    await user.type(textarea, "actual text");
    expect(sendButton).not.toBeDisabled();
  });

  it("enforces the 2000-character limit client-side", async () => {
    renderThread();
    await screen.findByText("Hey there");

    const textarea = screen.getByPlaceholderText(/type a message/i) as HTMLTextAreaElement;
    expect(textarea.maxLength).toBe(2000);
  });

  it("sends a message and clears the composer", async () => {
    const user = userEvent.setup();
    renderThread();
    await screen.findByText("Hey there");

    const textarea = screen.getByPlaceholderText(/type a message/i);
    await user.type(textarea, "New message body");
    await user.click(screen.getByRole("button", { name: /send message/i }));

    await waitFor(() => expect((textarea as HTMLTextAreaElement).value).toBe(""));
    expect(toast.error).not.toHaveBeenCalled();
  });
});

describe("ConversationThread — which appointment a message is about", () => {
  const ABOUT_A = { appointmentId: "appt-a", startsAt: "2026-12-03T13:30:00Z", serviceName: "Fine line" };
  const ABOUT_B = { appointmentId: "appt-b", startsAt: "2026-12-10T08:00:00Z", serviceName: null };
  const msg = (id: string, sender: "u1" | "u2", about: typeof ABOUT_A | typeof ABOUT_B | null): ChatMessageResponse => ({
    id, conversationId: "conv-1", senderUserId: sender, senderRole: sender === "u1" ? "client" : "artist",
    body: `body ${id}`, createdAt: "2026-11-01T10:00:00Z", readAt: null,
    appointmentId: about?.appointmentId ?? null, appointment: about,
  });
  const THREAD: ChatMessageResponse[] = [
    msg("m1", "u1", ABOUT_A), msg("m2", "u1", ABOUT_A), msg("m3", "u2", null), msg("m4", "u1", ABOUT_B),
  ];

  function useThread(messages: ChatMessageResponse[]) {
    server.use(http.get("http://localhost/api/v1/conversations/:id/messages", () => HttpResponse.json(messages)));
  }

  it("shows the label in the studio's timezone, once per run about the same appointment", async () => {
    useThread(THREAD);
    renderThread();

    // Studio-local (Tirana, UTC+1): the timezone loads after the messages, hence findBy.
    expect(await screen.findByText("Thu 3 Dec, 14:30 · Fine line")).toBeInTheDocument();
    expect(screen.getAllByText("Thu 3 Dec, 14:30 · Fine line")).toHaveLength(1);
    // No service: the date and time alone, no empty segment.
    expect(await screen.findByText("Thu 10 Dec, 09:00")).toBeInTheDocument();
    expect(screen.getAllByRole("link")).toHaveLength(2);
  });

  it("links a client to their own appointment page — never the staff route", async () => {
    useThread(THREAD);
    renderThread(Role.Client);

    const link = await screen.findByRole("link", { name: /Thu 3 Dec, 14:30/ });

    expect(link).toHaveAttribute("href", "/my-appointments/appt-a");
    for (const l of screen.getAllByRole("link")) {
      expect(l.getAttribute("href")).not.toMatch(/^\/appointments\//);
    }
  });

  it.each([Role.Artist, Role.Owner])("links %s to the staff appointment page, whoever sent the message", async (role) => {
    // The messages were sent by the client; the destination follows the reader's role.
    useThread(THREAD);
    renderThread(role);

    const link = await screen.findByRole("link", { name: /Thu 3 Dec, 14:30/ });

    expect(link).toHaveAttribute("href", "/appointments/appt-a");
  });

  it("renders no label, link or id for messages that aren't about an appointment", async () => {
    renderThread();
    await screen.findByText("Hey there");

    expect(screen.queryByRole("link")).not.toBeInTheDocument();
    expect(screen.queryByText(/appointment:/i)).not.toBeInTheDocument();
  });

  it("never renders the raw appointment id", async () => {
    useThread(THREAD);
    const { container } = renderThread();
    await screen.findByText("Thu 3 Dec, 14:30 · Fine line");

    expect(container.textContent).not.toMatch(/appt-a|appt-b/);
  });
});
