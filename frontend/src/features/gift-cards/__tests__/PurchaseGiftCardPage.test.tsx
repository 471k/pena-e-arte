import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";

import { PurchaseGiftCardPage } from "@/features/gift-cards/components/PurchaseGiftCardPage";

let mockStudio: { currency: string; countryCode?: string } = { currency: "EUR", countryCode: "AL" };

vi.mock("@/app/hooks", () => ({
  // A guest: no token, so the page skips /payments/capabilities and reads the public studio.
  useAppSelector: (select: (s: { auth: { token: string | null } }) => unknown) => select({ auth: { token: null } }),
}));

vi.mock("@/features/public/publicApi", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/public/publicApi")>();
  return { ...actual, useGetPublicStudioQuery: () => ({ data: mockStudio }) };
});

vi.mock("@/features/payments/paymentsApi", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/payments/paymentsApi")>();
  return { ...actual, useGetPaymentCapabilitiesQuery: () => ({ data: undefined }) };
});

vi.mock("@/features/gift-cards/giftCardsApi", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/gift-cards/giftCardsApi")>();
  return { ...actual, usePurchaseGiftCardMutation: () => [vi.fn(), { isLoading: false, isSuccess: false }] };
});

vi.mock("@/shared/hooks/useEurAllRate", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/shared/hooks/useEurAllRate")>();
  return {
    ...actual,
    useEurAllRate: () => ({ data: { rate: 90.84, rateDate: "2026-10-28", source: "BoA-Fix", isStale: false } }),
  };
});

beforeEach(() => { mockStudio = { currency: "EUR", countryCode: "AL" }; });
afterEach(() => cleanup());

function renderPage() {
  render(
    <MemoryRouter initialEntries={["/s/ink-and-iron-studio/gift-cards/buy"]}>
      <Routes>
        <Route path="/s/:slug/gift-cards/buy" element={<PurchaseGiftCardPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

// Intl puts a no-break space between "ALL" and the number; \s matches it.
describe("<PurchaseGiftCardPage> lek reference", () => {
  it("shows the lek of an amount typed into the box, not only of the presets", async () => {
    const user = userEvent.setup();
    renderPage();

    const box = screen.getByRole("spinbutton");
    await user.clear(box);
    await user.type(box, "75");

    // 75 x 90.84 = 6,813
    expect(screen.getByText(/You pay/)).toHaveTextContent(/^You pay €75 \(≈ ALL\s6,813\)$/);
  });

  it("follows a preset choice", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole("button", { name: /€100/ }));

    expect(screen.getByText(/You pay/)).toHaveTextContent(/^You pay €100 \(≈ ALL\s9,084\)$/);
  });

  it("adds no line for a studio that shows no lek", () => {
    mockStudio = { currency: "ALL", countryCode: "AL" };
    renderPage();

    expect(screen.queryByText(/You pay/)).not.toBeInTheDocument();
  });
});
