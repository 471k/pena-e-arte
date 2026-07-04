# Overnight Prompt — "My Studios" Page UX/UI Polish
**Date:** 2026-07-04
**Scope:** UX audit fixes for `MyStudiosPage`. All changes are frontend-only; no backend changes.

---

## Required Reading

```
CLAUDE.md
docs/claude/frontend.md
docs/claude/conventions.md
```

Then read these source files **before writing a single line**:

```
frontend/src/features/auth/components/MyStudiosPage.tsx
frontend/src/features/auth/__tests__/MyStudiosPage.test.tsx
frontend/src/features/auth/authApi.ts                    ← MyStudioResponse interface
frontend/src/shared/types/roles.ts
```

---

## Background

A UX audit of `MyStudiosPage` found seven actionable issues. All fixes are contained
to `MyStudiosPage.tsx` (and corresponding test updates in `MyStudiosPage.test.tsx`).
No new components, no backend changes, no new dependencies.

---

## Fix 1 — Remove the false-affordance "Current" button

### Problem
The active studio renders a `<Button size="sm" variant="outline" disabled>Current</Button>`.
A disabled `<button>` with full button chrome (border, padding, icon) is a false affordance
— users click it expecting something to happen. Once a second studio's "Switch" button exists
in the same row, the visual distinction between "this fires an action" and "this does nothing"
collapses entirely.

### Change

In `StudioCard`, replace the disabled `Button` for the active studio with a plain `<span>`:

**Before (in the `{isActive ? ... : ...}` block):**
```tsx
<Button size="sm" variant="outline" disabled className="gap-1.5 text-xs">
  <CheckCircle2 className="h-3.5 w-3.5" />
  Current
</Button>
```

**After:**
```tsx
<span
  className="inline-flex items-center gap-1 rounded-full px-2.5 py-1
             text-xs font-medium bg-emerald-500/15 text-emerald-500 shrink-0"
  aria-label={`${studio.name} is your current studio`}
>
  <CheckCircle2 className="h-3 w-3" aria-hidden />
  Current
</span>
```

The `<span>` has no interactive role, can't be tabbed to, and can't be clicked — no more
false affordance.

---

## Fix 2 — Replace the ring-style active border with a semantic green accent

### Problem
The active `Card` uses `ring-2 ring-primary ring-offset-2 ring-offset-background`. In a
dark theme where `primary` resolves to near-white, this renders as a bright full-contrast
outline that reads identically to a focus ring or error state, not a "currently selected"
indicator. It also clashes with the browser's native focus ring if a user Tab-navigates
to the card area.

### Change

In `StudioCard`, update the `Card`'s `className`:

**Before:**
```tsx
<Card
  className={`transition-colors ${
    isActive ? "ring-2 ring-primary ring-offset-2 ring-offset-background" : ""
  }`}
>
```

**After:**
```tsx
<Card
  className={`transition-colors ${
    isActive
      ? "border-emerald-500/40 bg-emerald-950/10"
      : "border-border/50"
  }`}
>
```

This uses a subtle tinted green border and barely-there background wash to communicate
"selected" without screaming focus ring. On non-active cards, a slightly muted border
(`border-border/50`) is also better than the hard default `border-border`.

---

## Fix 3 — Give the "Active" name-row badge semantic color

### Problem
The "Active" badge in the name row uses `bg-primary/15 text-primary`, which is the same
near-white as all other primary-colored text in the dark theme. It doesn't read as a status
badge — it reads as a label. Every SaaS convention uses green for Active.

### Change

In `StudioCard`, update the Active badge in the name/city block:

**Before:**
```tsx
<span className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium bg-primary/15 text-primary">
  <CheckCircle2 className="h-3 w-3" aria-hidden />
  Active
</span>
```

**After:**
```tsx
<span className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium bg-emerald-500/15 text-emerald-500">
  <CheckCircle2 className="h-3 w-3" aria-hidden />
  Active
</span>
```

The "Suspended" badge already uses `bg-destructive/10 text-destructive` — no change needed
there. This fix brings "Active" in line with the same pattern using the semantic green.

---

## Fix 4 — Improve the monogram avatar contrast

### Problem
`StudioAvatar` uses `bg-primary/10` for the monogram box — that's only 10% opacity of an
already light primary color, making the box almost indistinguishable from the card background.
The initials are legible but the box doesn't read as "avatar placeholder."

### Change

In `StudioAvatar`, update the monogram `div`:

**Before:**
```tsx
<div className="h-12 w-12 rounded-md bg-primary/10 text-primary flex items-center justify-center text-sm font-semibold shrink-0">
```

