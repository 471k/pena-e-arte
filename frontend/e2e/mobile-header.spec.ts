import { test, expect } from "@playwright/test";
import { STUDIO_ID, ARTIST_ID, OWNER_TOKEN, CLIENT_TOKEN, ARTIST_TOKEN, makeJwt, mockApiFallback, loginAs } from "./helpers";

// The app header (logo, nav trigger, feedback/help/messages/bell icons, user menu) used to be ~50px wider
// than a 375px phone in every role's layout, so the whole document scrolled sideways. jsdom can't see
// this — it needs real layout. Checked at 375px and at the WCAG reflow width (320px, SC 1.4.10).

const ADMIN_TOKEN = makeJwt("admin", "user-id-admin", "admin@tattooos.co");

// Owner and artist are measured on an artist profile page: that's where the header carries the most
// (the owner has an artist profile too, so the layout also mounts its artist-mode pieces).
const ARTIST = {
  id: ARTIST_ID, studioId: STUDIO_ID, firstName: "Rafaela", lastName: "Costa", email: "rafaela@tinta-alma.com",
  specializations: [], hourlyRate: null, isActive: true, avatarUrl: null, portfolioImages: [], slug: null,
  userId: "user-id-artist", createdAt: "2024-01-15T10:00:00.000Z", updatedAt: "2024-06-01T10:00:00.000Z",
};

const ROLES: ReadonlyArray<{ name: string; token: string; email: string; landing: RegExp; path?: string }> = [
  { name: "owner",  token: OWNER_TOKEN,  email: "owner@tinta-alma.com",   landing: /^(?!.*\/login).+/, path: `/artists/${ARTIST_ID}` },
  { name: "artist", token: ARTIST_TOKEN, email: "rafaela@tinta-alma.com", landing: /^(?!.*\/login).+/, path: `/artists/${ARTIST_ID}` },
  // C01: a client lands on their bookings.
  { name: "client", token: CLIENT_TOKEN, email: "ana@example.com",        landing: /\/my-appointments/i },
  { name: "admin",  token: ADMIN_TOKEN,  email: "admin@tattooos.co",      landing: /^(?!.*\/login).+/ },
];

