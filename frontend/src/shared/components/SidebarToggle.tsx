import { PanelLeftClose, PanelLeftOpen } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { useSidebarCollapsed } from "@/shared/hooks/useSidebarCollapsed";

/**
 * Collapse/expand button for the desktop sidebar, placed at the start of every layout header. It
 * used to sit at the bottom of the sidebar, which fell below the fold whenever a banner
 * (suspension, read-only, plan limit) pushed the layout down; the header is always on screen.
 * Desktop only — below `lg` the sidebar is replaced by the NavDrawer.
 */
export function SidebarToggle() {
  const { collapsed, toggle } = useSidebarCollapsed();
  const label = collapsed ? "Expand sidebar" : "Collapse sidebar";

  return (
    <Button
      type="button"
      variant="ghost"
      size="icon"
      onClick={toggle}
      aria-label={label}
      aria-keyshortcuts="Control+B Meta+B"
      title={`${label} (Ctrl+B)`}
      className="hidden lg:inline-flex h-8 w-8 shrink-0 text-muted-foreground hover:text-foreground"
    >
      {collapsed ? <PanelLeftOpen className="h-4 w-4" /> : <PanelLeftClose className="h-4 w-4" />}
    </Button>
  );
}
