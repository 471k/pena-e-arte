# Overnight Prompt — Designs List Overhaul
> Date: 2026-06-19
> Target files: `DesignListPage.tsx`, `DesignCard.tsx`, `DesignListPage.test.tsx`
> No new npm or NuGet packages. No new backend changes.

---

## Pre-flight

1. Read `CLAUDE.md` and `docs/claude/frontend.md` before making any changes.
2. Run `pnpm tsc --noEmit` — note any pre-existing errors; do not count them as regressions.
3. Run `pnpm test src/features/designs/DesignListPage` — confirm all 13 existing tests pass first.

---

## Context

**Files to change:**
- `frontend/src/features/designs/components/DesignListPage.tsx` (75 lines)
- `frontend/src/features/designs/components/DesignCard.tsx` (59 lines)
- `frontend/src/features/designs/__tests__/DesignListPage.test.tsx` (175 lines, 13 tests)

**Key data shapes (read-only — no backend changes):**

```ts
// design.types.ts
interface DesignResponse {
  id, studioId, clientId, artistId,
  title: string;
  description: string | null;
  createdAt: string;
  // NO status field — status badges are out of scope
  // NO client/artist names — only IDs; attribution lines are out of scope
}

// GetDesignsParams only supports clientId and artistId filters
// There is NO title/search param on the backend endpoint
// NO deleteDesign mutation exists in designsApi
```

**What the audit identified that is in scope (no backend required):**

| Issue | Fix |
|---|---|
| Narrow `max-w-2xl` column — half the screen wasted | Widen to `max-w-4xl` to match Artists/Clients |
| Flat skeleton bars don't match card shape | Replace with `DesignCardSkeleton` component |
| Plain text empty state ("No designs in this studio yet.") | Rich branded empty state with icon + CTA |
| No search/filter — list becomes unscannnable at 20+ items | Client-side title search (no API change needed) |
| Click affordance on card unclear | Add `ChevronRight` hint inside the link area of `DesignCard` |

**What is explicitly out of scope for this prompt:**
- Status badges — `DesignResponse` has no `status` field
- Client/artist name attribution — only IDs in `DesignResponse`; names would require extra API calls
- Delete design — no `deleteDesign` mutation in `designsApi`
- Thumbnail images — thumbnails come from revisions (separate API), not from the list endpoint
- Sortable columns, status filter tabs — no supporting data in the response

**Already resolved (do not re-fix):**
- Double bell icon — fixed in a prior session by removing the Notifications nav item from `OwnerLayout.tsx`
- Upload button icon — already uses `Upload` icon with `aria-label="Upload revision"`. The audit's complaint about a "share" icon is stale.
- Click-to-navigate — the card already wraps content in `<Link to="/designs/:id">` and has `hover:bg-muted/40`

---

## Part 1 — DesignListPage.tsx

### 1a — New imports

Add at the top:

```tsx
import { useMemo, useState } from "react";
import { Input } from "@/shared/components/ui/input";
```

Extend the existing lucide import to include `Search`:

```tsx
import { Palette, Plus, Search } from "lucide-react";
```

### 1b — DesignCardSkeleton component

Replace the inline skeleton blocks with a named component that mirrors the actual card layout
(icon square + title line + description line + date line + action button):

```tsx
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
```

### 1c — Client-side title search

Add to the component body, before the `return`:

```tsx
const [search, setSearch] = useState("");

const filteredDesigns = useMemo(() => {
  const term = search.trim().toLowerCase();
  if (!term) return designs ?? [];
  return (designs ?? []).filter((d) =>
    d.title.toLowerCase().includes(term),
  );
}, [designs, search]);

const hasDesigns = (designs?.length ?? 0) > 0;
```

> **No debounce needed** — the filter runs entirely in-memory with no API call, so `useMemo`
> is sufficient. This matches the intent of "No useEffect for data fetching" — there is no
> fetch here.

### 1d — Widen the container and add search bar

Change `max-w-2xl` to `max-w-4xl` on the `<main>` element.

Immediately after the opening `<main ...>` tag, add the search input (same pattern as Artists
and Clients pages):

```tsx
<div className="relative">
  <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
  <Input
    placeholder="Search by title…"
    value={search}
    onChange={(e) => setSearch(e.target.value)}
    className="pl-9"
  />
</div>
```

