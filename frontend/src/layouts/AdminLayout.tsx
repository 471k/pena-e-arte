import { Outlet, useNavigate } from "react-router-dom";
import { Activity, BarChart3, Building2, CreditCard, HelpCircle, LayoutDashboard, MessageSquare, PenLine, Receipt, ScrollText, Share2, ShieldAlert, Landmark } from "lucide-react";
import { UserMenu } from "@/shared/components/UserMenu";
import { HeaderActions } from "@/shared/components/HeaderActions";
import { HEADER_FOLD_BELOW_REM, useHeaderFold } from "@/shared/hooks/useHeaderFold";
import { NavDrawer } from "@/shared/components/NavDrawer";
import { AppSidebar } from "@/shared/components/AppSidebar";
import { SidebarToggle } from "@/shared/components/SidebarToggle";
import { useNavShell } from "@/shared/hooks/useNavShell";
import { withNavBadges } from "@/shared/utils/navSections";
import type { NavSection } from "@/shared/types/navItem";
import { useAppDispatch } from "@/app/hooks";
import { logout } from "@/features/auth/authSlice";
import { useGetFeedbackReportsQuery } from "@/features/feedback";
import { useGetPlatformConductReportsQuery } from "@/features/conduct-reports";
import { useAdminNotificationHub } from "@/shared/hooks/useAdminNotificationHub";
import { MAIN_CONTENT_ID, SkipToContent } from "@/shared/components/SkipToContent";

const ICON = "h-4 w-4";

const NAV_SECTIONS: NavSection[] = [
  {
    id: "overview",
    entries: [
      { label: "Dashboard", href: "/platform", icon: <LayoutDashboard className={ICON} />, tourId: "admin-dashboard-nav", end: true },
    ],
  },
  {
    id: "observe",
    label: "Observe",
    entries: [
      { label: "Live Traffic", href: "/platform/traffic",   icon: <Activity className={ICON} />,   tourId: "admin-traffic-nav" },
      { label: "Reports",      href: "/platform/reports",   icon: <BarChart3 className={ICON} /> },
      { label: "Audit Log",    href: "/platform/audit-log", icon: <ScrollText className={ICON} />, tourId: "admin-audit-log-nav" },
    ],
  },
  {
    id: "customers",
    label: "Customers",
    entries: [
      { label: "Studios",   href: "/platform/studios",   icon: <Building2 className={ICON} />, tourId: "admin-studios-nav" },
      { label: "Referrals", href: "/platform/referrals", icon: <Share2 className={ICON} /> },
      {
        id: "billing", label: "Billing", icon: <Landmark className={ICON} />,
        children: [
          { label: "Plans",         href: "/platform/plans",         icon: <CreditCard className={ICON} />, tourId: "admin-plans-nav" },
          { label: "Subscriptions", href: "/platform/subscriptions", icon: <Receipt className={ICON} />,    tourId: "admin-subscriptions-nav" },
        ],
      },
    ],
  },
  {
    id: "support",
    label: "Support",
    entries: [
      { label: "Feedback",        href: "/platform/feedback",        icon: <MessageSquare className={ICON} /> },
      { label: "Conduct Reports", href: "/platform/conduct-reports", icon: <ShieldAlert className={ICON} />, tourId: "admin-conduct-reports-nav" },
      { label: "Help Insights",   href: "/platform/help-insights",   icon: <HelpCircle className={ICON} /> },
    ],
  },
];

export function AdminLayout() {
  useAdminNotificationHub();
  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  const { navOpen, setNavOpen, revealTourId, onBeforeTourStep, onTourEnd, navModal } = useNavShell();
  const { data: openFeedback } = useGetFeedbackReportsQuery({ status: "Open" });
  const openCount = openFeedback?.length ?? 0;
  const { data: openConductReports } = useGetPlatformConductReportsQuery({ status: "Open" });
  const openConductReportCount = openConductReports?.length ?? 0;
  const navSections: NavSection[] = withNavBadges(NAV_SECTIONS, {
    "Feedback": openCount,
    "Conduct Reports": openConductReportCount,
  });

  function handleLogout() {
    dispatch(logout());
    navigate("/login", { replace: true });
  }

  const { ref: headerRef, folded: headerFolded } = useHeaderFold<HTMLElement>(HEADER_FOLD_BELOW_REM.admin);
  return (
    <div data-theme="after-hours" className="min-h-screen flex flex-col bg-background">
      <SkipToContent />
      <header ref={headerRef} data-pinned-shell className="flex items-center gap-2 px-4 sm:px-6 h-14 border-b bg-background sticky top-0 z-20">
        <SidebarToggle />
        <PenLine className="h-5 w-5" />
        {/* Folded, the title gives its room to the controls (S05: 200% text on a phone); the drawer still names it. */}
        {!headerFolded && <span className="font-semibold tracking-tight">Platform Admin</span>}

        <NavDrawer sections={navSections} title="Platform Admin" open={navOpen} onOpenChange={setNavOpen} revealTourId={revealTourId} modal={navModal} />

        {/* Help is the admin's only foldable control, so it stays inline: a one-item menu would save nothing. */}
        <HeaderActions
          folded={headerFolded}
          help={{ onBeforeTourStep, onTourEnd }}
          userMenu={<UserMenu onLogout={handleLogout} compact={headerFolded} />}
        />
      </header>

      <div className="flex flex-1 min-h-0">
        <AppSidebar sections={navSections} revealTourId={revealTourId} />
        <div id={MAIN_CONTENT_ID} tabIndex={-1} className="flex-1 min-w-0 focus:outline-none">
          <Outlet />
        </div>
      </div>
    </div>
  );
}
