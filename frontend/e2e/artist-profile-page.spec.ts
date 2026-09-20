import { test, expect, type Page } from "@playwright/test";
import { STUDIO_ID, ARTIST_ID, OWNER_TOKEN, ARTIST_TOKEN, mockApiFallback, loginAs } from "./helpers";

// Real-browser coverage for the artist profile page (`/artists/:id`), which jsdom cannot give us:
// the page used to render its own sticky <header> + `min-h-screen` root inside a layout that
// already provides both, so scroll, stacking order and document height only misbehave under real
// CSS. Runs under BOTH layouts (owner viewing an artist, artist viewing their own profile).

const ARTIST = {
  id: ARTIST_ID,
  studioId: STUDIO_ID,
  firstName: "Rafaela",
  lastName: "Costa",
  email: "rafaela@tinta-alma.com",
  specializations: ["fineline"],
  hourlyRate: 80,
  isActive: true,
  avatarUrl: null,
  portfolioImages: [],
  slug: "rafaela-costa",
  userId: "user-id-artist",
  createdAt: "2024-01-15T10:00:00.000Z",
  updatedAt: "2024-06-01T10:00:00.000Z",
};

const ANY_LANDING = /^(?!.*\/login).+/;

async function mockArtist(page: Page) {
  await mockApiFallback(page);
  await page.route(`**/api/v1/artists/${ARTIST_ID}`, async (route) => {
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(ARTIST) });
  });
  await page.route("**/api/v1/artists/me", async (route) => {
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(ARTIST) });
  });
  await page.route(`**/api/v1/artists/${ARTIST_ID}/schedule`, async (route) => {
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ schedule: [], timeOff: [] }) });
  });
  await page.route(`**/api/v1/artists/${ARTIST_ID}/instagram/status`, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ isConnected: false, username: null, lastSyncedAt: null, postCount: 0 }),
    });
  });
}

async function openAsOwner(page: Page, search = "") {
  await mockArtist(page);
  await loginAs(page, OWNER_TOKEN, "owner@tinta-alma.com", ANY_LANDING);
  await page.goto(`/artists/${ARTIST_ID}${search}`);
  await expect(page.getByRole("heading", { level: 1, name: "Rafaela Costa" })).toBeVisible();
}

async function openAsArtist(page: Page, search = "") {
  await mockArtist(page);
  await loginAs(page, ARTIST_TOKEN, "rafaela@tinta-alma.com", ANY_LANDING);
  await page.goto(`/artists/${ARTIST_ID}${search}`);
  await expect(page.getByRole("heading", { level: 1, name: "Rafaela Costa" })).toBeVisible();
}

const LAYOUTS: ReadonlyArray<{ name: string; open: (page: Page, search?: string) => Promise<void> }> = [
  { name: "OwnerLayout (owner viewing an artist)", open: openAsOwner },
  { name: "ArtistLayout (artist on their own profile)", open: openAsArtist },
];

for (const layout of LAYOUTS) {
  test.describe(layout.name, () => {
    test("renders exactly one header and Edit is never hidden behind it", async ({ page }) => {
      await page.setViewportSize({ width: 1280, height: 420 });
      await layout.open(page);

      // The layout's header is the only <header>; the page no longer renders a nested one.
      await expect(page.locator("header")).toHaveCount(1);

      const edit = page.getByRole("button", { name: "Edit" });
      await expect(edit).toBeInViewport();
      await edit.click({ trial: true });

      // Scroll to the very bottom and back: Edit must be reachable, not pinned under the app header.
      await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
      await edit.scrollIntoViewIfNeeded();
      await edit.click({ trial: true });
    });

    test("short content does not create a phantom vertical scrollbar", async ({ page }) => {
      await page.setViewportSize({ width: 1280, height: 1000 });
      await layout.open(page);

      const overflow = await page.evaluate(
        () => document.documentElement.scrollHeight - document.documentElement.clientHeight,
      );
      expect(overflow).toBeLessThanOrEqual(1);
    });

    test("?tab=social deep link lands on Social and survives a refresh", async ({ page }) => {
      await layout.open(page, "?tab=social");
      await expect(page.getByRole("tab", { name: "Social" })).toHaveAttribute("aria-selected", "true");

      await page.reload();
      await expect(page.getByRole("heading", { level: 1, name: "Rafaela Costa" })).toBeVisible();
      await expect(page.getByRole("tab", { name: "Social" })).toHaveAttribute("aria-selected", "true");
    });

    test("at 375px every tab is reachable, the active tab is in view and the page content does not scroll sideways", async ({ page }) => {
      await page.setViewportSize({ width: 375, height: 800 });
      await layout.open(page);

      for (const name of ["Portfolio", "Availability", "Bookings", "Designs", "Social"]) {
        const tab = page.getByRole("tab", { name });
        await tab.scrollIntoViewIfNeeded();
        await tab.click();
        await expect(tab).toHaveAttribute("aria-selected", "true");
      }

      // Selected tab (Social, the last one) is fully inside the viewport once the strip scrolls it in.
      await expect
        .poll(async () => {
          const box = await page.getByRole("tab", { name: "Social" }).boundingBox();
          return box !== null && box.x >= 0 && box.x + box.width <= 375;
        })
        .toBe(true);

      // The page's own content never scrolls sideways — only the tab strip does, inside itself.
      // (Scoped to <main>: at 375px the shared layout header's icon cluster is itself ~50px too
      // wide in every role's layout, a pre-existing issue outside this page — see PR notes.)
      const mainSideways = await page.evaluate(() => {
        const main = document.querySelector("main");
        return main ? main.scrollWidth - main.clientWidth : 0;
      });
      expect(mainSideways).toBeLessThanOrEqual(0);
    });
  });
}
