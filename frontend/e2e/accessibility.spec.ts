import { test, expect, type Page } from "@playwright/test";
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

// A Sonner toast fades/slides in over ~150ms. axe blends a partly transparent element's
// text colour into the background, so a scan that lands mid-animation reads the toast
// (opacity ~0.4 at mount) as low-contrast grey and reports a false color-contrast
// violation — measured: opacity 0.41 at mount, 1.0 (rgb(23,23,23) on white) 150ms later.
// It only surfaced when a runner happened to be fast enough to scan inside that window,
// which is how it flaked on main CI and silently skipped the CD deploy (2026-09-24).
// Waiting for every toast to be fully opaque (or gone) makes the scan deterministic while
// still checking the toast's real, settled contrast. Resolves immediately when none exist.
async function waitForToastsToSettle(page: Page) {
  await page.waitForFunction(() =>
    Array.from(document.querySelectorAll("[data-sonner-toast]")).every(
      (el) => getComputedStyle(el).opacity === "1",
    ),
  );
}

async function assertNoViolations(builder: AxeBuilder, page: Page) {
  await waitForToastsToSettle(page);
  const results = await builder.withTags(WCAG_TAGS).analyze();
  expect(results.violations, violationSummary(results.violations)).toEqual([]);
}

test.describe("Accessibility (WCAG 2.1 AA) — critical surfaces", () => {

  test("sign-in page has no violations", async ({ page }) => {
    await page.goto("/login");
    await assertNoViolations(new AxeBuilder({ page }), page);
  });

  test("studio sign-up page has no violations", async ({ page }) => {
    await page.goto("/register");
    // Leaflet's own generated map markup is third-party and out of this project's
    // control — excluded so this gate only ever flags application code.
    await assertNoViolations(new AxeBuilder({ page }).exclude(".leaflet-container"), page);
  });

  test("public guest booking flow has no violations", async ({ page }) => {
    await page.route("**/api/v1/public/studios/*/booking/artists", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([
          { id: ARTIST_ID, firstName: "Rafaela", lastName: "Costa", specializations: ["fineline"], hourlyRate: 80, avatarUrl: null },
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
    await assertNoViolations(new AxeBuilder({ page }), page);
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
    await assertNoViolations(new AxeBuilder({ page }), page);
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
          { id: ARTIST_ID, firstName: "Rafaela", lastName: "Costa", email: "rafaela@tinta-alma.com", specializations: [] },
        ]),
      });
    });
    await page.route("**/api/v1/payments*", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([
          { id: PAYMENT_ID, clientName: "Ana Silva", amount: 50, currency: "EUR", status: "CashPending" },
        ]),
      });
    });

    await loginAs(page, OWNER_TOKEN, "owner@tinta-alma.com", /\/dashboard/i);
    await expect(page.getByText("Awaiting Cash")).toBeVisible();
    await assertNoViolations(new AxeBuilder({ page }), page);
  });

  test("deposit payment page (not-found state) has no violations", async ({ page }) => {
    await mockApiFallback(page);
    await mockAuthLogin(page, CLIENT_TOKEN);
    await page.route(`**/api/v1/payments/${PAYMENT_ID}/client-token`, async (route) => {
      await route.fulfill({ status: 404, contentType: "application/json", body: "{}" });
    });

    await loginAs(page, CLIENT_TOKEN, "ana@example.com", /\/book/i);
    await page.goto(`/pay/${PAYMENT_ID}`);
    await expect(page.getByText("Payment not found or you don't have access to it.")).toBeVisible();
    await assertNoViolations(new AxeBuilder({ page }), page);
  });

  test("in-flow deposit payment method selector has no violations", async ({ page }) => {
    await mockApiFallback(page);
    await mockAuthLogin(page, CLIENT_TOKEN);
    await page.route("**/api/v1/artists*", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([
          { id: ARTIST_ID, firstName: "Rafaela", lastName: "Costa", email: "rafaela@tinta-alma.com", specializations: [] },
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
    await assertNoViolations(new AxeBuilder({ page }), page);

    // Cash tab is the other real, fully-renderable state of this same component.
    await page.getByRole("button", { name: "Cash" }).click();
    await expect(page.getByText("Pay at the studio")).toBeVisible();
    await assertNoViolations(new AxeBuilder({ page }), page);
  });

});

// ── After Hours scoped palette (DEC01, slice 0) ───────────────────────────────────────
// The client/owner tests above already scan themed screens (each sits inside a role layout
// carrying data-theme="after-hours"), in both colour-scheme projects. These two tests make
// that coverage non-vacuous: they assert the scoped palette is what actually rendered —
// on the screen AND on overlays Radix portals to <body>, outside the layout — and scan with
// those overlays open; and that public pages still render the unscoped palette.

const AFTER_HOURS = {
  light: { canvas: "rgb(245, 241, 232)", surface: "rgb(255, 253, 247)" },
  dark:  { canvas: "rgb(25, 22, 29)",    surface: "rgb(33, 29, 38)" },
} as const;
// index.css's unscoped --color-background: hsl(0 0% 100%) / hsl(240 10% 3.9%).
const UNSCOPED_BACKGROUND = { light: "rgb(255, 255, 255)", dark: "rgb(9, 9, 11)" } as const;
// A toast paints --color-popover / --color-popover-foreground / --color-border (index.css).
// Unscoped: hsl(0 0% 100%) / hsl(240 10% 3.9%) / hsl(240 5.9% 58%) light,
//           hsl(240 10% 3.9%) / hsl(0 0% 98%) / hsl(240 5% 40%) dark.
const UNSCOPED_TOAST = {
  light: { bg: "rgb(255, 255, 255)", color: "rgb(9, 9, 11)",    border: "rgb(142, 142, 154)" },
  dark:  { bg: "rgb(9, 9, 11)",      color: "rgb(250, 250, 250)", border: "rgb(97, 97, 107)" },
} as const;
// After Hours: #FFFDF7 / #19161D / #80718D light, #211D26 / #F5F1E8 / #877A93 dark.
const AFTER_HOURS_TOAST = {
  light: { bg: AFTER_HOURS.light.surface, color: "rgb(25, 22, 29)",    border: "rgb(128, 113, 141)" },
  dark:  { bg: AFTER_HOURS.dark.surface,  color: "rgb(245, 241, 232)", border: "rgb(135, 122, 147)" },
} as const;

async function colourScheme(page: Page): Promise<"light" | "dark"> {
  return page.evaluate(() => (matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light"));
}

/** The background colour actually painted behind `selector`'s first match: its own, or the
 *  nearest ancestor's that isn't transparent. */
async function paintedBackground(page: Page, selector: string): Promise<string> {
  return page.evaluate((sel) => {
    let el = document.querySelector(sel);
    while (el) {
      const bg = getComputedStyle(el).backgroundColor;
      if (bg !== "rgba(0, 0, 0, 0)" && bg !== "transparent") return bg;
      el = el.parentElement;
    }
    return "none";
  }, selector);
}

test.describe("After Hours palette — scoped to authenticated layouts", () => {

  test("themed screen and its portalled overlays (select, help sheet) render the scoped palette and pass axe", async ({ page }) => {
    await mockApiFallback(page);
    await mockAuthLogin(page, CLIENT_TOKEN);
    await page.route("**/api/v1/artists*", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([
          { id: ARTIST_ID, firstName: "Rafaela", lastName: "Costa", email: "rafaela@tinta-alma.com", specializations: [] },
        ]),
      });
    });

    await loginAs(page, CLIENT_TOKEN, "ana@example.com", /\/book/i);
    const mode = await colourScheme(page);
    const palette = AFTER_HOURS[mode];

    // The layout root carries the scope and paints the After Hours canvas…
    await expect(page.locator('[data-theme="after-hours"]')).toHaveCount(1);
    expect(await paintedBackground(page, '[data-theme="after-hours"]')).toBe(palette.canvas);
    // …while <html>/<body> themselves stay unscoped (no global theme, DEC01 / D4).
    expect(await page.evaluate(() => document.documentElement.hasAttribute("data-theme"))).toBe(false);
    expect(await page.evaluate(() => getComputedStyle(document.body).backgroundColor)).toBe(UNSCOPED_BACKGROUND[mode]);

    // Select: Radix portals the listbox to <body>, outside the layout div.
    await page.getByRole("combobox", { name: "Select artist" }).click();
    await page.getByRole("option", { name: "Rafaela Costa" }).waitFor({ state: "visible", timeout: 5_000 });
    // (Proves the premise: the listbox is NOT inside the themed subtree.)
    expect(await page.evaluate(() => Boolean(document.querySelector('[role="listbox"]')?.closest("[data-theme]")))).toBe(false);
    expect(await paintedBackground(page, '[role="listbox"]')).toBe(palette.surface);
    // Scanned on the overlay itself: with a Radix modal open, the rest of the page is
    // aria-hidden on purpose, which axe reports as aria-hidden-focus. aria-required-children
    // is off for this one scan only: the artist picker puts its search <input> inside
    // role="listbox". Both reproduce identically on origin/main (3 Nov 2026, measured against
    // its production build, both schemes) — pre-existing, logged as a follow-up, not caused
    // by the palette. Every other WCAG rule, colour contrast included, still runs.
    await assertNoViolations(
      new AxeBuilder({ page }).include('[role="listbox"]').disableRules(["aria-required-children"]),
      page,
    );
    await page.keyboard.press("Escape");
    await expect(page.getByRole("listbox")).toHaveCount(0);

    // Help sheet: a Radix Dialog, also portalled to <body>.
    await page.getByRole("button", { name: "Open help menu" }).click();
    const sheet = page.getByRole("dialog", { name: "Help" });
    await expect(sheet).toBeVisible();
    // Let the slide-in finish so axe reads settled colours, not a mid-animation frame.
    await expect.poll(() => sheet.evaluate((el) => el.getAnimations().length)).toBe(0);
    expect(await paintedBackground(page, '[role="dialog"]')).toBe(palette.canvas);
    await assertNoViolations(new AxeBuilder({ page }).include('[role="dialog"]'), page);
  });

  // Toasts (accessibility cleanup, 2026-11-04). Sonner's stylesheet is unlayered, so it beats
  // every Tailwind utility; index.css themes toasts through Sonner's own --normal-* variables
  // instead. Before that, a toast painted Sonner's stock #fff/#171717 (light) and #000/#fcfcfc
  // (dark) everywhere, ignoring both the app palette and the After Hours scope. These assert
  // the painted colours of a REAL toast — one out of scope (/contact) and one in scope
  // (/book) — in whichever colour scheme this project runs, and scan each with axe.
  test("toasts paint the app palette out of scope and the After Hours palette in scope, and pass axe", async ({ page }) => {
    const settledToast = async (text: RegExp) => {
      const toast = page.locator("[data-sonner-toast]").filter({ hasText: text }).first();
      await expect(toast).toBeVisible({ timeout: 10_000 });
      await waitForToastsToSettle(page);
      return toast.evaluate((el) => {
        const s = getComputedStyle(el);
        return { bg: s.backgroundColor, color: s.color, border: s.borderTopColor, themed: Boolean(el.closest("[data-theme]")) };
      });
    };

    // ── Out of scope: the public contact form (PublicContentLayout, no data-theme). ──
    await mockApiFallback(page);
    await page.goto("/contact");
    const mode = await colourScheme(page);
    await page.getByLabel("Name").fill("Ana Silva");
    await page.getByLabel("Email").fill("ana@example.com");
    await page.getByLabel("Message").fill("Hello — testing the contact form.");
    await page.getByRole("button", { name: "Send message" }).click();
    const outside = await settledToast(/Message sent/);
    expect(await page.locator("[data-theme]").count()).toBe(0);
    expect(outside).toEqual({ ...UNSCOPED_TOAST[mode], themed: false });
    await assertNoViolations(new AxeBuilder({ page }).include("[data-sonner-toaster]"), page);

    // ── In scope: a booking the mocked API rejects fires a real toast.error on /book. ──
    await page.unrouteAll({ behavior: "ignoreErrors" });
    await mockApiFallback(page);
    await mockAuthLogin(page, CLIENT_TOKEN);
    await page.route("**/api/v1/artists*", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([
          { id: ARTIST_ID, firstName: "Rafaela", lastName: "Costa", email: "rafaela@tinta-alma.com", specializations: [] },
        ]),
      });
    });
    await page.route("**/api/v1/appointments", async (route) => {
      if (route.request().method() === "POST") {
        await route.fulfill({
          status: 400,
          contentType: "application/json",
          body: JSON.stringify({ title: "Bad Request", detail: "That slot was just taken." }),
        });
      } else {
        await route.fallback();
      }
    });
    await loginAs(page, CLIENT_TOKEN, "ana@example.com", /\/book/i);
    await page.getByRole("combobox", { name: "Select artist" }).click();
    await page.getByRole("option", { name: "Rafaela Costa" }).waitFor({ state: "visible", timeout: 5_000 });
    await page.getByRole("option", { name: "Rafaela Costa" }).click();
    const future = new Date(Date.now() + 7 * 86_400_000);
    const pad = (n: number) => String(n).padStart(2, "0");
    await page.getByLabel("Date & Time").fill(
      `${future.getFullYear()}-${pad(future.getMonth() + 1)}-${pad(future.getDate())}` +
      `T${pad(future.getHours())}:${pad(future.getMinutes())}`,
    );
    await page.getByLabel("Appointment Duration").click();
    await page.getByRole("option", { name: "2 hours" }).waitFor({ state: "visible", timeout: 5_000 });
    await page.getByRole("option", { name: "2 hours" }).click();
    await page.getByLabel("What are you looking to get done?").fill("A small rose");
    await page.getByRole("button", { name: "Request Appointment" }).click();
    const inside = await settledToast(/./);
    // The toaster mounts beside the router, NOT inside the themed layout — the scoped
    // colours reach it only through index.css's `body:has(...) [data-sonner-toaster]` hook.
    expect(inside).toEqual({ ...AFTER_HOURS_TOAST[mode], themed: false });
    await assertNoViolations(new AxeBuilder({ page }).include("[data-sonner-toaster]"), page);
  });

  test("public pages outside the four layouts keep the unscoped palette", async ({ page }) => {
    await mockApiFallback(page);
    const mode = await (async () => { await page.goto("/login"); return colourScheme(page); })();
    for (const path of ["/login", "/features", "/s/tinta-alma", "/embed/tinta-alma"]) {
      await page.goto(path);
      await page.waitForLoadState("networkidle");
      await expect(page.locator("[data-theme]"), path).toHaveCount(0);
      expect(await page.evaluate(() => getComputedStyle(document.body).backgroundColor), path).toBe(UNSCOPED_BACKGROUND[mode]);
      expect(
        await page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue("--color-background").trim()),
        path,
      ).not.toBe(mode === "light" ? "#f5f1e8" : "#19161d");
    }
  });

});
