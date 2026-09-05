import { test, expect, type Page } from "@playwright/test";
import { makeJwt, mockAuthLogin } from "./helpers";

// Regression coverage for a real-browser bug that vitest/jsdom cannot catch:
// jsdom never applies actual CSS `pointer-events`, so a stuck
// `body { pointer-events: none }` left behind by a modal Radix DropdownMenu
// (after opening the Sheet-based "Manage notifications" panel from one of its
// items) only showed up when driven by a real Chromium engine.

const CLIENT_TOKEN = makeJwt("client", "user-id-client", "ana@example.com", "studio-aaa");

async function mockMyStudios(page: Page) {
  await page.route("**/api/v1/auth/my-studios", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify([
        { studioId: "studio-aaa", name: "Alpha Ink", slug: "alpha-ink", city: "Tirana", coverImageUrl: null, isStudioActive: true },
        { studioId: "studio-bbb", name: "Beta Art",  slug: "beta-art",  city: "Durrës", coverImageUrl: null, isStudioActive: true },
      ]),
    });
  });
  await page.route("**/api/v1/auth/my-studios/*/notification-preferences", async (route) => {
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ preferences: [] }) });
  });
}

async function loginAndGoToMyStudios(page: Page) {
  await mockAuthLogin(page, CLIENT_TOKEN);
  await mockMyStudios(page);
  await page.goto("/login");
  await page.getByLabel("Email").fill("ana@example.com");
  await page.getByLabel("Password", { exact: true }).fill("Password123!");
  await page.getByRole("button", { name: "Sign in" }).click();
  await page.goto("/my-studios");
  await expect(page.getByText("Alpha Ink")).toBeVisible();
}

async function assertPageStillClickable(page: Page) {
  const bodyPointerEvents = await page.evaluate(() => document.body.style.pointerEvents);
  expect(bodyPointerEvents).not.toBe("none");
  const kebab = page.getByRole("button", { name: /more options for alpha ink/i });
  await kebab.click({ timeout: 3000 });
  await expect(page.getByRole("menuitem", { name: /view public profile/i })).toBeVisible({ timeout: 3000 });
}

test("kebab -> Leave studio -> Cancel -> kebab still clickable", async ({ page }) => {
  await loginAndGoToMyStudios(page);
  await page.getByRole("button", { name: /more options for alpha ink/i }).click();
  await page.getByRole("menuitem", { name: /leave studio/i }).click();
  await expect(page.getByRole("alertdialog")).toBeVisible();
  await page.getByRole("button", { name: "Cancel" }).click();
  await expect(page.getByRole("alertdialog")).toBeHidden();
  await assertPageStillClickable(page);
});

test("kebab -> Manage notifications -> close via Escape -> kebab still clickable", async ({ page }) => {
  await loginAndGoToMyStudios(page);
  await page.getByRole("button", { name: /more options for alpha ink/i }).click();
  await page.getByRole("menuitem", { name: /manage notifications/i }).click();
  await expect(page.getByRole("dialog")).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toBeHidden();
  await assertPageStillClickable(page);
});

test("kebab -> Manage notifications -> close via X button -> kebab still clickable", async ({ page }) => {
  await loginAndGoToMyStudios(page);
  await page.getByRole("button", { name: /more options for alpha ink/i }).click();
  await page.getByRole("menuitem", { name: /manage notifications/i }).click();
  await expect(page.getByRole("dialog")).toBeVisible();
  await page.getByRole("button", { name: /close/i }).click();
  await expect(page.getByRole("dialog")).toBeHidden();
  await assertPageStillClickable(page);
});
