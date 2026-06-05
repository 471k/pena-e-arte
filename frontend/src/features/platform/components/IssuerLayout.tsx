import { NavLink, Outlet } from "react-router-dom";
import { Building2, CreditCard, Globe, PenLine } from "lucide-react";
import { cn } from "@/shared/utils/cn";

const NAV_ITEMS = [
  { label: "Studios",  href: "/platform/studios", icon: <Building2 className="h-4 w-4" /> },
  { label: "Plans",    href: "/platform/plans",   icon: <CreditCard className="h-4 w-4" /> },
  { label: "Map",      href: "/map",               icon: <Globe className="h-4 w-4" /> },
];

export function IssuerLayout() {
  return (
    <div className="min-h-screen flex flex-col bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-20">
        <PenLine className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Platform Admin</span>

        <nav className="ml-6 flex items-center gap-1">
          {NAV_ITEMS.map(({ label, href, icon }) => (
            <NavLink
              key={href}
              to={href}
              className={({ isActive }) =>
                cn(
                  "flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm transition-colors",
                  isActive
                    ? "bg-primary text-primary-foreground"
                    : "text-muted-foreground hover:text-foreground hover:bg-muted"
                )
              }
            >
              {icon}
              {label}
            </NavLink>
          ))}
        </nav>
      </header>

      <div className="flex-1">
        <Outlet />
      </div>
    </div>
  );
}
