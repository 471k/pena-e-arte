# Overnight Prompt — Public Nav Header for Portfolio Pages
**Date:** 2026-07-04
**Scope:** `StudioPortfolioPage` and `ArtistPortfolioPage` have no nav header — a guest
landing directly on `/s/:slug` or `/artist/:slug` from Google/Instagram/a shared link has
zero sign-in/sign-up entry point. This prompt fixes both pages and eliminates the duplicate
`AuthenticatedNav` code that would otherwise live in three places.

---

## Context: What Already Exists

`DiscoverPage.tsx` already has a complete, well-designed sticky header with:
- Brand mark (stylised needle SVG + "Pena e Artë") → links to `/discover`
- For **logged-out** visitors: "Sign in" link · "Sign up" link · "Register studio" outlined button
- For **logged-in** users: `AuthenticatedNav` — initials avatar button → dropdown with
  Dashboard / Book appointment / Saved / Sign out
- Sticky, `bg-background/95 backdrop-blur-sm`, `z-[100]`

`AuthenticatedNav` is currently **defined inline** inside `DiscoverPage.tsx`. It must be
extracted into a shared file so it can be used by all three public pages without duplication.

`StudioPortfolioPage.tsx` and `ArtistPortfolioPage.tsx` both start directly with content
(hero image / content div) and render no header at all.

---

## Required Reading

```
CLAUDE.md
docs/claude/frontend.md
docs/claude/conventions.md
frontend/src/features/public/components/DiscoverPage.tsx          ← AuthenticatedNav source of truth
frontend/src/features/public/components/StudioPortfolioPage.tsx   ← no header today
frontend/src/features/public/components/ArtistPortfolioPage.tsx   ← no header today
frontend/src/features/public/index.ts                             ← exports to update
frontend/src/features/auth/authSlice.ts                           ← getRoleRedirectPath lives in router, not slice
frontend/src/app/router.tsx                                        ← getRoleRedirectPath location
frontend/src/features/public/__tests__/StudioPortfolioPage.test.tsx
frontend/src/features/public/__tests__/ArtistPortfolioPage.test.tsx
```

---

## Architecture Decision

**One shared `PublicPageHeader` component**, not three copies of the nav.

- `AuthenticatedNav` moves from `DiscoverPage.tsx` → `PublicPageHeader.tsx`
- `PublicPageHeader` reads auth state from Redux internally (no props needed for auth)
- `DiscoverPage` imports `AuthenticatedNav` from `PublicPageHeader.tsx` for its own header,
  or simply adds `PublicPageHeader` and adapts it — see Step D3.
- The value-prop strip (`!token && ...`) and search row stay inline in `DiscoverPage` —
  they are page-specific, not shared.

---

## Step 1 — Create `PublicPageHeader.tsx`

**File:** `frontend/src/features/public/components/PublicPageHeader.tsx` (NEW)

This file exports two things:
1. `AuthenticatedNav` — moved verbatim from `DiscoverPage.tsx`, export it so `DiscoverPage`
   can import it back.
2. `PublicPageHeader` — a self-contained sticky header for use on portfolio pages.

```tsx
import { useEffect, useRef, useState } from "react";
import { Link, useNavigate }           from "react-router-dom";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { getRoleRedirectPath }           from "@/app/router";
import { logout }                        from "@/features/auth/authSlice";
import type { Role }                     from "@/shared/types/roles";

// ── Brand mark ─────────────────────────────────────────────────────────────────
// Identical needle SVG used in DiscoverPage — single source of truth here.

function BrandMark() {
  return (
    <Link
      to="/discover"
      aria-label="Pena e Artë — Discover studios"
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
      <span className="font-semibold tracking-tight text-sm">Pena e Artë</span>
    </Link>
  );
}

// ── AuthenticatedNav ────────────────────────────────────────────────────────────
// Moved from DiscoverPage.tsx — keep this as the single source of truth.

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
                   text-violet-300 text-xs font-semibold flex items-center justify-center
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
                         text-destructive hover:bg-muted/40 transition-colors"
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
                           border-2 border-violet-500 text-violet-400
                           bg-violet-500/5
                           hover:bg-violet-500/15 hover:text-violet-300
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
```

---

## Step 2 — Update `DiscoverPage.tsx`

`DiscoverPage` has its own full header (which also includes search, tabs, and location). It
does NOT use `PublicPageHeader` — its header is too different. However, `AuthenticatedNav`
is duplicated right now. Fix:

