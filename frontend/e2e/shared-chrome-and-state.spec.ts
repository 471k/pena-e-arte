import { test, expect, type Page } from "@playwright/test";
import { STUDIO_ID, ARTIST_ID, OWNER_TOKEN, mockApiFallback, loginAs } from "./helpers";

// Slice 6a, in a real browser, because jsdom can't see any of it: visible order and overflow (S08), whether a
// gated button's reason really appears on KEYBOARD focus (the contract's disabled-with-explanation), and what
// the live regions hold after a search (S06). What a screen reader then says is not something Chromium can
// show; the region's role, politeness and text are.

const json = (body: unknown) => ({ status: 200, contentType: "application/json", body: JSON.stringify(body) });

function studio(name: string) {
  return {
    id: STUDIO_ID, name, slug: "tinta-alma", city: "Tirana", latitude: 41.3275, longitude: 19.8187,
    showPlatformBranding: true, allowBrandingRemoval: false, isActive: true, slugLockedAt: null, isSolo: false,
    isPublished: true, timezone: "Europe/Tirane", countryCode: "AL", currency: "ALL",
    trialExpiresAt: new Date(Date.now() + 14 * 86_400_000).toISOString(), createdAt: new Date().toISOString(),
  };
}

const MY_ARTIST = {
  id: ARTIST_ID, studioId: STUDIO_ID, firstName: "Rafaela", lastName: "Costa", email: "owner@tinta-alma.com",
  specializations: [], hourlyRate: null, isActive: true, avatarUrl: null, portfolioImages: [], slug: null,
  userId: "user-id-owner", createdAt: "2024-01-15T10:00:00.000Z", updatedAt: "2024-06-01T10:00:00.000Z",
};

async function signInAsOwner(page: Page, opts: { width: number; studioName?: string; subscription?: string }) {
  await page.setViewportSize({ width: opts.width, height: 900 });
  await mockApiFallback(page);
  await page.route("**/api/v1/studios/me", (route) => route.fulfill(json(studio(opts.studioName ?? "Tinta & Alma"))));
  // A dual-role owner: the widest header there is (the Owner/Artist switch sits beside the studio's name).
  await page.route("**/api/v1/artists/me", (route) => route.fulfill(json(MY_ARTIST)));
  if (opts.subscription) {
    await page.route("**/api/v1/billing/subscription", (route) => route.fulfill(json({
      id: "sub-1", studioId: STUDIO_ID, planId: null, billingInterval: "Monthly", pendingPlanId: null,
      pendingBillingInterval: null, status: opts.subscription, trialExpiresAt: null,
      currentPeriodEnd: new Date().toISOString(), gracePeriodEnd: new Date().toISOString(),
      stripeSubscriptionId: null, cancelAtPeriodEnd: false,
    })));
  }
  await loginAs(page, OWNER_TOKEN, "owner@tinta-alma.com", /^(?!.*\/login).+/);
}

