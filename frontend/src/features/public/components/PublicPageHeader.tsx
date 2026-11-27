import { useEffect, useRef, useState } from "react";
import { Link, useNavigate }           from "react-router-dom";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { getRoleRedirectPath }           from "@/app/router";
import { logout }                        from "@/features/auth/authSlice";
import type { Role }                     from "@/shared/types/roles";
import { TattooOSLogo } from "@/shared/components/TattooOSLogo";

// ── Brand mark ─────────────────────────────────────────────────────────────────

/**
 * Where the small lockup gives way to the 20px micro mark, per header. Measured for brand master 1.0.0
 * (27 Nov 2026, Chrome, signed out, which is each header's widest nav) as the narrowest window with no
 * sideways scroll: Discover ("Map · Sign in · Sign up · Register studio") needs 386px, the portfolio
 * pages' header ("Discover · Sign in · …") 410px; the guest auth header ("Discover" only) fits at 320px
 * and keeps the package's own sub-360px rule. Static class strings so Tailwind generates them.
 */
const BRAND_SWAP = {
  360: { full: "inline-flex max-[359px]:hidden", micro: "hidden max-[359px]:inline-flex" },
  390: { full: "inline-flex max-[389px]:hidden", micro: "hidden max-[389px]:inline-flex" },
  416: { full: "inline-flex max-[415px]:hidden", micro: "hidden max-[415px]:inline-flex" },
} as const;

interface BrandMarkProps {
  /** Window width (px) from which the small lockup shows; below it, the micro mark. */
  fullFrom?: keyof typeof BRAND_SWAP;
}

export function BrandMark({ fullFrom = 360 }: BrandMarkProps) {
  const swap = BRAND_SWAP[fullFrom];
  return (
    <Link
      to="/discover"
      aria-label="TattooOS — Discover studios"
      // min-h-11 makes the link a 44px target; -my-1.5 lets those 44px reach into the header's own
      // py-2.5, so the row (sized by the 32px nav links) and the header height do not change.
      className="flex min-h-11 -my-1.5 items-center focus-visible:outline-hidden
                 focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1
                 rounded-sm"
    >
      {/* Brand master 1.0.0. The link's aria-label names it, so both images are decorative. The
          small lockup (141px) gives way to the micro mark where the header's nav needs the room
          (1.4.10 reflow: the old wordmark was hidden below 360px for the same reason, after it ran
          39px past a 320px screen). The visibility classes sit on wrappers because
          TattooOSLogo.css sets the logo's own display. */}
      <span className={swap.full}>
        <TattooOSLogo variant="small" decorative />
      </span>
      <span className={swap.micro}>
        <TattooOSLogo variant="micro" height={20} decorative />
      </span>
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
                   focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-ring"
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
// Not sticky on a short viewport (short-viewport, max-height 30rem; see index.css), like Discover's: at 400%
// zoom (320x256) it and the quick-book bar left a
// 114px band, shorter than a 138px portfolio tile, so a focused tile could never be fully visible (WCAG 2.4.11).
// The quick-book bar stays pinned: it is the booking path.

export function PublicPageHeader() {
  const token = useAppSelector((s) => s.auth.token);
  const user  = useAppSelector((s) => s.auth.user);
  const role  = useAppSelector((s) => s.auth.role);

  return (
    <header
      className="sticky short-viewport:relative top-0 z-[100] border-b bg-background/95 backdrop-blur-sm"
      aria-label="Site header"
    >
      <div className="flex items-center justify-between px-4 py-2.5">
        <BrandMark fullFrom={416} />

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
                           focus-visible:outline-hidden focus-visible:ring-2
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
