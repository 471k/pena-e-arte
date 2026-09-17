import { useEffect, useState } from "react";
import { NavLink, Outlet, useLocation, useNavigate } from "react-router-dom";
import {
  CalendarDays, LayoutDashboard, Users, UserSquare, Palette, CreditCard,
  Receipt, Settings, PenLine, MessageSquareMore, BarChart3, ImagePlus, ShieldAlert, MessageCircle, Wallet,
  ListOrdered, ListChecks, Banknote, Gift, Package as PackageIcon,
  Megaphone, Tag, DollarSign, FileText, ScrollText,
} from "lucide-react";
import { cn } from "@/shared/utils/cn";
import { isInArtistContext } from "@/shared/utils/artistContext";
import { ReadOnlyBanner } from "@/shared/components/ReadOnlyBanner";
import { PlanLimitBanner } from "@/shared/components/PlanLimitBanner";
import { SuspensionBanner } from "@/shared/components/SuspensionBanner";
import { SoloStudioPublishBanner } from "@/shared/components/SoloStudioPublishBanner";
import { ArtistModeSwitcher } from "@/shared/components/ArtistModeSwitcher";
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
import { StudioJoinInviteBell } from "@/features/auth/components/StudioJoinInviteBell";
import { FeedbackDialog } from "@/features/feedback";
import { HelpMenu } from "@/features/help";
import { useGetMyStudioConductReportsQuery, useGetMyConductReportsAsArtistQuery } from "@/features/conduct-reports";
import { MessagesNavBadge, useChatHub } from "@/features/messaging";

const ONBOARDING_REDIRECT_KEY = "solo-owner-onboarding-redirect-done";

const NAV_ITEMS: NavItem[] = [
  { label: "Dashboard",        href: "/dashboard",         icon: <LayoutDashboard className="h-4 w-4" />, tourId: "owner-dashboard-nav" },
  { label: "Schedule",         href: "/schedule",          icon: <CalendarDays    className="h-4 w-4" /> },
  { label: "Artists",          href: "/artists",           icon: <Users           className="h-4 w-4" />, tourId: "owner-add-artist-nav" },
  { label: "Clients",          href: "/clients",           icon: <UserSquare      className="h-4 w-4" /> },
  { label: "Messages",         href: "/messages",          icon: <MessageCircle   className="h-4 w-4" />, tourId: "owner-messages-nav" },
  { label: "Designs",          href: "/designs",           icon: <Palette         className="h-4 w-4" /> },
  { label: "Intake Form",      href: "/intake-form-builder", icon: <FileText      className="h-4 w-4" /> },
  { label: "Payments",         href: "/payments",          icon: <CreditCard      className="h-4 w-4" /> },
  { label: "Waitlist",         href: "/waitlist",          icon: <ListOrdered     className="h-4 w-4" />, tourId: "owner-waitlist-nav" },
  { label: "Booth Rent",       href: "/booth-rent",        icon: <Banknote        className="h-4 w-4" /> },
  { label: "Gift Cards",       href: "/gift-cards",        icon: <Gift            className="h-4 w-4" /> },
  { label: "Packages",         href: "/packages",          icon: <PackageIcon     className="h-4 w-4" /> },
  { label: "Services",         href: "/services",          icon: <ListChecks      className="h-4 w-4" />, tourId: "owner-services-nav" },
  { label: "Deposit Rules",    href: "/deposit-rules",     icon: <DollarSign      className="h-4 w-4" />, tourId: "owner-deposit-rules-nav" },
  { label: "Promo Codes",      href: "/promo-codes",       icon: <Tag             className="h-4 w-4" /> },
  { label: "Billing",          href: "/billing",           icon: <Receipt         className="h-4 w-4" />, tourId: "owner-billing-nav" },
  { label: "Reports",          href: "/reports",           icon: <BarChart3       className="h-4 w-4" />, tourId: "owner-reports-nav" },
  { label: "Campaigns",        href: "/campaigns",         icon: <Megaphone       className="h-4 w-4" /> },
  { label: "Conduct Reports",  href: "/conduct-reports",   icon: <ShieldAlert     className="h-4 w-4" />, tourId: "owner-conduct-reports-nav" },
  { label: "Studio Settings",  href: "/studios/me",        icon: <Settings        className="h-4 w-4" />, tourId: "owner-studio-profile-nav" },
];

