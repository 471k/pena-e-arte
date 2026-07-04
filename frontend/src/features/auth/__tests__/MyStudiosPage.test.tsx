import { describe, it, expect, beforeAll, afterEach, afterAll, vi } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { toast } from "sonner";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { authApi } from "@/features/auth/authApi";
import type { MyStudioResponse } from "@/features/auth/authApi";
import { MyStudiosPage } from "@/features/auth/components/MyStudiosPage";
import { Role } from "@/shared/types/roles";

vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));

vi.mock("@/shared/utils/jwt", () => ({
  decodeToken: () => ({
    user:     { id: "u-001", email: "test@test.com" },
    token:    "fake.jwt.token",
    tenantId: "studio-bbb",
    role:     "client",
  }),
}));

// ── Seed data ────────────────────────────────────────────────────────────────

const STUDIO_A: MyStudioResponse = {
  studioId:       "studio-aaa",
  name:           "Alpha Ink",
  slug:           "alpha-ink",
  city:           "Tirana",
  coverImageUrl:  null,
  isStudioActive: true,
};

const STUDIO_B: MyStudioResponse = {
  studioId:       "studio-bbb",
  name:           "Beta Art",
  slug:           "beta-art",
  city:           "Durrës",
  coverImageUrl:  "https://r2.example.com/beta.jpg",
  isStudioActive: true,
};

const SUSPENDED_STUDIO: MyStudioResponse = {
  studioId:       "studio-ccc",
  name:           "Closed Ink",
  slug:           "closed-ink",
  city:           "Vlorë",
  coverImageUrl:  null,
  isStudioActive: false,
};

// ── MSW server ───────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/auth/my-studios", () =>
    HttpResponse.json([STUDIO_A, STUDIO_B]),
  ),
  http.post("http://localhost/api/v1/auth/switch-studio", () =>
    HttpResponse.json({
      accessToken:     "fake.jwt.token",
      refreshToken:    "fake-refresh",
      isNewMembership: false,
      tokenType:       "Bearer",
    }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ──────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth: authReducer,
      ui: uiReducer,
      [authApi.reducerPath]: authApi.reducer,
    },
    middleware: (gd) => gd().concat(authApi.middleware),
    preloadedState: {
      auth: {
        user: { id: "u-001", email: "test@test.com" },
        token: "fake-token",
        refreshToken: null,
        tenantId: "studio-aaa",
        role: Role.Client,
        pendingReferralCode: null,
      },
      ui: { readOnlyError: null, sessionExpired: false, studioSuspended: false },
    },
  });
}

function renderPage() {
  return render(
    <Provider store={makeStore()}>
      <MemoryRouter initialEntries={["/my-studios"]}>
        <MyStudiosPage />
      </MemoryRouter>
    </Provider>,
  );
}

function renderPageWithRoutes() {
  return render(
    <Provider store={makeStore()}>
      <MemoryRouter initialEntries={["/my-studios"]}>
        <Routes>
          <Route path="/my-studios" element={<MyStudiosPage />} />
          <Route path="/book" element={<div>Book Page</div>} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Rendering ────────────────────────────────────────────────────────────────

describe("MyStudiosPage", () => {
  it("shows loading skeleton while fetching", () => {
    renderPage();
    expect(screen.getByLabelText(/loading studios/i)).toBeInTheDocument();
  });

  it("shows each studio's name and city", async () => {
    renderPage();
    expect(await screen.findByText("Alpha Ink")).toBeInTheDocument();
    expect(screen.getByText("Tirana")).toBeInTheDocument();
    expect(screen.getByText("Beta Art")).toBeInTheDocument();
    expect(screen.getByText("Durrës")).toBeInTheDocument();
  });

  it("shows an initials monogram when coverImageUrl is null", async () => {
    renderPage();
    await screen.findByText("Alpha Ink");
    expect(screen.getByText("AI")).toBeInTheDocument();
  });

  it("shows a cover image when coverImageUrl is present", async () => {
    renderPage();
    const img = await screen.findByAltText("Beta Art");
    expect(img).toHaveAttribute("src", "https://r2.example.com/beta.jpg");
  });

  it("shows a 'Suspended' badge for isStudioActive=false studios", async () => {
    server.use(
      http.get("http://localhost/api/v1/auth/my-studios", () =>
        HttpResponse.json([STUDIO_A, SUSPENDED_STUDIO]),
      ),
    );
    renderPage();
    expect(await screen.findByText("Suspended")).toBeInTheDocument();
  });

  // ── Active studio indicator (currentTenantId = "studio-aaa") ───────────────

  it("shows 'Current' button (disabled) on the studio matching the active tenantId", async () => {
    renderPage();
    await screen.findByText("Alpha Ink");
    const currentButton = screen.getByRole("button", { name: /current/i });
    expect(currentButton).toBeDisabled();
  });

  it("shows 'Switch' button (enabled) on studios that don't match the active tenantId", async () => {
    renderPage();
    const switchButton = await screen.findByRole("button", { name: /switch to beta art/i });
    expect(switchButton).toBeEnabled();
  });

  it("does not show 'Active' badge on non-active studios", async () => {
    renderPage();
    await screen.findByText("Alpha Ink");
    expect(screen.queryAllByText("Active")).toHaveLength(1);
  });

  // ── Studio switching ─────────────────────────────────────────────────────────

  it("calls the switch-studio API with the correct studioId on button click", async () => {
    let capturedBody: unknown;
    server.use(
      http.post("http://localhost/api/v1/auth/switch-studio", async ({ request }) => {
        capturedBody = await request.json();
        return HttpResponse.json({
          accessToken: "fake.jwt.token", refreshToken: "fake-refresh",
          isNewMembership: false, tokenType: "Bearer",
        });
      }),
    );
    const user = userEvent.setup();
    renderPage();
    const switchButton = await screen.findByRole("button", { name: /switch to beta art/i });
    await user.click(switchButton);

    expect(capturedBody).toEqual({ studioId: "studio-bbb" });
  });

  it("navigates to /book after a successful switch", async () => {
    const user = userEvent.setup();
    renderPageWithRoutes();
    const switchButton = await screen.findByRole("button", { name: /switch to beta art/i });
    await user.click(switchButton);

    expect(await screen.findByText("Book Page")).toBeInTheDocument();
  });

  it("shows a toast error on switch failure (500 response)", async () => {
    server.use(
      http.post("http://localhost/api/v1/auth/switch-studio", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    const switchButton = await screen.findByRole("button", { name: /switch to beta art/i });
    await user.click(switchButton);

    await vi.waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Couldn't switch studios. Please try again."),
    );
  });

  // ── Edge cases ───────────────────────────────────────────────────────────────

  it("shows the empty state when the API returns an empty array", async () => {
    server.use(
      http.get("http://localhost/api/v1/auth/my-studios", () => HttpResponse.json([])),
    );
    renderPage();
    expect(await screen.findByText(/no studios yet/i)).toBeInTheDocument();
  });

  it("shows the error state and a 'Try again' button when the API returns 500", async () => {
    server.use(
      http.get("http://localhost/api/v1/auth/my-studios", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText(/failed to load your studios/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /try again/i })).toBeInTheDocument();
  });

  it("shows the correct count in the header when multiple studios are returned", async () => {
    renderPage();
    expect(await screen.findByText("(2)")).toBeInTheDocument();
  });
});
