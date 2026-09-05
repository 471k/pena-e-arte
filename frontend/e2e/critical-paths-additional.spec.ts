import { test, expect } from "@playwright/test";
import {
  STUDIO_ID, ARTIST_ID, CLIENT_ID, APPT_ID,
  CLIENT_TOKEN, mockAuthLogin, mockApiFallback, loginAs,
} from "./helpers";

// Two currently-uncovered high-value flows, added per the 2026-09-05 e2e staleness review
// (docs/claude/architecture.md's Feature Module Map has dozens of features but this suite had
// exactly two spec files before this — see that review's note on cadence going forward).

test.describe("Deposit payment — cash", () => {
  test("client books an appointment and completes the deposit via cash", async ({ page }) => {
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
    // Forces the Card tab into its "unavailable" text state rather than mounting a real
    // Stripe Elements iframe — this local dev environment has a real
    // VITE_STRIPE_PUBLISHABLE_KEY in .env.local (unlike CI), so leaving capabilities
    // unmocked lets PaymentMethodSelector try to create a real PaymentIntent against this
    // test's unmocked deposit-payment response, which destabilizes the DOM while the Card
    // tab is still the default-active one and made the later Cash-tab click flaky.
    await page.route("**/api/v1/payments/capabilities", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ cardPaymentsAvailable: false }),
      });
    });
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
    let cashDeclared = false;
    await page.route("**/api/v1/payments/cash", async (route) => {
      cashDeclared = true;
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ id: "payment-1", appointmentId: APPT_ID, amount: 50, status: "CashPending" }),
      });
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

    await expect(page.getByText(/Secure your slot with a deposit/)).toBeVisible({ timeout: 10_000 });

    await page.getByRole("button", { name: "Cash" }).click();
    await expect(page.getByText("Pay at the studio")).toBeVisible();
    await page.getByRole("button", { name: /Confirm — I'll pay cash at the studio/ }).click();

    // Step 3 confirmation screen — depositDone="cash" branch of BookAppointmentForm
    await expect(page.getByText("Bring the deposit in cash to the studio. The artist will confirm soon.")).toBeVisible({ timeout: 10_000 });
    expect(cashDeclared).toBe(true);
  });
});

test.describe("Intake form — consent flow", () => {
  test("client submits an intake form and must accept consent to proceed", async ({ page }) => {
    await mockApiFallback(page);
    await mockAuthLogin(page, CLIENT_TOKEN);

    await page.route("**/api/v1/appointments/mine*", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([
          {
            id: APPT_ID, studioId: STUDIO_ID, artistId: ARTIST_ID, clientId: CLIENT_ID,
            date: new Date(Date.now() + 3 * 86_400_000).toISOString(), status: "Confirmed",
          },
        ]),
      });
    });
    await page.route("**/api/v1/consent-forms/active-template*", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ id: "template-1", kind: "IntakeFormConsent", bodyText: "Test consent body text." }),
      });
    });
    let submittedBody: unknown = null;
    await page.route("**/api/v1/intake-forms", async (route) => {
      if (route.request().method() === "POST") {
        submittedBody = route.request().postDataJSON();
        await route.fulfill({
          status: 201,
          contentType: "application/json",
          body: JSON.stringify({ id: "intake-1", ...(submittedBody as object) }),
        });
      } else {
        await route.continue();
      }
    });

    await mockAuthLogin(page, CLIENT_TOKEN);
    await page.goto("/login");
    await page.getByLabel("Email").fill("ana@example.com");
    await page.getByLabel("Password", { exact: true }).fill("Password123!");
    await page.getByRole("button", { name: "Sign in" }).click();
    await expect(page).toHaveURL(/\/book/i, { timeout: 10_000 });

    await page.goto("/forms/intake/new");
    // "Intake Form" is a plain <span> in the page header, not a semantic heading — this
    // checks the form's own descriptive copy instead, which is a real <p> in the DOM.
    await expect(page.getByText("Please share your medical history and any details your artist should know before your session.")).toBeVisible();

    // Submitting without consent must block — the client-side zod refine on consentAccepted.
    await page.getByLabel("Medical history & notes").fill("No known allergies. Healthy skin, no keloids.");
    await page.getByRole("button", { name: "Submit Intake Form" }).click();
    await expect(page.getByText("You must consent before submitting")).toBeVisible();
    expect(submittedBody).toBeNull();

    // Accepting consent and resubmitting must succeed.
    await page.getByLabel(/I consent to sharing this medical\/health information/).check();
    await page.getByRole("button", { name: "Submit Intake Form" }).click();

    await expect(page.getByText("Intake form submitted!")).toBeVisible({ timeout: 10_000 });
    expect((submittedBody as { consentAccepted: boolean } | null)?.consentAccepted).toBe(true);
  });
});