export function OwnerLayout() {
  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  const location = useLocation();
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
  const { data: myArtist, isLoading: myArtistLoading, isError: myArtistError } = useGetMyArtistQuery();
  // RTK Query only re-tags a query's cache entry on a SUCCESSFUL response — a 404 (no
  // profile) never gets tagged "Artist", so a later invalidation (e.g. deleting the profile)
  // does trigger a refetch, but that refetch's own failure leaves `data` holding the stale
  // pre-delete artist rather than clearing it. Must check isError explicitly, not just `data`,
  // or "My Portfolio" keeps showing a just-deleted profile until a hard reload.
  const hasArtistProfile = !myArtistError && !!myArtist;

  // Guided first step for a solo artist's owner account with no artist profile of their own
  // yet: route them straight into the existing "Enable my artist profile" form instead of
  // requiring them to find the Artists page. Fires once per browser session (sessionStorage
  // guard) so it never fights a deliberate later visit to another page.
  useEffect(() => {
    if (!studio?.isSolo || myArtistLoading || hasArtistProfile) return;
    if (location.pathname === "/artists") return;

    let alreadyRedirected = false;
    try {
      alreadyRedirected = sessionStorage.getItem(ONBOARDING_REDIRECT_KEY) === "1";
    } catch {
      // sessionStorage unavailable — treat as not-yet-redirected
    }
    if (alreadyRedirected) return;

    try {
      sessionStorage.setItem(ONBOARDING_REDIRECT_KEY, "1");
    } catch {
      // ignore — worst case the redirect fires again this session
    }
    navigate("/artists?onboarding=1", { replace: true });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [studio?.isSolo, myArtistLoading, hasArtistProfile]);
  const { data: openConductReports } = useGetMyStudioConductReportsQuery({ status: "Open" });
  const openConductReportCount = openConductReports?.length ?? 0;
  const withBadges = NAV_ITEMS.map((item) =>
    item.label === "Conduct Reports" ? { ...item, badge: openConductReportCount } : item,
  );
  const ownerNavItems: NavItem[] = hasArtistProfile
    ? [
        ...withBadges,
        { label: "My Portfolio", href: `/artists/${myArtist!.id}`, icon: <ImagePlus className="h-4 w-4" /> },
        { label: "My Earnings",  href: "/earnings",                icon: <Wallet    className="h-4 w-4" /> },
      ]
    : withBadges;

  // Only queried once the owner actually has a linked artist profile — every other owner
  // never needs this, and firing it unconditionally would be a wasted request on every load.
  const { data: myConductReportsAsArtist } = useGetMyConductReportsAsArtistQuery(undefined, {
    skip: !hasArtistProfile,
  });
  const myOpenConductReportCount =
    (myConductReportsAsArtist ?? []).filter((r) => r.status === "Open").length;

  // The owner's own dual-role artist identity — Schedule/Designs/Reports About Me link with
  // an explicit ?artistId= so those shared pages filter to "mine only" exactly the way they
  // already do for a real artist caller (GetAppointmentsQuery/GetDesignsQuery/
  // GetMyConductReportsAsArtistQuery all already support this for any caller, no backend
  // change needed — see docs/claude/architecture.md's Decisions Log). Deliberately excludes
  // every owner-only management item (Dashboard, Artists, Payments, Billing, Studio Settings,
  // Promo Codes, Booth Rent, Gift Cards, Packages, Campaigns, studio-wide Reports/Conduct
  // Reports) so the menu genuinely matches what a real invited artist would see, per the
  // request that owner and artist contexts each show only their own specific menu. "Owner
  // Dashboard" stays first as an escape hatch back to owner mode — the header switcher covers
  // this too, but only shows at sm+ widths.
  const artistNavItems: NavItem[] = myArtist
    ? [
        { label: "Owner Dashboard",  href: "/dashboard",                             icon: <LayoutDashboard className="h-4 w-4" /> },
        { label: "My Portfolio",     href: `/artists/${myArtist.id}`,                icon: <ImagePlus       className="h-4 w-4" /> },
        { label: "Schedule",         href: `/schedule?artistId=${myArtist.id}`,      icon: <CalendarDays    className="h-4 w-4" /> },
        { label: "Clients",          href: "/clients",                               icon: <UserSquare      className="h-4 w-4" /> },
        { label: "Messages",         href: "/messages",                              icon: <MessageCircle   className="h-4 w-4" /> },
        { label: "Designs",          href: `/designs?artistId=${myArtist.id}`,       icon: <Palette         className="h-4 w-4" /> },
        { label: "Intake Forms",     href: "/forms/intake",                          icon: <FileText        className="h-4 w-4" /> },
        { label: "Consent Forms",    href: "/forms/consent",                         icon: <ScrollText      className="h-4 w-4" /> },
        { label: "Deposit Rules",    href: "/deposit-rules",                         icon: <DollarSign      className="h-4 w-4" /> },
        { label: "Waitlist",         href: "/waitlist",                              icon: <ListOrdered     className="h-4 w-4" /> },
        { label: "My Earnings",      href: "/earnings",                              icon: <Wallet          className="h-4 w-4" /> },
        { label: "Reports About Me", href: `/conduct-reports?artistId=${myArtist.id}`, icon: <ShieldAlert   className="h-4 w-4" />, badge: myOpenConductReportCount },
      ]
    : [];

  const isArtistMode = isInArtistContext(location.pathname, location.search, myArtist?.id);
  const navItems: NavItem[] = isArtistMode && hasArtistProfile ? artistNavItems : ownerNavItems;

  function handleLogout() {
    dispatch(logout());
    navigate("/login", { replace: true });
  }

  return (
    <div className="min-h-screen flex flex-col bg-background">
      <SuspensionBanner studio={studio} />
      <ReadOnlyBanner />
      <PlanLimitBanner />
      <SoloStudioPublishBanner studio={studio} />
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
        {hasArtistProfile && myArtist && (
          <div className="hidden sm:flex ml-2 shrink-0">
            <ArtistModeSwitcher artistId={myArtist.id} />
          </div>
        )}
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
          <StudioJoinInviteBell enabled={!!studio?.isSolo} />
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
