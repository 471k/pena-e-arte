import { chromium } from "playwright";
import path from "path";
import { makeFakeJwt } from "./.claude/skills/verifier-gui/fake-jwt.mjs";

const SS   = "C:\\Users\\User\\AppData\\Local\\Temp";
const BASE = "http://localhost:5173";

const STRUCTURED_FORM_DATA = JSON.stringify({
  fullName: "Mia Carvalho",
  dateOfBirth: "1998-09-12",
  hasBloodCondition: false,
  hasDiabetes: false,
  takesBloodThinners: false,
  hasAllergies: true,
  allergyDetails: "Latex and nickel",
  hasSkinCondition: false,
  isPregnant: false,
  acknowledgesAftercare: true,
});

const PLAIN_FORM_DATA =
  "No known allergies. Takes ibuprofen occasionally. Acknowledges aftercare instructions.";

const MOCK_FORM_ID = "288405e3-1474-4da4-88e4-6acb81edb6a7";

async function injectAuth(page, role) {
  await page.goto(`${BASE}/login`);
  await page.evaluate(
    (t) => localStorage.setItem("auth_token", t),
    makeFakeJwt({ role })
  );
}

(async () => {
  const browser = await chromium.launch({ headless: true });

  // ── TEST 1: Logout button visible in OwnerLayout (/dashboard) ──────────────
  {
    const ctx  = await browser.newContext();
    const page = await ctx.newPage();
    await injectAuth(page, "owner");
    await page.goto(`${BASE}/dashboard`, { waitUntil: "networkidle" });

    const logoutBtn = page.locator("button", { hasText: "Log out" });
    const visible   = await logoutBtn.isVisible();
    console.log("[1] OwnerLayout logout button visible:", visible);
    await page.screenshot({ path: path.join(SS, "t1_owner_layout.png") });
    await ctx.close();
  }

  // ── TEST 2: Logout button visible in ArtistLayout (/schedule) ─────────────
  {
    const ctx  = await browser.newContext();
    const page = await ctx.newPage();
    await injectAuth(page, "artist");
    await page.goto(`${BASE}/schedule`, { waitUntil: "networkidle" });

    const logoutBtn = page.locator("button", { hasText: "Log out" });
    const visible   = await logoutBtn.isVisible();
    console.log("[2] ArtistLayout logout button visible:", visible);
    await page.screenshot({ path: path.join(SS, "t2_artist_layout.png") });
    await ctx.close();
  }

  // ── TEST 3: Logout button visible in ClientLayout (/book) ─────────────────
  {
    const ctx  = await browser.newContext();
    const page = await ctx.newPage();
    await injectAuth(page, "client");
    await page.goto(`${BASE}/book`, { waitUntil: "networkidle" });

    const logoutBtn = page.locator("button", { hasText: "Log out" });
    const visible   = await logoutBtn.isVisible();
    console.log("[3] ClientLayout logout button visible:", visible);
    await page.screenshot({ path: path.join(SS, "t3_client_layout.png") });
    await ctx.close();
  }

  // ── TEST 4: Logout button visible in IssuerLayout (/platform) ─────────────
  {
    const ctx  = await browser.newContext();
    const page = await ctx.newPage();
    await injectAuth(page, "issuer");
    await page.goto(`${BASE}/platform`, { waitUntil: "networkidle" });

    const logoutBtn = page.locator("button", { hasText: "Log out" });
    const visible   = await logoutBtn.isVisible();
    console.log("[4] IssuerLayout logout button visible:", visible);
    await page.screenshot({ path: path.join(SS, "t4_issuer_layout.png") });
    await ctx.close();
  }

  // ── TEST 5: Clicking logout clears token and redirects to /login ──────────
  {
    const ctx  = await browser.newContext();
    const page = await ctx.newPage();
    await injectAuth(page, "owner");
    await page.goto(`${BASE}/dashboard`, { waitUntil: "networkidle" });

    await page.locator("button", { hasText: "Log out" }).click();
    await page.waitForURL("**/login**", { timeout: 5000 });
    const url       = page.url();
    const tokenLeft = await page.evaluate(() => localStorage.getItem("auth_token"));
    console.log("[5] After logout URL:", url, "| token remaining:", tokenLeft);
    await page.screenshot({ path: path.join(SS, "t5_after_logout.png") });
    await ctx.close();
  }

  // ── TEST 6: Structured JSON formData → renders medical history view ────────
  {
    const ctx  = await browser.newContext();
    const page = await ctx.newPage();
    await injectAuth(page, "artist");

    await page.route(`**/api/v1/forms/intake/${MOCK_FORM_ID}`, (route) =>
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          id:            MOCK_FORM_ID,
          studioId:      "aaaa0001-0000-0000-0000-000000000000",
          clientId:      "bbbb0001-0000-0000-0000-000000000000",
          appointmentId: null,
          formData:      STRUCTURED_FORM_DATA,
          fileUrl:       null,
          submittedAt:   "2026-04-19T08:42:00Z",
          createdAt:     "2026-06-04T08:42:00Z",
        }),
      })
    );

    await page.goto(`${BASE}/forms/intake/${MOCK_FORM_ID}`, { waitUntil: "networkidle" });

    const rawBraceCount   = await page.locator("text={").count();
    const fullNameLabel   = await page.locator("text=Full name").count();
    const dobLabel        = await page.locator("text=Date of birth").count();
    const healthSection   = await page.locator("text=Health conditions").count();
    const allergyDetails  = await page.locator("text=Latex and nickel").count();
    const aftercareRow    = await page.locator("text=Acknowledges aftercare instructions").count();

    console.log("[6] Raw JSON brace '{' visible:", rawBraceCount);
    console.log("[6] 'Full name' label:", fullNameLabel);
    console.log("[6] 'Date of birth' label:", dobLabel);
    console.log("[6] 'Health conditions' section:", healthSection);
    console.log("[6] Allergy detail text:", allergyDetails);
    console.log("[6] Aftercare row:", aftercareRow);

    await page.screenshot({ path: path.join(SS, "t6_structured_form.png"), fullPage: true });
    await ctx.close();
  }

  // ── TEST 7 (probe): Plain-text formData → falls back gracefully ────────────
  {
    const PLAIN_ID = "99990000-0000-0000-0000-000000000000";
    const ctx      = await browser.newContext();
    const page     = await ctx.newPage();
    await injectAuth(page, "artist");

    await page.route(`**/api/v1/forms/intake/${PLAIN_ID}`, (route) =>
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          id:            PLAIN_ID,
          studioId:      "aaaa0001-0000-0000-0000-000000000000",
          clientId:      "bbbb0001-0000-0000-0000-000000000000",
          appointmentId: null,
          formData:      PLAIN_FORM_DATA,
          fileUrl:       null,
          submittedAt:   "2026-04-19T08:42:00Z",
          createdAt:     "2026-06-04T08:42:00Z",
        }),
      })
    );

    await page.goto(`${BASE}/forms/intake/${PLAIN_ID}`, { waitUntil: "networkidle" });

    const plainTextVisible = await page.locator("text=No known allergies").count();
    const healthSection    = await page.locator("text=Health conditions").count();
    console.log("[7] Plain text 'No known allergies' visible:", plainTextVisible);
    console.log("[7] 'Health conditions' section shown (should be 0):", healthSection);

    await page.screenshot({ path: path.join(SS, "t7_plain_text_form.png"), fullPage: true });
    await ctx.close();
  }

  await browser.close();
  console.log("Done. Screenshots in", SS);
})();
