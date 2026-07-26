import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import authReducer from "@/features/auth/authSlice";
import { UserMenu } from "../UserMenu";

function makeStore(role: "issuer" | "owner" = "issuer") {
  return configureStore({
    reducer: { auth: authReducer },
    preloadedState: {
      auth: {
        user: { id: "u1", email: "test@test.com", name: "Gabriel" },
        token: "tok",
        tenantId: null,
        role,
        pendingReferralCode: null,
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
      } as any,
    },
  });
}

function renderMenu(onLogout = vi.fn()) {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter>
        <UserMenu onLogout={onLogout} />
      </MemoryRouter>
    </Provider>,
  );
}

afterEach(cleanup);

describe("UserMenu", () => {
  it("renders the user display name", () => {
    renderMenu();
    expect(screen.getByText("Gabriel")).toBeInTheDocument();
  });

  it("chevron does NOT have opacity-50 class (contrast fix)", () => {
    renderMenu();
    // The ChevronDown SVG is inside the trigger button.
    // SVG elements expose `className` as an SVGAnimatedString, not a plain
    // string, so read the class attribute directly to avoid that footgun.
    const btn = screen.getByRole("button", { name: /user menu/i });
    const svg = btn.querySelector("svg");
    expect(svg?.getAttribute("class")).not.toMatch(/opacity-50/);
  });

  it("chevron has text-muted-foreground/70 class for legible contrast", () => {
    renderMenu();
    const btn = screen.getByRole("button", { name: /user menu/i });
    const svg = btn.querySelector("svg");
    // Tailwind generates the class as text-muted-foreground\/70 in the DOM
    expect(svg?.getAttribute("class")).toMatch(/muted-foreground/);
  });

  it("opens menu on click", async () => {
    const user = userEvent.setup();
    renderMenu();
    await user.click(screen.getByRole("button", { name: /user menu/i }));
    expect(screen.getByRole("button", { name: /log out/i })).toBeInTheDocument();
  });

  it("closes menu on Escape key", async () => {
    const user = userEvent.setup();
    renderMenu();
    await user.click(screen.getByRole("button", { name: /user menu/i }));
    expect(screen.getByRole("button", { name: /log out/i })).toBeInTheDocument();
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("button", { name: /log out/i })).not.toBeInTheDocument();
  });

  it("closes menu on outside click", async () => {
    const user = userEvent.setup();
    renderMenu();
    await user.click(screen.getByRole("button", { name: /user menu/i }));
    await user.click(document.body);
    expect(screen.queryByRole("button", { name: /log out/i })).not.toBeInTheDocument();
  });

  it("calls onLogout when Log out is clicked", async () => {
    const onLogout = vi.fn();
    const user = userEvent.setup();
    renderMenu(onLogout);
    await user.click(screen.getByRole("button", { name: /user menu/i }));
    await user.click(screen.getByRole("button", { name: /log out/i }));
    expect(onLogout).toHaveBeenCalledOnce();
  });

  it("links to /account/change-password", async () => {
    const user = userEvent.setup();
    renderMenu();
    await user.click(screen.getByRole("button", { name: /user menu/i }));
    expect(screen.getByRole("link", { name: /change password/i }))
      .toHaveAttribute("href", "/account/change-password");
  });

  it("links to /account/change-email", async () => {
    const user = userEvent.setup();
    renderMenu();
    await user.click(screen.getByRole("button", { name: /user menu/i }));
    expect(screen.getByRole("link", { name: /change email/i }))
      .toHaveAttribute("href", "/account/change-email");
  });

  it("closes menu when Change email is clicked", async () => {
    const user = userEvent.setup();
    renderMenu();
    await user.click(screen.getByRole("button", { name: /user menu/i }));
    await user.click(screen.getByRole("link", { name: /change email/i }));
    expect(screen.queryByRole("link", { name: /change email/i })).not.toBeInTheDocument();
  });
});
