# Overnight Prompt — Artists List Overhaul
> Date: 2026-06-19
> Target files: `ArtistListPage.tsx`, `ArtistListPage.test.tsx`
> No new npm or NuGet packages. No new backend changes.

---

## Pre-flight

1. Read `CLAUDE.md` and `docs/claude/frontend.md` before making any changes.
2. Run `pnpm tsc --noEmit` and note any pre-existing errors so you do not count them as regressions.
3. Run `pnpm test src/features/artists` and confirm all 10 existing tests pass before touching anything.

---

## Context

`frontend/src/features/artists/components/ArtistListPage.tsx` (137 lines) and its test file at `frontend/src/features/artists/__tests__/ArtistListPage.test.tsx` (167 lines, 10 tests) are the targets.

The current state (already applied from a previous prompt):
- Header with Users icon + count + "New Artist" button (owners only)
- Debounced search via `useEffect` + `useGetArtistsQuery(search)`
- `ArtistRowSkeleton` — 3 flat rectangles (does NOT match actual row shape)
- `DataTable` with Name (initials avatar + full name), Email, Specializations (chips or em-dash)
- `onRowClick` navigates to `/artists/:id`
- `emptyMessage` is search-aware string

What is still missing (from a UI/UX audit):
1. **ArtistRowSkeleton** does not resemble the actual rendered row — no avatar circle shape
2. **No row-level actions** — owners cannot Edit or Delete from the list
3. **No specialization filter** — users must manually search to narrow by discipline
4. **Rich empty state** — when zero artists exist (no search active), only a plain text string shows

---

## Part 1 — Update `ArtistRowSkeleton`

Replace the current flat-rectangle skeleton with one that matches the actual row layout: avatar circle on the left, name line + email line in the middle, two pill-shaped spec chips on the right.

```tsx
function ArtistRowSkeleton() {
  return (
    <div
      className="flex items-center gap-3 px-3 py-3 border-b"
      aria-hidden="true"
    >
      <Skeleton className="h-7 w-7 rounded-full shrink-0" />
      <div className="flex-1 space-y-1.5">
        <Skeleton className="h-3.5 w-32" />
        <Skeleton className="h-3 w-44 opacity-60" />
      </div>
      <div className="flex items-center gap-1">
        <Skeleton className="h-5 w-16 rounded-full" />
        <Skeleton className="h-5 w-14 rounded-full" />
      </div>
    </div>
  );
}
```

No test changes required for the skeleton shape.

---

## Part 2 — Specialization filter pills

Add a client-side specialization filter above the table. The filter derives unique specs from whatever `artists` the API returned (so it narrows alongside the search).

### New state and derived values in `ArtistListPage`

Add these after the existing `useGetArtistsQuery` call:

```tsx
const [selectedSpec, setSelectedSpec] = useState<string | null>(null);

// Derive unique sorted specs from the current API result
const allSpecs = useMemo<string[]>(() => {
  if (!artists) return [];
  const set = new Set<string>();
  artists.forEach((a) => {
    if (a.specializations) {
      a.specializations
        .split(",")
        .map((s) => s.trim())
        .filter(Boolean)
        .forEach((s) => set.add(s));
    }
  });
  return [...set].sort();
}, [artists]);

// Apply spec filter on top of the API result
const filteredArtists = useMemo<ArtistResponse[]>(() => {
  if (!artists) return [];
  if (!selectedSpec) return artists;
  return artists.filter((a) =>
    a.specializations
      ?.split(",")
      .map((s) => s.trim())
      .includes(selectedSpec),
  );
}, [artists, selectedSpec]);
```

Also add a `useEffect` (state sync, not data fetching) to reset the spec selection when the text search changes, so a stale filter does not silently empty the table:

```tsx
useEffect(() => {
  setSelectedSpec(null);
}, [search]);
```

Add `useMemo` to the existing `import { useEffect, useState } from "react"` line — change it to:

```tsx
import { useEffect, useMemo, useState } from "react";
```

Also add `cn` import (needed for the active-pill styling):

```tsx
import { cn } from "@/shared/utils/cn";
```

### Filter pill UI

Insert the spec filter pills between the search `<Input>` block and the loading/error/table block. Only show when data is available and there are specs to filter:

