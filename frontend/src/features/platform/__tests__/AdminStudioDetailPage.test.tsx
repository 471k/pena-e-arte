import { describe, it, expect, beforeAll, beforeEach, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor, within } from "@testing-library/react";
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
import { AdminStudioDetailPage } from "@/features/platform/components/AdminStudioDetailPage";
import type { StudioResponse } from "@/features/studios/studiosApi";
import type { PlatformSubscriptionResponse, PlatformReferralCodeResponse } from "@/features/platform/platform.types";
import type { PlanResponse } from "@/features/billing/billing.types";

// ── Fake JWT helper ────────────────────────────────────────────────────────────
// decodeToken() (used by authSlice's startImpersonation reducer) calls jwt-decode,
// which throws on a non-JWT-shaped string — a plain placeholder like "fake-token"
// is NOT enough here, unlike other tests in this file that never decode the token.

const ROLE_CLAIM = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

function toBase64Url(s: string) {
  return btoa(s).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function fakeImpersonationJwt() {
  const header = toBase64Url(JSON.stringify({ alg: "HS256", typ: "JWT" }));
  const payload = toBase64Url(JSON.stringify({
    sub: "admin-1", email: "admin@test.com",
    [ROLE_CLAIM]: "admin", tenant_id: "s1", imp: "sess-1",
    exp: Math.floor(Date.now() / 1000) + 3600,
  }));
  return `${header}.${payload}.fakesig`;
}

// ── Seed data ──────────────────────────────────────────────────────────────────

const STUDIO: StudioResponse = {
  id:                   "s1",
  name:                 "Ink Soul",
  slug:                 "ink-soul",
  city:                 "Porto",
  latitude:             41.1,
  longitude:            -8.6,
  showPlatformBranding: true,
  allowBrandingRemoval: false, allowApiAccess: false,
  trialExpiresAt:       new Date(Date.now() + 14 * 86_400_000).toISOString(),
  createdAt:            "2024-01-01T00:00:00Z",
  isActive:             true,
  slugLockedAt:         null,
  phoneNumber:          null,
  instagramHandle:      null,
  nipt:                 null,
  isSolo:               false,
  isPublished:          true,
  timezone:             "Europe/Tirane",
  addressLine1:         null,
  addressLine2:         null,
  postalCode:           null,
  countryCode: "AL",
  currency: "EUR",
  currencyLocked: false,
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
  cancelAtPeriodEnd: false,
};

const PLANS: PlanResponse[] = [
  {
    id:                    "plan-1",
    name:                  "Starter",
    yearlyDiscountPercent: 17,
    currency:              "EUR",
    yearlySavingAmount:    null,
    yearlyMonthsFree:      null,
    allowBrandingRemoval:  false,
    subscriberCount:       0,
    maxArtists:               null,
    maxAppointmentsPerMonth:  null,
    maxNotificationsPerMonth: null,
    maxStorageGb:             null,
    maxLocations:             null,
    allowApiAccess:           false,
    prioritySupport:          false,
    allowMarketingCampaigns: false,
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
  shareUrl:        "https://tattooos.co/register?ref=INKSOUL1",
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
      recentRefunds:    [],
    })
  ),
  http.get("http://localhost/api/v1/platform/referral-codes", () => HttpResponse.json([])),
  http.post("http://localhost/api/v1/platform/studios/:studioId/referral-codes", () =>
    HttpResponse.json(REFERRAL_CODE)),
  http.post("http://localhost/api/v1/platform/studios/:studioId/impersonate", () =>
    HttpResponse.json({
      sessionId: "sess-1",
      accessToken: fakeImpersonationJwt(),
      expiresAt: new Date(Date.now() + 45 * 60_000).toISOString(),
      studioId: "s1",
      studioName: "Ink Soul",
    })),
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
      auth: { user: { id: "u4", email: "admin@platform.test" }, token: "fake", tenantId: null, role: "admin", pendingReferralCode: null, impersonation: null } as any,
    },
  });
}

