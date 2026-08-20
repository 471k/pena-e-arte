import { describe, it, expect, vi } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { afterEach } from "vitest";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Routes, Route } from "react-router-dom";
import { Home, Settings } from "lucide-react";

import { NavDrawer } from "@/shared/components/NavDrawer";
import type { NavItem } from "@/shared/types/navItem";

afterEach(() => cleanup());

const NAV_ITEMS: NavItem[] = [
  { label: "Home",     href: "/home",     icon: <Home className="h-4 w-4" /> },
  { label: "Settings", href: "/settings", icon: <Settings className="h-4 w-4" /> },
];

function renderDrawer(props: Partial<React.ComponentProps<typeof NavDrawer>> = {}, initialPath = "/home") {
  const onOpenChange = props.onOpenChange ?? vi.fn();
  render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route
          path="*"
          element={
            <NavDrawer
              navItems={props.navItems ?? NAV_ITEMS}
              title={props.title ?? "TattooOS"}
              open={props.open ?? false}
              onOpenChange={onOpenChange}
            />
          }
        />
      </Routes>
    </MemoryRouter>,
  );
  return { onOpenChange };
}

describe("NavDrawer", () => {
  it("renders a hamburger trigger that is hidden at lg and above", () => {
    renderDrawer();
    const trigger = screen.getByRole("button", { name: /open navigation menu/i });
    expect(trigger.className).toMatch(/lg:hidden/);
  });

  it("does not render the sheet content when closed", () => {
    renderDrawer({ open: false });
    expect(screen.queryByText("TattooOS")).not.toBeInTheDocument();
  });

  it("clicking the trigger calls onOpenChange(true)", async () => {
    const user = userEvent.setup();
    const { onOpenChange } = renderDrawer({ open: false });
    await user.click(screen.getByRole("button", { name: /open navigation menu/i }));
    expect(onOpenChange).toHaveBeenCalledWith(true);
  });

  it("is controllable from outside: open=true shows it without a click", () => {
    renderDrawer({ open: true });
    expect(screen.getByText("TattooOS")).toBeInTheDocument();
  });

  it("renders all navItems inside the open drawer, each with the 44px touch-target class", () => {
    renderDrawer({ open: true });
    const homeLink = screen.getByRole("link", { name: /home/i });
    const settingsLink = screen.getByRole("link", { name: /settings/i });
    expect(homeLink.className).toMatch(/min-h-\[44px\]/);
    expect(settingsLink.className).toMatch(/min-h-\[44px\]/);
  });

  it("clicking a nav link navigates and closes the sheet", async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    render(
      <MemoryRouter initialEntries={["/home"]}>
        <Routes>
          <Route path="/home" element={<div data-testid="home-page" />} />
          <Route path="/settings" element={<div data-testid="settings-page" />} />
        </Routes>
        <NavDrawer navItems={NAV_ITEMS} title="TattooOS" open onOpenChange={onOpenChange} />
      </MemoryRouter>,
    );

    await user.click(screen.getByRole("link", { name: /settings/i }));
    expect(await screen.findByTestId("settings-page")).toBeInTheDocument();
    // Radix's SheetClose fires onOpenChange(false) as part of the same navigation.
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("does not render a badge when badge is 0 or undefined", () => {
    renderDrawer({
      open: true,
      navItems: [
        { label: "No Badge", href: "/a", icon: <Home className="h-4 w-4" />, badge: 0 },
        { label: "Undefined Badge", href: "/b", icon: <Home className="h-4 w-4" /> },
      ],
    });
    expect(screen.queryByText("0")).not.toBeInTheDocument();
  });

  it("renders a badge when badge > 0", () => {
    renderDrawer({
      open: true,
      navItems: [
        { label: "Feedback", href: "/feedback", icon: <Home className="h-4 w-4" />, badge: 3 },
      ],
    });
    expect(screen.getByText("3")).toBeInTheDocument();
  });

  it("caps the badge display at 99+", () => {
    renderDrawer({
      open: true,
      navItems: [
        { label: "Feedback", href: "/feedback", icon: <Home className="h-4 w-4" />, badge: 150 },
      ],
    });
    expect(screen.getByText("99+")).toBeInTheDocument();
  });
});
