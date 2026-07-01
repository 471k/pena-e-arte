# Overnight Prompt — Sign In + Discover Page Overhaul
**Date:** 2026-07-01
**Scope:** LoginPage polish, "Remember me", authenticated nav on Discover, portfolio chip accessibility

---

## Context & Rules

- No new npm/NuGet packages. Radix `@radix-ui/react-dropdown-menu` is **not** installed — use a
  plain `useState` custom dropdown instead.
- No `useEffect` for data fetching. `useEffect` is approved for browser API side-effects
  (outside-click detection, keyboard events, geolocation, resize).
- TypeScript strict mode. No `any`. No default exports on components.
- Run `pnpm test` after every section and fix any failures before moving on.
- Read `docs/claude/frontend.md` and `docs/claude/conventions.md` before starting.

---

## What to Audit Before Starting

Read these files in full before writing a single line of code:

```
frontend/src/features/auth/components/LoginPage.tsx
frontend/src/features/auth/authSlice.ts
frontend/src/features/auth/shared/utils/jwt.ts
frontend/src/features/public/components/DiscoverPage.tsx
frontend/src/features/public/components/PortfolioFeed.tsx
frontend/src/features/public/__tests__/DiscoverPage.test.tsx
frontend/src/features/auth/__tests__/LoginPage.test.tsx
```

---

## Section 1 — authSlice: "Remember me" Storage Split

**File:** `frontend/src/features/auth/authSlice.ts`

### Problem

`setCredentials` always writes to `localStorage`, meaning the token persists across browser
restarts even when the user didn't ask for that. The login form has no "Remember me" checkbox,
so users are permanently remembered by default with no opt-out.

### Changes

**1. Change `setCredentials` payload type** — add `remember?: boolean`:

```ts
setCredentials: (state, { payload }: PayloadAction<AuthPayload & { remember?: boolean }>) => {
  state.user         = payload.user;
  state.token        = payload.token;
  state.refreshToken = payload.refreshToken ?? null;
  state.tenantId     = payload.tenantId;
  state.role         = payload.role;

  // remember defaults to true (existing behaviour).
  // remember === false → sessionStorage only (cleared on tab close).
  const storage = payload.remember !== false ? localStorage : sessionStorage;
  storage.setItem(TOKEN_KEY, payload.token);
  if (payload.refreshToken) {
    storage.setItem(REFRESH_TOKEN_KEY, payload.refreshToken);
  }
},
```

**2. Update `loadInitialState`** — check both storages:

```ts
function loadInitialState(): AuthState {
  try {
    // localStorage first (persistent remembered sessions), then sessionStorage (session-only).
    const token        = localStorage.getItem(TOKEN_KEY) ?? sessionStorage.getItem(TOKEN_KEY);
    const refreshToken = localStorage.getItem(REFRESH_TOKEN_KEY) ?? sessionStorage.getItem(REFRESH_TOKEN_KEY);
    if (!token) return EMPTY;

    const payload = decodeToken(token);
    if (payload.exp && Date.now() / 1000 > payload.exp) {
      localStorage.removeItem(TOKEN_KEY);
      localStorage.removeItem(REFRESH_TOKEN_KEY);
      sessionStorage.removeItem(TOKEN_KEY);
      sessionStorage.removeItem(REFRESH_TOKEN_KEY);
      return EMPTY;
    }

    return {
      user: payload.user, token, refreshToken,
      tenantId: payload.tenantId, role: payload.role,
      pendingReferralCode: null,
    };
  } catch {
    return EMPTY;
  }
}
```

**3. Update `logout`** — clear both storages:

```ts
logout: () => {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(REFRESH_TOKEN_KEY);
  sessionStorage.removeItem(TOKEN_KEY);
  sessionStorage.removeItem(REFRESH_TOKEN_KEY);
  return EMPTY;
},
```

---

## Section 2 — LoginPage: Visual Polish + Remember Me

**File:** `frontend/src/features/auth/components/LoginPage.tsx`

