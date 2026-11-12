import { test, expect } from "@playwright/test";
import { STUDIO_ID, OWNER_TOKEN, mockApiFallback, loginAs } from "./helpers";

// E05b-2 in a real browser. O06: the "Not settled yet" card on Payments fits a 390px phone and a desktop,
// starts with the disagreement open and the routine groups closed, and opens a group on click. O07: the
// cancel dialog is a Radix AlertDialog (jsdom doesn't lay it out); "Cancel plan" stays disabled until a
// quote has loaded, and a changed quote cancels nothing and shows the new figure.

const json = (body: unknown, status = 200) => ({ status, contentType: "application/json", body: JSON.stringify(body) });
const hours = (h: number) => new Date(Date.now() + h * 3_600_000).toISOString();

const STUDIO = {
  id: STUDIO_ID, name: "Tinta & Alma", slug: "tinta-alma", isActive: true, timezone: "Europe/Tirane",
  countryCode: "PT", currency: "EUR", trialExpiresAt: hours(14 * 24), createdAt: new Date().toISOString(),
};

const ITEM = {
  appointmentId: "appt-1", appointmentDate: hours(5 * 24), appointmentStatus: "Confirmed", amount: 50, currency: "EUR",
  createdAt: hours(-1), releaseDeadline: null, providerCheckedAt: null, providerStatusAtCheck: null, checkOverdue: false,
};

const UNSETTLED = {
  generatedAt: new Date().toISOString(), checkOverdueAfterHours: 26, maxItemsPerGroup: 10,
  groups: [
    { kind: "ProviderDisagrees", total: 1, items: [{ ...ITEM, paymentId: "p-d", clientName: "Dora Disagree", status: "Pending", providerCheckedAt: hours(-2), providerStatusAtCheck: "Refunded" }] },
    { kind: "OnHold", total: 1, items: [{ ...ITEM, paymentId: "p-h", clientName: "Hana Held", status: "Captured", providerCheckedAt: hours(-30), providerStatusAtCheck: "Authorized", checkOverdue: true }] },
    { kind: "AwaitingCardPayment", total: 1, items: [{ ...ITEM, paymentId: "p-a", clientName: "Ari Awaiting", status: "Pending", releaseDeadline: hours(23) }] },
  ],
};

for (const width of [390, 1280]) {
  test(`Payments: "Not settled yet" fits and discloses at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await mockApiFallback(page);
    await page.route("**/api/v1/studios/me", (r) => r.fulfill(json(STUDIO)));
    await page.route("**/api/v1/payments/unsettled", (r) => r.fulfill(json(UNSETTLED)));
    await loginAs(page, OWNER_TOKEN, "owner@tinta-alma.com", /^(?!.*\/login).+/);
    await page.goto("/payments");

    const card = page.getByTestId("unsettled-payments");
    await expect(card.getByText("Dora Disagree")).toBeVisible();
    await expect(card.getByText("Ari Awaiting")).toBeHidden();

    await card.getByRole("button", { name: /waiting for the client to pay/i }).click();
    await expect(card.getByText("Ari Awaiting")).toBeVisible();
    await expect(card.getByTestId("unsettled-group-AwaitingCardPayment").getByTestId("unsettled-check"))
      .toContainText("Cancelled automatically after");

    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow, "the page scrolls sideways").toBeLessThanOrEqual(0);
  });
}

const PLANS = [{
  id: "plan-2", name: "Pro", yearlyDiscountPercent: 17, currency: "EUR", yearlySavingAmount: null, yearlyMonthsFree: null,
  allowBrandingRemoval: true, subscriberCount: 0, maxArtists: null, maxAppointmentsPerMonth: null, maxNotificationsPerMonth: null,
  maxStorageGb: null, maxLocations: null, allowApiAccess: false, prioritySupport: false, allowMarketingCampaigns: false,
  prices: [
    { id: "p2-m", interval: "Monthly", price: 79, stripePriceId: "price_m", isActive: true },
    { id: "p2-y", interval: "Yearly", price: 790, stripePriceId: "price_y", isActive: true },
  ],
}];

const YEARLY_SUB = {
  id: "sub-1", studioId: STUDIO_ID, planId: "plan-2", billingInterval: "Yearly", pendingPlanId: null, pendingBillingInterval: null,
  status: "Active", trialExpiresAt: null, currentPeriodEnd: hours(9 * 30 * 24), gracePeriodEnd: hours(7 * 24),
  stripeSubscriptionId: "sub_stripe", cancelAtPeriodEnd: false,
};

const QUOTE = {
  billingInterval: "Yearly", refundAmount: 553, monthsUsed: 3, accessEndDate: new Date().toISOString(),
  amountPaid: 790, monthlyReferencePrice: 79, invoiceId: "inv-1",
};

async function openBilling(page: import("@playwright/test").Page, quote: () => { status: number; body: unknown }) {
  await page.setViewportSize({ width: 390, height: 900 });
  await page.route("**/api/v1/studios/me", (r) => r.fulfill(json(STUDIO)));
  await page.route("**/api/v1/billing/plans", (r) => r.fulfill(json(PLANS)));
  await page.route("**/api/v1/billing/usage", (r) => r.fulfill(json(null)));
  await page.route("**/api/v1/billing/subscription", (r) => r.fulfill(json(YEARLY_SUB)));
  await page.route("**/api/v1/billing/subscription/cancel/quote", (r) => {
    const q = quote();
    return r.fulfill(json(q.body, q.status));
  });
  await loginAs(page, OWNER_TOKEN, "owner@tinta-alma.com", /^(?!.*\/login).+/);
  await page.goto("/billing");
  await page.getByRole("button", { name: "Cancel plan" }).click();
  return page.getByRole("alertdialog");
}

test("Cancel plan: no quote, no confirmation", async ({ page }) => {
  await mockApiFallback(page); // first, so the routes below win
  let posts = 0;
  await page.route("**/api/v1/billing/subscription/cancel", (r) => {
    if (r.request().method() === "POST") posts += 1;
    return r.fulfill(json({}));
  });
  const dialog = await openBilling(page, () => ({ status: 500, body: { message: "boom" } }));

  await expect(dialog.getByText(/can't be cancelled yet/i)).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Cancel plan" })).toBeDisabled();
  await expect(dialog.getByRole("button", { name: /try again/i })).toBeVisible();
  expect(posts).toBe(0);
});

test("Cancel plan: a changed quote cancels nothing and shows the new refund", async ({ page }) => {
  await mockApiFallback(page); // first, so the routes below win
  let quoteCalls = 0;
  const posted: unknown[] = [];
  await page.route("**/api/v1/billing/subscription/cancel", async (r) => {
    if (r.request().method() !== "POST") return r.fulfill(json({}));
    posted.push(r.request().postDataJSON());
    return r.fulfill(json({ status: 409, message: "Your refund changed.", code: "CANCELLATION_QUOTE_CHANGED" }, 409));
  });
  const dialog = await openBilling(page, () => {
    quoteCalls += 1;
    return { status: 200, body: quoteCalls === 1 ? QUOTE : { ...QUOTE, refundAmount: 474, monthsUsed: 4 } };
  });

  await expect(dialog.getByText(/553/)).toBeVisible();
  await dialog.getByRole("button", { name: "Cancel plan" }).click();

  await expect(dialog.getByTestId("quote-changed")).toBeVisible();
  await expect(dialog.getByText(/474/).first()).toBeVisible();
  await expect(dialog).toBeVisible();
  expect(posted[0]).toEqual({ billingInterval: "Yearly", refundAmount: 553, monthsUsed: 3, amountPaid: 790, invoiceId: "inv-1" });
});
