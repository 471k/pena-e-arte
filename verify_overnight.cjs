// Run from repo root: node verify_overnight.cjs
"use strict";

const { chromium } = require("C:\\nvm4w\\nodejs\\node_modules\\@playwright\\test");
const path = require("path");

const SS   = "C:\\Users\\User\\AppData\\Local\\Temp";
const BASE = "http://localhost:5173";

const ROLE_CLAIM = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

function makeFakeJwt({ role = "artist", tenantId = "00000000-0000-0000-0000-000000000002",
  userId = "00000000-0000-0000-0000-000000000001", email } = {}) {
  const em      = email ?? `${role}@verify.test`;
  const header  = Buffer.from(JSON.stringify({ alg: "HS256", typ: "JWT" })).toString("base64url");
  const payload = Buffer.from(JSON.stringify({
    sub: userId, email: em, tenant_id: tenantId, [ROLE_CLAIM]: role, exp: 9999999999,
  })).toString("base64url");
  return `${header}.${payload}.fakesig`;
}

// Block all backend API calls so the 401-redirect in baseQuery never fires.
// Each page's RTK Query hooks will land in isError state (expected, per verifier-gui docs).
async function stubAllApiCalls(page) {
  await page.route("**/api/v1/**", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: "[]" })
  );
}

const MOCK_FORM_ID = "288405e3-1474-4da4-88e4-6acb81edb6a7";

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

async function injectAuth(page, role) {
  await page.goto(`${BASE}/login`);
  await page.evaluate((t) => localStorage.setItem("auth_token", t), makeFakeJwt({ role }));
}

