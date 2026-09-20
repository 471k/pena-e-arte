import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { Home, Settings } from "lucide-react";

import { SidebarNav } from "@/shared/components/SidebarNav";
import type { NavSection } from "@/shared/types/navItem";

afterEach(() => cleanup());

const icon = <Home className="h-4 w-4" />;

const SECTIONS: NavSection[] = [
  { id: "top", entries: [{ label: "Dashboard", href: "/dashboard", icon }] },
  {
    id: "operations",
    label: "Operations",
    entries: [
      {
        id: "people", label: "People", icon: <Settings className="h-4 w-4" />,
        children: [
          { label: "Artists", href: "/artists", icon, tourId: "artists-nav" },
          { label: "Clients", href: "/clients", icon, badge: 4 },
        ],
      },
      {
        id: "sales", label: "Sales", icon,
        children: [{ label: "Payments", href: "/payments", icon }],
      },
    ],
  },
];

function renderNav(props: Partial<React.ComponentProps<typeof SidebarNav>> = {}, path = "/dashboard") {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <SidebarNav sections={SECTIONS} {...props} />
    </MemoryRouter>,
  );
}

describe("SidebarNav", () => {
  it("renders section headings and top-level links", () => {
    renderNav();
    expect(screen.getByText("Operations")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^dashboard$/i })).toBeInTheDocument();
  });

  it("keeps groups collapsed until opened, hiding their children", () => {
    renderNav();
    const people = screen.getByRole("button", { name: /people/i });
    expect(people).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByRole("link", { name: /artists/i })).not.toBeInTheDocument();
  });

  it("expands and collapses a group on click", async () => {
    const user = userEvent.setup();
    renderNav();
    const people = screen.getByRole("button", { name: /people/i });

    await user.click(people);
    expect(people).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("link", { name: /artists/i })).toBeInTheDocument();

    await user.click(people);
    expect(people).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByRole("link", { name: /artists/i })).not.toBeInTheDocument();
  });

  it("starts with the group containing the active route open, others closed", () => {
    renderNav({}, "/payments");
    expect(screen.getByRole("button", { name: /sales/i })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("button", { name: /people/i })).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByRole("link", { name: /payments/i }).className).toMatch(/bg-primary/);
  });

  it("lets the user close the active group without it snapping back open", async () => {
    const user = userEvent.setup();
    renderNav({}, "/payments");
    const sales = screen.getByRole("button", { name: /sales/i });
    await user.click(sales);
    expect(sales).toHaveAttribute("aria-expanded", "false");
  });

  it("shows the summed badge of a collapsed group, and the per-link badge once open", async () => {
    const user = userEvent.setup();
    renderNav();
    expect(screen.getByText("4")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /people/i }));
    // group badge disappears when open; the link's own badge remains — exactly one "4" either way
    expect(screen.getAllByText("4")).toHaveLength(1);
  });

  it("force-opens the group holding a tour step's target", () => {
    renderNav({ revealTourId: "artists-nav" });
    expect(screen.getByRole("button", { name: /people/i })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("link", { name: /artists/i })).toHaveAttribute("data-tour", "artists-nav");
  });

  it("calls onNavigate when a link is followed", async () => {
    const user = userEvent.setup();
    const onNavigate = vi.fn();
    renderNav({ onNavigate });
    await user.click(screen.getByRole("link", { name: /dashboard/i }));
    expect(onNavigate).toHaveBeenCalled();
  });

  describe("collapsed icon rail", () => {
    it("shows icon-only links labelled via aria-label/title, with no section headings", () => {
      renderNav({ collapsed: true });
      expect(screen.queryByText("Operations")).not.toBeInTheDocument();
      const dashboard = screen.getByRole("link", { name: "Dashboard" });
      expect(dashboard).toHaveAttribute("title", "Dashboard");
      expect(screen.queryByText("Dashboard")).not.toBeInTheDocument();
    });

    it("clicking a group icon asks the parent to expand out of the rail", async () => {
      const user = userEvent.setup();
      const onExpandRequest = vi.fn();
      renderNav({ collapsed: true, onExpandRequest });
      await user.click(screen.getByRole("button", { name: "People" }));
      expect(onExpandRequest).toHaveBeenCalledTimes(1);
    });
  });
});
