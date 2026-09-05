import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { messagingApi } from "../../messagingApi";
import { NewConversationDialog } from "../NewConversationDialog";
import type { ConversationContactResponse } from "../../messaging.types";
import { Role } from "@/shared/types/roles";

const CONTACTS: ConversationContactResponse[] = [
  { userId: "u2", role: "artist", displayName: "Marco Ink", avatarUrl: null, existingConversationId: "conv-1" },
  { userId: "u3", role: "owner", displayName: "Studio Owner", avatarUrl: null, existingConversationId: null },
];

let createConversationCalls = 0;

const server = setupServer(
  http.get("http://localhost/api/v1/conversations/contacts", () =>
    HttpResponse.json(CONTACTS),
  ),
  http.post("http://localhost/api/v1/conversations", () => {
    createConversationCalls += 1;
    return HttpResponse.json({
      id: "conv-2", otherUserId: "u3", otherRole: "owner", otherDisplayName: "Studio Owner",
      otherAvatarUrl: null, lastMessageAt: null, lastMessagePreview: null,
      lastMessageFromMe: false, unreadCount: 0, createdAt: "2026-08-01T00:00:00Z",
    });
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); createConversationCalls = 0; });
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

function renderDialog(onConversationSelected = vi.fn()) {
  render(
    <Provider store={makeStore()}>
      <NewConversationDialog open onOpenChange={vi.fn()} onConversationSelected={onConversationSelected} />
    </Provider>,
  );
  return { onConversationSelected };
}

describe("NewConversationDialog", () => {
  it("shows contacts from getContacts", async () => {
    renderDialog();

    expect(await screen.findByText("Marco Ink")).toBeInTheDocument();
    expect(await screen.findByText("Studio Owner")).toBeInTheDocument();
  });

  it("selecting a contact with an existing conversation navigates directly, without calling createConversation", async () => {
    const user = userEvent.setup();
    const { onConversationSelected } = renderDialog();

    await user.click(await screen.findByText("Marco Ink"));

    await waitFor(() => expect(onConversationSelected).toHaveBeenCalledWith("conv-1"));
    expect(createConversationCalls).toBe(0);
  });

  it("selecting a contact with no existing conversation calls createConversation", async () => {
    const user = userEvent.setup();
    const { onConversationSelected } = renderDialog();

    await user.click(await screen.findByText("Studio Owner"));

    await waitFor(() => expect(onConversationSelected).toHaveBeenCalledWith("conv-2"));
    expect(createConversationCalls).toBe(1);
  });
});
