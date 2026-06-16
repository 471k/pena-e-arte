import { NavLink, Outlet, useNavigate } from "react-router-dom";
import { BarChart3, Bell, Building2, CreditCard, LayoutDashboard, LogOut, PenLine, Receipt, Share2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { UserChip } from "@/shared/components/UserChip";
import { useAppDispatch } from "@/app/hooks";
import { logout } from "@/features/auth/authSlice";
import { cn } from "@/shared/utils/cn";
import { NotificationBell } from "@/features/notifications";

const NAV_ITEMS = [
  { label: "Dashboard",     href: "/platform",               icon: <LayoutDashboard className="h-4 w-4" /> },
  { label: "Studios",       href: "/platform/studios",       icon: <Building2       className="h-4 w-4" /> },
  { label: "Plans",         href: "/platform/plans",         icon: <CreditCard      className="h-4 w-4" /> },
  { label: "Subscriptions", href: "/platform/subscriptions", icon: <Receipt         className="h-4 w-4" /> },
  { label: "Referrals",     href: "/platform/referrals",     icon: <Share2          className="h-4 w-4" /> },
  { label: "Reports",       href: "/platform/reports",       icon: <BarChart3       className="h-4 w-4" /> },
  { label: "Notifications", href: "/notifications",          icon: <Bell            className="h-4 w-4" /> },
];

export function IssuerLayout() {
  const dispatch = useAppDispatch();
  const navigate = useNavigate();

  function handleLogout() {
    dispatch(logout());
    navigate("/login", { replace: true });
  }

  return (
    <div className="min-h-screen flex flex-col bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-20">
        <PenLine className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Platform Admin</span>

        <nav className="ml-6 flex items-center gap-1">
          {NAV_ITEMS.map(({ label, href, icon }) => (
            <NavLink
              key={href}
              to={href}
              end={href === "/platform"}
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
          <NotificationBell />
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
