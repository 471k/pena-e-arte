import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, act, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { configureStore, type EnhancedStore } from "@reduxjs/toolkit";
import React from "react";

import authReducer from "@/features/auth/authSlice";
import { Role } from "@/shared/types/roles";
import { messagingApi } from "../messagingApi";
import { useChatHub } from "../useChatHub";

const { mockOn, mockStart, mockStop, eventHandlers } = vi.hoisted(() => {
  const eventHandlers: Record<string, (...args: unknown[]) => void> = {};
  return {
    eventHandlers,
    mockStop:  vi.fn().mockResolvedValue(undefined),
    mockStart: vi.fn().mockResolvedValue(undefined),
    mockOn:    vi.fn((event: string, handler: (...args: unknown[]) => void) => {
      eventHandlers[event] = handler;
    }),
  };
});

vi.mock("@microsoft/signalr", () => {
  function HubConnectionBuilder(this: Record<string, unknown>) {
    this.withUrl                = vi.fn().mockReturnValue(this);
    this.withAutomaticReconnect = vi.fn().mockReturnValue(this);
    this.configureLogging       = vi.fn().mockReturnValue(this);
    this.build                  = vi.fn(() => ({
      on:    mockOn,
      start: mockStart,
      stop:  mockStop,
    }));
  }
  return { HubConnectionBuilder, LogLevel: { Warning: 2 } };
});

// eslint-disable-next-line @typescript-eslint/no-explicit-any
function makeStore(): EnhancedStore<any> {
  return configureStore({
    reducer: { auth: authReducer, [messagingApi.reducerPath]: messagingApi.reducer },
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    middleware: (gd) => gd().concat(messagingApi.middleware) as any,
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "client@test.com" }, token: "fake-jwt", tenantId: "t1", role: Role.Client } as any,
    },
  });
}

function renderChatHub(store: ReturnType<typeof makeStore>) {
  const wrapper = ({ children }: { children: React.ReactNode }) => (
    <Provider store={store}>{children}</Provider>
  );
  return renderHook(() => useChatHub(), { wrapper });
}

describe("useChatHub", () => {
  beforeEach(() => {
    for (const key of Object.keys(eventHandlers)) delete eventHandlers[key];
    mockOn.mockClear();
    mockStart.mockClear();
    mockStop.mockClear();
  });

  afterEach(() => {
    cleanup();
    vi.clearAllMocks();
  });

  it("connects to the chat hub on mount", async () => {
    renderChatHub(makeStore());
    await act(async () => { /* flush */ });

    expect(mockStart).toHaveBeenCalledTimes(1);
  });

  it("stops the connection on unmount", async () => {
    const { unmount } = renderChatHub(makeStore());
    await act(async () => { /* flush */ });

    unmount();
    await act(async () => { /* flush cleanup */ });

    expect(mockStop).toHaveBeenCalledTimes(1);
  });

  it("MessageReceived authored by someone else invalidates Messages/Conversation/UnreadCount", async () => {
    const store = makeStore();
    const dispatchSpy = vi.spyOn(store, "dispatch");
    renderChatHub(store);
    await act(async () => { /* flush */ });
    dispatchSpy.mockClear();

    act(() => {
      eventHandlers["MessageReceived"]({ senderUserId: "someone-else", conversationId: "conv-1" });
    });

    const calls = dispatchSpy.mock.calls as unknown as [unknown][];
    const invalidations = calls
      .map(([a]) => a as ReturnType<typeof messagingApi.util.invalidateTags>)
      .filter((a) => a?.type === messagingApi.util.invalidateTags.type);

    expect(invalidations).toHaveLength(1);
    expect(invalidations[0].payload).toEqual([
      { type: "Messages", id: "conv-1" }, "Conversation", "UnreadCount",
    ]);
  });

  it("MessageReceived echoing the current user's own message does NOT invalidate again", async () => {
    const store = makeStore(); // current user id is "u1"
    const dispatchSpy = vi.spyOn(store, "dispatch");
    renderChatHub(store);
    await act(async () => { /* flush */ });
    dispatchSpy.mockClear();

    act(() => {
      eventHandlers["MessageReceived"]({ senderUserId: "u1", conversationId: "conv-1" });
    });

    const calls = dispatchSpy.mock.calls as unknown as [unknown][];
    const invalidations = calls
      .map(([a]) => a as ReturnType<typeof messagingApi.util.invalidateTags>)
      .filter((a) => a?.type === messagingApi.util.invalidateTags.type);

    expect(invalidations).toHaveLength(0);
  });

  it("ConversationRead invalidates that conversation's Messages tag, not just Conversation", async () => {
    const store = makeStore();
    const dispatchSpy = vi.spyOn(store, "dispatch");
    renderChatHub(store);
    await act(async () => { /* flush */ });
    dispatchSpy.mockClear();

    act(() => {
      eventHandlers["ConversationRead"]({ id: "conv-1", readByUserId: "someone-else" });
    });

    const calls = dispatchSpy.mock.calls as unknown as [unknown][];
    const invalidations = calls
      .map(([a]) => a as ReturnType<typeof messagingApi.util.invalidateTags>)
      .filter((a) => a?.type === messagingApi.util.invalidateTags.type);

    expect(invalidations).toHaveLength(1);
    expect(invalidations[0].payload).toEqual([
      { type: "Messages", id: "conv-1" }, "Conversation",
    ]);
  });
});
