import { useState } from "react";
import { Outlet, useNavigate } from "react-router-dom";
import { PenLine, MessageSquareMore } from "lucide-react";
import { ReadOnlyBanner } from "@/shared/components/ReadOnlyBanner";
import { PlanLimitBanner } from "@/shared/components/PlanLimitBanner";
import { SuspensionBanner } from "@/shared/components/SuspensionBanner";
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
import { NotificationBell } from "@/features/notifications";
import { FeedbackDialog } from "@/features/feedback";
import { HelpMenu } from "@/features/help";
import { useSignalR } from "@/shared/hooks/useSignalR";
import { useGetMyArtistQuery } from "@/features/artists/artistsApi";
import { useGetMyConductReportsAsArtistQuery } from "@/features/conduct-reports";
import { MessagesNavBadge, useChatHub } from "@/features/messaging";

export function ArtistLayout() {
  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  const tenantId = useAppSelector((s) => s.auth.tenantId);
  useSignalR(tenantId);
  useChatHub();
  const [feedbackOpen, setFeedbackOpen] = useState(false);
  const { navOpen, setNavOpen, revealTourId, onBeforeTourStep } = useNavShell();

  const { data: myArtist } = useGetMyArtistQuery();
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

  return (
    <div className="min-h-screen flex flex-col bg-background">
      <SuspensionBanner role="artist" />
      <ReadOnlyBanner />
      <PlanLimitBanner />
      <header className="flex items-center gap-2 px-6 h-14 border-b bg-background sticky top-0 z-20">
        <PenLine className="h-5 w-5" />
        <span className="font-semibold tracking-tight">TattooOS</span>

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
