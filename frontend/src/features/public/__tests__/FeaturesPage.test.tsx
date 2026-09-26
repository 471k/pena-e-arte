import { describe, it, expect, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";

import authReducer from "@/features/auth/authSlice";
import { FeaturesPage } from "@/features/public/components/FeaturesPage";

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
        <FeaturesPage />
      </MemoryRouter>
    </Provider>,
  );
}

describe("FeaturesPage", () => {
  afterEach(cleanup);

  it("renders the page heading and all six feature groups", () => {
    renderPage();

    expect(screen.getByRole("heading", { level: 1, name: /features/i })).toBeInTheDocument();
    for (const title of [
      "Online booking & deposits",
      "Digital consent forms",
      "Design approval",
      "Client profiles & history",
      "Automatic reminders",
      "Studio & artist pages",
    ]) {
      expect(screen.getByRole("heading", { level: 2, name: title })).toBeInTheDocument();
    }
  });

  it("links to pricing and to studio registration", () => {
    renderPage();

    expect(screen.getByRole("link", { name: /see pricing/i })).toHaveAttribute("href", "/pricing");
    expect(screen.getByRole("link", { name: /register your studio/i })).toHaveAttribute("href", "/register");
  });

  it("sets its own document title", () => {
    renderPage();

    expect(document.title).toBe("Features — TattooOS");
  });

  it("does not claim a specific card processor for deposits", () => {
    renderPage();

    expect(screen.queryByText(/stripe/i)).not.toBeInTheDocument();
  });
});
