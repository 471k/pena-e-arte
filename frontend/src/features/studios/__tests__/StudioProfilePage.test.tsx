import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { studiosApi } from "@/features/studios/studiosApi";
import { StudioProfilePage } from "@/features/studios/components/StudioProfilePage";
import type { LocationPickerValue } from "@/shared/components/ui/location-picker";

// ── Mocks ─────────────────────────────────────────────────────────────────────

vi.mock("@/shared/components/ui/location-picker", () => ({
  LocationPicker: ({
    onChange,
    error,
  }: {
    onChange: (val: LocationPickerValue) => void;
    error?: string;
  }) => (
    <div>
      <button
        type="button"
        data-testid="mock-location-picker"
        onClick={() => onChange({ lat: 40.0, lng: -8.0, city: "Coimbra" })}
      >
        Pick location
      </button>
      {error && <p data-testid="location-error">{error}</p>}
    </div>
  ),
}));

// Prevent SubscriptionGatedButton from calling subscription/studio APIs
vi.mock("@/features/billing/useSubscriptionGuard", () => ({
  useSubscriptionGuard: () => ({ isReadOnly: false, isSuspended: false, cause: null, status: "Active" }),
}));

// These sub-components are covered by their own test files
vi.mock("@/features/studios/components/BrandingSettingsCard", () => ({
  BrandingSettingsCard: () => <div data-testid="branding-settings-card" />,
}));
vi.mock("@/features/studios/components/QrCodeSection", () => ({
  QrCodeSection: () => <div data-testid="qr-code-section" />,
}));
vi.mock("@/features/studios/components/ReferralCodeCard", () => ({
  ReferralCodeCard: () => <div data-testid="referral-code-card" />,
}));

// ── Fixtures ──────────────────────────────────────────────────────────────────

const STUDIO = {
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
};

// ── MSW server ────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json(STUDIO)),
  http.put("http://localhost/api/v1/studios/me", () => HttpResponse.json(STUDIO)),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ───────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                      authReducer,
      [studiosApi.reducerPath]:  studiosApi.reducer,
    },
    middleware: (gd) => gd().concat(studiosApi.middleware),
  });
}

function renderPage() {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter>
        <StudioProfilePage />
      </MemoryRouter>
    </Provider>,
  );
}

// Wait for the form to finish loading (studio data arrived + form populated)
async function waitForForm() {
  return screen.findByLabelText<HTMLInputElement>(/studio name/i);
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("StudioProfilePage — loading state", () => {
  it("shows a loading spinner while studio data is being fetched", () => {
    renderPage();
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });
});

describe("StudioProfilePage — after data loads", () => {
  it("pre-fills the name input with the studio name", async () => {
    renderPage();
    const nameInput = await waitForForm();
    expect(nameInput.value).toBe("Ink & Soul Studio");
  });

  it("shows the studio slug in the info card", async () => {
    renderPage();
    await waitForForm();
    expect(screen.getByText("ink-soul-studio")).toBeInTheDocument();
  });

  it("shows the registration date in the info card", async () => {
    renderPage();
    await waitForForm();
    expect(screen.getByText(/registered/i)).toBeInTheDocument();
  });

  it("renders child section placeholders", async () => {
    renderPage();
    await waitForForm();
    expect(screen.getByTestId("branding-settings-card")).toBeInTheDocument();
    expect(screen.getByTestId("qr-code-section")).toBeInTheDocument();
    expect(screen.getByTestId("referral-code-card")).toBeInTheDocument();
  });
});

describe("StudioProfilePage — form validation", () => {
  it("shows a validation error when the name is cleared and save is clicked", async () => {
    const user = userEvent.setup();
    renderPage();
    await waitForForm();

    await user.clear(screen.getByLabelText(/studio name/i));
    await user.click(screen.getByRole("button", { name: /save changes/i }));

    expect(await screen.findByText(/name is required/i)).toBeInTheDocument();
  });
});

describe("StudioProfilePage — save behaviour", () => {
  it("save button is disabled when the form is clean", async () => {
    renderPage();
    const btn = await screen.findByRole("button", { name: /save changes/i });
    expect(btn).toBeDisabled();
  });

  it("save button is enabled after the user edits the name", async () => {
    const user = userEvent.setup();
    renderPage();
    await waitForForm();

    await user.type(screen.getByLabelText(/studio name/i), " Extra");
    expect(screen.getByRole("button", { name: /save changes/i })).not.toBeDisabled();
  });

  it("shows a success message after a successful save", async () => {
    const user = userEvent.setup();
    renderPage();
    await waitForForm();

    await user.type(screen.getByLabelText(/studio name/i), " Extra");
    await user.click(screen.getByRole("button", { name: /save changes/i }));

    expect(await screen.findByText(/changes saved/i)).toBeInTheDocument();
  });

  it("save button is disabled again (form clean) after a successful save", async () => {
    const user = userEvent.setup();
    renderPage();
    await waitForForm();

    await user.type(screen.getByLabelText(/studio name/i), " Extra");
    await user.click(screen.getByRole("button", { name: /save changes/i }));

    await screen.findByText(/changes saved/i);
    expect(screen.getByRole("button", { name: /save changes/i })).toBeDisabled();
  });

  it("shows the server error message when save fails with a 400", async () => {
    server.use(
      http.put("http://localhost/api/v1/studios/me", () =>
        HttpResponse.json({ message: "Name is too long." }, { status: 400 }),
      ),
    );

    const user = userEvent.setup();
    renderPage();
    await waitForForm();

    await user.type(screen.getByLabelText(/studio name/i), " Extra");
    await user.click(screen.getByRole("button", { name: /save changes/i }));

    expect(await screen.findByText("Name is too long.")).toBeInTheDocument();
  });

  it("shows a generic error message when save fails with a network error", async () => {
    server.use(
      http.put("http://localhost/api/v1/studios/me", () => HttpResponse.error()),
    );

    const user = userEvent.setup();
    renderPage();
    await waitForForm();

    await user.type(screen.getByLabelText(/studio name/i), " Extra");
    await user.click(screen.getByRole("button", { name: /save changes/i }));

    expect(await screen.findByText(/unable to save changes/i)).toBeInTheDocument();
  });
});

describe("StudioProfilePage — location picker", () => {
  it("enables the save button after picking a new location", async () => {
    const user = userEvent.setup();
    renderPage();
    await waitForForm();

    // Picker fires with Coimbra coords — different from studio's Lisbon coords
    await user.click(screen.getByTestId("mock-location-picker"));

    expect(screen.getByRole("button", { name: /save changes/i })).not.toBeDisabled();
  });
});
