import { useState } from "react";
import { NavLink, Outlet, useNavigate } from "react-router-dom";
import { Activity, BarChart3, Building2, CreditCard, HelpCircle, LayoutDashboard, MessageSquare, PenLine, Receipt, ScrollText, Share2 } from "lucide-react";
import { UserMenu } from "@/shared/components/UserMenu";
import { NavDrawer } from "@/shared/components/NavDrawer";
import { shouldOpenNavDrawerForTourStep } from "@/shared/utils/shouldOpenNavDrawerForTourStep";
import type { NavItem } from "@/shared/types/navItem";
import { useAppDispatch } from "@/app/hooks";
import { logout } from "@/features/auth/authSlice";
import { cn } from "@/shared/utils/cn";
import { NotificationBell } from "@/features/notifications";
import { useGetFeedbackReportsQuery } from "@/features/feedback";
import { HelpMenu } from "@/features/help";

const NAV_ITEMS: NavItem[] = [
  { label: "Dashboard",     href: "/platform",               icon: <LayoutDashboard className="h-4 w-4" />, tourId: "issuer-dashboard-nav", end: true },
  { label: "Live Traffic",  href: "/platform/traffic",       icon: <Activity        className="h-4 w-4" />, tourId: "issuer-traffic-nav" },
  { label: "Studios",       href: "/platform/studios",       icon: <Building2       className="h-4 w-4" />, tourId: "issuer-studios-nav" },
  { label: "Plans",         href: "/platform/plans",         icon: <CreditCard      className="h-4 w-4" />, tourId: "issuer-plans-nav" },
  { label: "Subscriptions", href: "/platform/subscriptions", icon: <Receipt         className="h-4 w-4" />, tourId: "issuer-subscriptions-nav" },
  { label: "Referrals",     href: "/platform/referrals",     icon: <Share2          className="h-4 w-4" /> },
  { label: "Reports",       href: "/platform/reports",       icon: <BarChart3       className="h-4 w-4" /> },
  { label: "Feedback",      href: "/platform/feedback",      icon: <MessageSquare   className="h-4 w-4" /> },
  { label: "Help Insights", href: "/platform/help-insights", icon: <HelpCircle      className="h-4 w-4" /> },
  { label: "Audit Log",     href: "/platform/audit-log",     icon: <ScrollText      className="h-4 w-4" />, tourId: "issuer-audit-log-nav" },
];

export function IssuerLayout() {
  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  const [navOpen, setNavOpen] = useState(false);
  const { data: openFeedback } = useGetFeedbackReportsQuery({ status: "Open" });
  const openCount = openFeedback?.length ?? 0;
  const navItems: NavItem[] = NAV_ITEMS.map((item) =>
    item.label === "Feedback" ? { ...item, badge: openCount } : item,
  );

  function handleLogout() {
    dispatch(logout());
    navigate("/login", { replace: true });
  }

  return (
    <div className="min-h-screen flex flex-col bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-20">
        <PenLine className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Platform Admin</span>

        <nav className="hidden lg:flex ml-6 items-center gap-1 overflow-x-auto scrollbar-none shrink min-w-0">
          {navItems.map(({ label, href, icon, tourId, end, badge }) => (
            <NavLink
              key={href}
              to={href}
              end={end}
              data-tour={tourId}
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
              {!!badge && badge > 0 && (
                <span className="ml-1 min-w-[1.25rem] rounded-full bg-destructive px-1 py-0.5 text-[10px] font-medium text-destructive-foreground text-center">
                  {badge > 99 ? "99+" : badge}
                </span>
              )}
            </NavLink>
          ))}
        </nav>
        <NavDrawer navItems={navItems} title="Platform Admin" open={navOpen} onOpenChange={setNavOpen} />

        <div className="ml-auto flex items-center gap-3">
          <HelpMenu onBeforeTourStep={(step) => setNavOpen(shouldOpenNavDrawerForTourStep(step))} />
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
