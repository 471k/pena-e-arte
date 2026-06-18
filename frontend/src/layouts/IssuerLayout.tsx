import { useState, useEffect, useRef } from "react";
import { NavLink, Outlet, useNavigate } from "react-router-dom";
import { BarChart3, Building2, ChevronDown, CreditCard, LayoutDashboard, LogOut, PenLine, Receipt, Share2 } from "lucide-react";
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
];

function UserMenu({ onLogout }: { onLogout: () => void }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    function handleClick(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        setOpen(false);
      }
    }
    document.addEventListener("mousedown", handleClick);
    return () => document.removeEventListener("mousedown", handleClick);
  }, [open]);

  return (
    <div ref={ref} className="relative">
      <button
        onClick={() => setOpen((v) => !v)}
        aria-label="User menu"
        aria-expanded={open}
        aria-haspopup="true"
        className="flex items-center gap-1 rounded-md px-2 py-1.5 text-sm text-muted-foreground hover:text-foreground hover:bg-muted transition-colors"
      >
        <UserChip />
        <ChevronDown className="h-3 w-3 opacity-50" />
      </button>

      {open && (
        <div className="absolute right-0 top-full mt-1 w-40 rounded-md border bg-background shadow-md z-50 py-1">
          <button
            onClick={() => { setOpen(false); onLogout(); }}
            className="flex items-center gap-2 w-full px-3 py-2 text-sm text-muted-foreground hover:text-foreground hover:bg-muted transition-colors"
          >
            <LogOut className="h-4 w-4" />
            Log out
          </button>
        </div>
      )}
    </div>
  );
}

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
          <UserMenu onLogout={handleLogout} />
        </div>
      </header>

      <div className="flex-1">
        <Outlet />
      </div>
    </div>
  );
}
