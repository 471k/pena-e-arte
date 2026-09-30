import { useEffect } from "react";
import { cn } from "@/shared/utils/cn";
import { SidebarNav } from "@/shared/components/SidebarNav";
import { useSidebarCollapsed } from "@/shared/hooks/useSidebarCollapsed";
import { isNavGroup } from "@/shared/utils/navSections";
import type { NavSection } from "@/shared/types/navItem";

interface AppSidebarProps {
  sections: NavSection[];
  revealTourId?: string | null;
}

/**
 * Persistent desktop navigation: a vertical, scrollable rail of labelled sections and expandable
 * groups that folds down to an icon-only strip (the toggle in the layout header's SidebarToggle, or Ctrl/Cmd+B).
 * Sticks below the 3.5rem layout header; below `lg` the NavDrawer takes over instead.
 */
export function AppSidebar({ sections, revealTourId = null }: AppSidebarProps) {
  const { collapsed, setCollapsed, toggle } = useSidebarCollapsed();

  useEffect(() => {
    function handler(e: KeyboardEvent) {
      if (!(e.ctrlKey || e.metaKey) || e.shiftKey || e.altKey || e.key.toLowerCase() !== "b") return;
      const target = e.target as HTMLElement | null;
      // Ctrl+B is "bold" inside text fields and rich-text editors — leave it alone there.
      if (target && (["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName) || target.isContentEditable)) return;
      e.preventDefault();
      toggle();
    }
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [toggle]);

  // A tour step aimed at a link inside a group can't be measured while the rail hides every group's
  // children, so hold the rail open for that step (without touching the saved preference).
  const revealInsideGroup =
    !!revealTourId &&
    sections.some((s) => s.entries.some((e) => isNavGroup(e) && e.children.some((c) => c.tourId === revealTourId)));
  const railed = collapsed && !revealInsideGroup;

  return (
    <aside
      aria-label="Sidebar"
      className={cn(
        "hidden lg:flex shrink-0 flex-col border-r bg-background self-start sticky top-14 h-[calc(100vh-3.5rem)] transition-[width] duration-150",
        railed ? "w-14" : "w-64",
      )}
    >
      <div className="flex-1 overflow-y-auto overscroll-contain px-2 py-3">
        <SidebarNav
          sections={sections}
          collapsed={railed}
          revealTourId={revealTourId}
          onExpandRequest={() => setCollapsed(false)}
        />
      </div>
    </aside>
  );
}