```tsx
{!isLoading && !isError && allSpecs.length > 0 && (
  <div className="flex flex-wrap items-center gap-2" aria-label="Filter by specialization">
    {allSpecs.map((spec) => (
      <button
        key={spec}
        type="button"
        aria-pressed={selectedSpec === spec}
        onClick={() => setSelectedSpec(selectedSpec === spec ? null : spec)}
        className={cn(
          "rounded-full border px-3 py-0.5 text-xs font-medium transition-colors",
          selectedSpec === spec
            ? "border-foreground bg-foreground text-background"
            : "border-border bg-background text-muted-foreground hover:border-foreground hover:text-foreground",
        )}
      >
        {spec}
      </button>
    ))}
  </div>
)}
```

### DataTable data change

Change the DataTable `data` prop from `artists ?? []` to `filteredArtists`. Also compute the right `emptyMessage`:

```tsx
const tableEmptyMessage = search
  ? `No artists match "${search}".`
  : selectedSpec
  ? `No artists with "${selectedSpec}" specialization.`
  : "No artists in this studio yet.";
```

Pass `data={filteredArtists}` and `emptyMessage={tableEmptyMessage}` to `DataTable`.

---

## Part 3 — Row-level Edit + Delete actions (owners only)

### Delete mutation hook

Add to the component body (alongside the other hooks, before the `return`):

```tsx
const [deleteArtist, { isLoading: isDeletingArtist }] =
  useDeleteArtistMutation();
const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);
```

Add to the import from `"../artistsApi"`:

```tsx
import { useGetArtistsQuery, useDeleteArtistMutation } from "../artistsApi";
```

Add these lucide icons to the import:

```tsx
import { Pencil, Plus, Search, Trash2, Users } from "lucide-react";
```

### Actions column

Add a 4th column to the `columns` array passed to `DataTable`. This column has no header text and renders per-row actions. Use `onClick={(e) => e.stopPropagation()}` on the wrapper `div` to prevent the row's `onRowClick` from firing when a button is clicked inside the cell.

```tsx
{
  header: "",
  cell: (a) => (
    <div
      className="flex items-center justify-end gap-1"
      onClick={(e) => e.stopPropagation()}
    >
      {/* Edit — always visible so the affordance is clear */}
      <Button
        variant="ghost"
        size="sm"
        className="h-7 text-xs gap-1"
        onClick={() => navigate(`/artists/${a.id}`)}
      >
        <Pencil className="h-3 w-3" />
        Edit
      </Button>

      {/* Delete — owners only, inline two-step confirmation */}
      {canManage && (
        confirmDeleteId === a.id ? (
          <div className="flex items-center gap-1.5">
            <span className="text-xs text-destructive whitespace-nowrap">
              Delete {a.firstName} {a.lastName}?
            </span>
            <Button
              variant="ghost"
              size="sm"
              className="h-6 text-xs"
              onClick={() => setConfirmDeleteId(null)}
            >
              Cancel
            </Button>
            <Button
              variant="destructive"
              size="sm"
              className="h-6 text-xs"
              disabled={isDeletingArtist}
              onClick={async () => {
                await deleteArtist(a.id);
                setConfirmDeleteId(null);
              }}
            >
              {isDeletingArtist ? "Deleting…" : "Confirm"}
            </Button>
          </div>
        ) : (
          <Button
            variant="ghost"
            size="sm"
            className="h-7 text-xs gap-1 text-destructive hover:text-destructive hover:bg-destructive/10"
            onClick={() => setConfirmDeleteId(a.id)}
          >
            <Trash2 className="h-3 w-3" />
            Delete
          </Button>
        )
      )}
    </div>
  ),
},
```

---

## Part 4 — Rich empty state (zero artists, no search active)

When there are no artists at all (not filtered), show a rich centred empty state instead of relying on `DataTable`'s plain-text `emptyMessage`. Do this by adding a new conditional block **before** the `DataTable` block, and guarding the `DataTable` so it only renders when there is something to show or a search is active.

Add a boolean to simplify the conditionals:

```tsx
const hasArtists = (artists?.length ?? 0) > 0;
```

Replace the existing single render block:

```tsx
{!isLoading && !isError && (
  <DataTable<ArtistResponse> ...>
```

with:

```tsx
{/* Rich empty state — only when there are zero artists and no search is active */}
{!isLoading && !isError && !hasArtists && !search && (
  <div className="flex flex-col items-center gap-3 py-16 text-center">
    <Users className="h-8 w-8 text-muted-foreground/40" />
    <p className="text-sm font-medium">No artists yet</p>
    <p className="text-xs text-muted-foreground">
      Add your first artist to get started.
    </p>
    {canManage && (
      <Button
        size="sm"
        onClick={() => navigate("/artists/new")}
        className="gap-1.5 mt-1"
      >
        <Plus className="h-3.5 w-3.5" />
        New Artist
      </Button>
    )}
  </div>
)}

{/* Spec filter pills + table — when artists exist or a search is active */}
{!isLoading && !isError && (hasArtists || !!search) && (
  <>
    {allSpecs.length > 0 && (
      <div className="flex flex-wrap items-center gap-2" aria-label="Filter by specialization">
        {allSpecs.map((spec) => (
          <button
            key={spec}
            type="button"
            aria-pressed={selectedSpec === spec}
            onClick={() => setSelectedSpec(selectedSpec === spec ? null : spec)}
            className={cn(
              "rounded-full border px-3 py-0.5 text-xs font-medium transition-colors",
              selectedSpec === spec
                ? "border-foreground bg-foreground text-background"
                : "border-border bg-background text-muted-foreground hover:border-foreground hover:text-foreground",
            )}
          >
            {spec}
          </button>
        ))}
      </div>
    )}

    <DataTable<ArtistResponse>
      columns={[...columns with actions as described in Part 3...]}
      data={filteredArtists}
      keyExtractor={(a) => a.id}
      onRowClick={(a) => navigate(`/artists/${a.id}`)}
      emptyMessage={tableEmptyMessage}
    />
  </>
)}
```

> **Implementation note:** Extract `columns` into a `const columns: ColumnDef<ArtistResponse>[]` variable above the `return` if the inline JSX becomes unwieldy. `canManage`, `navigate`, `confirmDeleteId`, `setConfirmDeleteId`, `deleteArtist`, and `isDeletingArtist` must all be in scope.

---

## Part 5 — Update `ArtistListPage.test.tsx`

### Add to MSW server

Add a DELETE handler to the `setupServer(...)` call:

```ts
http.delete("http://localhost/api/v1/artists/:id", () =>
  new HttpResponse(null, { status: 204 }),
),
```

### Add imports

Add `within` and `waitFor` to the `@testing-library/react` import:

```ts
import { render, screen, cleanup, within, waitFor } from "@testing-library/react";
```

### Add a non-owner store helper

Add a second `makeStore` variant for testing that Delete is hidden from non-owners:

```ts
function makeStoreAsArtist() {
  return configureStore({
    reducer: {
      auth: authReducer,
      ui:   uiReducer,
      [artistsApi.reducerPath]: artistsApi.reducer,
    },
    middleware: (gd) => gd().concat(artistsApi.middleware),
    preloadedState: {
      auth: {
        user: { id: "u2", email: "artist@ink.test" },
        token: "fake-token",
        tenantId: "stud-0001",
        role: "artist",
        pendingReferralCode: null,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      } as any,
      ui: { readOnlyError: null, sessionExpired: false },
    },
  });
}

function renderPageAsArtist() {
  const store = makeStoreAsArtist();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/artists"]}>
        <Routes>
          <Route path="/artists"     element={<ArtistListPage />} />
          <Route path="/artists/:id" element={<div data-testid="artist-detail" />} />
          <Route path="/artists/new" element={<div data-testid="artist-new" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}
```

Also update the existing `renderPage` helper to include the `/artists/new` route:

```ts
function renderPage() {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/artists"]}>
        <Routes>
          <Route path="/artists"     element={<ArtistListPage />} />
          <Route path="/artists/:id" element={<div data-testid="artist-detail" />} />
          <Route path="/artists/new" element={<div data-testid="artist-new" />} />  {/* add this */}
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}
```

### New tests to append inside `describe("ArtistListPage", ...)`