Apply all of the following changes. Each is labelled with the bug/audit item it fixes.

### 2a. Background glow → branded violet

Current glow is a flat zinc/gray. Replace with a violet-tinted radial gradient so the page
feels brand-appropriate:

```tsx
// Replace the existing style prop value:
style={{
  background:
    "radial-gradient(ellipse 80% 50% at 50% 0%, rgba(124,58,237,0.10) 0%, transparent 70%)",
}}
```

### 2b. Card elevation in dark theme

Dark-on-dark shadows are invisible. Add a lighter-than-background shadow overlay:

```tsx
// Update card className:
<Card className="dark:bg-zinc-900/80 dark:border-zinc-700/60 shadow-lg
                 dark:shadow-[0_8px_32px_rgba(255,255,255,0.05)]">
```

### 2c. Tagline contrast bump

`text-foreground/65` sits below the 4.5:1 AA threshold for 14px text. Bump to `/80`:

```tsx
<p className="text-sm text-foreground/80">
  Run your studio. Book clients. Manage your team.
</p>
```

### 2d. autoFocus on email field

Add `autoFocus` to the email `<Input>`. Standard behaviour for login forms:

```tsx
<Input
  id="email"
  type="email"
  autoComplete="email"
  autoFocus
  placeholder="you@example.com"
  ...
/>
```

### 2e. Remember me + Forgot password row

Replace the current `<div className="flex justify-end">` that holds just "Forgot password?" with
a two-column row. Add `remember` state at the top of the component:

```tsx
const [remember, setRemember] = useState(true);
```

Update `onSubmit` to pass `remember` to `setCredentials`:

```tsx
async function onSubmit(values: LoginFormValues) {
  try {
    const { accessToken } = await login(values).unwrap();
    const payload = decodeToken(accessToken);
    dispatch(setCredentials({ ...payload, remember }));
  } catch {
    // error surfaced via RTK Query's `error` state
  }
}
```

Replace the forgot-password div inside the password `space-y-1.5` section:

```tsx
<div className="flex items-center justify-between gap-4 pt-0.5">
  <label className="flex items-center gap-2 cursor-pointer select-none">
    <input
      type="checkbox"
      checked={remember}
      onChange={(e) => setRemember(e.target.checked)}
      className="h-4 w-4 rounded border-input accent-violet-600 cursor-pointer"
      aria-label="Remember me on this device"
    />
    <span className="text-xs text-foreground/70">Remember me</span>
  </label>
  <Link
    to="/forgot-password"
    className="text-xs text-foreground/65 underline underline-offset-4
               hover:text-foreground py-2 inline-block"
  >
    Forgot password?
  </Link>
</div>
```

### 2f. Remove the separator between Sign in button and registration prompt

The `border-t` separator at the bottom of the card adds visual noise without purpose. Remove it:

```tsx
// Before (remove border-t pt-4):
<div className="mt-4 pt-4 border-t border-border/50 text-center text-sm text-foreground/65">

// After:
<div className="mt-4 text-center text-sm text-foreground/65">
```

### 2g. Color the registration links violet

"Register your studio" and "Create a client account" are currently muted gray and look
non-interactive. Color them to match the brand CTA:

```tsx
// Both Link elements in the registration prompt:
className="underline underline-offset-4 text-violet-400 hover:text-violet-300 py-2 inline-block"
```

### 2h. Footer contrast + mobile wrapping

`text-foreground/40` at 12px is below AA contrast. Bump to `/55`. Also fix wrapping on narrow
screens:

```tsx
<footer className="absolute bottom-6 left-0 right-0 text-center text-xs text-foreground/55">
  <div className="flex flex-wrap gap-x-4 gap-y-1.5 justify-center">
    <a href="/privacy" className="hover:text-foreground/70 transition-colors underline-offset-2 hover:underline">
      Privacy Policy
    </a>
    <span aria-hidden="true" className="text-border select-none">·</span>
    <a href="/terms" className="hover:text-foreground/70 transition-colors underline-offset-2 hover:underline">
      Terms of Service
    </a>
    <span aria-hidden="true" className="text-border select-none">·</span>
    <a href="mailto:support@penaearte.com" className="hover:text-foreground/70 transition-colors underline-offset-2 hover:underline">
      Contact support
    </a>
  </div>
</footer>
```

