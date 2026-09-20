import { useEffect, useState } from "react";
import { Outlet, useLocation, useNavigate } from "react-router-dom";
import {
  CalendarDays, LayoutDashboard, Users, UserSquare, Palette, CreditCard,
  Receipt, Settings, PenLine, MessageSquareMore, BarChart3, ImagePlus, ShieldAlert, MessageCircle, Wallet,
  ListOrdered, ListChecks, Banknote, Gift, Package as PackageIcon,
  Megaphone, Tag, DollarSign, FileText,
} from "lucide-react";
import { isInArtistContext } from "@/shared/utils/artistContext";
import { ReadOnlyBanner } from "@/shared/components/ReadOnlyBanner";
import { PlanLimitBanner } from "@/shared/components/PlanLimitBanner";
import { SuspensionBanner } from "@/shared/components/SuspensionBanner";
import { SoloStudioPublishBanner } from "@/shared/components/SoloStudioPublishBanner";
import { ArtistModeSwitcher } from "@/shared/components/ArtistModeSwitcher";
import { UserMenu } from "@/shared/components/UserMenu";
import { Button } from "@/shared/components/ui/button";
import { NavDrawer } from "@/shared/components/NavDrawer";
import { AppSidebar } from "@/shared/components/AppSidebar";
import { useNavShell } from "@/shared/hooks/useNavShell";
import { withNavBadges } from "@/shared/utils/navSections";
import type { NavSection } from "@/shared/types/navItem";
import { buildArtistSections } from "@/layouts/artistNavSections";
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

const ICON = "h-4 w-4";

const OWNER_SECTIONS: NavSection[] = [
  {
    id: "overview",
    entries: [
      { label: "Dashboard", href: "/dashboard", icon: <LayoutDashboard className={ICON} />, tourId: "owner-dashboard-nav" },
      { label: "Schedule",  href: "/schedule",  icon: <CalendarDays className={ICON} /> },
      { label: "Messages",  href: "/messages",  icon: <MessageCircle className={ICON} />, tourId: "owner-messages-nav" },
    ],
  },
  {
    id: "operations",
    label: "Operations",
    entries: [
      {
        id: "people", label: "People", icon: <Users className={ICON} />,
        children: [
          { label: "Artists", href: "/artists", icon: <Users className={ICON} />,      tourId: "owner-add-artist-nav" },
          { label: "Clients", href: "/clients", icon: <UserSquare className={ICON} /> },
        ],
      },
      {
        id: "client-work", label: "Client work", icon: <Palette className={ICON} />,
        children: [
          { label: "Designs",     href: "/designs",             icon: <Palette className={ICON} /> },
          { label: "Intake Form", href: "/intake-form-builder", icon: <FileText className={ICON} /> },
          { label: "Waitlist",    href: "/waitlist",            icon: <ListOrdered className={ICON} />, tourId: "owner-waitlist-nav" },
        ],
      },
    ],
  },
  {
    id: "sales",
    label: "Sales",
    entries: [
      {
        id: "payments", label: "Payments", icon: <CreditCard className={ICON} />,
        children: [
          { label: "Payments",      href: "/payments",      icon: <CreditCard className={ICON} /> },
          { label: "Deposit Rules", href: "/deposit-rules", icon: <DollarSign className={ICON} />, tourId: "owner-deposit-rules-nav" },
          { label: "Booth Rent",    href: "/booth-rent",    icon: <Banknote className={ICON} /> },
        ],
      },
      {
        id: "catalog", label: "Catalog", icon: <PackageIcon className={ICON} />,
        children: [
          { label: "Services",   href: "/services",   icon: <ListChecks className={ICON} />, tourId: "owner-services-nav" },
          { label: "Packages",   href: "/packages",   icon: <PackageIcon className={ICON} /> },
          { label: "Gift Cards", href: "/gift-cards", icon: <Gift className={ICON} /> },
        ],
      },
    ],
  },
  {
    id: "growth",
    label: "Growth",
    entries: [
      {
        id: "marketing", label: "Marketing", icon: <Megaphone className={ICON} />,
        children: [
          { label: "Campaigns",   href: "/campaigns",   icon: <Megaphone className={ICON} /> },
          { label: "Promo Codes", href: "/promo-codes", icon: <Tag className={ICON} /> },
        ],
      },
      { label: "Reports", href: "/reports", icon: <BarChart3 className={ICON} />, tourId: "owner-reports-nav" },
    ],
  },
  {
    id: "studio",
    label: "Studio",
    entries: [
      { label: "Conduct Reports", href: "/conduct-reports", icon: <ShieldAlert className={ICON} />, tourId: "owner-conduct-reports-nav" },
      {
        id: "manage-studio", label: "Manage studio", icon: <Settings className={ICON} />,
        children: [
          { label: "Studio Settings", href: "/studios/me", icon: <Settings className={ICON} />, tourId: "owner-studio-profile-nav" },
          { label: "Billing",         href: "/billing",    icon: <Receipt className={ICON} />,  tourId: "owner-billing-nav" },
        ],
      },
    ],
  },
];