test.describe("S08: which studio this is", () => {
  test("from md up the header names the studio right after the brand", async ({ page }) => {
    await signInAsOwner(page, { width: 1280 });
    await page.goto("/dashboard");

    const header = page.locator("header").first();
    const name = header.getByText("Tinta & Alma", { exact: true });
    await expect(name).toBeVisible();
    // Brand master 1.0.0: the brand is one role="img" named TattooOS (the small lockup at this width), not text.
    const brand = await header.getByRole("img", { name: "TattooOS" }).boundingBox();
    const nameBox = await name.boundingBox();
    expect(brand && nameBox && nameBox.x > brand.x + brand.width).toBeTruthy();
  });

  test("a long studio name truncates but stays readable, and the header doesn't overflow at 768px", async ({ page }) => {
    await signInAsOwner(page, { width: 768, studioName: "The Extraordinarily Long Named Tattoo Collective of Tirana" });
    await page.goto("/dashboard");

    const header = page.locator("header").first();
    const name = header.getByText(/Extraordinarily Long/);
    await expect(name).toBeVisible();
    // Flexbox once squeezed it to zero width beside the Owner/Artist switch (at 640): it must keep real width.
    const box = await name.boundingBox();
    expect(box && box.width >= 90).toBeTruthy();
    const overflow = await header.evaluate((el) => el.scrollWidth - el.clientWidth);
    expect(overflow).toBeLessThanOrEqual(0);
    const menu = await page.getByRole("button", { name: "User menu" }).boundingBox();
    expect(menu && menu.x + menu.width <= 768).toBeTruthy();
  });

  for (const width of [390, 640]) {
  test(`at ${width}px the header has no room, so the drawer names the studio and the role`, async ({ page }) => {
    await signInAsOwner(page, { width });
    await page.goto("/dashboard");

    await expect(page.locator("header").first().getByText("Tinta & Alma", { exact: true })).toBeHidden();
    await page.getByRole("button", { name: /open navigation menu/i }).click();
    const drawer = page.getByRole("dialog");
    await expect(drawer).toContainText("Tinta & Alma · Owner");
    // It is the drawer's description, so it is read when the drawer opens.
    await expect(drawer).toHaveAccessibleDescription(/Tinta & Alma · Owner/);
  });
  }
});

test("disabled-with-explanation: Tab reaches a gated button and its reason shows on keyboard focus", async ({ page }) => {
  await signInAsOwner(page, { width: 1280, subscription: "PastDue" });
  await page.goto("/artists/new");

  const create = page.getByRole("button", { name: "Create Artist" });
  await expect(create).toHaveAttribute("aria-disabled", "true");
  const reason = page.getByRole("tooltip").filter({ hasText: /subscription is inactive/i });
  await expect(reason).toHaveCSS("opacity", "0");

  // Reach it by keyboard, not by .focus(): the point is that it is in the tab order at all.
  let reached = false;
  for (let i = 0; i < 40 && !reached; i++) {
    await page.keyboard.press("Tab");
    reached = await create.evaluate((el) => el === document.activeElement);
  }
  expect(reached).toBe(true);
  await expect(reason).toHaveCSS("opacity", "1");
  await expect(create).toHaveAccessibleDescription(/subscription is inactive/i);

  // And it refuses: pressing it sends nothing.
  let posted = false;
  await page.route("**/api/v1/artists", (route) => {
    if (route.request().method() === "POST") posted = true;
    return route.fulfill(json({}));
  });
  await page.keyboard.press("Enter");
  await page.waitForTimeout(300);
  expect(posted).toBe(false);
});

test("S06: a client search announces its result count once settled, and the first load says nothing", async ({ page }) => {
  const clients = [
    { id: "c1", firstName: "Ana", lastName: "Hoxha" },
    { id: "c2", firstName: "Besa", lastName: "Kola" },
    { id: "c3", firstName: "Dritan", lastName: "Leka" },
  ].map((c) => ({
    ...c, studioId: STUDIO_ID, email: `${c.id}@example.com`, phone: null, createdAt: "2026-01-01T00:00:00Z",
    userId: null, artistId: null, artistName: null, erasureRequestedAt: null, archivedAt: null,
  }));
  await signInAsOwner(page, { width: 1280 });
  await page.route(/\/api\/v1\/clients(\?.*)?$/, (route) => {
    const search = new URL(route.request().url()).searchParams.get("search")?.toLowerCase() ?? "";
    return route.fulfill(json(clients.filter((c) => `${c.firstName} ${c.lastName}`.toLowerCase().includes(search))));
  });
  await page.goto("/clients");

  // The table at this width (the mobile card copy is hidden from sm up).
  await expect(page.getByRole("table").getByText("Besa Kola")).toBeVisible();
  const region = page.locator("main [role=status][aria-live=polite]").first();
  await expect(region).toHaveText("");

  await page.getByPlaceholder(/search/i).fill("besa");
  await expect(region).toHaveText("1 client found.");

  await page.getByPlaceholder(/search/i).fill("zzz");
  await expect(region).toHaveText("No clients found.");
});
