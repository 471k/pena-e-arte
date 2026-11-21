import { test, expect, type Page } from "@playwright/test";
import { STUDIO_ID, ARTIST_ID, CLIENT_ID, APPT_ID, OWNER_TOKEN, CLIENT_TOKEN, mockApiFallback, loginAs } from "./helpers";

// Slice 6b (S05, AP12, the control-target question, text resize): one rendered pass measured the four surfaces at
// 320/390/420/768/800/1024/1100/1440 in both themes, plus 200% text; this spec keeps what it found fixed. Both
// themes come from the two Playwright projects. 44px below is the PROJECT's comfortable-target goal, not WCAG:
// WCAG 2.2 AA (2.5.8) is 24x24 CSS px with spacing exceptions (docs/08).

const json = (b: unknown) => ({ status: 200, contentType: "application/json", body: JSON.stringify(b) });
const STUDIO = {
  id: STUDIO_ID, name: "Tinta & Alma", slug: "tinta-alma", city: "Tirana", latitude: 41, longitude: 19,
  showPlatformBranding: true, allowBrandingRemoval: false, isActive: true, slugLockedAt: null, isSolo: false,
  isPublished: true, timezone: "Europe/Tirane", countryCode: "AL", currency: "ALL",
  trialExpiresAt: new Date(Date.now() + 14 * 86_400_000).toISOString(), createdAt: new Date().toISOString(),
};
const now = new Date();
function at(dayOffset: number, hour: number) { const d = new Date(now); d.setDate(d.getDate() + dayOffset); d.setHours(hour, 0, 0, 0); return d.toISOString(); }
const appt = (i: number, day: number, hour: number, status = "Confirmed") => ({
  id: i === 0 ? APPT_ID : `appt-${i}`, studioId: STUDIO_ID, artistId: ARTIST_ID, clientId: CLIENT_ID,
  date: at(day, hour), endDate: at(day, hour + 2), durationMinutes: 120, status, depositStatus: "Paid", depositAmount: 5000,
  notes: null, createdAt: at(-10, 10), clientName: "Anastasia Hoxhaj-Papadopoulou", artistName: "Rafaela Costa",
  serviceName: "Large colour session", tattooDescription: "Koi fish across the forearm", readiness: { required: 3, satisfied: 1 },
  selfService: { noticeWindowHours: 48, isWithinNoticeWindow: false }, attachments: [],
});
const APPTS = [appt(0, 1, 10), appt(1, 1, 14, "Pending"), appt(2, 2, 11), appt(3, 0, 16, "Pending")];
const CLIENTS = Array.from({ length: 6 }, (_, i) => ({
  id: `c${i}`, studioId: STUDIO_ID, firstName: ["Ana", "Besa", "Dritan", "Elira", "Gent", "Ilir"][i], lastName: "Hoxhaj-Papadopoulou",
  email: `client${i}@example-long-domain.com`, phone: "+355691234567", createdAt: at(-30, 10), userId: null,
  artistId: ARTIST_ID, artistName: "Rafaela Costa", erasureRequestedAt: null, archivedAt: null,
}));
const PAYMENTS = ["Paid", "Pending", "Refunded", "Failed", "CashPending"].map((status, i) => ({
  id: `p${i}`, appointmentId: `appt-${i}`, amount: 12000, status, method: i % 2 ? "Cash" : "Card", providerReferenceId: null,
  clientToken: null, cashNote: null, paidAt: status === "Paid" ? at(-1, 12) : null, clientName: "Anastasia Hoxhaj-Papadopoulou",
  appointmentDate: at(1, 10), currency: "ALL", refundedAmount: status === "Refunded" ? 12000 : null,
}));

async function owner(page: Page, w: number) {
  await page.setViewportSize({ width: w, height: 900 });
  await page.addInitScript(() => localStorage.setItem("cookie-consent", "accepted"));
  await mockApiFallback(page);
  await page.route("**/api/v1/studios/me", (r) => r.fulfill(json(STUDIO)));
  await page.route(/\/api\/v1\/appointments(\?.*)?$/, (r) => r.fulfill(json(APPTS)));
  await page.route(/\/api\/v1\/clients(\?.*)?$/, (r) => r.fulfill(json(CLIENTS)));
  await page.route(/\/api\/v1\/payments(\?.*)?$/, (r) => r.fulfill(json(PAYMENTS)));
  await page.route("**/api/v1/artists", (r) => r.fulfill(json([{ id: ARTIST_ID, firstName: "Rafaela", lastName: "Costa", specializations: [], isActive: true }])));
  await loginAs(page, OWNER_TOKEN, "owner@tinta-alma.com", /^(?!.*\/login).+/);
}
async function client(page: Page, w: number) {
  await page.setViewportSize({ width: w, height: 900 });
  await page.addInitScript(() => localStorage.setItem("cookie-consent", "accepted"));
  await mockApiFallback(page);
  await page.route("**/api/v1/studios/me", (r) => r.fulfill(json(STUDIO)));
  await page.route(`**/api/v1/appointments/mine/${APPT_ID}`, (r) => r.fulfill(json({
    appointment: APPTS[0],
    readiness: [
      { key: "Deposit", label: "Pay your deposit", actionLabel: "Pay deposit", required: true, satisfied: true, available: true, route: null },
      { key: "ConsentForm", label: "Sign the consent form", actionLabel: "Sign consent form", required: true, satisfied: false, available: true, route: "/forms/consent" },
      { key: "IntakeForm", label: "Fill in the intake form", actionLabel: "Fill in intake form", required: true, satisfied: false, available: true, route: "/forms/intake" },
    ],
    contact: { recipientUserId: "user-id-artist", recipientRole: "artist", recipientName: "Rafaela Costa", studioName: "Tinta & Alma", studioPhone: "+355691234567", studioInstagram: "tinta.alma" },
    visit: { addressComplete: true, addressLine1: "Rruga Myslym Shyri 42, Kati 3", addressLine2: null, city: "Tirana", postalCode: "1001" },
  })));
  await page.route("**/api/v1/appointments/mine", (r) => r.fulfill(json(APPTS)));
  await loginAs(page, CLIENT_TOKEN, "ana@example.com", /^(?!.*\/login).+/);
}


