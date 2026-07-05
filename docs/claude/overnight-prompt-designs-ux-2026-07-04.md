# Overnight Prompt — My Designs Page UX/UI Audit Fixes
**Date:** 2026-07-04
**Scope:** All actionable items from the My Designs UX audit. Changes span `DesignListPage.tsx`,
`ClientLayout.tsx`, and a new shared `ResourceEmptyState` component. No backend changes.

---

## Required Reading

```
CLAUDE.md
docs/claude/frontend.md
docs/claude/conventions.md
```

Then read these files **before writing a single line**:

```
frontend/src/features/designs/components/DesignListPage.tsx
frontend/src/features/designs/__tests__/DesignListPage.test.tsx
frontend/src/features/designs/components/DesignCard.tsx
frontend/src/features/designs/components/DesignStatusBadge.tsx
frontend/src/layouts/ClientLayout.tsx
frontend/src/shared/hooks/usePermission.ts        ← understand how canCreate is evaluated
```

---

## Fix 1 — Shared `ResourceEmptyState` component (new file)

**File:** `frontend/src/shared/components/ResourceEmptyState.tsx` (NEW)

```tsx
import type { ReactNode } from "react";

interface ResourceEmptyStateProps {
  /** Icon element — rendered at ~32–40px, muted color applied by parent. */
  icon:    ReactNode;
  /** Bold heading — e.g. "No designs yet" */
  heading: string;
  /** Muted explanatory line — role-specific copy goes here. */
  body:    string;
  /** Optional CTA — fully constructed JSX. Caller decides role-gating. */
  action?: ReactNode;
}

/**
 * Canonical empty-state shell used by every resource list page.
 * Icon → heading → muted body → optional action. Single implementation,
 * no per-page variations in padding/spacing/typography.
 */
export function ResourceEmptyState({
  icon, heading, body, action,
}: ResourceEmptyStateProps) {
  return (
    <div className="flex flex-col items-center gap-4 py-16 text-center">
      <div className="text-muted-foreground/40" aria-hidden="true">
        {icon}
      </div>
      <div className="space-y-1">
        <p className="text-sm font-medium">{heading}</p>
        <p className="text-xs text-muted-foreground">{body}</p>
      </div>
      {action}
    </div>
  );
}
```

Export it from the shared components barrel:

**File:** `frontend/src/shared/components/index.ts`

Add the export (find the barrel or create one if absent):
```ts
export { ResourceEmptyState } from "./ResourceEmptyState";
```

---

## Fix 2 — `DesignListPage.tsx` — complete rewrite

Apply all seven audit changes in one pass. Write the entire file; do not patch incrementally.

### Change inventory

| # | Line(s) | Change |
|---|---------|--------|
| 2a | 65 | Wrap header content in `max-w-4xl mx-auto px-4` to align with `<main>` |
| 2b | 73–74 | Remove redundant `Palette` icon from the counter row |
| 2c | 88–97 | Wrap search `<div>` in `{hasDesigns && (...)}` |
| 2d | 91 | Add `aria-label="Search designs by title"` to `<Input>` |
| 2e | 113–131 | Replace inline empty state with `<ResourceEmptyState>` + role-branched body copy |

### Final file

