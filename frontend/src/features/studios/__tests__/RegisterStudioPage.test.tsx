import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { authApi } from "@/features/auth/authApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { RegisterStudioPage } from "@/features/studios/components/RegisterStudioPage";
import type { LocationPickerValue } from "@/shared/components/ui/location-picker";

// ── Mock LocationPicker ────────────────────────────────────────────────────────
// LocationPicker uses Leaflet and real map tiles — not viable in jsdom.
// The mock renders a button that fires onChange with a preset location.

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
        onClick={() => onChange({ lat: 38.7169, lng: -9.1395, city: "Lisbon" })}
      >
        Pick location
      </button>
      {error && <p data-testid="location-error">{error}</p>}
    </div>
  ),
}));

// ── Fake JWT ───────────────────────────────────────────────────────────────────

const ROLE_CLAIM = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

function toBase64Url(s: string) {
  return btoa(s).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function makeFakeJwt(role: string, email = "owner@test.com") {
  const header  = toBase64Url(JSON.stringify({ alg: "HS256", typ: "JWT" }));
  const payload = toBase64Url(JSON.stringify({
    sub:          "u-reg-test",
    email,
    [ROLE_CLAIM]:  role,
    tenant_id:    "t-reg",
    exp:           9_999_999_999,
  }));
  return `${header}.${payload}.fake-sig`;
}

// ── MSW server ─────────────────────────────────────────────────────────────────

const STUDIO_RESPONSE = {
  id:                   "stud-001",
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
  nipt:                 "L01234567A",
};

const server = setupServer(
  http.post("http://localhost/api/v1/studios", () =>
    HttpResponse.json(STUDIO_RESPONSE, { status: 201 }),
  ),
  http.post("http://localhost/api/v1/auth/register", () =>
    new HttpResponse(null, { status: 204 }),
  ),
  http.post("http://localhost/api/v1/auth/register/solo-artist", () =>
    new HttpResponse(null, { status: 204 }),
  ),
  http.post("http://localhost/api/v1/auth/login", () =>
    HttpResponse.json({ accessToken: makeFakeJwt("owner"), tokenType: "Bearer" }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); localStorage.clear(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                    authReducer,
      [authApi.reducerPath]:   authApi.reducer,
      [studiosApi.reducerPath]: studiosApi.reducer,
    },
    middleware: (gd) => gd().concat(authApi.middleware, studiosApi.middleware),
  });
}

