import { test, expect, type Page } from "@playwright/test";
import { STUDIO_ID, OWNER_TOKEN, makeJwt, mockApiFallback, loginAs } from "./helpers";

// Slice 6c moved page-level layout to the package's page breakpoints (420/800/1100). These two pin the bands where
// behaviour visibly changed: a side column now joins at 1100, not 1024 (so a 1024-1099px window gets the stacked
// layout), and Messages shows list and thread side by side from 800, not 640 (a 320px list left 320-479px for the
// thread). The full ten-width, two-theme pass is recorded in the PR.

const ADMIN_TOKEN = makeJwt("admin", "user-id-admin", "admin@tattooos.co");
const json = (b: unknown) => ({ status: 200, contentType: "application/json", body: JSON.stringify(b) });
const STUDIO = {
  id: STUDIO_ID, name: "Tinta & Alma Tattoo Collective", slug: "tinta-alma", city: "Tirana", latitude: 41, longitude: 19,
  showPlatformBranding: true, allowBrandingRemoval: false, allowApiAccess: false, isActive: true, slugLockedAt: null,
  isSolo: false, isPublished: true, timezone: "Europe/Tirane", countryCode: "AL", currency: "ALL", currencyLocked: true,
  trialExpiresAt: new Date(Date.now() + 14 * 86_400_000).toISOString(), createdAt: "2026-01-01T00:00:00Z",
  phoneNumber: "+355691234567", instagramHandle: "tinta.alma", nipt: "L01234567A", subscriptionStatus: "Active",
  pastDueSince: null, addressLine1: "Rruga Myslym Shyri 42", addressLine2: null, postalCode: "1001",
};
const CONVERSATIONS = Array.from({ length: 4 }, (_, i) => ({
  id: `conv-${i}`, otherUserId: `u-${i}`, otherRole: "client", otherDisplayName: `Anastasia Hoxhaj ${i}`, otherAvatarUrl: null,
  lastMessageAt: new Date().toISOString(), lastMessagePreview: "Can we move the session to Thursday afternoon?",
  lastMessageFromMe: false, unreadCount: i % 2, createdAt: "2026-11-01T00:00:00Z",
}));

async function as(page: Page, token: string, w: number) {
  await page.setViewportSize({ width: w, height: 900 });
  await page.addInitScript(() => localStorage.setItem("cookie-consent", "accepted"));
  await mockApiFallback(page);
  await page.route("**/api/v1/studios/me", (r) => r.fulfill(json(STUDIO)));
  await page.route(`**/api/v1/studios/${STUDIO_ID}`, (r) => r.fulfill(json(STUDIO)));
  await page.route(`**/api/v1/platform/studios/${STUDIO_ID}/summary`, (r) => r.fulfill(json({
    ownerEmail: "owner@tinta-alma.com", ownerDisplayName: "Rafaela Costa", artistCount: 5, clientCount: 214, appointmentCount: 1203, recentRefunds: [],
  })));
  await page.route("**/api/v1/conversations", (r) => r.fulfill(json(CONVERSATIONS)));
  await page.route("**/api/v1/platform/stats", (r) => r.fulfill(json({
    totalStudios: 128, activeSubscriptions: 96, trialStudios: 14, gracePeriodStudios: 3, pastDueStudios: 5, cancelledStudios: 7,
    suspendedStudios: 3, mrr: 4321.5, mrrGrowthPercent: 4.2, trialConversionRate: 0.61, newStudiosThisMonth: 9, payingStudios: 96,
    atRiskMrr: 210, scheduledChurnMrr: 99, pausedMrr: 45, discountsThisMonth: 120, refundsThisMonth: 2,
  })));
  await page.route("**/api/v1/platform/refunds/failed", (r) => r.fulfill(json({ items: [], totalCount: 0 })));
  await page.route("**/api/v1/platform/audit-log/vocabulary", (r) => r.fulfill(json({ actions: ["SuspendStudio"], targetTypes: ["Studio"] })));
  await page.route("**/api/v1/platform/revenue-retention*", (r) => r.fulfill(json({ grossRevenueRetention: 0.97, netRevenueRetention: 1.02, startMrr: 4100, periodStart: "2026-10-01T00:00:00Z" })));
  await page.route(/\/api\/v1\/platform\/audit-log(\?.*)?$/, (r) => r.fulfill(json({ items: [], totalCount: 0, page: 1, pageSize: 25 })));
  await page.route("**/api/v1/reports/my-earnings", (r) => r.fulfill(json({
    monthlyTrend: [], periodTotal: 82000, clientPaidTotal: 164000, payments: [], currency: "ALL", excludedOtherCurrencyCount: 0,
    shareIsKnown: true, arrangement: "Commission", commissionRate: 50, flatFeePerSession: null,
  })));
  await page.route("**/api/v1/platform/traffic/live*", (r) => r.fulfill(json({ totalActive: 12, guestCount: 7, roleCounts: { client: 3, owner: 2 }, visitors: [] })));
  await page.route("**/api/v1/platform/traffic/history*", (r) => r.fulfill(json({ days: 7, dataPoints: [] })));
  await page.route("**/api/v1/platform/traffic/breakdown*", (r) => r.fulfill(json({ days: 7, topCountries: [], deviceBreakdown: [], browserBreakdown: [], topPages: [], topNetworks: [] })));
  await loginAs(page, token, "x@example.com", /^(?!.*\/login).+/);
}


test("AdminStudioDetail: Actions stack under the details below 1100, and sit beside them from 1100", async ({ page }) => {
  for (const [width, beside] of [[1099, false], [1100, true]] as const) {
    if (width === 1099) await as(page, ADMIN_TOKEN, width); else await page.setViewportSize({ width, height: 900 });
    await page.goto(`/platform/studios/${STUDIO_ID}`);
    const actions = page.getByRole("heading", { name: "Actions" });
    const overview = page.getByRole("heading", { name: "Studio Overview" });
    await expect(actions).toBeVisible();
    const a = (await actions.boundingBox())!;
    const o = (await overview.boundingBox())!;
    expect(a.x > o.x + o.width, `beside at ${width}`).toBe(beside);
    expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
  }
});

test("Messages: one pane at a time below 800, list and thread side by side from 800", async ({ page }) => {
  for (const [width, split] of [[799, false], [800, true]] as const) {
    if (width === 799) await as(page, OWNER_TOKEN, width); else await page.setViewportSize({ width, height: 900 });
    await page.goto("/messages");
    await expect(page.getByText("Anastasia Hoxhaj 0")).toBeVisible();
    const placeholder = page.getByText("Select a conversation to view messages.");
    if (split) await expect(placeholder).toBeVisible(); else await expect(placeholder).toBeHidden();
  }
});
