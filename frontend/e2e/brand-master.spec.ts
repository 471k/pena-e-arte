import { test, expect, type Page } from "@playwright/test";
import { STUDIO_ID, ARTIST_ID, OWNER_TOKEN, CLIENT_TOKEN, mockApiFallback, loginAs } from "./helpers";

// Brand master 1.0.0 (27 Nov 2026). Where the small lockup gives way to the micro mark is MEASURED per header, so
// the controls keep their room first (TattooOSLogo.css, BrandMark's fullFrom). jsdom has no layout; these pin the
// swap points in a real browser, with the widest content each header shows, and that nothing scrolls sideways.

const json = (b: unknown) => ({ status: 200, contentType: "application/json", body: JSON.stringify(b) });
const ARTIST = {
  id: ARTIST_ID, studioId: STUDIO_ID, firstName: "Rafaela", lastName: "Costa", email: "rafaela@tinta-alma.com",
  specializations: [], hourlyRate: null, isActive: true, avatarUrl: null, portfolioImages: [], slug: null,
  userId: "user-id-artist", createdAt: "2024-01-15T10:00:00.000Z", updatedAt: "2024-06-01T10:00:00.000Z",
};
const STUDIO_ME = { id: STUDIO_ID, name: "Tinta & Alma Studio", slug: "tinta-alma", timeZone: "Europe/Tirane", currency: "EUR" };
const PUBLIC_STUDIO = {
  studioId: "studio-001", name: "Tinta & Alma Tattoo Collective", slug: "tinta-alma", city: "Tirana", description: "Five artists.",
  coverImageUrl: null, artists: [], showPlatformBranding: true, socialLinks: [], hours: [], galleryImages: [], showBookingCta: true,
  currency: "EUR", timezone: "Europe/Tirane",
};

/** File names of the brand images actually showing, left to right. */
const shownBrand = (page: Page, scope: string) => page.evaluate((sel) =>
  Array.from(document.querySelectorAll(`${sel} img[src*="/brand/after-hours/"]`))
    .filter((i) => i.getBoundingClientRect().width > 0)
    .map((i) => i.getAttribute("src")!.split("/").pop()!.replace(/-on-(light|dark)\.svg$/, "")), scope);
const sideways = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);

async function signIn(page: Page, role: "owner" | "client") {
  await mockApiFallback(page);
  await page.route("**/api/v1/studios/me", (r) => r.fulfill(json(STUDIO_ME)));
  if (role === "owner") {
    await page.route(`**/api/v1/artists/${ARTIST_ID}`, (r) => r.fulfill(json(ARTIST)));
    await page.route("**/api/v1/artists/me", (r) => r.fulfill(json(ARTIST)));
    await page.route(`**/api/v1/artists/${ARTIST_ID}/schedule`, (r) => r.fulfill(json({ schedule: [], timeOff: [] })));
    await loginAs(page, OWNER_TOKEN, "owner@tinta-alma.com", /^(?!.*\/login).+/);
    await page.goto(`/artists/${ARTIST_ID}`);
  } else {
    await loginAs(page, CLIENT_TOKEN, "ana@example.com", /\/my-appointments/i);
  }
}

const ROLE_CASES = [
  // The owner header needs 413px below 640 and 706px from 640 with the small lockup (switch + studio name).
  { role: "owner", width: 390, brand: ["cartridge-micro"] },
  { role: "owner", width: 700, brand: ["cartridge-micro"] },
  { role: "owner", width: 768, brand: ["small"] },
  // The client header needs 377px.
  { role: "client", width: 360, brand: ["cartridge-micro"] },
  { role: "client", width: 390, brand: ["small"] },
] as const;

for (const c of ROLE_CASES) {
  test(`${c.role} header at ${c.width}px: ${c.brand[0]}, brand named once, no fold, no sideways scroll`, async ({ page }) => {
    await page.setViewportSize({ width: c.width, height: 800 });
    await signIn(page, c.role);
    const header = page.locator("header[data-pinned-shell]");
    await expect(header.getByRole("img", { name: "TattooOS" })).toHaveCount(1);
    await expect.poll(() => shownBrand(page, "header[data-pinned-shell]")).toEqual([...c.brand]);
    // The small lockup never costs the header its inline controls.
    await expect(header.getByRole("button", { name: "Open help menu" })).toBeVisible();
    expect(await header.evaluate((h) => Math.round(h.getBoundingClientRect().height))).toBe(56);
    expect(await sideways(page)).toBeLessThanOrEqual(0);
  });
}

const PUBLIC_CASES = [
  // Signed out. Discover's nav needs 386px beside the small lockup, the portfolio header's 410px.
  { path: "/discover", width: 360, brand: ["cartridge-micro"] },
  { path: "/discover", width: 390, brand: ["small"] },
  { path: "/s/tinta-alma", width: 390, brand: ["cartridge-micro"] },
  { path: "/s/tinta-alma", width: 420, brand: ["small"] },
  { path: "/login", width: 320, brand: ["cartridge-micro"] },
  { path: "/login", width: 360, brand: ["small"] },
] as const;

for (const c of PUBLIC_CASES) {
  test(`${c.path} header at ${c.width}px: ${c.brand[0]} inside a 44px brand link, no sideways scroll`, async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("cookie-consent", "accepted"));
    await page.setViewportSize({ width: c.width, height: 800 });
    await mockApiFallback(page);
    await page.route("**/api/v1/public/studios/tinta-alma", (r) => r.fulfill(json(PUBLIC_STUDIO)));
    await page.goto(c.path);
    const link = page.getByRole("link", { name: "TattooOS — Discover studios" });
    await expect(link).toBeVisible();
    await expect.poll(() => shownBrand(page, 'a[aria-label="TattooOS — Discover studios"]')).toEqual([...c.brand]);
    expect(Math.round((await link.boundingBox())!.height)).toBeGreaterThanOrEqual(44);
    await expect.poll(() => sideways(page)).toBeLessThanOrEqual(0);
  });
}

test("embed: the Powered by TattooOS badge is a 124px logo inside a 44px link", async ({ page }) => {
  await mockApiFallback(page);
  await page.route("**/api/v1/public/studios/tinta-alma", (r) => r.fulfill(json(PUBLIC_STUDIO)));
  await page.goto("/embed/tinta-alma");
  const link = page.getByRole("link", { name: "Powered by TattooOS" });
  await expect(link).toBeVisible();
  expect(Math.round((await link.boundingBox())!.height)).toBeGreaterThanOrEqual(44);
  const img = link.locator("img");
  await expect(img).toHaveAttribute("alt", "");
  expect(Math.round((await img.boundingBox())!.width)).toBe(124);
});
