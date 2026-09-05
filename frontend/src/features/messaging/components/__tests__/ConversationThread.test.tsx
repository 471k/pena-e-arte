import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { toast } from "sonner";

import authReducer from "@/features/auth/authSlice";
import { messagingApi } from "../../messagingApi";
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

function makeStore() {
  return configureStore({
    reducer: { auth: authReducer, [messagingApi.reducerPath]: messagingApi.reducer },
    middleware: (gd) => gd().concat(messagingApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "client@test.com" }, token: "fake", tenantId: "t1", role: Role.Client } as any,
    },
  });
}

function renderThread() {
  return render(
    <Provider store={makeStore()}>
      <ConversationThread conversation={CONVERSATION} />
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
