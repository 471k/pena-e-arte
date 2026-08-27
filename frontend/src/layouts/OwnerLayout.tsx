import { useState } from "react";
import { NavLink, Outlet, useNavigate } from "react-router-dom";
import {
  CalendarDays, LayoutDashboard, Users, UserSquare, Palette, CreditCard,
  Receipt, Settings, PenLine, MessageSquareMore, BarChart3, ImagePlus, ShieldAlert, MessageCircle,
} from "lucide-react";
import { cn } from "@/shared/utils/cn";
import { ReadOnlyBanner } from "@/shared/components/ReadOnlyBanner";
import { PlanLimitBanner } from "@/shared/components/PlanLimitBanner";
import { SuspensionBanner } from "@/shared/components/SuspensionBanner";
import { UserMenu } from "@/shared/components/UserMenu";
import { Button } from "@/shared/components/ui/button";
import { NavDrawer } from "@/shared/components/NavDrawer";
import { shouldOpenNavDrawerForTourStep } from "@/shared/utils/shouldOpenNavDrawerForTourStep";
import type { NavItem } from "@/shared/types/navItem";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { logout } from "@/features/auth/authSlice";
import { useSignalR } from "@/shared/hooks/useSignalR";
import { useGetSubscriptionQuery } from "@/features/billing/billingApi";
import { useGetMyStudioQuery } from "@/features/studios/studiosApi";
import { useGetMyArtistQuery } from "@/features/artists/artistsApi";
import { NotificationBell } from "@/features/notifications";
import { FeedbackDialog } from "@/features/feedback";
import { HelpMenu } from "@/features/help";
import { useGetMyStudioConductReportsQuery } from "@/features/conduct-reports";
import { MessagesNavBadge, useChatHub } from "@/features/messaging";

const NAV_ITEMS: NavItem[] = [
  { label: "Dashboard",        href: "/dashboard",         icon: <LayoutDashboard className="h-4 w-4" />, tourId: "owner-dashboard-nav" },
  { label: "Schedule",         href: "/schedule",          icon: <CalendarDays    className="h-4 w-4" /> },
  { label: "Artists",          href: "/artists",           icon: <Users           className="h-4 w-4" />, tourId: "owner-add-artist-nav" },
  { label: "Clients",          href: "/clients",           icon: <UserSquare      className="h-4 w-4" /> },
  { label: "Messages",         href: "/messages",          icon: <MessageCircle   className="h-4 w-4" />, tourId: "owner-messages-nav" },
  { label: "Designs",          href: "/designs",           icon: <Palette         className="h-4 w-4" /> },
  { label: "Payments",         href: "/payments",          icon: <CreditCard      className="h-4 w-4" /> },
  { label: "Billing",          href: "/billing",           icon: <Receipt         className="h-4 w-4" />, tourId: "owner-billing-nav" },
  { label: "Reports",          href: "/reports",           icon: <BarChart3       className="h-4 w-4" />, tourId: "owner-reports-nav" },
  { label: "Conduct Reports",  href: "/conduct-reports",   icon: <ShieldAlert     className="h-4 w-4" />, tourId: "owner-conduct-reports-nav" },
  { label: "Studio Settings",  href: "/studios/me",        icon: <Settings        className="h-4 w-4" />, tourId: "owner-studio-profile-nav" },
];

export function OwnerLayout() {
  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  const tenantId = useAppSelector((s) => s.auth.tenantId);
  useSignalR(tenantId);
  useChatHub();
  const [feedbackOpen, setFeedbackOpen] = useState(false);
  const [navOpen, setNavOpen] = useState(false);
  // Primes RTK Query caches so subscription + suspension state is known before child forms render.
  useGetSubscriptionQuery();
  const { data: studio } = useGetMyStudioQuery();
  // Fires unconditionally for every owner (most won't have a profile yet — a normal 404 each
  // load, exactly like ArtistLayout already does for every artist). RTK Query dedupes this
  // against the same call ArtistListPage's "Become an artist" CTA makes via the shared
  // "Artist" cache tag.
  const { data: myArtist } = useGetMyArtistQuery();
  const { data: openConductReports } = useGetMyStudioConductReportsQuery({ status: "Open" });
  const openConductReportCount = openConductReports?.length ?? 0;
  const withBadges = NAV_ITEMS.map((item) =>
    item.label === "Conduct Reports" ? { ...item, badge: openConductReportCount } : item,
  );
  const navItems: NavItem[] = myArtist
    ? [...withBadges, { label: "My Portfolio", href: `/artists/${myArtist.id}`, icon: <ImagePlus className="h-4 w-4" /> }]
    : withBadges;

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

        <nav className="hidden lg:flex ml-6 items-center gap-1 overflow-x-auto scrollbar-none shrink min-w-0">
          {navItems.map(({ label, href, icon, tourId, badge }) => (
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
              {!!badge && badge > 0 && (
                <span className="ml-1 min-w-[1.25rem] rounded-full bg-destructive px-1 py-0.5 text-[10px] font-medium text-destructive-foreground text-center">
                  {badge > 99 ? "99+" : badge}
                </span>
              )}
            </NavLink>
          ))}
        </nav>
        <NavDrawer navItems={navItems} title="TattooOS" open={navOpen} onOpenChange={setNavOpen} />

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
          <HelpMenu onBeforeTourStep={(step) => setNavOpen(shouldOpenNavDrawerForTourStep(step))} />
          <MessagesNavBadge />
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