### 1e — Replace skeleton, empty state, and card list

Replace the existing `{isLoading && ...}`, `{!isLoading && !isError && designs?.length === 0 ...}`,
and `{!isLoading && !isError && designs && designs.length > 0 ...}` blocks with:

```tsx
{/* Loading skeleton */}
{isLoading && (
  <div className="space-y-2" aria-label="Loading designs">
    {Array.from({ length: 5 }).map((_, i) => (
      <DesignCardSkeleton key={i} />
    ))}
  </div>
)}

{/* Error */}
{isError && (
  <p className="text-center text-sm text-destructive py-16">
    Failed to load designs. Please try again.
  </p>
)}

{/* Rich empty state — no designs in studio at all */}
{!isLoading && !isError && !hasDesigns && (
  <div className="flex flex-col items-center gap-3 py-16 text-center">
    <Palette className="h-8 w-8 text-muted-foreground/40" />
    <p className="text-sm font-medium">No designs yet</p>
    <p className="text-xs text-muted-foreground">
      Upload a tattoo design to start tracking approvals.
    </p>
    {canCreate && (
      <Button
        size="sm"
        onClick={() => navigate("/designs/new")}
        className="gap-1.5 mt-1"
      >
        <Plus className="h-3.5 w-3.5" />
        New Design
      </Button>
    )}
  </div>
)}

{/* Search produced no matches */}
{!isLoading && !isError && hasDesigns && filteredDesigns.length === 0 && (
  <p className="text-center text-sm text-muted-foreground py-8">
    No designs match &ldquo;{search}&rdquo;.
  </p>
)}

{/* Design cards */}
{!isLoading && !isError && filteredDesigns.length > 0 && (
  <div className="space-y-2">
    {filteredDesigns.map((design) => (
      <DesignCard key={design.id} design={design} />
    ))}
  </div>
)}
```

---

## Part 2 — DesignCard.tsx

### 2a — New import

Add `ChevronRight` to the existing lucide import line:

```tsx
import { ChevronRight, Palette, Upload } from "lucide-react";
```

### 2b — Add ChevronRight hint inside the link

Inside the `<Link>` wrapper, the current layout is:
```
[icon square]  [text block (title + description + date)]
```

Add a `ChevronRight` at the far right of the link's flex container to signal navigation:

Change the `<Link>` opening div from:
```tsx
<div className="min-w-0 flex-1 space-y-1">
```
to keep it, and add after the closing `</div>` of that text block (but still inside `<Link>`):

```tsx
<ChevronRight className="h-4 w-4 text-muted-foreground/40 shrink-0 self-center" />
```

The complete `<Link>` interior after the change:

```tsx
<Link
  to={`/designs/${design.id}`}
  className="flex items-center gap-4 flex-1 min-w-0 focus:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded"
>
  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-muted">
    <Palette className="h-5 w-5 text-muted-foreground" />
  </div>

  <div className="min-w-0 flex-1 space-y-1">
    <p className="text-sm font-medium leading-none">{design.title}</p>
    {design.description && (
      <p className="text-xs text-muted-foreground truncate">{design.description}</p>
    )}
    <p className="text-xs text-muted-foreground">{formatDate(design.createdAt)}</p>
  </div>

  <ChevronRight className="h-4 w-4 text-muted-foreground/40 shrink-0 self-center" />
</Link>
```

> Change `items-start` to `items-center` on the `<Link>` className so the chevron and the
> upload button align vertically with the center of the card, not the top edge.

---

## Part 3 — DesignListPage.test.tsx

### 3a — Update the broken empty state test

The existing test checks for `"No designs in this studio yet."` which will no longer match
after the rich empty state change. Update it:

```ts
// BEFORE
it("shows empty-state text when no designs exist", async () => {
  server.use(
    http.get("http://localhost/api/v1/designs", () => HttpResponse.json([])),
  );
  renderPage();
  expect(await screen.findByText("No designs in this studio yet.")).toBeInTheDocument();
});

// AFTER
it("shows rich empty state when no designs exist", async () => {
  server.use(
    http.get("http://localhost/api/v1/designs", () => HttpResponse.json([])),
  );
  renderPage();
  expect(await screen.findByText("No designs yet")).toBeInTheDocument();
  expect(screen.getByText(/upload a tattoo design/i)).toBeInTheDocument();
});
```