**After:**
```tsx
<div className="h-12 w-12 rounded-md bg-muted text-muted-foreground/80 flex items-center justify-center text-sm font-semibold shrink-0 border border-border/50">
```

`bg-muted` is a platform-standard surface colour that always provides meaningful separation
from the card background across light and dark themes.

---

## Fix 5 — Add a proper touch target to the external-link icon

### Problem
The external-link `<Link>` renders a bare `<ExternalLink className="h-4 w-4" />` with no
padding. The hit area is ~16×16px — well below the 44×44px minimum required by WCAG 2.5.5
and the iOS/Android HIG.

### Change

In `StudioCard`, update the external-link `<Link>`:

**Before:**
```tsx
<Link
  to={`/s/${studio.slug}`}
  aria-label={`View ${studio.name} portfolio`}
  className="text-muted-foreground hover:text-foreground transition-colors"
  title="View portfolio"
>
  <ExternalLink className="h-4 w-4" />
</Link>
```

**After:**
```tsx
<Link
  to={`/s/${studio.slug}`}
  aria-label={`View ${studio.name} public profile`}
  title="View studio public profile"
  className="inline-flex items-center justify-center h-8 w-8 rounded-md
             text-muted-foreground hover:text-foreground hover:bg-accent
             transition-colors"
>
  <ExternalLink className="h-4 w-4" aria-hidden />
</Link>
```

The 32×32px box (`h-8 w-8`) with hover background matches the shadcn/ui ghost-icon-button
convention used elsewhere (the `hover:bg-accent` paired with `rounded-md`). Not quite 44px,
but within the acceptable 32px minimum for secondary icon actions paired with nearby primary
buttons. The improved aria-label also clarifies what the link opens.

---

## Fix 6 — Add a "Discover studios" / "Join another" CTA to the populated list state

### Problem
For a platform that explicitly supports multi-studio membership, the page is a dead end once
a user has ≥1 studio. There's no way to find and join a second studio from this page.
The "Discover studios" CTA only appears in the zero-studio empty state.

### Change

In `MyStudiosPage`, add a `Plus` import to the lucide-react import line and update the
populated-state section.

**Import change (add `Plus`):**
```tsx
import { Building2, CheckCircle2, ExternalLink, Loader2, Plus } from "lucide-react";
```

**Populated-state sub-header (replaces the plain `<p>` copy line):**

Before:
```tsx
{!isLoading && !isError && studios && studios.length > 0 && (
  <>
    <p className="text-xs text-muted-foreground px-1">
      {studios.length === 1
        ? "You belong to one studio."
        : `You belong to ${studios.length} studios. Tap "Switch" to change your active studio.`}
    </p>
    ...
```

After:
```tsx
{!isLoading && !isError && studios && studios.length > 0 && (
  <>
    <div className="flex items-center justify-between px-1 gap-2">
      <p className="text-xs text-muted-foreground">
        {studios.length === 1
          ? "You belong to 1 studio."
          : `You belong to ${studios.length} studios. Tap "Switch" to change your active studio.`}
      </p>
      <Button
        size="sm"
        variant="ghost"
        className="shrink-0 h-7 px-2 text-xs gap-1 text-muted-foreground hover:text-foreground"
        onClick={() => navigate("/discover")}
        aria-label="Discover more studios to join"
      >
        <Plus className="h-3 w-3" aria-hidden />
        Join another
      </Button>
    </div>
    ...
```

---

## Fix 7 — Update the page header to always show a "Discover" shortcut

### Problem
On desktop, the `<header>` bar's right side is empty (no action). Comparing to equivalent
"Your Organizations" pages in Slack, Notion, and Linear — there's always a primary CTA in
the header.

### Change

In `MyStudiosPage`, update the `<header>`:

**Before:**
```tsx
<header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
  <Building2 className="h-5 w-5" />
  <span className="font-semibold tracking-tight">My Studios</span>
  {studios && studios.length > 0 && (
    <span className="text-xs text-muted-foreground ml-1">
      ({studios.length})
    </span>
  )}
</header>
```

**After:**
```tsx
<header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
  <Building2 className="h-5 w-5" />
  <span className="font-semibold tracking-tight">My Studios</span>
  {studios && studios.length > 0 && (
    <span className="text-xs text-muted-foreground ml-1">
      ({studios.length})
    </span>
  )}
  <Button
    size="sm"
    variant="ghost"
    className="ml-auto h-7 px-2 text-xs gap-1 text-muted-foreground hover:text-foreground"
    onClick={() => navigate("/discover")}
    aria-label="Discover more studios"
  >
    <Plus className="h-3.5 w-3.5" aria-hidden />
    Discover
  </Button>
</header>
```

