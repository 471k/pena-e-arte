# Overnight Prompt — Login Page Overhaul (2026-06-19)

> **Scope:** Frontend-only. No backend changes required — the login API, auth
> flow, and token handling are already correct. All changes are in
> `LoginPage.tsx` and its test file.
>
> No new npm packages. No new NuGet packages.
> Commit after each numbered task.

---

## 0. Mandatory Reading (Do This First)

```
CLAUDE.md
docs/claude/frontend.md
docs/claude/conventions.md
```

Then read these source files in full before touching anything:

```
frontend/src/features/auth/components/LoginPage.tsx
frontend/src/features/auth/__tests__/LoginPage.test.tsx
frontend/src/features/studios/components/RegisterStudioPage.tsx   ← reference for card/form patterns
```

---

## Current State Inventory

The page already has, and must **keep**:
- Zod validation with field-level error messages (`email`, `password`)
- `useLoginMutation` loading state — Loader2 spinner on button while pending
- Server error rendering with `role="alert"` (currently a bare `<p>`)
- Session expired banner when `?reason=session_expired`
- Redirect-if-already-logged-in via `useEffect`
- `PasswordInput` with eye toggle
- `PenLine` icon + "Pena e Arte" brand + "Tattoo Studio Management" subtitle
- "Forgot password?" link to `/forgot-password`
- "New studio? Register your studio" link to `/register`

The existing tests all pass and must **continue to pass** after this work.

---

## 1. Remove the Boilerplate `CardDescription`

**File:** `frontend/src/features/auth/components/LoginPage.tsx`

The current `<CardDescription>Enter your credentials to access your account.</CardDescription>`
occupies a full line in a compact card and communicates nothing.
Every user on a login screen already knows why they're there.
It signals a template, not a considered product.

Remove the `<CardDescription>` element entirely:

```diff
-<CardDescription>Enter your credentials to access your account.</CardDescription>
```

Also remove `CardDescription` from the import line:

```diff
-import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/shared/components/ui/card";
+import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
```

Run `pnpm --dir frontend tsc --noEmit` after every file change — must stay zero errors.

**Commit:** `fix(auth): remove boilerplate CardDescription from LoginPage`

---

## 2. Add Subtle Background Gradient

**File:** `frontend/src/features/auth/components/LoginPage.tsx`

The pure `bg-background` void makes the page read as broken.
Add a decorative radial glow centered behind the card.
The approach uses an absolutely-positioned `aria-hidden` div so the gradient
is purely visual and does not affect layout or accessibility.

Change the outer container:

```tsx
// Before:
<div className="min-h-screen flex items-center justify-center bg-background p-4">
  <div className="w-full max-w-md space-y-6">

// After:
<div className="min-h-screen flex items-center justify-center bg-background p-4 relative overflow-hidden">
  {/* Decorative background glow — purely visual, aria-hidden */}
  <div
    className="pointer-events-none absolute inset-0"
    style={{
      background:
        "radial-gradient(ellipse 90% 55% at 50% -5%, rgba(113,113,122,0.18) 0%, transparent 100%)",
    }}
    aria-hidden="true"
  />
  <div className="w-full max-w-md space-y-6 relative">
```

Close the new outer `<div>` — ensure the JSX closes correctly.

No imports needed (inline style, no new class).

**Commit:** `feat(auth): add subtle radial glow to login page background`

---

## 3. Upgrade Server Error to `Alert` Component

**File:** `frontend/src/features/auth/components/LoginPage.tsx`

The current server error is a bare `<p>`. Upgrade to a proper `Alert` with an
icon for visual weight and clearer ARIA semantics.

**3a. Add imports:**

```tsx
import { Alert, AlertDescription } from "@/shared/components/ui/alert";
import { AlertCircle } from "lucide-react";
```

(Add `AlertCircle` to the existing lucide import line; add the Alert import from
`@/shared/components/ui/alert`.)

**3b. Detect 429 rate-limiting in `serverError`:**

```tsx
// Before:
const serverError = error
  ? "data" in error
    ? (error.data as { message?: string; detail?: string })?.message ??
      (error.data as { message?: string; detail?: string })?.detail ??
      "Invalid email or password."
    : "Unable to reach the server. Please try again."
  : null;

// After:
const serverError = error
  ? "data" in error
    ? error.status === 429
      ? "Too many sign-in attempts. Please try again in a few minutes."
      : (error.data as { message?: string; detail?: string })?.message ??
        (error.data as { message?: string; detail?: string })?.detail ??
        "Invalid email or password."
    : "Unable to reach the server. Please try again."
  : null;
```

**3c. Replace the bare `<p>` with `<Alert>`:**

