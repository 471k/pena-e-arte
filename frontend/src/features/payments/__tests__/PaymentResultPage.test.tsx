import { useLayoutEffect } from "react";
import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { delay, http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { PaymentResultPage } from "@/features/payments/components/PaymentResultPage";
import type { PaymentConfirmationResponse } from "@/features/payments/payment.types";
import { Role } from "@/shared/types/roles";

// C08: /payment-result says nothing about money until POST /payments/{id}/confirm answers. The URL's
// `status` (POK's redirect contract, typeable by anyone) must never decide a claim. These replace the
// five tests that asserted the opposite.

const PAYMENT = "3f2b8c1e-1a2b-4c3d-8e9f-0a1b2c3d4e5f";
const CONFIRM_URL = "http://localhost/api/v1/payments/:id/confirm";

function answer(fields: Partial<PaymentConfirmationResponse>): PaymentConfirmationResponse {
  return { paymentId: PAYMENT, status: "Pending", amount: 50, currency: "EUR", appointmentCancelled: false, ...fields };
}

let confirmCalls = 0;
let next: PaymentConfirmationResponse = answer({});

const server = setupServer(
  http.post(CONFIRM_URL, () => {
    confirmCalls += 1;
    return HttpResponse.json(next);
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  server.resetHandlers();
  cleanup();
  confirmCalls = 0;
  next = answer({});
});
afterAll(() => server.close());

// Records the page's text at its FIRST commit. A layout effect in a later sibling runs after the page's
// DOM is committed and before any passive effect (which is where the query subscribes), so this sees the
// very first paint. An assertion straight after render() cannot: RTL wraps render in act(), which has
// already flushed effects by the time it returns.
function FirstCommitProbe({ into }: { into: string[] }) {
  useLayoutEffect(() => {
    into.push(document.body.textContent ?? "");
  }, [into]);
  return null;
}

function renderAt(search: string, role: string | null = Role.Client, firstCommit?: string[]) {
  const store = configureStore({
    reducer: { auth: authReducer, ui: uiReducer, [paymentsApi.reducerPath]: paymentsApi.reducer },
    middleware: (gd) => gd().concat(paymentsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: role ? { id: "u1", email: "c@test.com" } : null, token: role ? "t" : null, tenantId: role ? "s1" : null, role, pendingReferralCode: null, impersonation: null } as any,
    },
  });
  return render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[`/payment-result${search}`]}>
        <PaymentResultPage />
        {firstCommit && <FirstCommitProbe into={firstCommit} />}
      </MemoryRouter>
    </Provider>,
  );
}

const heading = (name: RegExp) => screen.findByRole("heading", { level: 1, name });
const pageText = () => document.body.textContent ?? "";

