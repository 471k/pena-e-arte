import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { delay, http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import type { ReactNode } from "react";

import authReducer from "@/features/auth/authSlice";
import { platformApi } from "@/features/platform/platformApi";
import { CancelSubscriptionReview } from "@/features/platform/components/CancelSubscriptionReview";
import { ExtendTrialReview } from "@/features/platform/components/ExtendTrialReview";
import { FailedRefundsCard, RecentRefundsList } from "@/features/platform/components/RefundInvestigation";
import { extendedTrialEnd, nextActionFor, refundFor } from "@/features/platform/components/subscriptionConsequences";
import type {
  AdminCancellationQuoteResponse,
  FailedSubscriptionRefundResponse,
  RecentRefundResponse,
} from "@/features/platform/platform.types";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

// Slice 8 phase 3: every admin subscription action reviews its consequence before it is confirmed (D03), and a failed
// refund is something an admin can find and act on (D07).

const QUOTE_URL  = "http://localhost/api/v1/platform/studios/:studioId/subscription/cancel-quote";
const CANCEL_URL = "http://localhost/api/v1/platform/subscriptions/:studioId/cancel";
const TRIAL_URL  = "http://localhost/api/v1/platform/subscriptions/:studioId/trial";
const FAILED_URL = "http://localhost/api/v1/platform/refunds/failed";

const YEARLY: AdminCancellationQuoteResponse = {
  status: "Active", planName: "Pro", billingInterval: "Yearly", billedThroughStripe: true, refundApplies: true,
  currency: "eur", formulaRefund: 553, fullRefund: 790, monthsUsed: 3, amountPaid: 790, invoiceId: "inv-1",
};

let quote: AdminCancellationQuoteResponse = YEARLY;
let quoteCalls = 0;

const server = setupServer(
  http.get(QUOTE_URL, () => {
    quoteCalls++;
    return HttpResponse.json(quote);
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); quote = YEARLY; quoteCalls = 0; vi.useRealTimers(); });
afterAll(() => server.close());

function renderWithStore(ui: ReactNode) {
  const store = configureStore({
    reducer: { auth: authReducer, [platformApi.reducerPath]: platformApi.reducer },
    middleware: (gd) => gd().concat(platformApi.middleware),
  });
  render(<Provider store={store}><MemoryRouter>{ui}</MemoryRouter></Provider>);
}

function renderCancel(onDone = vi.fn()) {
  renderWithStore(
    <CancelSubscriptionReview studioId="s1" studioName="Ink Soul" idSuffix="t" onDone={onDone} onBack={vi.fn()} />,
  );
  return { onDone, review: () => screen.getByRole("region", { name: /^cancel subscription$/i }) };
}

// ── D03: cancel ────────────────────────────────────────────────────────────────

describe("CancelSubscriptionReview", () => {
  it("says it is working out the consequence and allows no confirm until it has one", async () => {
    server.use(http.get(QUOTE_URL, async () => { await delay(50); return HttpResponse.json(YEARLY); }));
    const { review } = renderCancel();

    expect(within(review()).getByRole("status")).toHaveTextContent(/working out what cancelling will do/i);
    expect(within(review()).getByRole("button", { name: /^cancel subscription$/i })).toBeDisabled();
    expect(await within(review()).findByText(/a refund of €553 of the €790 paid \(3 months used\)/i)).toBeInTheDocument();
    expect(within(review()).getByRole("button", { name: /^cancel subscription$/i })).toBeEnabled();
  });

  it("states who, what happens and what can't be undone, in the refund's currency", async () => {
    const { review } = renderCancel();

    expect(await within(review()).findByText(/the pro \(yearly, now active\) is cancelled immediately/i)).toBeInTheDocument();
    expect(within(review()).getByText(/for ink soul/i)).toBeInTheDocument();
    expect(within(review()).getByText(/loses access straight away/i)).toBeInTheDocument();
    expect(within(review()).getByText(/its stripe subscription is cancelled too/i)).toBeInTheDocument();
    expect(within(review()).getByText(/a refund can't be recalled once it's sent to stripe/i)).toBeInTheDocument();
    expect(within(review()).getByRole("radio", { name: /use the formula: €553/i })).toBeChecked();
    expect(within(review()).getByRole("radio", { name: /full refund: €790/i })).toBeInTheDocument();
  });

  it("an override needs a reason, and the cancel sends back the amount for the rule chosen", async () => {
    let body: unknown = null;
    server.use(http.patch(CANCEL_URL, async ({ request }) => { body = await request.json(); return new HttpResponse(null, { status: 204 }); }));
    const user = userEvent.setup();
    const { onDone, review } = renderCancel();

    await user.click(await within(review()).findByRole("radio", { name: /full refund/i }));
    expect(within(review()).getByText(/a full refund of €790, by your override/i)).toBeInTheDocument();
    const confirm = within(review()).getByRole("button", { name: /^cancel subscription$/i });
    expect(confirm).toBeDisabled();

    await user.type(within(review()).getByLabelText(/reason/i), "Goodwill");
    await user.click(confirm);

    await waitFor(() => expect(onDone).toHaveBeenCalled());
    expect(body).toEqual({
      override: "AdminFull", reason: "Goodwill",
      confirmedStatus: "Active", confirmedInvoiceId: "inv-1", confirmedRefundAmount: 790,
    });
  });

  it("when the server says the refund changed: nothing is cancelled, the new figure is shown, choices are kept", async () => {
    server.use(http.patch(CANCEL_URL, () => HttpResponse.json(
      { status: 409, code: "CANCELLATION_QUOTE_CHANGED", message: "The subscription or its refund changed since you opened this. Nothing was cancelled." },
      { status: 409 },
    )));
    const user = userEvent.setup();
    const { onDone, review } = renderCancel();

    await user.click(await within(review()).findByRole("radio", { name: /full refund/i }));
    await user.type(within(review()).getByLabelText(/reason/i), "Goodwill");
    quote = { ...YEARLY, formulaRefund: 474, monthsUsed: 4 };
    await user.click(within(review()).getByRole("button", { name: /^cancel subscription$/i }));

    const alert = await within(review()).findByRole("alert");
    expect(alert).toHaveTextContent(/nothing was cancelled/i);
    await waitFor(() => expect(alert).toHaveTextContent(/what happens above is the new consequence/i));
    expect(quoteCalls).toBe(2);
    expect(within(review()).getByRole("radio", { name: /use the formula: €474/i })).toBeInTheDocument();
    expect(within(review()).getByRole("radio", { name: /full refund/i })).toBeChecked();
    expect(within(review()).getByLabelText(/reason/i)).toHaveValue("Goodwill");
    expect(within(review()).getByRole("button", { name: /^try again$/i })).toBeEnabled();
    expect(onDone).not.toHaveBeenCalled();
  });

  it("a quote the server refuses is shown as the reason, with no way to confirm", async () => {
    server.use(http.get(QUOTE_URL, () => HttpResponse.json(
      { status: 422, message: "A subscription with status 'Cancelled' cannot be cancelled." }, { status: 422 },
    )));
    const { review } = renderCancel();

    expect(await within(review()).findByRole("alert")).toHaveTextContent(/status 'cancelled' cannot be cancelled/i);
    expect(within(review()).getByText(/couldn't be worked out, so it can't be confirmed yet/i)).toBeInTheDocument();
    expect(within(review()).getByRole("button", { name: /^cancel subscription$/i })).toBeDisabled();
  });

  it("with nothing to refund, offers no refund rule and says why there is no refund", async () => {
    quote = { ...YEARLY, billingInterval: "Monthly", refundApplies: false, formulaRefund: 0, fullRefund: 0, invoiceId: null, amountPaid: null, monthsUsed: null };
    const { review } = renderCancel();

    expect(await within(review()).findByText(/no refund: only a yearly plan paid through stripe has one/i)).toBeInTheDocument();
    expect(within(review()).queryByRole("radio")).not.toBeInTheDocument();
    expect(within(review()).queryByText(/can't be recalled/i)).not.toBeInTheDocument();
  });
});

// ── D03: extend ────────────────────────────────────────────────────────────────

describe("ExtendTrialReview", () => {
  function renderExtend(status: string, currentTrialEnd: string | null) {
    renderWithStore(
      <ExtendTrialReview studioId="s1" studioName="Ink Soul" idSuffix="t" status={status}
        currentTrialEnd={currentTrialEnd} onDone={vi.fn()} onBack={vi.fn()} />,
    );
    return () => screen.getByRole("region", { name: /extend trial|grant trial extension/i });
  }

  it("a running trial is extended from its current end, and the review gives the new date", () => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-11-19T12:00:00Z"));
    const review = renderExtend("Trialing", "2026-11-25T09:00:00Z");

    expect(within(review()).getByText(/the trial ends 2 dec 2026, 09:00 utc instead of 25 nov 2026, 09:00 utc\./i)).toBeInTheDocument();
    expect(within(review()).getByText(/grace period .* ends 7 days later, 9 dec 2026, 09:00 utc/i)).toBeInTheDocument();
  });

  it("an ended trial is counted from now and gives the studio access again", () => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-11-19T12:00:00Z"));
    const review = renderExtend("Trialing", "2026-11-01T00:00:00Z");

    expect(within(review()).getByRole("heading", { name: /grant trial extension/i })).toBeInTheDocument();
    expect(within(review()).getByText(/ends 26 nov 2026, 12:00 utc .* counted from now/i)).toBeInTheDocument();
    expect(within(review()).getByText(/can use tattooos again until then/i)).toBeInTheDocument();
  });

  it("a failure keeps the days and says so", async () => {
    server.use(http.patch(TRIAL_URL, () => HttpResponse.json(
      { status: 500, message: "Something went wrong." }, { status: 500 },
    )));
    const user = userEvent.setup();
    const review = renderExtend("Trialing", "2099-01-01T00:00:00Z");

    await user.clear(within(review()).getByRole("spinbutton"));
    await user.type(within(review()).getByRole("spinbutton"), "14");
    await user.click(within(review()).getByRole("button", { name: /^extend trial$/i }));

    expect(await within(review()).findByRole("alert")).toHaveTextContent(/you can try again/i);
    expect(within(review()).getByRole("spinbutton")).toHaveValue(14);
    expect(within(review()).getByRole("button", { name: /^try again$/i })).toBeEnabled();
  });

  it("a refusal (the studio became past due after the page loaded) shows the server's next step and offers no retry", async () => {
    const message =
      "This studio's subscription payment is overdue, so a trial extension wouldn't restore access. "
      + "Access comes back when the overdue payment is paid (the owner updates their billing details), "
      + "or when you record a cash payment with Activate on the studio's page.";
    server.use(http.patch(TRIAL_URL, () => HttpResponse.json({ status: 422, message }, { status: 422 })));
    const user = userEvent.setup();
    const review = renderExtend("Trialing", "2099-01-01T00:00:00Z");

    await user.click(within(review()).getByRole("button", { name: /^extend trial$/i }));

    const alert = await within(review()).findByRole("alert");
    expect(alert).toHaveTextContent(/record a cash payment with activate/i);
    expect(alert).not.toHaveTextContent(/try again/i);
    expect(within(review()).queryByRole("button", { name: /^try again$/i })).not.toBeInTheDocument();
    expect(within(review()).getByRole("button", { name: /^extend trial$/i })).toBeDisabled();
    expect(within(review()).getByRole("button", { name: /back/i })).toBeEnabled();
  });
});

// ── D07: failed refunds ────────────────────────────────────────────────────────

const FAILED_RECENT: RecentRefundResponse = {
  amount: 99, currency: "usd", status: "Failed", createdAt: "2026-11-10T08:00:00Z", updatedAt: "2026-11-12T08:00:00Z",
  failureReason: "insufficient_funds", invoiceReference: "in_123", reachedStripe: true,
};

describe("RecentRefundsList", () => {
  it("shows a failed refund's reason, when it failed, its invoice and the next action as text, in its own currency", () => {
    render(<RecentRefundsList refunds={[FAILED_RECENT]} />);

    expect(screen.getByText(/\$99/)).toBeInTheDocument();          // not a hardcoded euro (evidence §0.3)
    expect(screen.queryByText(/€/)).not.toBeInTheDocument();
    expect(screen.getByText(/failed 12 nov 2026, 08:00 utc:/i)).toBeInTheDocument();
    expect(screen.getByText(/insufficient_funds/)).toBeInTheDocument(); // visible text, not a tooltip
    expect(screen.getByText(/^invoice in_123$/i)).toBeInTheDocument();
    expect(screen.getByText(/money went back to the platform's stripe balance/i)).toBeInTheDocument();
  });

  it("renders nothing when there are no refunds", () => {
    const { container } = render(<RecentRefundsList refunds={[]} />);
    expect(container).toBeEmptyDOMElement();
  });
});

describe("FailedRefundsCard", () => {
  const ITEM: FailedSubscriptionRefundResponse = {
    id: "r1", studioId: "s9", studioName: "Ink Soul", amount: 120, currency: "eur", rule: "YearlyFormula",
    createdAt: "2026-11-01T10:00:00Z", failedAt: "2026-11-01T10:00:00Z", failureReason: null,
    invoiceReference: "in_9", reachedStripe: false,
  };

  it("lists each failure with its studio, amount, reason and next action, and says when the list is capped", async () => {
    server.use(http.get(FAILED_URL, () => HttpResponse.json({ items: [ITEM], totalCount: 51 })));
    renderWithStore(<FailedRefundsCard />);

    expect(await screen.findByRole("link", { name: "Ink Soul" })).toHaveAttribute("href", "/platform/studios/s9");
    expect(screen.getByText("€120")).toBeInTheDocument();
    expect(screen.getByText(/stripe gave no reason/i)).toBeInTheDocument();
    expect(screen.getByText(/check invoice in_9 in stripe first/i)).toBeInTheDocument();
    expect(screen.getByText(/showing the newest 1 of 51 failed refunds/i)).toBeInTheDocument();
  });

  it("shows loading, then 'none' only when the server answered none", async () => {
    server.use(http.get(FAILED_URL, async () => { await delay(30); return HttpResponse.json({ items: [], totalCount: 0 }); }));
    renderWithStore(<FailedRefundsCard />);

    expect(screen.getByRole("status", { name: /loading failed refunds/i })).toBeInTheDocument();
    expect(await screen.findByText(/no failed refunds/i)).toBeInTheDocument();
  });

  it("a failed read is an error with Try again, never 'no failed refunds'", async () => {
    server.use(http.get(FAILED_URL, () => new HttpResponse(null, { status: 500 })));
    renderWithStore(<FailedRefundsCard />);

    expect(await screen.findByRole("alert")).toHaveTextContent(/couldn't load this list of failed refunds/i);
    expect(screen.getByRole("button", { name: /try again/i })).toBeInTheDocument();
    expect(screen.queryByText(/no failed refunds/i)).not.toBeInTheDocument();
  });
});

// ── the rules themselves ───────────────────────────────────────────────────────

describe("subscriptionConsequences", () => {
  it("refundFor follows the server: nothing when no refund applies, whatever the rule", () => {
    expect(refundFor(YEARLY, "formula")).toBe(553);
    expect(refundFor(YEARLY, "AdminFull")).toBe(790);
    expect(refundFor(YEARLY, "AdminNone")).toBe(0);
    const none = { ...YEARLY, refundApplies: false };
    expect(refundFor(none, "AdminFull")).toBe(0);
    expect(refundFor(none, "formula")).toBe(0);
  });

  it("extendedTrialEnd mirrors ExtendTrialHandler: from the end while running, from now once ended or unknown", () => {
    const now = new Date("2026-11-19T12:00:00Z");
    expect(extendedTrialEnd("2026-11-25T00:00:00Z", 7, now).toISOString()).toBe("2026-12-02T00:00:00.000Z");
    expect(extendedTrialEnd("2026-11-01T00:00:00Z", 7, now).toISOString()).toBe("2026-11-26T12:00:00.000Z");
    expect(extendedTrialEnd(null, 1, now).toISOString()).toBe("2026-11-20T12:00:00.000Z");
  });

  it("nextActionFor never tells an admin to refund again before checking, when the request itself failed", () => {
    expect(nextActionFor(false, "in_1")).toMatch(/check invoice in_1 in stripe first: if a refund is there, nothing more is needed/i);
    expect(nextActionFor(true, "in_1")).toMatch(/refund it again from invoice in_1/i);
  });
});
