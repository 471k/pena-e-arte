import { describe, it, expect, afterEach, vi } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import type { ReactElement } from "react";

import authReducer from "@/features/auth/authSlice";
import { contactApi } from "@/features/public/contactApi";
import {
  HomePage,
  PrivacyPolicyPage,
  TermsOfServicePage,
  RefundPolicyPage,
  ContactPage,
} from "@/features/public";

function renderPublic(ui: ReactElement) {
  const store = configureStore({
    reducer: { auth: authReducer, [contactApi.reducerPath]: contactApi.reducer },
    middleware: (getDefault) => getDefault().concat(contactApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: null, token: null, tenantId: null, role: null, pendingReferralCode: null } as any,
    },
  });
  return render(
    <Provider store={store}>
      <MemoryRouter>{ui}</MemoryRouter>
    </Provider>,
  );
}

describe("public content pages", () => {
  afterEach(cleanup);

  it.each([
    ["Home", <HomePage key="h" />],
    ["Privacy", <PrivacyPolicyPage key="p" />],
    ["Terms", <TermsOfServicePage key="t" />],
    ["Refund", <RefundPolicyPage key="r" />],
    ["Contact", <ContactPage key="c" />],
  ])("%s renders with the public header and site footer and no console errors", (_name, ui) => {
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    renderPublic(ui);
    expect(screen.getByRole("banner")).toBeInTheDocument(); // PublicPageHeader
    expect(screen.getByRole("contentinfo")).toBeInTheDocument(); // SiteFooter
    expect(errorSpy).not.toHaveBeenCalled();
    errorSpy.mockRestore();
  });

  it("Privacy Policy names special-category health data and the planned payment sub-processors", () => {
    renderPublic(<PrivacyPolicyPage />);
    expect(screen.getAllByText(/special-category/i).length).toBeGreaterThan(0);
    expect(screen.getByText(/medical notes and allergies/i)).toBeInTheDocument();
    expect(screen.getByText(/POK, easyPos, Polar/i)).toBeInTheDocument();
    expect(screen.getByText(/\[LAWYER REVIEW REQUIRED\]/i)).toBeInTheDocument();
  });

  it("Refund Policy states the 24-hour default window and deposit forfeiture facts from the live code", () => {
    renderPublic(<RefundPolicyPage />);
    expect(screen.getByText(/48 hours/i)).toBeInTheDocument();
    expect(screen.getByText(/refunded in full \(100%\)/i)).toBeInTheDocument();
    expect(screen.getAllByText(/the deposit is forfeited/i).length).toBeGreaterThan(0);
    // Refund policy is REAL copy, so it must NOT carry the lawyer-review banner.
    expect(screen.queryByText(/\[LAWYER REVIEW REQUIRED\]/i)).not.toBeInTheDocument();
  });
});
