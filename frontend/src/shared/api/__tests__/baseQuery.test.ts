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
