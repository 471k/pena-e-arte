import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import authReducer from "@/features/auth/authSlice";
import { useTrafficBeacon } from "../useTrafficBeacon";

function renderBeacon(token: string | null = null) {
  const store = configureStore({
    reducer: { auth: authReducer },
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: null, token, refreshToken: null, tenantId: null, role: null } as any,
    },
  });
  return renderHook(() => useTrafficBeacon(), {
    wrapper: ({ children }) => <Provider store={store}>{children}</Provider>,
  });
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const { mockSubscribe, listeners, routerState } = vi.hoisted(() => {
  const listeners: Array<(state: any) => void> = [];
  return {
    listeners,
    routerState: { location: { pathname: "/discover" } },
    mockSubscribe: vi.fn((cb: (state: any) => void) => {
      listeners.push(cb);
      return () => {
        const i = listeners.indexOf(cb);
        if (i >= 0) listeners.splice(i, 1);
      };
    }),
  };
});

vi.mock("@/app/router", () => ({
  router: {
    get state() {
      return routerState;
    },
    subscribe: mockSubscribe,
  },
}));

function navigateTo(pathname: string) {
  routerState.location = { pathname };
  listeners.forEach((cb) => cb(routerState));
}

function setVisibility(state: "visible" | "hidden") {
  Object.defineProperty(document, "visibilityState", { value: state, configurable: true });
  document.dispatchEvent(new Event("visibilitychange"));
}

describe("useTrafficBeacon", () => {
  beforeEach(() => {
    listeners.length = 0;
    routerState.location = { pathname: "/discover" };
    localStorage.clear();
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: true }));
    setVisibility("visible");
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("fires a navigation beacon on mount with the current path", () => {
    renderBeacon();

    expect(fetch).toHaveBeenCalledWith(
      "/api/v1/public/traffic/beacon",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ path: "/discover", isNavigation: true }),
      }),
    );
  });

  it("sends the visitor id as a header, generating and persisting one if absent", () => {
    renderBeacon();

    const call = (fetch as ReturnType<typeof vi.fn>).mock.calls[0];
    const headers = call[1].headers as Record<string, string>;
    expect(headers["X-Visitor-Id"]).toBeTruthy();
    expect(localStorage.getItem("pea_visitor_id")).toBe(headers["X-Visitor-Id"]);
  });

  // Regression coverage for the bug caught in code review: without this header, the backend
  // never sees an authenticated caller at all, so every signed-in visitor is recorded as a Guest.
  it("attaches an Authorization header when a token is present", () => {
    renderBeacon("fake-jwt-token");

    const call = (fetch as ReturnType<typeof vi.fn>).mock.calls[0];
    const headers = call[1].headers as Record<string, string>;
    expect(headers.Authorization).toBe("Bearer fake-jwt-token");
  });

  it("sends no Authorization header for an unauthenticated (guest) visitor", () => {
    renderBeacon(null);

    const call = (fetch as ReturnType<typeof vi.fn>).mock.calls[0];
    const headers = call[1].headers as Record<string, string>;
    expect(headers.Authorization).toBeUndefined();
  });

  it("fires a new navigation beacon on route change", () => {
    renderBeacon();
    (fetch as ReturnType<typeof vi.fn>).mockClear();

    navigateTo("/s/ink-society");

    expect(fetch).toHaveBeenCalledWith(
      "/api/v1/public/traffic/beacon",
      expect.objectContaining({
        body: JSON.stringify({ path: "/s/ink-society", isNavigation: true }),
      }),
    );
  });

  it("does not re-fire when the path is unchanged", () => {
    renderBeacon();
    (fetch as ReturnType<typeof vi.fn>).mockClear();

    navigateTo("/discover");

    expect(fetch).not.toHaveBeenCalled();
  });

  it("redacts the /share/:token path segment before sending", () => {
    routerState.location = { pathname: "/share/a-live-secret-token" };
    renderBeacon();

    expect(fetch).toHaveBeenCalledWith(
      "/api/v1/public/traffic/beacon",
      expect.objectContaining({
        body: JSON.stringify({ path: "/share/[redacted]", isNavigation: true }),
      }),
    );
  });

  it("sends a heartbeat beacon on the 20s interval while the tab is visible", () => {
    vi.useFakeTimers();
    renderBeacon();
    (fetch as ReturnType<typeof vi.fn>).mockClear();

    vi.advanceTimersByTime(20_000);

    expect(fetch).toHaveBeenCalledWith(
      "/api/v1/public/traffic/beacon",
      expect.objectContaining({
        body: JSON.stringify({ path: "/discover", isNavigation: false }),
      }),
    );
  });

  it("pauses the heartbeat while the tab is hidden", () => {
    vi.useFakeTimers();
    renderBeacon();
    (fetch as ReturnType<typeof vi.fn>).mockClear();

    setVisibility("hidden");
    vi.advanceTimersByTime(60_000);

    expect(fetch).not.toHaveBeenCalled();
  });

  it("resumes the heartbeat when the tab becomes visible again", () => {
    vi.useFakeTimers();
    renderBeacon();
    setVisibility("hidden");
    (fetch as ReturnType<typeof vi.fn>).mockClear();

    setVisibility("visible");
    vi.advanceTimersByTime(20_000);

    expect(fetch).toHaveBeenCalledWith(
      "/api/v1/public/traffic/beacon",
      expect.objectContaining({ body: JSON.stringify({ path: "/discover", isNavigation: false }) }),
    );
  });
});
