import type { ReactNode } from "react";

export interface NavItem {
  label:      string;
  href:       string;
  icon:       ReactNode;
  tourId?:    string;
  end?:       boolean;   // exact-match routing, e.g. IssuerLayout's Dashboard item
  badge?:     number;    // e.g. IssuerLayout's open-feedback count
}
