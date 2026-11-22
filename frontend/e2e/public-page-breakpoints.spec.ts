import { test, expect, type Page } from "@playwright/test";
import { mockApiFallback } from "./helpers";

// Slice 6e moved the public product pages (artist portfolio, studio portfolio, Discover, the feed) onto the
// package's page breakpoints, so a client sees the same layout on an artist's work signed in or out (6c had
// moved the signed-in pages). These pin the two bands that visibly changed and run the overflow pass.
//
// The artist page's fixed "Book with ..." bar, the padding that clears it and the side column holding the
// booking button change TOGETHER at 1100: the bar shows exactly while that column is stacked. Measured from
// computed styles (grid tracks, column count), not positions, so placeholder images can't move the answer.
// Like page-breakpoints.spec.ts, the pairs sit ON the boundary with zero margin on purpose; a sudden failure
// with nothing relevant in the diff is most likely a classic vertical scrollbar, not a layout regression.

const PNG = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=", "base64");
const json = (b: unknown) => ({ status: 200, contentType: "application/json", body: JSON.stringify(b) });
const IMG = (i: number) => ({ imageId: `img-${i}`, imageUrl: `https://cdn.example.com/p${i}.jpg`, style: i % 2 ? "blackwork" : "fine-line", category: null });
const ARTIST = {
  artistId: "artist-001", name: "Maria Silva Hoxhaj", slug: "maria-silva", bio: "Neo-traditional and blackwork, mostly large colour pieces.",
  profileImageUrl: null, portfolioImages: Array.from({ length: 9 }, (_, i) => IMG(i)), specializations: ["blackwork", "neo-traditional"],
  hourlyRate: 120, averageRating: 4.5, reviewCount: 8, studioName: "Tinta & Alma Tattoo Collective", studioSlug: "tinta-alma",
  showBookingCta: true, isOwnProfile: false, socialLinks: [], currency: "EUR",
};
const STUDIO = {
  studioId: "studio-001", name: "Tinta & Alma Tattoo Collective", slug: "tinta-alma", city: "Tirana", latitude: 41.33, longitude: 19.82,
  description: "A collective of five artists in central Tirana.", coverImageUrl: null, phoneNumber: "+355 69 123 4567",
  averageRating: 4.7, reviewCount: 12, galleryImages: Array.from({ length: 6 }, (_, i) => `https://cdn.example.com/g${i}.jpg`),
  artists: Array.from({ length: 4 }, (_, i) => ({
    artistId: `artist-00${i}`, name: `Anastasia Kolonjari ${i}`, slug: `artist-${i}`, bio: "Fine line and lettering.",
    profileImageUrl: null, specializations: ["fine-line"], averageRating: 4.9, reviewCount: 8,
  })),
  showBookingCta: true, socialLinks: [], hours: [{ dayOfWeek: 1, startTime: "09:00:00", endTime: "18:00:00", isOpen: true }],
  timezone: "Europe/Tirane", showPlatformBranding: true, addressLine1: "Rruga Myslym Shyri 42", addressLine2: null, postalCode: "1001",
  currency: "EUR", countryCode: "AL",
};
const FEED = Array.from({ length: 9 }, (_, i) => ({
  ...IMG(i), artistName: "Maria Silva Hoxhaj", artistSlug: "maria-silva", studioName: "Tinta & Alma Tattoo Collective", studioSlug: "tinta-alma",
  averageRating: 4.5, reviewCount: 8, imageAverageRating: null, imageReviewCount: 0, distanceKm: 1.2, viewCount: 40,
}));
const NEARBY = Array.from({ length: 6 }, (_, i) => ({
  studioId: `s-${i}`, name: `Tinta & Alma Tattoo Collective ${i}`, slug: `s-${i}`, city: "Tirana", coverImageUrl: null,
  distanceKm: 1.5 + i, artistCount: 5, averageRating: 4.6, reviewCount: 20,
}));

test.use({ geolocation: { latitude: 41.33, longitude: 19.82 }, permissions: ["geolocation"] });