describe("PaymentResultPage (C08)", () => {
  it("the URL says paid but the server says Pending: not confirmed yet, and no charge claim anywhere", async () => {
    next = answer({ status: "Pending" });
    renderAt(`?status=paid&payment=${PAYMENT}&orderId=abc`);

    expect(await heading(/payment not confirmed yet/i)).toBeInTheDocument();
    expect(pageText()).not.toMatch(/has been charged/i);
    expect(pageText()).not.toMatch(/payment received/i);
    expect(confirmCalls).toBe(1);
  });

  it("the URL says failed but the server says Paid: received, never 'nothing was charged', no Try again", async () => {
    next = answer({ status: "Paid" });
    renderAt(`?status=failed&payment=${PAYMENT}&orderId=abc`);

    expect(await heading(/payment received/i)).toBeInTheDocument();
    expect(pageText()).not.toMatch(/nothing was charged/i);
    expect(screen.queryByRole("link", { name: /try again/i })).not.toBeInTheDocument();
  });

  it("signed out: no call, an honest page, and a sign-in link back to this exact URL", async () => {
    const search = `?status=paid&payment=${PAYMENT}&orderId=abc`;
    renderAt(search, null);

    expect(await heading(/^payment$/i)).toBeInTheDocument();
    expect(screen.getByText(/can't confirm this payment here/i)).toBeInTheDocument();
    const expected = `/login?${new URLSearchParams({ redirect: `/payment-result${search}` }).toString()}`;
    expect(screen.getByRole("link", { name: /sign in/i })).toHaveAttribute("href", expected);
    expect(screen.getByRole("link", { name: /back to tattooos/i })).toHaveAttribute("href", "/");
    expect(screen.queryByRole("link", { name: /try again/i })).not.toBeInTheDocument();
    expect(pageText()).not.toMatch(/charged|received/i);
    // Give a would-be request time to go out before asserting none did.
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(confirmCalls).toBe(0);
  });

  describe.each([
    { row: 4, fields: { status: "Paid" },                                heading: /^payment received$/i,          money: /your card was charged €50\./i,                              tryAgain: false },
    { row: 5, fields: { status: "Captured" },                            heading: /^payment authorised$/i,        money: /authorised €50 and it's being held — not charged yet/i,     tryAgain: false },
    { row: 6, fields: { status: "Refunded", refundedAmount: 50 },        heading: /^payment refunded$/i,          money: /refunded — €50 went back to your card\. refunds usually/i,  tryAgain: false },
    { row: 6, fields: { status: "Refunded", refundedAmount: null },      heading: /^payment refunded$/i,          money: /has since been refunded\. refunds usually/i,                tryAgain: false },
    { row: 7, fields: { status: "Failed" },                              heading: /^payment not completed$/i,     money: /bank reported that this payment didn't go through/i,        tryAgain: "/book" },
    { row: 8, fields: { status: "Pending" },                             heading: /^payment not confirmed yet$/i, money: /haven't had confirmation from your bank yet/i,              tryAgain: `/pay/${PAYMENT}` },
    { row: 9, fields: { status: "CashPending" },                         heading: /^cash payment$/i,              money: /recorded as cash/i,                                         tryAgain: false },
    { row: 3, fields: { status: "Pending", appointmentCancelled: true }, heading: /^appointment cancelled$/i,     money: /being returned to your card automatically/i,                tryAgain: false },
    { row: 3, fields: { status: "Failed", appointmentCancelled: true },  heading: /^appointment cancelled$/i,     money: /being returned to your card automatically/i,                tryAgain: false },
  ] as const)("row $row ($fields.status)", ({ fields, heading: name, money, tryAgain }) => {
    it("renders its heading, its money sentence, and Try again only where paying again is right", async () => {
      next = answer(fields as Partial<PaymentConfirmationResponse>);
      renderAt(`?payment=${PAYMENT}`);

      expect(await heading(name)).toBeInTheDocument();
      expect(pageText()).toMatch(money);
      const retry = screen.queryByRole("link", { name: /try again/i });
      if (tryAgain) expect(retry).toHaveAttribute("href", tryAgain);
      else expect(retry).not.toBeInTheDocument();
      // Exactly one way to My bookings: the retry when it goes there, "Go to My bookings" otherwise.
      const toBookings = screen.getAllByRole("link").filter((a) => a.getAttribute("href") === "/book");
      expect(toBookings).toHaveLength(1);
      if (tryAgain !== "/book") expect(toBookings[0]).toHaveAccessibleName(/go to my bookings/i);
    });
  });

  it("Failed never sends anyone back to /pay/{id}, which refuses a Failed payment (Finding A)", async () => {
    next = answer({ status: "Failed" });
    renderAt(`?payment=${PAYMENT}`);

    await heading(/payment not completed/i);
    expect(screen.getByRole("link", { name: /try again in my bookings/i })).toHaveAttribute("href", "/book");
    expect(pageText()).toMatch(/"Pay deposit" on the booking in My bookings/);
    expect(screen.getAllByRole("link").map((a) => a.getAttribute("href"))).not.toContain(`/pay/${PAYMENT}`);
  });

  it("Failed seen by someone who isn't the client: no retry, because My bookings is the client's page", async () => {
    next = answer({ status: "Failed" });
    renderAt(`?payment=${PAYMENT}`, Role.Owner);

    await heading(/payment not completed/i);
    expect(screen.queryByRole("link", { name: /try again/i })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: /back to tattooos/i })).toHaveAttribute("href", "/");
  });

  it("Failed reports what the bank said, not a flat 'nothing was charged'", async () => {
    next = answer({ status: "Failed" });
    renderAt(`?payment=${PAYMENT}`);

    await heading(/payment not completed/i);
    expect(pageText()).not.toMatch(/nothing was charged/i);
    expect(pageText()).toMatch(/if a charge does appear, contact the studio/i);
  });

  it("a cancelled appointment outranks Paid: money coming back, never 'charged'", async () => {
    next = answer({ status: "Paid", appointmentCancelled: true });
    renderAt(`?status=paid&payment=${PAYMENT}`);

    expect(await heading(/appointment cancelled/i)).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: /payment received/i })).not.toBeInTheDocument();
    expect(pageText()).not.toMatch(/was charged|has been charged/i);
  });

  it("a payment id that isn't a GUID makes no call and says it couldn't check", async () => {
    renderAt("?status=paid&payment=../../admin");

    expect(await screen.findByText(/couldn't check how your payment ended/i)).toBeInTheDocument();
    expect(pageText()).not.toMatch(/received|charged/i);
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(confirmCalls).toBe(0);
  });

  it("the read fails: the neutral 'couldn't check' page, whatever the URL says", async () => {
    server.use(http.post(CONFIRM_URL, () => HttpResponse.json({ message: "boom" }, { status: 500 })));
    renderAt(`?status=paid&payment=${PAYMENT}`);

    expect(await screen.findByText(/couldn't check how your payment ended/i)).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent(/^Payment$/);
    expect(pageText()).not.toMatch(/received|charged/i);
  });

  it("while checking: announced as status, no claim, and no way to navigate away", async () => {
    server.use(http.post(CONFIRM_URL, async () => { await delay("infinite"); return HttpResponse.json(next); }));
    renderAt(`?status=paid&payment=${PAYMENT}`);

    const status = await screen.findByRole("status");
    expect(within(status).getByRole("heading", { level: 1, name: /checking your payment/i })).toBeInTheDocument();
    expect(screen.queryAllByRole("link")).toHaveLength(0);
    expect(screen.queryAllByRole("button")).toHaveLength(0);
    expect(pageText()).not.toMatch(/received|charged/i);
    expect(document.title).toBe("Payment — TattooOS");
  });

  it("the first paint, before the request is in flight, says it is checking and never 'couldn't check'", () => {
    server.use(http.post(CONFIRM_URL, async () => { await delay("infinite"); return HttpResponse.json(next); }));
    const firstCommit: string[] = [];
    renderAt(`?status=paid&payment=${PAYMENT}`, Role.Client, firstCommit);

    // Synchronous on purpose (Desktop's C08 review 1.2): findBy* retries, so it cannot rule out a first
    // render that showed the neutral block before the request started.
    expect(firstCommit.length).toBeGreaterThan(0);
    expect(firstCommit[0]).toMatch(/checking your payment/i);
    expect(firstCommit[0]).not.toMatch(/couldn't check/i);
    expect(firstCommit[0]).not.toMatch(/received|charged/i);
    expect(pageText()).toMatch(/checking your payment/i);
    expect(pageText()).not.toMatch(/couldn't check/i);
  });

  it("Refresh asks the server again and shows the new answer", async () => {
    next = answer({ status: "Pending" });
    renderAt(`?payment=${PAYMENT}`);
    await heading(/payment not confirmed yet/i);

    next = answer({ status: "Paid" });
    await userEvent.click(screen.getByRole("button", { name: /refresh/i }));

    expect(await heading(/payment received/i)).toBeInTheDocument();
    expect(confirmCalls).toBe(2);
  });

  it("one h1, decorative icon hidden, and the title follows the server's answer", async () => {
    next = answer({ status: "Paid" });
    const { container } = renderAt(`?payment=${PAYMENT}`);

    await heading(/payment received/i);
    expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
    const icons = container.querySelectorAll("svg");
    expect(icons.length).toBeGreaterThan(0);
    icons.forEach((icon) => expect(icon).toHaveAttribute("aria-hidden", "true"));
    expect(document.title).toBe("Payment received — TattooOS");
  });

  it("shows the lek frozen on the payment beside the euro amount, as the other payment screens do", async () => {
    next = answer({ status: "Paid", referenceAmount: 4537, referenceCurrency: "ALL", referenceRate: 90.74 });
    renderAt(`?payment=${PAYMENT}`);

    await heading(/payment received/i);
    expect(pageText()).toMatch(/your card was charged €50 \(ALL\s?4,537\)\./i);
  });
});
