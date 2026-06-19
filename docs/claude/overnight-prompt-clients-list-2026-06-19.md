# Overnight Prompt — Clients List Overhaul
> Date: 2026-06-19
> Target files: `ClientListPage.tsx`, `ClientListPage.test.tsx`
> No new npm or NuGet packages. No new backend changes.

---

## Pre-flight

1. Read `CLAUDE.md` and `docs/claude/frontend.md` before making any changes.
2. Run `pnpm tsc --noEmit` — note any pre-existing errors; do not count them as regressions.
3. Run `pnpm test src/features/clients/ClientListPage` — confirm all 10 existing tests pass first.

---

## Context

`frontend/src/features/clients/components/ClientListPage.tsx` (118 lines) and its test file
`frontend/src/features/clients/__tests__/ClientListPage.test.tsx` (164 lines, 10 tests) are
the targets.

Current state (already applied from a previous session):
- Header: Users icon + client count badge + "New Client" button (artists and above)
- Debounced search → `useGetClientsQuery(search)`
- `ClientRowSkeleton` — 3 flat rectangles (does NOT reflect actual row shape)
- `DataTable` with Name (initials avatar + full name), Email, Phone (accessible em-dash)
- `onRowClick` navigates to `/clients/:id`
- `emptyMessage` is search-aware string

Note — the double bell icon reported in the audit is **already fixed**: `OwnerLayout.tsx` was
updated in a prior session to remove the Notifications nav item, leaving only the
`NotificationBell` component in the header. No further action needed there.

What is still missing:
1. `ClientRowSkeleton` shape does not match the rendered row
2. No visible row-level action — zero affordance that rows are interactive (audit's #1 critical)
3. No rich empty state when the studio has zero clients

> There is no `deleteClient` mutation in `clientsApi.ts`. The Actions column is "View →" only.
> Do not fabricate a delete action.

---

## Part 1 — Update `ClientRowSkeleton`

Replace the three flat rectangles with a skeleton that mirrors the actual rendered row:
avatar circle on the left, name line + email line stacked in the middle, a short phone
placeholder, and a small button shape on the right (matching the View button added in Part 2).

```tsx
function ClientRowSkeleton() {
  return (
    <div
      className="flex items-center gap-3 px-3 py-3 border-b"
      aria-hidden="true"
    >
      <Skeleton className="h-7 w-7 rounded-full shrink-0" />
      <div className="flex-1 space-y-1.5">
        <Skeleton className="h-3.5 w-28" />
        <Skeleton className="h-3 w-40 opacity-60" />
      </div>
      <Skeleton className="h-3.5 w-28" />
      <Skeleton className="h-7 w-14 rounded-md" />
    </div>
  );
}
```

No test changes are required for the skeleton shape.

---

## Part 2 — Add a "View" Actions column

The audit's #1 critical issue: rows show data but provide zero visual affordance that they are
interactive. Add a 4th column with an explicit "View →" button per row.

Use `onClick={(e) => e.stopPropagation()}` on the wrapper `div` so the button click does not
also trigger the row's `onRowClick` handler (which would navigate twice to the same route — React
Router handles this gracefully, but stopPropagation is the correct pattern).

### New import

Add `ChevronRight` to the lucide-react import line:

```tsx
import { ChevronRight, Plus, Search, Users } from "lucide-react";
```

### New column (append after the Phone column)

```tsx
{
  header: "",
  cell: (c) => (
    <div
      className="flex items-center justify-end"
      onClick={(e) => e.stopPropagation()}
    >
      <Button
        variant="ghost"
        size="sm"
        className="h-7 text-xs gap-1 text-muted-foreground hover:text-foreground"
        onClick={() => navigate(`/clients/${c.id}`)}
      >
        View
        <ChevronRight className="h-3 w-3" />
      </Button>
    </div>
  ),
},
```

The four columns in order: Name, Email, Phone, (actions).

---

## Part 3 — Rich empty state

When there are zero clients and no search is active, render a centred branded empty state
instead of relying on `DataTable`'s plain-text `emptyMessage`. This is the first-run experience
for a new studio owner.

Add a helper boolean:

```tsx
const hasClients = (clients?.length ?? 0) > 0;
```

Replace the single conditional block:

```tsx
{!isLoading && !isError && (
  <DataTable<ClientResponse> ...>
```

with two separate blocks:

