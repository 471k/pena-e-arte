import { test, expect, type Page } from "@playwright/test";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const STUDIO_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const ARTIST_ID = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const CLIENT_ID = "cccccccc-cccc-cccc-cccc-cccccccccccc";
const APPT_ID   = "dddddddd-dddd-dddd-dddd-dddddddddddd";

// The decodeToken() utility reads the MS role claim key from the JWT payload.
const MS_ROLE_CLAIM = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

function makeJwt(role: string, sub: string, email: string): string {
  const header  = Buffer.from(JSON.stringify({ alg: "HS256", typ: "JWT" })).toString("base64");
  const payload = Buffer.from(
    JSON.stringify({
      sub,
      email,
      given_name: email.split("@")[0],
      tenant_id: STUDIO_ID,
      [MS_ROLE_CLAIM]: role,
      exp: Math.floor(Date.now() / 1000) + 3600,
    }),
  ).toString("base64");
  return `${header}.${payload}.fakesig`;
}

const OWNER_TOKEN  = makeJwt("owner",  "user-id-owner",  "owner@tinta-alma.com");
const CLIENT_TOKEN = makeJwt("client", "user-id-client", "ana@example.com");

// ---------------------------------------------------------------------------
// Mock helpers
// ---------------------------------------------------------------------------

async function mockAuthLogin(page: Page, token: string) {
  await page.route("**/api/v1/auth/login", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ accessToken: token, tokenType: "Bearer" }),
    });
  });
}

async function mockStudioRegistration(page: Page) {
  await page.route("**/api/v1/studios", async (route) => {
    if (route.request().method() === "POST") {
      await route.fulfill({
        status: 201,
        contentType: "application/json",
        body: JSON.stringify({
          id:                   STUDIO_ID,
          name:                 "Tinta & Alma",
          slug:                 "tinta-alma",
          city:                 "Porto",
          latitude:             41.1579,
          longitude:            -8.6291,
          showPlatformBranding: true,
          allowBrandingRemoval: false,
          trialExpiresAt:       new Date(Date.now() + 14 * 86_400_000).toISOString(),
          createdAt:            new Date().toISOString(),
          isActive:             true,
          slugLockedAt:         null,
        }),
      });
    } else {
      await route.continue();
    }
  });

  await page.route("**/api/v1/auth/register", async (route) => {
    await route.fulfill({ status: 200, contentType: "application/json", body: "{}" });
  });

  // Nominatim reverse geocoding (triggered by clicking the LocationPicker map)
  await page.route("https://nominatim.openstreetmap.org/**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        address: { city: "Porto", country: "Portugal" },
      }),
    });
  });
}

async function mockStudioMe(page: Page) {
  await page.route("**/api/v1/studios/me", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        id:                   STUDIO_ID,
        name:                 "Tinta & Alma",
        slug:                 "tinta-alma",
        city:                 "Porto",
        latitude:             41.1579,
        longitude:            -8.6291,
        showPlatformBranding: true,
        allowBrandingRemoval: false,
        trialExpiresAt:       new Date(Date.now() + 14 * 86_400_000).toISOString(),
        createdAt:            new Date().toISOString(),
        isActive:             true,
        slugLockedAt:         null,
      }),
    });
  });
}

async function mockBookingApis(page: Page) {
  // Artists list
  await page.route("**/api/v1/artists*", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify([
        { id: ARTIST_ID, firstName: "Rafaela", lastName: "Costa", email: "rafaela@tinta-alma.com" },
      ]),
    });
  });

  // Client's own profile (used to set clientId for client role)
  await page.route("**/api/v1/clients/me", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ id: CLIENT_ID, firstName: "Ana", lastName: "Silva", email: "ana@example.com" }),
    });
  });

  // Deposit rules — empty so the picker is hidden
  await page.route("**/api/v1/deposit-rules*", async (route) => {
    await route.fulfill({ status: 200, contentType: "application/json", body: "[]" });
  });

  // Client's own appointments list (MyBookingsSection)
  await page.route("**/api/v1/appointments/mine*", async (route) => {
    await route.fulfill({ status: 200, contentType: "application/json", body: "[]" });
  });

  // Create appointment (POST) — depositAmount: 0 skips the deposit step
  await page.route("**/api/v1/appointments", async (route) => {
    if (route.request().method() === "POST") {
      await route.fulfill({
        status: 201,
        contentType: "application/json",
        body: JSON.stringify({
          id:              APPT_ID,
          studioId:        STUDIO_ID,
          artistId:        ARTIST_ID,
          clientId:        CLIENT_ID,
          date:            new Date(Date.now() + 7 * 86_400_000).toISOString(),
          endDate:         new Date(Date.now() + 7 * 86_400_000 + 7_200_000).toISOString(),
          durationMinutes: 120,
          status:          "Pending",
          depositStatus:   "Pending",
          depositAmount:   0,
          notes:           null,
          createdAt:       new Date().toISOString(),
        }),
      });
    } else {
      await route.continue();
    }
  });
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

