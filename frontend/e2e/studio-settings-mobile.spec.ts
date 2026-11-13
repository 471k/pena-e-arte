import { test, expect, type Page } from "@playwright/test";
import { STUDIO_ID, OWNER_TOKEN, mockApiFallback, loginAs } from "./helpers";

// Studio settings (/studios/me) used to scroll sideways on a 390px phone: each Studio hours row put the
// day, the open switch and both time inputs on one line (~420px), wider than the card. The A07a setup
// checklist's "Set pay model" step sends owners to this page, so it has to fit a phone (A07a cleanup,
// finding 3). jsdom can't see this; it needs real layout. Checked at 390px and at the WCAG reflow
// width (320px, SC 1.4.10).
//
// E05c (O04) grouped the page's cards under collapsible headings. The width checks now run with every
// group closed (how the page opens, Business details aside) and again with every group open, and the
// deep links and the tour's card-level steps are checked here too: jsdom can see whether a target sits
// inside a `hidden` group, but only a browser can show the spotlight actually lands on it.

const json = (body: unknown) => ({ status: 200, contentType: "application/json", body: JSON.stringify(body) });

const STUDIO = {
  id: STUDIO_ID, name: "Tinta & Alma", slug: "tinta-alma", city: "Tirana", latitude: 41.3275, longitude: 19.8187,
  showPlatformBranding: true, allowBrandingRemoval: false, isActive: true, slugLockedAt: null,
  timezone: "Europe/Tirane", countryCode: "AL", currency: "ALL",
  trialExpiresAt: new Date(Date.now() + 14 * 86_400_000).toISOString(), createdAt: new Date().toISOString(),
};

// Every day open, so every row carries its time inputs.
const HOURS = Array.from({ length: 7 }, (_, dayOfWeek) => ({ dayOfWeek, startTime: "10:00:00", endTime: "19:00:00", isOpen: true }));

const GROUP_TITLES = [
  "Business details", "Hours and closures", "Payments and artist pay", "Online booking",
  "Notifications", "Integrations", "Referral programme", "Recent activity",
];

async function signIn(page: Page, width: number, opts: { tour?: boolean } = {}) {
  await page.setViewportSize({ width, height: 900 });
  // The cookie banner is fixed to the bottom of a phone screen and covers the last group headings.
  await page.addInitScript(() => localStorage.setItem("cookie-consent", "accepted"));
  await mockApiFallback(page);
  await page.route("**/api/v1/studios/me", (route) => route.fulfill(json(STUDIO)));
  await page.route(`**/api/v1/studios/${STUDIO_ID}/hours`, (route) => route.fulfill(json(HOURS)));
  await page.route("**/api/v1/studios/me/pay-model", (route) => route.fulfill(json({
    arrangement: "Commission", commissionRate: null, flatFeePerSession: null, reviewedAt: null, currency: "ALL",
  })));
  await page.route("**/api/v1/notifications/preferences", (route) => route.fulfill(json({ preferences: [] })));
  // The tour visits /artists, whose plan-usage line can't read the catch-all's `[]`.
  await page.route("**/api/v1/billing/usage", (route) => route.fulfill(json(null)));
  await page.route("**/api/v1/studios/me/audit-log*", (route) => route.fulfill(json({ items: [], totalCount: 0, page: 1, pageSize: 10 })));
  if (opts.tour) {
    await page.route("**/api/v1/onboarding/tour-status*", (route) => route.fulfill(json({ hasCompletedTour: false })));
  }
  await loginAs(page, OWNER_TOKEN, "owner@tinta-alma.com", /^(?!.*\/login).+/);
}

async function measureOverflow(page: Page) {
  return page.evaluate(() => {
    const rows = Array.from(document.querySelectorAll("[data-testid='studio-hours-row'] *"))
      .filter((el) => !el.closest("[hidden]"));
    return {
      overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      widest: rows.length ? Math.max(...rows.map((el) => el.getBoundingClientRect().right)) : 0,
    };
  });
}

