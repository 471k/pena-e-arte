import { test, expect } from "@playwright/test";
import { STUDIO_ID, OWNER_TOKEN, mockApiFallback, loginAs } from "./helpers";

// Studio settings (/studios/me) used to scroll sideways on a 390px phone: each Studio hours row put the
// day, the open switch and both time inputs on one line (~420px), wider than the card. The A07a setup
// checklist's "Set pay model" step sends owners to this page, so it has to fit a phone (A07a cleanup,
// finding 3). jsdom can't see this; it needs real layout. Checked at 390px and at the WCAG reflow
// width (320px, SC 1.4.10).

const json = (body: unknown) => ({ status: 200, contentType: "application/json", body: JSON.stringify(body) });

const STUDIO = {
  id: STUDIO_ID, name: "Tinta & Alma", slug: "tinta-alma", city: "Tirana", latitude: 41.3275, longitude: 19.8187,
  showPlatformBranding: true, allowBrandingRemoval: false, isActive: true, slugLockedAt: null,
  timezone: "Europe/Tirane", countryCode: "AL", currency: "ALL",
  trialExpiresAt: new Date(Date.now() + 14 * 86_400_000).toISOString(), createdAt: new Date().toISOString(),
};

// Every day open, so every row carries its time inputs.
const HOURS = Array.from({ length: 7 }, (_, dayOfWeek) => ({ dayOfWeek, startTime: "10:00:00", endTime: "19:00:00", isOpen: true }));

for (const width of [390, 320]) {
  test(`owner: studio settings fit a ${width}px viewport with no sideways page scroll`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await mockApiFallback(page);
    await page.route("**/api/v1/studios/me", (route) => route.fulfill(json(STUDIO)));
    await page.route(`**/api/v1/studios/${STUDIO_ID}/hours`, (route) => route.fulfill(json(HOURS)));
    await page.route("**/api/v1/studios/me/pay-model", (route) => route.fulfill(json({
      arrangement: "Commission", commissionRate: null, flatFeePerSession: null, reviewedAt: null, currency: "ALL",
    })));
    await page.route("**/api/v1/notifications/preferences", (route) => route.fulfill(json({ preferences: [] })));
    await page.route("**/api/v1/studios/me/audit-log*", (route) => route.fulfill(json({ items: [], totalCount: 0, page: 1, pageSize: 10 })));

    await loginAs(page, OWNER_TOKEN, "owner@tinta-alma.com", /^(?!.*\/login).+/);
    await page.goto("/studios/me");
    await expect(page.getByRole("switch", { name: /saturday open/i })).toBeVisible();
    await expect(page.getByTestId("studio-pay-model-card")).toBeVisible();

    const { overflow, widest } = await page.evaluate(() => {
      const rows = Array.from(document.querySelectorAll("[data-testid='studio-hours-row'] *"));
      return {
        overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
        widest: Math.max(...rows.map((el) => el.getBoundingClientRect().right)),
      };
    });

    expect(overflow, "the page scrolls sideways").toBeLessThanOrEqual(0);
    expect(widest, "a Studio hours control sits past the viewport edge").toBeLessThanOrEqual(width);
  });
}
