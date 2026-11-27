import { describe, it, expect, vi, afterEach } from "vitest";
import { render, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import type { ReactElement } from "react";

import authReducer from "@/features/auth/authSlice";
import { contactApi } from "@/features/public/contactApi";
import {
  FeaturesPage,
  PricingPage,
  UseCaseBookingPage,
  UseCaseDepositsPage,
  UseCaseConsentFormsPage,
  FaqPage,
  ContactPage,
  PrivacyPolicyPage,
  TermsOfServicePage,
  ClientTermsPage,
  RefundPolicyPage,
} from "@/features/public";
import { ROUTE_META, SITE_URL } from "@/shared/seo/siteRoutes";
import type { StaticRoutePath } from "@/shared/seo/siteRoutes";

vi.mock("@/features/public/publicApi", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/public/publicApi")>();
  return {
    ...actual,
    useGetPublicPlansQuery: () => ({ data: [], isLoading: false, isError: false }),
  };
});

function renderPage(ui: ReactElement) {
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

function headContent(selector: string, attr: string): string | null {
  return document.head.querySelector(selector)?.getAttribute(attr) ?? null;
}

const PAGES: ReadonlyArray<readonly [StaticRoutePath, ReactElement]> = [
  ["/features", <FeaturesPage key="f" />],
  ["/pricing", <PricingPage key="p" />],
  ["/use/booking", <UseCaseBookingPage key="b" />],
  ["/use/deposits", <UseCaseDepositsPage key="d" />],
  ["/use/consent-forms", <UseCaseConsentFormsPage key="c" />],
  ["/faq", <FaqPage key="q" />],
  ["/contact", <ContactPage key="ct" />],
  ["/privacy", <PrivacyPolicyPage key="pv" />],
  ["/terms", <TermsOfServicePage key="t" />],
  ["/client-terms", <ClientTermsPage key="ct2" />],
  ["/refund-policy", <RefundPolicyPage key="r" />],
];

describe("static page metadata matches the route manifest", () => {
  afterEach(cleanup);

  it.each(PAGES)("%s: client-side title, description and canonical equal the manifest and the apex", (path, ui) => {
    renderPage(ui);
    const meta = ROUTE_META[path];

    expect(document.title).toBe(meta.title);
    expect(headContent('meta[name="description"]', "content")).toBe(meta.description);
    expect(headContent('link[rel="canonical"]', "href")).toBe(`${SITE_URL}${path}`);
    expect(headContent('meta[property="og:url"]', "content")).toBe(`${SITE_URL}${path}`);
  });

  // The test above checks the title against the MANIFEST and never against the page itself, so a page
  // headed "Platform Terms for Studios" shipped with the tab title "Terms of Service" and CI could not see
  // it (found 26-27 Nov 2026). This checks that the h1 and the route title name the same audience.
  // Deliberately the audience word, not string equality: the title carries " — TattooOS" and the h1 does
  // not, and an equality check would invite someone to weaken it.
  it.each([
    ["/terms", "Studio", <TermsOfServicePage key="t" />],
    ["/client-terms", "Client", <ClientTermsPage key="c" />],
  ] as const)("%s: the h1 and the route title both name the audience %s", (path, audience, ui) => {
    renderPage(ui);
    const h1 = document.querySelector("h1")?.textContent ?? "";
    expect(h1).toContain(audience);
    expect(ROUTE_META[path].title).toContain(audience);
    expect(document.title).toContain(audience);
  });

  it("names the apex as canonical regardless of the host the page is served from", () => {
    // jsdom serves from http://localhost; the canonical must still be the production apex.
    renderPage(<FeaturesPage />);

    expect(window.location.origin).not.toBe(SITE_URL);
    expect(headContent('link[rel="canonical"]', "href")).toBe(`${SITE_URL}/features`);
  });
});
