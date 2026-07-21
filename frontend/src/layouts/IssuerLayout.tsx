import { NavLink, Outlet, useNavigate } from "react-router-dom";
import { BarChart3, Building2, CreditCard, HelpCircle, LayoutDashboard, MessageSquare, PenLine, Receipt, ScrollText, Share2 } from "lucide-react";
import { UserMenu } from "@/shared/components/UserMenu";
import { useAppDispatch } from "@/app/hooks";
import { logout } from "@/features/auth/authSlice";
import { cn } from "@/shared/utils/cn";
import { NotificationBell } from "@/features/notifications";
import { useGetFeedbackReportsQuery } from "@/features/feedback";
import { HelpMenu } from "@/features/help";

const NAV_ITEMS = [
  { label: "Dashboard",     href: "/platform",               icon: <LayoutDashboard className="h-4 w-4" />, tourId: "issuer-dashboard-nav" },
  { label: "Studios",       href: "/platform/studios",       icon: <Building2       className="h-4 w-4" />, tourId: "issuer-studios-nav" },
  { label: "Plans",         href: "/platform/plans",         icon: <CreditCard      className="h-4 w-4" />, tourId: "issuer-plans-nav" },
  { label: "Subscriptions", href: "/platform/subscriptions", icon: <Receipt         className="h-4 w-4" />, tourId: "issuer-subscriptions-nav" },
  { label: "Referrals",     href: "/platform/referrals",     icon: <Share2          className="h-4 w-4" /> },
  { label: "Reports",       href: "/platform/reports",       icon: <BarChart3       className="h-4 w-4" /> },
  { label: "Feedback",      href: "/platform/feedback",      icon: <MessageSquare   className="h-4 w-4" /> },
  { label: "Help Insights", href: "/platform/help-insights", icon: <HelpCircle      className="h-4 w-4" /> },
  { label: "Audit Log",     href: "/platform/audit-log",    icon: <ScrollText      className="h-4 w-4" />, tourId: "issuer-audit-log-nav" },
];

export function IssuerLayout() {
  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  const { data: openFeedback } = useGetFeedbackReportsQuery({ status: "Open" });
  const openCount = openFeedback?.length ?? 0;

  function handleLogout() {
    dispatch(logout());
    navigate("/login", { replace: true });
  }

  return (
    <div className="min-h-screen flex flex-col bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-20">
        <PenLine className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Platform Admin</span>

        <nav className="ml-6 flex items-center gap-1 overflow-x-auto scrollbar-none shrink min-w-0">
          {NAV_ITEMS.map(({ label, href, icon, tourId }) => (
            <NavLink
              key={href}
              to={href}
              end={href === "/platform"}
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
              {label === "Feedback" && openCount > 0 && (
                <span className="ml-1 min-w-[1.25rem] rounded-full bg-destructive px-1 py-0.5 text-[10px] font-medium text-destructive-foreground text-center">
                  {openCount > 99 ? "99+" : openCount}
                </span>
              )}
            </NavLink>
          ))}
        </nav>

        <div className="ml-auto flex items-center gap-3">
          <HelpMenu />
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
