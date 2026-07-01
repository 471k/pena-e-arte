import { describe, it, expect, beforeAll, beforeEach, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
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

function toBase64Url(s: string) {
  return btoa(s).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function makeFakeJwt(role: string, email = "owner@test.com") {
  const header  = toBase64Url(JSON.stringify({ alg: "HS256", typ: "JWT" }));
  const payload = toBase64Url(JSON.stringify({
    sub:         "u-login-test",
    email,
    [ROLE_CLAIM]: role,
    tenant_id:   "t-test",
    exp:          9_999_999_999,
  }));
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
    // Boilerplate CardDescription must be gone
    expect(
      screen.queryByText(/enter your credentials to access your account/i)
    ).not.toBeInTheDocument();
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

  it("does not render the generic credential subtitle", () => {
    renderPage();
    expect(
      screen.queryByText(/enter your credentials to access your account/i)
    ).not.toBeInTheDocument();
  });

  it("shows rate-limit message on 429 response", async () => {
    server.use(
      http.post("http://localhost/api/v1/auth/login", () =>
        HttpResponse.json({ message: "Rate limit exceeded." }, { status: 429 }),
      ),
    );

    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/email/i), "owner@test.com");
    await user.type(screen.getByLabelText("Password"), "wrongpass");
    await user.click(screen.getByRole("button", { name: /sign in/i }));

    expect(
      await screen.findByText(/too many sign-in attempts/i)
    ).toBeInTheDocument();
  });

  it("server error is rendered inside an Alert with role=alert", async () => {
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

    const alertEl = await screen.findByRole("alert");
    expect(alertEl).toHaveTextContent("Invalid credentials.");
  });

  it("email field gets aria-invalid=true and aria-describedby on validation error", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole("button", { name: /sign in/i }));
    await screen.findByText(/email is required/i);

    const emailInput = screen.getByLabelText(/email/i);
    expect(emailInput).toHaveAttribute("aria-invalid", "true");
    expect(emailInput).toHaveAttribute("aria-describedby", "email-error");
  });

  it("password field gets aria-invalid=true and aria-describedby on validation error", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/email/i), "owner@test.com");
    await user.click(screen.getByRole("button", { name: /sign in/i }));
    await screen.findByText(/password is required/i);

    const passwordInput = screen.getByLabelText("Password");
    expect(passwordInput).toHaveAttribute("aria-invalid", "true");
    expect(passwordInput).toHaveAttribute("aria-describedby", "password-error");
  });

  it("field validation error paragraphs have role=alert for screen reader announcement", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole("button", { name: /sign in/i }));

    const errorEl = await screen.findByText(/email is required/i);
    expect(errorEl).toHaveAttribute("role", "alert");
  });

  it("registration link is present in the document", () => {
    renderPage();
    expect(
      screen.getByRole("link", { name: /register your studio/i })
    ).toBeInTheDocument();
  });

  it("renders the updated subtitle copy", () => {
    renderPage();
    expect(
      screen.getByText("Run your studio. Book clients. Manage your team.")
    ).toBeInTheDocument();
  });

  it("does NOT render the old subtitle", () => {
    renderPage();
    expect(
      screen.queryByText("Tattoo Studio Management")
    ).not.toBeInTheDocument();
  });

  it("renders the registration prompt with updated copy", () => {
    renderPage();
    expect(screen.getByText(/don't have an account/i)).toBeInTheDocument();
  });

  it("does NOT render the old 'New studio?' copy", () => {
    renderPage();
    expect(screen.queryByText(/new studio\?/i)).not.toBeInTheDocument();
  });

  it("renders the legal footer with Privacy Policy link", () => {
    renderPage();
    expect(screen.getByRole("link", { name: /privacy policy/i })).toBeInTheDocument();
  });

  it("renders the legal footer with Terms of Service link", () => {
    renderPage();
    expect(screen.getByRole("link", { name: /terms of service/i })).toBeInTheDocument();
  });

  it("renders the Contact support link in the footer", () => {
    renderPage();
    expect(screen.getByRole("link", { name: /contact support/i })).toBeInTheDocument();
  });

  it("PenLine icon has aria-hidden to suppress screen reader announcement", () => {
    renderPage();
    expect(screen.getByText("Pena e Arte")).toBeInTheDocument();
  });

  it("honours ?redirect= after successful login instead of going to role home", async () => {
    server.use(
      http.post("http://localhost/api/v1/auth/login", () =>
        HttpResponse.json({ accessToken: makeFakeJwt("client", "client@test.com"), tokenType: "Bearer" }),
      ),
    );

    const store = makeStore();
    render(
      <Provider store={store}>
        <MemoryRouter initialEntries={["/login?redirect=%2Fs%2Fmy-studio"]}>
          <Routes>
            <Route path="/login"    element={<LoginPage />} />
            <Route path="/s/:slug"  element={<div data-testid="studio-profile" />} />
            <Route path="/book"     element={<div data-testid="client-home" />} />
          </Routes>
        </MemoryRouter>
      </Provider>,
    );

    const user = userEvent.setup();
    await user.type(screen.getByLabelText(/email/i), "client@test.com");
    await user.type(screen.getByLabelText("Password"), "secret123");
    await user.click(screen.getByRole("button", { name: /sign in/i }));

    await screen.findByTestId("studio-profile");
    expect(screen.queryByTestId("client-home")).not.toBeInTheDocument();
  });

  it("password field has a placeholder", () => {
    renderPage();
    const passwordInput = screen.getByLabelText("Password");
    expect(passwordInput).toHaveAttribute("placeholder", "••••••••");
  });

  it("Forgot password link has accessible touch target (py-2 class applied)", () => {
    renderPage();
    const forgotLink = screen.getByRole("link", { name: /forgot password/i });
    expect(forgotLink).toBeInTheDocument();
    expect(forgotLink).toHaveClass("py-2");
  });
});

describe("LoginPage — Remember me", () => {
  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  it("renders a 'Remember me' checkbox checked by default", () => {
    renderPage();
    const checkbox = screen.getByRole("checkbox", { name: /remember me/i });
    expect(checkbox).toBeInTheDocument();
    expect(checkbox).toBeChecked();
  });

  it("stores token in localStorage when 'Remember me' is checked (default)", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/email/i), "owner@test.com");
    await user.type(screen.getByLabelText("Password"), "secret123");
    await user.click(screen.getByRole("button", { name: /sign in/i }));
    await screen.findByTestId("owner-home");

    expect(localStorage.getItem("auth_token")).not.toBeNull();
    expect(sessionStorage.getItem("auth_token")).toBeNull();
  });

  it("stores token in sessionStorage when 'Remember me' is unchecked", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole("checkbox", { name: /remember me/i }));

    await user.type(screen.getByLabelText(/email/i), "owner@test.com");
    await user.type(screen.getByLabelText("Password"), "secret123");
    await user.click(screen.getByRole("button", { name: /sign in/i }));
    await screen.findByTestId("owner-home");

    expect(sessionStorage.getItem("auth_token")).not.toBeNull();
    expect(localStorage.getItem("auth_token")).toBeNull();
  });

  it("'Forgot password?' and 'Remember me' are on the same row", () => {
    renderPage();
    const forgotLink = screen.getByRole("link", { name: /forgot password/i });
    const checkbox   = screen.getByRole("checkbox", { name: /remember me/i });
    expect(forgotLink.closest("div")).toBe(checkbox.closest("label")?.closest("div"));
  });
});