(async () => {
  const browser = await chromium.launch({ headless: true });

  // ── T1: OwnerLayout — logout button visible on /dashboard ──────────────────
  {
    const ctx  = await browser.newContext({ viewport: { width: 1400, height: 800 } });
    const page = await ctx.newPage();
    await injectAuth(page, "owner");
    await stubAllApiCalls(page);
    await page.goto(`${BASE}/dashboard`, { waitUntil: "domcontentloaded" });
    await page.waitForSelector("header", { timeout: 5000 });
    const visible = await page.locator("button", { hasText: "Log out" }).isVisible();
    console.log("[T1] OwnerLayout   logout visible:", visible);
    await page.screenshot({ path: path.join(SS, "t1_owner_layout.png") });
    await ctx.close();
  }

  // ── T2: ArtistLayout — logout button visible on /schedule ──────────────────
  {
    const ctx  = await browser.newContext({ viewport: { width: 1400, height: 800 } });
    const page = await ctx.newPage();
    await injectAuth(page, "artist");
    await stubAllApiCalls(page);
    await page.goto(`${BASE}/schedule`, { waitUntil: "domcontentloaded" });
    await page.waitForSelector("header", { timeout: 5000 });
    const visible = await page.locator("button", { hasText: "Log out" }).isVisible();
    console.log("[T2] ArtistLayout  logout visible:", visible);
    await page.screenshot({ path: path.join(SS, "t2_artist_layout.png") });
    await ctx.close();
  }

  // ── T3: ClientLayout — logout button visible on /book ──────────────────────
  {
    const ctx  = await browser.newContext({ viewport: { width: 1400, height: 800 } });
    const page = await ctx.newPage();
    await injectAuth(page, "client");
    await stubAllApiCalls(page);
    await page.goto(`${BASE}/book`, { waitUntil: "domcontentloaded" });
    await page.waitForSelector("header", { timeout: 5000 });
    const visible = await page.locator("button", { hasText: "Log out" }).isVisible();
    console.log("[T3] ClientLayout  logout visible:", visible);
    await page.screenshot({ path: path.join(SS, "t3_client_layout.png") });
    await ctx.close();
  }

  // ── T4: IssuerLayout — logout button visible on /platform ──────────────────
  {
    const ctx  = await browser.newContext({ viewport: { width: 1400, height: 800 } });
    const page = await ctx.newPage();
    await injectAuth(page, "issuer");
    await stubAllApiCalls(page);
    await page.goto(`${BASE}/platform`, { waitUntil: "domcontentloaded" });
    await page.waitForSelector("header", { timeout: 5000 });
    const visible = await page.locator("button", { hasText: "Log out" }).isVisible();
    console.log("[T4] IssuerLayout  logout visible:", visible);
    await page.screenshot({ path: path.join(SS, "t4_issuer_layout.png") });
    await ctx.close();
  }

  // ── T5: Clicking logout dispatches action and reaches /login ────────────────
  {
    const ctx  = await browser.newContext({ viewport: { width: 1400, height: 800 } });
    const page = await ctx.newPage();
    await injectAuth(page, "owner");
    await stubAllApiCalls(page);
    await page.goto(`${BASE}/dashboard`, { waitUntil: "domcontentloaded" });
    await page.waitForSelector("header", { timeout: 5000 });

    await page.locator("button", { hasText: "Log out" }).click();
    await page.waitForURL("**/login**", { timeout: 5000 });
    const url       = page.url();
    const tokenLeft = await page.evaluate(() => localStorage.getItem("auth_token"));
    console.log("[T5] After logout URL:", url, "| token remaining:", tokenLeft);
    await page.screenshot({ path: path.join(SS, "t5_after_logout.png") });
    await ctx.close();
  }

  // ── T6: Structured JSON formData → structured medical history view ──────────
  {
    const ctx  = await browser.newContext({ viewport: { width: 1400, height: 900 } });
    const page = await ctx.newPage();
    await injectAuth(page, "artist");

    // Single handler — API path is /api/v1/intake-forms/:id (not /forms/intake/:id)
    await page.route("**/api/v1/**", (route) => {
      const url = route.request().url();
      if (url.includes(`/intake-forms/${MOCK_FORM_ID}`)) {
        return route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            id: MOCK_FORM_ID,
            studioId: "aaaa0001-0000-0000-0000-000000000000",
            clientId: "bbbb0001-0000-0000-0000-000000000000",
            appointmentId: null,
            formData: STRUCTURED_FORM_DATA,
            fileUrl: null,
            submittedAt: "2026-04-19T08:42:00Z",
            createdAt: "2026-06-04T08:42:00Z",
          }),
        });
      }
      return route.fulfill({ status: 200, contentType: "application/json", body: "[]" });
    });

    await page.goto(`${BASE}/forms/intake/${MOCK_FORM_ID}`, { waitUntil: "domcontentloaded" });
    await page.waitForSelector("text=Full name", { timeout: 5000 });

    const rawBrace   = await page.locator("text={").count();
    const fullName   = await page.locator("text=Full name").count();
    const dob        = await page.locator("text=Date of birth").count();
    const health     = await page.locator("text=Health conditions").count();
    const allergy    = await page.locator("text=Latex and nickel").count();
    const aftercare  = await page.locator("text=Acknowledges aftercare instructions").count();

    console.log("[T6] Raw '{' brace:", rawBrace, "(want 0)");
    console.log("[T6] Full name row:", fullName, "(want 1)");
    console.log("[T6] Date of birth:", dob, "(want 1)");
    console.log("[T6] Health conditions section:", health, "(want 1)");
    console.log("[T6] Allergy detail text:", allergy, "(want 1)");
    console.log("[T6] Aftercare row:", aftercare, "(want 1)");

    await page.screenshot({ path: path.join(SS, "t6_structured_form.png"), fullPage: true });
    await ctx.close();
  }

  // ── T7 (probe): Plain-text formData → falls back to plain text ──────────────
  {
    const PLAIN_ID = "99990000-0000-0000-0000-000000000000";
    const ctx      = await browser.newContext({ viewport: { width: 1400, height: 900 } });
    const page     = await ctx.newPage();
    await injectAuth(page, "artist");

    await page.route("**/api/v1/**", (route) => {
      const url = route.request().url();
      if (url.includes(`/intake-forms/${PLAIN_ID}`)) {
        return route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            id: PLAIN_ID,
            studioId: "aaaa0001-0000-0000-0000-000000000000",
            clientId: "bbbb0001-0000-0000-0000-000000000000",
            appointmentId: null,
            formData: PLAIN_FORM_DATA,
            fileUrl: null,
            submittedAt: "2026-04-19T08:42:00Z",
            createdAt: "2026-06-04T08:42:00Z",
          }),
        });
      }
      return route.fulfill({ status: 200, contentType: "application/json", body: "[]" });
    });

    await page.goto(`${BASE}/forms/intake/${PLAIN_ID}`, { waitUntil: "domcontentloaded" });
    await page.waitForSelector("text=No known allergies", { timeout: 5000 });

    const plainVisible = await page.locator("text=No known allergies").count();
    const noHealth     = await page.locator("text=Health conditions").count();
    console.log("[T7] Plain text visible:", plainVisible, "(want 1)");
    console.log("[T7] Health section shown (want 0):", noHealth);

    await page.screenshot({ path: path.join(SS, "t7_plain_text_form.png"), fullPage: true });
    await ctx.close();
  }

  // ── T8: Intake form list — client names instead of raw JSON ─────────────────
  {
    const MOCK_FORMS = [
      { id: "f0000001-0000-0000-0000-000000000001", clientId: "c0000001-0000-0000-0000-000000000001", appointmentId: "a0000001-0000-0000-0000-000000000001", formData: JSON.stringify({ fullName: "Mia Carvalho",   dateOfBirth: "1998-09-12", hasAllergies: true  }), fileUrl: null, submittedAt: "2026-04-19T08:42:00Z", createdAt: "2026-04-19T08:00:00Z", studioId: "s0000001-0000-0000-0000-000000000001" },
      { id: "f0000001-0000-0000-0000-000000000002", clientId: "c0000001-0000-0000-0000-000000000002", appointmentId: null,                                    formData: JSON.stringify({ fullName: "Rafael Mendes",  dateOfBirth: "1990-03-22" }),                            fileUrl: null, submittedAt: null,                    createdAt: "2026-06-04T10:00:00Z", studioId: "s0000001-0000-0000-0000-000000000001" },
      { id: "f0000001-0000-0000-0000-000000000003", clientId: "c0000001-0000-0000-0000-000000000003", appointmentId: null,                                    formData: "No known allergies. Takes ibuprofen occasionally.",                                                    fileUrl: null, submittedAt: "2026-05-14T09:00:00Z", createdAt: "2026-05-14T08:30:00Z", studioId: "s0000001-0000-0000-0000-000000000001" },
    ];

    const ctx  = await browser.newContext({ viewport: { width: 1400, height: 800 } });
    const page = await ctx.newPage();
    await injectAuth(page, "artist");

    await page.route("**/api/v1/**", (route) => {
      const url = route.request().url();
      if (url.includes("/intake-forms") && !url.match(/\/intake-forms\//)) {
        return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(MOCK_FORMS) });
      }
      return route.fulfill({ status: 200, contentType: "application/json", body: "[]" });
    });

    await page.goto(`${BASE}/forms/intake`, { waitUntil: "domcontentloaded" });
    await page.waitForSelector("text=Mia Carvalho", { timeout: 5000 });

    const rawBrace  = await page.locator("text={").count();
    const mia       = await page.locator("text=Mia Carvalho").count();
    const rafael    = await page.locator("text=Rafael Mendes").count();
    const plainText = await page.locator("text=No known allergies").count();

    console.log("[T8] Raw '{' brace count:", rawBrace, "(want 0)");
    console.log("[T8] 'Mia Carvalho' visible:", mia,       "(want 1)");
    console.log("[T8] 'Rafael Mendes' visible:", rafael,    "(want 1)");
    console.log("[T8] Plain text fallback:",    plainText,  "(want 1)");

    await page.screenshot({ path: path.join(SS, "t8_intake_list.png"), fullPage: true });
    await ctx.close();
  }

  // ── T9: Login page — password toggle eye button ─────────────────────────────
  {
    const ctx  = await browser.newContext({ viewport: { width: 1400, height: 800 } });
    const page = await ctx.newPage();
    await page.goto(`${BASE}/login`, { waitUntil: "domcontentloaded" });
    await page.waitForSelector("#password", { timeout: 5000 });

    const typeBefore  = await page.locator("#password").getAttribute("type");
    const eyeVisible  = await page.locator('button[aria-label="Show password"]').isVisible();
    console.log("[T9] Input type (initial):", typeBefore, "(want password)");
    console.log("[T9] Eye button visible:", eyeVisible, "(want true)");
    await page.screenshot({ path: path.join(SS, "t9_login_eye_before.png") });

    await page.locator('button[aria-label="Show password"]').click();
    const typeAfter   = await page.locator("#password").getAttribute("type");
    const hideVisible = await page.locator('button[aria-label="Hide password"]').isVisible();
    console.log("[T9] Input type after click:", typeAfter, "(want text)");
    console.log("[T9] Hide button visible:", hideVisible, "(want true)");
    await page.screenshot({ path: path.join(SS, "t9_login_eye_after.png") });
    await ctx.close();
  }

  // ── T10: Register page — map picker visible, no raw lat/lng inputs ──────────
  {
    const ctx  = await browser.newContext({ viewport: { width: 1400, height: 900 } });
    const page = await ctx.newPage();
    await page.goto(`${BASE}/register`, { waitUntil: "domcontentloaded" });
    await page.waitForSelector(".leaflet-container", { timeout: 8000 });

    const mapPresent    = await page.locator(".leaflet-container").count();
    const myLocBtn      = await page.locator("button", { hasText: "My location" }).count();
    const rawLatInput   = await page.locator("#latitude").count();
    const rawCityInput  = await page.locator("#city").count();

    console.log("[T10] Leaflet map present:", mapPresent,  "(want 1)");
    console.log("[T10] 'My location' button:", myLocBtn,   "(want 1)");
    console.log("[T10] Raw latitude input gone:", rawLatInput  === 0, "(want true)");
    console.log("[T10] Raw city input gone:", rawCityInput === 0, "(want true)");

    await page.waitForTimeout(1500); // let tiles start loading
    await page.screenshot({ path: path.join(SS, "t10_register_map.png"), fullPage: true });
    await ctx.close();
  }

  await browser.close();
  console.log("\nDone. Screenshots saved to", SS);
})().catch((err) => { console.error(err); process.exit(1); });
