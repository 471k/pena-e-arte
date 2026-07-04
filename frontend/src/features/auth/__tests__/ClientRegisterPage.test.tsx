import { describe, it, expect, beforeAll, afterEach, afterAll, vi } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { toast } from "sonner";

import authReducer from "@/features/auth/authSlice";
import { authApi } from "@/features/auth/authApi";
import { ClientRegisterPage } from "@/features/auth/components/ClientRegisterPage";

vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));

// ── Fake JWT ───────────────────────────────────────────────────────────────────

const ROLE_CLAIM = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

function toBase64Url(s: string) {
  return btoa(s).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function makeFakeJwt(role = "client") {
  const header  = toBase64Url(JSON.stringify({ alg: "HS256", typ: "JWT" }));
  const payload = toBase64Url(JSON.stringify({
    sub:          "u-client-test",
    email:        "new.client@example.com",
    [ROLE_CLAIM]: role,
    tenant_id:    "t-test",
    exp:          9_999_999_999,
  }));
  return `${header}.${payload}.fake-sig`;
}

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.post("http://localhost/api/v1/auth/register", () => new HttpResponse(null, { status: 204 })),
  http.post("http://localhost/api/v1/auth/login", () =>
    HttpResponse.json({ accessToken: makeFakeJwt(), tokenType: "Bearer" }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); localStorage.clear(); sessionStorage.clear(); cleanup(); vi.clearAllMocks(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore(preloadedRole: string | null = null) {
  return configureStore({
    reducer: {
      auth:                  authReducer,
      [authApi.reducerPath]: authApi.reducer,
    },
    middleware: (gd) => gd().concat(authApi.middleware),
    preloadedState: preloadedRole
      ? {
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          auth: { user: { id: "u1", email: "x@test.com" }, token: "fake", tenantId: "t1", role: preloadedRole, pendingReferralCode: null } as any,
        }
      : undefined,
  });
}

function renderPage(initialPath = "/client-register?studioId=studio-1") {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[initialPath]}>
        <Routes>
          <Route path="/client-register" element={<ClientRegisterPage />} />
          <Route path="/book"     element={<div data-testid="book-page" />} />
          <Route path="/discover" element={<div data-testid="discover-page" />} />
          <Route path="/login"    element={<div data-testid="login-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

async function fillValidForm(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText(/first name/i), "Alex");
  await user.type(screen.getByLabelText(/^email$/i), "new.client@example.com");
  await user.type(screen.getByLabelText(/^password$/i), "Password1!");
  await user.type(screen.getByLabelText(/confirm password/i), "Password1!");
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("ClientRegisterPage", () => {
  it("shows the registration form even when studioId is missing (studio-less signup)", () => {
    renderPage("/client-register");
    expect(screen.getByRole("heading", { name: /create your account/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/first name/i)).toBeInTheDocument();
  });

  it("shows the registration form when studioId is present", () => {
    renderPage();
    expect(screen.getByRole("heading", { name: /create your account/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/first name/i)).toBeInTheDocument();
  });

  it("requires first name", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole("button", { name: /create account/i }));
    expect(await screen.findByText(/first name is required/i)).toBeInTheDocument();
  });

  it("validates email format", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/first name/i), "Alex");
    await user.type(screen.getByLabelText(/^email$/i), "not-an-email");
    await user.click(screen.getByRole("button", { name: /create account/i }));
    expect(await screen.findByText(/enter a valid email/i)).toBeInTheDocument();
  });

  it("requires password of at least 8 characters", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/first name/i), "Alex");
    await user.type(screen.getByLabelText(/^email$/i), "a@example.com");
    await user.type(screen.getByLabelText(/^password$/i), "short");
    await user.click(screen.getByRole("button", { name: /create account/i }));
    const passwordInput = await screen.findByLabelText(/^password$/i);
    expect(passwordInput).toHaveAttribute("aria-describedby", "password-error");
    expect(document.getElementById("password-error")).toHaveTextContent(/at least 8 characters/i);
  });

  it("requires confirmPassword to match password", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/first name/i), "Alex");
    await user.type(screen.getByLabelText(/^email$/i), "a@example.com");
    await user.type(screen.getByLabelText(/^password$/i), "Password1!");
    await user.type(screen.getByLabelText(/confirm password/i), "Different1!");
    await user.click(screen.getByRole("button", { name: /create account/i }));
    expect(await screen.findByText(/passwords do not match/i)).toBeInTheDocument();
  });

  it("registers, logs in, and navigates to redirectTo on success", async () => {
    const user = userEvent.setup();
    renderPage("/client-register?studioId=studio-1&redirect=%2Fbook");
    await fillValidForm(user);
    await user.click(screen.getByRole("button", { name: /create account/i }));

    expect(await screen.findByTestId("book-page")).toBeInTheDocument();
  });

  it("shows 'already exists' error on 409 and does not attempt login", async () => {
    let loginCalled = false;
    server.use(
      http.post("http://localhost/api/v1/auth/register", () =>
        HttpResponse.json({ message: "An account with this email already exists." }, { status: 409 }),
      ),
      http.post("http://localhost/api/v1/auth/login", () => {
        loginCalled = true;
        return HttpResponse.json({ accessToken: makeFakeJwt(), tokenType: "Bearer" });
      }),
    );

    const user = userEvent.setup();
    renderPage();
    await fillValidForm(user);
    await user.click(screen.getByRole("button", { name: /create account/i }));

    expect(await screen.findByText(/already exists/i)).toBeInTheDocument();
    expect(loginCalled).toBe(false);
  });

  it("shows rate-limit message on 429", async () => {
    server.use(
      http.post("http://localhost/api/v1/auth/register", () => new HttpResponse(null, { status: 429 })),
    );

    const user = userEvent.setup();
    renderPage();
    await fillValidForm(user);
    await user.click(screen.getByRole("button", { name: /create account/i }));

    expect(await screen.findByText(/too many attempts/i)).toBeInTheDocument();
  });

  it("shows a toast and redirects to /login when register succeeds but login fails", async () => {
    server.use(
      http.post("http://localhost/api/v1/auth/login", () =>
        HttpResponse.json({ message: "Invalid credentials." }, { status: 401 }),
      ),
    );

    const user = userEvent.setup();
    renderPage();
    await fillValidForm(user);
    await user.click(screen.getByRole("button", { name: /create account/i }));

    expect(await screen.findByTestId("login-page")).toBeInTheDocument();
    expect(toast.error).toHaveBeenCalledWith("Account created. Please sign in manually.");
  });

  it("redirects away when already authenticated", () => {
    const store = makeStore("client");
    render(
      <Provider store={store}>
        <MemoryRouter initialEntries={["/client-register?studioId=studio-1"]}>
          <Routes>
            <Route path="/client-register" element={<ClientRegisterPage />} />
            <Route path="/book" element={<div data-testid="book-page" />} />
          </Routes>
        </MemoryRouter>
      </Provider>,
    );
    expect(screen.getByTestId("book-page")).toBeInTheDocument();
  });

  it("'Already have an account' link points to /login", () => {
    renderPage();
    expect(screen.getByRole("link", { name: /sign in/i })).toHaveAttribute("href", "/login");
  });

  it("omits studioId from the register payload when signing up with no studio", async () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    let capturedBody: any = null;
    server.use(
      http.post("http://localhost/api/v1/auth/register", async ({ request }) => {
        capturedBody = await request.json();
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const user = userEvent.setup();
    renderPage("/client-register");
    await fillValidForm(user);
    await user.click(screen.getByRole("button", { name: /create account/i }));

    await waitFor(() => expect(capturedBody).not.toBeNull());
    expect(capturedBody).not.toHaveProperty("studioId");
  });

  it("redirects to /discover by default when signing up with no studio", async () => {
    const user = userEvent.setup();
    renderPage("/client-register");
    await fillValidForm(user);
    await user.click(screen.getByRole("button", { name: /create account/i }));

    expect(await screen.findByTestId("discover-page")).toBeInTheDocument();
  });

  it("redirects to /book by default when signing up with a studio", async () => {
    const user = userEvent.setup();
    renderPage("/client-register?studioId=studio-1");
    await fillValidForm(user);
    await user.click(screen.getByRole("button", { name: /create account/i }));

    expect(await screen.findByTestId("book-page")).toBeInTheDocument();
  });
});
