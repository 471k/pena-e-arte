import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { platformApi } from "@/features/platform/platformApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { PlatformReferralPage } from "@/features/platform/components/PlatformReferralPage";
import type { PlatformReferralCodeResponse } from "@/features/platform/platform.types";
import type { StudioResponse } from "@/features/studios/studiosApi";

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

const STUDIOS: StudioResponse[] = [
  {
    id:                   "s1",
    name:                 "Ink Soul",
    slug:                 "ink-soul",
    city:                 "Porto",
    latitude:             41.1,
    longitude:            -8.6,
    showPlatformBranding: true,
    allowBrandingRemoval: false,
    trialExpiresAt:       new Date(Date.now() + 14 * 86_400_000).toISOString(),
    createdAt:            "2024-01-01T00:00:00Z",
    isActive:             true,
    slugLockedAt:         null,
    phoneNumber:          null,
    instagramHandle:      null,
    nipt:                 null,
  },
];

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/platform/referral-codes", () =>
    HttpResponse.json(CODES),
  ),
  http.get("http://localhost/api/v1/studios", () => HttpResponse.json(STUDIOS)),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                       authReducer,
      [platformApi.reducerPath]:  platformApi.reducer,
      [studiosApi.reducerPath]:   studiosApi.reducer,
    },
    middleware: (gd) => gd().concat(platformApi.middleware).concat(studiosApi.middleware),
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

  it("shows skeleton cards while loading, not a spinner", () => {
    renderPage();
    expect(document.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
    expect(screen.queryByText("Loading…")).not.toBeInTheDocument();
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

  it("shows the total count as a styled badge in the header", async () => {
    renderPage();
    await screen.findByText("INK2026");
    expect(screen.getByText("3", { selector: "span" })).toBeInTheDocument();
    expect(screen.queryByText("(3)")).not.toBeInTheDocument();
  });

  it("shows 'Active' badge (Title Case) for active codes", async () => {
    renderPage();
    await screen.findByText("INK2026");
    const activeBadges = screen.getAllByText("Active", { selector: "span" });
    expect(activeBadges.length).toBeGreaterThanOrEqual(2);
  });

  it("shows 'Inactive' badge (Title Case) for inactive codes", async () => {
    renderPage();
    await screen.findByText("OLD2025");
    expect(screen.getByText("Inactive", { selector: "span" })).toBeInTheDocument();
    expect(screen.queryByText("inactive", { selector: "span" })).not.toBeInTheDocument();
  });

  it("shows 'Single use' badge for single-use codes", async () => {
    renderPage();
    await screen.findByText("ROOTS1X");
    expect(screen.getByText("Single use")).toBeInTheDocument();
    expect(screen.queryByText("single-use")).not.toBeInTheDocument();
  });

  it("shows redemption count", async () => {
    renderPage();
    await screen.findByText("INK2026");
    expect(screen.getByText(/5 redemptions/i)).toBeInTheDocument();
    expect(screen.getByText(/1 redemption(?!s)/i)).toBeInTheDocument();
  });

  it("shows studio names", async () => {
    renderPage();
    expect(await screen.findByText(/Ink Soul/)).toBeInTheDocument();
    expect(screen.getByText(/Deep Roots Tattoo/)).toBeInTheDocument();
    expect(screen.getByText(/Old School Ink/)).toBeInTheDocument();
  });

  it("shows 'Deactivate' button for active and 'Reactivate' button for inactive codes", async () => {
    renderPage();
    await screen.findByText("INK2026");
    expect(screen.getAllByRole("button", { name: /deactivate/i })).toHaveLength(2);
    expect(screen.getAllByRole("button", { name: /reactivate/i })).toHaveLength(1);
  });

  it("does NOT show Deactivate button for inactive codes", async () => {
    renderPage();
    await screen.findByText("OLD2025");
    const deactivateBtns = screen.getAllByRole("button", { name: /deactivate/i });
    expect(deactivateBtns).toHaveLength(2);
  });

  it("clicking Deactivate shows confirmation naming the code", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("INK2026");

    const [firstBtn] = screen.getAllByRole("button", { name: /deactivate/i });
    await user.click(firstBtn);

    expect(screen.getByText(/deactivate code/i)).toBeInTheDocument();
    expect(screen.getAllByText(/INK2026/).length).toBeGreaterThanOrEqual(1);
    expect(screen.getByRole("button", { name: /yes, deactivate/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /cancel/i })).toBeInTheDocument();
  });

  it("clicking Cancel hides the deactivate confirmation", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("INK2026");

    const [firstBtn] = screen.getAllByRole("button", { name: /deactivate/i });
    await user.click(firstBtn);
    await user.click(screen.getByRole("button", { name: /cancel/i }));

    expect(screen.queryByText(/yes, deactivate/i)).not.toBeInTheDocument();
  });

  it("clicking Yes, deactivate calls PATCH referral-codes/:id/deactivate", async () => {
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
    await user.click(screen.getByRole("button", { name: /yes, deactivate/i }));

    await waitFor(() => expect(deactivateSpy).toHaveBeenCalledOnce());
  });

  it("shows Delete button on every card", async () => {
    renderPage();
    await screen.findByText("INK2026");
    expect(screen.getAllByRole("button", { name: /delete/i })).toHaveLength(3);
  });

  it("delete confirmation warns about redemptions if code has been redeemed", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("INK2026"); // ref-1 has 5 redemptions

    const deleteBtns = screen.getAllByRole("button", { name: /delete referral code/i });
    await user.click(deleteBtns[0]); // first = ref-1 (5 redemptions)

    expect(screen.getByText(/cannot be deleted/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /yes, delete/i })).not.toBeInTheDocument();
  });

  it("delete confirmation for unredeemed code shows 'Yes, delete' button", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/referral-codes", () =>
        HttpResponse.json([
          { ...CODES[2], redemptionCount: 0 },
        ]),
      ),
    );
    cleanup();
    renderPage();
    await screen.findByText("OLD2025");

    const deleteBtn = screen.getByRole("button", { name: /delete referral code old2025/i });
    await userEvent.setup().click(deleteBtn);

    expect(screen.getByRole("button", { name: /yes, delete/i })).toBeInTheDocument();
  });

  it("clicking Reactivate shows confirmation", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("OLD2025");

    await user.click(screen.getByRole("button", { name: /reactivate referral code old2025/i }));

    expect(screen.getByRole("button", { name: /yes, reactivate/i })).toBeInTheDocument();
  });

  it("confirming Reactivate calls PATCH referral-codes/:id/reactivate", async () => {
    const reactivateSpy = vi.fn();
    server.use(
      http.patch("http://localhost/api/v1/platform/referral-codes/ref-3/reactivate", () => {
        reactivateSpy();
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByText("OLD2025");

    await user.click(screen.getByRole("button", { name: /reactivate referral code old2025/i }));
    await user.click(screen.getByRole("button", { name: /yes, reactivate/i }));

    await waitFor(() => expect(reactivateSpy).toHaveBeenCalledOnce());
  });

  it("shows empty state when no codes exist", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/referral-codes", () =>
        HttpResponse.json([]),
      ),
    );
    renderPage();
    expect(await screen.findByText(/no referral codes yet/i)).toBeInTheDocument();
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

  it("shows search input when codes exist", async () => {
    renderPage();
    await screen.findByText("INK2026");
    expect(screen.getByRole("searchbox")).toBeInTheDocument();
  });

  it("filters codes by search term (code string)", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("INK2026");

    await user.type(screen.getByRole("searchbox"), "INK");

    expect(screen.getByText("INK2026")).toBeInTheDocument();
    expect(screen.queryByText("ROOTS1X")).not.toBeInTheDocument();
  });

  it("shows 'No codes match your search' when filtered to zero results", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("INK2026");

    await user.type(screen.getByRole("searchbox"), "ZZZNOMATCH");

    expect(screen.getByText(/no codes match your search/i)).toBeInTheDocument();
  });

  it("filter pills show count for each status", async () => {
    renderPage();
    await screen.findByText("INK2026");
    expect(screen.getByRole("button", { name: /^All \(3\)/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^Active \(2\)/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^Inactive \(1\)/i })).toBeInTheDocument();
  });

  it("Generate Code button appears in header", async () => {
    renderPage();
    expect(screen.getByRole("button", { name: /generate new referral code/i })).toBeInTheDocument();
  });

  it("clicking Generate Code expands the generate form", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("INK2026");

    await user.click(screen.getByRole("button", { name: /generate new referral code/i }));

    expect(screen.getByText(/generate referral code/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/studio/i)).toBeInTheDocument();
  });

  it("generate form's studio selector is populated from the studios list", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("INK2026");
    await user.click(screen.getByRole("button", { name: /generate new referral code/i }));

    const studioSelect = screen.getByLabelText(/studio/i);
    expect(await within(studioSelect).findByText("Ink Soul")).toBeInTheDocument();
  });

  it("Generate button is disabled until a studio is selected", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("INK2026");
    await user.click(screen.getByRole("button", { name: /generate new referral code/i }));

    const generateBtn = screen.getByRole("button", { name: /^generate$/i });
    expect(generateBtn).toBeDisabled();

    const studioSelect = screen.getByLabelText(/studio/i);
    await within(studioSelect).findByText("Ink Soul");
    await user.selectOptions(studioSelect, "s1");
    expect(generateBtn).not.toBeDisabled();
  });

  it("submitting the generate form calls the studio-scoped endpoint", async () => {
    let capturedStudioId: string | null = null;
    server.use(
      http.post("http://localhost/api/v1/platform/studios/:studioId/referral-codes", ({ params }) => {
        capturedStudioId = params.studioId as string;
        return HttpResponse.json({
          id: "code-new", studioId: "s1", studioName: "Ink Soul", code: "NEWCODE1",
          isActive: true, isSingleUse: true, createdAt: "2026-06-01T00:00:00Z",
          expiresAt: null, redemptionCount: 0,
        });
      }),
    );
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("INK2026");
    await user.click(screen.getByRole("button", { name: /generate new referral code/i }));
    const studioSelect = screen.getByLabelText(/studio/i);
    await within(studioSelect).findByText("Ink Soul");
    await user.selectOptions(studioSelect, "s1");
    await user.click(screen.getByRole("button", { name: /^generate$/i }));

    await waitFor(() => expect(capturedStudioId).toBe("s1"));
  });

  it("each code has a copy button with aria-label", async () => {
    renderPage();
    await screen.findByText("INK2026");
    expect(screen.getByRole("button", { name: /copy referral code INK2026/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /copy referral code ROOTS1X/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /copy referral code OLD2025/i })).toBeInTheDocument();
  });

  it("helper text is present below the header", async () => {
    renderPage();
    await screen.findByText("INK2026");
    expect(screen.getByText(/referral codes give studios/i)).toBeInTheDocument();
  });
});
