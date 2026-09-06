import type { ReactNode } from "react";

export interface NavItem {
  label:      string;
  href:       string;
  icon:       ReactNode;
  tourId?:    string;
  end?:       boolean;   // exact-match routing, e.g. AdminLayout's Dashboard item
  badge?:     number;    // e.g. AdminLayout's open-feedback count
}
