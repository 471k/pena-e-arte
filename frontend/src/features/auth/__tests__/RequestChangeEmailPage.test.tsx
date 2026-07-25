import { describe, it, expect, beforeAll, afterEach, afterAll, vi } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { toast } from "sonner";

import authReducer from "@/features/auth/authSlice";
import { authApi } from "@/features/auth/authApi";
import { RequestChangeEmailPage } from "@/features/auth/components/RequestChangeEmailPage";

vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));

const server = setupServer(
  http.post("http://localhost/api/v1/auth/change-email", () => new HttpResponse(null, { status: 204 })),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); vi.clearAllMocks(); });
afterAll(() => server.close());

function makeStore() {
  return configureStore({
    reducer: {
      auth:                   authReducer,
      [authApi.reducerPath]:  authApi.reducer,
    },
    middleware: (gd) => gd().concat(authApi.middleware),
  });
}

function renderPage() {
  render(
    <Provider store={makeStore()}>
      <RequestChangeEmailPage />
    </Provider>,
  );
}

describe("RequestChangeEmailPage", () => {
  it("renders current-password and new-email fields", () => {
    renderPage();
    expect(screen.getByLabelText(/current password/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/new email address/i)).toBeInTheDocument();
  });

  it("shows a validation error for an invalid email", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/current password/i), "Password1!");
    await user.type(screen.getByLabelText(/new email address/i), "not-an-email");
    await user.click(screen.getByRole("button", { name: /send confirmation link/i }));

    expect(await screen.findByText(/enter a valid email address/i)).toBeInTheDocument();
  });

  it("shows a validation error when current password is empty", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/new email address/i), "new@test.com");
    await user.click(screen.getByRole("button", { name: /send confirmation link/i }));

    expect(await screen.findByText(/required/i)).toBeInTheDocument();
  });

  it("submits and shows the confirmation message on success", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/current password/i), "Password1!");
    await user.type(screen.getByLabelText(/new email address/i), "new@test.com");
    await user.click(screen.getByRole("button", { name: /send confirmation link/i }));

    expect(await screen.findByText(/we sent a confirmation link/i)).toBeInTheDocument();
  });

  it("shows an 'already in use' error on 409 conflict", async () => {
    server.use(
      http.post("http://localhost/api/v1/auth/change-email", () =>
        HttpResponse.json({ status: 409, message: "That email is already in use." }, { status: 409 }),
      ),
    );
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/current password/i), "Password1!");
    await user.type(screen.getByLabelText(/new email address/i), "taken@test.com");
    await user.click(screen.getByRole("button", { name: /send confirmation link/i }));

    await vi.waitFor(() => expect(toast.error).toHaveBeenCalledWith("That email is already in use."));
  });

  it("shows a generic error when the current password is wrong", async () => {
    server.use(
      http.post("http://localhost/api/v1/auth/change-email", () =>
        HttpResponse.json({ status: 422, message: "Incorrect password." }, { status: 422 }),
      ),
    );
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/current password/i), "WrongPassword1!");
    await user.type(screen.getByLabelText(/new email address/i), "new@test.com");
    await user.click(screen.getByRole("button", { name: /send confirmation link/i }));

    await vi.waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Failed to start email change. Check your current password."));
  });
});