1. **Delete** the `AuthenticatedNav` function body from `DiscoverPage.tsx` (the entire
   component definition, ~70 lines).
2. **Add** a named import at the top of `DiscoverPage.tsx`:
   ```ts
   import { AuthenticatedNav } from "./PublicPageHeader";
   ```
3. **Verify** every reference to `AuthenticatedNav` inside `DiscoverPage` still works —
   the only usage is `{token ? <AuthenticatedNav user={user} role={role} /> : ...}` in
   the sticky header.

Also remove these imports from `DiscoverPage.tsx` if they are no longer used after the
`AuthenticatedNav` extraction (check carefully — they may still be needed elsewhere in
the file):
```ts
// These are imported for AuthenticatedNav's internals — check before removing:
import { useEffect, useRef }  // still needed for geolocation useEffect in DiscoverPage
import { useNavigate }         // still needed for forward geocode
import { useAppDispatch }      // still needed if logout is called
import { getRoleRedirectPath } // no longer needed in DiscoverPage — REMOVE if not used elsewhere
import { logout }              // still needed for the header's sign-out — but now in AuthenticatedNav; REMOVE from DiscoverPage if AuthenticatedNav is the only user
```

**Be careful:** read through `DiscoverPage.tsx` before removing any import — confirm it is
genuinely unused after extraction. TypeScript strict mode will catch missed removals
(`noUnusedLocals`).

The `BrandMark` SVG in `DiscoverPage`'s header can remain inline (it's a small SVG),
OR you can import `BrandMark` if it's exported from `PublicPageHeader.tsx`.
For simplicity, export `BrandMark` from `PublicPageHeader.tsx` and import it in `DiscoverPage`
to avoid two copies of the same SVG path. Add `export` to the `BrandMark` function in the
file above.

---

## Step 3 — Update `StudioPortfolioPage.tsx`

**Four changes:**

### 3a — Import `PublicPageHeader`

```ts
import { PublicPageHeader } from "./PublicPageHeader";
```

### 3b — Update `StudioPageSkeleton`

The skeleton must also show a header placeholder so layout doesn't shift when data loads.
Replace the current skeleton's opening:

```tsx
function StudioPageSkeleton() {
  return (
    <div className="min-h-screen bg-background" aria-label="Loading studio page" aria-busy="true">
      {/* Header placeholder — matches real header height (~48px) */}
      <div className="h-[49px] border-b bg-background/95" aria-hidden="true" />

      <Skeleton className="h-72 w-full rounded-none" />
      {/* ... rest of skeleton unchanged ... */}
    </div>
  );
}
```

### 3c — Add `<PublicPageHeader />` to the page

In the `return` of `StudioPortfolioPage`, add the header **before** the hero `<div>`:

```tsx
return (
  <div className="min-h-screen bg-background flex flex-col">
    <StudioMeta ... />

    <GalleryLightbox ... />

    {/* ← ADD THIS */}
    <PublicPageHeader />

    {/* Hero — unchanged */}
    <div className="relative h-72 bg-zinc-900 overflow-hidden">
      ...
    </div>

    {/* Content — unchanged EXCEPT sidebar top offset (see 3d) */}
    ...
  </div>
);
```

Also add `<PublicPageHeader />` to the **error state**:

```tsx
if (isError || !studio) {
  return (
    <div className="min-h-screen bg-background flex flex-col">
      <PublicPageHeader />
      <div className="flex flex-col items-center justify-center flex-1 gap-4">
        <p className="text-muted-foreground">Studio not found.</p>
        <Button variant="outline" asChild>
          <Link to="/discover">Browse studios</Link>
        </Button>
      </div>
    </div>
  );
}
```

### 3d — Adjust sticky sidebar `top` offset

The sidebar is currently `lg:sticky lg:top-6`. With the ~48px sticky header, update to:

```tsx
{/* Right: sticky sidebar */}
<aside className="lg:sticky lg:top-[72px] space-y-4">
```

`72px` = 48px header + 24px breathing room, matching the original `top-6` intent but
accounting for the header height.

---

## Step 4 — Update `ArtistPortfolioPage.tsx`

Identical pattern to Step 3.

### 4a — Import `PublicPageHeader`

```ts
import { PublicPageHeader } from "./PublicPageHeader";
```

### 4b — Update `ArtistPageSkeleton`