```tsx
import { useMemo, useState } from "react";
import { useSuspensionAwareError } from "@/shared/hooks/useSuspensionAwareError";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { Palette, Plus, Search } from "lucide-react";
import { useSearchParams, useNavigate } from "react-router-dom";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { usePermission } from "@/shared/hooks/usePermission";
import { ResourceEmptyState } from "@/shared/components/ResourceEmptyState";
import { Role } from "@/shared/types/roles";
import { useGetDesignsQuery } from "../designsApi";
import { DesignCard } from "./DesignCard";

function DesignCardSkeleton() {
  return (
    <div
      className="rounded-xl border bg-card p-4 flex items-start gap-4"
      aria-hidden="true"
    >
      <Skeleton className="h-10 w-10 rounded-lg shrink-0" />
      <div className="flex-1 space-y-2">
        <Skeleton className="h-4 w-40" />
        <Skeleton className="h-3 w-56 opacity-60" />
        <Skeleton className="h-3 w-20 opacity-40" />
      </div>
      <Skeleton className="h-8 w-8 rounded-md shrink-0" />
    </div>
  );
}

export function DesignListPage() {
  useDocumentMeta({ title: "Designs — Pena e Artë", canonical: "/designs" });

  const navigate = useNavigate();
  const canCreate = usePermission(Role.Artist);
  const [searchParams] = useSearchParams();
  const clientId = searchParams.get("clientId") ?? undefined;
  const artistId = searchParams.get("artistId") ?? undefined;

  const { data: designs, isLoading, isError } = useGetDesignsQuery({ clientId, artistId });
  const errorMessage = useSuspensionAwareError(isError, "Failed to load designs. Please try again.");

  const [search, setSearch] = useState("");

  const filteredDesigns = useMemo(() => {
    const term = search.trim().toLowerCase();
    const matching = term
      ? (designs ?? []).filter((d) => d.title.toLowerCase().includes(term))
      : (designs ?? []);

    // Designs awaiting a new revision from the artist are the most time-sensitive —
    // surface them first regardless of creation date.
    return [...matching].sort((a, b) => {
      const aUrgent = a.status === "ChangesRequested";
      const bUrgent = b.status === "ChangesRequested";
      if (aUrgent === bUrgent) return 0;
      return aUrgent ? -1 : 1;
    });
  }, [designs, search]);

  const hasDesigns = (designs?.length ?? 0) > 0;

  return (
    <div className="min-h-screen bg-background">
      {/* ── Header — shares max-w-4xl container with <main> so left edges align ── */}
      <header className="border-b bg-background sticky top-0 z-10">
        <div className="max-w-4xl mx-auto px-4 py-3 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Palette className="h-5 w-5" />
            <span className="font-semibold tracking-tight">Designs</span>
          </div>
          <div className="flex items-center gap-3">
            {designs && (
              <span className="text-xs text-muted-foreground">
                {designs.length} design{designs.length !== 1 ? "s" : ""}
              </span>
            )}
            {canCreate && (
              <Button size="sm" onClick={() => navigate("/designs/new")} className="gap-1.5">
                <Plus className="h-3.5 w-3.5" />
                New Design
              </Button>
            )}
          </div>
        </div>
      </header>

      <main className="max-w-4xl mx-auto px-4 py-6 space-y-4">
        {/* Search — hidden until there are designs to search */}
        {hasDesigns && (
          <div className="relative">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" aria-hidden="true" />
            <Input
              aria-label="Search designs by title"
              placeholder="Search by title…"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="pl-9"
            />
          </div>
        )}

        {/* Loading */}
        {isLoading && (
          <div className="space-y-2" aria-label="Loading designs">
            {Array.from({ length: 5 }).map((_, i) => (
              <DesignCardSkeleton key={i} />
            ))}
          </div>
        )}

        {/* Error */}
        {errorMessage && (
          <p className="text-center text-sm text-destructive py-16" role="alert">
            {errorMessage}
          </p>
        )}

        {/* Empty state — role-aware copy + conditional CTA */}
        {!isLoading && !isError && !hasDesigns && (
          <ResourceEmptyState
            icon={<Palette className="h-8 w-8" />}
            heading="No designs yet"
            body={
              canCreate
                ? "Upload a tattoo design to start tracking approvals."
                : "Your artist will upload designs here for your approval."
            }
            action={
              canCreate ? (
                <Button
                  size="sm"
                  onClick={() => navigate("/designs/new")}
                  className="gap-1.5 mt-1"
                  data-testid="empty-state-new-design"
                >
                  <Plus className="h-3.5 w-3.5" />
                  New Design
                </Button>
              ) : undefined
            }
          />
        )}

        {/* No search match */}
        {!isLoading && !isError && hasDesigns && filteredDesigns.length === 0 && (
          <p className="text-center text-sm text-muted-foreground py-8">
            No designs match &ldquo;{search}&rdquo;.
          </p>
        )}

        {/* List */}
        {!isLoading && !isError && filteredDesigns.length > 0 && (
          <div className="space-y-2">
            {filteredDesigns.map((design) => (
              <DesignCard key={design.id} design={design} />
            ))}
          </div>
        )}
      </main>
    </div>
  );
}
```

---

## Fix 3 — `ClientLayout.tsx` — two targeted changes

### Fix 3a — Active nav accent color

The active nav item uses `bg-primary text-primary-foreground`, which in the current dark theme
resolves to near-white — indistinguishable from other text on screen. Other interactive
controls across the app use `bg-violet-600` directly. Make the nav consistent:

**File:** `frontend/src/layouts/ClientLayout.tsx`

