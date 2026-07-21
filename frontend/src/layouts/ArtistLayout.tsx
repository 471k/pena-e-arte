import { useState } from "react";
import { NavLink, Outlet, useNavigate } from "react-router-dom";
import {
  CalendarDays, Users, Palette, FileText, ScrollText,
  DollarSign, Bell, PenLine, ImagePlus, MessageSquareMore,
} from "lucide-react";
import { cn } from "@/shared/utils/cn";
import { ReadOnlyBanner } from "@/shared/components/ReadOnlyBanner";
import { PlanLimitBanner } from "@/shared/components/PlanLimitBanner";
import { SuspensionBanner } from "@/shared/components/SuspensionBanner";
import { UserMenu } from "@/shared/components/UserMenu";
import { Button } from "@/shared/components/ui/button";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { logout } from "@/features/auth/authSlice";
import { NotificationBell } from "@/features/notifications";
import { FeedbackDialog } from "@/features/feedback";
import { HelpMenu } from "@/features/help";
import { useSignalR } from "@/shared/hooks/useSignalR";
import { useGetMyArtistQuery } from "@/features/artists/artistsApi";

const STATIC_NAV = [
  { label: "Schedule",      href: "/schedule",        icon: <CalendarDays className="h-4 w-4" />, tourId: "artist-schedule-nav" },
  { label: "Clients",       href: "/clients",         icon: <Users        className="h-4 w-4" />, tourId: "artist-clients-nav" },
  { label: "Designs",       href: "/designs",         icon: <Palette      className="h-4 w-4" /> },
  { label: "Intake Forms",  href: "/forms/intake",    icon: <FileText     className="h-4 w-4" /> },
  { label: "Consent Forms", href: "/forms/consent",   icon: <ScrollText   className="h-4 w-4" /> },
  { label: "Deposit Rules", href: "/deposit-rules",   icon: <DollarSign   className="h-4 w-4" /> },
  { label: "Notifications", href: "/notifications",   icon: <Bell         className="h-4 w-4" /> },
];

export function ArtistLayout() {
  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  const tenantId = useAppSelector((s) => s.auth.tenantId);
  useSignalR(tenantId);
  const [feedbackOpen, setFeedbackOpen] = useState(false);

  const { data: myArtist } = useGetMyArtistQuery();

  function handleLogout() {
    dispatch(logout());
    navigate("/login", { replace: true });
  }

  return (
    <div className="min-h-screen flex flex-col bg-background">
      <SuspensionBanner role="artist" />
      <ReadOnlyBanner />
      <PlanLimitBanner />
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-20">
        <PenLine className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Pena e Artë</span>

        <nav className="ml-6 flex items-center gap-1 overflow-x-auto scrollbar-none shrink min-w-0">
          {STATIC_NAV.map(({ label, href, icon, tourId }) => (
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
          {myArtist && (
            <NavLink
              to={`/artists/${myArtist.id}`}
              className={({ isActive }) =>
                cn(
                  "flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm transition-colors shrink-0",
                  isActive
                    ? "bg-primary text-primary-foreground"
                    : "text-muted-foreground hover:text-foreground hover:bg-muted"
                )
              }
            >
              <ImagePlus className="h-4 w-4" />
              My Portfolio
            </NavLink>
          )}
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
