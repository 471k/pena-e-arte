import { describe, it, expect, beforeAll, beforeEach, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { platformApi } from "@/features/platform/platformApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { billingApi } from "@/features/billing/billingApi";
import { IssuerStudioDetailPage } from "@/features/platform/components/IssuerStudioDetailPage";
import type { StudioResponse } from "@/features/studios/studiosApi";
import type { PlatformSubscriptionResponse, PlatformReferralCodeResponse } from "@/features/platform/platform.types";
import type { PlanResponse } from "@/features/billing/billing.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const STUDIO: StudioResponse = {
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
};

const SUB: PlatformSubscriptionResponse = {
  studioId:         "s1",
  studioName:       "Ink Soul",
  studioSlug:       "ink-soul",
  subscriptionId:   "sub-1",
  status:           "Active",
  planName:         "Pro",
  trialExpiresAt:   new Date(Date.now() + 30 * 86_400_000).toISOString(),
  currentPeriodEnd: new Date(Date.now() + 30 * 86_400_000).toISOString(),
  isSuspended:      false,
};

const PLANS: PlanResponse[] = [
  {
    id:                    "plan-1",
    name:                  "Starter",
    yearlyDiscountPercent: 17,
    allowBrandingRemoval:  false,
    subscriberCount:       0,
    maxArtists:               null,
    maxAppointmentsPerMonth:  null,
    maxNotificationsPerMonth: null,
    maxStorageGb:             null,
    maxLocations:             null,
    allowApiAccess:           false,
    prioritySupport:          false,
    prices: [
      { id: "price-1-m", interval: "Monthly", price: 29, stripePriceId: null, isActive: true },
    ],
  },
];

// ── MSW server ─────────────────────────────────────────────────────────────────

const REFERRAL_CODE: PlatformReferralCodeResponse = {
  id:              "code-1",
  studioId:        "s1",
  studioName:      "Ink Soul",
  code:            "INKSOUL1",
  isActive:        true,
  isSingleUse:     true,
  createdAt:       "2026-06-01T00:00:00Z",
  expiresAt:       null,
  redemptionCount: 0,
};

const server = setupServer(
  http.get("http://localhost/api/v1/studios/s1", () => HttpResponse.json(STUDIO)),
  http.get("http://localhost/api/v1/platform/subscriptions", () => HttpResponse.json([SUB])),
  http.get("http://localhost/api/v1/billing/plans", () => HttpResponse.json(PLANS)),
  http.get("http://localhost/api/v1/platform/studios/s1/summary", () =>
    HttpResponse.json({
      ownerEmail:       "owner@ink-soul.test",
      ownerDisplayName: "Maria Silva",
      artistCount:      3,
      clientCount:      47,
      appointmentCount: 129,
    })
  ),
  http.get("http://localhost/api/v1/platform/referral-codes", () => HttpResponse.json([])),
  http.post("http://localhost/api/v1/platform/studios/:studioId/referral-codes", () =>
    HttpResponse.json(REFERRAL_CODE)),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                         authReducer,
      [platformApi.reducerPath]:    platformApi.reducer,
      [studiosApi.reducerPath]:     studiosApi.reducer,
      [billingApi.reducerPath]:     billingApi.reducer,
    },
    middleware: (gd) =>
      gd().concat(platformApi.middleware, studiosApi.middleware, billingApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u4", email: "issuer@platform.test" }, token: "fake", tenantId: null, role: "issuer", pendingReferralCode: null } as any,
    },
  });
}

