import { useState } from "react";
import { NavLink, Outlet, useNavigate } from "react-router-dom";
import {
  CalendarDays, LayoutDashboard, Users, UserSquare, Palette, CreditCard,
  Receipt, Settings, PenLine, MessageSquareMore, BarChart3,
} from "lucide-react";
import { cn } from "@/shared/utils/cn";
import { ReadOnlyBanner } from "@/shared/components/ReadOnlyBanner";
import { PlanLimitBanner } from "@/shared/components/PlanLimitBanner";
import { SuspensionBanner } from "@/shared/components/SuspensionBanner";
import { UserMenu } from "@/shared/components/UserMenu";
import { Button } from "@/shared/components/ui/button";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { logout } from "@/features/auth/authSlice";
import { useSignalR } from "@/shared/hooks/useSignalR";
import { useGetSubscriptionQuery } from "@/features/billing/billingApi";
import { useGetMyStudioQuery } from "@/features/studios/studiosApi";
import { NotificationBell } from "@/features/notifications";
import { FeedbackDialog } from "@/features/feedback";
import { HelpMenu } from "@/features/help";

const NAV_ITEMS = [
  { label: "Dashboard",       href: "/dashboard",  icon: <LayoutDashboard className="h-4 w-4" />, tourId: "owner-dashboard-nav" },
  { label: "Schedule",        href: "/schedule",   icon: <CalendarDays    className="h-4 w-4" /> },
  { label: "Artists",         href: "/artists",    icon: <Users           className="h-4 w-4" />, tourId: "owner-add-artist-nav" },
  { label: "Clients",         href: "/clients",    icon: <UserSquare      className="h-4 w-4" /> },
  { label: "Designs",         href: "/designs",    icon: <Palette         className="h-4 w-4" /> },
  { label: "Payments",        href: "/payments",   icon: <CreditCard      className="h-4 w-4" /> },
  { label: "Billing",         href: "/billing",    icon: <Receipt         className="h-4 w-4" />, tourId: "owner-billing-nav" },
  { label: "Reports",         href: "/reports",    icon: <BarChart3       className="h-4 w-4" />, tourId: "owner-reports-nav" },
  { label: "Studio Settings", href: "/studios/me", icon: <Settings        className="h-4 w-4" />, tourId: "owner-studio-profile-nav" },
];

export function OwnerLayout() {
  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  const tenantId = useAppSelector((s) => s.auth.tenantId);
  useSignalR(tenantId);
  const [feedbackOpen, setFeedbackOpen] = useState(false);
  // Primes RTK Query caches so subscription + suspension state is known before child forms render.
  useGetSubscriptionQuery();
  const { data: studio } = useGetMyStudioQuery();

  function handleLogout() {
    dispatch(logout());
    navigate("/login", { replace: true });
  }

  return (
    <div className="min-h-screen flex flex-col bg-background">
      <SuspensionBanner studio={studio} />
      <ReadOnlyBanner />
      <PlanLimitBanner />
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-20">
        <PenLine className="h-5 w-5" />
        <span className="font-semibold tracking-tight">TattooOS</span>

        <nav className="ml-6 flex items-center gap-1 overflow-x-auto scrollbar-none shrink min-w-0">
          {NAV_ITEMS.map(({ label, href, icon, tourId }) => (
            <NavLink
              key={href}
              to={href}
              data-tour={tourId}
              className={({ isActive }) =>
                cn(
                  "flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm transition-colors shrink-0",
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

        <div className="ml-auto flex items-center gap-3">
          <Button
            variant="ghost"
            size="icon"
            className="h-8 w-8"
            onClick={() => setFeedbackOpen(true)}
            title="Send feedback"
            aria-label="Send feedback"
          >
            <MessageSquareMore className="h-4 w-4" />
          </Button>
          <HelpMenu />
          <NotificationBell />
          <UserMenu onLogout={handleLogout} />
        </div>
      </header>

      <div className="flex-1">
        <Outlet />
      </div>
      <FeedbackDialog open={feedbackOpen} onOpenChange={setFeedbackOpen} />
    </div>
  );
}