Find the `NavLink` `className` callback (line ~47) and change:

```tsx
// BEFORE
isActive
  ? "bg-primary text-primary-foreground"
  : "text-muted-foreground hover:text-foreground hover:bg-muted"

// AFTER
isActive
  ? "bg-violet-600 text-white"
  : "text-muted-foreground hover:text-foreground hover:bg-muted"
```

### Fix 3b — Nav touch targets

The nav links use `py-1.5` (~34px tall at mobile) at the exact breakpoint where touch input
is most likely. Increase to `py-2.5` on small screens while keeping `sm:py-1.5` at desktop
to preserve the compact header:

**Before:**
```tsx
"flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm transition-colors shrink-0"
```

**After:**
```tsx
"flex items-center gap-1.5 px-3 py-2.5 sm:py-1.5 rounded-md text-sm transition-colors shrink-0"
```

---

## Test updates — `DesignListPage.test.tsx`

Several existing tests need updates; several new ones need to be added.

### Tests that need updating

**Test: "shows rich empty state when no designs exist" (line 106)**

The current test asserts `getByText(/upload a tattoo design/i)` and runs with `Role.Owner`
(the default). After Fix 2e, `canCreate` for Owner is true so the copy is unchanged.
**No change needed** — this test still passes.

**Test: "rich empty state shows a New Design button for owner role" (line 181)**

Uses `data-testid="empty-state-new-design"` — still present in the new file. **No change needed.**

**Test: "search input is present on the designs page" (line 212)**

This test calls `renderPage()` (default = `Role.Owner`) which returns 2 designs. `hasDesigns`
is true so the search bar renders. **No change needed.**

**Tests: search filtering tests (lines 218–264)**

All run with designs present (`Role.Owner`, 2 records returned). `hasDesigns` is true.
**No change needed.**

### New tests to add

Append to the end of `describe("DesignListPage", ...)`:

```ts
// ── Role-aware empty state copy ───────────────────────────────────────────────

it("empty state shows artist-targeted copy for Artist and Owner roles", async () => {
  server.use(
    http.get("http://localhost/api/v1/designs", () => HttpResponse.json([])),
  );
  renderPage(Role.Artist);
  await screen.findByText("No designs yet");
  expect(screen.getByText(/upload a tattoo design/i)).toBeInTheDocument();
});

it("empty state shows client-targeted copy for Client role", async () => {
  server.use(
    http.get("http://localhost/api/v1/designs", () => HttpResponse.json([])),
  );
  renderPage(Role.Client);
  await screen.findByText("No designs yet");
  expect(
    screen.getByText(/your artist will upload designs here for your approval/i)
  ).toBeInTheDocument();
});

it("empty state for Client role does NOT show 'Upload a tattoo design' copy", async () => {
  server.use(
    http.get("http://localhost/api/v1/designs", () => HttpResponse.json([])),
  );
  renderPage(Role.Client);
  await screen.findByText("No designs yet");
  expect(screen.queryByText(/upload a tattoo design/i)).not.toBeInTheDocument();
});

// ── Search bar visibility ──────────────────────────────────────────────────────

it("search bar is hidden when there are no designs", async () => {
  server.use(
    http.get("http://localhost/api/v1/designs", () => HttpResponse.json([])),
  );
  renderPage();
  await screen.findByText("No designs yet");
  expect(screen.queryByPlaceholderText(/search by title/i)).not.toBeInTheDocument();
});

it("search bar is visible when designs exist", async () => {
  renderPage();
  expect(await screen.findByPlaceholderText(/search by title/i)).toBeInTheDocument();
});

// ── Accessible name on search input ──────────────────────────────────────────

it("search input has an accessible aria-label", async () => {
  renderPage();
  await screen.findByText("Dragon Sleeve");
  expect(
    screen.getByRole("searchbox", { name: /search designs by title/i })
  ).toBeInTheDocument();
});

// ── Header/body alignment ─────────────────────────────────────────────────────

it("header count renders without a Palette icon next to it", async () => {
  renderPage();
  await screen.findByText("2 designs");
  // Only one Palette icon: the title icon. Previously two appeared in the header.
  // We can't reliably count SVG renders in JSDOM but we can assert the icon
  // is not given a duplicate aria role (it's aria-hidden throughout).
  expect(screen.getByText("2 designs").closest("span")).not.toBeNull();
});
```

