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

// 200% text on the narrowest phones (28 Nov 2026): the header's rem padding and gaps doubled and pushed the account
// menu 68px past a 320px screen (28px at 360px), and its open panel (w-56, 448px at 200%) started 132px off the left
// edge. The header now spaces in px and the panel is capped at the screen width. This checks the header, its
// account menu and its notification panel (w-80 anchored to the bell started 416px off a 320px screen; below sm it is
// now pinned 16px inside the header's edges). Page content is checked in reflow-200-text.spec.ts.
for (const width of [320, 360] as const) {
  for (const role of ROLES) {
    test(`${role.name} at ${width}px and 200% text: the header and its open account menu stay on screen`, async ({ page }) => {
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
      const header = page.locator("header").first();
      await expect(header).toBeVisible();

      await expect.poll(() => header.evaluate((el) => el.scrollWidth - el.clientWidth),
        { message: "the header scrolls sideways" }).toBeLessThanOrEqual(0);
      const headerRight = await page.evaluate(() => Math.max(
        ...Array.from(document.querySelector("header")!.querySelectorAll("*")).map((el) => el.getBoundingClientRect().right)));
      expect(headerRight, "a header control sits past the viewport edge").toBeLessThanOrEqual(width);

      const userMenu = header.getByRole("button", { name: /user menu$/i });
      await userMenu.click();
      const logOut = page.getByRole("button", { name: /log out/i });
      await expect(logOut).toBeVisible();
      const panel = await logOut.evaluate((el) => {
        const box = el.closest("div.absolute")!.getBoundingClientRect();
        return { left: box.left, right: box.right };
      });
      expect(panel.left, "the open account menu starts off the left edge").toBeGreaterThanOrEqual(0);
      expect(panel.right, "the open account menu ends past the right edge").toBeLessThanOrEqual(width);

      await page.keyboard.press("Escape");
      const bell = header.getByRole("button", { name: /^View notifications/ });
      await bell.click();
      await expect(bell).toHaveAttribute("aria-expanded", "true");
      const bellPanel = await bell.evaluate((el) => {
        const box = el.parentElement!.querySelector(":scope > div")!.getBoundingClientRect();
        return { left: box.left, right: box.right, top: box.top, bellBottom: el.getBoundingClientRect().bottom };
      });
      expect(bellPanel.left, "the open notification panel starts off the left edge").toBeGreaterThanOrEqual(0);
      expect(bellPanel.right, "the open notification panel ends past the right edge").toBeLessThanOrEqual(width);
      expect(bellPanel.top, "the open notification panel sits under the bell").toBeGreaterThanOrEqual(bellPanel.bellBottom);
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