### 3b — New tests to append inside `describe("DesignListPage", ...)`

```ts
// ── Rich empty state ──────────────────────────────────────────────────────────

it("rich empty state shows a New Design button for owner role", async () => {
  server.use(
    http.get("http://localhost/api/v1/designs", () => HttpResponse.json([])),
  );
  renderPage(Role.Owner);
  await screen.findByText("No designs yet");
  expect(screen.getByRole("button", { name: /new design/i })).toBeInTheDocument();
});

it("rich empty state New Design button navigates to /designs/new", async () => {
  const user = userEvent.setup();
  server.use(
    http.get("http://localhost/api/v1/designs", () => HttpResponse.json([])),
  );
  renderPage(Role.Owner);
  await screen.findByText("No designs yet");
  await user.click(screen.getByRole("button", { name: /new design/i }));
  expect(screen.getByTestId("create-page")).toBeInTheDocument();
});

it("rich empty state does NOT show New Design button for client role", async () => {
  server.use(
    http.get("http://localhost/api/v1/designs", () => HttpResponse.json([])),
  );
  renderPage(Role.Client);
  await screen.findByText("No designs yet");
  // Clients cannot create designs
  expect(screen.queryByRole("button", { name: /new design/i })).not.toBeInTheDocument();
});

// ── Client-side search ────────────────────────────────────────────────────────

it("search input is present on the designs page", async () => {
  renderPage();
  await screen.findByText("Dragon Sleeve");
  expect(screen.getByPlaceholderText(/search by title/i)).toBeInTheDocument();
});

it("typing in the search bar filters designs by title", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Dragon Sleeve");

  await user.type(screen.getByPlaceholderText(/search by title/i), "Dragon");

  expect(screen.getByText("Dragon Sleeve")).toBeInTheDocument();
  expect(screen.queryByText("Rose Chest")).not.toBeInTheDocument();
});

it("search is case-insensitive", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Dragon Sleeve");

  await user.type(screen.getByPlaceholderText(/search by title/i), "rose");

  expect(screen.getByText("Rose Chest")).toBeInTheDocument();
  expect(screen.queryByText("Dragon Sleeve")).not.toBeInTheDocument();
});

it("shows a no-match message when search finds nothing", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Dragon Sleeve");

  await user.type(screen.getByPlaceholderText(/search by title/i), "xyzzy");

  expect(await screen.findByText(/no designs match/i)).toBeInTheDocument();
  expect(screen.queryByText("Dragon Sleeve")).not.toBeInTheDocument();
  expect(screen.queryByText("Rose Chest")).not.toBeInTheDocument();
});

it("clearing the search restores all designs", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Dragon Sleeve");

  const input = screen.getByPlaceholderText(/search by title/i);
  await user.type(input, "Dragon");
  expect(screen.queryByText("Rose Chest")).not.toBeInTheDocument();

  await user.clear(input);
  expect(screen.getByText("Dragon Sleeve")).toBeInTheDocument();
  expect(screen.getByText("Rose Chest")).toBeInTheDocument();
});
```

---

## Part 4 — Verify

```bash
# TypeScript — must be clean (no new errors beyond pre-existing)
pnpm tsc --noEmit

# Designs list tests — all must pass (13 existing updated + 8 new = 21 total)
pnpm test src/features/designs/DesignListPage --run

# Smoke-check that DesignCard tests still pass (tests ChevronRight change doesn't break anything)
pnpm test src/features/designs --run

# Full suite smoke-check
pnpm test --run
```

If `pnpm test --run` surfaces failures in unrelated files, note them and move on.

---

## Constraints (from CLAUDE.md)

- Do NOT add new npm packages — `Input`, `Skeleton`, `Button`, `Search`, `ChevronRight` are all
  already in the project.
- No useEffect for data fetching — the client-side search uses `useMemo` only. No API calls.
- TypeScript strict mode — no `any`, explicit types everywhere.
- No default exports on components.
- Do NOT add status badges, attribution lines, delete actions, or thumbnails — none of these
  have supporting data in `DesignResponse` or `designsApi`. Scope is limited to layout,
  skeleton, empty state, and client-side search.
