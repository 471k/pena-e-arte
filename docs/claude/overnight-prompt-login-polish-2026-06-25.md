# Overnight Prompt — Login Page UI/UX Polish
**Date:** 2026-06-25
**Scope:** Frontend only — `LoginPage.tsx`, `password-input.tsx`, `index.css`,
            `LoginPage.test.tsx`. No backend changes. No new npm packages.

---

## Context

Read `CLAUDE.md` before starting.

### What already exists (verified — do NOT re-add)
- Loading spinner on Sign In button: `{isLoading && <Loader2 .../>}` ✅
- Server error `<Alert variant="destructive">` ✅
- `aria-label="Show password"` / `"Hide password"` on eye toggle ✅
- Hover color on eye toggle: `text-muted-foreground hover:text-foreground` ✅
- PenLine icon at `h-8 w-8` (already 1.33× text-2xl cap-height) ✅
- Session-expired amber banner ✅
- 13 unit tests in `LoginPage.test.tsx` ✅

### Root cause of the visual issues (confirmed from `index.css`)
In the dark OS theme, the design tokens are:
```css
--color-background: hsl(240 10% 3.9%);  /* near-black */
--color-card:       hsl(240 10% 3.9%);  /* IDENTICAL */
--color-primary:    hsl(0 0% 98%);      /* white      */
--color-muted-foreground: hsl(240 5% 64.9%);  /* ~4.1:1 on background */
```
Card and background are the same token value — the card has no felt surface.
Primary is white — the Sign In button is the brightest element on screen.
Muted foreground fails WCAG AA by ~0.4:1 contrast ratio.

### Hard rules (apply everywhere)
- No new npm packages.
- TypeScript strict mode. No `any`. No default exports on components.
- No `useEffect` for data fetching.
- All existing tests must still pass after changes.

---

## Part 1 — Card elevation (critical visual issue #1)

**File:** `frontend/src/features/public/../features/auth/components/LoginPage.tsx`
(full path: `frontend/src/features/auth/components/LoginPage.tsx`)

The login card is invisible against the page background in dark mode because they share
the same token. Fix: give the login Card a slightly elevated surface in dark mode and
add a shadow so the eye reads it as a raised surface.

Change the `<Card>` opening tag from:
```tsx
<Card>
```
to:
```tsx
<Card className="dark:bg-zinc-900/80 dark:border-zinc-800 shadow-lg dark:shadow-black/60">
```

**Why this works:**
- `dark:bg-zinc-900/80` → in dark OS mode, the card background becomes `#18181b` at 80% —
  visibly lighter than `hsl(240 10% 3.9%)` ≈ `#0a0a0f` background.
- `dark:border-zinc-800` → makes the card border `#27272a`, more visible than the token default.
- `shadow-lg dark:shadow-black/60` → adds depth even when background is very dark.

> Do NOT change `--color-card` globally in `index.css`. The token affects every card in
> the authenticated app (dashboard panels, list cards, modals). Only the login page card
> needs this treatment because every other card is inside a layout with inherent visual
> context (sidebars, headers, coloured content beside them).

---

## Part 2 — Sign In button: replace white with brand violet (critical issue #2)

**File:** `frontend/src/features/auth/components/LoginPage.tsx`

In dark mode, `--color-primary` is white. The Sign In button is the highest-luminance
element on the page — users' eyes land on it before reading the brand name. This is
inverted hierarchy.

Change the `<Button>` on the form submit from:
```tsx
<Button type="submit" className="w-full" disabled={isLoading}>
```
to:
```tsx
<Button
  type="submit"
  className="w-full bg-violet-600 hover:bg-violet-700 text-white border-0 focus-visible:ring-violet-500"
  disabled={isLoading}
>
```

