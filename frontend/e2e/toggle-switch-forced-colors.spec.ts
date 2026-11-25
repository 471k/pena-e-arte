import { test, expect, type Locator } from "@playwright/test";
import { mockApiFallback, mockAuthLogin, loginAs, CLIENT_TOKEN, ARTIST_ID, CLIENT_ID, STUDIO_ID } from "./helpers";

// Forced colors (Windows contrast themes) repaints backgrounds as Canvas and removes box-shadows, so the
// ToggleSwitch thumb (a white disc with a shadow) vanished into its track: off showed an empty outline, on only a
// check. The thumb is now drawn in system colours. This checks, in both states, that the thumb's colour differs
// from the track's, read after the colour transition has finished, and attaches a screenshot of each state.
// Client /book has a switch; jsdom has no forced-colors mode, so this runs in a real browser.

const json = (b: unknown) => ({ status: 200, contentType: "application/json", body: JSON.stringify(b) });

const colours = (sw: Locator) =>
  sw.evaluate((btn) => {
    const thumb = btn.firstElementChild as HTMLElement;
    return { track: getComputedStyle(btn).backgroundColor, thumb: getComputedStyle(thumb).backgroundColor };
  });

test("forced colors: the switch thumb is visible in both states", async ({ page }, testInfo) => {
  test.setTimeout(90_000);
  await page.emulateMedia({ forcedColors: "active" });
  await page.addInitScript(() => localStorage.setItem("cookie-consent", "accepted"));
  await mockApiFallback(page);
  await page.route("**/api/v1/studios/me", (r) => r.fulfill(json({ id: STUDIO_ID, timezone: "Europe/Tirane" })));
  await mockAuthLogin(page, CLIENT_TOKEN);
  await page.route("**/api/v1/artists*", (r) => r.fulfill(json([{ id: ARTIST_ID, firstName: "Rafaela", lastName: "Costa", email: "r@x.co", specializations: [] }])));
  await page.route("**/api/v1/clients/me", (r) => r.fulfill(json({ id: CLIENT_ID, firstName: "Ana", lastName: "Silva", email: "ana@example.com" })));
  await loginAs(page, CLIENT_TOKEN, "ana@example.com", /\/my-appointments/i);
  await page.goto("/book");
  expect(await page.evaluate(() => matchMedia("(forced-colors: active)").matches)).toBe(true);

  const sw = page.getByRole("switch").first();
  await expect(sw).toHaveAttribute("aria-checked", "false");
  for (const state of ["off", "on"] as const) {
    if (state === "on") {
      await sw.click();
      await expect(sw).toHaveAttribute("aria-checked", "true");
    }
    // transition-colors animates background-color, so poll until the thumb and track have settled apart.
    await expect.poll(async () => { const c = await colours(sw); return c.thumb !== c.track; }, { message: `thumb differs from track (${state})` }).toBe(true);
    const c = await colours(sw);
    expect(c.thumb, `thumb is not transparent (${state})`).not.toMatch(/rgba\(.*, 0\)|transparent/);
    await testInfo.attach(`switch-${state}`, { body: await sw.screenshot(), contentType: "image/png" });
  }
});
