import { useState } from "react";
import { Outlet, useNavigate } from "react-router-dom";
import { ReadOnlyBanner } from "@/shared/components/ReadOnlyBanner";
import { PlanLimitBanner } from "@/shared/components/PlanLimitBanner";
import { SuspensionBanner } from "@/shared/components/SuspensionBanner";
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
import { FeedbackDialog } from "@/features/feedback";
import { useSignalR } from "@/shared/hooks/useSignalR";
import { useGetMyArtistQuery } from "@/features/artists/artistsApi";
import { useGetMyStudioQuery } from "@/features/studios/studiosApi";
import { MoneyStudioContext } from "@/shared/components/moneyStudioContext";
import { useGetMyConductReportsAsArtistQuery } from "@/features/conduct-reports";
import { useChatHub } from "@/features/messaging";
import { RoleHeaderBrand } from "@/shared/components/RoleHeaderBrand";
import { StudioContextLabel } from "@/shared/components/StudioContextLabel";
import { MAIN_CONTENT_ID, SkipToContent } from "@/shared/components/SkipToContent";

export function ArtistLayout() {
  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  const tenantId = useAppSelector((s) => s.auth.tenantId);
  useSignalR(tenantId);
  useChatHub();
  const [feedbackOpen, setFeedbackOpen] = useState(false);
  const { navOpen, setNavOpen, revealTourId, onBeforeTourStep, onTourEnd, navModal } = useNavShell();

  const { data: myArtist } = useGetMyArtistQuery();
  const { data: studio } = useGetMyStudioQuery();
  const moneyStudio = studio ? { countryCode: studio.countryCode, currency: studio.currency } : null;
  const { data: openConductReports } = useGetMyConductReportsAsArtistQuery();
  const openConductReportCount = (openConductReports ?? []).filter((r) => r.status === "Open").length;
  const navSections: NavSection[] = withNavBadges(
    buildArtistSections({
      scheduleHref:  "/schedule",
      designsHref:   "/designs",
      reportsHref:   "/conduct-reports",
      portfolioHref: myArtist ? `/artists/${myArtist.id}` : null,
      notifications: true,
      tourIds:       true,
    }),
    { "Conduct Reports": openConductReportCount },
  );

  function handleLogout() {
    dispatch(logout());
    navigate("/login", { replace: true });
  }

  const { ref: headerRef, folded: headerFolded } = useHeaderFold<HTMLElement>(HEADER_FOLD_BELOW_REM.artist);
  return (
    <div data-theme="after-hours" className="min-h-screen flex flex-col bg-background">
      <SkipToContent />
      <SuspensionBanner role="artist" />
      <ReadOnlyBanner />
      <PlanLimitBanner />
      <header ref={headerRef} data-pinned-shell className="@container flex items-center gap-2 px-4 sm:px-6 h-14 border-b bg-background sticky top-0 z-20">
        <SidebarToggle />
        {/* Folded, the wordmark gives its room to the controls (S05: 200% text on a phone): the brand
            shrinks to the micro mark. */}
        <RoleHeaderBrand folded={headerFolded} fit="artist" />
        <StudioContextLabel variant="header" />

        <NavDrawer sections={navSections} title="TattooOS" context={<StudioContextLabel variant="drawer" />} open={navOpen} onOpenChange={setNavOpen} revealTourId={revealTourId} modal={navModal} />

        <HeaderActions
          folded={headerFolded}
          onFeedback={() => setFeedbackOpen(true)}
          showMessages
          help={{ onBeforeTourStep, onTourEnd }}
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