function renderPage(initialPath = "/register") {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[initialPath]}>
        <Routes>
          <Route path="/register"  element={<RegisterStudioPage />} />
          <Route path="/dashboard" element={<div data-testid="dashboard" />} />
          <Route path="/book"      element={<div data-testid="client-home" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Step helpers ──────────────────────────────────────────────────────────────

async function fillStep1(user: ReturnType<typeof userEvent.setup>, studioName = "Ink & Soul Studio") {
  await user.type(screen.getByLabelText(/studio name/i), studioName);
  await user.type(screen.getByLabelText(/business tax id/i), "L01234567A");
  await user.click(screen.getByTestId("mock-location-picker"));
}

async function advanceToStep2(user: ReturnType<typeof userEvent.setup>, studioName = "Ink & Soul Studio") {
  await fillStep1(user, studioName);
  await user.click(screen.getByRole("button", { name: /next/i }));
  await screen.findByLabelText(/^email$/i);
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("RegisterStudioPage — step 1", () => {
  it("renders step 1 form fields", () => {
    renderPage();
    expect(screen.getByRole("heading", { name: /register your studio/i })).toBeInTheDocument();
    expect(screen.getByText(/step 1 of 2/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/studio name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/url slug/i)).toBeInTheDocument();
    expect(screen.getByTestId("mock-location-picker")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /next/i })).toBeInTheDocument();
  });

  it("auto-generates a slug from the studio name", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/studio name/i), "Ink & Soul Studio");

    const slugInput = screen.getByLabelText<HTMLInputElement>(/url slug/i);
    // & is stripped, spaces become hyphens, consecutive hyphens collapsed
    expect(slugInput.value).toBe("ink-soul-studio");
  });

  it("slug shown in URL preview below the field", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/studio name/i), "My Studio");

    expect(screen.getByText(/tattooos\.co\//i)).toBeInTheDocument();
  });

  it("shows validation errors when Next is clicked with no data", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole("button", { name: /next/i }));

    expect(await screen.findByText(/studio name is required/i)).toBeInTheDocument();
  });

  it("shows location error when Next is clicked without picking a location", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/studio name/i), "My Studio");
    // Do NOT pick a location
    await user.click(screen.getByRole("button", { name: /next/i }));

    // Latitude or city will be required
    expect(await screen.findByTestId("location-error")).toBeInTheDocument();
  });

  it("shows validation error when NIPT is left empty", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/studio name/i), "My Studio");
    await user.click(screen.getByTestId("mock-location-picker"));
    await user.click(screen.getByRole("button", { name: /next/i }));

    expect(await screen.findByText(/nipt must be exactly 10 characters/i)).toBeInTheDocument();
  });

  it.each(["L0123456A", "L012345678A", "0101234567A", "L01234567"])(
    "rejects malformed NIPT %s",
    async (badNipt) => {
      const user = userEvent.setup();
      renderPage();

      await user.type(screen.getByLabelText(/studio name/i), "My Studio");
      await user.type(screen.getByLabelText(/business tax id/i), badNipt);
      await user.click(screen.getByTestId("mock-location-picker"));
      await user.click(screen.getByRole("button", { name: /next/i }));

      expect(screen.queryByText(/step 2 of 2/i)).not.toBeInTheDocument();
    },
  );

  it("advances to step 2 when all step-1 fields are valid", async () => {
    const user = userEvent.setup();
    renderPage();

    await advanceToStep2(user);

    expect(screen.getByText(/step 2 of 2/i)).toBeInTheDocument();
    expect(screen.getByText(/owner account/i)).toBeInTheDocument();
  });

  it("dispatches the referral code from ?ref= query param", () => {
    const store = renderPage("/register?ref=PROMO50");
    // Effect fires on mount
    expect(store.getState().auth.pendingReferralCode).toBe("PROMO50");
  });
});

