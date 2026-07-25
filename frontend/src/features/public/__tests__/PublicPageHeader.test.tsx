import { describe, it, expect } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";

import authReducer from "@/features/auth/authSlice";
import { PublicPageHeader } from "@/features/public/components/PublicPageHeader";
import type { Role } from "@/shared/types/roles";

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore(token: string | null, role: Role | null = null) {
  return configureStore({
    reducer: { auth: authReducer },
    preloadedState: {
      auth: {
        user: token ? { id: "u-001", email: "test@example.com" } : null,
        token,
        tenantId: null,
        role,
        refreshToken: null,
        pendingReferralCode: null,
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
      } as any,
    },
  });
}

function renderHeader(token: string | null = null, role: Role | null = null) {
  render(
    <Provider store={makeStore(token, role)}>
      <MemoryRouter>
        <PublicPageHeader />
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("PublicPageHeader", () => {
  describe("logged-out state", () => {
    it("renders the brand mark with link to /discover", () => {
      renderHeader(null);
      const brand = screen.getByRole("link", { name: /tattooos.*discover/i });
      expect(brand).toHaveAttribute("href", "/discover");
    });

    it("renders 'Sign in' link pointing to /login", () => {
      renderHeader(null);
      expect(screen.getByRole("link", { name: /sign in/i })).toHaveAttribute("href", "/login");
    });

    it("renders 'Sign up' link pointing to /client-register", () => {
      renderHeader(null);
      expect(screen.getByRole("link", { name: /sign up/i })).toHaveAttribute(
        "href",
        "/client-register",
      );
    });

    it("renders 'Register studio' link pointing to /register", () => {
      renderHeader(null);
      expect(screen.getByRole("link", { name: /register studio/i })).toHaveAttribute(
        "href",
        "/register",
      );
    });

    it("does not render the initials avatar button", () => {
      renderHeader(null);
      expect(screen.queryByRole("button", { name: /account menu/i })).not.toBeInTheDocument();
    });
  });

  describe("logged-in state", () => {
    it("renders the initials avatar button", () => {
      renderHeader("fake-token");
      expect(screen.getByRole("button", { name: /account menu/i })).toBeInTheDocument();
    });

    it("does not render 'Sign in', 'Sign up', or 'Register studio' links", () => {
      renderHeader("fake-token");
      expect(screen.queryByRole("link", { name: /^sign in$/i })).not.toBeInTheDocument();
      expect(screen.queryByRole("link", { name: /^sign up$/i })).not.toBeInTheDocument();
      expect(screen.queryByRole("link", { name: /register studio/i })).not.toBeInTheDocument();
    });

    it("clicking the avatar opens the dropdown menu", () => {
      renderHeader("fake-token");
      const avatarButton = screen.getByRole("button", { name: /account menu/i });
      expect(avatarButton).toHaveAttribute("aria-expanded", "false");
      fireEvent.click(avatarButton);
      expect(avatarButton).toHaveAttribute("aria-expanded", "true");
    });

    it("dropdown contains a 'Dashboard' link", () => {
      renderHeader("fake-token");
      fireEvent.click(screen.getByRole("button", { name: /account menu/i }));
      expect(screen.getByRole("menuitem", { name: /dashboard/i })).toBeInTheDocument();
    });

    it("dropdown contains a 'Book appointment' link", () => {
      renderHeader("fake-token");
      fireEvent.click(screen.getByRole("button", { name: /account menu/i }));
      expect(screen.getByRole("menuitem", { name: /book appointment/i })).toBeInTheDocument();
    });

    it("dropdown contains a 'Sign out' button", () => {
      renderHeader("fake-token");
      fireEvent.click(screen.getByRole("button", { name: /account menu/i }));
      expect(screen.getByRole("menuitem", { name: /sign out/i })).toBeInTheDocument();
    });

    it("clicking outside the dropdown closes it", () => {
      renderHeader("fake-token");
      const avatarButton = screen.getByRole("button", { name: /account menu/i });
      fireEvent.click(avatarButton);
      expect(avatarButton).toHaveAttribute("aria-expanded", "true");
      fireEvent.mouseDown(document.body);
      expect(avatarButton).toHaveAttribute("aria-expanded", "false");
    });

    it("pressing Escape closes the dropdown", () => {
      renderHeader("fake-token");
      const avatarButton = screen.getByRole("button", { name: /account menu/i });
      fireEvent.click(avatarButton);
      expect(avatarButton).toHaveAttribute("aria-expanded", "true");
      fireEvent.keyDown(document, { key: "Escape" });
      expect(avatarButton).toHaveAttribute("aria-expanded", "false");
    });
  });

  describe("accessibility", () => {
    it("header element has aria-label='Site header'", () => {
      renderHeader(null);
      expect(screen.getByLabelText("Site header")).toBeInTheDocument();
    });

    it("nav has aria-label='Site navigation'", () => {
      renderHeader(null);
      expect(screen.getByLabelText("Site navigation")).toBeInTheDocument();
    });

    it("dropdown has role='menu' and aria-label='Account options'", () => {
      renderHeader("fake-token");
      fireEvent.click(screen.getByRole("button", { name: /account menu/i }));
      expect(screen.getByRole("menu", { name: /account options/i })).toBeInTheDocument();
    });
  });
});