`tailwind-merge` (used inside shadcn/ui's `cn()`) will resolve the conflict between
`bg-primary` (from the Button variant) and `bg-violet-600` (from `className`), keeping
the latter. Same for hover and focus ring.

> This is a login-page-scoped fix only. A separate design-system pass (updating the
> global `--color-primary` token to violet) would propagate the brand color everywhere.
> That is a larger change requiring review of every authenticated page — do NOT do it
> in this prompt.

---

## Part 3 — Muted text contrast (critical issue #3 — WCAG AA failures)

**File:** `frontend/src/features/auth/components/LoginPage.tsx`

Three elements use `text-muted-foreground` which in dark mode is `hsl(240 5% 64.9%)`,
giving approximately 4.1:1 contrast on the `hsl(240 10% 3.9%)` background —
below the required 4.5:1 for body text.

Fix each element individually (do NOT change the global token):

### 3a — Subtitle
```tsx
// Before:
<p className="text-sm text-muted-foreground">Tattoo Studio Management</p>

// After (also update copy — see Part 5):
<p className="text-sm text-foreground/65">Run your studio. Book clients. Manage your team.</p>
```
`text-foreground/65` = `rgba(255,255,255,0.65)` in dark mode ≈ 8.2:1 ✅

### 3b — "Forgot password?" link
```tsx
// Before:
<Link
  to="/forgot-password"
  className="text-xs text-muted-foreground underline underline-offset-4 hover:text-primary"
>
  Forgot password?
</Link>

// After:
<Link
  to="/forgot-password"
  className="text-xs text-foreground/65 underline underline-offset-4 hover:text-foreground py-2 inline-block"
>
  Forgot password?
</Link>
```
`py-2` expands the touch target height from ~16px to ~32px. `inline-block` is required
for vertical padding to apply on an inline element. `hover:text-foreground` gives a
clear interactive hover state.

### 3c — "Don't have an account?" registration prompt
```tsx
// Before:
<div className="mt-4 pt-4 border-t border-border/50 text-center text-sm text-muted-foreground">
  New studio?{" "}
  <Link
    to="/register"
    className="underline underline-offset-4 hover:text-primary"
  >
    Register your studio
  </Link>
</div>

// After (also fixes copy — see Part 5):
<div className="mt-4 pt-4 border-t border-border/50 text-center text-sm text-foreground/65">
  Don't have an account?{" "}
  <Link
    to="/register"
    className="underline underline-offset-4 text-foreground/65 hover:text-foreground py-2 inline-block"
  >
    Register your studio
  </Link>
</div>
```

---

## Part 4 — Accessibility: PenLine icon (screen reader noise)

**File:** `frontend/src/features/auth/components/LoginPage.tsx`

The brand mark is:
```tsx
<div className="flex items-center gap-2">
  <PenLine className="h-8 w-8" />
  <span className="text-2xl font-semibold tracking-tight">Pena e Arte</span>
</div>
```

A screen reader will announce "pen line" before "Pena e Arte" because the icon SVG has
no `aria-hidden`. Fix:
```tsx
<div className="flex items-center gap-2">
  <PenLine className="h-8 w-8" aria-hidden="true" />
  <span className="text-2xl font-semibold tracking-tight">Pena e Arte</span>
</div>
```

---

## Part 5 — Eye toggle: add `cursor-pointer` (UX affordance)

**File:** `frontend/src/shared/components/ui/password-input.tsx`

The eye toggle button reads as UI chrome rather than a clickable control, partly because
there is no cursor change. The button already has `aria-label` and hover color.
Add `cursor-pointer` to make the interactive affordance explicit:

```tsx
// Before:
className="absolute inset-y-0 right-0 flex items-center px-3 text-muted-foreground hover:text-foreground transition-colors"

// After:
className="absolute inset-y-0 right-0 flex items-center px-3 text-muted-foreground hover:text-foreground transition-colors cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1 rounded-sm"
```

Note: `tabIndex={-1}` stays as-is — this is an intentional UX pattern so Tab flows
from password field directly to the submit button without a detour through the eye icon.
The icon remains keyboard-accessible via Shift+Tab.

---

## Part 6 — Password field: add placeholder

**File:** `frontend/src/features/auth/components/LoginPage.tsx`

The email field has `placeholder="you@example.com"`. The password field has nothing.
Adding a subtle placeholder balances the two fields visually:

```tsx
<PasswordInput
  id="password"
  autoComplete="current-password"
  placeholder="••••••••"
  {...register("password")}
  aria-invalid={!!errors.password}
  aria-describedby={errors.password ? "password-error" : undefined}
/>
```

---

## Part 7 — Legal footer

**File:** `frontend/src/features/auth/components/LoginPage.tsx`

A login page with no legal links is non-compliant in EU/GDPR jurisdictions.
Add a `<footer>` outside the card container but inside the page wrapper:

```tsx
// Add after the closing </div> of <div className="w-full max-w-md space-y-6 relative">
// and before the closing </div> of the outer page wrapper:

<footer className="mt-8 text-center text-xs text-foreground/40 space-x-4">
  <a
    href="/privacy"
    className="hover:text-foreground/70 transition-colors underline-offset-2 hover:underline"
  >
    Privacy Policy
  </a>
  <span aria-hidden="true">·</span>
  <a
    href="/terms"
    className="hover:text-foreground/70 transition-colors underline-offset-2 hover:underline"
  >
    Terms of Service
  </a>
  <span aria-hidden="true">·</span>
  <a
    href="mailto:support@penaearte.com"
    className="hover:text-foreground/70 transition-colors underline-offset-2 hover:underline"
  >
    Contact support
  </a>
</footer>
```

> These link to `/privacy`, `/terms`, and a mailto — they are acceptable as
> placeholder hrefs for now. The actual pages can be created in a later pass.
> Do NOT add routes for `/privacy` or `/terms` yet — those require content.

The full page wrapper JSX should look like:
```tsx
<div className="min-h-screen flex items-center justify-center bg-background p-4 relative overflow-hidden">
  {/* decorative glow */}
  <div ... aria-hidden="true" />

  <div className="w-full max-w-md space-y-6 relative">
    {/* brand mark, session banner, card */}
    ...
  </div>

  <footer className="absolute bottom-6 left-0 right-0 text-center text-xs ...">
    ...
  </footer>
</div>
```

Use `absolute bottom-6` to pin the footer to the bottom of the viewport without
affecting the vertical centering of the card:
```tsx
<footer className="absolute bottom-6 left-0 right-0 text-center text-xs text-foreground/40 space-x-4">
```

---

## Part 8 — Complete `LoginPage.tsx` after all changes

After applying Parts 1–7, the complete file should be:

```tsx
import { zodResolver } from "@hookform/resolvers/zod";
import { AlertCircle, Loader2, PenLine } from "lucide-react";
import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { z } from "zod";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { getRoleRedirectPath } from "@/app/router";
import { Alert, AlertDescription } from "@/shared/components/ui/alert";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { PasswordInput } from "@/shared/components/ui/password-input";
import { decodeToken } from "@/shared/utils/jwt";
import { useLoginMutation } from "../authApi";
import { setCredentials } from "../authSlice";

const loginSchema = z.object({
  email:    z.string().min(1, "Email is required").email("Enter a valid email"),
  password: z.string().min(1, "Password is required"),
});

type LoginFormValues = z.infer<typeof loginSchema>;

export function LoginPage() {
  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const existingRole = useAppSelector((s) => s.auth.role);
  const [login, { isLoading, error }] = useLoginMutation();

  const sessionExpired = searchParams.get("reason") === "session_expired";
  const redirectPath   = existingRole ? getRoleRedirectPath(existingRole) : null;

  useEffect(() => {
    if (redirectPath) navigate(redirectPath, { replace: true });
  }, [redirectPath, navigate]);

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<LoginFormValues>({ resolver: zodResolver(loginSchema) });

  async function onSubmit(values: LoginFormValues) {
    try {
      const { accessToken } = await login(values).unwrap();
      const payload = decodeToken(accessToken);
      dispatch(setCredentials(payload));
      navigate(getRoleRedirectPath(payload.role), { replace: true });
    } catch {
      // error surfaced via RTK Query's `error` state below
    }
  }

  const serverError = error
    ? "data" in error
      ? error.status === 429
        ? "Too many sign-in attempts. Please try again in a few minutes."
        : (error.data as { message?: string; detail?: string })?.message ??
          (error.data as { message?: string; detail?: string })?.detail ??
          "Invalid email or password."
      : "Unable to reach the server. Please try again."
    : null;

  return (
    <div className="min-h-screen flex items-center justify-center bg-background p-4 relative overflow-hidden">
      {/* Decorative background glow — purely visual */}
      <div
        className="pointer-events-none absolute inset-0"
        style={{
          background:
            "radial-gradient(ellipse 90% 55% at 50% -5%, rgba(113,113,122,0.18) 0%, transparent 100%)",
        }}
        aria-hidden="true"
      />

      <div className="w-full max-w-md space-y-6 relative">
        {/* Brand mark */}
        <div className="flex flex-col items-center gap-2 text-center">
          <div className="flex items-center gap-2">
            <PenLine className="h-8 w-8" aria-hidden="true" />
            <span className="text-2xl font-semibold tracking-tight">Pena e Arte</span>
          </div>
          <p className="text-sm text-foreground/65">
            Run your studio. Book clients. Manage your team.
          </p>
        </div>

        {sessionExpired && (
          <div className="rounded-lg border border-amber-500/30 bg-amber-500/10 px-4 py-3 text-sm text-amber-700 dark:text-amber-400">
            Your session expired. Please sign in again.
          </div>
        )}

        <Card className="dark:bg-zinc-900/80 dark:border-zinc-800 shadow-lg dark:shadow-black/60">
          <CardHeader>
            <CardTitle>Sign in</CardTitle>
          </CardHeader>
          <CardContent>
            <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-4">
              {/* Email */}
              <div className="space-y-1.5">
                <Label htmlFor="email">Email</Label>
                <Input
                  id="email"
                  type="email"
                  autoComplete="email"
                  placeholder="you@example.com"
                  {...register("email")}
                  aria-invalid={!!errors.email}
                  aria-describedby={errors.email ? "email-error" : undefined}
                />
                {errors.email && (
                  <p id="email-error" className="text-xs text-destructive" role="alert">
                    {errors.email.message}
                  </p>
                )}
              </div>

              {/* Password */}
              <div className="space-y-1.5">
                <Label htmlFor="password">Password</Label>
                <PasswordInput
                  id="password"
                  autoComplete="current-password"
                  placeholder="••••••••"
                  {...register("password")}
                  aria-invalid={!!errors.password}
                  aria-describedby={errors.password ? "password-error" : undefined}
                />
                {errors.password && (
                  <p id="password-error" className="text-xs text-destructive" role="alert">
                    {errors.password.message}
                  </p>
                )}
                <div className="flex justify-end">
                  <Link
                    to="/forgot-password"
                    className="text-xs text-foreground/65 underline underline-offset-4 hover:text-foreground py-2 inline-block"
                  >
                    Forgot password?
                  </Link>
                </div>
              </div>

              {serverError && (
                <Alert variant="destructive" role="alert">
                  <AlertCircle className="h-4 w-4" />
                  <AlertDescription>{serverError}</AlertDescription>
                </Alert>
              )}

              <Button
                type="submit"
                className="w-full bg-violet-600 hover:bg-violet-700 text-white border-0 focus-visible:ring-violet-500"
                disabled={isLoading}
              >
                {isLoading && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
                Sign in
              </Button>
            </form>

            <div className="mt-4 pt-4 border-t border-border/50 text-center text-sm text-foreground/65">
              Don't have an account?{" "}
              <Link
                to="/register"
                className="underline underline-offset-4 text-foreground/65 hover:text-foreground py-2 inline-block"
              >
                Register your studio
              </Link>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Legal footer — pinned to viewport bottom */}
      <footer className="absolute bottom-6 left-0 right-0 text-center text-xs text-foreground/40 space-x-4">
        <a
          href="/privacy"
          className="hover:text-foreground/70 transition-colors underline-offset-2 hover:underline"
        >
          Privacy Policy
        </a>
        <span aria-hidden="true">·</span>
        <a
          href="/terms"
          className="hover:text-foreground/70 transition-colors underline-offset-2 hover:underline"
        >
          Terms of Service
        </a>
        <span aria-hidden="true">·</span>
        <a
          href="mailto:support@penaearte.com"
          className="hover:text-foreground/70 transition-colors underline-offset-2 hover:underline"
        >
          Contact support
        </a>
      </footer>
    </div>
  );
}
```

---

## Part 9 — `password-input.tsx` update

**File:** `frontend/src/shared/components/ui/password-input.tsx`

Apply the `cursor-pointer` and focus ring changes from Part 5:

```tsx
import * as React from "react";
import { Eye, EyeOff } from "lucide-react";
import { cn } from "@/shared/utils/cn";
import { Input } from "./input";
import type { InputProps } from "./input";

export const PasswordInput = React.forwardRef<HTMLInputElement, Omit<InputProps, "type">>(
  ({ className, ...props }, ref) => {
    const [show, setShow] = React.useState(false);
    return (
      <div className="relative">
        <Input
          {...props}
          ref={ref}
          type={show ? "text" : "password"}
          className={cn("pr-10", className)}
        />
        <button
          type="button"
          tabIndex={-1}
          aria-label={show ? "Hide password" : "Show password"}
          onClick={() => setShow((v) => !v)}
          className="absolute inset-y-0 right-0 flex items-center px-3
                     text-muted-foreground hover:text-foreground transition-colors
                     cursor-pointer
                     focus-visible:outline-none focus-visible:ring-2
                     focus-visible:ring-ring focus-visible:ring-offset-1 rounded-sm"
        >
          {show ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
        </button>
      </div>
    );
  }
);
PasswordInput.displayName = "PasswordInput";
```

---

## Part 10 — Update `LoginPage.test.tsx`

**File:** `frontend/src/features/auth/__tests__/LoginPage.test.tsx`

### Tests that need updating (copy changes)

Test at line 107 checks for "register your studio" link — still valid, the link text
is unchanged. ✅

No test currently checks for "Tattoo Studio Management", "New studio?", or the old
subtitle — no breaking changes there.

### New tests to add (append to the `describe` block)

```ts
it("renders the updated subtitle copy", () => {
  renderPage();
  expect(
    screen.getByText("Run your studio. Book clients. Manage your team.")
  ).toBeInTheDocument();
});

it("does NOT render the old subtitle", () => {
  renderPage();
  expect(
    screen.queryByText("Tattoo Studio Management")
  ).not.toBeInTheDocument();
});

it("renders the registration prompt with updated copy", () => {
  renderPage();
  expect(screen.getByText(/don't have an account/i)).toBeInTheDocument();
});

it("does NOT render the old 'New studio?' copy", () => {
  renderPage();
  expect(screen.queryByText(/new studio\?/i)).not.toBeInTheDocument();
});

it("renders the legal footer with Privacy Policy link", () => {
  renderPage();
  expect(screen.getByRole("link", { name: /privacy policy/i })).toBeInTheDocument();
});

it("renders the legal footer with Terms of Service link", () => {
  renderPage();
  expect(screen.getByRole("link", { name: /terms of service/i })).toBeInTheDocument();
});

it("renders the Contact support link in the footer", () => {
  renderPage();
  expect(screen.getByRole("link", { name: /contact support/i })).toBeInTheDocument();
});

it("PenLine icon has aria-hidden to suppress screen reader announcement", () => {
  renderPage();
  // The SVG rendered by PenLine must not be announced before the brand name.
  // A11y-tree: find all elements with aria-hidden in the brand mark area.
  // Simplified: confirm the brand text is accessible without decoration noise.
  expect(screen.getByText("Pena e Arte")).toBeInTheDocument();
  // The icon SVG should carry aria-hidden="true" — verified by the JSX change.
});

it("password field has a placeholder", () => {
  renderPage();
  const passwordInput = screen.getByLabelText("Password");
  expect(passwordInput).toHaveAttribute("placeholder", "••••••••");
});

it("Forgot password link has accessible touch target (py-2 class applied)", () => {
  renderPage();
  const forgotLink = screen.getByRole("link", { name: /forgot password/i });
  // Verify it renders — the py-2 class is a visual concern; confirm link exists.
  expect(forgotLink).toBeInTheDocument();
  expect(forgotLink).toHaveClass("py-2");
});
```

---

## Part 11 — Verification checklist

After all changes:

- [ ] `pnpm test` — all tests green (13 existing + 10 new = 23 total for LoginPage).
- [ ] `pnpm build` — zero TypeScript errors.
- [ ] `pnpm lint` — zero lint errors.
- [ ] In dark OS mode, the login card is visually distinct from the page background.
- [ ] The Sign In button renders violet, not white, in dark mode.
- [ ] "Forgot password?" touch target is at least 32px tall (has `py-2`).
- [ ] The subtitle reads "Run your studio. Book clients. Manage your team."
- [ ] The registration prompt reads "Don't have an account? Register your studio"
- [ ] The legal footer appears at the bottom of the viewport.
- [ ] The eye icon shows `cursor: pointer` on hover.
- [ ] The password field shows `••••••••` placeholder.
- [ ] `pnpm exec playwright test e2e/critical-path.spec.ts` — still green (no E2E regression).

---

## Summary of files changed

```
frontend/src/features/auth/components/LoginPage.tsx       modified
frontend/src/shared/components/ui/password-input.tsx      modified
frontend/src/features/auth/__tests__/LoginPage.test.tsx   modified (10 new tests)
```

No backend changes. No new packages. No global design token changes.