```tsx
// Before:
{serverError && (
  <p className="text-sm text-destructive" role="alert">
    {serverError}
  </p>
)}

// After:
{serverError && (
  <Alert variant="destructive" role="alert">
    <AlertCircle className="h-4 w-4" />
    <AlertDescription>{serverError}</AlertDescription>
  </Alert>
)}
```

The `role="alert"` stays on the `<Alert>` element itself, not on a child —
this ensures screen readers announce it immediately when it appears.

Existing tests use `findByText(...)` on the message string — they still pass
because `<AlertDescription>` renders the text as DOM content.

**Commit:** `feat(auth): upgrade server error to Alert component, handle 429 rate limit`

---

## 4. Add `aria-describedby` to Error-State Inputs

**File:** `frontend/src/features/auth/components/LoginPage.tsx`

When a field has a validation error, the error `<p>` should be programmatically
associated with its input via `aria-describedby`. This is required for WCAG 1.3.1.

**4a. Email input + error paragraph:**

```tsx
// Before:
<Input
  id="email"
  type="email"
  autoComplete="email"
  placeholder="you@example.com"
  {...register("email")}
  aria-invalid={!!errors.email}
/>
{errors.email && (
  <p className="text-xs text-destructive">{errors.email.message}</p>
)}

// After:
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
```

**4b. Password input + error paragraph:**

```tsx
// Before:
<PasswordInput
  id="password"
  autoComplete="current-password"
  {...register("password")}
  aria-invalid={!!errors.password}
/>
{errors.password && (
  <p className="text-xs text-destructive">{errors.password.message}</p>
)}

// After:
<PasswordInput
  id="password"
  autoComplete="current-password"
  {...register("password")}
  aria-invalid={!!errors.password}
  aria-describedby={errors.password ? "password-error" : undefined}
/>
{errors.password && (
  <p id="password-error" className="text-xs text-destructive" role="alert">
    {errors.password.message}
  </p>
)}
```

**Commit:** `fix(auth): add aria-describedby and role=alert to field error messages`

---

## 5. Move "Forgot password?" Below the Password Input

**File:** `frontend/src/features/auth/components/LoginPage.tsx`

The current position (right-aligned on the `<Label>` line) is non-standard
and reduces discoverability — users who scan the label quickly will miss it.
Industry standard (Gmail, Linear, Notion) places it below the input, right-aligned.

**Before** (the whole password group):

```tsx
<div className="space-y-1.5">
  <div className="flex items-center justify-between">
    <Label htmlFor="password">Password</Label>
    <Link
      to="/forgot-password"
      className="text-xs text-muted-foreground underline underline-offset-4 hover:text-primary"
    >
      Forgot password?
    </Link>
  </div>
  <PasswordInput
    id="password"
    autoComplete="current-password"
    {...register("password")}
    aria-invalid={!!errors.password}
    aria-describedby={errors.password ? "password-error" : undefined}
  />
  {errors.password && (
    <p id="password-error" className="text-xs text-destructive" role="alert">
      {errors.password.message}
    </p>
  )}
</div>
```

**After** (label stands alone; "Forgot password?" moves below the error message):

```tsx
<div className="space-y-1.5">
  <Label htmlFor="password">Password</Label>
  <PasswordInput
    id="password"
    autoComplete="current-password"
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
      className="text-xs text-muted-foreground underline underline-offset-4 hover:text-primary"
    >
      Forgot password?
    </Link>
  </div>
</div>
```

The existing test `getByRole("link", { name: /forgot password/i })` still passes
— the link still exists, just repositioned in the DOM.

**Commit:** `fix(auth): move Forgot password link below password input`

---

## 6. Move Registration Link Inside the Card

**File:** `frontend/src/features/auth/components/LoginPage.tsx`

The "New studio? Register your studio" paragraph currently sits on the raw dark
background outside the card. Users who finish reading the form and don't scroll
down may never notice it. Moving it inside the card with a visual separator
increases discoverability dramatically.

**6a. Remove the outer `<p>` entirely** (currently below `</Card>`):

```diff
-<p className="text-center text-sm text-muted-foreground">
-  New studio?{" "}
-  <Link to="/register" className="underline underline-offset-4 hover:text-primary">
-    Register your studio
-  </Link>
-</p>
```

**6b. Add it inside `<CardContent>`, immediately after the closing `</form>` tag:**

```tsx
{/* Separator + registration link — inside the card */}
<div className="mt-4 pt-4 border-t border-border/50 text-center text-sm text-muted-foreground">
  New studio?{" "}
  <Link
    to="/register"
    className="underline underline-offset-4 hover:text-primary"
  >
    Register your studio
  </Link>
</div>
```

The full `<CardContent>` block should now look like:

```tsx
<CardContent>
  <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-4">
    {/* email field */}
    {/* password field + forgot password link */}
    {/* server error Alert */}
    {/* submit button */}
  </form>
  <div className="mt-4 pt-4 border-t border-border/50 text-center text-sm text-muted-foreground">
    New studio?{" "}
    <Link to="/register" className="underline underline-offset-4 hover:text-primary">
      Register your studio
    </Link>
  </div>
</CardContent>
```

Also remove the now-empty outer `<div className="w-full max-w-md space-y-6 relative">` child
`<p>` element — only the logo block, the session expired banner, and the card remain
as direct children of that outer div.

The existing test `getByRole("link", { name: /register your studio/i })` still passes
— the link exists, now inside the card DOM.

**Commit:** `feat(auth): move registration link inside the card with separator`

---

## 7. Complete File After All Changes

After applying Tasks 1–6, `LoginPage.tsx` should look exactly like this:

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
  email: z.string().min(1, "Email is required").email("Enter a valid email"),
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
      // error is surfaced via RTK Query's `error` state below
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

        {/* ── Brand mark ──────────────────────────────────────────── */}
        <div className="flex flex-col items-center gap-2 text-center">
          <div className="flex items-center gap-2">
            <PenLine className="h-8 w-8" />
            <span className="text-2xl font-semibold tracking-tight">Pena e Arte</span>
          </div>
          <p className="text-sm text-muted-foreground">Tattoo Studio Management</p>
        </div>

        {/* ── Session expired banner ───────────────────────────────── */}
        {sessionExpired && (
          <div className="rounded-lg border border-amber-500/30 bg-amber-500/10 px-4 py-3 text-sm text-amber-700 dark:text-amber-400">
            Your session expired. Please sign in again.
          </div>
        )}

        {/* ── Login card ──────────────────────────────────────────── */}
        <Card>
          <CardHeader>
            <CardTitle>Sign in</CardTitle>
            {/* CardDescription intentionally removed — filler copy */}
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
                  {...register("password")}
                  aria-invalid={!!errors.password}
                  aria-describedby={errors.password ? "password-error" : undefined}
                />
                {errors.password && (
                  <p id="password-error" className="text-xs text-destructive" role="alert">
                    {errors.password.message}
                  </p>
                )}
                {/* Forgot password — below the input, right-aligned (industry standard) */}
                <div className="flex justify-end">
                  <Link
                    to="/forgot-password"
                    className="text-xs text-muted-foreground underline underline-offset-4 hover:text-primary"
                  >
                    Forgot password?
                  </Link>
                </div>
              </div>

              {/* Server error */}
              {serverError && (
                <Alert variant="destructive" role="alert">
                  <AlertCircle className="h-4 w-4" />
                  <AlertDescription>{serverError}</AlertDescription>
                </Alert>
              )}

              {/* Submit */}
              <Button type="submit" className="w-full" disabled={isLoading}>
                {isLoading && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
                Sign in
              </Button>

            </form>

            {/* Registration link — inside card for visibility */}
            <div className="mt-4 pt-4 border-t border-border/50 text-center text-sm text-muted-foreground">
              New studio?{" "}
              <Link
                to="/register"
                className="underline underline-offset-4 hover:text-primary"
              >
                Register your studio
              </Link>
            </div>
          </CardContent>
        </Card>

      </div>
    </div>
  );
}
```

Run `pnpm --dir frontend tsc --noEmit` — zero errors.
Run `pnpm --dir frontend lint` — zero errors.

**Commit:** `refactor(auth): final LoginPage structure cleanup`

---

## 8. Update Tests

**File:** `frontend/src/features/auth/__tests__/LoginPage.test.tsx`

All existing tests must continue to pass — verify they do before adding new ones.

The following changes are needed because of Tasks 1–7:

### 8a. Update the "renders the sign-in form" test

The boilerplate description text is now gone. Add an assertion that it is NOT present:

```typescript
it("renders the sign-in form", () => {
  renderPage();
  expect(screen.getByRole("heading", { name: /sign in/i })).toBeInTheDocument();
  expect(screen.getByLabelText(/email/i)).toBeInTheDocument();
  expect(screen.getByLabelText("Password")).toBeInTheDocument();
  expect(screen.getByRole("button", { name: /sign in/i })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: /forgot password/i })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: /register your studio/i })).toBeInTheDocument();
  // Boilerplate CardDescription must be gone
  expect(
    screen.queryByText(/enter your credentials to access your account/i)
  ).not.toBeInTheDocument();
});
```

### 8b. Add new tests

Add these after the existing tests, inside the `describe("LoginPage")` block:

```typescript
it("does not render the generic credential subtitle", () => {
  renderPage();
  expect(
    screen.queryByText(/enter your credentials to access your account/i)
  ).not.toBeInTheDocument();
});