for (const width of [390, 320]) {
  test(`owner: studio settings fit a ${width}px viewport with no sideways page scroll, groups closed and open`, async ({ page }) => {
    await signIn(page, width);
    await page.goto("/studios/me");
    const main = page.getByRole("main");
    await expect(main.getByRole("heading", { level: 2, name: "Business details" })).toBeVisible();

    const closed = await measureOverflow(page);
    expect(closed.overflow, "the page scrolls sideways with the groups closed").toBeLessThanOrEqual(0);

    for (const title of GROUP_TITLES.slice(1)) {
      await main.getByRole("button", { name: title }).click();
      await expect(main.getByRole("button", { name: title })).toHaveAttribute("aria-expanded", "true");
    }
    await expect(page.getByRole("switch", { name: /saturday open/i })).toBeVisible();
    await expect(page.getByTestId("studio-pay-model-card")).toBeVisible();

    const open = await measureOverflow(page);
    expect(open.overflow, "the page scrolls sideways with every group open").toBeLessThanOrEqual(0);
    expect(open.widest, "a Studio hours control sits past the viewport edge").toBeLessThanOrEqual(width);
  });
}

test("owner at 390px: the setup checklist's links land with their card's group open", async ({ page }) => {
  await signIn(page, 390);
  await page.goto("/studios/me?section=hours");
  await expect(page.getByRole("switch", { name: /saturday open/i })).toBeVisible();
  await expect(page.getByRole("switch", { name: /saturday open/i })).toBeInViewport();

  await page.goto("/studios/me?section=money");
  await expect(page.getByTestId("studio-pay-model-card")).toBeVisible();
});

test("owner at 390px: each card-level tour step on Studio Settings spotlights its card, not a hidden box", async ({ page }) => {
  await signIn(page, 390, { tour: true });
  await page.goto("/dashboard");

  const tour = page.locator("[data-onboarding-tour]");
  const counter = tour.getByText(/^\d+ \/ \d+$/);
  const cardSteps: Array<{ title: string; target: string }> = [
    { title: "Set your hours", target: "owner-studio-hours-card" },
    { title: "Your currency", target: "owner-studio-currency-card" },
    { title: "How artists get paid", target: "owner-studio-pay-model-card" },
    { title: "Take card deposits with POK", target: "owner-pok-card" },
  ];

  let seen = 0;
  // Step through the owner tour. Each click waits for the next popover's counter: a skipped step shows no
  // popover while it polls, so clicking on a timer would press a later step's Next (owner-phone-navigation).
  for (let i = 0; i < 25 && seen < cardSteps.length; i++) {
    await expect(counter).toBeVisible();
    const heading = (await tour.getByRole("heading").first().textContent()) ?? "";
    const step = cardSteps.find((s) => heading.includes(s.title));
    if (step) {
      await expect(page).toHaveURL(/\/studios\/me\?section=/);
      const target = page.locator(`[data-tour="${step.target}"]`);
      await expect(target, `${step.title}: its card isn't showing`).toBeVisible();
      // The spotlight follows the card after the scroll settles.
      await expect.poll(async () => (await page.getByTestId("tour-spotlight").boundingBox())?.width ?? 0,
        { message: `${step.title}: the spotlight has no size (a hidden target)` }).toBeGreaterThan(40);
      // The spotlight surrounds the card (it pads 6 px each side), measured once the scroll has settled;
      // 2 px of slack for sub-pixel layout.
      await expect.poll(async () => {
        const spot = await page.getByTestId("tour-spotlight").boundingBox();
        const box = await target.boundingBox();
        if (!spot || !box) return false;
        return spot.x <= box.x + 2 && spot.y <= box.y + 2 && spot.x + spot.width >= box.x + box.width - 2;
      }, { message: `${step.title}: the spotlight doesn't surround its card` }).toBe(true);
      seen += 1;
    }
    const before = await counter.textContent();
    await tour.getByRole("button", { name: /^(next|done)/i }).click();
    if (seen < cardSteps.length) await expect(counter).not.toHaveText(before ?? "");
  }
  expect(seen, "not every Studio Settings card step showed").toBe(cardSteps.length);
});