```tsx
{/* Rich empty state — zero clients, no search active */}
{!isLoading && !isError && !hasClients && !search && (
  <div className="flex flex-col items-center gap-3 py-16 text-center">
    <Users className="h-8 w-8 text-muted-foreground/40" />
    <p className="text-sm font-medium">No clients yet</p>
    <p className="text-xs text-muted-foreground">
      Add your first client to get started.
    </p>
    {canCreate && (
      <Button
        size="sm"
        onClick={() => navigate("/clients/new")}
        className="gap-1.5 mt-1"
      >
        <Plus className="h-3.5 w-3.5" />
        New Client
      </Button>
    )}
  </div>
)}

{/* Table — when clients exist or a search is active */}
{!isLoading && !isError && (hasClients || !!search) && (
  <DataTable<ClientResponse>
    columns={[...all four columns as described above...]}
    data={clients ?? []}
    keyExtractor={(c) => c.id}
    onRowClick={(c) => navigate(`/clients/${c.id}`)}
    emptyMessage={search ? `No clients match "${search}".` : "No clients in this studio yet."}
  />
)}
```

Conditional logic explanation:
- No clients + no search → rich empty state ✓
- No clients + search active → DataTable with `emptyMessage="No clients match '…'."` ✓
- Clients exist → DataTable with data ✓

---

## Part 4 — Update `ClientListPage.test.tsx`

### Update `renderPage` helper

Add the `/clients/new` route so the "New Client" CTA navigation test does not crash:

```ts
function renderPage() {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/clients"]}>
        <Routes>
          <Route path="/clients"     element={<ClientListPage />} />
          <Route path="/clients/:id" element={<div data-testid="client-detail" />} />
          <Route path="/clients/new" element={<div data-testid="client-new" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}
```

### New tests to append inside `describe("ClientListPage", ...)`

```ts
// ── View action ─────────────────────────────────────────────────────────────

it("renders a View button for each loaded client", async () => {
  renderPage();
  await screen.findByText("João Silva");

  const viewButtons = screen.getAllByRole("button", { name: /^view$/i });
  expect(viewButtons.length).toBe(2); // one per loaded client
});

it("clicking the View button navigates to /clients/:id", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("João Silva");

  const viewButtons = screen.getAllByRole("button", { name: /^view$/i });
  await user.click(viewButtons[0]);

  expect(screen.getByTestId("client-detail")).toBeInTheDocument();
});

// ── Rich empty state ─────────────────────────────────────────────────────────

it("shows rich empty state when no clients exist and no search is active", async () => {
  server.use(
    http.get("http://localhost/api/v1/clients", () => HttpResponse.json([])),
  );
  renderPage();

  expect(await screen.findByText("No clients yet")).toBeInTheDocument();
  expect(screen.getByText(/add your first client/i)).toBeInTheDocument();
});

it("rich empty state shows a New Client button", async () => {
  server.use(
    http.get("http://localhost/api/v1/clients", () => HttpResponse.json([])),
  );
  renderPage();

  await screen.findByText("No clients yet");
  expect(screen.getByRole("button", { name: /new client/i })).toBeInTheDocument();
});

it("rich empty state New Client button navigates to /clients/new", async () => {
  const user = userEvent.setup();
  server.use(
    http.get("http://localhost/api/v1/clients", () => HttpResponse.json([])),
  );
  renderPage();

  await screen.findByText("No clients yet");
  await user.click(screen.getByRole("button", { name: /new client/i }));

  expect(screen.getByTestId("client-new")).toBeInTheDocument();
});

it("shows DataTable with emptyMessage when search returns no clients", async () => {
  server.use(
    http.get("http://localhost/api/v1/clients", () => HttpResponse.json([])),
  );
  const user = userEvent.setup();
  renderPage();

  // Wait for data to arrive (empty), then type a search query
  await screen.findByText("No clients yet");
  await user.type(screen.getByPlaceholderText(/search/i), "xyz");

  // The server still returns [] — the DataTable emptyMessage should show
  // The rich empty state should NOT show while a search is active
  expect(await screen.findByText(/no clients match/i)).toBeInTheDocument();
  expect(screen.queryByText("No clients yet")).not.toBeInTheDocument();
});
```

> **Note on the last test:** The search sends the query param to the API. The MSW handler still
> returns `[]`. Because `search` is now set (non-undefined), `!search` is false, so the rich
> empty state block is skipped and the DataTable block renders with `emptyMessage`.
> This test requires `waitFor` or `findBy` because the debounce fires after 300 ms.

---

## Part 5 — Verify

```bash
# TypeScript — must be clean (no new errors beyond pre-existing)
pnpm tsc --noEmit

# Client list tests — all must pass (10 existing + 6 new = 16 total)
pnpm test src/features/clients/ClientListPage --run

# Smoke-check nothing else broke
pnpm test --run
```

If `pnpm test --run` surfaces failures in unrelated files, note them and move on.

---

## Constraints (from CLAUDE.md)

- Do NOT add new npm packages. All components (`Button`, `Input`, `Skeleton`, `DataTable`,
  `ChevronRight`) are already in the project.
- No useEffect for data fetching — the existing debounce `useEffect` is state sync, not data
  fetching. It stays as-is.
- TypeScript strict mode — no `any`, explicit types everywhere.
- No default exports on components.
- No `deleteClient` mutation exists in `clientsApi.ts`. Do not invent one.
