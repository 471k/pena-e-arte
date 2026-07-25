import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import { ConfirmChangeEmailPage } from "@/features/auth/components/ConfirmChangeEmailPage";

const server = setupServer();

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

function renderAt(path: string) {
  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/confirm-change-email" element={<ConfirmChangeEmailPage />} />
        <Route path="/login" element={<div data-testid="login-page" />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("ConfirmChangeEmailPage", () => {
  it("shows success when the backend confirms the token", async () => {
    server.use(
      http.get("http://localhost/api/v1/auth/confirm-change-email", () => new HttpResponse(null, { status: 204 })),
    );

    renderAt("/confirm-change-email?userId=u1&newEmail=new%40test.com&token=abc");

    expect(await screen.findByText(/email changed!/i)).toBeInTheDocument();
  });

  it("shows an error when the token is invalid", async () => {
    server.use(
      http.get("http://localhost/api/v1/auth/confirm-change-email", () =>
        HttpResponse.json({ status: 422, message: "This email-change link is invalid or has expired." }, { status: 422 }),
      ),
    );

    renderAt("/confirm-change-email?userId=u1&newEmail=new%40test.com&token=bad");

    expect(await screen.findByText(/confirmation failed/i)).toBeInTheDocument();
  });

  it("shows an error when required query params are missing", async () => {
    renderAt("/confirm-change-email");

    expect(await screen.findByText(/confirmation failed/i)).toBeInTheDocument();
  });
});
