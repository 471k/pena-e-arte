import { NavLink, Outlet } from "react-router-dom";
import {
  CalendarDays, Users, Palette, FileText, ScrollText,
  DollarSign, Bell, PenLine,
} from "lucide-react";
import { cn } from "@/shared/utils/cn";
import { ReadOnlyBanner } from "@/shared/components/ReadOnlyBanner";

const NAV_ITEMS = [
  { label: "Schedule",      href: "/schedule",        icon: <CalendarDays className="h-4 w-4" /> },
  { label: "Clients",       href: "/clients",         icon: <Users        className="h-4 w-4" /> },
  { label: "Designs",       href: "/designs",         icon: <Palette      className="h-4 w-4" /> },
  { label: "Intake Forms",  href: "/forms/intake",    icon: <FileText     className="h-4 w-4" /> },
  { label: "Consent Forms", href: "/forms/consent",   icon: <ScrollText   className="h-4 w-4" /> },
  { label: "Deposit Rules", href: "/deposit-rules",   icon: <DollarSign   className="h-4 w-4" /> },
  { label: "Notifications", href: "/notifications",   icon: <Bell         className="h-4 w-4" /> },
];

export function ArtistLayout() {
  return (
    <div className="min-h-screen flex flex-col bg-background">
      <ReadOnlyBanner />
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-20">
        <PenLine className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Pena e Artë</span>

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
