import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { authApi } from "@/features/auth/authApi";
import { StudioJoinInviteBell } from "@/features/auth/components/StudioJoinInviteBell";
import type { MyStudioJoinInviteResponse } from "@/features/auth/authApi";

// ── Fake JWT (for the accept response) ──────────────────────────────────────────

const ROLE_CLAIM = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

function toBase64Url(s: string) {
  return btoa(s).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function makeFakeJwt(role: string) {
  const header  = toBase64Url(JSON.stringify({ alg: "HS256", typ: "JWT" }));
  const payload = toBase64Url(JSON.stringify({
    sub: "u1", email: "jane@test.com", [ROLE_CLAIM]: role, tenant_id: "s-new", exp: 9_999_999_999,
  }));
  return `${header}.${payload}.fake-sig`;
}

// ── Seed data ──────────────────────────────────────────────────────────────────

const INVITES: MyStudioJoinInviteResponse[] = [
  {
    id:         "inv-1",
    studioId:   "s-new",
    studioName: "Ink Collective",
    studioSlug: "ink-collective",
    studioCity: "Lisbon",
    expiresAt:  "2099-01-01T00:00:00Z",
  },
];

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/auth/join-invites", () => HttpResponse.json(INVITES)),
  http.post("http://localhost/api/v1/auth/join-invites/:id/accept", () =>
    HttpResponse.json({ accessToken: makeFakeJwt("artist"), refreshToken: "r-1", tokenType: "Bearer" }),
  ),
  http.post("http://localhost/api/v1/auth/join-invites/:id/decline", () =>
    new HttpResponse(null, { status: 204 }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: { auth: authReducer, [authApi.reducerPath]: authApi.reducer },
    middleware: (gd) => gd().concat(authApi.middleware),
  });
}

function renderBell(enabled = true) {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/dashboard"]}>
        <Routes>
          <Route path="/dashboard" element={<StudioJoinInviteBell enabled={enabled} />} />
          <Route path="/schedule" element={<div data-testid="schedule-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("StudioJoinInviteBell", () => {
  it("does not fetch or render when enabled=false (non-solo owner)", async () => {
    let requestMade = false;
    server.use(http.get("http://localhost/api/v1/auth/join-invites", () => {
      requestMade = true;
      return HttpResponse.json(INVITES);
    }));
    renderBell(false);
    await new Promise((r) => setTimeout(r, 0));

    expect(requestMade).toBe(false);
    expect(screen.queryByLabelText(/pending/i)).not.toBeInTheDocument();
  });

  it("does not render when there are no pending invites", async () => {
    server.use(http.get("http://localhost/api/v1/auth/join-invites", () => HttpResponse.json([])));
    renderBell();
    // allow the query to resolve
    await new Promise((r) => setTimeout(r, 0));
    expect(screen.queryByLabelText(/pending/i)).not.toBeInTheDocument();
  });

  it("shows the pending-invite badge count", async () => {
    renderBell();
    expect(await screen.findByLabelText(/1 pending/i)).toBeInTheDocument();
  });

  it("opens the panel and shows the inviting studio", async () => {
    const user = userEvent.setup();
    renderBell();

    await user.click(await screen.findByLabelText(/pending/i));

    expect(await screen.findByText(/ink collective/i)).toBeInTheDocument();
    expect(screen.getByText(/lisbon/i)).toBeInTheDocument();
  });

  it("Accept opens a confirmation dialog before doing anything", async () => {
    const user = userEvent.setup();
    renderBell();

    await user.click(await screen.findByLabelText(/pending/i));
    await user.click(screen.getByRole("button", { name: /^accept$/i }));

    const dialog = await screen.findByRole("alertdialog");
    expect(within(dialog).getByText(/join ink collective/i)).toBeInTheDocument();
    expect(within(dialog).getByText(/closed/i)).toBeInTheDocument();
  });

  it("confirming Accept updates credentials and navigates to /schedule", async () => {
    const user  = userEvent.setup({ delay: null });
    const store = renderBell();

    await user.click(await screen.findByLabelText(/pending/i));
    await user.click(screen.getByRole("button", { name: /^accept$/i }));
    const dialog = await screen.findByRole("alertdialog");
    await user.click(within(dialog).getByRole("button", { name: /join studio/i }));

    await screen.findByTestId("schedule-page");
    expect(store.getState().auth.role).toBe("artist");
  });

  it("Decline removes the invite without a confirmation dialog", async () => {
    const user = userEvent.setup();
    renderBell();

    await user.click(await screen.findByLabelText(/pending/i));
    await user.click(screen.getByRole("button", { name: /^decline$/i }));

    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
  });
});
