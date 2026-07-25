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
import { ResetPasswordPage } from "@/features/auth/components/ResetPasswordPage";

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.post("http://localhost/api/v1/auth/reset-password", () =>
    new HttpResponse(null, { status: 204 }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function renderPage(search = "?email=owner%40test.com&token=tok123") {
  const store = configureStore({
    reducer: {
      auth:                  authReducer,
      [authApi.reducerPath]: authApi.reducer,
    },
    middleware: (gd) => gd().concat(authApi.middleware),
  });

  const { container } = render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[`/reset-password${search}`]}>
        <Routes>
          <Route path="/reset-password" element={<ResetPasswordPage />} />
          <Route path="/login"          element={<div data-testid="login-page" />} />
          <Route path="/forgot-password" element={<div data-testid="forgot-password-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return { store, container };
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("ResetPasswordPage", () => {
  it("renders the reset-password form", () => {
    renderPage();
    expect(screen.getByRole("heading", { name: /reset your password/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/^email$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/new password/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/confirm password/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /reset password/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /back to sign in/i })).toBeInTheDocument();
  });

  it("never shows a token field — it's carried as a hidden input, not a visible one", () => {
    const { container } = renderPage("?email=owner%40test.com&token=my-reset-token");

    expect(screen.queryByLabelText(/reset token/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/reset token/i)).not.toBeInTheDocument();
    const hiddenInput = container.querySelector('input[type="hidden"][name="token"]');
    expect(hiddenInput).toHaveValue("my-reset-token");
  });

  it("pre-populates email from URL query params, read-only by default", () => {
    renderPage("?email=owner%40test.com&token=my-reset-token");

    const emailInput = screen.getByLabelText<HTMLInputElement>(/^email$/i);
    expect(emailInput.value).toBe("owner@test.com");
    expect(emailInput).toHaveAttribute("readonly");
  });

  it("email is editable when no params are in the URL, but the form is blocked by the missing token", () => {
    renderPage("?");

    expect(screen.queryByLabelText(/^email$/i)).not.toBeInTheDocument();
    expect(screen.getByText(/needs a valid reset link/i)).toBeInTheDocument();
  });

  it("unlocks the email field for editing via the pencil affordance", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole("button", { name: /change email/i }));

    const emailInput = screen.getByLabelText<HTMLInputElement>(/^email$/i);
    expect(emailInput).not.toHaveAttribute("readonly");
    await user.clear(emailInput);
    await user.type(emailInput, "new@test.com");
    expect(emailInput.value).toBe("new@test.com");
  });

  it("blocks the form and offers a recovery link when the URL has no token", () => {
    renderPage("?email=owner%40test.com");

    expect(screen.getByText(/needs a valid reset link/i)).toBeInTheDocument();
    const link = screen.getByRole("link", { name: /request a new reset link/i });
    expect(link).toHaveAttribute("href", "/forgot-password");
    expect(screen.queryByRole("button", { name: /^reset password$/i })).not.toBeInTheDocument();
  });

  it("shows password-min-length error when new password is too short", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/new password/i), "short");
    await user.type(screen.getByLabelText(/confirm password/i), "short");
    await user.click(screen.getByRole("button", { name: /reset password/i }));

    expect(await screen.findByText(/password must be at least 8 characters/i)).toBeInTheDocument();
  });

  it("shows password-mismatch error when passwords differ", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/new password/i), "ValidPass1!");
    await user.type(screen.getByLabelText(/confirm password/i), "DifferentPass1!");
    await user.click(screen.getByRole("button", { name: /reset password/i }));

    expect(await screen.findByText(/passwords do not match/i)).toBeInTheDocument();
  });

  it("shows a live password-match indicator as the user types", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/new password/i), "ValidPass1!");
    await user.type(screen.getByLabelText(/confirm password/i), "ValidPass1");
    expect(await screen.findByText(/doesn't match yet/i)).toBeInTheDocument();

    await user.type(screen.getByLabelText(/confirm password/i), "!");
    expect(await screen.findByText(/^passwords match$/i)).toBeInTheDocument();
  });

  it("shows success state after a successful reset", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/new password/i), "ValidPass1!");
    await user.type(screen.getByLabelText(/confirm password/i), "ValidPass1!");
    await user.click(screen.getByRole("button", { name: /reset password/i }));

    expect(await screen.findByText(/password reset successfully/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /back to sign in/i })).toBeInTheDocument();
    // Form is gone after success
    expect(screen.queryByRole("button", { name: /reset password/i })).not.toBeInTheDocument();
  });

  it("shows an invalid-or-expired message with a recovery link for RESET_TOKEN_INVALID", async () => {
    server.use(
      http.post("http://localhost/api/v1/auth/reset-password", () =>
        HttpResponse.json(
          { status: 422, message: "This reset link is invalid or has expired.", code: "RESET_TOKEN_INVALID" },
          { status: 422 },
        ),
      ),
    );

    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/new password/i), "ValidPass1!");
    await user.type(screen.getByLabelText(/confirm password/i), "ValidPass1!");
    await user.click(screen.getByRole("button", { name: /reset password/i }));

    expect(await screen.findByText(/invalid or has expired/i)).toBeInTheDocument();
    const link = screen.getByRole("link", { name: /request a new reset link/i });
    expect(link).toHaveAttribute("href", expect.stringContaining("/forgot-password"));
  });

  it("shows the raw server message for non-token failures (e.g. password policy)", async () => {
    server.use(
      http.post("http://localhost/api/v1/auth/reset-password", () =>
        HttpResponse.json({ status: 422, message: "Passwords must have at least one non alphanumeric character." }, { status: 422 }),
      ),
    );

    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/new password/i), "ValidPass1!");
    await user.type(screen.getByLabelText(/confirm password/i), "ValidPass1!");
    await user.click(screen.getByRole("button", { name: /reset password/i }));

    expect(await screen.findByText(/passwords must have at least one non alphanumeric character/i)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /request a new reset link/i })).not.toBeInTheDocument();
  });

  it("falls back to generic error when body has no message", async () => {
    server.use(
      http.post("http://localhost/api/v1/auth/reset-password", () =>
        HttpResponse.json({}, { status: 400 }),
      ),
    );

    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/new password/i), "ValidPass1!");
    await user.type(screen.getByLabelText(/confirm password/i), "ValidPass1!");
    await user.click(screen.getByRole("button", { name: /reset password/i }));

    expect(await screen.findByText("Reset failed.")).toBeInTheDocument();
  });

  it("shows network-error message when fetch fails", async () => {
    server.use(
      http.post("http://localhost/api/v1/auth/reset-password", () => HttpResponse.error()),
    );

    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/new password/i), "ValidPass1!");
    await user.type(screen.getByLabelText(/confirm password/i), "ValidPass1!");
    await user.click(screen.getByRole("button", { name: /reset password/i }));

    expect(await screen.findByText(/unable to reach the server/i)).toBeInTheDocument();
  });
});
