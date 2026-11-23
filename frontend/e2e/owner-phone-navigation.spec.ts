import { test, expect, type Page } from "@playwright/test";
import { STUDIO_ID, ARTIST_ID, OWNER_TOKEN, mockApiFallback, loginAs } from "./helpers";

// Owner chrome on a 390px phone (owner phone-navigation patch, 2026-11-12, found by the first signed-in
// look at production on a real phone):
//  - the Owner/Artist switch is `hidden sm:flex` in the header, so on a phone it has to be in the nav
//    drawer, or artist mode is only reachable through "My Portfolio", which doesn't say it swaps the menu;
//  - the onboarding tour's switch step must point at that drawer copy, not at the hidden header one;
//  - the Dashboard header used to put "Dashboard" against "+ Book Appointment" and wrap the date to
//    three lines.
// jsdom can't see any of this; it needs real layout. Same measuring approach as studio-settings-mobile.

const json = (body: unknown) => ({ status: 200, contentType: "application/json", body: JSON.stringify(body) });

const STUDIO = {
  id: STUDIO_ID, name: "Tinta & Alma", slug: "tinta-alma", city: "Tirana", latitude: 41.3275, longitude: 19.8187,
  showPlatformBranding: true, allowBrandingRemoval: false, isActive: true, slugLockedAt: null, isSolo: true,
  isPublished: true, timezone: "Europe/Tirane", countryCode: "AL", currency: "ALL",
  trialExpiresAt: new Date(Date.now() + 14 * 86_400_000).toISOString(), createdAt: new Date().toISOString(),
};

// The owner's own artist profile: a dual-role owner, like the production account that found this.
const MY_ARTIST = {
  id: ARTIST_ID, studioId: STUDIO_ID, firstName: "Rafaela", lastName: "Costa", email: "owner@tinta-alma.com",
  specializations: [], hourlyRate: null, isActive: true, avatarUrl: null, portfolioImages: [], slug: null,
  userId: "user-id-owner", createdAt: "2024-01-15T10:00:00.000Z", updatedAt: "2024-06-01T10:00:00.000Z",
};

async function signInAsDualRoleOwner(page: Page, opts: { tour?: boolean; myArtistDelayMs?: number } = {}) {
  await page.setViewportSize({ width: 390, height: 844 });
  await mockApiFallback(page);
  await page.route("**/api/v1/studios/me", (route) => route.fulfill(json(STUDIO)));
  await page.route("**/api/v1/artists/me", async (route) => {
    if (opts.myArtistDelayMs) await new Promise((r) => setTimeout(r, opts.myArtistDelayMs));
    await route.fulfill(json(MY_ARTIST));
  });
  await page.route(`**/api/v1/artists/${ARTIST_ID}`, (route) => route.fulfill(json(MY_ARTIST)));
  await page.route(`**/api/v1/artists/${ARTIST_ID}/schedule`, (route) => route.fulfill(json({ schedule: [], timeOff: [] })));
  if (opts.tour) {
    await page.route("**/api/v1/onboarding/tour-status*", (route) => route.fulfill(json({ hasCompletedTour: false })));
  }
  await loginAs(page, OWNER_TOKEN, "owner@tinta-alma.com", /^(?!.*\/login).+/);
}

const SWITCH = { name: /switch between owner and artist/i };

test("owner at 390px: the nav drawer's Owner/Artist switch enters artist mode, and Today is in its menu", async ({ page }) => {
  await signInAsDualRoleOwner(page);
  await page.goto("/dashboard");

  // The header copy exists but is hidden below sm.
  await expect(page.locator("header").getByRole("tablist", SWITCH)).toBeHidden();

  await page.getByRole("button", { name: /open navigation menu/i }).click();
  const drawer = page.getByRole("dialog");
  const drawerSwitch = drawer.getByRole("tablist", SWITCH);
  await expect(drawerSwitch).toBeVisible();

  // Both halves fit inside the drawer.
  const box = await drawerSwitch.boundingBox();
  const drawerBox = await drawer.boundingBox();
  expect(box && drawerBox && box.x + box.width <= drawerBox.x + drawerBox.width).toBeTruthy();

  await drawerSwitch.getByRole("tab", { name: /artist/i }).click();
  await expect(page).toHaveURL(new RegExp(`/artists/${ARTIST_ID}$`));
  await expect(page.getByRole("dialog")).toBeHidden();

  await page.getByRole("button", { name: /open navigation menu/i }).click();
  await expect(page.getByRole("dialog").getByRole("link", { name: /^today$/i })).toBeVisible();
  await expect(page.getByRole("dialog").getByRole("tab", { name: /artist/i })).toHaveAttribute("aria-selected", "true");
});