```ts
// ── Actions column ──────────────────────────────────────────────────────────

it("Edit button navigates to /artists/:id", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Ana Costa");

  const editButtons = screen.getAllByRole("button", { name: /^edit$/i });
  await user.click(editButtons[0]);

  expect(screen.getByTestId("artist-detail")).toBeInTheDocument();
});

it("Delete button is visible to owners", async () => {
  renderPage();
  await screen.findByText("Ana Costa");
  expect(screen.getAllByRole("button", { name: /^delete$/i }).length).toBeGreaterThanOrEqual(1);
});

it("Delete button is NOT visible to non-owners", async () => {
  renderPageAsArtist();
  await screen.findByText("Ana Costa");
  expect(screen.queryByRole("button", { name: /^delete$/i })).not.toBeInTheDocument();
});

it("clicking Delete shows inline confirmation for that artist", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Ana Costa");

  // Click the Delete button for the first artist
  await user.click(screen.getAllByRole("button", { name: /^delete$/i })[0]);

  expect(screen.getByText(/delete ana costa\?/i)).toBeInTheDocument();
  expect(screen.getByRole("button", { name: /^cancel$/i })).toBeInTheDocument();
  expect(screen.getByRole("button", { name: /^confirm$/i })).toBeInTheDocument();
});

it("clicking Cancel hides the delete confirmation", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Ana Costa");

  await user.click(screen.getAllByRole("button", { name: /^delete$/i })[0]);
  expect(screen.getByText(/delete ana costa\?/i)).toBeInTheDocument();

  await user.click(screen.getByRole("button", { name: /^cancel$/i }));

  expect(screen.queryByText(/delete ana costa\?/i)).not.toBeInTheDocument();
  // The Delete button is back
  expect(screen.getAllByRole("button", { name: /^delete$/i }).length).toBeGreaterThanOrEqual(1);
});

it("confirming delete calls DELETE /artists/:id", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Ana Costa");

  await user.click(screen.getAllByRole("button", { name: /^delete$/i })[0]);
  await user.click(screen.getByRole("button", { name: /^confirm$/i }));

  // Confirmation row should clear after the mutation resolves
  await waitFor(() => {
    expect(screen.queryByText(/delete ana costa\?/i)).not.toBeInTheDocument();
  });
});

// ── Specialization filter ───────────────────────────────────────────────────

it("spec filter buttons appear for specs in the loaded data", async () => {
  renderPage();
  await screen.findByText("Ana Costa");

  // "Realism" and "Blackwork" are from ARTIST_A; they should appear as filter buttons
  expect(screen.getByRole("button", { name: "Realism" })).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Blackwork" })).toBeInTheDocument();
});

it("clicking a spec filter button filters the table to matching artists", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Ana Costa");

  await user.click(screen.getByRole("button", { name: "Realism" }));

  // Ana has "Realism", Marco does not — Marco should be gone
  expect(screen.getByText("Ana Costa")).toBeInTheDocument();
  expect(screen.queryByText("Marco Silva")).not.toBeInTheDocument();
});

it("clicking the active spec filter button again clears the filter", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Ana Costa");

  await user.click(screen.getByRole("button", { name: "Realism" }));
  expect(screen.queryByText("Marco Silva")).not.toBeInTheDocument();

  await user.click(screen.getByRole("button", { name: "Realism" }));
  expect(screen.getByText("Marco Silva")).toBeInTheDocument();
});

// ── Rich empty state ────────────────────────────────────────────────────────

it("shows rich empty state with icon text and CTA when zero artists", async () => {
  server.use(
    http.get("http://localhost/api/v1/artists", () => HttpResponse.json([])),
  );
  renderPage();

  expect(await screen.findByText("No artists yet")).toBeInTheDocument();
  expect(screen.getByText(/add your first artist/i)).toBeInTheDocument();
});

it("rich empty state New Artist button navigates to /artists/new", async () => {
  const user = userEvent.setup();
  server.use(
    http.get("http://localhost/api/v1/artists", () => HttpResponse.json([])),
  );
  renderPage();

  await screen.findByText("No artists yet");
  await user.click(screen.getByRole("button", { name: /new artist/i }));

  expect(screen.getByTestId("artist-new")).toBeInTheDocument();
});
```

---

## Part 6 — Verify

```bash
# TypeScript — must be clean (no new errors)
pnpm tsc --noEmit

# Artists tests — all must pass (10 existing + 11 new = 21 total)
pnpm test src/features/artists --run

# Smoke-check nothing else broke
pnpm test --run
```

If `pnpm test --run` has pre-existing failures in unrelated files, do not fix them — note them and move on.

---

## Constraints (from CLAUDE.md)

- Do NOT add new npm packages. All components used here (`Button`, `Input`, `Skeleton`, `DataTable`) are already in the project.
- No useEffect for data fetching — the existing debounce `useEffect` and the spec-reset `useEffect` are state sync, not data fetching. Both are acceptable.
- TypeScript strict mode — no `any`, explicit types on all variables.
- No default exports on components.
- No business logic in endpoints — this prompt is frontend-only; no backend changes.