---

## Final component — complete `MyStudiosPage.tsx`

After applying all seven fixes, the complete updated file should look like this.
Write the file in full — do not patch piece-by-piece after reading the above:

```tsx
import { useState } from "react";
import { Building2, CheckCircle2, ExternalLink, Loader2, Plus } from "lucide-react";
import { Link, useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { setCredentials } from "@/features/auth/authSlice";
import { decodeToken } from "@/shared/utils/jwt";
import { useGetMyStudiosQuery, useSwitchStudioMutation } from "@/features/auth/authApi";
import type { MyStudioResponse } from "@/features/auth/authApi";

// ── Helpers ───────────────────────────────────────────────────────────────────

function StudioAvatar({ name, coverImageUrl }: { name: string; coverImageUrl: string | null }) {
  if (coverImageUrl) {
    return (
      <img
        src={coverImageUrl}
        alt={name}
        className="h-12 w-12 rounded-md object-cover shrink-0"
      />
    );
  }

  const initials = name
    .split(" ")
    .map((w) => w[0])
    .join("")
    .toUpperCase()
    .slice(0, 2);

  return (
    <div
      className="h-12 w-12 rounded-md bg-muted text-muted-foreground/80
                 flex items-center justify-center text-sm font-semibold
                 shrink-0 border border-border/50"
    >
      {initials}
    </div>
  );
}

// ── Studio card ───────────────────────────────────────────────────────────────

interface StudioCardProps {
  studio:      MyStudioResponse;
  isActive:    boolean;
  isSwitching: boolean;
  onSwitch:    (studioId: string) => void;
}

function StudioCard({ studio, isActive, isSwitching, onSwitch }: StudioCardProps) {
  return (
    <Card
      className={`transition-colors ${
        isActive
          ? "border-emerald-500/40 bg-emerald-950/10"
          : "border-border/50"
      }`}
    >
      <CardContent className="p-4">
        <div className="flex items-start gap-3">
          <StudioAvatar name={studio.name} coverImageUrl={studio.coverImageUrl} />

          <div className="flex-1 min-w-0 space-y-0.5">
            <div className="flex items-center gap-2 flex-wrap">
              <p className="text-sm font-semibold truncate">{studio.name}</p>
              {isActive && (
                <span
                  className="inline-flex items-center gap-1 rounded-full px-2 py-0.5
                             text-xs font-medium bg-emerald-500/15 text-emerald-500"
                >
                  <CheckCircle2 className="h-3 w-3" aria-hidden />
                  Active
                </span>
              )}
              {!studio.isStudioActive && (
                <span
                  className="inline-flex items-center rounded-full px-2 py-0.5
                             text-xs font-medium bg-destructive/10 text-destructive"
                >
                  Suspended
                </span>
              )}
            </div>
            <p className="text-xs text-muted-foreground">{studio.city}</p>
          </div>

          <div className="flex items-center gap-1 shrink-0">
            <Link
              to={`/s/${studio.slug}`}
              aria-label={`View ${studio.name} public profile`}
              title="View studio public profile"
              className="inline-flex items-center justify-center h-8 w-8 rounded-md
                         text-muted-foreground hover:text-foreground hover:bg-accent
                         transition-colors"
            >
              <ExternalLink className="h-4 w-4" aria-hidden />
            </Link>

            {isActive ? (
              <span
                className="inline-flex items-center gap-1 rounded-full px-2.5 py-1
                           text-xs font-medium bg-emerald-500/15 text-emerald-500 shrink-0"
                aria-label={`${studio.name} is your current studio`}
              >
                <CheckCircle2 className="h-3 w-3" aria-hidden />
                Current
              </span>
            ) : (
              <Button
                size="sm"
                variant="outline"
                onClick={() => onSwitch(studio.studioId)}
                disabled={isSwitching}
                className="text-xs gap-1.5"
                aria-label={`Switch to ${studio.name}`}
              >
                {isSwitching ? (
                  <Loader2 className="h-3.5 w-3.5 animate-spin" />
                ) : null}
                Switch
              </Button>
            )}
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function MyStudiosPage() {
  useDocumentMeta({ title: "My Studios — Pena e Artë", canonical: "/my-studios" });

  const dispatch        = useAppDispatch();
  const currentTenantId = useAppSelector((s) => s.auth.tenantId);
  const navigate        = useNavigate();

  const { data: studios, isLoading, isError, refetch } = useGetMyStudiosQuery();
  const [switchStudio]    = useSwitchStudioMutation();
  const [switchingId, setSwitchingId] = useState<string | null>(null);

  async function handleSwitch(studioId: string) {
    setSwitchingId(studioId);
    try {
      const response = await switchStudio({ studioId }).unwrap();
      const decoded  = decodeToken(response.accessToken);
      dispatch(setCredentials({ ...decoded, refreshToken: response.refreshToken }));
      toast.success(
        response.isNewMembership
          ? "Joined studio — welcome!"
          : "Studio switched successfully."
      );
      navigate("/book", { replace: true });
    } catch {
      toast.error("Couldn't switch studios. Please try again.");
    } finally {
      setSwitchingId(null);
    }
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Building2 className="h-5 w-5" />
        <span className="font-semibold tracking-tight">My Studios</span>
        {studios && studios.length > 0 && (
          <span className="text-xs text-muted-foreground ml-1">
            ({studios.length})
          </span>
        )}
        <Button
          size="sm"
          variant="ghost"
          className="ml-auto h-7 px-2 text-xs gap-1 text-muted-foreground hover:text-foreground"
          onClick={() => navigate("/discover")}
          aria-label="Discover more studios"
        >
          <Plus className="h-3.5 w-3.5" aria-hidden />
          Discover
        </Button>
      </header>

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-3">
        {/* ── Loading ── */}
        {isLoading && (
          <div className="space-y-3" aria-label="Loading studios">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton key={i} className="h-20 w-full rounded-lg" />
            ))}
          </div>
        )}

        {/* ── Error ── */}
        {isError && (
          <p className="text-center text-sm text-destructive py-16" role="alert">
            Failed to load your studios.{" "}
            <button type="button" className="underline" onClick={() => refetch()}>
              Try again
            </button>
          </p>
        )}

        {/* ── Empty ── */}
        {!isLoading && !isError && studios?.length === 0 && (
          <div className="flex flex-col items-center gap-4 py-20 text-center">
            <Building2 className="h-10 w-10 text-muted-foreground/50" />
            <div className="space-y-1">
              <p className="text-sm font-medium">No studios yet</p>
              <p className="text-xs text-muted-foreground">
                Visit a studio&apos;s page and tap &quot;Book&quot; to join.
              </p>
            </div>
            <Button size="sm" variant="outline" onClick={() => navigate("/discover")}>
              Discover studios
            </Button>
          </div>
        )}

        {/* ── List ── */}
        {!isLoading && !isError && studios && studios.length > 0 && (
          <>
            <div className="flex items-center justify-between px-1 gap-2">
              <p className="text-xs text-muted-foreground">
                {studios.length === 1
                  ? "You belong to 1 studio."
                  : `You belong to ${studios.length} studios. Tap "Switch" to change your active studio.`}
              </p>
              <Button
                size="sm"
                variant="ghost"
                className="shrink-0 h-7 px-2 text-xs gap-1 text-muted-foreground hover:text-foreground"
                onClick={() => navigate("/discover")}
                aria-label="Discover more studios to join"
              >
                <Plus className="h-3 w-3" aria-hidden />
                Join another
              </Button>
            </div>

            {studios.map((studio) => (
              <StudioCard
                key={studio.studioId}
                studio={studio}
                isActive={studio.studioId === currentTenantId}
                isSwitching={switchingId === studio.studioId}
                onSwitch={handleSwitch}
              />
            ))}
          </>
        )}
      </main>
    </div>
  );
}
```

