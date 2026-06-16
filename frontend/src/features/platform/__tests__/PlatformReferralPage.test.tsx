import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { platformApi } from "@/features/platform/platformApi";
import { PlatformReferralPage } from "@/features/platform/components/PlatformReferralPage";
import type { PlatformReferralCodeResponse } from "@/features/platform/platform.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const CODES: PlatformReferralCodeResponse[] = [
  {
    id:              "ref-1",
    studioId:        "s1",
    studioName:      "Ink Soul",
    code:            "INK2026",
    isActive:        true,
    isSingleUse:     false,
    createdAt:       "2026-01-15T00:00:00Z",
    expiresAt:       null,
    redemptionCount: 5,
  },
  {
    id:              "ref-2",
    studioId:        "s2",
    studioName:      "Deep Roots Tattoo",
    code:            "ROOTS1X",
    isActive:        true,
    isSingleUse:     true,
    createdAt:       "2026-03-01T00:00:00Z",
    expiresAt:       "2026-12-31T00:00:00Z",
    redemptionCount: 1,
  },
  {
    id:              "ref-3",
    studioId:        "s3",
    studioName:      "Old School Ink",
    code:            "OLD2025",
    isActive:        false,
    isSingleUse:     false,
    createdAt:       "2025-06-01T00:00:00Z",
    expiresAt:       null,
    redemptionCount: 3,
  },
];

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/platform/referral-codes", () =>
    HttpResponse.json(CODES),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                      authReducer,
      [platformApi.reducerPath]: platformApi.reducer,
    },
    middleware: (gd) => gd().concat(platformApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u4", email: "issuer@platform.test" }, token: "fake", tenantId: null, role: "issuer", pendingReferralCode: null } as any,
    },
  });
}

function renderPage() {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter>
        <PlatformReferralPage />
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("PlatformReferralPage", () => {

  it("shows a loading spinner while loading", () => {
    renderPage();
    expect(screen.getByText("Loading…")).toBeInTheDocument();
  });

  it("renders the Referral Codes header", async () => {
    renderPage();
    expect(await screen.findByText("Referral Codes")).toBeInTheDocument();
  });

  it("renders all code values", async () => {
    renderPage();
    expect(await screen.findByText("INK2026")).toBeInTheDocument();
    expect(screen.getByText("ROOTS1X")).toBeInTheDocument();
    expect(screen.getByText("OLD2025")).toBeInTheDocument();
  });

  it("shows the total count in the header", async () => {
    renderPage();
    await screen.findByText("INK2026");
    expect(screen.getByText("(3)")).toBeInTheDocument();
  });

  it("shows 'active' badge for active codes", async () => {
    renderPage();
    await screen.findByText("INK2026");
    const activeBadges = screen.getAllByText("active");
    expect(activeBadges.length).toBeGreaterThanOrEqual(2); // ref-1 and ref-2
  });

  it("shows 'inactive' badge for inactive codes", async () => {
    renderPage();
    await screen.findByText("OLD2025");
    expect(screen.getByText("inactive")).toBeInTheDocument();
  });

  it("shows 'single-use' badge for single-use codes", async () => {
    renderPage();
    await screen.findByText("ROOTS1X");
    expect(screen.getByText("single-use")).toBeInTheDocument();
  });

  it("shows redemption count", async () => {
    renderPage();
    await screen.findByText("INK2026");
    expect(screen.getByText(/5 redemptions/i)).toBeInTheDocument();
    expect(screen.getByText(/1 redemption(?!s)/i)).toBeInTheDocument();
  });

  it("shows studio names", async () => {
    renderPage();
    // Studio name is a text node inside a <p> with other content, so use regex for partial match
    expect(await screen.findByText(/Ink Soul/)).toBeInTheDocument();
    expect(screen.getByText(/Deep Roots Tattoo/)).toBeInTheDocument();
    expect(screen.getByText(/Old School Ink/)).toBeInTheDocument();
  });

  it("shows Deactivate button only for active codes", async () => {
    renderPage();
    await screen.findByText("INK2026");
    const deactivateBtns = screen.getAllByRole("button", { name: /deactivate/i });
    expect(deactivateBtns).toHaveLength(2); // ref-1 and ref-2 are active
  });

  it("does NOT show Deactivate button for inactive codes", async () => {
    renderPage();
    await screen.findByText("OLD2025");
    const deactivateBtns = screen.getAllByRole("button", { name: /deactivate/i });
    // Only active codes (2) have this button; OLD2025 does not
    expect(deactivateBtns).toHaveLength(2);
  });

  it("clicking Deactivate shows confirmation with Yes/No", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("INK2026");

    const [firstBtn] = screen.getAllByRole("button", { name: /deactivate/i });
    await user.click(firstBtn);

    expect(screen.getByText(/deactivate\?/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /yes/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /no/i })).toBeInTheDocument();
  });

  it("clicking No cancels the confirmation", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("INK2026");

    const [firstBtn] = screen.getAllByRole("button", { name: /deactivate/i });
    await user.click(firstBtn);
    await user.click(screen.getByRole("button", { name: /no/i }));

    expect(screen.queryByText(/deactivate\?/i)).not.toBeInTheDocument();
  });

  it("clicking Yes calls PATCH referral-codes/:id/deactivate", async () => {
    const deactivateSpy = vi.fn();
    server.use(
      http.patch("http://localhost/api/v1/platform/referral-codes/ref-1/deactivate", () => {
        deactivateSpy();
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByText("INK2026");

    const [firstBtn] = screen.getAllByRole("button", { name: /deactivate/i });
    await user.click(firstBtn);
    await user.click(screen.getByRole("button", { name: /yes/i }));

    await waitFor(() => expect(deactivateSpy).toHaveBeenCalledOnce());
  });

  it("shows empty state when no codes exist", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/referral-codes", () =>
        HttpResponse.json([]),
      ),
    );
    renderPage();
    expect(await screen.findByText(/no referral codes found/i)).toBeInTheDocument();
  });

  it("shows error state when fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/referral-codes", () =>
        HttpResponse.json({ message: "Server error" }, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText(/failed to load referral codes/i)).toBeInTheDocument();
  });
});