---

## Section 3 — LoginPage Tests

**File:** `frontend/src/features/auth/__tests__/LoginPage.test.tsx`

### 3a. Preserve all existing passing tests

Do not break any existing tests. The only test that references the auth state internals is
"successful login dispatches credentials and navigates to the role home" — ensure it still
passes. The `setCredentials` change is backwards-compatible since `remember` defaults to `true`.

### 3b. New tests to add

Add a new `describe("LoginPage — Remember me", ...)` block after the existing tests:

```tsx
describe("LoginPage — Remember me", () => {
  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  it("renders a 'Remember me' checkbox checked by default", () => {
    renderPage();
    const checkbox = screen.getByRole("checkbox", { name: /remember me/i });
    expect(checkbox).toBeInTheDocument();
    expect(checkbox).toBeChecked();
  });

  it("stores token in localStorage when 'Remember me' is checked (default)", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/email/i), "owner@test.com");
    await user.type(screen.getByLabelText("Password"), "secret123");
    await user.click(screen.getByRole("button", { name: /sign in/i }));
    await screen.findByTestId("owner-home");

    expect(localStorage.getItem("auth_token")).not.toBeNull();
    expect(sessionStorage.getItem("auth_token")).toBeNull();
  });

  it("stores token in sessionStorage when 'Remember me' is unchecked", async () => {
    const user = userEvent.setup();
    renderPage();

    // Uncheck "Remember me"
    await user.click(screen.getByRole("checkbox", { name: /remember me/i }));

    await user.type(screen.getByLabelText(/email/i), "owner@test.com");
    await user.type(screen.getByLabelText("Password"), "secret123");
    await user.click(screen.getByRole("button", { name: /sign in/i }));
    await screen.findByTestId("owner-home");

    expect(sessionStorage.getItem("auth_token")).not.toBeNull();
    expect(localStorage.getItem("auth_token")).toBeNull();
  });

  it("'Forgot password?' and 'Remember me' are on the same row", () => {
    renderPage();
    const forgotLink  = screen.getByRole("link", { name: /forgot password/i });
    const checkbox    = screen.getByRole("checkbox", { name: /remember me/i });
    // Both must share the same parent container
    expect(forgotLink.closest("div")).toBe(checkbox.closest("label")?.closest("div"));
  });
});
```

### 3c. Update "registration link" test

The existing test checks for "Register your studio" text. Make sure the test still passes after
changing the link color (it should — color is a class, not text content).

### 3d. Existing test about registration link

Test at line 110 of the test file checks:
```
expect(screen.getByRole("link", { name: /register your studio/i })).toBeInTheDocument();
```
This passes unchanged.

---

## Section 4 — DiscoverPage: Authenticated Nav + Footer Cleanup

**File:** `frontend/src/features/public/components/DiscoverPage.tsx`

### 4a. AuthenticatedNav component

Add this component **before** `DiscoverPage`. It uses a plain custom dropdown — no new Radix
packages. The `useEffect` calls are approved (browser API side-effects: outside-click detection
+ keyboard escape).

Import additions needed at the top of the file:
```tsx
import { useRef } from "react";
import { useNavigate } from "react-router-dom";
import { useAppDispatch } from "@/app/hooks";
import { getRoleRedirectPath } from "@/app/router";
import { logout } from "@/features/auth/authSlice";
import type { Role } from "@/shared/types/roles";
```

The component:

```tsx
// ── Authenticated nav ─────────────────────────────────────────────────────────

interface AuthenticatedNavProps {
  user:  { id: string; email: string; name?: string } | null;
  role:  Role | null;
}

function AuthenticatedNav({ user, role }: AuthenticatedNavProps) {
  const dispatch  = useAppDispatch();
  const navigate  = useNavigate();
  const [open, setOpen] = useState(false);
  const menuRef   = useRef<HTMLDivElement>(null);

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
```