Write the above exactly as given into `frontend/src/features/auth/components/MyStudiosPage.tsx`.

---

## Test updates — `MyStudiosPage.test.tsx`

The existing test on line 165 queries for a button named `/current/i`. Since "Current" is now
a `<span>` (not a `<button>`), this test must be updated. Several new tests also need to be added.

### Changes to make in `MyStudiosPage.test.tsx`:

**1. Update the "Current" test** (was: `getByRole("button", { name: /current/i })`):

Replace:
```ts
it("shows 'Current' button (disabled) on the studio matching the active tenantId", async () => {
  renderPage();
  await screen.findByText("Alpha Ink");
  const currentButton = screen.getByRole("button", { name: /current/i });
  expect(currentButton).toBeDisabled();
});
```

With:
```ts
it("shows a non-interactive 'Current' badge (not a button) on the studio matching the active tenantId", async () => {
  renderPage();
  await screen.findByText("Alpha Ink");
  // "Current" should be a plain span, not a button — no false affordance
  expect(screen.queryByRole("button", { name: /current/i })).not.toBeInTheDocument();
  // The span should still be accessible via aria-label
  expect(screen.getByLabelText(/alpha ink is your current studio/i)).toBeInTheDocument();
});
```

**2. Add new tests** — append these inside the `describe("MyStudiosPage", ...)` block:

