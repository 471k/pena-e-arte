import { describe, it, expect, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";

import authReducer from "@/features/auth/authSlice";
import { PaymentResultPage } from "@/features/payments/components/PaymentResultPage";
import { Role } from "@/shared/types/roles";

const PAYMENT = "3f2b8c1e-1a2b-4c3d-8e9f-0a1b2c3d4e5f";

function renderAt(search: string, role: string | null) {
  const store = configureStore({
    reducer: { auth: authReducer },
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: role ? { id: "u1", email: "c@test.com" } : null, token: role ? "t" : null, tenantId: role ? "s1" : null, role, pendingReferralCode: null, impersonation: null } as any,
    },
  });
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[`/payment-result${search}`]}>
        <PaymentResultPage />
      </MemoryRouter>
    </Provider>,
  );
}

afterEach(cleanup);

describe("PaymentResultPage", () => {
  it("paid: says the payment went through and links a client to My bookings", () => {
    renderAt(`?status=paid&payment=${PAYMENT}&orderId=abc`, Role.Client);
    expect(screen.getByRole("heading", { name: /payment received/i })).toBeInTheDocument();
    expect(screen.getByText(/your card has been charged/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /go to my bookings/i })).toHaveAttribute("href", "/book");
    expect(screen.queryByRole("link", { name: /try again/i })).not.toBeInTheDocument();
  });

  it("failed: says nothing was charged and offers a signed-in payer Try again on the same payment", () => {
    renderAt(`?status=failed&payment=${PAYMENT}&orderId=abc`, Role.Client);
    expect(screen.getByRole("heading", { name: /payment not completed/i })).toBeInTheDocument();
    expect(screen.getByText(/nothing was charged/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /try again/i })).toHaveAttribute("href", `/pay/${PAYMENT}`);
  });

  it("failed for a guest: no Try again (the checkout needs a sign-in), just a way back", () => {
    renderAt(`?status=failed&payment=${PAYMENT}`, null);
    expect(screen.queryByRole("link", { name: /try again/i })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: /back to tattooos/i })).toHaveAttribute("href", "/");
  });

  it("ignores a payment id that isn't a GUID", () => {
    renderAt("?status=failed&payment=../../admin", Role.Client);
    expect(screen.queryByRole("link", { name: /try again/i })).not.toBeInTheDocument();
  });

  it("unknown status: neutral message", () => {
    renderAt("", Role.Client);
    expect(screen.getByRole("heading", { name: /^payment$/i })).toBeInTheDocument();
    expect(screen.getByText(/couldn.t tell how your payment ended/i)).toBeInTheDocument();
  });
});
