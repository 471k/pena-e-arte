import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { baseQuery } from "../baseQuery";

const server = setupServer(
  http.get("http://localhost/api/v1/ok", () => HttpResponse.json({ hello: "world" })),
  http.get("http://localhost/api/v1/read-only", () =>
    HttpResponse.json({ message: "Your studio is in read-only mode." }, { status: 402 }),
  ),
  http.get("http://localhost/api/v1/suspended", () =>
    HttpResponse.json({ code: "STUDIO_SUSPENDED", message: "Studio suspended." }, { status: 403 }),
  ),
  http.get("http://localhost/api/v1/plan-limit", () =>
    HttpResponse.json(
      { code: "PLAN_LIMIT_EXCEEDED", message: "This studio's plan allows up to 6 artists. Upgrade the plan to continue." },
      { status: 403 },
    ),
  ),
  http.get("http://localhost/api/v1/other-403", () =>
    HttpResponse.json({ message: "Some other forbidden error." }, { status: 403 }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

function makeStore() {
  return configureStore({
    reducer: { auth: authReducer, ui: uiReducer },
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: null, token: "fake-token", tenantId: null, role: null, refreshToken: null } as any,
    },
  });
}

// Minimal fake BaseQueryApi — enough surface for baseQuery.ts's own usage
// (api.getState() for auth.refreshToken, api.dispatch(...) for the ui-slice actions).
function makeFakeApi(store: ReturnType<typeof makeStore>) {
  return {
    signal: new AbortController().signal,
    abort: () => {},
    dispatch: store.dispatch,
    getState: store.getState,
    extra: undefined,
    endpoint: "test",
    type: "query" as const,
    forced: false,
  };
}

describe("baseQuery", () => {
  it("passes through a successful response without dispatching anything", async () => {
    const store = makeStore();
    const result = await baseQuery("ok", makeFakeApi(store), {});

    expect(result.data).toEqual({ hello: "world" });
    expect(store.getState().ui.readOnlyError).toBeNull();
    expect(store.getState().ui.studioSuspended).toBe(false);
    expect(store.getState().ui.planLimitError).toBeNull();
  });

  it("402 dispatches setReadOnlyError with the backend message", async () => {
    const store = makeStore();
    await baseQuery("read-only", makeFakeApi(store), {});

    expect(store.getState().ui.readOnlyError).toBe("Your studio is in read-only mode.");
  });

  it("403 STUDIO_SUSPENDED dispatches setStudioSuspended", async () => {
    const store = makeStore();
    await baseQuery("suspended", makeFakeApi(store), {});

    expect(store.getState().ui.studioSuspended).toBe(true);
    expect(store.getState().ui.planLimitError).toBeNull();
  });

  it("403 PLAN_LIMIT_EXCEEDED dispatches setPlanLimitError with the backend message", async () => {
    const store = makeStore();
    await baseQuery("plan-limit", makeFakeApi(store), {});

    expect(store.getState().ui.planLimitError).toBe(
      "This studio's plan allows up to 6 artists. Upgrade the plan to continue.",
    );
    expect(store.getState().ui.studioSuspended).toBe(false);
  });

  it("403 with an unrecognized code dispatches nothing", async () => {
    const store = makeStore();
    await baseQuery("other-403", makeFakeApi(store), {});

    expect(store.getState().ui.studioSuspended).toBe(false);
    expect(store.getState().ui.planLimitError).toBeNull();
  });
});

// ── Session renewal ───────────────────────────────────────────────────────────

const ROLE_CLAIM = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
const b64url = (s: string) => btoa(s).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
function jwt(expOffsetSeconds: number, sub = "user-0001"): string {
  const claims = {
    sub, email: "owner@test.com", tenant_id: "studio-0001", [ROLE_CLAIM]: "owner",
    exp: Math.floor(Date.now() / 1000) + expOffsetSeconds,
  };
  return `${b64url(JSON.stringify({ alg: "HS256", typ: "JWT" }))}.${b64url(JSON.stringify(claims))}.sig`;
}

const EXPIRED = jwt(-600);
const RENEWED = jwt(900, "user-0001-renewed");
const OTHER_TAB = jwt(900, "user-0001-other-tab");

let refreshCalls = 0;
function useRenewalHandlers(refreshAccepts: string | null) {
  refreshCalls = 0;
  server.use(
    http.post("http://localhost/api/v1/auth/refresh", async ({ request }) => {
      refreshCalls++;
      const body = (await request.json()) as { refreshToken: string };
      return body.refreshToken === refreshAccepts
        ? HttpResponse.json({ accessToken: RENEWED, refreshToken: "rt-new", tokenType: "Bearer" })
        : HttpResponse.json({ message: "Invalid refresh token." }, { status: 422 });
    }),
    http.get("http://localhost/api/v1/protected", ({ request }) => {
      const auth = request.headers.get("Authorization");
      return auth === `Bearer ${RENEWED}` || auth === `Bearer ${OTHER_TAB}`
        ? HttpResponse.json({ ok: true })
        : HttpResponse.json({ message: "Unauthorized" }, { status: 401 });
    }),
  );
}

function makeSignedInStore(token: string, refreshToken: string) {
  return configureStore({
    reducer: { auth: authReducer, ui: uiReducer },
    preloadedState: {
      auth: {
        user: { id: "user-0001", email: "owner@test.com", tenantId: "studio-0001", role: "owner" },
        token, refreshToken, tenantId: "studio-0001", role: "owner",
        pendingReferralCode: null, impersonation: null,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      } as any,
    },
  });
}

describe("baseQuery — session renewal", () => {
  afterEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  it("renews an expired access token BEFORE sending, so the request succeeds first time", async () => {
    useRenewalHandlers("rt-old");
    localStorage.setItem("auth_token", EXPIRED);
    localStorage.setItem("auth_refresh_token", "rt-old");
    const store = makeSignedInStore(EXPIRED, "rt-old");

    const result = await baseQuery("protected", makeFakeApi(store), {});

    expect(result.data).toEqual({ ok: true });
    expect(refreshCalls).toBe(1);
    expect(store.getState().auth.token).toBe(RENEWED);
    expect(localStorage.getItem("auth_refresh_token")).toBe("rt-new");
    expect(store.getState().ui.sessionExpired).toBe(false);
  });

  it("several requests at once with an expired token renew it only once", async () => {
    useRenewalHandlers("rt-old");
    localStorage.setItem("auth_token", EXPIRED);
    localStorage.setItem("auth_refresh_token", "rt-old");
    const store = makeSignedInStore(EXPIRED, "rt-old");
    const api = makeFakeApi(store);

    const results = await Promise.all([
      baseQuery("protected", api, {}), baseQuery("protected", api, {}), baseQuery("protected", api, {}),
    ]);

    expect(results.every((r) => r.error === undefined)).toBe(true);
    expect(refreshCalls).toBe(1);
  });

  it("keeps renewed tokens in sessionStorage when 'remember me' was off", async () => {
    useRenewalHandlers("rt-old");
    sessionStorage.setItem("auth_token", EXPIRED);
    sessionStorage.setItem("auth_refresh_token", "rt-old");
    const store = makeSignedInStore(EXPIRED, "rt-old");

    await baseQuery("protected", makeFakeApi(store), {});

    expect(sessionStorage.getItem("auth_refresh_token")).toBe("rt-new");
    expect(localStorage.getItem("auth_token")).toBeNull();
  });

  it("takes the session another tab already renewed instead of renewing with a stale token", async () => {
    useRenewalHandlers("rt-current");
    localStorage.setItem("auth_token", OTHER_TAB);
    localStorage.setItem("auth_refresh_token", "rt-other-tab");
    const store = makeSignedInStore(EXPIRED, "rt-old");

    const result = await baseQuery("protected", makeFakeApi(store), {});

    expect(result.data).toEqual({ ok: true });
    expect(refreshCalls).toBe(0);
    expect(store.getState().auth.token).toBe(OTHER_TAB);
    expect(store.getState().ui.sessionExpired).toBe(false);
  });

  it("signs out and flags the session as expired when renewal is refused", async () => {
    useRenewalHandlers("rt-something-else");
    localStorage.setItem("auth_token", EXPIRED);
    localStorage.setItem("auth_refresh_token", "rt-old");
    const store = makeSignedInStore(EXPIRED, "rt-old");

    const result = await baseQuery("protected", makeFakeApi(store), {});

    expect(result.error?.status).toBe(401);
    expect(refreshCalls).toBe(1);
    expect(store.getState().auth.token).toBeNull();
    expect(store.getState().ui.sessionExpired).toBe(true);
    expect(localStorage.getItem("auth_refresh_token")).toBeNull();
  });

  it("keeps the session when renewal can't reach the server (network error), so other tabs aren't signed out", async () => {
    server.use(
      http.post("http://localhost/api/v1/auth/refresh", () => HttpResponse.error()),
      http.get("http://localhost/api/v1/protected", () => HttpResponse.json({ message: "Unauthorized" }, { status: 401 })),
    );
    localStorage.setItem("auth_token", EXPIRED);
    localStorage.setItem("auth_refresh_token", "rt-old");
    const store = makeSignedInStore(EXPIRED, "rt-old");

    const result = await baseQuery("protected", makeFakeApi(store), {});

    expect(result.error?.status).toBe(401);
    expect(store.getState().auth.refreshToken).toBe("rt-old");
    expect(store.getState().ui.sessionExpired).toBe(false);
    expect(localStorage.getItem("auth_refresh_token")).toBe("rt-old");
  });

  it("keeps the session when the server is rate-limiting renewals (429)", async () => {
    server.use(
      http.post("http://localhost/api/v1/auth/refresh", () => HttpResponse.json({}, { status: 429 })),
      http.get("http://localhost/api/v1/protected", () => HttpResponse.json({ message: "Unauthorized" }, { status: 401 })),
    );
    localStorage.setItem("auth_token", EXPIRED);
    localStorage.setItem("auth_refresh_token", "rt-old");
    const store = makeSignedInStore(EXPIRED, "rt-old");

    await baseQuery("protected", makeFakeApi(store), {});

    expect(store.getState().ui.sessionExpired).toBe(false);
    expect(localStorage.getItem("auth_refresh_token")).toBe("rt-old");
  });

  it("renews and retries once when a not-yet-expired token is rejected with 401", async () => {
    useRenewalHandlers("rt-old");
    const revoked = jwt(600, "user-0001-revoked");
    localStorage.setItem("auth_token", revoked);
    localStorage.setItem("auth_refresh_token", "rt-old");
    const store = makeSignedInStore(revoked, "rt-old");

    const result = await baseQuery("protected", makeFakeApi(store), {});

    expect(result.data).toEqual({ ok: true });
    expect(refreshCalls).toBe(1);
  });
});
