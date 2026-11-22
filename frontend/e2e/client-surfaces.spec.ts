import { test, expect, type Page } from "@playwright/test";
import { APPT_ID, CLIENT_ID, STUDIO_ID, ARTIST_ID, CLIENT_TOKEN, mockApiFallback, mockStudioZone, loginAs } from "./helpers";

// Slice 6d, in a real browser: where a client lands (C01), whether the error summary's "Service" link really moves
// focus to the service picker (C04), whether Space opens the file chooser from the upload button (C06), and whether
// the calendar file really downloads from the client's own page (AP10). jsdom can see none of these for certain.

const json = (body: unknown) => ({ status: 200, contentType: "application/json", body: JSON.stringify(body) });

async function signInAsClient(page: Page, opts: { services?: boolean; bookings?: unknown[] } = {}) {
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.addInitScript(() => localStorage.setItem("cookie-consent", "accepted"));
  await mockApiFallback(page);
  await mockStudioZone(page);
  await page.route("**/api/v1/appointments/mine", (route) => route.fulfill(json(opts.bookings ?? [])));
  await page.route("**/api/v1/artists", (route) => route.fulfill(json([
    { id: ARTIST_ID, firstName: "Rafaela", lastName: "Costa", specializations: [], isActive: true },
  ])));
  if (opts.services) {
    await page.route("**/api/v1/services", (route) => route.fulfill(json([{
      id: "svc-1", studioId: STUDIO_ID, name: "Large colour session", description: null, durationMinutes: 120,
      price: null, depositAmount: null, isActive: true, createdAt: "2024-01-01T00:00:00Z", updatedAt: "2024-01-01T00:00:00Z",
    }])));
  }
  await loginAs(page, CLIENT_TOKEN, "ana@example.com", /\/my-appointments$/);
}

test("C01: a client signs in to their bookings, and a first visit offers booking as the primary action", async ({ page }) => {
  await signInAsClient(page);

  await expect(page.getByRole("heading", { name: "My bookings" })).toBeVisible();
  await expect(page.getByText("No bookings yet")).toBeVisible();
  const book = page.getByRole("main").getByRole("link", { name: "Book an appointment" });
  await expect(book).toBeVisible();
  // The menu marks the client's home as the current page.
  await expect(page.getByRole("navigation").first().getByRole("link", { name: "My bookings" })).toHaveAttribute("aria-current", "page");

  await book.click();
  await expect(page).toHaveURL(/\/book$/);
});

test("C04: with services, nothing is assumed, and the error summary's Service link puts focus on the picker", async ({ page }) => {
  await signInAsClient(page, { services: true });
  await page.goto("/book");

  const picker = page.getByRole("combobox", { name: /service/i });
  await expect(picker).toHaveText(/Choose a service/);
  await expect(page.getByText("Choose a service to see open times.")).toBeVisible();

  await page.getByRole("button", { name: /request appointment/i }).click();
  const summary = page.getByRole("alert").filter({ hasText: /things? to fix/i });
  await summary.getByRole("link", { name: /service/i }).click();
  await expect(picker).toBeFocused();
});

test("C06: Space on the upload button activates it and opens the file input, as a button should", async ({ page }) => {
  await signInAsClient(page);
  await page.goto("/book");

  // Wait for the form to settle (the studio's zone and the artists load), so focus isn't lost to a re-render.
  await expect(page.getByRole("combobox", { name: "Select artist" })).toBeVisible();
  // Count clicks reaching the hidden file input: that is the app's half. Whether headless Chromium then shows its
  // native dialog was flaky (a "filechooser" wait passed in one theme and timed out in the other in the same run).
  await page.evaluate(() => {
    const input = document.getElementById("images-AreaPhoto-file");
    if (!input) throw new Error("no file input");
    (window as unknown as { fileInputClicks: number }).fileInputClicks = 0;
    input.addEventListener("click", (e) => {
      e.preventDefault(); // don't open the native dialog
      (window as unknown as { fileInputClicks: number }).fileInputClicks += 1;
    });
  });
  const clicks = () => page.evaluate(() => (window as unknown as { fileInputClicks: number }).fileInputClicks);

  const upload = page.getByRole("button", { name: "Area photo" });
  await upload.focus();
  await expect(upload).toBeFocused();
  await page.keyboard.press("Space");
  await expect.poll(clicks).toBe(1);
  await page.keyboard.press("Enter");
  await expect.poll(clicks).toBe(2);
});

test("AP10: a confirmed appointment's own page downloads the calendar file", async ({ page }) => {
  await signInAsClient(page);
  const start = new Date(Date.now() + 7 * 86_400_000);
  const end = new Date(start.getTime() + 2 * 3_600_000);
  await page.route(`**/api/v1/appointments/mine/${APPT_ID}`, (route) => route.fulfill(json({
    appointment: {
      id: APPT_ID, studioId: STUDIO_ID, artistId: ARTIST_ID, clientId: CLIENT_ID, date: start.toISOString(),
      endDate: end.toISOString(), durationMinutes: 120, status: "Confirmed", depositStatus: "Paid", depositAmount: 0,
      notes: null, createdAt: start.toISOString(), artistName: "Rafaela Costa", serviceName: "Large colour session",
    },
    readiness: [],
    contact: { recipientUserId: null, recipientRole: null, recipientName: null, studioName: "Tinta & Alma", studioPhone: null, studioInstagram: null },
    visit: { addressComplete: false, addressLine1: null, addressLine2: null, city: null, postalCode: null },
  })));
  let requested = "";
  await page.route(`**/api/v1/appointments/${APPT_ID}/calendar.ics`, (route) => {
    requested = route.request().url();
    return route.fulfill({ status: 200, contentType: "text/calendar; charset=utf-8", body: "BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n" });
  });
  await page.goto(`/my-appointments/${APPT_ID}`);

  const download = page.waitForEvent("download");
  await page.getByRole("button", { name: "Add to calendar" }).click();
  expect((await download).suggestedFilename()).toBe(`appointment-${APPT_ID}.ics`);
  expect(requested).toContain(`/appointments/${APPT_ID}/calendar.ics`);
});
