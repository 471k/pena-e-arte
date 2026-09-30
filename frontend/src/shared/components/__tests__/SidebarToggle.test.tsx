import { describe, it, expect, afterEach, beforeEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { SidebarToggle } from "@/shared/components/SidebarToggle";

beforeEach(() => localStorage.clear());
afterEach(() => cleanup());

describe("SidebarToggle", () => {
  it("offers to collapse an expanded sidebar, advertising the Ctrl+B shortcut", () => {
    render(<SidebarToggle />);
    const button = screen.getByRole("button", { name: "Collapse sidebar" });
    expect(button).toHaveAttribute("title", "Collapse sidebar (Ctrl+B)");
    expect(button).toHaveAttribute("aria-keyshortcuts", "Control+B Meta+B");
  });

  it("toggles between Collapse and Expand and saves the preference", async () => {
    const user = userEvent.setup();
    render(<SidebarToggle />);

    await user.click(screen.getByRole("button", { name: "Collapse sidebar" }));
    expect(screen.getByRole("button", { name: "Expand sidebar" })).toBeInTheDocument();
    expect(localStorage.getItem("sidebar-collapsed")).toBe("1");

    await user.click(screen.getByRole("button", { name: "Expand sidebar" }));
    expect(screen.getByRole("button", { name: "Collapse sidebar" })).toBeInTheDocument();
    expect(localStorage.getItem("sidebar-collapsed")).toBe("0");
  });

  it("starts in the saved state", () => {
    localStorage.setItem("sidebar-collapsed", "1");
    render(<SidebarToggle />);
    expect(screen.getByRole("button", { name: "Expand sidebar" })).toBeInTheDocument();
  });

  it("stays in step with a second toggle on the page", async () => {
    const user = userEvent.setup();
    render(<><SidebarToggle /><SidebarToggle /></>);

    await user.click(screen.getAllByRole("button", { name: "Collapse sidebar" })[0]);

    expect(screen.getAllByRole("button", { name: "Expand sidebar" })).toHaveLength(2);
  });

  it("is desktop-only: hidden below lg, where the nav drawer replaces the sidebar", () => {
    render(<SidebarToggle />);
    const button = screen.getByRole("button", { name: "Collapse sidebar" });
    expect(button.className.split(/\s+/)).toContain("hidden");
    expect(button.className).toMatch(/lg:inline-flex/);
  });
});
