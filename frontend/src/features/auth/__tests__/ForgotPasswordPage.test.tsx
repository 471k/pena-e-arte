import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { authApi } from "@/features/auth/authApi";
import { ForgotPasswordPage } from "@/features/auth/components/ForgotPasswordPage";

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.post("http://localhost/api/v1/auth/forgot-password", () =>
    HttpResponse.json({ resetToken: null }, { status: 200 }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function renderPage() {
  const store = configureStore({
    reducer: {
      auth:                  authReducer,
      [authApi.reducerPath]: authApi.reducer,
    },
    middleware: (gd) => gd().concat(authApi.middleware),
  });

  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/forgot-password"]}>
        <Routes>
          <Route path="/forgot-password" element={<ForgotPasswordPage />} />
          <Route path="/login"           element={<div data-testid="login-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("ForgotPasswordPage", () => {
  it("renders the reset-password form", () => {
    renderPage();
    expect(screen.getByRole("heading", { name: /reset password/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/email/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /send reset link/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /back to sign in/i })).toBeInTheDocument();
    // Boilerplate CardDescription must be gone
    expect(
      screen.queryByText(/enter your email and we.ll send a reset link/i)
    ).not.toBeInTheDocument();
  });

  it("shows email-required error on empty submit", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole("button", { name: /send reset link/i }));

    expect(await screen.findByText(/email is required/i)).toBeInTheDocument();
  });

  it("shows invalid-email error on bad email format", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/email/i), "not-valid");
    await user.click(screen.getByRole("button", { name: /send reset link/i }));

    expect(await screen.findByText(/enter a valid email/i)).toBeInTheDocument();
  });

  it("shows success confirmation after a successful submission", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/email/i), "owner@test.com");
    await user.click(screen.getByRole("button", { name: /send reset link/i }));

    expect(
      await screen.findByText(/if an account exists for that email, a reset link has been sent/i),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /back to sign in/i })).toBeInTheDocument();
    // Form is gone after success
    expect(screen.queryByRole("button", { name: /send reset link/i })).not.toBeInTheDocument();
  });

  it("shows server error message on API error with body", async () => {
    server.use(
      http.post("http://localhost/api/v1/auth/forgot-password", () =>
        HttpResponse.json({ message: "Too many requests." }, { status: 429 }),
      ),
    );

    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/email/i), "owner@test.com");
    await user.click(screen.getByRole("button", { name: /send reset link/i }));

    expect(await screen.findByText("Too many requests.")).toBeInTheDocument();
  });

  it("falls back to generic error when body has no message", async () => {
    server.use(
      http.post("http://localhost/api/v1/auth/forgot-password", () =>
        HttpResponse.json({}, { status: 500 }),
      ),
    );

    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/email/i), "owner@test.com");
    await user.click(screen.getByRole("button", { name: /send reset link/i }));

    expect(await screen.findByText("Something went wrong.")).toBeInTheDocument();
  });

  it("shows network-error message when fetch fails", async () => {
    server.use(
      http.post("http://localhost/api/v1/auth/forgot-password", () => HttpResponse.error()),
    );

    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/email/i), "owner@test.com");
    await user.click(screen.getByRole("button", { name: /send reset link/i }));

    expect(await screen.findByText(/unable to reach the server/i)).toBeInTheDocument();
  });

  it("does not render the CardDescription subtitle", () => {
    renderPage();
    expect(
      screen.queryByText(/enter your email and we.ll send a reset link/i)
    ).not.toBeInTheDocument();
  });

  it("email field gets aria-invalid=true and aria-describedby on validation error", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole("button", { name: /send reset link/i }));
    await screen.findByText(/email is required/i);

    const emailInput = screen.getByLabelText(/email/i);
    expect(emailInput).toHaveAttribute("aria-invalid", "true");
    expect(emailInput).toHaveAttribute("aria-describedby", "email-error");
  });

  it("email error paragraph has role=alert for screen reader announcement", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole("button", { name: /send reset link/i }));

    const errorEl = await screen.findByText(/email is required/i);
    expect(errorEl).toHaveAttribute("role", "alert");
  });

  it("server error is rendered inside an Alert with role=alert", async () => {
    server.use(
      http.post("http://localhost/api/v1/auth/forgot-password", () =>
        HttpResponse.json({ message: "Something went wrong." }, { status: 500 }),
      ),
    );

    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/email/i), "owner@test.com");
    await user.click(screen.getByRole("button", { name: /send reset link/i }));

    const alertEl = await screen.findByRole("alert");
    expect(alertEl).toHaveTextContent("Something went wrong.");
  });

  it("Back to sign in link is inside the card with a separator (not inside the form)", () => {
    renderPage();
    const link = screen.getByRole("link", { name: /back to sign in/i });
    expect(link).toBeInTheDocument();
    // The link's parent container has the border-t separator class
    expect(link.closest("div")).toHaveClass("border-t");
  });
});
