import { useState } from "react";
import { NavLink, Outlet, useNavigate } from "react-router-dom";
import {
  CalendarDays, Palette, FileText, ScrollText, User, PenLine, Building2, MessageCircle,
} from "lucide-react";
import { cn } from "@/shared/utils/cn";
import { ReadOnlyBanner } from "@/shared/components/ReadOnlyBanner";
import { PlanLimitBanner } from "@/shared/components/PlanLimitBanner";
import { SuspensionBanner } from "@/shared/components/SuspensionBanner";
import { UserMenu } from "@/shared/components/UserMenu";
import { NavDrawer } from "@/shared/components/NavDrawer";
import { shouldOpenNavDrawerForTourStep } from "@/shared/utils/shouldOpenNavDrawerForTourStep";
import type { NavItem } from "@/shared/types/navItem";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { logout } from "@/features/auth/authSlice";
import { useSignalR } from "@/shared/hooks/useSignalR";
import { NotificationBell } from "@/features/notifications";
import { HelpMenu } from "@/features/help";
import { MessagesNavBadge, useChatHub } from "@/features/messaging";

const NAV_ITEMS: NavItem[] = [
  { label: "Book Appointment", href: "/book",        icon: <CalendarDays className="h-4 w-4" />, tourId: "client-book-nav" },
  { label: "My Studios",       href: "/my-studios",  icon: <Building2    className="h-4 w-4" />, tourId: "client-my-studios-nav" },
  { label: "Messages",         href: "/messages",    icon: <MessageCircle className="h-4 w-4" />, tourId: "client-messages-nav" },
  { label: "My Designs",       href: "/designs",       icon: <Palette      className="h-4 w-4" />, tourId: "client-designs-nav" },
  { label: "Intake Forms",     href: "/forms/intake",  icon: <FileText     className="h-4 w-4" /> },
  { label: "Consent Forms",    href: "/forms/consent", icon: <ScrollText   className="h-4 w-4" /> },
  { label: "My Profile",       href: "/clients/me",    icon: <User         className="h-4 w-4" /> },
];

export function ClientLayout() {
  const dispatch  = useAppDispatch();
  const navigate  = useNavigate();
  const tenantId  = useAppSelector((s) => s.auth.tenantId);
  useSignalR(tenantId);
  useChatHub();
  const [navOpen, setNavOpen] = useState(false);

  function handleLogout() {
    dispatch(logout());
    navigate("/login", { replace: true });
  }

  return (
    <div className="min-h-screen flex flex-col bg-background">
      <SuspensionBanner role="client" />
      <ReadOnlyBanner />
      <PlanLimitBanner />
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-20">
        <PenLine className="h-5 w-5" />
        <span className="font-semibold tracking-tight">TattooOS</span>

        <nav className="hidden lg:flex ml-6 items-center gap-1 overflow-x-auto scrollbar-none shrink min-w-0">
          {NAV_ITEMS.map(({ label, href, icon, tourId }) => (
            <NavLink
              key={href}
              to={href}
              data-tour={tourId}
              className={({ isActive }) =>
                cn(
                  "flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm transition-colors shrink-0",
                  isActive
                    ? "bg-violet-600 text-white"
                    : "text-muted-foreground hover:text-foreground hover:bg-muted"
                )
              }
              aria-label={label}
            >
              {icon}
              {label}
            </NavLink>
          ))}
        </nav>
        <NavDrawer navItems={NAV_ITEMS} title="TattooOS" open={navOpen} onOpenChange={setNavOpen} />

        <div className="ml-auto flex items-center gap-3">
          <HelpMenu onBeforeTourStep={(step) => setNavOpen(shouldOpenNavDrawerForTourStep(step))} />
          <MessagesNavBadge />
          <NotificationBell />
          <UserMenu onLogout={handleLogout} />
        </div>
      </header>

      <div className="flex-1">
        <Outlet />
      </div>
    </div>
  );
}
