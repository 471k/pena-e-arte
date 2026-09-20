import { describe, it, expect, afterEach, beforeEach } from "vitest";
import { render, screen, cleanup, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { Home } from "lucide-react";

import { AppSidebar } from "@/shared/components/AppSidebar";
import type { NavSection } from "@/shared/types/navItem";

const icon = <Home className="h-4 w-4" />;

const SECTIONS: NavSection[] = [
  {
    id: "main",
    label: "Main",
    entries: [
      { label: "Dashboard", href: "/dashboard", icon },
      {
        id: "people", label: "People", icon,
        children: [{ label: "Artists", href: "/artists", icon, tourId: "artists-nav" }],
      },
    ],
  },
];

function renderSidebar(props: Partial<React.ComponentProps<typeof AppSidebar>> = {}) {
  return render(
    <MemoryRouter initialEntries={["/dashboard"]}>
      <input aria-label="notes" />
      <AppSidebar sections={SECTIONS} {...props} />
    </MemoryRouter>,
  );
}

beforeEach(() => localStorage.clear());
afterEach(() => cleanup());

describe("AppSidebar", () => {
  it("is a desktop-only aside that scrolls its nav and sticks below the header", () => {
    renderSidebar();
    const aside = screen.getByRole("complementary", { name: /sidebar/i });
    expect(aside.className).toMatch(/hidden/);
    expect(aside.className).toMatch(/lg:flex/);
    expect(aside.className).toMatch(/sticky/);
    expect(screen.getByRole("navigation", { name: /main navigation/i }).parentElement?.className).toMatch(/overflow-y-auto/);
  });

  it("starts expanded with section headings and a Collapse control", () => {
    renderSidebar();
    expect(screen.getByText("Main")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /collapse sidebar/i })).toBeInTheDocument();
  });

  it("collapses to an icon rail, persists that, and expands again", async () => {
    const user = userEvent.setup();
    renderSidebar();

    await user.click(screen.getByRole("button", { name: /collapse sidebar/i }));
    expect(screen.queryByText("Main")).not.toBeInTheDocument();
    expect(localStorage.getItem("sidebar-collapsed")).toBe("1");

    await user.click(screen.getByRole("button", { name: /expand sidebar/i }));
    expect(screen.getByText("Main")).toBeInTheDocument();
    expect(localStorage.getItem("sidebar-collapsed")).toBe("0");
  });

  it("restores the collapsed preference on mount", () => {
    localStorage.setItem("sidebar-collapsed", "1");
    renderSidebar();
    expect(screen.queryByText("Main")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /expand sidebar/i })).toBeInTheDocument();
  });

  it("Ctrl+B toggles the sidebar", () => {
    renderSidebar();
    fireEvent.keyDown(window, { key: "b", ctrlKey: true });
    expect(screen.getByRole("button", { name: /expand sidebar/i })).toBeInTheDocument();
    fireEvent.keyDown(window, { key: "b", ctrlKey: true });
    expect(screen.getByRole("button", { name: /collapse sidebar/i })).toBeInTheDocument();
  });

  it("Ctrl+B is ignored while typing in a text field", () => {
    renderSidebar();
    fireEvent.keyDown(screen.getByLabelText("notes"), { key: "b", ctrlKey: true });
    expect(screen.getByRole("button", { name: /collapse sidebar/i })).toBeInTheDocument();
  });

  it("clicking a group icon in the rail expands the sidebar and opens that group", async () => {
    const user = userEvent.setup();
    localStorage.setItem("sidebar-collapsed", "1");
    renderSidebar();

    await user.click(screen.getByRole("button", { name: "People" }));
    expect(screen.getByRole("button", { name: /collapse sidebar/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /artists/i })).toBeInTheDocument();
  });

  it("holds the rail open for a tour step targeting a link inside a group, without saving that", () => {
    localStorage.setItem("sidebar-collapsed", "1");
    renderSidebar({ revealTourId: "artists-nav" });
    expect(screen.getByRole("link", { name: /artists/i })).toBeInTheDocument();
    expect(localStorage.getItem("sidebar-collapsed")).toBe("1");
  });
});
