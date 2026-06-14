import { NavLink, Outlet, useNavigate } from "react-router-dom";
import {
  LayoutDashboard, Users, UserSquare, Palette, CreditCard,
  Receipt, Settings, Bell, PenLine, LogOut,
} from "lucide-react";
import { cn } from "@/shared/utils/cn";
import { ReadOnlyBanner } from "@/shared/components/ReadOnlyBanner";
import { SuspensionBanner } from "@/shared/components/SuspensionBanner";
import { UserChip } from "@/shared/components/UserChip";
import { Button } from "@/shared/components/ui/button";
import { useAppDispatch } from "@/app/hooks";
import { logout } from "@/features/auth/authSlice";
import { useGetSubscriptionQuery } from "@/features/billing/billingApi";
import { useGetMyStudioQuery } from "@/features/studios/studiosApi";

const NAV_ITEMS = [
  { label: "Dashboard",       href: "/dashboard",    icon: <LayoutDashboard className="h-4 w-4" /> },
  { label: "Artists",         href: "/artists",      icon: <Users           className="h-4 w-4" /> },
  { label: "Clients",         href: "/clients",      icon: <UserSquare      className="h-4 w-4" /> },
  { label: "Designs",         href: "/designs",      icon: <Palette         className="h-4 w-4" /> },
  { label: "Payments",        href: "/payments",     icon: <CreditCard      className="h-4 w-4" /> },
  { label: "Billing",         href: "/billing",      icon: <Receipt         className="h-4 w-4" /> },
  { label: "Studio Settings", href: "/studios/me",   icon: <Settings        className="h-4 w-4" /> },
  { label: "Notifications",   href: "/notifications",icon: <Bell            className="h-4 w-4" /> },
];

export function OwnerLayout() {
  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  // Primes RTK Query caches so subscription + suspension state is known before child forms render.
  useGetSubscriptionQuery();
  const { data: studio } = useGetMyStudioQuery();

  function handleLogout() {
    dispatch(logout());
    navigate("/login", { replace: true });
  }

  return (
    <div className="min-h-screen flex flex-col bg-background">
      <SuspensionBanner studio={studio} />
      <ReadOnlyBanner />
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-20">
        <PenLine className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Pena e Artë</span>

        <nav className="ml-6 flex items-center gap-1">
          {NAV_ITEMS.map(({ label, href, icon }) => (
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
        </nav>

        <div className="ml-auto flex items-center gap-3">
          <UserChip />
          <div className="w-px h-5 bg-border" />
          <Button
            variant="ghost"
            size="sm"
            className="text-muted-foreground hover:text-foreground"
            onClick={handleLogout}
          >
            <LogOut className="h-4 w-4 mr-1.5" />
            Log out
          </Button>
        </div>
      </header>

      <div className="flex-1">
        <Outlet />
      </div>
    </div>
  );
}
