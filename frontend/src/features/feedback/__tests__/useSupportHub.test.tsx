import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, act, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { configureStore, type EnhancedStore } from "@reduxjs/toolkit";
import React from "react";

import authReducer from "@/features/auth/authSlice";
import { Role } from "@/shared/types/roles";
import { feedbackApi } from "../feedbackApi";
import { useSupportHub } from "../useSupportHub";

const { mockOn, mockStart, mockInvoke, mockStop, eventHandlers } = vi.hoisted(() => {
  const eventHandlers: Record<string, () => void> = {};
  return {
    eventHandlers,
    mockStop:   vi.fn().mockResolvedValue(undefined),
    mockInvoke: vi.fn().mockResolvedValue(undefined),
    mockStart:  vi.fn().mockResolvedValue(undefined),
    mockOn:     vi.fn((event: string, handler: () => void) => {
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
      on:     mockOn,
      start:  mockStart,
      invoke: mockInvoke,
      stop:   mockStop,
    }));
  }
  return { HubConnectionBuilder, LogLevel: { Warning: 2 } };
});

// eslint-disable-next-line @typescript-eslint/no-explicit-any
function makeStore(): EnhancedStore<any> {
  return configureStore({
    reducer: { auth: authReducer, [feedbackApi.reducerPath]: feedbackApi.reducer },
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    middleware: (gd) => gd().concat(feedbackApi.middleware) as any,
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "owner@test.com" }, token: "fake-jwt", tenantId: "t1", role: Role.Owner } as any,
    },
  });
}

function renderSupportHub(store: ReturnType<typeof makeStore>, feedbackReportId: string | null) {
  const wrapper = ({ children }: { children: React.ReactNode }) => (
    <Provider store={store}>{children}</Provider>
  );
  return renderHook(({ id }) => useSupportHub(id), {
    wrapper,
    initialProps: { id: feedbackReportId },
  });
}

describe("useSupportHub", () => {
  beforeEach(() => {
    for (const key of Object.keys(eventHandlers)) delete eventHandlers[key];
    mockOn.mockClear();
    mockStart.mockClear();
    mockInvoke.mockClear();
    mockStop.mockClear();
  });

  afterEach(() => {
    cleanup();
    vi.clearAllMocks();
  });

  it("does not connect when feedbackReportId is null", () => {
    renderSupportHub(makeStore(), null);
    expect(mockStart).not.toHaveBeenCalled();
  });

  it("connects and joins the ticket group on mount", async () => {
    renderSupportHub(makeStore(), "fb-123");
    await act(async () => { /* flush */ });

    expect(mockStart).toHaveBeenCalledTimes(1);
    expect(mockInvoke).toHaveBeenCalledWith("JoinTicket", "fb-123");
  });

  it("stops the connection on unmount", async () => {
    const { unmount } = renderSupportHub(makeStore(), "fb-123");
    await act(async () => { /* flush */ });

    unmount();
    await act(async () => { /* flush cleanup */ });

    expect(mockStop).toHaveBeenCalledTimes(1);
  });

  it("reconnects to a new ticket group when feedbackReportId changes", async () => {
    const store = makeStore();
    const { rerender } = renderSupportHub(store, "fb-123");
    await act(async () => { /* flush */ });
    expect(mockInvoke).toHaveBeenCalledWith("JoinTicket", "fb-123");

    rerender({ id: "fb-456" });
    await act(async () => { /* flush */ });

    expect(mockStop).toHaveBeenCalled();
    expect(mockInvoke).toHaveBeenCalledWith("JoinTicket", "fb-456");
  });

  it("SupportMessageReceived invalidates the FeedbackMessage tag for that ticket", async () => {
    const store = makeStore();
    const dispatchSpy = vi.spyOn(store, "dispatch");
    renderSupportHub(store, "fb-123");
    await act(async () => { /* flush */ });
    dispatchSpy.mockClear();

    act(() => { eventHandlers["SupportMessageReceived"](); });

    const calls = dispatchSpy.mock.calls as unknown as [unknown][];
    const invalidations = calls
      .map(([a]) => a as ReturnType<typeof feedbackApi.util.invalidateTags>)
      .filter((a: ReturnType<typeof feedbackApi.util.invalidateTags>) => a.type === feedbackApi.util.invalidateTags.type);

    expect(invalidations).toHaveLength(1);
    expect(invalidations[0].payload).toEqual([{ type: "FeedbackMessage", id: "fb-123" }]);
  });
});
