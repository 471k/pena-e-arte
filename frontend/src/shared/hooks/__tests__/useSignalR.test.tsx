import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, act, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { configureStore, type EnhancedStore } from "@reduxjs/toolkit";
import React from "react";

import authReducer from "@/features/auth/authSlice";
import { Role } from "@/shared/types/roles";
import notificationsReducer from "@/features/notifications/notificationsSlice";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import { designsApi } from "@/features/designs/designsApi";
import { notificationsApi } from "@/features/notifications/notificationsApi";
import { useSignalR } from "../useSignalR";

// ── SignalR mock ───────────────────────────────────────────────────────────────
// vi.mock is hoisted above imports by Vitest's static analysis, so variables
// defined with vi.hoisted() are guaranteed to exist when the factory runs.

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
  // HubConnectionBuilder is used with `new`, so the mock must be a constructor.
  function HubConnectionBuilder(this: Record<string, unknown>) {
    this.withUrl               = vi.fn().mockReturnValue(this);
    this.withAutomaticReconnect = vi.fn().mockReturnValue(this);
    this.configureLogging      = vi.fn().mockReturnValue(this);
    this.build                 = vi.fn(() => ({
      on:     mockOn,
      start:  mockStart,
      invoke: mockInvoke,
      stop:   mockStop,
    }));
  }

  return {
    HubConnectionBuilder,
    LogLevel: { Warning: 2 },
  };
});

// ── Store helper ───────────────────────────────────────────────────────────────

// eslint-disable-next-line @typescript-eslint/no-explicit-any
function makeStore(): EnhancedStore<any> {
  return configureStore({
    reducer: {
      auth:                              authReducer,
      notifications:                     notificationsReducer,
      [appointmentsApi.reducerPath]:     appointmentsApi.reducer,
      [designsApi.reducerPath]:          designsApi.reducer,
      [notificationsApi.reducerPath]:    notificationsApi.reducer,
    },
    middleware: (gd) =>
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      gd().concat(
        appointmentsApi.middleware,
        designsApi.middleware,
        notificationsApi.middleware,
      ) as any,
    preloadedState: {
      auth: {
        user:                { id: "u1", email: "owner@test.com" },
        token:               "fake-jwt-token",
        tenantId:            "studio-0001",
        role:                Role.Owner,
        pendingReferralCode: null,
      },
    },
  });
}

