import { describe, it, expect, afterEach, vi } from "vitest";
import { render, screen, cleanup, within } from "@testing-library/react";
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
  ClientTermsPage,
  RefundPolicyPage,
  ContactPage,
} from "@/features/public";
import { FINAL_LEGAL_COPY, HAS_FINAL_LEGAL_COPY } from "@/features/public/legal/legalFlags";

function renderPublic(ui: ReactElement) {
  const store = configureStore({
    reducer: { auth: authReducer, [contactApi.reducerPath]: contactApi.reducer },
    middleware: (getDefault) => getDefault().concat(contactApi.middleware),
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

describe("public content pages", () => {
  afterEach(cleanup);

  it.each([
    ["Home", <HomePage key="h" />],
    ["Privacy", <PrivacyPolicyPage key="p" />],
    ["Terms", <TermsOfServicePage key="t" />],
    ["Client Terms", <ClientTermsPage key="ct" />],
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

  it("Home links to the marketing pages in an Explore nav", () => {
    renderPublic(<HomePage />);
    const explore = screen.getByRole("navigation", { name: "Explore" });
    expect(within(explore).getByRole("link", { name: "Features" })).toHaveAttribute("href", "/features");
    expect(within(explore).getByRole("link", { name: "Pricing" })).toHaveAttribute("href", "/pricing");
    expect(within(explore).getByRole("link", { name: "FAQ" })).toHaveAttribute("href", "/faq");
  });

  it("Home does not repeat the footer's policy links in its own body (found 2026-09-27, both were visible on screen at once)", () => {
    renderPublic(<HomePage />);
    // The footer (SiteFooter, asserted elsewhere by role "contentinfo") already carries these —
    // Home's own content should link to Privacy/Terms/Refund/Contact exactly once, from the footer.
    expect(screen.getAllByRole("link", { name: "Privacy Policy" })).toHaveLength(1);
    expect(screen.getAllByRole("link", { name: "Studio Terms" })).toHaveLength(1);
    expect(screen.getAllByRole("link", { name: "Client Terms" })).toHaveLength(1);
    expect(screen.getAllByRole("link", { name: "Refund Policy" })).toHaveLength(1);
    expect(screen.getAllByRole("link", { name: "Contact" })).toHaveLength(1);
  });

  it("Privacy Policy names special-category health data, the roles and the sub-processors", () => {
    renderPublic(<PrivacyPolicyPage />);
    // The page writes "Health data (special category)"; GDPR Art. 9 term, with or without the hyphen.
    expect(screen.getAllByText(/Health data \(special.category\)/i).length).toBeGreaterThan(0);
    expect(screen.getByText(/The studio is the controller/i)).toBeInTheDocument();
    expect(screen.getByText(/Payment provider \(POK\)/i)).toBeInTheDocument();
    expect(screen.getByText(/Hetzner \(Germany\)/i)).toBeInTheDocument();
    // The old "standard body-art record-retention period" sentence was unsupported and must stay gone.
    expect(screen.queryByText(/standard body-art record-retention/i)).not.toBeInTheDocument();
  });

  it("the four legal pages carry no lawyer-review banner while their flags are final, and link Client Terms once", () => {
    expect(HAS_FINAL_LEGAL_COPY).toBe(true);
    expect(FINAL_LEGAL_COPY).toEqual({ terms: true, clientTerms: true, privacy: true });
    for (const ui of [
      <PrivacyPolicyPage key="p" />,
      <TermsOfServicePage key="t" />,
      <ClientTermsPage key="c" />,
      <RefundPolicyPage key="r" />,
    ]) {
      renderPublic(ui);
      expect(screen.queryByText(/\[LAWYER REVIEW REQUIRED\]/i)).not.toBeInTheDocument();
      // The amber banner is the only role="alert" these pages can render.
      expect(screen.queryByRole("alert")).not.toBeInTheDocument();
      expect(screen.getAllByRole("link", { name: "Client Terms" })).toHaveLength(1);
      cleanup();
    }
  });

  it("Platform Terms state the lek prices, the 14-day trial and the yearly refund example", () => {
    renderPublic(<TermsOfServicePage />);
    expect(screen.getByText(/Solo 2,000, Studio 4,500 and Studio Plus 9,000 ALL/)).toBeInTheDocument();
    expect(screen.getByText(/14-day trial with all features and no card/)).toBeInTheDocument();
    // All four points of the 45,000 ALL example in Platform Terms §8, in the order the clause states them.
    expect(
      screen.getByText(
        /Cancelling in month 1 refunds 40,500, in month 3 refunds 31,500, in month 6 refunds 18,000, and from month 10 refunds nothing/,
      ),
    ).toBeInTheDocument();
    expect(screen.getByText(/within 48 hours of confirming it/)).toBeInTheDocument();
  });

  it("Client Terms say the studio, not TattooOS, is the service provider", () => {
    renderPublic(<ClientTermsPage />);
    expect(screen.getByText(/your agreement for the tattoo is with the studio, not with TattooOS/)).toBeInTheDocument();
  });

  it("Refund Policy states the 48-hour default window and deposit facts from the live code", () => {
    renderPublic(<RefundPolicyPage />);
    expect(screen.getByText(/48 hours/i)).toBeInTheDocument();
    expect(screen.getByText(/refunded in full \(100%\)/i)).toBeInTheDocument();
    expect(screen.getAllByText(/the deposit is forfeited/i).length).toBeGreaterThan(0);
    // Corrected 12 Nov 2026 to match the app: charged up front, and rescheduling never costs the
    // deposit (a client simply can't self-reschedule inside the window).
    expect(screen.getByText(/charged in full when you pay it/i)).toBeInTheDocument();
    expect(screen.getByText(/moving it costs nothing/i)).toBeInTheDocument();
    expect(screen.getByText(/Inside the notice window you can.t reschedule yourself/i)).toBeInTheDocument();
    expect(screen.queryByText(/cancel or reschedule inside the notice window/i)).not.toBeInTheDocument();
    expect(screen.getByText(/refunded in full,\s+whatever the timing/i)).toBeInTheDocument();
    // Refund policy is REAL copy, so it must NOT carry the lawyer-review banner.
    expect(screen.queryByText(/\[LAWYER REVIEW REQUIRED\]/i)).not.toBeInTheDocument();
  });

  // /terms is the studio B2B contract (clause 3: "You are not a consumer for these terms"). Someone who
  // arrives there by a direct link is out of reach of any relabelled link, so each terms page names
  // its audience and points at the other one (27 Nov 2026).
  it("/terms tells a non-studio reader these terms are not theirs and links the Client Terms of Use", () => {
    renderPublic(<TermsOfServicePage />);
    expect(screen.getByText(/Not a studio\? These terms do not apply to you/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Client Terms of Use" })).toHaveAttribute("href", "/client-terms");
  });

  it("/client-terms tells a studio reader these terms are not theirs and links the Studio Terms", () => {
    renderPublic(<ClientTermsPage />);
    expect(screen.getByText(/Running a studio\? These terms do not apply to you/)).toBeInTheDocument();
    const main = screen.getByRole("heading", { level: 1, name: "Client Terms of Use" }).parentElement!;
    expect(within(main).getByRole("link", { name: "Studio Terms" })).toHaveAttribute("href", "/terms");
  });
});
