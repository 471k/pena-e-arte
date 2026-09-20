import { useEffect, useState } from "react";
import { PanelLeftClose, PanelLeftOpen } from "lucide-react";
import { cn } from "@/shared/utils/cn";
import { SidebarNav } from "@/shared/components/SidebarNav";
import { isNavGroup } from "@/shared/utils/navSections";
import type { NavSection } from "@/shared/types/navItem";

const COLLAPSED_KEY = "sidebar-collapsed";

function readCollapsed(): boolean {
  try {
    return localStorage.getItem(COLLAPSED_KEY) === "1";
  } catch {
    return false;
  }
}

function writeCollapsed(value: boolean) {
  try {
    localStorage.setItem(COLLAPSED_KEY, value ? "1" : "0");
  } catch {
    // storage unavailable — the preference just won't persist
  }
}

interface AppSidebarProps {
  sections: NavSection[];
  revealTourId?: string | null;
}

/**
 * Persistent desktop navigation: a vertical, scrollable rail of labelled sections and expandable
 * groups that folds down to an icon-only strip (button at the bottom, or Ctrl/Cmd+B). Sticks below the
 * 3.5rem layout header; below `lg` the NavDrawer takes over instead.
 */
export function AppSidebar({ sections, revealTourId = null }: AppSidebarProps) {
  const [collapsed, setCollapsed] = useState<boolean>(readCollapsed);

  useEffect(() => {
    writeCollapsed(collapsed);
  }, [collapsed]);

  useEffect(() => {
    function handler(e: KeyboardEvent) {
      if (!(e.ctrlKey || e.metaKey) || e.shiftKey || e.altKey || e.key.toLowerCase() !== "b") return;
      const target = e.target as HTMLElement | null;
      // Ctrl+B is "bold" inside text fields and rich-text editors — leave it alone there.
      if (target && (["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName) || target.isContentEditable)) return;
      e.preventDefault();
      setCollapsed((prev) => !prev);
    }
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, []);

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
      <div className="border-t p-2">
        <button
          type="button"
          onClick={() => setCollapsed(!collapsed)}
          aria-label={collapsed ? "Expand sidebar" : "Collapse sidebar"}
          aria-keyshortcuts="Control+B Meta+B"
          title={collapsed ? "Expand sidebar (Ctrl+B)" : "Collapse sidebar (Ctrl+B)"}
          className={cn(
            "flex h-9 items-center rounded-md text-sm text-muted-foreground transition-colors hover:bg-muted hover:text-foreground",
            railed ? "mx-auto w-9 justify-center" : "w-full gap-3 px-3",
          )}
        >
          {collapsed ? <PanelLeftOpen className="h-4 w-4 shrink-0" /> : <PanelLeftClose className="h-4 w-4 shrink-0" />}
          {!railed && (
            <>
              <span>Collapse</span>
              <kbd className="ml-auto rounded border px-1.5 py-0.5 text-[10px] font-medium text-muted-foreground">Ctrl B</kbd>
            </>
          )}
        </button>
      </div>
    </aside>
  );
}