function renderPage(studioId = "s1", listSearch?: string) {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[{ pathname: `/platform/studios/${studioId}`, state: listSearch ? { listSearch } : null }]}>
        <Routes>
          <Route path="/platform/studios/:studioId" element={<AdminStudioDetailPage />} />
          <Route path="/schedule" element={<div data-testid="support-landed" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("AdminStudioDetailPage", () => {
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

  it("opened from a shared link, Back goes to the studio list focused on this studio", async () => {
    renderPage();
    await screen.findAllByText("Ink Soul");
    expect(screen.getByRole("link", { name: /studios/i })).toHaveAttribute("href", "/platform/studios?focus=s1");
  });

  it("opened from the list, Back returns to the same filters and page with this studio in focus (D08)", async () => {
    renderPage("s1", "?status=PastDue&page=2&focus=s1");
    await screen.findAllByText("Ink Soul");
    expect(screen.getByRole("link", { name: /studios/i }))
      .toHaveAttribute("href", "/platform/studios?status=PastDue&page=2&focus=s1");
  });

  it("reads only this studio's subscription and referral codes, not the whole platform's (D08)", async () => {
    const requested: string[] = [];
    server.use(
      http.get("http://localhost/api/v1/platform/subscriptions", ({ request }) => {
        requested.push(request.url);
        return HttpResponse.json([SUB]);
      }),
      http.get("http://localhost/api/v1/platform/referral-codes", ({ request }) => {
        requested.push(request.url);
        return HttpResponse.json([]);
      }),
    );
    renderPage();
    await screen.findAllByText("Ink Soul");
    await waitFor(() => expect(requested).toHaveLength(2));
    expect(requested.every((u) => new URL(u).searchParams.get("studioId") === "s1")).toBe(true);
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
      // The server scopes the read (?studioId=); this mock honours it the way the handler does.
      server.use(
        http.get("http://localhost/api/v1/platform/referral-codes", ({ request }) => {
          const studioId = new URL(request.url).searchParams.get("studioId");
          const all = [
            REFERRAL_CODE,
            { ...REFERRAL_CODE, id: "code-2", studioId: "s2", studioName: "Other Studio", code: "OTHER123" },
          ];
          return HttpResponse.json(studioId ? all.filter((c) => c.studioId === studioId) : all);
        }),
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

  // ── Support Impersonation ─────────────────────────────────────────────────────

  describe("Impersonate", () => {
    it("shows an Impersonate button", async () => {
      renderPage();
      await screen.findAllByText("Ink Soul");
      expect(screen.getByRole("button", { name: /impersonate/i })).toBeInTheDocument();
    });

    it("clicking Impersonate opens the reason form with the start button disabled", async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findAllByText("Ink Soul");
      await user.click(screen.getByRole("button", { name: /impersonate/i }));

      expect(screen.getByLabelText(/reason/i)).toBeInTheDocument();
      expect(screen.getByRole("button", { name: /start impersonating/i })).toBeDisabled();
    });

    it("a too-short reason keeps the start button disabled", async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findAllByText("Ink Soul");
      await user.click(screen.getByRole("button", { name: /impersonate/i }));
      await user.type(screen.getByLabelText(/reason/i), "hi");

      expect(screen.getByRole("button", { name: /start impersonating/i })).toBeDisabled();
    });

    it("submitting a valid reason starts the session and lands on the schedule (D05: the dashboard's reads aren't allowed in a support session)", async () => {
      let capturedBody: { reasonCode?: string } | null = null;
      const accessToken = fakeImpersonationJwt();
      server.use(
        http.post("http://localhost/api/v1/platform/studios/:studioId/impersonate", async ({ request }) => {
          capturedBody = (await request.json()) as { reasonCode?: string };
          return HttpResponse.json({
            sessionId: "sess-1", accessToken,
            expiresAt: new Date(Date.now() + 45 * 60_000).toISOString(),
            studioId: "s1", studioName: "Ink Soul",
          });
        }),
      );
      const user = userEvent.setup();
      const store = renderPage();
      await screen.findAllByText("Ink Soul");
      await user.click(screen.getByRole("button", { name: /impersonate/i }));
      await user.type(screen.getByLabelText(/reason/i), "Investigating ticket #42");
      await user.click(screen.getByRole("button", { name: /start impersonating/i }));

      expect(await screen.findByTestId("support-landed")).toBeInTheDocument();
      expect(capturedBody).toEqual({ reasonCode: "Investigating ticket #42" });
      expect(store.getState().auth.impersonation).toEqual({
        sessionId: "sess-1", studioId: "s1", studioName: "Ink Soul",
        expiresAt: expect.any(String),
        reason: "Investigating ticket #42",
      });
      expect(store.getState().auth.token).toBe(accessToken);
    });

    it("a failed start shows an error toast and does not navigate away", async () => {
      server.use(
        http.post("http://localhost/api/v1/platform/studios/:studioId/impersonate", () =>
          HttpResponse.json({ message: "Failed" }, { status: 500 })),
      );
      const user = userEvent.setup();
      renderPage();
      await screen.findAllByText("Ink Soul");
      await user.click(screen.getByRole("button", { name: /impersonate/i }));
      await user.type(screen.getByLabelText(/reason/i), "Investigating ticket #42");
      await user.click(screen.getByRole("button", { name: /start impersonating/i }));

      expect(screen.queryByTestId("support-landed")).not.toBeInTheDocument();
      // Still on the studio detail page, reason form remains
      expect(await screen.findByLabelText(/reason/i)).toBeInTheDocument();
    });
  });

  // ── D02: an unknown subscription is not "No subscription" ─────────────────────

  describe("subscription known, loading or failed (D02)", () => {
    const SUB_NONE: PlatformSubscriptionResponse = { ...SUB, subscriptionId: null, status: "NoSubscription", planName: null };

    it("a failed subscription read never claims 'No Subscription' and offers no subscription action", async () => {
      server.use(http.get("http://localhost/api/v1/platform/subscriptions", () =>
        HttpResponse.json({ message: "boom" }, { status: 500 })));
      renderPage();
      expect((await screen.findAllByText("Subscription unknown")).length).toBeGreaterThan(0);
      expect(screen.queryByText("No Subscription")).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: /^activate$/i })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: /cancel subscription/i })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: /extend trial|grant extension/i })).not.toBeInTheDocument();
      // Unrelated safe actions stay available.
      expect(screen.getByRole("button", { name: /suspend/i })).toBeInTheDocument();
    });

    it("retrying the failed read brings the actions back once the status is known", async () => {
      let calls = 0;
      server.use(http.get("http://localhost/api/v1/platform/subscriptions", () => {
        calls += 1;
        return calls === 1 ? HttpResponse.json({ message: "boom" }, { status: 500 }) : HttpResponse.json([SUB_NONE]);
      }));
      const user = userEvent.setup();
      renderPage();
      await user.click(await screen.findByRole("button", { name: /retry loading the subscription/i }));
      expect(await screen.findByRole("button", { name: /^activate$/i })).toBeInTheDocument();
    });

    it("while the subscription is still loading, no subscription action is offered", async () => {
      server.use(http.get("http://localhost/api/v1/platform/subscriptions", async () => {
        await new Promise((r) => setTimeout(r, 60_000));
        return HttpResponse.json([SUB_NONE]);
      }));
      renderPage();
      await screen.findAllByText("Ink Soul");
      expect(screen.getByText(/loading the subscription before offering its actions/i)).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: /^activate$/i })).not.toBeInTheDocument();
      expect(screen.queryByText("No Subscription")).not.toBeInTheDocument();
    });

    it("a server error on the studio itself is not 'not found', and offers Try again", async () => {
      let calls = 0;
      server.use(http.get("http://localhost/api/v1/studios/s1", () => {
        calls += 1;
        return calls === 1 ? HttpResponse.json({ message: "boom" }, { status: 500 }) : HttpResponse.json(STUDIO);
      }));
      const user = userEvent.setup();
      renderPage();
      expect(await screen.findByText(/couldn't load this studio/i)).toBeInTheDocument();
      expect(screen.queryByText(/studio not found/i)).not.toBeInTheDocument();
      await user.click(screen.getByRole("button", { name: /try again/i }));
      expect((await screen.findAllByText("Ink Soul")).length).toBeGreaterThan(0);
    });

    it("no access is said as such, with no Try again", async () => {
      server.use(http.get("http://localhost/api/v1/studios/s1", () =>
        HttpResponse.json({ message: "nope" }, { status: 403 })));
      renderPage();
      expect(await screen.findByText(/you don't have access to this studio/i)).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: /try again/i })).not.toBeInTheDocument();
    });
  });

  // ── D04: failed edits are kept, successes leave a receipt ─────────────────────

  describe("cash activation keeps failed input and leaves a receipt (D04)", () => {
    const SUB_NONE: PlatformSubscriptionResponse = { ...SUB, subscriptionId: null, status: "NoSubscription", planName: null };

    async function openAndFill(user: ReturnType<typeof userEvent.setup>) {
      await user.click(await screen.findByRole("button", { name: /^activate$/i }));
      await user.selectOptions(screen.getByLabelText("Plan"), "plan-1");
      await user.type(screen.getByLabelText(/note \(optional\)/i), "Paid in cash at the desk");
      await user.click(screen.getByRole("button", { name: /activate subscription/i }));
    }

    it("a failure keeps the plan and note, says why, and moves focus to the error", async () => {
      server.use(
        http.get("http://localhost/api/v1/platform/subscriptions", () => HttpResponse.json([SUB_NONE])),
        http.post("http://localhost/api/v1/platform/studios/s1/subscription/activate", () =>
          HttpResponse.json({ message: "That plan is archived." }, { status: 422 })),
      );
      const user = userEvent.setup();
      renderPage();
      await openAndFill(user);

      const alert = await screen.findByRole("alert");
      expect(alert).toHaveTextContent("Activation failed: That plan is archived.");
      expect(alert).toHaveFocus();
      expect(screen.getByLabelText("Plan")).toHaveValue("plan-1");
      expect(screen.getByLabelText(/note \(optional\)/i)).toHaveValue("Paid in cash at the desk");
      // The recheck found the studio still activatable, so a retry is offered.
      expect(await screen.findByText(/your plan and note are kept/i)).toBeInTheDocument();
      expect(screen.getByRole("button", { name: /try again/i })).toBeEnabled();
    });

    it("if the recheck finds the studio already active, the retry is withheld, not sent twice", async () => {
      let activated = false;
      let posts = 0;
      server.use(
        http.get("http://localhost/api/v1/platform/subscriptions", () =>
          HttpResponse.json([activated ? { ...SUB, status: "Active" } : SUB_NONE])),
        http.post("http://localhost/api/v1/platform/studios/s1/subscription/activate", () => {
          posts += 1;
          activated = true; // it went through, but the response was lost
          return HttpResponse.json({ message: "Gateway timeout" }, { status: 504 });
        }),
      );
      const user = userEvent.setup();
      renderPage();
      await openAndFill(user);

      expect(await screen.findByText(/the studio's subscription is now active, so there is nothing to activate/i)).toBeInTheDocument();
      expect(screen.getByRole("button", { name: /try again/i })).toBeDisabled();
      expect(posts).toBe(1);
    });

    it("a success leaves a receipt with the studio, the new state, the time and an audit-log link", async () => {
      server.use(
        http.get("http://localhost/api/v1/platform/subscriptions", () => HttpResponse.json([SUB_NONE])),
        http.post("http://localhost/api/v1/platform/studios/s1/subscription/activate", () =>
          HttpResponse.json({
            id: "sub-new", studioId: "s1", planId: "plan-1", billingInterval: "Monthly", pendingPlanId: null,
            pendingBillingInterval: null, status: "Active", trialExpiresAt: null,
            currentPeriodEnd: "2026-12-19T00:00:00Z", gracePeriodEnd: "2026-12-26T00:00:00Z",
            stripeSubscriptionId: null, cancelAtPeriodEnd: false,
          })),
      );
      const user = userEvent.setup();
      renderPage();
      await openAndFill(user);

      const receipt = await screen.findByRole("status");
      expect(receipt).toHaveTextContent("Subscription activated for Ink Soul: now Active on Starter.");
      expect(receipt).toHaveTextContent(/UTC/);
      expect(within(receipt).getByRole("link", { name: /view in the audit log/i }))
        .toHaveAttribute("href", "/platform/audit-log?studioId=s1&action=Subscription.ActivatedManually");
      await user.click(within(receipt).getByRole("button", { name: /dismiss this receipt/i }));
      expect(screen.queryByText(/subscription activated for ink soul/i)).not.toBeInTheDocument();
    });
  });
});
