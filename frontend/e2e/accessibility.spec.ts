import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import {
  STUDIO_ID, ARTIST_ID, CLIENT_ID, APPT_ID, PAYMENT_ID,
  CLIENT_TOKEN, OWNER_TOKEN,
  mockAuthLogin, mockApiFallback, loginAs,
} from "./helpers";

// WCAG 2.1 AA automated coverage (CLAUDE.md non-negotiable rule 6 — matches current
// industry standard for a booking SaaS) on the highest-traffic, highest-risk pages:
// public booking, sign-in/sign-up, client + owner "home" screens, and the deposit
// payment page. See docs/claude/accessibility-audit-2026-09-05.md for what this
// found and fixed the first time it ran, and CI's "Frontend" job for where this is
// gated (a real regression here now fails the build, not just a manual audit).

const WCAG_TAGS = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"];

function violationSummary(violations: { id: string; help: string; nodes: unknown[] }[]): string {
  if (violations.length === 0) return "";
  return violations
    .map((v) => `- [${v.id}] ${v.help} (${v.nodes.length} node${v.nodes.length === 1 ? "" : "s"})`)
    .join("\n");
}

async function assertNoViolations(builder: AxeBuilder) {
  const results = await builder.withTags(WCAG_TAGS).analyze();
  expect(results.violations, violationSummary(results.violations)).toEqual([]);
}

