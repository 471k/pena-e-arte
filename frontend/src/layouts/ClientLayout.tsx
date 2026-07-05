import { NavLink, Outlet, useNavigate } from "react-router-dom";
import {
  CalendarDays, Palette, FileText, ScrollText, User, PenLine, Building2,
} from "lucide-react";
import { cn } from "@/shared/utils/cn";
import { ReadOnlyBanner } from "@/shared/components/ReadOnlyBanner";
import { SuspensionBanner } from "@/shared/components/SuspensionBanner";
import { UserMenu } from "@/shared/components/UserMenu";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { logout } from "@/features/auth/authSlice";
import { useSignalR } from "@/shared/hooks/useSignalR";
import { NotificationBell } from "@/features/notifications";

const NAV_ITEMS = [
  { label: "Book Appointment", shortLabel: "Book",    href: "/book",         icon: <CalendarDays className="h-4 w-4" /> },
  { label: "My Studios",       shortLabel: "Studios", href: "/my-studios",   icon: <Building2    className="h-4 w-4" /> },
  { label: "My Designs",       shortLabel: undefined, href: "/designs",        icon: <Palette      className="h-4 w-4" /> },
  { label: "Intake Forms",     shortLabel: undefined, href: "/forms/intake",   icon: <FileText     className="h-4 w-4" /> },
  { label: "Consent Forms",    shortLabel: undefined, href: "/forms/consent",  icon: <ScrollText   className="h-4 w-4" /> },
  { label: "My Profile",       shortLabel: undefined, href: "/clients/me",     icon: <User         className="h-4 w-4" /> },
];

export function ClientLayout() {
  const dispatch  = useAppDispatch();
  const navigate  = useNavigate();
  const tenantId  = useAppSelector((s) => s.auth.tenantId);
  useSignalR(tenantId);

  function handleLogout() {
    dispatch(logout());
    navigate("/login", { replace: true });
  }

  return (
    <div className="min-h-screen flex flex-col bg-background">
      <SuspensionBanner role="client" />
      <ReadOnlyBanner />
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-20">
        <PenLine className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Pena e Artë</span>

        <nav className="ml-6 flex items-center gap-1 overflow-x-auto scrollbar-none shrink min-w-0">
          {NAV_ITEMS.map(({ label, shortLabel, href, icon }) => (
            <NavLink
              key={href}
              to={href}
              className={({ isActive }) =>
                cn(
                  "flex items-center gap-1.5 px-3 py-2.5 sm:py-1.5 rounded-md text-sm transition-colors shrink-0",
                  isActive
                    ? "bg-violet-600 text-white"
                    : "text-muted-foreground hover:text-foreground hover:bg-muted"
                )
              }
              aria-label={label}
            >
              {icon}
              <span className="hidden sm:inline">{label}</span>
              <span className="sm:hidden">{shortLabel ?? label}</span>
            </NavLink>
          ))}
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
