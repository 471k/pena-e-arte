import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { Toaster } from "sonner";

import authReducer from "@/features/auth/authSlice";
import { studiosApi, type StudioResponse, type StudioApiKeyStatusResponse } from "@/features/studios/studiosApi";
import { DeveloperSettingsCard } from "@/features/studios/components/DeveloperSettingsCard";

// ── Fixtures ─────────────────────────────────────────────────────────────────

const STUDIO_NO_API_ACCESS: StudioResponse = {
  id:                   "studio-001",
  name:                 "Ink & Soul Studio",
  slug:                 "ink-soul-studio",
  city:                 "Lisbon",
  latitude:             38.7169,
  longitude:            -9.1395,
  showPlatformBranding: true,
  allowBrandingRemoval: false,
  allowApiAccess:       false,
  trialExpiresAt:       "2099-01-01T00:00:00Z",
  createdAt:            "2025-01-01T00:00:00Z",
  isActive:             true,
  slugLockedAt:         null,
  phoneNumber:          null,
  instagramHandle:      null,
  nipt:                 null,
  isSolo:               false,
  isPublished:          true,
  timezone:             "Europe/Tirane",
};

const STUDIO_WITH_API_ACCESS: StudioResponse = { ...STUDIO_NO_API_ACCESS, allowApiAccess: true };

const NO_KEY_STATUS: StudioApiKeyStatusResponse = {
  hasActiveKey: false, keyPrefix: null, createdAt: null, lastUsedAt: null,
};

const ACTIVE_KEY_STATUS: StudioApiKeyStatusResponse = {
  hasActiveKey: true, keyPrefix: "tos_live_ab12", createdAt: "2026-01-01T00:00:00Z", lastUsedAt: null, // gitleaks:allow
};

// ── MSW server ────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json(STUDIO_WITH_API_ACCESS)),
  http.get("http://localhost/api/v1/studios/me/api-key", () => HttpResponse.json(NO_KEY_STATUS)),
);

beforeAll(() => {
  server.listen({ onUnhandledRequest: "error" });
  Object.assign(navigator, { clipboard: { writeText: vi.fn().mockResolvedValue(undefined) } });
});
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ───────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                     authReducer,
      [studiosApi.reducerPath]: studiosApi.reducer,
    },
    middleware: (gd) => gd().concat(studiosApi.middleware),
  });
}

function renderCard() {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter>
        <Toaster />
        <DeveloperSettingsCard />
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("DeveloperSettingsCard", () => {

  it("shows an upgrade hint when the plan does not include API access", async () => {
    server.use(http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json(STUDIO_NO_API_ACCESS)));
    renderCard();
    expect(await screen.findByText(/upgrade to the pro plan/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /generate api key/i })).not.toBeInTheDocument();
  });

  it("shows a Generate button when the plan allows access and no key exists yet", async () => {
    renderCard();
    expect(await screen.findByRole("button", { name: /generate api key/i })).toBeInTheDocument();
  });

  it("shows the active key's prefix and created date when one exists", async () => {
    server.use(http.get("http://localhost/api/v1/studios/me/api-key", () => HttpResponse.json(ACTIVE_KEY_STATUS)));
    renderCard();
    expect(await screen.findByText(/tos_live_ab12/)).toBeInTheDocument();
    expect(screen.getByText(/never used/i)).toBeInTheDocument();
  });

  it("generating a key shows the plaintext key exactly once, in its own dialog", async () => {
    server.use(
      http.post("http://localhost/api/v1/studios/me/api-key", () =>
        HttpResponse.json({ apiKey: "tos_live_fullsecretvalue", keyPrefix: "tos_live_full", createdAt: "2026-01-01T00:00:00Z" }),
      ),
    );
    const user = userEvent.setup();
    renderCard();

    await user.click(await screen.findByRole("button", { name: /generate api key/i }));

    expect(await screen.findByText("Your new API key")).toBeInTheDocument();
    expect(screen.getByText("tos_live_fullsecretvalue")).toBeInTheDocument();
  });

  it("closing the new-key dialog via Done removes it from the DOM", async () => {
    server.use(
      http.post("http://localhost/api/v1/studios/me/api-key", () =>
        HttpResponse.json({ apiKey: "tos_live_fullsecretvalue", keyPrefix: "tos_live_full", createdAt: "2026-01-01T00:00:00Z" }),
      ),
    );
    const user = userEvent.setup();
    renderCard();
    await user.click(await screen.findByRole("button", { name: /generate api key/i }));
    await screen.findByText("Your new API key");

    await user.click(screen.getByRole("button", { name: /^done$/i }));

    await waitFor(() => expect(screen.queryByText("Your new API key")).not.toBeInTheDocument());
  });

  it("revoking an active key calls the DELETE endpoint after confirming", async () => {
    let revokeCalled = false;
    server.use(
      http.get("http://localhost/api/v1/studios/me/api-key", () => HttpResponse.json(ACTIVE_KEY_STATUS)),
      http.delete("http://localhost/api/v1/studios/me/api-key", () => {
        revokeCalled = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const user = userEvent.setup();
    renderCard();

    await user.click(await screen.findByRole("button", { name: /revoke/i }));
    await user.click(await screen.findByRole("button", { name: /^revoke$/i }));

    await waitFor(() => expect(revokeCalled).toBe(true));
  });

  it("cancelling the revoke confirmation does not call the endpoint", async () => {
    let revokeCalled = false;
    server.use(
      http.get("http://localhost/api/v1/studios/me/api-key", () => HttpResponse.json(ACTIVE_KEY_STATUS)),
      http.delete("http://localhost/api/v1/studios/me/api-key", () => {
        revokeCalled = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const user = userEvent.setup();
    renderCard();

    await user.click(await screen.findByRole("button", { name: /revoke/i }));
    await user.click(await screen.findByRole("button", { name: /cancel/i }));

    expect(revokeCalled).toBe(false);
  });
});
