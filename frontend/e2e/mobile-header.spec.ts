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
  { name: "client", token: CLIENT_TOKEN, email: "ana@example.com",        landing: /\/book/i },
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
