import { describe, it, expect, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import type { ReactElement } from "react";

import authReducer from "@/features/auth/authSlice";
import { UseCaseBookingPage } from "@/features/public/components/UseCaseBookingPage";
import { UseCaseDepositsPage } from "@/features/public/components/UseCaseDepositsPage";
import { UseCaseConsentFormsPage } from "@/features/public/components/UseCaseConsentFormsPage";

function renderPage(ui: ReactElement) {
  const store = configureStore({
    reducer: { auth: authReducer },
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: null, token: null, tenantId: null, role: null, pendingReferralCode: null, impersonation: null } as any,
    },
  });
  return render(
    <Provider store={store}>
      <MemoryRouter>{ui}</MemoryRouter>
    </Provider>,
  );
}

const CASES: ReadonlyArray<readonly [string, ReactElement, string, string]> = [
  ["booking", <UseCaseBookingPage key="b" />, "Online booking for tattoo studios", "Online booking for tattoo studios — TattooOS"],
  ["deposits", <UseCaseDepositsPage key="d" />, "Deposits for tattoo bookings", "Tattoo deposits without the chasing — TattooOS"],
  ["consent forms", <UseCaseConsentFormsPage key="c" />, "Digital consent forms", "Digital consent forms for tattoo studios — TattooOS"],
];

describe("use-case marketing pages", () => {
  afterEach(cleanup);

  it.each(CASES)("%s page renders its heading, document title and closing links", (_name, ui, heading, title) => {
    renderPage(ui);

    expect(screen.getByRole("heading", { level: 1, name: heading })).toBeInTheDocument();
    expect(document.title).toBe(title);
    expect(screen.getByRole("link", { name: /see pricing/i })).toHaveAttribute("href", "/pricing");
    expect(screen.getByRole("link", { name: /register your studio/i })).toHaveAttribute("href", "/register");
  });

  it("deposits page states there is no commission and does not name a card processor", () => {
    renderPage(<UseCaseDepositsPage />);

    expect(screen.getByText(/no commission on\s+bookings/i)).toBeInTheDocument();
    expect(screen.queryByText(/stripe/i)).not.toBeInTheDocument();
  });
});
