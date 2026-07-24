import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { studiosApi, type StudioResponse } from "@/features/studios/studiosApi";
import { BrandingSettingsCard } from "@/features/studios/components/BrandingSettingsCard";

// ── Fixtures ─────────────────────────────────────────────────────────────────

const STUDIO_BRANDING_ON: StudioResponse = {
  id:                   "studio-001",
  name:                 "Ink & Soul Studio",
  slug:                 "ink-soul-studio",
  city:                 "Lisbon",
  latitude:             38.7169,
  longitude:            -9.1395,
  showPlatformBranding: true,
  allowBrandingRemoval: false,
  trialExpiresAt:       "2099-01-01T00:00:00Z",
  createdAt:            "2025-01-01T00:00:00Z",
  isActive:             true,
  slugLockedAt:         null,
  phoneNumber:          null,
  instagramHandle:      null,
  nipt:                 null,
};

const STUDIO_BRANDING_REMOVABLE: StudioResponse = {
  ...STUDIO_BRANDING_ON,
  showPlatformBranding: true,
  allowBrandingRemoval: true,
};

const STUDIO_BRANDING_OFF: StudioResponse = {
  ...STUDIO_BRANDING_REMOVABLE,
  showPlatformBranding: false,
  allowBrandingRemoval: true,
};

// ── MSW server ────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/studios/me", () =>
    HttpResponse.json(STUDIO_BRANDING_ON),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
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
        <BrandingSettingsCard />
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("BrandingSettingsCard", () => {

  it("renders the card title", async () => {
    renderCard();
    expect(await screen.findByRole("heading", { name: /platform branding/i })).toBeInTheDocument();
  });

  it("shows the branding description text", async () => {
    renderCard();
    expect(await screen.findByText(/powered by pena e art/i)).toBeInTheDocument();
  });

  it("switch is checked when showPlatformBranding is true", async () => {
    renderCard();
    const sw = await screen.findByRole("switch");
    expect(sw).toHaveAttribute("aria-checked", "true");
  });

  it("switch is unchecked when showPlatformBranding is false", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios/me", () =>
        HttpResponse.json(STUDIO_BRANDING_OFF),
      ),
    );
    renderCard();
    const sw = await screen.findByRole("switch");
    expect(sw).toHaveAttribute("aria-checked", "false");
  });

  it("switch is disabled when plan does not allow branding removal and branding is on", async () => {
    renderCard();
    const sw = await screen.findByRole("switch");
    expect(sw).toBeDisabled();
  });

  it("switch is enabled when plan allows branding removal", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios/me", () =>
        HttpResponse.json(STUDIO_BRANDING_REMOVABLE),
      ),
    );
    renderCard();
    const sw = await screen.findByRole("switch");
    expect(sw).not.toBeDisabled();
  });

  it("shows upgrade hint text when plan does not allow branding removal", async () => {
    renderCard();
    expect(await screen.findByText(/upgrade your plan/i)).toBeInTheDocument();
  });

  it("does NOT show upgrade hint when plan allows removal", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios/me", () =>
        HttpResponse.json(STUDIO_BRANDING_REMOVABLE),
      ),
    );
    renderCard();
    await screen.findByRole("switch");
    expect(screen.queryByText(/upgrade your plan/i)).not.toBeInTheDocument();
  });

  it("calls updateBranding with inverted showPlatformBranding when switch is clicked", async () => {
    const updateSpy = vi.fn();
    server.use(
      http.get("http://localhost/api/v1/studios/me", () =>
        HttpResponse.json(STUDIO_BRANDING_REMOVABLE),
      ),
      http.patch("http://localhost/api/v1/studios/studio-001/branding", async ({ request }) => {
        const body = await request.json();
        updateSpy(body);
        return HttpResponse.json({ ...STUDIO_BRANDING_REMOVABLE, showPlatformBranding: false });
      }),
    );

    const user = userEvent.setup();
    renderCard();
    const sw = await screen.findByRole("switch");
    await user.click(sw);

    await waitFor(() => expect(updateSpy).toHaveBeenCalledOnce());
    expect(updateSpy).toHaveBeenCalledWith(
      expect.objectContaining({ showPlatformBranding: false }),
    );
  });

});