describe("RegisterStudioPage — step 2", () => {
  it("renders step 2 form fields", async () => {
    const user = userEvent.setup();
    renderPage();

    await advanceToStep2(user);

    expect(screen.getByLabelText(/^email$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^password$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/confirm password/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /register/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /back/i })).toBeInTheDocument();
  });

  it("Back button returns to step 1", async () => {
    const user = userEvent.setup();
    renderPage();

    await advanceToStep2(user);
    await user.click(screen.getByRole("button", { name: /back/i }));

    expect(screen.getByText(/step 1 of 2/i)).toBeInTheDocument();
  });

  it("shows email-required error on empty submit", async () => {
    const user = userEvent.setup();
    renderPage();

    await advanceToStep2(user);
    await user.click(screen.getByRole("button", { name: /register/i }));

    expect(await screen.findByText(/email is required/i)).toBeInTheDocument();
  });

  it("shows password-min-length error when password is too short", async () => {
    const user = userEvent.setup();
    renderPage();

    await advanceToStep2(user);
    await user.type(screen.getByLabelText(/^email$/i), "owner@test.com");
    await user.type(screen.getByLabelText(/^password$/i), "short");
    await user.type(screen.getByLabelText(/confirm password/i), "short");
    await user.click(screen.getByRole("button", { name: /register/i }));

    expect(await screen.findByText(/password must be at least 8 characters/i)).toBeInTheDocument();
  });

  it("shows password-mismatch error when passwords differ", async () => {
    const user = userEvent.setup();
    renderPage();

    await advanceToStep2(user);
    await user.type(screen.getByLabelText(/^email$/i), "owner@test.com");
    await user.type(screen.getByLabelText(/^password$/i), "ValidPass1!");
    await user.type(screen.getByLabelText(/confirm password/i), "Different1!");
    await user.click(screen.getByRole("button", { name: /register/i }));

    expect(await screen.findByText(/passwords do not match/i)).toBeInTheDocument();
  });

  it("successful registration navigates to /dashboard", async () => {
    const user = userEvent.setup({ delay: null });
    renderPage();

    await advanceToStep2(user);
    await user.type(screen.getByLabelText(/^email$/i), "owner@test.com");
    await user.type(screen.getByLabelText(/^password$/i), "ValidPass1!");
    await user.type(screen.getByLabelText(/confirm password/i), "ValidPass1!");
    await user.click(screen.getByRole("button", { name: /register/i }));

    await screen.findByTestId("dashboard");
  });

  it("dispatches credentials after successful registration", async () => {
    const user  = userEvent.setup({ delay: null });
    const store = renderPage();

    await advanceToStep2(user);
    await user.type(screen.getByLabelText(/^email$/i), "owner@test.com");
    await user.type(screen.getByLabelText(/^password$/i), "ValidPass1!");
    await user.type(screen.getByLabelText(/confirm password/i), "ValidPass1!");
    await user.click(screen.getByRole("button", { name: /register/i }));

    await screen.findByTestId("dashboard");

    expect(store.getState().auth.role).toBe("owner");
    expect(store.getState().auth.token).toBeTruthy();
  });

  it("clears the pending referral code after successful registration", async () => {
    const user  = userEvent.setup({ delay: null });
    const store = renderPage("/register?ref=PROMO50");

    await advanceToStep2(user);
    await user.type(screen.getByLabelText(/^email$/i), "owner@test.com");
    await user.type(screen.getByLabelText(/^password$/i), "ValidPass1!");
    await user.type(screen.getByLabelText(/confirm password/i), "ValidPass1!");
    await user.click(screen.getByRole("button", { name: /register/i }));

    await screen.findByTestId("dashboard");

    expect(store.getState().auth.pendingReferralCode).toBeNull();
  });

  it("includes the NIPT in the registerStudio mutation payload", async () => {
    let capturedBody: Record<string, unknown> | null = null;
    server.use(
      http.post("http://localhost/api/v1/studios", async ({ request }) => {
        capturedBody = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(STUDIO_RESPONSE, { status: 201 });
      }),
    );

    const user = userEvent.setup({ delay: null });
    renderPage();

    await advanceToStep2(user);
    await user.type(screen.getByLabelText(/^email$/i), "owner@test.com");
    await user.type(screen.getByLabelText(/^password$/i), "ValidPass1!");
    await user.type(screen.getByLabelText(/confirm password/i), "ValidPass1!");
    await user.click(screen.getByRole("button", { name: /register/i }));

    await screen.findByTestId("dashboard");

    expect(capturedBody).toMatchObject({ nipt: "L01234567A" });
  });

  it("shows server error when studio registration fails", async () => {
    server.use(
      http.post("http://localhost/api/v1/studios", () =>
        HttpResponse.json({ message: "Slug already taken." }, { status: 409 }),
      ),
    );

    const user = userEvent.setup();
    renderPage();

    await advanceToStep2(user);
    await user.type(screen.getByLabelText(/^email$/i), "owner@test.com");
    await user.type(screen.getByLabelText(/^password$/i), "ValidPass1!");
    await user.type(screen.getByLabelText(/confirm password/i), "ValidPass1!");
    await user.click(screen.getByRole("button", { name: /register/i }));

    expect(await screen.findByText("Slug already taken.")).toBeInTheDocument();
  }, 15_000);

  it("shows network-error message when fetch fails", async () => {
    server.use(
      http.post("http://localhost/api/v1/studios", () => HttpResponse.error()),
    );

    const user = userEvent.setup();
    renderPage();

    await advanceToStep2(user);
    await user.type(screen.getByLabelText(/^email$/i), "owner@test.com");
    await user.type(screen.getByLabelText(/^password$/i), "ValidPass1!");
    await user.type(screen.getByLabelText(/confirm password/i), "ValidPass1!");
    await user.click(screen.getByRole("button", { name: /register/i }));

    expect(await screen.findByText(/unable to reach the server/i)).toBeInTheDocument();
  }, 15_000);
});