function renderSignalR(store: ReturnType<typeof makeStore>, studioId: string | null) {
  const wrapper = ({ children }: { children: React.ReactNode }) => (
    <Provider store={store}>{children}</Provider>
  );
  return renderHook(() => useSignalR(studioId), { wrapper });
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("useSignalR", () => {
  let store: ReturnType<typeof makeStore>;
  let dispatchSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    for (const key of Object.keys(eventHandlers)) delete eventHandlers[key];
    mockOn.mockClear();
    mockStart.mockClear();
    mockInvoke.mockClear();
    mockStop.mockClear();

    store       = makeStore();
    dispatchSpy = vi.spyOn(store, "dispatch");
  });

  afterEach(() => {
    cleanup();
    vi.clearAllMocks();
  });

  // ── Connection lifecycle ─────────────────────────────────────────────────────

  it("does not open a connection when studioId is null", () => {
    renderSignalR(store, null);
    expect(mockStart).not.toHaveBeenCalled();
  });

  it("does not open a connection when token is absent", () => {
    const noAuthStore = configureStore({
      reducer: {
        auth:                           authReducer,
        [appointmentsApi.reducerPath]:  appointmentsApi.reducer,
        [designsApi.reducerPath]:       designsApi.reducer,
        [notificationsApi.reducerPath]: notificationsApi.reducer,
      },
      preloadedState: {
        auth: { user: null, token: null, tenantId: null, role: null, pendingReferralCode: null },
      },
    });

    const wrapper = ({ children }: { children: React.ReactNode }) => (
      <Provider store={noAuthStore}>{children}</Provider>
    );
    renderHook(() => useSignalR("studio-0001"), { wrapper });

    expect(mockStart).not.toHaveBeenCalled();
  });

  it("opens a connection and joins the studio when studioId and token are present", async () => {
    renderSignalR(store, "studio-0001");
    await act(async () => { /* flush microtasks */ });

    // Three hubs: schedule, design, notification
    expect(mockStart).toHaveBeenCalledTimes(3);
    expect(mockInvoke).toHaveBeenCalledWith("JoinStudio", "studio-0001");
  });

  // ── DesignUploaded — the critical regression guard ──────────────────────────

  it("DesignUploaded dispatches designsApi tag invalidation", async () => {
    renderSignalR(store, "studio-0001");
    await act(async () => { /* flush connection start */ });
    dispatchSpy.mockClear();

    act(() => { eventHandlers["DesignUploaded"](); });

    const types = dispatchSpy.mock.calls.map(([a]: [unknown]) =>
      (a as { type: string }).type,
    );
    expect(types).toContain(designsApi.util.invalidateTags.type);
  });

  it("DesignUploaded does NOT dispatch appointmentsApi tag invalidation", async () => {
    renderSignalR(store, "studio-0001");
    await act(async () => { /* flush connection start */ });
    dispatchSpy.mockClear();

    act(() => { eventHandlers["DesignUploaded"](); });

    const types = dispatchSpy.mock.calls.map(([a]: [unknown]) =>
      (a as { type: string }).type,
    );
    expect(types).not.toContain(appointmentsApi.util.invalidateTags.type);
  });

  it("DesignUploaded does NOT dispatch notificationsApi tag invalidation", async () => {
    renderSignalR(store, "studio-0001");
    await act(async () => { /* flush connection start */ });
    dispatchSpy.mockClear();

    act(() => { eventHandlers["DesignUploaded"](); });

    const types = dispatchSpy.mock.calls.map(([a]: [unknown]) =>
      (a as { type: string }).type,
    );
    expect(types).not.toContain(notificationsApi.util.invalidateTags.type);
  });

  // ── Appointment events ───────────────────────────────────────────────────────

  it.each([
    "AppointmentCreated",
    "AppointmentConfirmed",
    "AppointmentCompleted",
    "AppointmentNoShow",
    "AppointmentCancelled",
  ])("%s dispatches appointmentsApi tag invalidation", async (event) => {
    renderSignalR(store, "studio-0001");
    await act(async () => { /* flush */ });
    dispatchSpy.mockClear();

    act(() => { eventHandlers[event](); });

    const types = dispatchSpy.mock.calls.map(([a]: [unknown]) =>
      (a as { type: string }).type,
    );
    expect(types).toContain(appointmentsApi.util.invalidateTags.type);
  });

  it.each([
    "AppointmentCreated",
    "AppointmentConfirmed",
    "AppointmentCompleted",
    "AppointmentNoShow",
    "AppointmentCancelled",
  ])("%s does NOT dispatch designsApi tag invalidation", async (event) => {
    renderSignalR(store, "studio-0001");
    await act(async () => { /* flush */ });
    dispatchSpy.mockClear();

    act(() => { eventHandlers[event](); });

    const types = dispatchSpy.mock.calls.map(([a]: [unknown]) =>
      (a as { type: string }).type,
    );
    expect(types).not.toContain(designsApi.util.invalidateTags.type);
  });

  // ── Notification event ───────────────────────────────────────────────────────

  it("NotificationReceived dispatches notificationsApi tag invalidation", async () => {
    renderSignalR(store, "studio-0001");
    await act(async () => { /* flush */ });
    dispatchSpy.mockClear();

    act(() => { eventHandlers["NotificationReceived"](); });

    const types = dispatchSpy.mock.calls.map(([a]: [unknown]) =>
      (a as { type: string }).type,
    );
    expect(types).toContain(notificationsApi.util.invalidateTags.type);
  });

  it("NotificationReceived does NOT dispatch designsApi tag invalidation", async () => {
    renderSignalR(store, "studio-0001");
    await act(async () => { /* flush */ });
    dispatchSpy.mockClear();

    act(() => { eventHandlers["NotificationReceived"](); });

    const types = dispatchSpy.mock.calls.map(([a]: [unknown]) =>
      (a as { type: string }).type,
    );
    expect(types).not.toContain(designsApi.util.invalidateTags.type);
  });

  it("NotificationReceived increments the unread notification count", async () => {
    renderSignalR(store, "studio-0001");
    await act(async () => { /* flush */ });

    expect(store.getState().notifications.unreadCount).toBe(0);

    act(() => { eventHandlers["NotificationReceived"](); });

    expect(store.getState().notifications.unreadCount).toBe(1);
  });

  it("NotificationReceived increments the unread count once per event", async () => {
    renderSignalR(store, "studio-0001");
    await act(async () => { /* flush */ });

    act(() => {
      eventHandlers["NotificationReceived"]();
      eventHandlers["NotificationReceived"]();
      eventHandlers["NotificationReceived"]();
    });

    expect(store.getState().notifications.unreadCount).toBe(3);
  });

  it("AppointmentCreated does NOT increment the unread notification count", async () => {
    renderSignalR(store, "studio-0001");
    await act(async () => { /* flush */ });

    act(() => { eventHandlers["AppointmentCreated"](); });

    expect(store.getState().notifications.unreadCount).toBe(0);
  });

  // ── Correct invalidation payload ─────────────────────────────────────────────

  it("DesignUploaded invalidates the [Design] tag specifically", async () => {
    renderSignalR(store, "studio-0001");
    await act(async () => { /* flush */ });
    dispatchSpy.mockClear();

    act(() => { eventHandlers["DesignUploaded"](); });

    const designInvalidations = dispatchSpy.mock.calls
      .map(([a]: [unknown]) => a as ReturnType<typeof designsApi.util.invalidateTags>)
      .filter((a: ReturnType<typeof designsApi.util.invalidateTags>) => a.type === designsApi.util.invalidateTags.type);

    expect(designInvalidations).toHaveLength(1);
    expect(designInvalidations[0].payload).toEqual(["Design"]);
  });

  it("AppointmentCreated invalidates the [Appointment] tag specifically", async () => {
    renderSignalR(store, "studio-0001");
    await act(async () => { /* flush */ });
    dispatchSpy.mockClear();

    act(() => { eventHandlers["AppointmentCreated"](); });

    const apptInvalidations = dispatchSpy.mock.calls
      .map(([a]: [unknown]) => a as ReturnType<typeof appointmentsApi.util.invalidateTags>)
      .filter((a: ReturnType<typeof appointmentsApi.util.invalidateTags>) => a.type === appointmentsApi.util.invalidateTags.type);

    expect(apptInvalidations).toHaveLength(1);
    expect(apptInvalidations[0].payload).toEqual(["Appointment"]);
  });
});
