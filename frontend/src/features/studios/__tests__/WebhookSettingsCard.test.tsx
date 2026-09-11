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
import { studiosApi, type StudioResponse, type WebhookEndpointStatusResponse } from "@/features/studios/studiosApi";
import { WebhookSettingsCard } from "@/features/studios/components/WebhookSettingsCard";

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

const NO_ENDPOINT: WebhookEndpointStatusResponse = {
  hasEndpoint: false, url: null, isActive: false, createdAt: null,
  lastDeliveryAt: null, lastDeliverySucceeded: null,
};

const ACTIVE_ENDPOINT: WebhookEndpointStatusResponse = {
  hasEndpoint: true, url: "https://example.com/hooks/pea", isActive: true,
  createdAt: "2026-01-01T00:00:00Z", lastDeliveryAt: null, lastDeliverySucceeded: null,
};

const DISABLED_ENDPOINT: WebhookEndpointStatusResponse = { ...ACTIVE_ENDPOINT, isActive: false };

// ── MSW server ────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json(STUDIO_WITH_API_ACCESS)),
  http.get("http://localhost/api/v1/studios/me/webhook", () => HttpResponse.json(NO_ENDPOINT)),
  http.get("http://localhost/api/v1/studios/me/webhook/deliveries", () => HttpResponse.json([])),
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
        <WebhookSettingsCard />
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("WebhookSettingsCard", () => {

  it("shows an upgrade hint when the plan does not include API access", async () => {
    server.use(http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json(STUDIO_NO_API_ACCESS)));
    renderCard();
    expect(await screen.findByText(/upgrade to the pro plan/i)).toBeInTheDocument();
    expect(screen.queryByPlaceholderText(/webhooks\/pena-e-arte/i)).not.toBeInTheDocument();
  });

  it("shows a URL input when the plan allows access and no endpoint exists yet", async () => {
    renderCard();
    expect(await screen.findByPlaceholderText(/webhooks\/pena-e-arte/i)).toBeInTheDocument();
  });

  it("saving a URL shows the signing secret exactly once, in its own dialog", async () => {
    server.use(
      http.post("http://localhost/api/v1/studios/me/webhook", () =>
        HttpResponse.json({ url: "https://example.com/hooks/pea", secret: "whsec_fullsecretvalue", createdAt: "2026-01-01T00:00:00Z" }),
      ),
    );
    const user = userEvent.setup();
    renderCard();

    const input = await screen.findByPlaceholderText(/webhooks\/pena-e-arte/i);
    await user.type(input, "https://example.com/hooks/pea");
    await user.click(screen.getByRole("button", { name: /^save$/i }));

    expect(await screen.findByText("Your webhook signing secret")).toBeInTheDocument();
    expect(screen.getByText("whsec_fullsecretvalue")).toBeInTheDocument();
  });

  it("closing the new-secret dialog via Done removes it from the DOM", async () => {
    server.use(
      http.post("http://localhost/api/v1/studios/me/webhook", () =>
        HttpResponse.json({ url: "https://example.com/hooks/pea", secret: "whsec_fullsecretvalue", createdAt: "2026-01-01T00:00:00Z" }),
      ),
    );
    const user = userEvent.setup();
    renderCard();
    const input = await screen.findByPlaceholderText(/webhooks\/pena-e-arte/i);
    await user.type(input, "https://example.com/hooks/pea");
    await user.click(screen.getByRole("button", { name: /^save$/i }));
    await screen.findByText("Your webhook signing secret");

    await user.click(screen.getByRole("button", { name: /^done$/i }));

    await waitFor(() => expect(screen.queryByText("Your webhook signing secret")).not.toBeInTheDocument());
  });

  it("shows a server validation message when saving an invalid URL fails", async () => {
    server.use(
      http.post("http://localhost/api/v1/studios/me/webhook", () =>
        HttpResponse.json({ message: "The webhook URL must be a public HTTPS address." }, { status: 400 }),
      ),
    );
    const user = userEvent.setup();
    renderCard();

    const input = await screen.findByPlaceholderText(/webhooks\/pena-e-arte/i);
    await user.type(input, "http://localhost/hook");
    await user.click(screen.getByRole("button", { name: /^save$/i }));

    expect(await screen.findByText(/public https address/i)).toBeInTheDocument();
  });

  it("shows the active endpoint's URL and a Send test event button", async () => {
    server.use(http.get("http://localhost/api/v1/studios/me/webhook", () => HttpResponse.json(ACTIVE_ENDPOINT)));
    renderCard();
    expect(await screen.findByText("https://example.com/hooks/pea")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /send test event/i })).toBeEnabled();
  });

  it("shows a disabled warning and a disabled test-event button for an inactive endpoint", async () => {
    server.use(http.get("http://localhost/api/v1/studios/me/webhook", () => HttpResponse.json(DISABLED_ENDPOINT)));
    renderCard();
    expect(await screen.findByText(/disabled after repeated delivery failures/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /send test event/i })).toBeDisabled();
  });

  it("clicking Send test event calls the test endpoint", async () => {
    let testCalled = false;
    server.use(
      http.get("http://localhost/api/v1/studios/me/webhook", () => HttpResponse.json(ACTIVE_ENDPOINT)),
      http.post("http://localhost/api/v1/studios/me/webhook/test", () => {
        testCalled = true;
        return new HttpResponse(null, { status: 202 });
      }),
    );
    const user = userEvent.setup();
    renderCard();

    await user.click(await screen.findByRole("button", { name: /send test event/i }));

    await waitFor(() => expect(testCalled).toBe(true));
  });

  it("removing an endpoint calls the DELETE endpoint after confirming", async () => {
    let deleteCalled = false;
    server.use(
      http.get("http://localhost/api/v1/studios/me/webhook", () => HttpResponse.json(ACTIVE_ENDPOINT)),
      http.delete("http://localhost/api/v1/studios/me/webhook", () => {
        deleteCalled = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const user = userEvent.setup();
    renderCard();

    await user.click(await screen.findByRole("button", { name: /remove/i }));
    await user.click(await screen.findByRole("button", { name: /^remove$/i }));

    await waitFor(() => expect(deleteCalled).toBe(true));
  });

  it("cancelling the remove confirmation does not call the endpoint", async () => {
    let deleteCalled = false;
    server.use(
      http.get("http://localhost/api/v1/studios/me/webhook", () => HttpResponse.json(ACTIVE_ENDPOINT)),
      http.delete("http://localhost/api/v1/studios/me/webhook", () => {
        deleteCalled = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const user = userEvent.setup();
    renderCard();

    await user.click(await screen.findByRole("button", { name: /remove/i }));
    await user.click(await screen.findByRole("button", { name: /cancel/i }));

    expect(deleteCalled).toBe(false);
  });

  it("shows the delivery log when deliveries exist", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios/me/webhook", () => HttpResponse.json(ACTIVE_ENDPOINT)),
      http.get("http://localhost/api/v1/studios/me/webhook/deliveries", () => HttpResponse.json([
        { id: "d1", eventType: "appointment.created", responseStatusCode: 200, succeeded: true, errorMessage: null, attemptedAt: "2026-01-02T00:00:00Z" },
        { id: "d2", eventType: "appointment.cancelled", responseStatusCode: 500, succeeded: false, errorMessage: "boom", attemptedAt: "2026-01-01T00:00:00Z" },
      ])),
    );
    renderCard();

    expect(await screen.findByText("appointment.created")).toBeInTheDocument();
    expect(screen.getByText("Delivered")).toBeInTheDocument();
    expect(screen.getByText("Failed (500)")).toBeInTheDocument();
  });
});
