import { useEffect, useRef, useState } from "react";
import { Link, useNavigate }           from "react-router-dom";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { getRoleRedirectPath }           from "@/app/router";
import { logout }                        from "@/features/auth/authSlice";
import type { Role }                     from "@/shared/types/roles";

// ── Brand mark ─────────────────────────────────────────────────────────────────

export function BrandMark() {
  return (
    <Link
      to="/discover"
      aria-label="TattooOS — Discover studios"
      className="flex items-center gap-2 focus-visible:outline-none
                 focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1
                 rounded-sm"
    >
      <svg
        aria-hidden="true"
        viewBox="0 0 24 24"
        className="h-5 w-5"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.75"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <line x1="12" y1="2" x2="12" y2="18" />
        <path d="M10 16 L12 22 L14 16" />
        <circle cx="12" cy="5" r="2" fill="currentColor" stroke="none" />
        <line x1="8" y1="9" x2="16" y2="9" />
      </svg>
      <span className="font-semibold tracking-tight text-sm">TattooOS</span>
    </Link>
  );
}

// ── AuthenticatedNav ────────────────────────────────────────────────────────────
// Single source of truth — shared by DiscoverPage and PublicPageHeader.

interface AuthenticatedNavProps {
  user: { id: string; email: string; name?: string } | null;
  role: Role | null;
}

export function AuthenticatedNav({ user, role }: AuthenticatedNavProps) {
  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  const [open, setOpen]   = useState(false);
  const menuRef           = useRef<HTMLDivElement>(null);

  // Outside-click → close. Approved useEffect: DOM event, not data fetching.
  useEffect(() => {
    function handleOutside(e: MouseEvent) {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    }
    if (open) document.addEventListener("mousedown", handleOutside);
    return () => document.removeEventListener("mousedown", handleOutside);
  }, [open]);

  // Escape key → close. Approved useEffect: keyboard event.
  useEffect(() => {
    function handleEsc(e: KeyboardEvent) {
      if (e.key === "Escape") setOpen(false);
    }
    if (open) document.addEventListener("keydown", handleEsc);
    return () => document.removeEventListener("keydown", handleEsc);
  }, [open]);

  const initials = (user?.name ?? user?.email ?? "?")
    .split(/\s+/)
    .filter(Boolean)
    .map((w) => w[0]?.toUpperCase() ?? "")
    .slice(0, 2)
    .join("") || "?";

  const dashboardPath = role ? getRoleRedirectPath(role) : "/";

  function handleSignOut() {
    dispatch(logout());
    setOpen(false);
    navigate("/login");
  }

  return (
    <div className="relative" ref={menuRef}>
      <button
        type="button"
        aria-label="Account menu"
        aria-expanded={open}
        aria-haspopup="menu"
        onClick={() => setOpen((v) => !v)}
        className="h-8 w-8 rounded-full bg-violet-600/20 border border-violet-500/40
                   text-violet-800 dark:text-violet-300 text-xs font-semibold flex items-center justify-center
                   hover:bg-violet-600/30 transition-colors
                   focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      >
        {initials}
      </button>

      {open && (
        <div
          role="menu"
          aria-label="Account options"
          className="absolute right-0 top-full mt-1.5 w-52 rounded-md border
                     bg-popover shadow-lg z-[200] overflow-hidden py-1"
        >
          {user?.email && (
            <div className="px-3 py-2 text-xs text-muted-foreground truncate
                            border-b border-border/40 mb-1">
              {user.email}
            </div>
          )}
          <Link
            role="menuitem"
            to={dashboardPath}
            onClick={() => setOpen(false)}
            className="flex w-full items-center px-3 py-2 text-sm
                       hover:bg-muted/40 transition-colors"
          >
            Dashboard
          </Link>
          <Link
            role="menuitem"
            to="/book"
            onClick={() => setOpen(false)}
            className="flex w-full items-center px-3 py-2 text-sm
                       hover:bg-muted/40 transition-colors"
          >
            Book appointment
          </Link>
          <Link
            role="menuitem"
            to="/saved"
            onClick={() => setOpen(false)}
            className="flex w-full items-center px-3 py-2 text-sm
                       hover:bg-muted/40 transition-colors"
          >
            Saved
          </Link>
          <div className="border-t border-border/40 mt-1 pt-1">
            <button
              role="menuitem"
              type="button"
              onClick={handleSignOut}
              className="flex w-full items-center px-3 py-2 text-sm
                         text-destructive-text hover:bg-muted/40 transition-colors"
            >
              Sign out
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

// ── PublicPageHeader ────────────────────────────────────────────────────────────
// Self-contained sticky header for public portfolio pages (StudioPortfolioPage,
// ArtistPortfolioPage). Reads auth state from Redux internally — no props needed.

export function PublicPageHeader() {
  const token = useAppSelector((s) => s.auth.token);
  const user  = useAppSelector((s) => s.auth.user);
  const role  = useAppSelector((s) => s.auth.role);

  return (
    <header
      className="sticky top-0 z-[100] border-b bg-background/95 backdrop-blur-sm"
      aria-label="Site header"
    >
      <div className="flex items-center justify-between px-4 py-2.5">
        <BrandMark />

        <nav className="flex items-center gap-1" aria-label="Site navigation">
          <Link
            to="/discover"
            className="text-xs text-muted-foreground hover:text-foreground
                       transition-colors px-3 py-2 rounded-md hover:bg-muted/40"
          >
            Discover
          </Link>

          {token ? (
            <AuthenticatedNav user={user} role={role} />
          ) : (
            <>
              <Link
                to="/login"
                className="text-xs text-muted-foreground hover:text-foreground
                           transition-colors px-3 py-2 rounded-md hover:bg-muted/40"
              >
                Sign in
              </Link>
              <Link
                to="/client-register"
                className="text-xs text-muted-foreground hover:text-foreground
                           transition-colors px-3 py-2 rounded-md hover:bg-muted/40"
              >
                Sign up
              </Link>
              <Link
                to="/register"
                className="text-xs font-medium px-3 py-2 rounded-md
                           border-2 border-violet-500 text-violet-700 dark:text-violet-400
                           bg-violet-500/5
                           hover:bg-violet-500/15 hover:text-violet-800 dark:hover:text-violet-300
                           transition-colors
                           focus-visible:outline-none focus-visible:ring-2
                           focus-visible:ring-violet-500"
              >
                Register studio
              </Link>
            </>
          )}
        </nav>
      </div>
    </header>
  );
}
