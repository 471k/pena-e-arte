import { useCallback, useEffect, useState } from "react";
import type { TourStep } from "@/shared/components/OnboardingTour";
import { Outlet, useLocation, useNavigate } from "react-router-dom";
import {
  CalendarDays, LayoutDashboard, Users, UserSquare, Palette, CreditCard,
  Receipt, Settings, PenLine, BarChart3, ImagePlus, ShieldAlert, MessageCircle, Wallet,
  ListOrdered, ListChecks, Banknote, Gift, Package as PackageIcon,
  Megaphone, Tag, DollarSign, FileText,
} from "lucide-react";
import { isInArtistContext } from "@/shared/utils/artistContext";
import { ReadOnlyBanner } from "@/shared/components/ReadOnlyBanner";
import { PlanLimitBanner } from "@/shared/components/PlanLimitBanner";
import { SuspensionBanner } from "@/shared/components/SuspensionBanner";
import { SoloStudioPublishBanner } from "@/shared/components/SoloStudioPublishBanner";
import { soloPublishBannerApplies } from "@/shared/components/soloPublishBanner";
import { ArtistModeSwitcher } from "@/shared/components/ArtistModeSwitcher";
import { UserMenu } from "@/shared/components/UserMenu";
import { HeaderActions } from "@/shared/components/HeaderActions";
import { HEADER_FOLD_BELOW_REM, useHeaderFold } from "@/shared/hooks/useHeaderFold";
import { NavDrawer } from "@/shared/components/NavDrawer";
import { AppSidebar } from "@/shared/components/AppSidebar";
import { SidebarToggle } from "@/shared/components/SidebarToggle";
import { useNavShell } from "@/shared/hooks/useNavShell";
import { withNavBadges } from "@/shared/utils/navSections";
import type { NavSection } from "@/shared/types/navItem";
import { buildArtistSections } from "@/layouts/artistNavSections";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { logout } from "@/features/auth/authSlice";
import { useSignalR } from "@/shared/hooks/useSignalR";
import { useGetSubscriptionQuery } from "@/features/billing/billingApi";
import { useGetMyStudioQuery } from "@/features/studios/studiosApi";
import { MoneyStudioContext } from "@/shared/components/moneyStudioContext";
import { useGetMyArtistQuery } from "@/features/artists/artistsApi";
import { StudioJoinInviteBell } from "@/features/auth/components/StudioJoinInviteBell";
import { FeedbackDialog } from "@/features/feedback";
import type { OwnerTourFacts } from "@/features/help/tours/ownerTour";
import { useGetMyJoinInvitesQuery } from "@/features/auth/authApi";
import { useGetMyStudioConductReportsQuery, useGetMyConductReportsAsArtistQuery } from "@/features/conduct-reports";
import { useChatHub } from "@/features/messaging";
import { Wordmark } from "@/shared/components/Wordmark";
import { StudioContextLabel } from "@/shared/components/StudioContextLabel";
import { MAIN_CONTENT_ID, SkipToContent } from "@/shared/components/SkipToContent";

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
  const { navOpen, setNavOpen, revealTourId, onBeforeTourStep, onTourEnd, navModal } = useNavShell();
  // Primes RTK Query caches so subscription + suspension state is known before child forms render.
  useGetSubscriptionQuery();
  const { data: studio, isLoading: studioLoading } = useGetMyStudioQuery();
  const moneyStudio = studio ? { countryCode: studio.countryCode, currency: studio.currency } : null;
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
  // The same query, with the same arguments and skip, that StudioJoinInviteBell renders from: RTK Query shares
  // one cache entry, so this adds no request. It lets the tour know whether the bell will render.
  const { data: joinInvites, isLoading: joinInvitesLoading } = useGetMyJoinInvitesQuery(undefined, { skip: !studio?.isSolo });
  // Whether each conditional owner-tour step's target renders, decided by the same values that render them.
  // `undefined` until every query they depend on has answered, so the tour waits instead of timing steps out.
  const ownerTourFacts: OwnerTourFacts | undefined = myArtistLoading || studioLoading || joinInvitesLoading
    ? undefined
    : {
      hasArtistProfile,
      showsPublishBanner: soloPublishBannerApplies(studio),
      hasJoinInvite: !!studio?.isSolo && (joinInvites?.length ?? 0) > 0,
    };

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

  // The owner's own dual-role artist identity — Schedule/Designs/Conduct Reports link with
  // an explicit ?artistId= so those shared pages filter to "mine only" exactly the way they
  // already do for a real artist caller (GetAppointmentsQuery/GetDesignsQuery/
  // GetMyConductReportsAsArtistQuery all already support this for any caller, no backend
  // change needed — see docs/claude/architecture.md's Decisions Log). Built by the same
  // buildArtistSections ArtistLayout uses, so it deliberately excludes every owner-only
  // management item (Dashboard, Artists, Payments, Billing, Studio Settings, Promo Codes, Booth
  // Rent, Gift Cards, Packages, Campaigns, studio-wide Reports/Conduct Reports) and the menu
  // genuinely matches what a real invited artist would see, per the request that owner and
  // artist contexts each show only their own specific menu. "Owner Dashboard" stays first as an
  // escape hatch back to owner mode; the Owner/Artist switch covers this too, in the header from
  // sm up and at the top of the nav drawer below that.
  const artistSections: NavSection[] = myArtist
    ? withNavBadges(
        buildArtistSections({
          scheduleHref:   `/schedule?artistId=${myArtist.id}`,
          designsHref:    `/designs?artistId=${myArtist.id}`,
          reportsHref:    `/conduct-reports?artistId=${myArtist.id}`,
          portfolioHref:  `/artists/${myArtist.id}`,
          ownerDashboard: true,
        }),
        { "Conduct Reports": myOpenConductReportCount },
      )
    : [];

  const isArtistMode = isInArtistContext(location.pathname, location.search, myArtist?.id);
  const navSections: NavSection[] = isArtistMode && hasArtistProfile ? artistSections : ownerSections;

  function handleLogout() {
    dispatch(logout());
    navigate("/login", { replace: true });
  }

  const { ref: headerRef, folded: headerFolded } = useHeaderFold<HTMLElement>(HEADER_FOLD_BELOW_REM.owner);
  // Folded, the header's Owner/Artist switch is gone and the drawer carries it, as below sm. So the tour's switch
  // step opens the drawer whenever the header is folded too, decided from the value that hides the header copy
  // (#430's rule), not left to time out looking for it.
  const onBeforeOwnerTourStep = useCallback((step: TourStep) => {
    onBeforeTourStep(step);
    if (headerFolded && step.inNavDrawerBelowSm) setNavOpen(true);
  }, [onBeforeTourStep, headerFolded, setNavOpen]);
  return (
    <div data-theme="after-hours" className="min-h-screen flex flex-col bg-background">
      <SkipToContent />
      <SuspensionBanner studio={studio} />
      <ReadOnlyBanner />
      <PlanLimitBanner />
      <SoloStudioPublishBanner studio={studio} />
      <header ref={headerRef} data-pinned-shell className="@container flex items-center gap-2 px-4 sm:px-6 h-14 border-b bg-background sticky top-0 z-20">
        <SidebarToggle />
        <PenLine className="h-5 w-5" />
        {/* Folded, the wordmark gives its room to the controls (S05: 200% text on a phone). */}
        {!headerFolded && <Wordmark />}
        <StudioContextLabel variant="header" />

        {/* Folded, the header switch goes and the drawer's copy (below) carries it, as on a phone. */}
        {hasArtistProfile && myArtist && !headerFolded && (
          <div className="hidden sm:flex ml-2 shrink-0">
            <ArtistModeSwitcher artistId={myArtist.id} />
          </div>
        )}
        {/* Below sm the header switch is hidden, so the drawer carries the same switch: without it a
            phone could only reach artist mode through "My Portfolio", which doesn't say it changes the
            whole menu (owner phone-navigation patch, 2026-11-12). */}
        <NavDrawer
          sections={navSections}
          title="TattooOS"
          context={<StudioContextLabel variant="drawer" />}
          open={navOpen}
          onOpenChange={setNavOpen}
          revealTourId={revealTourId}
          modal={navModal}
          top={hasArtistProfile && myArtist
            ? <ArtistModeSwitcher artistId={myArtist.id} layout="fill" onSwitch={() => setNavOpen(false)} />
            : undefined}
        />

        {/* ownerTourFacts: the values that render ArtistModeSwitcher, SoloStudioPublishBanner and
            StudioJoinInviteBell, so the tour and its targets can't disagree. The invite bell stays outside the fold:
            it renders only while invites are pending and carries that count, like the notification bell.
            Phi's list of what stays in the bar named four items, and this bell is not
            on it. But the list's reason was that anything carrying unread state stays visible, and the invite bell
            meets that reason. Following the reason over the letter is right when the letter was shorthand for it,
            so don't move this into the More menu to match the list. */}
        <HeaderActions
          folded={headerFolded}
          onFeedback={() => setFeedbackOpen(true)}
          showMessages
          help={{ onBeforeTourStep: onBeforeOwnerTourStep, onTourEnd, ownerTourFacts }}
          pinned={<StudioJoinInviteBell enabled={!!studio?.isSolo} />}
          userMenu={<UserMenu onLogout={handleLogout} context={<StudioContextLabel variant="drawer" />} compact={headerFolded} />}
        />
      </header>

      <div className="flex flex-1 min-h-0">
        <AppSidebar sections={navSections} revealTourId={revealTourId} />
        <div id={MAIN_CONTENT_ID} tabIndex={-1} className="flex-1 min-w-0 focus:outline-none">
          {/* Staff screens show lek next to euro for Albanian EUR studios (spec Q3, 2026-10-26). */}
          <MoneyStudioContext.Provider value={moneyStudio}>
            <Outlet />
          </MoneyStudioContext.Provider>
        </div>
      </div>
      <FeedbackDialog open={feedbackOpen} onOpenChange={setFeedbackOpen} />
    </div>
  );
}