it("shows rate-limit message on 429 response", async () => {
  server.use(
    http.post("http://localhost/api/v1/auth/login", () =>
      HttpResponse.json({ message: "Rate limit exceeded." }, { status: 429 }),
    ),
  );

  const user = userEvent.setup();
  renderPage();

  await user.type(screen.getByLabelText(/email/i), "owner@test.com");
  await user.type(screen.getByLabelText("Password"), "wrongpass");
  await user.click(screen.getByRole("button", { name: /sign in/i }));

  expect(
    await screen.findByText(/too many sign-in attempts/i)
  ).toBeInTheDocument();
});

it("server error is rendered inside an Alert with role=alert", async () => {
  server.use(
    http.post("http://localhost/api/v1/auth/login", () =>
      HttpResponse.json({ message: "Invalid credentials." }, { status: 401 }),
    ),
  );

  const user = userEvent.setup();
  renderPage();

  await user.type(screen.getByLabelText(/email/i), "owner@test.com");
  await user.type(screen.getByLabelText("Password"), "wrongpass");
  await user.click(screen.getByRole("button", { name: /sign in/i }));

  const alertEl = await screen.findByRole("alert");
  expect(alertEl).toHaveTextContent("Invalid credentials.");
});

it("email field gets aria-invalid=true and aria-describedby on validation error", async () => {
  const user = userEvent.setup();
  renderPage();

  await user.click(screen.getByRole("button", { name: /sign in/i }));
  await screen.findByText(/email is required/i);

  const emailInput = screen.getByLabelText(/email/i);
  expect(emailInput).toHaveAttribute("aria-invalid", "true");
  expect(emailInput).toHaveAttribute("aria-describedby", "email-error");
});

it("password field gets aria-invalid=true and aria-describedby on validation error", async () => {
  const user = userEvent.setup();
  renderPage();

  await user.type(screen.getByLabelText(/email/i), "owner@test.com");
  await user.click(screen.getByRole("button", { name: /sign in/i }));
  await screen.findByText(/password is required/i);

  const passwordInput = screen.getByLabelText("Password");
  expect(passwordInput).toHaveAttribute("aria-invalid", "true");
  expect(passwordInput).toHaveAttribute("aria-describedby", "password-error");
});

it("field validation error paragraphs have role=alert for screen reader announcement", async () => {
  const user = userEvent.setup();
  renderPage();

  await user.click(screen.getByRole("button", { name: /sign in/i }));

  const errorEl = await screen.findByText(/email is required/i);
  expect(errorEl).toHaveAttribute("role", "alert");
});

it("registration link is present in the document", () => {
  // This link is now inside the card (not floating outside it), but the
  // assertion is structure-agnostic — we just verify it's there.
  renderPage();
  expect(
    screen.getByRole("link", { name: /register your studio/i })
  ).toBeInTheDocument();
});
```

Run `pnpm test` — all tests (existing + new) must pass.

**Commit:** `test(auth): add tests for rate limit, Alert, aria-describedby, no boilerplate text`

---

## 9. Final Verification

1. `pnpm --dir frontend tsc --noEmit` — zero TypeScript errors.
2. `pnpm --dir frontend lint` — zero errors.
3. `pnpm --dir frontend test` — all tests pass (existing + new).
4. Visual checks (start dev server):
   - Subtle zinc radial glow behind the card — visible on dark mode
   - "Sign in" heading renders without any subtitle below it
   - Email field + error paragraph correctly labeled and described
   - Password field label stands alone (no "Forgot password?" beside it)
   - "Forgot password?" link appears below the password input, right-aligned
   - On 401: destructive Alert with AlertCircle icon appears above the submit button
   - On 429: "Too many sign-in attempts. Please try again in a few minutes." in the same Alert
   - "New studio? Register your studio" appears inside the card, after the submit button, with a `border-t` separator
   - The area below the card is now clean — no orphaned text below it
   - Session expired amber banner still appears correctly with `?reason=session_expired`
   - Loading spinner on the button still appears during submission
5. `git log --oneline -10` — all commits present in order.

---

## Reference: Audit Issue → Task Map

| Audit Issue                                                    | Task |
|----------------------------------------------------------------|------|
| "Enter your credentials to access your account." filler copy   | 1    |
| Background is a featureless void                               | 2    |
| Server error is a bare `<p>` with no icon                      | 3    |
| No 429 rate-limit error state                                  | 3    |
| No `aria-describedby` linking inputs to their error messages   | 4    |
| "Forgot password?" on the label line (non-standard position)   | 5    |
| "New studio?" link orphaned outside the card                   | 6    |
| Field error `<p>` has no `role="alert"` (a11y)                 | 4    |
