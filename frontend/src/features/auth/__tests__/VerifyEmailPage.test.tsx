import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { authApi } from "@/features/auth/authApi";
import { VerifyEmailPage } from "@/features/auth/components/VerifyEmailPage";

const ROLE_CLAIM = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

function toBase64Url(s: string) {
  return btoa(s).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function makeFakeJwt(emailVerified: boolean) {
  const header  = toBase64Url(JSON.stringify({ alg: "HS256", typ: "JWT" }));
  const payload = toBase64Url(JSON.stringify({
    sub:            "u1",
    email:          "client@test.com",
    [ROLE_CLAIM]:   "client",
    tenant_id:      "t-test",
    email_verified: emailVerified,
    exp:            9_999_999_999,
  }));
  return `${header}.${payload}.fake-sig`;
}

const server = setupServer();

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); localStorage.clear(); sessionStorage.clear(); cleanup(); });
afterAll(() => server.close());

function makeStore(preloaded?: { token: string; refreshToken: string | null }) {
  return configureStore({
    reducer: {
      auth:                  authReducer,
      [authApi.reducerPath]: authApi.reducer,
    },
    middleware: (gd) => gd().concat(authApi.middleware),
    preloadedState: preloaded
      ? {
          auth: {
            user:                { id: "u1", email: "client@test.com", emailVerified: false },
            token:               preloaded.token,
            refreshToken:        preloaded.refreshToken,
            tenantId:            "t-test",
            role:                "client",
            pendingReferralCode: null,
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
          } as any,
        }
      : undefined,
  });
}

function renderAt(path: string, store = makeStore()) {
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/verify-email" element={<VerifyEmailPage />} />
          <Route path="/login" element={<div data-testid="login-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

describe("VerifyEmailPage", () => {
  it("shows success when the backend confirms the token", async () => {
    server.use(
      http.get("http://localhost/api/v1/auth/verify-email", () => new HttpResponse(null, { status: 204 })),
    );

    renderAt("/verify-email?userId=u1&token=abc");

    expect(await screen.findByText(/email confirmed!/i)).toBeInTheDocument();
  });

  it("shows an error when required query params are missing", async () => {
    renderAt("/verify-email");

    expect(await screen.findByText(/verification failed/i)).toBeInTheDocument();
  });

  it("shows an error when the backend rejects the token", async () => {
    server.use(
      http.get("http://localhost/api/v1/auth/verify-email", () =>
        HttpResponse.json({ message: "Invalid confirmation request." }, { status: 400 })),
    );

    renderAt("/verify-email?userId=u1&token=bad");

    expect(await screen.findByText(/verification failed/i)).toBeInTheDocument();
  });

  it("refreshes the session token when the user already has an active session", async () => {
    server.use(
      http.get("http://localhost/api/v1/auth/verify-email", () => new HttpResponse(null, { status: 204 })),
      http.post("http://localhost/api/v1/auth/refresh", () =>
        HttpResponse.json({
          accessToken:  makeFakeJwt(true),
          refreshToken: "new-refresh-token",
          tokenType:    "Bearer",
        })),
    );

    const store = makeStore({ token: "stale-token", refreshToken: "old-refresh-token" });
    renderAt("/verify-email?userId=u1&token=abc", store);

    await screen.findByText(/email confirmed!/i);

    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(store.getState().auth.user?.emailVerified).toBe(true);
    expect(store.getState().auth.refreshToken).toBe("new-refresh-token");
  });

  it("does not attempt a refresh when there is no active session", async () => {
    let refreshCalled = false;
    server.use(
      http.get("http://localhost/api/v1/auth/verify-email", () => new HttpResponse(null, { status: 204 })),
      http.post("http://localhost/api/v1/auth/refresh", () => {
        refreshCalled = true;
        return HttpResponse.json({ accessToken: makeFakeJwt(true), refreshToken: "x", tokenType: "Bearer" });
      }),
    );

    renderAt("/verify-email?userId=u1&token=abc");

    await screen.findByText(/email confirmed!/i);
    expect(refreshCalled).toBe(false);
  });
});