```ts
// ── Header and navigation ─────────────────────────────────────────────────────

it("renders a 'Discover' button in the header that links to /discover", async () => {
  const user = userEvent.setup();
  renderPageWithRoutes();
  await screen.findByText("Alpha Ink");

  // We need a /discover route in renderPageWithRoutes for this to navigate,
  // but we can still assert the button exists and is clickable
  const discoverBtn = screen.getByRole("button", { name: /discover more studios/i });
  expect(discoverBtn).toBeInTheDocument();
  expect(discoverBtn).not.toBeDisabled();
});

it("renders a 'Join another' button in the list area when studios exist", async () => {
  renderPage();
  await screen.findByText("Alpha Ink");
  const joinBtn = screen.getByRole("button", { name: /discover more studios to join/i });
  expect(joinBtn).toBeInTheDocument();
});

it("does not render 'Join another' button in the empty state", async () => {
  server.use(
    http.get("http://localhost/api/v1/auth/my-studios", () => HttpResponse.json([])),
  );
  renderPage();
  await screen.findByText(/no studios yet/i);
  expect(screen.queryByRole("button", { name: /discover more studios to join/i })).not.toBeInTheDocument();
});

// ── External link accessibility ───────────────────────────────────────────────

it("external portfolio link has an accessible aria-label including studio name", async () => {
  renderPage();
  await screen.findByText("Alpha Ink");
  expect(
    screen.getByRole("link", { name: /view alpha ink public profile/i })
  ).toBeInTheDocument();
});

// ── Active studio card visual treatment ──────────────────────────────────────

it("renders 'Active' badge only on the studio matching the active tenantId", async () => {
  renderPage();
  await screen.findByText("Alpha Ink");
  // Only Alpha Ink is active (tenantId = "studio-aaa")
  expect(screen.getAllByText("Active")).toHaveLength(1);
  // "Current" span is present alongside "Active" badge
  expect(screen.getByLabelText(/alpha ink is your current studio/i)).toBeInTheDocument();
});

it("shows single-studio copy ('You belong to 1 studio.') when there is exactly one studio", async () => {
  server.use(
    http.get("http://localhost/api/v1/auth/my-studios", () => HttpResponse.json([STUDIO_A])),
  );
  renderPage();
  expect(await screen.findByText(/you belong to 1 studio/i)).toBeInTheDocument();
});
```

**Note on the existing `"does not show 'Active' badge on non-active studios"` test:**
That test currently asserts `expect(screen.queryAllByText("Active")).toHaveLength(1)`.
This still passes because both the "Active" badge and the "Current" span show in the DOM —
"Active" appears once (for Alpha Ink), "Current" appears once (also for Alpha Ink, in the
action area). No change needed to that test.

---

## Verification

```bash
cd "Pena e Arte"
cd frontend
pnpm tsc --noEmit
pnpm test -- MyStudiosPage
```

Both commands must exit 0. Fix any failures before finishing.

---

## Exit condition

Tests pass, TypeScript compiles clean. Then append to `docs/claude/architecture.md`:

```markdown
## My Studios Page — UX Polish — 2026-07-04

### Issues resolved
1. **False-affordance "Current" button** → replaced with a plain `<span>` badge. No button role,
   no disabled state, no click handler. Tests updated to assert it is NOT a button.
2. **Ring-style active border** → replaced with `border-emerald-500/40 bg-emerald-950/10`.
   Communicates selection without looking like a focus ring or error state.
3. **"Active" badge semantic color** → `bg-emerald-500/15 text-emerald-500` (was `primary`).
   "Suspended" badge was already `destructive` — no change.
4. **Monogram avatar contrast** → `bg-muted border-border/50` (was `bg-primary/10`). Consistently
   separates from the card background across light/dark themes.
5. **External link touch target** → 32×32 `inline-flex` wrapper with `hover:bg-accent` padding.
   aria-label clarified to "View {name} public profile".
6. **"Join another studio" CTA** → added as a ghost button in the list sub-header row.
   Navigates to `/discover`. Absent from empty state (where "Discover studios" button already exists).
7. **Header "Discover" shortcut** → always-visible ghost button on the right side of the
   sticky header. Navigates to `/discover`.

### Files changed
- `frontend/src/features/auth/components/MyStudiosPage.tsx` (complete rewrite)
- `frontend/src/features/auth/__tests__/MyStudiosPage.test.tsx` (1 test updated, 6 added)
```