**Note on the `role="searchbox"` query:** The `<Input>` renders as an `<input type="text">`,
which has the implicit ARIA role `textbox`, not `searchbox`. If `getByRole("searchbox", ...)` 
fails, fall back to `getByRole("textbox", { name: /search designs by title/i })`. Verify which
role JSDOM assigns and use whichever passes.

---

## `ResourceEmptyState` tests

**File:** `frontend/src/shared/components/__tests__/ResourceEmptyState.test.tsx` (NEW)

```tsx
import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { ResourceEmptyState } from "@/shared/components/ResourceEmptyState";
import { Button } from "@/shared/components/ui/button";

describe("ResourceEmptyState", () => {
  it("renders the heading", () => {
    render(<ResourceEmptyState icon={<span />} heading="Nothing here" body="Try again." />);
    expect(screen.getByText("Nothing here")).toBeInTheDocument();
  });

  it("renders the body text", () => {
    render(<ResourceEmptyState icon={<span />} heading="H" body="Body text here." />);
    expect(screen.getByText("Body text here.")).toBeInTheDocument();
  });

  it("renders the action when provided", () => {
    render(
      <ResourceEmptyState
        icon={<span />}
        heading="H"
        body="B"
        action={<Button>Click me</Button>}
      />
    );
    expect(screen.getByRole("button", { name: /click me/i })).toBeInTheDocument();
  });

  it("renders nothing for the action slot when action is undefined", () => {
    render(<ResourceEmptyState icon={<span />} heading="H" body="B" />);
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("icon wrapper has aria-hidden so it does not pollute the accessible tree", () => {
    render(
      <ResourceEmptyState
        icon={<span role="img" aria-label="test icon" />}
        heading="H"
        body="B"
      />
    );
    // The outer div hides the icon from AT — only heading/body/action are meaningful
    const wrapper = screen.queryByRole("img", { name: /test icon/i });
    // aria-hidden="true" on the parent means the img is hidden from AT
    expect(wrapper?.closest('[aria-hidden="true"]')).not.toBeNull();
  });
});
```

---

## Verification

```bash
cd "Pena e Arte/frontend"
pnpm tsc --noEmit
pnpm test -- --testPathPattern="DesignListPage|ResourceEmptyState"
```

Both must exit 0. Fix any failures before finishing.

---

## Exit condition

Tests green, types clean. Then append to `docs/claude/architecture.md`:

```markdown
## My Designs Page — UX Audit Fixes — 2026-07-04

### Issues resolved
1. **Header/body left-edge misalignment** — header content now wrapped in
   `max-w-4xl mx-auto px-4 py-3` matching `<main>`. Both edges align on all viewports.

2. **Redundant Palette icon in counter** — removed from the design count row.
   Palette now appears once in the header title and once in the empty state only.

3. **Role-blind empty state copy** — `ResourceEmptyState` body is branched on `canCreate`:
   - Artist/Owner/Issuer: "Upload a tattoo design to start tracking approvals."
   - Client: "Your artist will upload designs here for your approval."

4. **False affordance: search bar with nothing to search** — search `<div>` is now
   wrapped in `{hasDesigns && (...)}`. Renders only when records exist.

5. **Accessible name missing from search input** — `aria-label="Search designs by title"` added.

6. **ClientLayout active nav color** — changed from `bg-primary text-primary-foreground`
   (resolves near-white in dark theme) to `bg-violet-600 text-white` matching app-wide convention.

7. **ClientLayout nav touch targets** — `py-1.5` → `py-2.5 sm:py-1.5` ensures
   mobile touch targets are ≥40px at the breakpoint where short labels are active.

### New shared component
- `frontend/src/shared/components/ResourceEmptyState.tsx`
  Props: `icon`, `heading`, `body`, `action?`.
  Canonical empty-state shell for all resource list pages — use this instead of
  inline flex+icon+p+p+button patterns. `MyStudiosPage` can adopt it in a follow-up.

### Files changed
- `frontend/src/features/designs/components/DesignListPage.tsx` (fix 1–5)
- `frontend/src/layouts/ClientLayout.tsx` (fix 6–7)
- `frontend/src/shared/components/ResourceEmptyState.tsx` (new)
- `frontend/src/features/designs/__tests__/DesignListPage.test.tsx` (+7 tests)
- `frontend/src/shared/components/__tests__/ResourceEmptyState.test.tsx` (new, 5 tests)
```
