import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";

import authReducer from "@/features/auth/authSlice";
import { BookingWidget } from "@/features/booking/components/BookingWidget";

// ── Mock studiosApi ───────────────────────────────────────────────────────────

const mockUseGetMyStudioQuery = vi.fn();

vi.mock("@/features/studios/studiosApi", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/studios/studiosApi")>();
  return {
    ...actual,
    useGetMyStudioQuery: (...args: unknown[]) => mockUseGetMyStudioQuery(...args),
  };
});

// ── Helpers ───────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: { auth: authReducer },
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    preloadedState: { auth: { user: null, token: "t", tenantId: "s1", role: "owner" } as any },
  });
}

function renderWidget(showPlatformBranding = true) {
  mockUseGetMyStudioQuery.mockReturnValue({
    data:      { showPlatformBranding, isActive: true },
    isLoading: false,
  });
  render(
    <Provider store={makeStore()}>
      <BookingWidget>
        <div data-testid="booking-form">Booking Form</div>
      </BookingWidget>
    </Provider>,
  );
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("BookingWidget", () => {
  beforeEach(() => {
    mockUseGetMyStudioQuery.mockReturnValue({ data: undefined, isLoading: true });
  });

  it("renders the booking form (children) for the given context", () => {
    renderWidget();
    expect(screen.getByTestId("booking-form")).toBeInTheDocument();
  });

  it("renders 'Powered by Pena e Artë' footer when showPlatformBranding is true", () => {
    renderWidget(true);
    expect(screen.getByText(/powered by pena e art/i)).toBeInTheDocument();
  });

  it("does NOT render branding footer when showPlatformBranding is false", () => {
    renderWidget(false);
    expect(screen.queryByText(/powered by pena e art/i)).not.toBeInTheDocument();
  });

  it("branding footer links to https://penaearte.com", () => {
    renderWidget(true);
    const link = screen.getByRole("link", { name: /powered by pena e art/i });
    expect(link).toHaveAttribute("href", "https://penaearte.com");
  });

  it("shows loading state while studio data is fetching", () => {
    mockUseGetMyStudioQuery.mockReturnValue({ data: undefined, isLoading: true });
    render(
      <Provider store={makeStore()}>
        <BookingWidget>
          <div data-testid="content">Content</div>
        </BookingWidget>
      </Provider>,
    );
    // children still render; no branding footer since data is undefined
    expect(screen.getByTestId("content")).toBeInTheDocument();
    expect(screen.queryByText(/powered by pena e art/i)).not.toBeInTheDocument();
  });
});