export function OwnerLayout() {
  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  const location = useLocation();
  const tenantId = useAppSelector((s) => s.auth.tenantId);
  useSignalR(tenantId);
  useChatHub();
  const [feedbackOpen, setFeedbackOpen] = useState(false);
  const { navOpen, setNavOpen, revealTourId, onBeforeTourStep } = useNavShell();
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
  // The owner's own dual-role artist identity gets its own section so "My Portfolio" / "My Earnings"
  // stay one click away from owner mode without cluttering the studio-management groups.
  const ownerSections: NavSection[] = withNavBadges(
    hasArtistProfile
      ? [
          ...OWNER_SECTIONS,
          {
            id: "my-artist-profile",
            label: "My artist profile",
            entries: [
              { label: "My Portfolio", href: `/artists/${myArtist!.id}`, icon: <ImagePlus className={ICON} /> },
              { label: "My Earnings",  href: "/earnings",                icon: <Wallet    className={ICON} /> },
            ],
          },
        ]
      : OWNER_SECTIONS,
    { "Conduct Reports": openConductReportCount },
  );

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
  // change needed — see docs/claude/architecture.md's Decisions Log). Built by the same
  // buildArtistSections ArtistLayout uses, so it deliberately excludes every owner-only
  // management item (Dashboard, Artists, Payments, Billing, Studio Settings, Promo Codes, Booth
  // Rent, Gift Cards, Packages, Campaigns, studio-wide Reports/Conduct Reports) and the menu
  // genuinely matches what a real invited artist would see, per the request that owner and
  // artist contexts each show only their own specific menu. "Owner Dashboard" stays first as an
  // escape hatch back to owner mode — the header switcher covers this too, but only shows at sm+
  // widths.
  const artistSections: NavSection[] = myArtist
    ? withNavBadges(
        buildArtistSections({
          scheduleHref:   `/schedule?artistId=${myArtist.id}`,
          designsHref:    `/designs?artistId=${myArtist.id}`,
          reportsHref:    `/conduct-reports?artistId=${myArtist.id}`,
          portfolioHref:  `/artists/${myArtist.id}`,
          ownerDashboard: true,
        }),
        { "Reports About Me": myOpenConductReportCount },
      )
    : [];

  const isArtistMode = isInArtistContext(location.pathname, location.search, myArtist?.id);
  const navSections: NavSection[] = isArtistMode && hasArtistProfile ? artistSections : ownerSections;

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
      <header className="flex items-center gap-2 px-6 h-14 border-b bg-background sticky top-0 z-20">
        <PenLine className="h-5 w-5" />
        <span className="font-semibold tracking-tight">TattooOS</span>

        {hasArtistProfile && myArtist && (
          <div className="hidden sm:flex ml-2 shrink-0">
            <ArtistModeSwitcher artistId={myArtist.id} />
          </div>
        )}
        <NavDrawer sections={navSections} title="TattooOS" open={navOpen} onOpenChange={setNavOpen} revealTourId={revealTourId} />

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
          <HelpMenu onBeforeTourStep={onBeforeTourStep} />
          <MessagesNavBadge />
          <StudioJoinInviteBell enabled={!!studio?.isSolo} />
          <NotificationBell />
          <UserMenu onLogout={handleLogout} />
        </div>
      </header>

      <div className="flex flex-1 min-h-0">
        <AppSidebar sections={navSections} revealTourId={revealTourId} />
        <div className="flex-1 min-w-0">
          <Outlet />
        </div>
      </div>
      <FeedbackDialog open={feedbackOpen} onOpenChange={setFeedbackOpen} />
    </div>
  );
}
