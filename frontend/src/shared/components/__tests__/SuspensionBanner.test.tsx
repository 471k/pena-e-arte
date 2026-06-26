import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";

import { SuspensionBanner } from "@/shared/components/SuspensionBanner";
import type { StudioResponse } from "@/features/studios/studiosApi";
import uiReducer from "@/features/ui/uiSlice";
import authReducer from "@/features/auth/authSlice";

// ── Fixtures ───────────────────────────────────────────────────────────────────

const SUSPENDED_STUDIO: StudioResponse = {
  id:                   "s1",
  name:                 "Ink Soul",
  slug:                 "ink-soul",
  city:                 "Porto",
  latitude:             41.15,
  longitude:            -8.61,
  showPlatformBranding: true,
  allowBrandingRemoval: false,
  trialExpiresAt:       "2099-01-01T00:00:00Z",
  createdAt:            "2024-01-01T00:00:00Z",
  isActive:             false,
  slugLockedAt:         null,
};

const ACTIVE_STUDIO: StudioResponse = { ...SUSPENDED_STUDIO, isActive: true };

// ── Store helper ───────────────────────────────────────────────────────────────

function makeStoreWithSuspension(suspended: boolean) {
  return configureStore({
    reducer: { auth: authReducer, ui: uiReducer },
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: null, token: null, tenantId: null, role: null, pendingReferralCode: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: suspended },
    },
  });
}

// ── Render helpers ─────────────────────────────────────────────────────────────

function renderBanner(props: Parameters<typeof SuspensionBanner>[0] = {}, suspended = false) {
  const store = makeStoreWithSuspension(suspended);
  render(
    <Provider store={store}>
      <MemoryRouter>
        <SuspensionBanner {...props} />
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("SuspensionBanner", () => {
  // ── studio-prop path (owner layout) ─────────────────────────────────────────

  it("renders without crashing when studio is suspended", () => {
    renderBanner({ studio: SUSPENDED_STUDIO });
    expect(screen.getByText(/suspended/i)).toBeInTheDocument();
  });

  it("does not render when studio is active", () => {
    renderBanner({ studio: ACTIVE_STUDIO });
    expect(screen.queryByText(/suspended/i)).not.toBeInTheDocument();
  });

  it("does not render when studio is undefined and Redux flag is false", () => {
    const { container } = (() => {
      const store = makeStoreWithSuspension(false);
      return render(
        <Provider store={store}>
          <MemoryRouter>
            <SuspensionBanner />
          </MemoryRouter>
        </Provider>,
      );
    })();
    expect(container.firstChild).toBeNull();
  });

  it("contains text about platform administrator", () => {
    renderBanner({ studio: SUSPENDED_STUDIO });
    expect(screen.getByText(/platform administrator/i)).toBeInTheDocument();
  });

  it("contains a link to /subscribe for owner role", () => {
    renderBanner({ studio: SUSPENDED_STUDIO });
    const link = screen.getByRole("link", { name: /reactivate your subscription/i });
    expect(link.getAttribute("href")).toContain("/subscribe");
  });

  // ── Redux-driven path (artist / client layouts) ──────────────────────────────

  it("renders when studioSuspended is true in Redux state (no studio prop)", () => {
    renderBanner({}, true);
    expect(screen.getByRole("alert")).toBeInTheDocument();
  });

  it("does not render when studioSuspended is false and no studio prop", () => {
    renderBanner({}, false);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("renders artist-role copy when role='artist'", () => {
    renderBanner({ role: "artist" }, true);
    expect(screen.getByText(/contact your studio owner/i)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /reactivate your subscription/i })).not.toBeInTheDocument();
  });

  it("renders client-role copy when role='client'", () => {
    renderBanner({ role: "client" }, true);
    expect(screen.getByText(/contact the studio/i)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /reactivate your subscription/i })).not.toBeInTheDocument();
  });

  it("renders owner reactivation link when role='owner' (default) with studio prop", () => {
    renderBanner({ studio: SUSPENDED_STUDIO });
    expect(screen.getByRole("link", { name: /reactivate your subscription/i })).toBeInTheDocument();
  });
});
