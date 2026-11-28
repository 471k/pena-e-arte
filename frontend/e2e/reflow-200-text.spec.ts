import { test, expect } from "@playwright/test";
import { STUDIO_ID, ARTIST_ID, OWNER_TOKEN, CLIENT_TOKEN, ARTIST_TOKEN, mockApiFallback, loginAs } from "./helpers";

// Page content at 200% text on the narrowest phones (28 Nov 2026 follow-ups). Each page scrolled sideways on main:
// client bookings 27px (the empty state's button and "Copy share link"), owner dashboard 100px (the page bar's
// button and the three stat cards shrunk to 69px), the artist profile 1px ("Resend invite"), artist Today 6px
// ("Open schedule"), /map 64px at 320 and 145px at 360 ("Register your studio"), /register 30px (the registration
// type toggle). The header and its menus are covered in mobile-header.spec.ts. jsdom can't see any of this.

const ARTIST = {
  id: ARTIST_ID, studioId: STUDIO_ID, firstName: "Rafaela", lastName: "Costa", email: "rafaela@tinta-alma.com",
  specializations: [], hourlyRate: null, isActive: true, avatarUrl: null, portfolioImages: [], slug: null,
  userId: "user-id-artist", createdAt: "2024-01-15T10:00:00.000Z", updatedAt: "2024-06-01T10:00:00.000Z",
};

const PAGES: ReadonlyArray<{ name: string; path: string; ready: string; token?: string; email?: string; landing?: RegExp }> = [
  { name: "client bookings", path: "/my-appointments", ready: "main", token: CLIENT_TOKEN, email: "ana@example.com", landing: /\/my-appointments/i },
  { name: "owner dashboard", path: "/dashboard", ready: "[data-testid='stat-deposits']", token: OWNER_TOKEN, email: "owner@tinta-alma.com", landing: /^(?!.*\/login).+/ },
  { name: "owner artist profile", path: `/artists/${ARTIST_ID}`, ready: "[role='tablist']", token: OWNER_TOKEN, email: "owner@tinta-alma.com", landing: /^(?!.*\/login).+/ },
  { name: "artist today", path: "/today", ready: "main h1", token: ARTIST_TOKEN, email: "rafaela@tinta-alma.com", landing: /^(?!.*\/login).+/ },
  { name: "public map", path: "/map", ready: "nav a[href='/register']" },
  { name: "register", path: "/register", ready: "[aria-label='Registration type']" },
];

for (const width of [320, 360] as const) {
  for (const p of PAGES) {
    test(`${p.name} at ${width}px and 200% text: no sideways scroll`, async ({ page }) => {
      await page.setViewportSize({ width, height: 800 });
      await page.addInitScript(() => {
        document.addEventListener("DOMContentLoaded", () => { document.documentElement.style.fontSize = "200%"; });
      });
      await mockApiFallback(page);
      const json = (body: unknown) => ({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
      await page.route(`**/api/v1/artists/${ARTIST_ID}`, (route) => route.fulfill(json(ARTIST)));
      await page.route("**/api/v1/artists/me", (route) => route.fulfill(json(ARTIST)));
      await page.route(`**/api/v1/artists/${ARTIST_ID}/schedule`, (route) => route.fulfill(json({ schedule: [], timeOff: [] })));
      if (p.token) await loginAs(page, p.token, p.email!, p.landing!);
      await page.goto(p.path);
      await expect(page.locator(p.ready).first()).toBeVisible();
      await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth),
        { message: "the page scrolls sideways" }).toBeLessThanOrEqual(0);
    });
  }
}
