import type { ReactNode } from "react";

export interface NavItem {
  label:      string;
  href:       string;
  icon:       ReactNode;
  tourId?:    string;
  end?:       boolean;   // exact-match routing, e.g. AdminLayout's Dashboard item
  badge?:     number;    // e.g. AdminLayout's open-feedback count
}

/** An expandable category: a parent row with an icon whose children are revealed inline. */
export interface NavGroup {
  id:       string;      // stable key for open/closed state
  label:    string;
  icon:     ReactNode;
  children: NavItem[];
}

export type NavEntry = NavItem | NavGroup;

/** A labelled block of entries, e.g. Cloudflare's "Observe" / "Build". Omit `label` for the unlabelled top block. */
export interface NavSection {
  id:      string;
  label?:  string;
  entries: NavEntry[];
}