test.describe("Accessibility (WCAG 2.1 AA) — critical surfaces", () => {

  test("sign-in page has no violations", async ({ page }) => {
    await page.goto("/login");
    await assertNoViolations(new AxeBuilder({ page }));
  });

  test("studio sign-up page has no violations", async ({ page }) => {
    await page.goto("/register");
    // Leaflet's own generated map markup is third-party and out of this project's
    // control — excluded so this gate only ever flags application code.
    await assertNoViolations(new AxeBuilder({ page }).exclude(".leaflet-container"));
  });

  test("public guest booking flow has no violations", async ({ page }) => {
    await page.route("**/api/v1/public/studios/*/booking/artists", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([
          { id: ARTIST_ID, firstName: "Rafaela", lastName: "Costa", specializations: "Fine line", hourlyRate: 80, avatarUrl: null },
        ]),
      });
    });
    await page.route("**/api/v1/public/studios/*/booking/deposit-rule", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ name: "Standard deposit", amountFixed: 50, amountPercent: null }),
      });
    });

    await page.goto("/book?studio=tinta-alma");
    await expect(page.getByRole("heading", { name: "Book an appointment" })).toBeVisible();
    await assertNoViolations(new AxeBuilder({ page }));
  });

  test("client home (My Studios) has no violations", async ({ page }) => {
    await mockApiFallback(page);
    await mockAuthLogin(page, CLIENT_TOKEN);
    await page.route("**/api/v1/auth/my-studios", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([
          { studioId: STUDIO_ID, name: "Tinta & Alma", slug: "tinta-alma", city: "Porto", coverImageUrl: null, isStudioActive: true },
        ]),
      });
    });

    await loginAs(page, CLIENT_TOKEN, "ana@example.com", /\/book/i);
    await page.goto("/my-studios");
    await expect(page.getByText("Tinta & Alma")).toBeVisible();
    await assertNoViolations(new AxeBuilder({ page }));
  });

  test("owner dashboard has no violations", async ({ page }) => {
    await mockApiFallback(page);
    await mockAuthLogin(page, OWNER_TOKEN);
    await page.route("**/api/v1/billing/subscription", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          status: "Trialing",
          trialExpiresAt: new Date(Date.now() + 5 * 86_400_000).toISOString(),
          currentPeriodEnd: new Date(Date.now() + 5 * 86_400_000).toISOString(),
          gracePeriodEnd: null,
        }),
      });
    });
    await page.route("**/api/v1/appointments*", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([
          {
            id: APPT_ID, artistId: ARTIST_ID, clientId: CLIENT_ID, status: "Confirmed",
            depositStatus: "Paid", date: new Date().toISOString(),
            endDate: new Date(Date.now() + 3_600_000).toISOString(), durationMinutes: 60,
          },
        ]),
      });
    });
    await page.route("**/api/v1/artists*", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([
          { id: ARTIST_ID, firstName: "Rafaela", lastName: "Costa", email: "rafaela@tinta-alma.com" },
        ]),
      });
    });
    await page.route("**/api/v1/payments*", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([
          { id: PAYMENT_ID, clientName: "Ana Silva", amount: 50, status: "CashPending" },
        ]),
      });
    });

    await loginAs(page, OWNER_TOKEN, "owner@tinta-alma.com", /\/dashboard/i);
    await expect(page.getByText("Awaiting Cash")).toBeVisible();
    await assertNoViolations(new AxeBuilder({ page }));
  });

  test("deposit payment page (not-found state) has no violations", async ({ page }) => {
    await mockApiFallback(page);
    await mockAuthLogin(page, CLIENT_TOKEN);
    await page.route(`**/api/v1/payments/${PAYMENT_ID}/client-secret`, async (route) => {
      await route.fulfill({ status: 404, contentType: "application/json", body: "{}" });
    });

    await loginAs(page, CLIENT_TOKEN, "ana@example.com", /\/book/i);
    await page.goto(`/pay/${PAYMENT_ID}`);
    await expect(page.getByText("Payment not found or you don't have access to it.")).toBeVisible();
    await assertNoViolations(new AxeBuilder({ page }));
  });

  test("in-flow deposit payment method selector has no violations", async ({ page }) => {
    await mockApiFallback(page);
    await mockAuthLogin(page, CLIENT_TOKEN);
    await page.route("**/api/v1/artists*", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([
          { id: ARTIST_ID, firstName: "Rafaela", lastName: "Costa", email: "rafaela@tinta-alma.com" },
        ]),
      });
    });
    await page.route("**/api/v1/clients/me", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ id: CLIENT_ID, firstName: "Ana", lastName: "Silva", email: "ana@example.com" }),
      });
    });
    // Deliberately false, not true: this forces the Card tab into its "unavailable" text
    // state instead of trying to mount a real Stripe Elements iframe. This local dev
    // environment (unlike CI's e2e step) has a real VITE_STRIPE_PUBLISHABLE_KEY in
    // .env.local, so `cardPaymentsAvailable: true` here does NOT fall back to "no key
    // configured" — it tries to actually create a PaymentElement, which throws ("you must
    // pass a clientSecret or mode") against this test's unmocked/empty deposit-payment
    // response and crashes into an ErrorBoundary that then blocks every later click.
    // Forcing this false keeps the scan deterministic regardless of which environment
    // (with or without a real key) it runs in.
    await page.route("**/api/v1/payments/capabilities", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ cardPaymentsAvailable: false }),
      });
    });
    // Non-zero depositAmount is what puts BookAppointmentForm into the
    // PaymentMethodSelector step instead of the plain confirmation screen.
    await page.route("**/api/v1/appointments", async (route) => {
      if (route.request().method() === "POST") {
        await route.fulfill({
          status: 201,
          contentType: "application/json",
          body: JSON.stringify({
            id: APPT_ID, studioId: STUDIO_ID, artistId: ARTIST_ID, clientId: CLIENT_ID,
            date: new Date(Date.now() + 7 * 86_400_000).toISOString(),
            endDate: new Date(Date.now() + 7 * 86_400_000 + 7_200_000).toISOString(),
            durationMinutes: 120, status: "Pending", depositStatus: "Pending",
            depositAmount: 50, notes: null, createdAt: new Date().toISOString(),
          }),
        });
      } else {
        await route.continue();
      }
    });

    await loginAs(page, CLIENT_TOKEN, "ana@example.com", /\/book/i);

    await page.getByRole("combobox", { name: "Select artist" }).click();
    await page.getByRole("option", { name: "Rafaela Costa" }).waitFor({ state: "visible", timeout: 5_000 });
    await page.getByRole("option", { name: "Rafaela Costa" }).click();

    const future = new Date(Date.now() + 7 * 86_400_000);
    const pad = (n: number) => String(n).padStart(2, "0");
    const dateStr =
      `${future.getFullYear()}-${pad(future.getMonth() + 1)}-${pad(future.getDate())}` +
      `T${pad(future.getHours())}:${pad(future.getMinutes())}`;
    await page.getByLabel("Date & Time").fill(dateStr);

    await page.getByLabel("Appointment Duration").click();
    await page.getByRole("option", { name: "2 hours" }).waitFor({ state: "visible", timeout: 5_000 });
    await page.getByRole("option", { name: "2 hours" }).click();

    await page.getByLabel("What are you looking to get done?").fill("A small rose");
    await page.getByRole("button", { name: "Request Appointment" }).click();

    // Card tab renders its "unavailable" text (capabilities mocked false above) instead
    // of mounting a real Stripe Elements iframe, so this scans real rendered content
    // deterministically, regardless of whether a real Stripe key is configured locally.
    await expect(page.getByText(/Secure your slot with a deposit/)).toBeVisible({ timeout: 10_000 });
    await assertNoViolations(new AxeBuilder({ page }));

    // Cash tab is the other real, fully-renderable state of this same component.
    await page.getByRole("button", { name: "Cash" }).click();
    await expect(page.getByText("Pay at the studio")).toBeVisible();
    await assertNoViolations(new AxeBuilder({ page }));
  });

});
