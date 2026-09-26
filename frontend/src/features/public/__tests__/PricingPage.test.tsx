import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";

import authReducer from "@/features/auth/authSlice";
import { PricingPage } from "@/features/public/components/PricingPage";
import type { PublicPlanResponse } from "@/features/public/publicApi";

const mockUseGetPublicPlansQuery = vi.fn();

vi.mock("@/features/public/publicApi", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/public/publicApi")>();
  return {
    ...actual,
    useGetPublicPlansQuery: (...args: unknown[]) => mockUseGetPublicPlansQuery(...args),
  };
});

const FREE: PublicPlanResponse = {
  name: "Free",
  currency: "EUR",
  prices: [{ interval: "Monthly", price: 0 }],
  yearlyMonthsFree: null,
  allowBrandingRemoval: false,
  allowMarketingCampaigns: false,
  allowApiAccess: false,
  maxArtists: 1,
  maxAppointmentsPerMonth: 15,
  maxNotificationsPerMonth: 50,
  maxStorageGb: 1,
  maxLocations: null,
};

const PREMIUM: PublicPlanResponse = {
  name: "Premium",
  currency: "EUR",
  prices: [
    { interval: "Monthly", price: 79 },
    { interval: "Yearly", price: 790 },
  ],
  yearlyMonthsFree: 2,
  allowBrandingRemoval: true,
  allowMarketingCampaigns: true,
  allowApiAccess: true,
  maxArtists: 10,
  maxAppointmentsPerMonth: 1000,
  maxNotificationsPerMonth: 2500,
  maxStorageGb: 50,
  maxLocations: null,
};

function renderPage() {
  const store = configureStore({
    reducer: { auth: authReducer },
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: null, token: null, tenantId: null, role: null, pendingReferralCode: null, impersonation: null } as any,
    },
  });
  return render(
    <Provider store={store}>
      <MemoryRouter>
        <PricingPage />
      </MemoryRouter>
    </Provider>,
  );
}

describe("PricingPage", () => {
  beforeEach(() => mockUseGetPublicPlansQuery.mockReset());
  afterEach(cleanup);

  it("shows a busy loading skeleton while the plans load", () => {
    mockUseGetPublicPlansQuery.mockReturnValue({ data: undefined, isLoading: true, isError: false });

    renderPage();

    const loading = screen.getByLabelText(/loading pricing/i);
    expect(loading).toHaveAttribute("aria-busy", "true");
    expect(screen.queryByRole("heading", { level: 2 })).not.toBeInTheDocument();
  });

  it("shows a friendly message with a contact link when the request fails", () => {
    mockUseGetPublicPlansQuery.mockReturnValue({ data: undefined, isLoading: false, isError: true });

    renderPage();

    expect(screen.getByText(/pricing is temporarily unavailable/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /contact us/i })).toHaveAttribute("href", "/contact");
  });

  it("shows an empty state, not an error, when no plans are configured", () => {
    mockUseGetPublicPlansQuery.mockReturnValue({ data: [], isLoading: false, isError: false });

    renderPage();

    expect(screen.getByText(/pricing is being finalized/i)).toBeInTheDocument();
    expect(screen.queryByText(/temporarily unavailable/i)).not.toBeInTheDocument();
  });

  it("renders each plan with its price, yearly price and months free", () => {
    mockUseGetPublicPlansQuery.mockReturnValue({ data: [FREE, PREMIUM], isLoading: false, isError: false });

    renderPage();

    expect(screen.getByRole("heading", { level: 2, name: "Free" })).toBeInTheDocument();
    expect(screen.getByText("€0")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 2, name: "Premium" })).toBeInTheDocument();
    expect(screen.getByText("€79")).toBeInTheDocument();
    expect(screen.getByText(/or €790 \/ year — 2 months free/)).toBeInTheDocument();
  });

  it("derives each plan's bullets from its limits and feature flags", () => {
    mockUseGetPublicPlansQuery.mockReturnValue({ data: [PREMIUM], isLoading: false, isError: false });

    renderPage();

    expect(screen.getByText(/Up to 10 artists/)).toBeInTheDocument();
    expect(screen.getByText(/Up to 1,000 appointments per month/)).toBeInTheDocument();
    expect(screen.getByText(/50 GB of file storage/)).toBeInTheDocument();
    expect(screen.getByText(/API keys and webhooks/)).toBeInTheDocument();
  });

  it("shows two decimals for fractional prices, never a single one", () => {
    mockUseGetPublicPlansQuery.mockReturnValue({
      data: [{ ...PREMIUM, prices: [{ interval: "Monthly", price: 29 }, { interval: "Yearly", price: 98.6 }] }],
      isLoading: false,
      isError: false,
    });

    renderPage();

    expect(screen.getByText(/or €98\.60 \/ year/)).toBeInTheDocument();
  });

  it("never overstates the yearly saving (fractions are floored)", () => {
    mockUseGetPublicPlansQuery.mockReturnValue({
      data: [{ ...PREMIUM, yearlyMonthsFree: 1.87 }],
      isLoading: false,
      isError: false,
    });

    renderPage();

    expect(screen.getByText(/1 month free/)).toBeInTheDocument();
  });

  it("omits the months-free note when the yearly price does not save", () => {
    mockUseGetPublicPlansQuery.mockReturnValue({
      data: [{ ...PREMIUM, yearlyMonthsFree: null }],
      isLoading: false,
      isError: false,
    });

    renderPage();

    expect(screen.getByText(/or €790 \/ year/)).toBeInTheDocument();
    expect(screen.queryByText(/free$/i)).not.toBeInTheDocument();
  });

  it("calls the plans query with no arguments and sets its document title", () => {
    mockUseGetPublicPlansQuery.mockReturnValue({ data: [], isLoading: false, isError: false });

    renderPage();

    expect(mockUseGetPublicPlansQuery).toHaveBeenCalledWith();
    expect(document.title).toBe("Pricing — TattooOS");
  });

  it("offers a way to register the studio", () => {
    mockUseGetPublicPlansQuery.mockReturnValue({ data: [], isLoading: false, isError: false });

    renderPage();

    expect(screen.getByRole("link", { name: /register your studio/i })).toHaveAttribute("href", "/register");
  });
});
