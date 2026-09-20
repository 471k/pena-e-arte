import { matchPath } from "react-router-dom";
import type { NavEntry, NavGroup, NavItem, NavSection } from "@/shared/types/navItem";

export function isNavGroup(entry: NavEntry): entry is NavGroup {
  return "children" in entry;
}

/** Every leaf link in a section list, in display order. */
export function flattenNavItems(sections: NavSection[]): NavItem[] {
  return sections.flatMap((s) => s.entries.flatMap((e) => (isNavGroup(e) ? e.children : [e])));
}

/** Route-only part of an href — nav hrefs may carry a query string (e.g. `/schedule?artistId=…`). */
function hrefPathname(href: string): string {
  const cut = href.search(/[?#]/);
  return cut === -1 ? href : href.slice(0, cut);
}

/** Same matching NavLink uses for its `isActive` state, so a group can pre-open around the active link. */
export function isNavItemActive(item: NavItem, pathname: string): boolean {
  return matchPath({ path: hrefPathname(item.href), end: !!item.end }, pathname) !== null;
}

export function groupContainsActive(group: NavGroup, pathname: string): boolean {
  return group.children.some((c) => isNavItemActive(c, pathname));
}

export function groupBadgeTotal(group: NavGroup): number {
  return group.children.reduce((sum, c) => sum + (c.badge ?? 0), 0);
}

/** Drops empty groups and empty sections so callers can build sections conditionally without leaving dead headings. */
export function pruneNavSections(sections: NavSection[]): NavSection[] {
  return sections
    .map((s) => ({
      ...s,
      entries: s.entries.filter((e) => !isNavGroup(e) || e.children.length > 0),
    }))
    .filter((s) => s.entries.length > 0);
}

/** Applies live badge counts (keyed by item label) to every matching link, including those inside groups. */
export function withNavBadges(sections: NavSection[], badges: Record<string, number>): NavSection[] {
  const apply = (item: NavItem): NavItem =>
    item.label in badges ? { ...item, badge: badges[item.label] } : item;
  return sections.map((s) => ({
    ...s,
    entries: s.entries.map((e) =>
      isNavGroup(e) ? { ...e, children: e.children.map(apply) } : apply(e),
    ),
  }));
}
