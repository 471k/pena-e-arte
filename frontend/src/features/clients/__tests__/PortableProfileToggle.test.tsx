import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { clientsApi } from "@/features/clients/clientsApi";
import { PortableProfileToggle } from "@/features/clients/components/PortableProfileToggle";

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.patch("http://localhost/api/v1/clients/me/portable-profile", () =>
    new HttpResponse(null, { status: 204 }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth: authReducer,
      [clientsApi.reducerPath]: clientsApi.reducer,
    },
    middleware: (gd) => gd().concat(clientsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "client@test.com" }, token: "fake", tenantId: "t1", role: "client" } as any,
    },
  });
}

function renderToggle(currentOptIn: boolean) {
  render(
    <Provider store={makeStore()}>
      <PortableProfileToggle currentOptIn={currentOptIn} />
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("PortableProfileToggle", () => {
  it("shows 'Off' when currentOptIn is false", () => {
    renderToggle(false);
    expect(screen.getByRole("button", { name: "Off" })).toBeInTheDocument();
  });

  it("shows 'On' when currentOptIn is true", () => {
    renderToggle(true);
    expect(screen.getByRole("button", { name: "On" })).toBeInTheDocument();
  });

  it("does not show the warning banner when opted out", () => {
    renderToggle(false);
    expect(screen.queryByText(/any artist on tattooos.* will be able to view/i)).not.toBeInTheDocument();
  });

  it("shows the warning banner when opted in", () => {
    renderToggle(true);
    expect(screen.getByText(/any artist on tattooos.* will be able to view/i)).toBeInTheDocument();
  });

  it("clicking the toggle when off switches to 'On' and shows the warning banner", async () => {
    const user = userEvent.setup();
    renderToggle(false);
    await user.click(screen.getByRole("button", { name: "Off" }));
    expect(await screen.findByRole("button", { name: "On" })).toBeInTheDocument();
    expect(screen.getByText(/any artist on tattooos.* will be able to view/i)).toBeInTheDocument();
  });

  it("clicking the toggle when on switches to 'Off'", async () => {
    const user = userEvent.setup();
    renderToggle(true);
    await user.click(screen.getByRole("button", { name: "On" }));
    expect(await screen.findByRole("button", { name: "Off" })).toBeInTheDocument();
  });

  it("calls the mutation with the new opt-in value", async () => {
    let capturedBody: unknown = null;
    server.use(
      http.patch("http://localhost/api/v1/clients/me/portable-profile", async ({ request }) => {
        capturedBody = await request.json();
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const user = userEvent.setup();
    renderToggle(false);
    await user.click(screen.getByRole("button", { name: "Off" }));
    await screen.findByRole("button", { name: "On" });
    expect(capturedBody).toEqual({ optIn: true });
  });

  it("reverts to the previous state if the mutation fails", async () => {
    server.use(
      http.patch("http://localhost/api/v1/clients/me/portable-profile", () =>
        HttpResponse.json({ message: "Server error" }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    renderToggle(false);
    await user.click(screen.getByRole("button", { name: "Off" }));
    expect(await screen.findByRole("button", { name: "Off" })).toBeInTheDocument();
  });
});