### 4b. Read user and role from auth store in DiscoverPage

The `DiscoverPage` already reads `token`. Extend it to also read `user` and `role`:

```tsx
// In DiscoverPage, after the existing const token = ...:
const user  = useAppSelector((s) => s.auth.user);
const role  = useAppSelector((s) => s.auth.role);
```

### 4c. Conditional nav — authenticated vs. unauthenticated

Replace the current `<nav>` block (Sign in + Register studio links) with a conditional:

```tsx
<nav className="flex items-center gap-1" aria-label="Site navigation">
  <Link to="/map"
    className="text-xs text-muted-foreground hover:text-foreground
               transition-colors px-3 py-2 rounded-md hover:bg-muted/40">
    Map
  </Link>

  {token ? (
    <AuthenticatedNav user={user} role={role} />
  ) : (
    <>
      <Link to="/login"
        className="text-xs text-muted-foreground hover:text-foreground
                   transition-colors px-3 py-2 rounded-md hover:bg-muted/40">
        Sign in
      </Link>
      <Link to="/register"
        className="text-xs font-medium px-3 py-2 rounded-md
                   border-2 border-violet-500 text-violet-400
                   bg-violet-500/5
                   hover:bg-violet-500/15 hover:text-violet-300
                   transition-colors
                   focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-violet-500">
        Register studio
      </Link>
    </>
  )}
</nav>
```

Note: "View studios on map" was renamed to "Map" to match the footer and avoid inconsistency.
This requires updating one existing test (see Section 5).

### 4d. Footer cleanup

Replace the current footer nav with a leaner version:
- Remove the circular "Discover" link (takes you to the current page — useless)
- Hide "Register studio" for authenticated users
- Bump contrast from `/50` to `/65`

```tsx
<footer className="py-5 border-t border-border/40">
  <div className="max-w-6xl mx-auto px-4 flex flex-col sm:flex-row items-center
                  justify-between gap-3 text-xs text-foreground/65">
    <span>© {new Date().getFullYear()} Pena e Artë. All rights reserved.</span>
    <nav aria-label="Footer links" className="flex items-center gap-4">
      <Link to="/map" className="hover:text-foreground/80 transition-colors">
        Map
      </Link>
      {!token && (
        <Link to="/register" className="hover:text-foreground/80 transition-colors">
          Register studio
        </Link>
      )}
    </nav>
  </div>
</footer>
```

---

## Section 5 — DiscoverPage Tests

**File:** `frontend/src/features/public/__tests__/DiscoverPage.test.tsx`

### 5a. Add auth store helper and renderLoggedInPage

Add after the existing `makeStore` and `renderPage` helpers:

```tsx
function makeAuthStore() {
  return configureStore({
    reducer: {
      auth: authReducer,
      [publicApi.reducerPath]:      publicApi.reducer,
      [savedImagesApi.reducerPath]: savedImagesApi.reducer,
    },
    middleware: (gd) => gd().concat(publicApi.middleware, savedImagesApi.middleware),
    preloadedState: {
      auth: {
        user:                { id: "u-1", email: "phi@test.com", name: "Phi" },
        token:               "fake-jwt-token",
        refreshToken:        null,
        tenantId:            "t-1",
        role:                "owner" as const,
        pendingReferralCode: null,
      },
    },
  });
}

function renderLoggedInPage() {
  render(
    <Provider store={makeAuthStore()}>
      <MemoryRouter>
        <DiscoverPage />
      </MemoryRouter>
    </Provider>,
  );
}
```

### 5b. Update broken tests

**Update** the test that checks for "View studios on map" — the nav link was renamed to "Map":

