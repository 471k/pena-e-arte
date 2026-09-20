import { describe, it, expect } from "vitest";
import type { NavGroup, NavItem, NavSection } from "@/shared/types/navItem";
import {
  flattenNavItems, groupBadgeTotal, groupContainsActive, isNavGroup, isNavItemActive,
  pruneNavSections, withNavBadges,
} from "@/shared/utils/navSections";
import { navTourIdForStep } from "@/shared/utils/shouldOpenNavDrawerForTourStep";

const item = (label: string, href: string, extra: Partial<NavItem> = {}): NavItem =>
  ({ label, href, icon: null, ...extra });

const GROUP: NavGroup = {
  id: "people", label: "People", icon: null,
  children: [item("Artists", "/artists"), item("Clients", "/clients", { badge: 2 })],
};

const SECTIONS: NavSection[] = [
  { id: "a", entries: [item("Home", "/home", { end: true })] },
  { id: "b", label: "B", entries: [GROUP, item("Reports", "/reports")] },
];

describe("navSections helpers", () => {
  it("isNavGroup tells groups from links", () => {
    expect(isNavGroup(GROUP)).toBe(true);
    expect(isNavGroup(item("Home", "/home"))).toBe(false);
  });

  it("flattenNavItems lists every leaf link in display order", () => {
    expect(flattenNavItems(SECTIONS).map((i) => i.label)).toEqual(["Home", "Artists", "Clients", "Reports"]);
  });

  it("isNavItemActive matches by path prefix, honours `end`, and ignores the query string", () => {
    expect(isNavItemActive(item("Artists", "/artists"), "/artists/42")).toBe(true);
    expect(isNavItemActive(item("Home", "/platform", { end: true }), "/platform/studios")).toBe(false);
    expect(isNavItemActive(item("Schedule", "/schedule?artistId=9"), "/schedule")).toBe(true);
  });

  it("groupContainsActive is true only when a child matches the current path", () => {
    expect(groupContainsActive(GROUP, "/clients")).toBe(true);
    expect(groupContainsActive(GROUP, "/reports")).toBe(false);
  });

  it("groupBadgeTotal sums the children's badges", () => {
    expect(groupBadgeTotal(GROUP)).toBe(2);
  });

  it("pruneNavSections drops empty groups and then empty sections", () => {
    const emptyGroup: NavGroup = { id: "e", label: "Empty", icon: null, children: [] };
    const pruned = pruneNavSections([
      { id: "x", entries: [emptyGroup] },
      { id: "y", entries: [emptyGroup, item("Keep", "/keep")] },
    ]);
    expect(pruned).toHaveLength(1);
    expect(pruned[0].entries).toHaveLength(1);
  });

  it("withNavBadges applies badges to links inside groups and to top-level links, by label", () => {
    const out = withNavBadges(SECTIONS, { Artists: 5, Reports: 1 });
    const flat = flattenNavItems(out);
    expect(flat.find((i) => i.label === "Artists")?.badge).toBe(5);
    expect(flat.find((i) => i.label === "Reports")?.badge).toBe(1);
    expect(flat.find((i) => i.label === "Clients")?.badge).toBe(2);
    // input is not mutated
    expect(flattenNavItems(SECTIONS).find((i) => i.label === "Artists")?.badge).toBeUndefined();
  });
});

describe("navTourIdForStep", () => {
  const step = (targetSelector: string) => ({ targetSelector, title: "t", body: "b" });

  it("extracts the data-tour id of a nav target", () => {
    expect(navTourIdForStep(step('[data-tour="owner-billing-nav"]'))).toBe("owner-billing-nav");
  });

  it("returns null for a non-nav target", () => {
    expect(navTourIdForStep(step('[data-tour="owner-help-button"]'))).toBeNull();
  });
});
