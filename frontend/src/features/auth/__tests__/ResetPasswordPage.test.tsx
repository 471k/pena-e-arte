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

  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[`/reset-password${search}`]}>
        <Routes>
          <Route path="/reset-password" element={<ResetPasswordPage />} />
          <Route path="/login"          element={<div data-testid="login-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("ResetPasswordPage", () => {
  it("renders the set-new-password form", () => {
    renderPage();
    expect(screen.getByRole("heading", { name: /set new password/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/^email$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/reset token/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/new password/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/confirm password/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /reset password/i })).toBeInTheDocument();
  });

  it("pre-populates email and token from URL query params", () => {
    renderPage("?email=owner%40test.com&token=my-reset-token");

    expect(screen.getByLabelText<HTMLInputElement>(/^email$/i).value).toBe("owner@test.com");
    expect(screen.getByLabelText<HTMLInputElement>(/reset token/i).value).toBe("my-reset-token");
  });

  it("email and token are empty when no params are in the URL", () => {
    renderPage("?");

    expect(screen.getByLabelText<HTMLInputElement>(/^email$/i).value).toBe("");
    expect(screen.getByLabelText<HTMLInputElement>(/reset token/i).value).toBe("");
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

  it("shows success state after a successful reset", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/new password/i), "ValidPass1!");
    await user.type(screen.getByLabelText(/confirm password/i), "ValidPass1!");
    await user.click(screen.getByRole("button", { name: /reset password/i }));

    expect(await screen.findByText(/password reset successfully/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /sign in/i })).toBeInTheDocument();
    // Form is gone after success
    expect(screen.queryByRole("button", { name: /reset password/i })).not.toBeInTheDocument();
  });

  it("shows server error message on API error with body", async () => {
    server.use(
      http.post("http://localhost/api/v1/auth/reset-password", () =>
        HttpResponse.json({ message: "Token has expired." }, { status: 400 }),
      ),
    );

    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/new password/i), "ValidPass1!");
    await user.type(screen.getByLabelText(/confirm password/i), "ValidPass1!");
    await user.click(screen.getByRole("button", { name: /reset password/i }));

    expect(await screen.findByText("Token has expired.")).toBeInTheDocument();
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
