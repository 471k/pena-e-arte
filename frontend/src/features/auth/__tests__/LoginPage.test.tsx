import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { authApi } from "@/features/auth/authApi";
import { LoginPage } from "@/features/auth/components/LoginPage";

// ── Fake JWT ───────────────────────────────────────────────────────────────────

const ROLE_CLAIM = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

function makeFakeJwt(role: string, email = "owner@test.com") {
  const header  = Buffer.from(JSON.stringify({ alg: "HS256", typ: "JWT" })).toString("base64url");
  const payload = Buffer.from(JSON.stringify({
    sub:         "u-login-test",
    email,
    [ROLE_CLAIM]: role,
    tenant_id:   "t-test",
    exp:          9_999_999_999,
  })).toString("base64url");
  return `${header}.${payload}.fake-sig`;
}

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.post("http://localhost/api/v1/auth/login", () =>
    HttpResponse.json({ accessToken: makeFakeJwt("owner"), tokenType: "Bearer" }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); localStorage.clear(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore(preloadedRole: string | null = null) {
  return configureStore({
    reducer: {
      auth:                   authReducer,
      [authApi.reducerPath]:  authApi.reducer,
    },
    middleware: (gd) => gd().concat(authApi.middleware),
    preloadedState: preloadedRole
      ? {
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          auth: { user: { id: "u1", email: "owner@test.com" }, token: "fake", tenantId: "t1", role: preloadedRole, pendingReferralCode: null } as any,
        }
      : undefined,
  });
}

function renderPage(initialPath = "/login") {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[initialPath]}>
        <Routes>
          <Route path="/login"    element={<LoginPage />} />
          <Route path="/book"     element={<div data-testid="client-home" />} />
          <Route path="/schedule" element={<div data-testid="artist-home" />} />
          <Route path="/dashboard" element={<div data-testid="owner-home" />} />
          <Route path="/platform" element={<div data-testid="issuer-home" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

function renderLoggedIn(role: string) {
  const store = makeStore(role);
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/login"]}>
        <Routes>
          <Route path="/login"     element={<LoginPage />} />
          <Route path="/book"      element={<div data-testid="client-home" />} />
          <Route path="/schedule"  element={<div data-testid="artist-home" />} />
          <Route path="/dashboard" element={<div data-testid="owner-home" />} />
          <Route path="/platform"  element={<div data-testid="issuer-home" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("LoginPage", () => {
  it("renders the sign-in form", () => {
    renderPage();
    expect(screen.getByRole("heading", { name: /sign in/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/email/i)).toBeInTheDocument();
    expect(screen.getByLabelText("Password")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /sign in/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /forgot password/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /register your studio/i })).toBeInTheDocument();
  });

  it("shows the session-expired banner when ?reason=session_expired is in the URL", () => {
    renderPage("/login?reason=session_expired");
    expect(screen.getByText(/your session expired/i)).toBeInTheDocument();
  });

  it("does not show session-expired banner without the query param", () => {
    renderPage();
    expect(screen.queryByText(/your session expired/i)).not.toBeInTheDocument();
  });

  it("shows email-required validation error when submitting empty form", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole("button", { name: /sign in/i }));

    expect(await screen.findByText(/email is required/i)).toBeInTheDocument();
  });

  it("shows invalid-email validation error on bad email format", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/email/i), "not-an-email");
    await user.click(screen.getByRole("button", { name: /sign in/i }));

    expect(await screen.findByText(/enter a valid email/i)).toBeInTheDocument();
  });

  it("shows password-required validation error when password is empty", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/email/i), "owner@test.com");
    await user.click(screen.getByRole("button", { name: /sign in/i }));

    expect(await screen.findByText(/password is required/i)).toBeInTheDocument();
  });

  it("successful login dispatches credentials and navigates to the role home", async () => {
    const user  = userEvent.setup();
    const store = renderPage();

    await user.type(screen.getByLabelText(/email/i), "owner@test.com");
    await user.type(screen.getByLabelText("Password"), "secret123");
    await user.click(screen.getByRole("button", { name: /sign in/i }));

    await screen.findByTestId("owner-home");

    expect(store.getState().auth.role).toBe("owner");
    expect(store.getState().auth.token).toBeTruthy();
  });

  it("successful client login navigates to /book", async () => {
    server.use(
      http.post("http://localhost/api/v1/auth/login", () =>
        HttpResponse.json({ accessToken: makeFakeJwt("client", "client@test.com"), tokenType: "Bearer" }),
      ),
    );

    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/email/i), "client@test.com");
    await user.type(screen.getByLabelText("Password"), "secret123");
    await user.click(screen.getByRole("button", { name: /sign in/i }));

    await screen.findByTestId("client-home");
  });

  it("shows server error message on 401 with body", async () => {
    server.use(
      http.post("http://localhost/api/v1/auth/login", () =>
        HttpResponse.json({ message: "Invalid credentials." }, { status: 401 }),
      ),
    );

    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/email/i), "owner@test.com");
    await user.type(screen.getByLabelText("Password"), "wrongpass");
    await user.click(screen.getByRole("button", { name: /sign in/i }));

    expect(await screen.findByText("Invalid credentials.")).toBeInTheDocument();
  });

  it("falls back to generic message when 401 body has no message", async () => {
    server.use(
      http.post("http://localhost/api/v1/auth/login", () =>
        HttpResponse.json({}, { status: 401 }),
      ),
    );

    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/email/i), "owner@test.com");
    await user.type(screen.getByLabelText("Password"), "wrongpass");
    await user.click(screen.getByRole("button", { name: /sign in/i }));

    expect(await screen.findByText("Invalid email or password.")).toBeInTheDocument();
  });

  it("shows network-error message when fetch fails", async () => {
    server.use(
      http.post("http://localhost/api/v1/auth/login", () => HttpResponse.error()),
    );

    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/email/i), "owner@test.com");
    await user.type(screen.getByLabelText("Password"), "secret123");
    await user.click(screen.getByRole("button", { name: /sign in/i }));

    expect(await screen.findByText(/unable to reach the server/i)).toBeInTheDocument();
  });

  it("redirects to role home when user is already logged in", async () => {
    renderLoggedIn("artist");

    await screen.findByTestId("artist-home");
  });

  it("renders the Pena e Arte brand mark", () => {
    renderPage();
    expect(screen.getByText("Pena e Arte")).toBeInTheDocument();
  });
});
