import { test, expect, type Page } from "@playwright/test";
import { mockApiFallback, loginAs, OWNER_TOKEN, STUDIO_ID } from "./helpers";

// WCAG 2.4.11: PinnedChromeScrollPadding measures pinned chrome into --pinned-top, and index.css turns it into
// html's scroll-padding-top. The measurement failed silently once (it dropped the 137px Schedule header), so on
// a page with the signed-in shell header the top padding is floored at that header's 3.5rem (56px): a measurement
// that finds nothing, finds too little, or never runs still pads the top. A page WITHOUT the shell gets no floor,
// because a flat one pushed a 138px portfolio tile under the public pages' quick-book bar at 400% zoom.
//
// --pinned-top is overwritten by hand here to stand in for each failure: the hook only re-measures on a change
// to <body>, so a value written on <html> stays until the next one.

const json = (b: unknown) => ({ status: 200, contentType: "application/json", body: JSON.stringify(b) });
const STUDIO = {
  id: STUDIO_ID, name: "Tinta & Alma", slug: "tinta-alma", city: "Tirana", latitude: 41, longitude: 19,
  showPlatformBranding: true, allowBrandingRemoval: false, isActive: true, slugLockedAt: null, isSolo: false,
  isPublished: true, timezone: "Europe/Tirane", countryCode: "AL", currency: "ALL",
  trialExpiresAt: new Date(Date.now() + 14 * 86_400_000).toISOString(), createdAt: new Date().toISOString(),
};

const paddingWith = (page: Page, pinnedTop: string | null) =>
  page.evaluate((value) => {
    const root = document.documentElement;
    if (value === null) root.style.removeProperty("--pinned-top");
    else root.style.setProperty("--pinned-top", value);
    return getComputedStyle(root).scrollPaddingTop;
  }, pinnedTop);

test("signed in: the top padding never falls below the shell header, whatever the measurement says", async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 256 });
  await page.addInitScript(() => localStorage.setItem("cookie-consent", "accepted"));
  await mockApiFallback(page);
  await page.route("**/api/v1/studios/me", (r) => r.fulfill(json(STUDIO)));
  await loginAs(page, OWNER_TOKEN, "owner@tinta-alma.com", /^(?!.*\/login).+/);
  await page.goto("/schedule");
  await expect(page.locator("header[data-pinned-shell]")).toBeVisible();

  expect(await paddingWith(page, "0px"), "the measurement found nothing").toBe("56px");
  expect(await paddingWith(page, "20px"), "the measurement found less than the shell").toBe("56px");
  expect(await paddingWith(page, null), "the measurement never ran (the hook threw)").toBe("56px");
  expect(await paddingWith(page, "137px"), "a real, taller measurement is kept").toBe("137px");
});

test("public page at 400% zoom: no shell header, so no floor and no padding the page does not need", async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 256 });
  await page.addInitScript(() => localStorage.setItem("cookie-consent", "accepted"));
  await mockApiFallback(page);
  await page.goto("/discover");
  await expect(page.locator("header").first()).toBeVisible();
  await expect(page.locator("header[data-pinned-shell]")).toHaveCount(0);

  expect(await paddingWith(page, "0px")).toBe("0px");
  expect(await paddingWith(page, null)).toBe("0px");
});
