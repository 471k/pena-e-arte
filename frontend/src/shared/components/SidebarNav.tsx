import { useState } from "react";
import { NavLink, useLocation } from "react-router-dom";
import { ChevronDown, ChevronRight } from "lucide-react";
import { cn } from "@/shared/utils/cn";
import { groupBadgeTotal, groupContainsActive, isNavGroup } from "@/shared/utils/navSections";
import type { NavGroup, NavItem, NavSection } from "@/shared/types/navItem";

interface SidebarNavProps {
  sections: NavSection[];
  /** Icon-only rail: labels and children hidden, section headings become dividers. */
  collapsed?: boolean;
  /** 44px touch targets for the mobile drawer. */
  touch?: boolean;
  /** `data-tour` id whose group must be open (an onboarding-tour step is about to measure it). */
  revealTourId?: string | null;
  /** Fired when a link is followed — the mobile drawer closes itself with this. */
  onNavigate?: () => void;
  /** Rail mode only: a group icon was clicked, so the parent should expand out of the rail. */
  onExpandRequest?: () => void;
  ariaLabel?: string;
}

interface GroupOverride {
  open: boolean;
  /** Pathname the override was made on — a manual "close" only sticks until the user navigates. */
  at:   string;
}

function CountBadge({ count, className }: { count: number; className?: string }) {
  if (count <= 0) return null;
  return (
    <span
      className={cn(
        "min-w-[1.25rem] rounded-full bg-destructive px-1 py-0.5 text-[10px] font-medium text-destructive-foreground text-center",
        className,
      )}
    >
      {count > 99 ? "99+" : count}
    </span>
  );
}

function DotBadge({ count }: { count: number }) {
  if (count <= 0) return null;
  return <span aria-hidden className="absolute right-1 top-1 h-2 w-2 rounded-full bg-destructive" />;
}

export function SidebarNav({
  sections, collapsed = false, touch = false, revealTourId = null, onNavigate, onExpandRequest,
  ariaLabel = "Main navigation",
}: SidebarNavProps) {
  const { pathname } = useLocation();
  const [overrides, setOverrides] = useState<Record<string, GroupOverride>>({});

  const rowHeight = touch ? "min-h-[44px]" : "min-h-9";

  function isGroupOpen(group: NavGroup): boolean {
    if (revealTourId && group.children.some((c) => c.tourId === revealTourId)) return true;
    const override = overrides[group.id];
    if (override?.open) return true;
    if (override && override.at === pathname) return false;
    return groupContainsActive(group, pathname);
  }

  function setGroupOpen(group: NavGroup, open: boolean) {
    setOverrides((prev) => ({ ...prev, [group.id]: { open, at: pathname } }));
  }

  function renderLink(item: NavItem, nested: boolean) {
    const { label, href, icon, tourId, end, badge } = item;
    return (
      <NavLink
        key={href}
        to={href}
        end={end}
        data-tour={tourId}
        onClick={onNavigate}
        title={collapsed ? label : undefined}
        aria-label={collapsed ? label : undefined}
        className={({ isActive }) =>
          cn(
            "relative flex items-center rounded-md text-sm transition-colors",
            rowHeight,
            collapsed ? "mx-auto h-9 w-9 justify-center" : "gap-3 px-3",
            isActive
              ? "bg-primary/10 text-primary font-medium"
              : "text-muted-foreground hover:text-foreground hover:bg-muted",
          )
        }
      >
        {!nested && icon}
        {!collapsed && <span className="truncate">{label}</span>}
        {collapsed ? <DotBadge count={badge ?? 0} /> : <CountBadge count={badge ?? 0} className="ml-auto" />}
      </NavLink>
    );
  }

  function renderGroup(group: NavGroup) {
    const open = !collapsed && isGroupOpen(group);
    const panelId = `nav-group-${group.id}`;
    const badge = groupBadgeTotal(group);
    const active = groupContainsActive(group, pathname);
    const showBadge = !open && badge > 0;

    return (
      <div key={group.id}>
        <button
          type="button"
          aria-expanded={open}
          aria-controls={collapsed ? undefined : panelId}
          aria-label={collapsed ? group.label : undefined}
          title={collapsed ? group.label : undefined}
          onClick={() => {
            if (collapsed) {
              setGroupOpen(group, true);
              onExpandRequest?.();
              return;
            }
            setGroupOpen(group, !open);
          }}
          className={cn(
            "relative flex w-full items-center rounded-md text-sm transition-colors",
            rowHeight,
            collapsed ? "mx-auto h-9 w-9 justify-center" : "gap-3 px-3",
            active && !open ? "text-foreground font-medium" : "text-muted-foreground",
            "hover:text-foreground hover:bg-muted",
          )}
        >
          {group.icon}
          {!collapsed && (
            <>
              <span className="truncate text-left">{group.label}</span>
              {showBadge && <CountBadge count={badge} className="ml-auto" />}
              {open
                ? <ChevronDown className="ml-auto h-4 w-4 shrink-0" />
                : <ChevronRight className={cn("h-4 w-4 shrink-0", !showBadge && "ml-auto")} />}
            </>
          )}
          {collapsed && <DotBadge count={badge} />}
        </button>
        {open && (
          <div id={panelId} className="ml-[1.35rem] mt-0.5 flex flex-col gap-0.5 border-l pl-2">
            {group.children.map((child) => renderLink(child, true))}
          </div>
        )}
      </div>
    );
  }

  return (
    <nav aria-label={ariaLabel} className="flex flex-col">
      {sections.map((section, index) => (
        <div key={section.id} className={cn(index > 0 && (collapsed || !section.label) && "mt-2 border-t pt-2")}>
          {!collapsed && section.label && (
            <p className={cn("px-3 pb-1 text-xs font-medium text-muted-foreground", index > 0 ? "pt-4" : "pt-1")}>
              {section.label}
            </p>
          )}
          <div className="flex flex-col gap-0.5">
            {section.entries.map((entry) => (isNavGroup(entry) ? renderGroup(entry) : renderLink(entry, false)))}
          </div>
        </div>
      ))}
    </nav>
  );
}