async function mocks(page: Page) {
  await page.addInitScript(() => localStorage.setItem("cookie-consent", "accepted"));
  await mockApiFallback(page);
  // The fixtures' images are served locally (a 1x1 PNG): an unresolvable host made each load wait on DNS.
  await page.route("https://cdn.example.com/**", (r) => r.fulfill({ status: 200, contentType: "image/png", body: PNG }));
  await page.route("**/api/v1/public/artists/maria-silva", (r) => r.fulfill(json(ARTIST)));
  await page.route("**/api/v1/public/artists/maria-silva/reviews", (r) => r.fulfill(json({ reviews: [], averageRating: 4.5, reviewCount: 8 })));
  await page.route("**/api/v1/public/studios/tinta-alma", (r) => r.fulfill(json(STUDIO)));
  await page.route("**/api/v1/public/studios/tinta-alma/reviews", (r) => r.fulfill(json({ reviews: [], averageRating: 4.7, reviewCount: 12 })));
  await page.route("**/api/v1/public/portfolio/feed*", (r) => r.fulfill(json(FEED)));
  await page.route("**/api/v1/public/studios/nearby*", (r) => r.fulfill(json(NEARBY)));
}

const overflow = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
const tracks = (page: Page, selector: string) =>
  page.locator(selector).first().evaluate((el) => getComputedStyle(el).gridTemplateColumns.split(" ").filter(Boolean).length);

test("artist page: the side column, the quick-book bar and its padding switch together at 1100", async ({ page }) => {
  await mocks(page);
  for (const [width, beside] of [[1099, false], [1100, true]] as const) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/artist/maria-silva");
    await expect(page.getByRole("heading", { name: ARTIST.name })).toBeVisible();
    const layout = page.locator("aside").first().locator("..");
    expect(await layout.evaluate((el) => getComputedStyle(el).gridTemplateColumns.split(" ").length), `two columns at ${width}`).toBe(beside ? 2 : 1);
    const bar = page.getByLabel("Quick book bar");
    if (beside) await expect(bar).toBeHidden(); else await expect(bar).toBeVisible();
    // The padding clears the bar exactly while the bar shows (pb-20 = 80px, page-lg:pb-8 = 32px).
    const pad = await page.locator("div.pb-20").first().evaluate((el) => getComputedStyle(el).paddingBottom);
    expect(pad, `padding at ${width}`).toBe(beside ? "32px" : "80px");
    expect(await overflow(page)).toBeLessThanOrEqual(0);
  }
});

test("artist page: the portfolio is two columns below 800 and three from 800, the same as signed in", async ({ page }) => {
  await mocks(page);
  for (const [width, cols] of [[799, "2"], [800, "3"]] as const) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/artist/maria-silva");
    await expect(page.getByRole("heading", { name: ARTIST.name })).toBeVisible();
    const masonry = page.locator('[class*="columns-2"]').first();
    expect(await masonry.evaluate((el) => getComputedStyle(el).columnCount), `columns at ${width}`).toBe(cols);
  }
});

test("Discover: studio cards are one column below 800, two from 800, three from 1100", async ({ page }) => {
  await mocks(page);
  for (const [width, n] of [[799, 1], [800, 2], [1099, 2], [1100, 3]] as const) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/discover");
    await page.getByRole("tab", { name: /studios/i }).click();
    await expect(page.getByText(NEARBY[0].name, { exact: true })).toBeVisible();
    expect(await tracks(page, '[class*="page-md:grid-cols-2"]'), `tracks at ${width}`).toBe(n);
  }
});

const ROUTES = ["/artist/maria-silva", "/s/tinta-alma", "/discover"] as const;
const WIDTHS = [320, 390, 420, 768, 799, 800, 1024, 1099, 1100, 1440] as const;

// Each route loads once and is then resized through the ten widths (media queries re-evaluate on resize, as in
// page-breakpoints.spec.ts). Thirty full loads in a row made the Windows dev server stall, leaving a white page.
test("public product pages: no sideways scroll at ten widths", async ({ page }) => {
  test.setTimeout(180_000);
  const errors: string[] = [];
  page.on("pageerror", (e) => errors.push(e.message));
  await mocks(page);
  for (const route of ROUTES) {
    await page.setViewportSize({ width: WIDTHS[0], height: 900 });
    await page.goto(route);
    await expect(page.getByRole("heading").first(), `${route} rendered nothing; page errors: ${errors.join(" | ")}`).toBeVisible({ timeout: 15_000 });
    await page.waitForLoadState("networkidle");
    for (const width of WIDTHS) {
      await page.setViewportSize({ width, height: 900 });
      await page.evaluate(() => new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r))));
      expect(await overflow(page), `${route} at ${width}`).toBeLessThanOrEqual(0);
    }
  }
  expect(errors, "page errors").toEqual([]);
});
