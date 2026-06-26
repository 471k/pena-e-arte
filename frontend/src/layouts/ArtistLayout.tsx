import { NavLink, Outlet, useNavigate } from "react-router-dom";
import {
  CalendarDays, Users, Palette, FileText, ScrollText,
  DollarSign, Bell, PenLine, ImagePlus,
} from "lucide-react";
import { cn } from "@/shared/utils/cn";
import { ReadOnlyBanner } from "@/shared/components/ReadOnlyBanner";
import { SuspensionBanner } from "@/shared/components/SuspensionBanner";
import { UserMenu } from "@/shared/components/UserMenu";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { logout } from "@/features/auth/authSlice";
import { NotificationBell } from "@/features/notifications";
import { useSignalR } from "@/shared/hooks/useSignalR";
import { useGetMyArtistQuery } from "@/features/artists/artistsApi";

const STATIC_NAV = [
  { label: "Schedule",      href: "/schedule",        icon: <CalendarDays className="h-4 w-4" /> },
  { label: "Clients",       href: "/clients",         icon: <Users        className="h-4 w-4" /> },
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

  const { data: myArtist } = useGetMyArtistQuery();

  function handleLogout() {
    dispatch(logout());
    navigate("/login", { replace: true });
  }

  return (
    <div className="min-h-screen flex flex-col bg-background">
      <SuspensionBanner role="artist" />
      <ReadOnlyBanner />
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-20">
        <PenLine className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Pena e Artë</span>

        <nav className="ml-6 flex items-center gap-1">
          {STATIC_NAV.map(({ label, href, icon }) => (
            <NavLink
              key={href}
              to={href}
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
            </NavLink>
          ))}
          {myArtist && (
            <NavLink
              to={`/artists/${myArtist.id}`}
              className={({ isActive }) =>
                cn(
                  "flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm transition-colors",
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