test.describe("Critical path — register, login, create appointment", () => {

  test("owner registers a studio successfully", async ({ page }) => {
    await mockStudioRegistration(page);
    await mockAuthLogin(page, OWNER_TOKEN);

    await page.goto("/register");

    // ── Step 1: Studio details ─────────────────────────────────────────────
    // The Label text is "Studio name" (htmlFor="name")
    await page.getByLabel("Studio name").fill("Tinta & Alma");
    // Slug auto-fills from name; leave as-is

    // NIPT (business tax ID) is required as of the 2026-07-22 NIPT feature —
    // format is a letter, 8 digits, then a letter (see UpdateStudioNiptValidator).
    await page.getByLabel("Business tax ID (NIPT)").fill("L01234567A");

    // LocationPicker renders a Leaflet map. Click the map centre to place a pin.
    // The reverse-geocode call is mocked to return "Porto, Portugal".
    const mapContainer = page.locator(".leaflet-container");
    await mapContainer.waitFor({ state: "visible", timeout: 15_000 });
    await mapContainer.click();

    // Wait for the location label to appear (geocoding mock resolves immediately)
    await expect(page.getByText("Porto")).toBeVisible({ timeout: 5_000 });

    await page.getByRole("button", { name: "Next" }).click();

    // ── Step 2: Owner account ──────────────────────────────────────────────
    // Label "Email" (htmlFor="email"), "Password" (htmlFor="password"),
    // "Confirm password" (htmlFor="confirmPassword")
    await page.getByLabel("Email").fill("owner@tinta-alma.com");
    await page.getByLabel("Password", { exact: true }).fill("Password123!");
    await page.getByLabel("Confirm password", { exact: true }).fill("Password123!");

    await page.getByRole("button", { name: "Register" }).click();

    // After registration the app dispatches setCredentials and navigates to /dashboard
    await expect(page).toHaveURL(/\/dashboard/i, { timeout: 10_000 });
  });

  test("owner logs in successfully", async ({ page }) => {
    await mockAuthLogin(page, OWNER_TOKEN);

    await page.goto("/login");

    // Label "Email" (htmlFor="email"); "Password" PasswordInput (htmlFor="password")
    await page.getByLabel("Email").fill("owner@tinta-alma.com");
    await page.getByLabel("Password", { exact: true }).fill("Password123!");

    // Button text is "Sign in"
    await page.getByRole("button", { name: "Sign in" }).click();

    // Owner role → redirected to /dashboard
    await expect(page).toHaveURL(/\/dashboard/i, { timeout: 10_000 });
  });

  test("client creates an appointment", async ({ page }) => {
    // Use client token — /book is guarded by Role.Client and Role.Issuer.
    // Owner role redirects to /dashboard, which has no booking form.
    await mockAuthLogin(page, CLIENT_TOKEN);
    await mockStudioMe(page);
    await mockBookingApis(page);

    // Log in as client; redirect lands on /book
    await page.goto("/login");
    await page.getByLabel("Email").fill("ana@example.com");
    await page.getByLabel("Password", { exact: true }).fill("Password123!");
    await page.getByRole("button", { name: "Sign in" }).click();

    await expect(page).toHaveURL(/\/book/i, { timeout: 10_000 });

    // ── Fill the booking form ──────────────────────────────────────────────
    // Artist picker: Radix Select with id="artistId", Label "Artist"
    await page.getByLabel("Artist").click();
    await page.getByRole("option", { name: "Rafaela Costa" })
      .waitFor({ state: "visible", timeout: 5_000 });
    await page.getByRole("option", { name: "Rafaela Costa" }).click();

    // Date & Time: datetime-local input, Label "Date & Time" (htmlFor="scheduledAt")
    // Build a local datetime string — toISOString() is UTC, which causes past-date
    // validation failures in timezones behind UTC.
    const future = new Date(Date.now() + 7 * 86_400_000);
    const pad = (n: number) => String(n).padStart(2, "0");
    const dateStr =
      `${future.getFullYear()}-${pad(future.getMonth() + 1)}-${pad(future.getDate())}` +
      `T${pad(future.getHours())}:${pad(future.getMinutes())}`;
    await page.getByLabel("Date & Time").fill(dateStr);

    // Duration: Radix Select (no longer a free-text number input), id="durationMinutes",
    // Label "Appointment Duration". 120 minutes is the "2 hours" option.
    await page.getByLabel("Appointment Duration").click();
    await page.getByRole("option", { name: "2 hours" })
      .waitFor({ state: "visible", timeout: 5_000 });
    await page.getByRole("option", { name: "2 hours" }).click();

    // Submit — button text is "Request Appointment"
    await page.getByRole("button", { name: "Request Appointment" }).click();

    // On success the form renders: <p className="text-sm font-medium">Appointment requested!</p>
    await expect(page.getByText("Appointment requested!")).toBeVisible({ timeout: 10_000 });
  });

});