describe("RegisterStudioPage — solo artist mode", () => {
  async function switchToSoloMode(user: ReturnType<typeof userEvent.setup>) {
    await user.click(screen.getByRole("button", { name: /i'm an independent artist/i }));
  }

  it("shows a registration-type toggle at step 1", () => {
    renderPage();
    expect(screen.getByRole("button", { name: /i run a studio/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /i'm an independent artist/i })).toBeInTheDocument();
  });

  it("switches to the minimal solo-artist form", async () => {
    const user = userEvent.setup();
    renderPage();

    await switchToSoloMode(user);

    expect(screen.getByRole("heading", { name: /register as an independent artist/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/first name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/last name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^email$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^password$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/confirm password/i)).toBeInTheDocument();
    // No studio-specific fields in solo mode
    expect(screen.queryByLabelText(/studio name/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/business tax id/i)).not.toBeInTheDocument();
  });

  it("shows validation errors on empty submit", async () => {
    const user = userEvent.setup();
    renderPage();

    await switchToSoloMode(user);
    await user.click(screen.getByRole("button", { name: /create my account/i }));

    expect(await screen.findByText(/first name is required/i)).toBeInTheDocument();
  });

  it("shows password-mismatch error when passwords differ", async () => {
    const user = userEvent.setup();
    renderPage();

    await switchToSoloMode(user);
    await user.type(screen.getByLabelText(/first name/i), "Jane");
    await user.type(screen.getByLabelText(/last name/i), "Doe");
    await user.type(screen.getByLabelText(/^email$/i), "jane@test.com");
    await user.type(screen.getByLabelText(/^password$/i), "ValidPass1!");
    await user.type(screen.getByLabelText(/confirm password/i), "Different1!");
    await user.click(screen.getByRole("button", { name: /create my account/i }));

    expect(await screen.findByText(/passwords do not match/i)).toBeInTheDocument();
  });

  it("successful solo registration navigates to /dashboard and dispatches credentials", async () => {
    const user  = userEvent.setup({ delay: null });
    const store = renderPage();

    await switchToSoloMode(user);
    await user.type(screen.getByLabelText(/first name/i), "Jane");
    await user.type(screen.getByLabelText(/last name/i), "Doe");
    await user.type(screen.getByLabelText(/^email$/i), "jane@test.com");
    await user.type(screen.getByLabelText(/^password$/i), "ValidPass1!");
    await user.type(screen.getByLabelText(/confirm password/i), "ValidPass1!");
    await user.click(screen.getByRole("button", { name: /create my account/i }));

    await screen.findByTestId("dashboard");

    expect(store.getState().auth.role).toBe("owner");
    expect(store.getState().auth.token).toBeTruthy();
  });

  it("shows server error when solo registration fails", async () => {
    server.use(
      http.post("http://localhost/api/v1/auth/register/solo-artist", () =>
        HttpResponse.json({ message: "Email already registered." }, { status: 409 }),
      ),
    );

    const user = userEvent.setup();
    renderPage();

    await switchToSoloMode(user);
    await user.type(screen.getByLabelText(/first name/i), "Jane");
    await user.type(screen.getByLabelText(/last name/i), "Doe");
    await user.type(screen.getByLabelText(/^email$/i), "jane@test.com");
    await user.type(screen.getByLabelText(/^password$/i), "ValidPass1!");
    await user.type(screen.getByLabelText(/confirm password/i), "ValidPass1!");
    await user.click(screen.getByRole("button", { name: /create my account/i }));

    expect(await screen.findByText("Email already registered.")).toBeInTheDocument();
  }, 15_000);

  it("switching back to studio mode restores the multi-step studio form", async () => {
    const user = userEvent.setup();
    renderPage();

    await switchToSoloMode(user);
    await user.click(screen.getByRole("button", { name: /i run a studio/i }));

    expect(screen.getByRole("heading", { name: /register your studio/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/studio name/i)).toBeInTheDocument();
  });
});
