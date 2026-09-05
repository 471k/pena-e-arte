import type { Page } from "@playwright/test";

// Shared JWT/mock helpers extracted from critical-path.spec.ts and
// my-studios-kebab-menu.spec.ts so new specs (accessibility.spec.ts, etc.) don't
// re-derive the same fake-token plumbing a third time.

export const STUDIO_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
export const ARTIST_ID = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
export const CLIENT_ID = "cccccccc-cccc-cccc-cccc-cccccccccccc";
export const APPT_ID   = "dddddddd-dddd-dddd-dddd-dddddddddddd";
export const PAYMENT_ID = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";

// The decodeToken() utility reads the MS role claim key from the JWT payload.
export const MS_ROLE_CLAIM = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

export function makeJwt(role: string, sub: string, email: string, tenantId: string = STUDIO_ID): string {
  const header  = Buffer.from(JSON.stringify({ alg: "HS256", typ: "JWT" })).toString("base64");
  const payload = Buffer.from(
    JSON.stringify({
      sub,
      email,
      given_name: email.split("@")[0],
      tenant_id: tenantId,
      [MS_ROLE_CLAIM]: role,
      exp: Math.floor(Date.now() / 1000) + 3600,
    }),
  ).toString("base64");
  return `${header}.${payload}.fakesig`;
}

export const OWNER_TOKEN  = makeJwt("owner",  "user-id-owner",  "owner@tinta-alma.com");
export const CLIENT_TOKEN = makeJwt("client", "user-id-client", "ana@example.com");
export const ARTIST_TOKEN = makeJwt("artist", "user-id-artist", "rafaela@tinta-alma.com");

export async function mockAuthLogin(page: Page, token: string) {
  await page.route("**/api/v1/auth/login", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ accessToken: token, tokenType: "Bearer" }),
    });
  });
}

/**
 * Registers a catch-all fallback for every /api/v1/* call before any page-specific
 * mocks. Playwright runs the most-recently-registered matching route first, so
 * routes registered after this one (the specific per-page mocks) take priority —
 * this only answers whatever a page/layout calls that a test didn't bother mocking
 * explicitly (nav badge counts, secondary widgets, etc.), so pages render into a
 * real, non-loading state instead of hanging on an unmocked fetch.
 *
 * GETs resolve to an empty array — safe for both list endpoints (renders an empty
 * list) and single-object endpoints (optional-chaining reads come back `undefined`,
 * which every component here already treats as "not loaded" rather than crashing).
 * Everything else resolves to an empty object so a mutation call doesn't hang.
 */
export async function mockApiFallback(page: Page) {
  await page.route("**/api/v1/**", async (route) => {
    const method = route.request().method();
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: method === "GET" ? "[]" : "{}",
    });
  });
  // Registered after the catch-all above so it wins (Playwright runs the most-recently
  // registered matching route first). Without this, the generic `[]` fallback reads as
  // a truthy-but-property-less object to useOnboardingTour's `!status.hasCompletedTour`
  // check, which evaluates to "not completed" and auto-launches the full-screen
  // OnboardingTour overlay — its `fixed inset-0 z-[1200]` backdrop then blocks every
  // subsequent click in the test, which is what happened here before this fix.
  await page.route("**/api/v1/onboarding/tour-status*", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ hasCompletedTour: true }),
    });
  });
}

export async function loginAs(page: Page, token: string, email: string, landingUrlPattern: RegExp) {
  const { expect } = await import("@playwright/test");
  await mockAuthLogin(page, token);
  await page.goto("/login");
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password", { exact: true }).fill("Password123!");
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page).toHaveURL(landingUrlPattern, { timeout: 10_000 });
}