```tsx
function ArtistPageSkeleton() {
  return (
    <div
      className="min-h-screen bg-background"
      aria-label="Loading artist profile"
      aria-busy="true"
    >
      {/* Header placeholder */}
      <div className="h-[49px] border-b bg-background/95" aria-hidden="true" />

      <div className="max-w-6xl mx-auto px-4 py-8">
        {/* ... rest of skeleton unchanged ... */}
      </div>
    </div>
  );
}
```

### 4c — Add `<PublicPageHeader />` to the page

In the `return` of `ArtistPortfolioPage`, add the header as the first child:

```tsx
return (
  <div className="min-h-screen bg-background flex flex-col">
    <ArtistMeta ... />

    <Lightbox ... />

    {/* ← ADD THIS */}
    <PublicPageHeader />

    {/* Existing content div — unchanged EXCEPT aside top offset */}
    <div className="flex-1 max-w-6xl mx-auto w-full px-4 py-8 space-y-6">
      ...
    </div>

    <footer ...>...</footer>
  </div>
);
```

Also add to the **error state**:

```tsx
if (isError || !artist) {
  return (
    <div className="min-h-screen bg-background flex flex-col">
      <PublicPageHeader />
      <div className="flex flex-col items-center justify-center flex-1 gap-4">
        <p className="text-muted-foreground">Artist not found.</p>
        <Button variant="outline" asChild>
          <Link to="/discover">Browse artists</Link>
        </Button>
      </div>
    </div>
  );
}
```

### 4d — Adjust sticky aside `top` offset

```tsx
<aside className="lg:sticky lg:top-[72px] space-y-5">
```

---

## Step 5 — Export from the `public` feature index

**File:** `frontend/src/features/public/index.ts`

Add the export:
```ts
export { PublicPageHeader, AuthenticatedNav } from "./components/PublicPageHeader";
```

---

## Step 6 — Tests

### 6a — New test file for `PublicPageHeader`

**File:** `frontend/src/features/public/__tests__/PublicPageHeader.test.tsx` (NEW)

Follow the same `makeStore` + `MemoryRouter` pattern used in `StudioPortfolioPage.test.tsx`.

```ts
// Seed data / helpers
function makeStore(token: string | null, role: Role | null = null) {
  return configureStore({
    reducer: { auth: authReducer },
    preloadedState: {
      auth: {
        user: token ? { id: "u-001", email: "test@example.com" } : null,
        token,
        tenantId: null,
        role,
        refreshToken: null,
        pendingReferralCode: null,
      } as AuthState,
    },
  });
}

function renderHeader(token: string | null = null, role: Role | null = null) {
  render(
    <Provider store={makeStore(token, role)}>
      <MemoryRouter>
        <PublicPageHeader />
      </MemoryRouter>
    </Provider>,
  );
}
```

Write these tests:

```
Logged-out state
  1. Renders the brand mark with link to /discover
  2. Renders "Sign in" link pointing to /login
  3. Renders "Sign up" link pointing to /client-register
  4. Renders "Register studio" link pointing to /register
  5. Does NOT render the initials avatar button

Logged-in state (token present)
  6. Renders the initials avatar button (aria-label "Account menu")
  7. Does NOT render "Sign in", "Sign up", or "Register studio" links
  8. Clicking the avatar opens the dropdown menu (aria-expanded becomes true)
  9. Dropdown contains a "Dashboard" link
  10. Dropdown contains a "Book appointment" link
  11. Dropdown contains a "Sign out" button
  12. Clicking outside the dropdown closes it
  13. Pressing Escape closes the dropdown

Accessibility
  14. Header element has aria-label="Site header"
  15. Nav has aria-label="Site navigation"
  16. Dropdown has role="menu" and aria-label="Account options"
```

### 6b — Update `StudioPortfolioPage.test.tsx`

Add to the existing `describe` block:

```ts
describe("PublicPageHeader on StudioPortfolioPage", () => {
  it("renders 'Sign in' and 'Sign up' links when unauthenticated", () => {
    renderPage(null);
    expect(screen.getByRole("link", { name: /sign in/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /sign up/i })).toBeInTheDocument();
  });

  it("renders 'Register studio' link when unauthenticated", () => {
    renderPage(null);
    expect(screen.getByRole("link", { name: /register studio/i })).toBeInTheDocument();
  });

  it("renders initials avatar when authenticated", () => {
    renderPage("fake-token");
    expect(screen.getByRole("button", { name: /account menu/i })).toBeInTheDocument();
  });

  it("renders brand mark link to /discover", () => {
    renderPage();
    expect(screen.getByRole("link", { name: /pena e artë.*discover/i })).toBeInTheDocument();
  });

  it("header is present in the loading skeleton", () => {
    mockUseGetPublicStudioQuery.mockReturnValue({ data: undefined, isLoading: true, isError: false });
    renderPage();
    // The skeleton renders a header placeholder div (aria-hidden), not the real header.
    // Confirm the page is in loading state (no sign-in links rendered yet).
    expect(screen.getByLabelText(/loading studio page/i)).toBeInTheDocument();
  });

  it("header is present in the not-found error state", () => {
    mockUseGetPublicStudioQuery.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    renderPage(null);
    expect(screen.getByText("Studio not found.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /sign in/i })).toBeInTheDocument();
  });
});
```

### 6c — Update `ArtistPortfolioPage.test.tsx`

Add the same group of 6 tests (mirror 6b), adapted for `ArtistPortfolioPage`:

```ts
describe("PublicPageHeader on ArtistPortfolioPage", () => {
  it("renders 'Sign in' and 'Sign up' links when unauthenticated", () => { ... });
  it("renders 'Register studio' link when unauthenticated",        () => { ... });
  it("renders initials avatar when authenticated",                  () => { ... });
  it("renders brand mark link to /discover",                        () => { ... });
  it("header is present in the loading skeleton",                   () => { ... });
  it("header is present in the not-found error state",              () => { ... });
});
```

---

## Verification

```bash
cd "Pena e Arte/frontend"

# 1. TypeScript — no errors (AuthenticatedNav removed from DiscoverPage must not leave
#    dangling imports; BrandMark duplication removed)
pnpm tsc --noEmit

# 2. New PublicPageHeader unit tests
pnpm test -- --reporter=verbose __tests__/PublicPageHeader

# 3. Updated portfolio page tests
pnpm test -- --reporter=verbose __tests__/StudioPortfolioPage
pnpm test -- --reporter=verbose __tests__/ArtistPortfolioPage

# 4. DiscoverPage tests must still pass (AuthenticatedNav refactor must be transparent)
pnpm test -- --reporter=verbose __tests__/DiscoverPage

# 5. Full frontend test suite
pnpm test
```

All five commands must exit 0 with no TypeScript errors.

---

## Exit Condition

All tests green. Then append to `docs/claude/architecture.md`:

```markdown
## Public Portfolio Pages — Nav Header — 2026-07-04

### Problem
`StudioPortfolioPage` (/s/:slug) and `ArtistPortfolioPage` (/artist/:slug) are public routes
reachable via Google, shared links, and Instagram bios. Neither page had a nav header, so
unauthenticated visitors landing directly on these pages had no sign-in or sign-up entry point.

### Solution
- Extracted `AuthenticatedNav` from `DiscoverPage.tsx` into a new shared file:
  `frontend/src/features/public/components/PublicPageHeader.tsx`
- Created `PublicPageHeader` component — sticky header with brand mark + sign-in/sign-up
  links (logged-out) or account dropdown (logged-in). No props — reads Redux internally.
- Added `<PublicPageHeader />` to the loaded, error, and (placeholder in) loading states of
  both portfolio pages.
- Adjusted sticky sidebar `top` offset from `top-6` to `top-[72px]` to account for header height.

### Files changed
- `frontend/src/features/public/components/PublicPageHeader.tsx` (NEW)
- `frontend/src/features/public/components/DiscoverPage.tsx` — removed inline `AuthenticatedNav`,
  imported it from `PublicPageHeader.tsx`; removed `BrandMark` duplication if applicable
- `frontend/src/features/public/components/StudioPortfolioPage.tsx` — added header + error state header + skeleton placeholder + sidebar top adjustment
- `frontend/src/features/public/components/ArtistPortfolioPage.tsx` — same as above
- `frontend/src/features/public/index.ts` — added exports
- `frontend/src/features/public/__tests__/PublicPageHeader.test.tsx` (NEW — 16 tests)
- `frontend/src/features/public/__tests__/StudioPortfolioPage.test.tsx` — 6 new header tests
- `frontend/src/features/public/__tests__/ArtistPortfolioPage.test.tsx` — 6 new header tests
```