for (const width of [375, 320]) {
  for (const role of ROLES) {
    test(`${role.name}: the header fits a ${width}px viewport with no sideways page scroll`, async ({ page }) => {
      await page.setViewportSize({ width, height: 800 });
      await mockApiFallback(page);
      if (role.path) {
        const json = (body: unknown) => ({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
        await page.route(`**/api/v1/artists/${ARTIST_ID}`, (route) => route.fulfill(json(ARTIST)));
        await page.route("**/api/v1/artists/me", (route) => route.fulfill(json(ARTIST)));
        await page.route(`**/api/v1/artists/${ARTIST_ID}/schedule`, (route) => route.fulfill(json({ schedule: [], timeOff: [] })));
      }
      await loginAs(page, role.token, role.email, role.landing);
      if (role.path) await page.goto(role.path);
      await expect(page.locator("header").first()).toBeVisible();

      const { overflow, headerRight } = await page.evaluate(() => {
        const header = document.querySelector("header");
        const rightmost = header
          ? Math.max(...Array.from(header.querySelectorAll("*")).map((el) => el.getBoundingClientRect().right))
          : 0;
        return {
          overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
          headerRight: rightmost,
        };
      });

      expect(overflow, "document scrolls sideways").toBeLessThanOrEqual(0);
      expect(headerRight, "a header control sits past the viewport edge").toBeLessThanOrEqual(width);
    });
  }
}

// S05's last tail: 200% text on a 390px phone overflowed every role's header (owner and artist 280px, client 208,
// admin 119, measured 23 Nov). Below its rem threshold (useHeaderFold) the header folds feedback, help and messages
// into one "More" menu and drops the wordmark; the notification bell and the account menu stay. The text size is set
// before the app loads, as a user's own browser setting would be.
for (const textSize of ["100%", "200%"] as const) {
  for (const role of ROLES) {
    test(`${role.name} at 390px and ${textSize} text: no sideways scroll, and the header folds only when it must`, async ({ page }) => {
      await page.setViewportSize({ width: 390, height: 800 });
      await page.addInitScript((size) => {
        document.addEventListener("DOMContentLoaded", () => { document.documentElement.style.fontSize = size; });
      }, textSize);
      await mockApiFallback(page);
      if (role.path) {
        const json = (body: unknown) => ({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
        await page.route(`**/api/v1/artists/${ARTIST_ID}`, (route) => route.fulfill(json(ARTIST)));
        await page.route("**/api/v1/artists/me", (route) => route.fulfill(json(ARTIST)));
        await page.route(`**/api/v1/artists/${ARTIST_ID}/schedule`, (route) => route.fulfill(json({ schedule: [], timeOff: [] })));
      }
      await loginAs(page, role.token, role.email, role.landing);
      if (role.path) await page.goto(role.path);
      const header = page.locator("header").first();
      await expect(header).toBeVisible();

      // Admin has only Help to fold, and a one-item menu saves nothing, so it never shows "More".
      const folds = textSize === "200%" && role.name !== "admin";
      const more = header.getByRole("button", { name: /^More/ });
      const help = header.getByRole("button", { name: "Open help menu" });
      if (folds) {
        await expect(more).toBeVisible();
        await expect(help).toHaveCount(0);
      } else {
        await expect(help).toBeVisible();
        await expect(more).toHaveCount(0);
      }
      // The bell and the account menu never fold.
      await expect(header.getByRole("button", { name: "User menu" })).toBeVisible();

      await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth),
        { message: "document scrolls sideways" }).toBeLessThanOrEqual(0);
      const headerRight = await page.evaluate(() => Math.max(
        ...Array.from(document.querySelector("header")!.querySelectorAll("*")).map((el) => el.getBoundingClientRect().right)));
      expect(headerRight, "a header control sits past the viewport edge").toBeLessThanOrEqual(390);

      if (folds) {
        // What folded is still one tap away.
        await more.click();
        await expect(page.getByRole("menuitem", { name: "Help" })).toBeVisible();
        await expect(page.getByRole("menuitem", { name: /^Messages/ })).toBeVisible();
        await page.getByRole("menuitem", { name: "Help" }).click();
        await expect(page.getByRole("dialog", { name: "Help" })).toBeVisible();
      }
    });
  }
}

// From 640px up the header also carries the Owner/Artist switch, the studio's name and wider gaps, so a fixed rem
// threshold calibrated on a phone let a 768px window at 200% text overflow by 446px (slice 9's pass, 23 Nov). The
// header now also folds when its content overflows.
for (const width of [768, 1024] as const) {
  for (const role of ROLES) {
    test(`${role.name} at ${width}px and 200% text: no sideways scroll`, async ({ page }) => {
      await page.setViewportSize({ width, height: 800 });
      await page.addInitScript(() => {
        document.addEventListener("DOMContentLoaded", () => { document.documentElement.style.fontSize = "200%"; });
      });
      await mockApiFallback(page);
      if (role.path) {
        const json = (body: unknown) => ({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
        await page.route(`**/api/v1/artists/${ARTIST_ID}`, (route) => route.fulfill(json(ARTIST)));
        await page.route("**/api/v1/artists/me", (route) => route.fulfill(json(ARTIST)));
        await page.route(`**/api/v1/artists/${ARTIST_ID}/schedule`, (route) => route.fulfill(json({ schedule: [], timeOff: [] })));
      }
      await loginAs(page, role.token, role.email, role.landing);
      if (role.path) await page.goto(role.path);
      await expect(page.locator("header").first()).toBeVisible();
      await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth),
        { message: "document scrolls sideways" }).toBeLessThanOrEqual(0);
      // The bell and the account menu never fold.
      await expect(page.locator("header").first().getByRole("button", { name: "User menu" })).toBeVisible();
    });
  }
}