```tsx
// OLD (delete this):
it("renders 'View studios on map' and 'Sign in' nav links", () => {
  renderPage();
  expect(screen.getByRole("link", { name: /view studios on map/i })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: /sign in/i })).toBeInTheDocument();
});

// NEW (replace with):
it("renders 'Map' nav link when unauthenticated", () => {
  renderPage();
  const header = document.querySelector("header")!;
  const navLinks = Array.from(header.querySelectorAll("a"));
  expect(navLinks.some((a) => /^map$/i.test(a.textContent?.trim() ?? ""))).toBe(true);
});

it("renders 'Sign in' nav link when unauthenticated", () => {
  renderPage();
  expect(screen.getByRole("link", { name: /^sign in$/i })).toBeInTheDocument();
});
```

**Update** the footer test — "Discover" link is gone, "Register studio" only appears for unauthenticated:

```tsx
// OLD (delete this):
it("footer renders Discover, Map, and Register links", () => {
  renderPage();
  const footer = document.querySelector("footer");
  const links  = footer!.querySelectorAll("a");
  const texts  = Array.from(links).map((a) => a.textContent?.trim());
  expect(texts).toContain("Discover");
  expect(texts).toContain("Map");
  expect(texts).toContain("Register studio");
});

// NEW (replace with two tests):
it("footer renders Map link for unauthenticated users", () => {
  renderPage();
  const footer = document.querySelector("footer")!;
  const links  = Array.from(footer.querySelectorAll("a")).map((a) => a.textContent?.trim());
  expect(links).toContain("Map");
  expect(links).not.toContain("Discover"); // circular link removed
});

it("footer renders 'Register studio' for unauthenticated users only", () => {
  renderPage();
  const footer = document.querySelector("footer")!;
  const links  = Array.from(footer.querySelectorAll("a")).map((a) => a.textContent?.trim());
  expect(links).toContain("Register studio");
});
```

### 5c. New tests for authenticated state

Add a new `describe("DiscoverPage — authenticated nav", ...)` block:

```tsx
describe("DiscoverPage — authenticated nav", () => {
  it("shows avatar button and hides 'Sign in' when authenticated", () => {
    renderLoggedInPage();
    expect(screen.getByRole("button", { name: /account menu/i })).toBeInTheDocument();
    // "Sign in" link must not be in the nav
    const header = document.querySelector("header")!;
    const navSignIn = Array.from(header.querySelectorAll("a")).find((a) =>
      /^sign in$/i.test(a.textContent?.trim() ?? "")
    );
    expect(navSignIn).toBeUndefined();
  });

  it("shows avatar initials derived from user name", () => {
    renderLoggedInPage();
    const avatarBtn = screen.getByRole("button", { name: /account menu/i });
    // "Phi" → first letter "P"
    expect(avatarBtn.textContent).toBe("P");
  });

  it("hides 'Register studio' nav button when authenticated", () => {
    renderLoggedInPage();
    const header = document.querySelector("header")!;
    const registerLinks = Array.from(header.querySelectorAll("a")).filter((a) =>
      /register studio/i.test(a.textContent ?? "")
    );
    expect(registerLinks).toHaveLength(0);
  });

  it("opens account dropdown when avatar button is clicked", async () => {
    const user = userEvent.setup();
    renderLoggedInPage();
    await user.click(screen.getByRole("button", { name: /account menu/i }));
    expect(screen.getByRole("menu", { name: /account options/i })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: /dashboard/i })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: /sign out/i })).toBeInTheDocument();
  });

  it("footer hides 'Register studio' when authenticated", () => {
    renderLoggedInPage();
    const footer = document.querySelector("footer")!;
    const links  = Array.from(footer.querySelectorAll("a")).map((a) => a.textContent?.trim());
    expect(links).not.toContain("Register studio");
  });

  it("footer does not contain a circular 'Discover' link (unauthenticated)", () => {
    renderPage();
    const footer = document.querySelector("footer")!;
    const links  = Array.from(footer.querySelectorAll("a")).map((a) => a.textContent?.trim());
    expect(links).not.toContain("Discover");
  });

  it("avatar button has aria-expanded=false when dropdown is closed", () => {
    renderLoggedInPage();
    const btn = screen.getByRole("button", { name: /account menu/i });
    expect(btn).toHaveAttribute("aria-expanded", "false");
  });

  it("avatar button has aria-expanded=true when dropdown is open", async () => {
    const user = userEvent.setup();
    renderLoggedInPage();
    await user.click(screen.getByRole("button", { name: /account menu/i }));
    expect(screen.getByRole("button", { name: /account menu/i }))
      .toHaveAttribute("aria-expanded", "true");
  });
});
```