// Walks the owner tour from the start to the Owner/Artist switch step and checks its spotlight is on the drawer copy.
// `firstStopTimeoutMs`: the tour waits for GET /artists/me before its first stop, so a delayed answer delays it.
async function expectTourReachesTheDrawerSwitch(page: Page, firstStopTimeoutMs = 5_000) {
  const tour = page.locator("[data-onboarding-tour]");
  const counter = tour.getByText(/^\d+ \/ \d+$/);
  // Step through the earlier owner steps (some open the drawer) until the switch step shows. "Also work as an
  // artist?" isn't in this owner's tour at all: they already have a profile, so the switch is step 4.
  // Each click waits for the next popover's counter: a skipped step shows no popover while it polls,
  // so clicking on a timer would press the following step's Next and step straight past it.
  // `seen` records each stop (counter, heading, whether the drawer was open, and the spotlight's box), so a CI
  // failure says which step the tour skipped or where it stopped instead of only "not found". Since #415 this
  // test failed intermittently on CI with the switch step's popover never shown: the tour started before
  // GET /artists/me answered, so the switch wasn't rendered yet and OnboardingTour timed the step out. The tour
  // now waits for that answer (useOnboardingTour); the delayed-/artists/me test below pins it.
  const seen: string[] = [];
  for (let i = 0; i < 8; i++) {
    await expect(counter).toBeVisible({ timeout: i === 0 ? firstStopTimeoutMs : 5_000 });
    const heading = (await tour.getByRole("heading").first().textContent()) ?? "";
    const spotBox = await page.getByTestId("tour-spotlight").boundingBox();
    // The tour popover is itself a dialog, so "drawer open" is read as "the drawer's switch is showing".
    const drawerSwitchShowing = await page.getByRole("dialog").getByRole("tablist", SWITCH).first().isVisible();
    seen.push(`${await counter.textContent()} "${heading}" drawerSwitch=${drawerSwitchShowing} spot=${spotBox ? `${Math.round(spotBox.x)},${Math.round(spotBox.y)} ${Math.round(spotBox.width)}x${Math.round(spotBox.height)}` : "none"}`);
    if (heading.includes("Switch between Owner and Artist")) break;
    const before = await counter.textContent();
    await tour.getByRole("button", { name: /^next/i }).click();
    await expect(counter).not.toHaveText(before ?? "");
  }
  await expect(tour.getByText("Switch between Owner and Artist", { exact: true }), `tour stops seen: ${seen.join(" | ")}`).toBeVisible();
  await expect(counter).toHaveText(/^4 \//);

  const target = await page.getByRole("dialog").getByRole("tablist", SWITCH).boundingBox();
  expect(target, "the drawer switch isn't showing").not.toBeNull();
  // The spotlight glides from the previous step's target (a CSS transition, ~250 ms; measured 22 Nov: the switch
  // stays put while the spotlight travels y 233 -> 87), so poll until it has arrived rather than reading frame one.
  await expect.poll(async () => {
    const spot = await page.getByTestId("tour-spotlight").boundingBox();
    if (!spot) return "no spotlight";
    if (spot.width <= 40) return "the spotlight has no size: it's on the hidden header copy";
    // The spotlight surrounds the drawer switch (it adds a few px of padding on every side).
    const surrounds = spot.x <= target!.x && spot.y <= target!.y
      && spot.x + spot.width >= target!.x + target!.width && spot.y + spot.height >= target!.y + target!.height;
    return surrounds ? "surrounds the drawer switch" : `spot ${Math.round(spot.x)},${Math.round(spot.y)} ${Math.round(spot.width)}x${Math.round(spot.height)} misses it`;
  }).toBe("surrounds the drawer switch");
}

test("owner at 390px: the tour's Owner/Artist step spotlights the switch in the drawer, not a hidden box", async ({ page }) => {
  await signInAsDualRoleOwner(page, { tour: true });
  await page.goto("/dashboard");
  await expectTourReachesTheDrawerSwitch(page);
});

// The CI flake reproduced on purpose (22 Nov): before the tour waited for GET /artists/me, a 5 s answer made it skip
// the switch step and the next ones too (it stopped at "Build your service catalog"). The old skip needed about
// 3.6 s locally, so 5 s is past it there. On a slower runner the old code needed longer, so this test can pass
// against the old code on CI: its mutation check was run locally.
test("owner at 390px: a slow GET /artists/me doesn't make the tour skip the Owner/Artist step", async ({ page }) => {
  test.slow();
  await signInAsDualRoleOwner(page, { tour: true, myArtistDelayMs: 5_000 });
  await page.goto("/dashboard");
  await expectTourReachesTheDrawerSwitch(page, 15_000);
});

// S05 follow-up: from 640px up the header normally shows its own Owner/Artist switch, but at 200% text it doesn't fit
// and the header folds, which hides that copy as on a phone. The tour's switch step must then open the drawer and
// spotlight the drawer copy, decided from the fold itself, not time out looking for the header copy.
test("owner at 768px and 200% text: the header folds and the tour's Owner/Artist step spotlights the drawer switch", async ({ page }) => {
  await page.addInitScript(() => {
    document.addEventListener("DOMContentLoaded", () => { document.documentElement.style.fontSize = "200%"; });
  });
  await signInAsDualRoleOwner(page, { tour: true });
  await page.setViewportSize({ width: 768, height: 1024 });
  await page.goto("/dashboard");
  await expect(page.locator("header").first().getByRole("button", { name: /^More/ })).toBeVisible();
  await expectTourReachesTheDrawerSwitch(page);
});

for (const width of [390, 320]) {
  test(`owner: the Dashboard header fits ${width}px with nothing colliding`, async ({ page }) => {
    await signInAsDualRoleOwner(page);
    await page.setViewportSize({ width, height: 844 });
    await page.goto("/dashboard");

    const button = page.getByRole("button", { name: /\+ book appointment/i });
    await expect(button).toBeVisible();
    const titleEl = page.getByText("Dashboard", { exact: true }).filter({ visible: true }).first();
    const date = page.getByTestId("dashboard-date");

    const [titleBox, buttonBox, dateBox] = await Promise.all([titleEl.boundingBox(), button.boundingBox(), date.boundingBox()]);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);

    expect(overflow, "the page scrolls sideways").toBeLessThanOrEqual(0);
    expect(buttonBox!.x + buttonBox!.width, "the button runs past the viewport edge").toBeLessThanOrEqual(width);
    expect(titleBox!.x + titleBox!.width, "the title touches the button").toBeLessThan(buttonBox!.x);
    expect(dateBox!.x + dateBox!.width, "the date runs under the button").toBeLessThan(buttonBox!.x);
    // One line of text-xs (16px line-height), not the three it used to wrap to.
    expect(dateBox!.height, "the date wraps").toBeLessThanOrEqual(20);
  });
}
