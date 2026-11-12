import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse, delay } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { UnsettledPaymentsSection } from "@/features/payments/components/UnsettledPaymentsSection";
import type { UnsettledPaymentItem, UnsettledPaymentsResponse } from "@/features/payments/payment.types";

const NOW = new Date();
const hoursFromNow = (h: number) => new Date(NOW.getTime() + h * 3_600_000).toISOString();

const BASE_ITEM: UnsettledPaymentItem = {
  paymentId:             "pay-1",
  appointmentId:         "appt-1",
  appointmentDate:       hoursFromNow(5 * 24),
  appointmentStatus:     "Confirmed",
  clientName:            "Maria Silva",
  amount:                50,
  currency:              "EUR",
  status:                "Pending",
  createdAt:             hoursFromNow(-1),
  releaseDeadline:       null,
  providerCheckedAt:     null,
  providerStatusAtCheck: null,
  checkOverdue:          false,
};

const RESPONSE: UnsettledPaymentsResponse = {
  generatedAt:            NOW.toISOString(),
  checkOverdueAfterHours: 26,
  maxItemsPerGroup:       10,
  groups: [
    {
      kind: "ProviderDisagrees", total: 1,
      items: [{ ...BASE_ITEM, paymentId: "pay-d", clientName: "Dora Disagree", providerCheckedAt: hoursFromNow(-2), providerStatusAtCheck: "Refunded" }],
    },
    {
      kind: "OnHold", total: 14,
      items: [{ ...BASE_ITEM, paymentId: "pay-h", clientName: "Hana Held", status: "Captured", providerCheckedAt: hoursFromNow(-30), providerStatusAtCheck: "Authorized", checkOverdue: true }],
    },
    {
      kind: "AwaitingCardPayment", total: 2,
      items: [
        { ...BASE_ITEM, paymentId: "pay-a", clientName: "Ari Awaiting", releaseDeadline: hoursFromNow(23) },
        { ...BASE_ITEM, paymentId: "pay-x", clientName: "Xena Expired", releaseDeadline: hoursFromNow(-1) },
      ],
    },
  ],
};

const server = setupServer(
  http.get("http://localhost/api/v1/payments/unsettled", () => HttpResponse.json(RESPONSE)),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

function renderSection() {
  const store = configureStore({
    reducer: { auth: authReducer, ui: uiReducer, [paymentsApi.reducerPath]: paymentsApi.reducer },
    middleware: (gd) => gd().concat(paymentsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u-001", email: "owner@test.com" }, token: "fake-token", tenantId: "s-001", role: "owner", pendingReferralCode: null, impersonation: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null, impersonationScopeError: null, impersonationSessionExpired: false },
    },
  });
  render(
    <Provider store={store}>
      <MemoryRouter>
        <UnsettledPaymentsSection />
      </MemoryRouter>
    </Provider>,
  );
}

describe("UnsettledPaymentsSection (E05b-2, O06)", () => {
  it("shows each group the server sent, in its order, with the server's count rather than the rows' length", async () => {
    renderSection();

    const groups = await screen.findAllByTestId(/^unsettled-group-/);
    expect(groups.map((g) => g.dataset.testid)).toEqual([
      "unsettled-group-ProviderDisagrees", "unsettled-group-OnHold", "unsettled-group-AwaitingCardPayment",
    ]);
    const held = screen.getByTestId("unsettled-group-OnHold");
    expect(within(held).getByRole("button", { name: /on hold, not charged yet \(14\)/i })).toBeInTheDocument();
    expect(within(held).getByTestId("unsettled-more")).toHaveTextContent("1 oldest shown of 14");
  });

  it("opens a disagreement by default and keeps routine groups collapsed until asked", async () => {
    renderSection();
    const disagrees = await screen.findByTestId("unsettled-group-ProviderDisagrees");
    expect(within(disagrees).getByRole("button", { expanded: true })).toBeInTheDocument();
    expect(within(disagrees).getByText("Dora Disagree")).toBeVisible();
    expect(within(disagrees).getByTestId("unsettled-check")).toHaveTextContent("The card provider said refunded");

    const awaiting = screen.getByTestId("unsettled-group-AwaitingCardPayment");
    const toggle = within(awaiting).getByRole("button", { expanded: false });
    // Collapsed with `hidden`: still in the document (find-in-page), just not shown.
    expect(within(awaiting).getByText("Ari Awaiting")).not.toBeVisible();

    await userEvent.setup().click(toggle);

    expect(toggle).toHaveAttribute("aria-expanded", "true");
    expect(within(awaiting).getByText("Ari Awaiting")).toBeVisible();
  });

  it("says when each row was last checked, flags an overdue check, and gives the cancellation deadline", async () => {
    renderSection();
    const held = await screen.findByTestId("unsettled-group-OnHold");
    expect(within(held).getByTestId("unsettled-check-overdue")).toBeInTheDocument();
    expect(within(held).getByText(/on hold, not charged/)).toBeInTheDocument();

    const awaiting = screen.getByTestId("unsettled-group-AwaitingCardPayment");
    const [notYet, expired] = within(awaiting).getAllByTestId("unsettled-check");
    expect(notYet).toHaveTextContent("Not checked with the card provider yet.");
    expect(notYet).toHaveTextContent("Cancelled automatically after");
    expect(expired).toHaveTextContent("Expired: it will be cancelled at the next check.");
  });

  it("never shows a total amount, and links cash to the dashboard instead of counting it", async () => {
    renderSection();
    await screen.findAllByTestId("unsettled-item");
    expect(screen.queryByText(/total/i)).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: /dashboard under needs you/i })).toHaveAttribute("href", "/dashboard");
  });

  it("each row opens its payment", async () => {
    renderSection();
    const disagrees = await screen.findByTestId("unsettled-group-ProviderDisagrees");
    expect(within(disagrees).getByTestId("unsettled-item")).toHaveAttribute("href", "/payments/appt-1");
  });

  it("loading, error and empty are three different answers", async () => {
    server.use(http.get("http://localhost/api/v1/payments/unsettled", async () => {
      await delay(50);
      return HttpResponse.json({ ...RESPONSE, groups: [] });
    }));
    renderSection();
    expect(screen.getByTestId("unsettled-loading")).toBeInTheDocument();
    expect(await screen.findByTestId("unsettled-empty")).toHaveTextContent("No card payments are waiting to settle.");
    expect(screen.queryByTestId("unsettled-error")).not.toBeInTheDocument();
  });

  it("an error offers a retry and never reads as 'nothing waiting'", async () => {
    let calls = 0;
    server.use(http.get("http://localhost/api/v1/payments/unsettled", () => {
      calls += 1;
      return calls === 1 ? HttpResponse.json({ message: "boom" }, { status: 500 }) : HttpResponse.json(RESPONSE);
    }));
    renderSection();

    const error = await screen.findByTestId("unsettled-error");
    expect(screen.queryByTestId("unsettled-empty")).not.toBeInTheDocument();
    await userEvent.setup().click(within(error).getByRole("button", { name: /try again/i }));

    expect(await screen.findByTestId("unsettled-group-OnHold")).toBeInTheDocument();
  });
});
