import { test, expect, type Page } from "@playwright/test";
import { STUDIO_ID, ARTIST_ID, OWNER_TOKEN, ARTIST_TOKEN, mockApiFallback, loginAs } from "./helpers";

// E05b-1, finding 4: the revenue trend chart was a fixed 640-wide SVG scaled to fit, so on a 390px phone
// its labels shrank to about 5px. It is now drawn at its container's pixel width. Checked on both pages
// that use it (owner Reports, artist My Earnings) at a phone width and a desktop width; jsdom can't see
// layout. The owner Payments list's new filters are checked for sideways scroll at the same widths.

const json = (body: unknown) => ({ status: 200, contentType: "application/json", body: JSON.stringify(body) });

const TREND = Array.from({ length: 12 }, (_, i) => ({
  month: `2026-${String(i + 1).padStart(2, "0")}`,
  revenue: 800 + i * 150,
}));

const STUDIO = {
  id: STUDIO_ID, name: "Tinta & Alma", slug: "tinta-alma", isActive: true, timezone: "Europe/Tirane",
  countryCode: "PT", currency: "EUR", trialExpiresAt: new Date(Date.now() + 14 * 86_400_000).toISOString(),
  createdAt: new Date().toISOString(),
};

async function measureLabels(page: Page) {
  await expect(page.getByRole("img", { name: /revenue trend/i })).toBeVisible();
  return page.evaluate(() => {
    const labels = Array.from(document.querySelectorAll("[data-testid='trend-month-label']"))
      .map((el) => el.getBoundingClientRect());
    const overlaps = labels.slice(1).some((r, i) => r.left < labels[i].right);
    return {
      count: labels.length,
      minHeight: Math.min(...labels.map((r) => r.height)),
      overlaps,
      overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    };
  });
}

for (const width of [390, 1280]) {
  test(`owner Reports: trend chart labels are readable and apart at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await mockApiFallback(page);
    await page.route("**/api/v1/studios/me", (r) => r.fulfill(json(STUDIO)));
    await page.route("**/api/v1/reports/revenue-summary*", (r) => r.fulfill(json({
      monthlyTrend: TREND, perArtist: [], currency: "EUR", excludedOtherCurrencyCount: 0,
    })));
    await loginAs(page, OWNER_TOKEN, "owner@tinta-alma.com", /^(?!.*\/login).+/);
    await page.goto("/reports");

    const m = await measureLabels(page);
    // An 11px font renders a text box about 13px tall; the old chart's labels were ~5px tall on a phone.
    expect(m.minHeight, "month labels are too small to read").toBeGreaterThanOrEqual(12);
    expect(m.overlaps, "two month labels overlap").toBe(false);
    expect(m.count, width < 640 ? "a phone shows every other month" : "a desktop shows every month")
      .toBe(width < 640 ? 6 : 12);
    expect(m.overflow, "the page scrolls sideways").toBeLessThanOrEqual(0);
  });

  test(`artist My Earnings: trend chart labels are readable and apart at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await mockApiFallback(page);
    await page.route("**/api/v1/studios/me", (r) => r.fulfill(json(STUDIO)));
    await page.route("**/api/v1/artists/me", (r) => r.fulfill(json({ id: ARTIST_ID, studioId: STUDIO_ID, firstName: "R", lastName: "C" })));
    await page.route("**/api/v1/reports/my-earnings*", (r) => r.fulfill(json({
      monthlyTrend: TREND.map((t) => ({ month: t.month, artistAmount: t.revenue * 0.6, clientPaid: t.revenue, shareIsKnown: true })),
      periodTotal: 1000, clientPaidTotal: 1600, payments: [], currency: "EUR", excludedOtherCurrencyCount: 0,
      shareIsKnown: true, arrangement: "Commission", commissionRate: 60, flatFeePerSession: null,
    })));
    await loginAs(page, ARTIST_TOKEN, "rafaela@tinta-alma.com", /^(?!.*\/login).+/);
    await page.goto("/earnings");

    const m = await measureLabels(page);
    expect(m.minHeight).toBeGreaterThanOrEqual(12);
    expect(m.overlaps).toBe(false);
    expect(m.overflow).toBeLessThanOrEqual(0);
  });

  test(`owner Payments: the filters fit ${width}px with no sideways scroll`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await mockApiFallback(page);
    await page.route("**/api/v1/studios/me", (r) => r.fulfill(json(STUDIO)));
    await loginAs(page, OWNER_TOKEN, "owner@tinta-alma.com", /^(?!.*\/login).+/);
    await page.goto("/payments");

    await expect(page.getByTestId("payment-filters")).toBeVisible();
    await expect(page.getByLabel("Paid from")).toBeVisible();
    const { overflow, widest } = await page.evaluate(() => ({
      overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      widest: Math.max(...Array.from(document.querySelectorAll("[data-testid='payment-filters'] *"))
        .map((el) => el.getBoundingClientRect().right)),
    }));
    expect(overflow, "the page scrolls sideways").toBeLessThanOrEqual(0);
    expect(widest, "a filter control sits past the viewport edge").toBeLessThanOrEqual(width);
  });
}