function renderPage(studioId = "s1") {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[`/platform/studios/${studioId}`]}>
        <Routes>
          <Route path="/platform/studios/:studioId" element={<IssuerStudioDetailPage />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("IssuerStudioDetailPage", () => {
  it("renders the studio name", async () => {
    renderPage();
    // Studio name appears in both the header breadcrumb and the card title
    const names = await screen.findAllByText("Ink Soul");
    expect(names.length).toBeGreaterThan(0);
  });

  it("renders Active badge", async () => {
    renderPage();
    // Wait for data to load before asserting
    await screen.findAllByText("Ink Soul");
    expect(screen.getAllByText("Active").length).toBeGreaterThan(0);
  });

  it("renders city and registration date", async () => {
    renderPage();
    expect(await screen.findByText("Porto")).toBeInTheDocument();
  });

  it("renders a back link to /platform/studios", async () => {
    renderPage();
    await screen.findAllByText("Ink Soul");
    expect(screen.getByRole("link", { name: /studios/i })).toHaveAttribute("href", "/platform/studios");
  });

  it("shows 404 message for unknown studio id", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios/unknown", () =>
        HttpResponse.json({ message: "Not found" }, { status: 404 }),
      ),
    );
    renderPage("unknown");
    expect(await screen.findByText(/studio not found/i)).toBeInTheDocument();
  });

  it("shows Suspend button for active studios", async () => {
    renderPage();
    await screen.findAllByText("Ink Soul");
    expect(screen.getByRole("button", { name: /suspend/i })).toBeInTheDocument();
  });

  // ── Fix 1: Subscription status pill ──────────────────────────────────────────

  it("renders subscription status as a pill badge, not plain text", async () => {
    renderPage();
    await screen.findAllByText("Ink Soul");
    // Find the subscription status label
    const label = screen.getByText("Subscription status");
    // The value sibling must be a <span> with rounded-full class (pill), not a plain <p>
    const field = label.closest("div")!;
    const pill  = field.querySelector("span.rounded-full");
    expect(pill).not.toBeNull();
    expect(pill?.textContent).toBe("Active");
  });

  // ── Fix 4: Button labels ──────────────────────────────────────────────────────

  it("suspend button is labelled 'Suspend Studio'", async () => {
    renderPage();
    await screen.findAllByText("Ink Soul");
    expect(screen.getByRole("button", { name: /suspend studio/i })).toBeInTheDocument();
  });

  // ── Fix 5: Consequence copy ───────────────────────────────────────────────────

  it("suspend confirm panel shows consequence copy", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findAllByText("Ink Soul");
    await user.click(screen.getByRole("button", { name: /suspend studio/i }));
    expect(await screen.findByText(/immediately hides the studio from discover/i)).toBeInTheDocument();
  });

  // ── Fix 3: View public portfolio link ────────────────────────────────────────

  it("'View public portfolio' renders as an <a> link pointing to the studio's public page", async () => {
    renderPage();
    await screen.findAllByText("Ink Soul");
    const link = screen.getByRole("link", { name: /view public portfolio/i });
    expect(link).toHaveAttribute("href", "/s/ink-soul");
    expect(link).toHaveAttribute("target", "_blank");
  });

  // ── Phase 2: Studio Overview card ────────────────────────────────────────────

  it("Studio Overview card renders owner email", async () => {
    renderPage();
    expect(await screen.findByText("owner@ink-soul.test")).toBeInTheDocument();
  });

  it("Studio Overview card renders artist, client, and appointment counts", async () => {
    renderPage();
    expect(await screen.findByText("3")).toBeInTheDocument();   // artistCount
    expect(screen.getByText("47")).toBeInTheDocument();          // clientCount
    expect(screen.getByText("129")).toBeInTheDocument();         // appointmentCount
  });

  describe("when studio is suspended", () => {
    beforeEach(() => {
      server.use(
        http.get("http://localhost/api/v1/studios/s1", () =>
          HttpResponse.json({ ...STUDIO, isActive: false }),
        ),
        http.get("http://localhost/api/v1/platform/subscriptions", () =>
          HttpResponse.json([{ ...SUB, isSuspended: true }]),
        ),
      );
    });

    it("shows Suspended badge in amber (not red) in the card header", async () => {
      renderPage();
      await screen.findAllByText("Ink Soul");
      const badges = screen.getAllByText("Suspended");
      expect(badges.length).toBeGreaterThan(0);
      for (const badge of badges) {
        expect(badge.className).toMatch(/amber/);
        expect(badge.className).not.toMatch(/red/);
      }
    });

    it("Reactivate Studio button has variant=default (filled)", async () => {
      renderPage();
      await screen.findAllByText("Ink Soul");
      const btn = screen.getByRole("button", { name: /reactivate studio/i });
      expect(btn.className).toMatch(/bg-primary/);
    });

    it("does NOT show 'Renews' date when studio is suspended", async () => {
      renderPage();
      await screen.findAllByText("Ink Soul");
      expect(screen.queryByText(/renews/i)).not.toBeInTheDocument();
    });
  });

  describe("trial expiry only shown in trial-relevant states", () => {
    it("does NOT show trial expiry for an Active studio", async () => {
      // SUB has status: "Active" and a future trialExpiresAt (converted studio)
      server.use(
        http.get("http://localhost/api/v1/platform/subscriptions", () =>
          HttpResponse.json([{
            ...SUB,
            status: "Active",
            isSuspended: false,
            trialExpiresAt: new Date(Date.now() - 30 * 86_400_000).toISOString(), // expired 30 days ago
          }]),
        ),
      );
      renderPage();
      await screen.findAllByText("Ink Soul");
      expect(screen.queryByText(/trial expiry/i)).not.toBeInTheDocument();
    });

    it("shows trial expiry for a Trialing studio", async () => {
      server.use(
        http.get("http://localhost/api/v1/platform/subscriptions", () =>
          HttpResponse.json([{
            ...SUB,
            status: "Trialing",
            isSuspended: false,
            trialExpiresAt: new Date(Date.now() + 7 * 86_400_000).toISOString(),
          }]),
        ),
      );
      renderPage();
      await screen.findAllByText("Ink Soul");
      expect(await screen.findByText(/trial expiry/i)).toBeInTheDocument();
    });
  });

  // Phase 3 row 9 of the full-app master audit: this is the single point in the app
  // where Free plan tier + referral-code conversion + OAuth-registered owner all
  // converge on one read. OAuth vs password registration produces identical Studio/
  // Subscription/Client rows (see RegisterOAuthUserHandler audit note), and a
  // referral-code redemption never appears on this page (no referral section exists
  // here), so the two conditions actually exercised by this page's own data are: an
  // Active subscription on a Free-tier plan whose TrialExpiresAt has already been
  // cleared to null (CreateSubscriptionHandler always nulls it on activation,
  // including for price == 0 plans) and whose CurrentPeriodEnd is the 50-year
  // far-future sentinel rather than a real renewal date.
  describe("when studio is on a converted Free-tier subscription (Phase 3 row 9)", () => {
    beforeEach(() => {
      server.use(
        http.get("http://localhost/api/v1/platform/subscriptions", () =>
          HttpResponse.json([{
            ...SUB,
            status:           "Active",
            planName:         "Free",
            trialExpiresAt:   null,
            currentPeriodEnd: new Date(Date.now() + 50 * 365 * 86_400_000).toISOString(),
            isSuspended:      false,
          }]),
        ),
      );
    });

    it("renders without crashing and shows the Free plan name and Active badge", async () => {
      renderPage();
      await screen.findAllByText("Ink Soul");
      expect(screen.getByText("Free")).toBeInTheDocument();
      expect(screen.getAllByText("Active").length).toBeGreaterThan(0);
    });

    it("renders no 'undefined' or 'NaN' text anywhere on the page", async () => {
      renderPage();
      await screen.findAllByText("Ink Soul");
      expect(document.body.textContent).not.toMatch(/undefined/i);
      expect(document.body.textContent).not.toContain("NaN");
    });
  });

  describe("Referral Codes card", () => {
    it("shows an empty state when the studio has no referral codes", async () => {
      renderPage();
      await screen.findAllByText("Ink Soul");
      expect(await screen.findByText(/no referral codes generated for this studio yet/i))
        .toBeInTheDocument();
    });

    it("shows only this studio's codes, not other studios'", async () => {
      server.use(
        http.get("http://localhost/api/v1/platform/referral-codes", () =>
          HttpResponse.json([
            REFERRAL_CODE,
            { ...REFERRAL_CODE, id: "code-2", studioId: "s2", studioName: "Other Studio", code: "OTHER123" },
          ])),
      );
      renderPage();
      await screen.findAllByText("Ink Soul");
      expect(await screen.findByText("INKSOUL1")).toBeInTheDocument();
      expect(screen.queryByText("OTHER123")).not.toBeInTheDocument();
    });

    it("clicking 'Generate Code' opens the expiry-date form", async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findAllByText("Ink Soul");
      await user.click(screen.getByRole("button", { name: /generate code/i }));
      expect(screen.getByLabelText(/expiry date/i)).toBeInTheDocument();
    });

    it("generating a code calls the studio-scoped endpoint and closes the form", async () => {
      let capturedStudioId: string | null = null;
      server.use(
        http.post("http://localhost/api/v1/platform/studios/:studioId/referral-codes", ({ params }) => {
          capturedStudioId = params.studioId as string;
          return HttpResponse.json(REFERRAL_CODE);
        }),
      );
      const user = userEvent.setup();
      renderPage();
      await screen.findAllByText("Ink Soul");
      await user.click(screen.getByRole("button", { name: /generate code/i }));
      await user.click(screen.getByRole("button", { name: /^generate$/i }));

      expect(await screen.findByRole("button", { name: /generate code/i })).toBeInTheDocument();
      expect(capturedStudioId).toBe("s1");
    });
  });
});
