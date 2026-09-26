import { describe, it, expect, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";

import authReducer from "@/features/auth/authSlice";
import { FaqPage } from "@/features/public/components/FaqPage";

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
        <FaqPage />
      </MemoryRouter>
    </Provider>,
  );
}

const QUESTIONS = [
  "What is TattooOS?",
  "Does TattooOS take a commission on bookings?",
  "Do clients need an account to book?",
  "How do deposits work?",
  "How do digital consent forms work?",
  "How do I get started?",
];

describe("FaqPage", () => {
  afterEach(cleanup);

  it("lists every question, all collapsed by default", () => {
    renderPage();

    for (const question of QUESTIONS) {
      const trigger = screen.getByRole("button", { name: question });
      expect(trigger).toHaveAttribute("aria-expanded", "false");
    }
  });

  it("expands an answer on click and collapses it again", async () => {
    const user = userEvent.setup();
    renderPage();

    const trigger = screen.getByRole("button", { name: "Does TattooOS take a commission on bookings?" });
    await user.click(trigger);

    expect(trigger).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByText(/charges no commission on bookings/i)).toBeVisible();

    await user.click(trigger);
    expect(trigger).toHaveAttribute("aria-expanded", "false");
  });

  it("expands an answer from the keyboard", async () => {
    const user = userEvent.setup();
    renderPage();

    const trigger = screen.getByRole("button", { name: "Do clients need an account to book?" });
    trigger.focus();
    await user.keyboard("{Enter}");

    expect(trigger).toHaveAttribute("aria-expanded", "true");
  });

  it("links the deposits answer to the deposits page", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole("button", { name: "How do deposits work?" }));

    expect(screen.getByRole("link", { name: /more about deposits/i })).toHaveAttribute("href", "/use/deposits");
  });

  it("sets its own document title", () => {
    renderPage();

    expect(document.title).toBe("FAQ — TattooOS");
  });
});