const overflow = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);

for (const width of [320, 390]) {
  test(`S05 schedule at ${width}px: no sideways scroll, the time never breaks, actions on their own row at 44px`, async ({ page }) => {
    await owner(page, width);
    await page.goto("/schedule");
    await expect(page.getByRole("link", { name: /Anastasia/ }).first()).toBeVisible();
    expect(await overflow(page)).toBeLessThanOrEqual(0);

    const time = page.getByRole("link", { name: /Anastasia/ }).first().locator("span").first();
    // One rect per text fragment ("{start} – {end}" is three), so: do they all sit on one line?
    expect(await time.evaluate((el) => new Set(Array.from(el.getClientRects()).map((r) => Math.round(r.top))).size)).toBe(1);

    const actions = page.locator("[data-record-actions]").first();
    const link = await page.getByRole("link", { name: /Anastasia/ }).first().boundingBox();
    const row = await actions.boundingBox();
    expect(link && row && row.y >= link.y + link.height).toBeTruthy();
    for (const button of await actions.getByRole("button").all()) {
      const box = await button.boundingBox();
      expect(box && box.height >= 44).toBeTruthy();
    }
  });

  test(`S05 clients at ${width}px: the header wraps instead of running off screen, and search keeps a usable width`, async ({ page }) => {
    await owner(page, width);
    await page.goto("/clients");
    const newClient = page.getByRole("button", { name: "New Client" });
    await expect(newClient).toBeVisible();
    expect(await overflow(page)).toBeLessThanOrEqual(0);
    const box = await newClient.boundingBox();
    expect(box && box.x + box.width <= width).toBeTruthy();
    const search = await page.getByRole("textbox", { name: /search clients/i }).boundingBox();
    expect(search && search.width >= width - 64).toBeTruthy();
  });

  test(`AP12 the client's appointment page at ${width}px fits and shows the appointment`, async ({ page }) => {
    await client(page, width);
    await page.goto(`/my-appointments/${APPT_ID}`);
    await expect(page.getByText("Large colour session")).toBeVisible();
    await expect(page.getByText("Your visit")).toBeVisible();
    expect(await overflow(page)).toBeLessThanOrEqual(0);
  });
}

test("2.5.8: the Show archived switch answers a pointer beyond its 20px-high track (a target of at least 24x24)", async ({ page }) => {
  await owner(page, 390);
  await page.goto("/clients");
  const sw = page.getByRole("switch", { name: "Show archived clients" });
  const box = (await sw.boundingBox())!;
  expect(box.height).toBeLessThan(24);
  // 3px above and below the visible track, and 1px either side, still land on the switch: 40x28 in all.
  const cx = box.x + box.width / 2;
  const cy = box.y + box.height / 2;
  for (const [x, y] of [[cx, box.y - 3], [cx, box.y + box.height + 3], [box.x - 1, cy], [box.x + box.width + 1, cy]]) {
    const hit = await page.evaluate(([xx, yy]) => document.elementFromPoint(xx, yy)?.closest("[role=switch]")?.getAttribute("aria-label") ?? null, [x, y]);
    expect(hit).toBe("Show archived clients");
  }
});

test("200% text at 1280px: nothing scrolls sideways, and the studio's name never sits under the Owner/Artist switch", async ({ page }) => {
  await page.route("**/api/v1/artists/me", (r) => r.fulfill(json({ id: ARTIST_ID, studioId: STUDIO_ID, firstName: "Rafaela", lastName: "Costa", email: "o@x.com", specializations: [], hourlyRate: null, isActive: true, avatarUrl: null, portfolioImages: [], slug: null, userId: "user-id-owner", createdAt: "2024-01-15T10:00:00.000Z", updatedAt: "2024-06-01T10:00:00.000Z" })));
  await owner(page, 1280);
  await page.goto("/payments");
  await page.evaluate(() => { document.documentElement.style.fontSize = "200%"; });
  await expect(page.getByRole("tablist", { name: /switch between owner and artist/i }).first()).toBeVisible();
  expect(await overflow(page)).toBeLessThanOrEqual(0);

  const name = page.locator("header").first().getByText("Tinta & Alma", { exact: true });
  if (await name.isVisible()) {
    const n = (await name.boundingBox())!;
    const s = (await page.getByRole("tablist", { name: /switch between owner and artist/i }).first().boundingBox())!;
    expect(n.x + n.width <= s.x || s.x + s.width <= n.x).toBeTruthy();
  }
  // Wherever the header has no room, the user menu still says which studio this is.
  await page.getByRole("button", { name: "User menu" }).click();
  await expect(page.getByText("Tinta & Alma · Owner")).toBeVisible();
});