---

## Section 6 — PortfolioFeed: Touch Targets + Attribution Star

**File:** `frontend/src/features/public/components/PortfolioFeed.tsx`

### 6a. Style chip touch targets

WCAG 2.5.5 requires 44×44px minimum touch target. Current chips are ~30px tall (`py-1`).

In `StyleChips`, update the button className:

```tsx
// Before:
className={`shrink-0 px-3 py-1 rounded-full text-xs font-medium
            border transition-colors whitespace-nowrap
            ${isActive ? ... : ...}`}

// After — add min-h-[44px] and bump vertical padding:
className={`shrink-0 px-3 py-2 min-h-[44px] rounded-full text-xs font-medium
            border transition-colors whitespace-nowrap flex items-center
            ${isActive
              ? "bg-violet-600 border-violet-500 text-white"
              : "border-border text-muted-foreground hover:text-foreground hover:border-border/80"
            }`}
```

### 6b. Attribution strip — accessible star rating

The unicode `★` glyph is announced by screen readers as "black star". Fix by adding a
properly labelled wrapper and hiding the glyph from AT.

In `PortfolioTile`, update the attribution star block (currently lines ~425-432):

```tsx
{image.reviewCount > 0 && (
  <div
    className="flex items-center gap-0.5 shrink-0"
    aria-label={`Rating: ${image.averageRating?.toFixed(1) ?? "0"} out of 5`}
  >
    <span aria-hidden="true" className="text-yellow-400 text-[10px]">★</span>
    <span aria-hidden="true" className="text-white/60 text-[10px]">
      {image.averageRating?.toFixed(1)}
    </span>
  </div>
)}
```

---

## Section 7 — PortfolioFeed Tests

**File:** `frontend/src/features/public/__tests__/PortfolioFeed.test.tsx`

### 7a. Touch target test

Add to the existing test suite:

```tsx
describe("PortfolioFeed — style chips accessibility", () => {
  it("all style filter chips have min-h-[44px] for WCAG 2.5.5 touch target compliance", async () => {
    // The feed returns empty so we see chips without the grid loading.
    // The style chips are always rendered.
    // Wait for the feed to finish loading (skeleton disappears):
    await waitFor(() =>
      expect(screen.queryByLabelText("Loading portfolio")).not.toBeInTheDocument()
    );

    const chips = screen.getAllByRole("radio");
    chips.forEach((chip) => {
      expect(chip.className).toContain("min-h-[44px]");
    });
  });
});
```

If `waitFor` is not imported, add it: `import { render, screen, waitFor } from "@testing-library/react";`

---

## Section 8 — Verification Checklist

After all changes, run:

```bash
cd "frontend"
pnpm test
```

All tests must pass. Then manually verify:

- [ ] `authSlice` tests pass — including the new remember-me storage tests
- [ ] `LoginPage` tests pass — existing + new remember-me describe block
- [ ] `DiscoverPage` tests pass — updated footer + nav tests + new authenticated nav describe
- [ ] `PortfolioFeed` tests pass — touch target test
- [ ] TypeScript: `pnpm build` produces no type errors
- [ ] Lint: `pnpm lint` produces no new warnings

**Do not ship if any test is red.** Fix the failure before moving on.

---

## Do Not Change

- `docs/claude/architecture.md` — no architectural decisions in this batch
- Any backend files — this is a pure frontend polish pass
- `frontend/src/features/public/savedImagesApi.ts` — not in scope
- Any test file not listed above
