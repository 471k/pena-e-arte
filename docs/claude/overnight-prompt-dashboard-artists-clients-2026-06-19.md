# Overnight Prompt — Dashboard + Artists List + Clients List UI/UX Polish
**Date:** 2026-06-19
**Scope:** Frontend only — 3 components + 2 new test files + 1 updated test file
**Packages:** Do NOT add any new npm packages.

---

## Pre-flight

Read in order before touching any file:
1. `CLAUDE.md`
2. `docs/claude/frontend.md`
3. `docs/claude/conventions.md`

---

## PART A — DashboardPage

**Source:** `frontend/src/features/dashboard/components/DashboardPage.tsx`
**Tests:**  `frontend/src/features/dashboard/__tests__/DashboardPage.test.tsx`

---

### A1 — Widen the page container

Find:
```tsx
<main className="max-w-lg mx-auto px-4 py-6 space-y-4">
```
Replace with:
```tsx
<main className="max-w-2xl mx-auto px-4 py-6 space-y-4">
```

`max-w-lg` (~32rem) is too narrow on desktop. `max-w-2xl` (~42rem) gives the page room without becoming too wide on mobile.

---

### A2 — QuickNav: fix grid and update label copy

**A2a — Fix grid**

8 tiles in `grid-cols-3` leaves an empty cell in the last row. Change the QuickNav grid wrapper to:
```
grid-cols-4
```
2 rows × 4 columns = 8 cells exactly.

**A2b — Fix label copy in NAV_TILES**

Make exactly two label changes in the `NAV_TILES` constant:

| Current | New |
|---|---|
| `"Deposit Rules"` | `"Deposits"` |
| `"Studio"` | `"Studio Settings"` |

Do not change any other tiles, icons, or hrefs.

---

### A3 — TodaySection: replace Loader2 spinner with skeleton rows

**A3a — Add Skeleton import**
```tsx
import { Skeleton } from "@/shared/components/ui/skeleton";
```

**A3b — Define AppointmentRowSkeleton** at module level (not exported), before the `DashboardPage` component:
```tsx
function AppointmentRowSkeleton() {
  return (
    <div
      className="flex items-center gap-3 py-2"
      data-testid="appointment-skeleton"
      aria-hidden="true"
    >
      <Skeleton className="h-8 w-8 rounded-full" />
      <div className="flex-1 space-y-1">
        <Skeleton className="h-3 w-1/3" />
        <Skeleton className="h-3 w-1/2" />
      </div>
      <Skeleton className="h-5 w-16 rounded-full" />
    </div>
  );
}
```

**A3c — Replace the loading block**

Find the existing loading state (renders a `Loader2` spinner and the text `"Loading…"`). Replace the entire block with:
```tsx
{isLoading && (
  <div className="space-y-2" aria-label="Loading appointments">
    <AppointmentRowSkeleton />
    <AppointmentRowSkeleton />
    <AppointmentRowSkeleton />
  </div>
)}
```

**A3d — Remove unused Loader2 import** if it is no longer used anywhere else in the file.

---

### A4 — TodaySection: empty state with CTAs

Find:
```tsx
{!isLoading && !isError && appointments?.length === 0 && (
  <p className="text-sm text-muted-foreground py-4 text-center">
    No appointments today.
  </p>
)}
```
Replace with:
```tsx
{!isLoading && !isError && appointments?.length === 0 && (
  <div className="py-6 flex flex-col items-center gap-3 text-center">
    <p className="text-sm text-muted-foreground">No appointments today.</p>
    <div className="flex gap-2">
      <Button size="sm" onClick={() => navigate("/appointments/new")}>
        Book Appointment
      </Button>
      <Button variant="ghost" size="sm" onClick={() => navigate("/schedule")}>
        View this week →
      </Button>
    </div>
  </div>
)}
```

`Button` and `navigate` are already available in this file.

---

### A5 — "Full schedule" button: make more prominent

Find:
```tsx
<Button variant="ghost" size="sm" className="h-7 text-xs px-2" onClick={() => navigate("/schedule")}>
  Full schedule
</Button>
```
Replace with:
```tsx
<Button variant="link" size="sm" className="h-7 text-xs px-2 gap-1" onClick={() => navigate("/schedule")}>
  Full schedule
  <ChevronRight className="h-3 w-3" />
</Button>
```
Add `ChevronRight` to the lucide-react import line.

---

### A6 — Update Dashboard tests

**A6a — Add `/appointments/new` route to `renderPage`**

In the `<Routes>` block of the `renderPage` helper, add:
```tsx
<Route path="/appointments/new" element={<div data-testid="new-appointment-page" />} />
```

**A6b — Update loading spinner test**

Find:
```ts
it("shows a loading spinner while appointments are fetching", () => {
  renderPage();
  expect(screen.getByText("Loading…")).toBeInTheDocument();
});
```
Replace with:
```ts
it("shows skeleton rows while appointments are fetching", () => {
  renderPage();
  expect(screen.getAllByTestId("appointment-skeleton")).toHaveLength(3);
});
```

**A6c — Update QuickNav labels test**

Find the `"renders all 8 quick-nav tiles"` test. Inside it, change:
```ts
for (const label of ["Schedule", "Clients", "Artists", "Designs", "Deposit Rules", "Billing", "Notifications", "Studio"]) {
```
to:
```ts
for (const label of ["Schedule", "Clients", "Artists", "Designs", "Deposits", "Billing", "Notifications", "Studio Settings"]) {
```

**A6d — Add 4 new tests** at the end of the `describe("DashboardPage", ...)` block, before the final `});`:

```ts
// ── Empty state CTAs ────────────────────────────────────────────────────────

it("empty state shows 'Book Appointment' button", async () => {
  renderPage();
  expect(await screen.findByRole("button", { name: /book appointment/i })).toBeInTheDocument();
});

it("'Book Appointment' button navigates to /appointments/new", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByRole("button", { name: /book appointment/i });

  await user.click(screen.getByRole("button", { name: /book appointment/i }));

  expect(screen.getByTestId("new-appointment-page")).toBeInTheDocument();
});

it("empty state shows 'View this week' button", async () => {
  renderPage();
  expect(await screen.findByRole("button", { name: /view this week/i })).toBeInTheDocument();
});

it("'View this week →' button navigates to /schedule", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByRole("button", { name: /view this week/i });

  await user.click(screen.getByRole("button", { name: /view this week/i }));

  expect(screen.getByTestId("schedule-page")).toBeInTheDocument();
});
```

**A6e — Verify Dashboard**
```bash
cd frontend
pnpm tsc --noEmit
pnpm test src/features/dashboard
```
All previously passing tests still pass; 4 new tests pass (38 total).

---

## PART B — ArtistListPage

**Source:** `frontend/src/features/artists/components/ArtistListPage.tsx`
**Tests:**  `frontend/src/features/artists/__tests__/ArtistListPage.test.tsx` ← **create this file**

---

### B1 — Fix header icon

The header renders `<PenLine className="h-5 w-5" />` — a pencil icon, semantically wrong for a people list.

In the lucide-react import, replace `PenLine` with `Users`. Then in the JSX, replace `<PenLine className="h-5 w-5" />` with `<Users className="h-5 w-5" />`.

---

### B2 — Name column: add initials avatar

Find the Name column definition:
```tsx
{ header: "Name", cell: (a) => <span className="font-medium">{a.firstName} {a.lastName}</span> }
```
Replace with:
```tsx
{
  header: "Name",
  cell: (a) => (
    <div className="flex items-center gap-2">
      <div className="h-7 w-7 rounded-full bg-muted flex items-center justify-center text-xs font-medium shrink-0 select-none">
        {a.firstName[0]?.toUpperCase()}{a.lastName[0]?.toUpperCase()}
      </div>
      <span className="font-medium">{a.firstName} {a.lastName}</span>
    </div>
  ),
}
```

---

### B3 — Specializations column: render as chip badges

Find:
```tsx
{ header: "Specializations", cell: (a) => a.specializations ?? "—" }
```
Replace with:
```tsx
{
  header: "Specializations",
  cell: (a) => {
    if (!a.specializations) {
      return <span className="text-muted-foreground/60">—</span>;
    }
    const chips = a.specializations
      .split(",")
      .map((s) => s.trim())
      .filter(Boolean);
    if (chips.length === 0) {
      return <span className="text-muted-foreground/60">—</span>;
    }
    return (
      <div className="flex flex-wrap gap-1">
        {chips.map((spec) => (
          <span
            key={spec}
            className="rounded-full bg-muted px-1.5 py-0.5 text-xs font-medium"
          >
            {spec}
          </span>
        ))}
      </div>
    );
  },
}
```

`a.specializations` is stored as a comma-separated string (e.g. `"Realism, Blackwork, Geometric"`).

---

### B4 — Create ArtistListPage test file

**Create:** `frontend/src/features/artists/__tests__/ArtistListPage.test.tsx`

Read `ArtistListPage.tsx` first to confirm the exact hook name, type name, `reducerPath`, and GET endpoint URL before writing the test file.

**Seed data:**
```ts
const ARTIST_A: ArtistResponse = {
  id:              "artist-0001",
  studioId:        "stud-0001",
  firstName:       "Ana",
  lastName:        "Costa",
  email:           "ana@ink.test",
  specializations: "Realism, Blackwork",
  hourlyRate:      null,
  createdAt:       "2024-01-01T00:00:00Z",
  updatedAt:       "2024-01-01T00:00:00Z",
};

const ARTIST_B: ArtistResponse = {
  id:              "artist-0002",
  studioId:        "stud-0001",
  firstName:       "Marco",
  lastName:        "Silva",
  email:           "marco@ink.test",
  specializations: null,
  hourlyRate:      null,
  createdAt:       "2024-01-02T00:00:00Z",
  updatedAt:       "2024-01-02T00:00:00Z",
};
```

**MSW server** — default returns `[ARTIST_A, ARTIST_B]`:
```ts
const server = setupServer(
  http.get("http://localhost/api/v1/artists", () =>
    HttpResponse.json([ARTIST_A, ARTIST_B]),
  ),
);
```

**Store / render helpers:**
```ts
function makeStore() {
  return configureStore({
    reducer: {
      auth: authReducer,
      ui:   uiReducer,
      [artistsApi.reducerPath]: artistsApi.reducer,
    },
    middleware: (gd) => gd().concat(artistsApi.middleware),
    preloadedState: {
      auth: {
        user: { id: "u1", email: "owner@ink.test" },
        token: "fake-token",
        tenantId: "stud-0001",
        role: "owner",
        pendingReferralCode: null,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      } as any,
      ui: { readOnlyError: null, sessionExpired: false },
    },
  });
}

function renderPage() {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/artists"]}>
        <Routes>
          <Route path="/artists"     element={<ArtistListPage />} />
          <Route path="/artists/:id" element={<div data-testid="artist-detail" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}
```

**Tests** (all in `describe("ArtistListPage", () => { ... })`):

```ts
it("renders the Artists page heading", async () => {
  renderPage();
  expect(await screen.findByRole("heading", { name: /artists/i })).toBeInTheDocument();
});

it("does not show artist names while loading", () => {
  renderPage();
  expect(screen.queryByText("Ana Costa")).not.toBeInTheDocument();
});

it("renders artist full names", async () => {
  renderPage();
  expect(await screen.findByText("Ana Costa")).toBeInTheDocument();
  expect(screen.getByText("Marco Silva")).toBeInTheDocument();
});

it("renders initials avatar for each artist", async () => {
  renderPage();
  await screen.findByText("Ana Costa");
  expect(screen.getByText("AC")).toBeInTheDocument();
  expect(screen.getByText("MS")).toBeInTheDocument();
});

it("renders specialization chips when present", async () => {
  renderPage();
  await screen.findByText("Ana Costa");
  expect(screen.getByText("Realism")).toBeInTheDocument();
  expect(screen.getByText("Blackwork")).toBeInTheDocument();
});

it("renders em-dash placeholder when specializations are null", async () => {
  renderPage();
  await screen.findByText("Marco Silva");
  expect(screen.getAllByText("—").length).toBeGreaterThanOrEqual(1);
});

it("clicking an artist row navigates to /artists/:id", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Ana Costa");

  await user.click(screen.getByText("Ana Costa"));

  expect(screen.getByTestId("artist-detail")).toBeInTheDocument();
});

it("search input is present on the page", async () => {
  renderPage();
  await screen.findByText("Ana Costa");
  expect(screen.getByPlaceholderText(/search/i)).toBeInTheDocument();
});

it("shows an error message when artists fetch fails", async () => {
  server.use(
    http.get("http://localhost/api/v1/artists", () =>
      HttpResponse.json({ message: "Server error" }, { status: 500 }),
    ),
  );
  renderPage();
  expect(await screen.findByText(/failed to load/i)).toBeInTheDocument();
});

it("shows empty state when no artists are returned", async () => {
  server.use(
    http.get("http://localhost/api/v1/artists", () =>
      HttpResponse.json([]),
    ),
  );
  renderPage();
  // Check ArtistListPage.tsx for the exact emptyMessage prop string
  await screen.findByText(/no artists/i);
});
```

**B4 — Verify Artists**
```bash
pnpm tsc --noEmit
pnpm test src/features/artists
```

---

## PART C — ClientListPage

**Source:** `frontend/src/features/clients/components/ClientListPage.tsx`
**Tests:**  `frontend/src/features/clients/__tests__/ClientListPage.test.tsx` ← **create this file**

---

### C1 — Name column: add initials avatar

Find the Name column definition:
```tsx
{ header: "Name", cell: (c) => <span className="font-medium">{c.firstName} {c.lastName}</span> }
```
Replace with:
```tsx
{
  header: "Name",
  cell: (c) => (
    <div className="flex items-center gap-2">
      <div className="h-7 w-7 rounded-full bg-muted flex items-center justify-center text-xs font-medium shrink-0 select-none">
        {c.firstName[0]?.toUpperCase()}{c.lastName[0]?.toUpperCase()}
      </div>
      <span className="font-medium">{c.firstName} {c.lastName}</span>
    </div>
  ),
}
```

---

### C2 — Phone column: accessible em-dash for missing values

Find:
```tsx
{ header: "Phone", cell: (c) => c.phone ?? "—" }
```
Replace with:
```tsx
{
  header: "Phone",
  cell: (c) =>
    c.phone ?? (
      <span aria-label="Not provided" className="text-muted-foreground/50">
        —
      </span>
    ),
}
```

Screen readers skip or misread a bare em-dash. `aria-label="Not provided"` gives them the correct announcement.

---

### C3 — Create ClientListPage test file

**Create:** `frontend/src/features/clients/__tests__/ClientListPage.test.tsx`

Read `ClientListPage.tsx` first to confirm the exact hook name, type name, `reducerPath`, and GET endpoint URL before writing the test file.

**Seed data** (adjust field names to match the actual `ClientResponse` type):
```ts
const CLIENT_A = {
  id:        "client-0001",
  studioId:  "stud-0001",
  firstName: "João",
  lastName:  "Silva",
  email:     "joao@test.com",
  phone:     "+351912345678",
  createdAt: "2024-01-01T00:00:00Z",
  updatedAt: "2024-01-01T00:00:00Z",
};

const CLIENT_B = {
  id:        "client-0002",
  studioId:  "stud-0001",
  firstName: "Maria",
  lastName:  "Ferreira",
  email:     "maria@test.com",
  phone:     null,
  createdAt: "2024-01-02T00:00:00Z",
  updatedAt: "2024-01-02T00:00:00Z",
};
```

Type-annotate with the actual `ClientResponse` type once you've confirmed it.

**MSW server** — default returns `[CLIENT_A, CLIENT_B]`:
```ts
const server = setupServer(
  http.get("http://localhost/api/v1/clients", () =>
    HttpResponse.json([CLIENT_A, CLIENT_B]),
  ),
);
```

**Store / render helpers:**
```ts
function makeStore() {
  return configureStore({
    reducer: {
      auth: authReducer,
      ui:   uiReducer,
      [clientsApi.reducerPath]: clientsApi.reducer,
    },
    middleware: (gd) => gd().concat(clientsApi.middleware),
    preloadedState: {
      auth: {
        user: { id: "u1", email: "owner@ink.test" },
        token: "fake-token",
        tenantId: "stud-0001",
        role: "owner",
        pendingReferralCode: null,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      } as any,
      ui: { readOnlyError: null, sessionExpired: false },
    },
  });
}

function renderPage() {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/clients"]}>
        <Routes>
          <Route path="/clients"     element={<ClientListPage />} />
          <Route path="/clients/:id" element={<div data-testid="client-detail" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}
```

**Tests** (all in `describe("ClientListPage", () => { ... })`):

```ts
it("renders the Clients page heading", async () => {
  renderPage();
  expect(await screen.findByRole("heading", { name: /clients/i })).toBeInTheDocument();
});

it("does not show client names while loading", () => {
  renderPage();
  expect(screen.queryByText("João Silva")).not.toBeInTheDocument();
});

it("renders client full names", async () => {
  renderPage();
  expect(await screen.findByText("João Silva")).toBeInTheDocument();
  expect(screen.getByText("Maria Ferreira")).toBeInTheDocument();
});

it("renders initials avatar for each client", async () => {
  renderPage();
  await screen.findByText("João Silva");
  expect(screen.getByText("JS")).toBeInTheDocument();
  expect(screen.getByText("MF")).toBeInTheDocument();
});

it("renders the phone number when present", async () => {
  renderPage();
  await screen.findByText("João Silva");
  expect(screen.getByText("+351912345678")).toBeInTheDocument();
});

it("renders an accessible em-dash when phone is null", async () => {
  renderPage();
  await screen.findByText("Maria Ferreira");
  expect(screen.getByLabelText("Not provided")).toBeInTheDocument();
});

it("clicking a client row navigates to /clients/:id", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("João Silva");

  await user.click(screen.getByText("João Silva"));

  expect(screen.getByTestId("client-detail")).toBeInTheDocument();
});

it("search input is present on the page", async () => {
  renderPage();
  await screen.findByText("João Silva");
  expect(screen.getByPlaceholderText(/search/i)).toBeInTheDocument();
});

it("shows an error message when clients fetch fails", async () => {
  server.use(
    http.get("http://localhost/api/v1/clients", () =>
      HttpResponse.json({ message: "Server error" }, { status: 500 }),
    ),
  );
  renderPage();
  expect(await screen.findByText(/failed to load/i)).toBeInTheDocument();
});

it("shows empty state when no clients are returned", async () => {
  server.use(
    http.get("http://localhost/api/v1/clients", () =>
      HttpResponse.json([]),
    ),
  );
  renderPage();
  // Check ClientListPage.tsx for the exact emptyMessage prop string
  await screen.findByText(/no clients/i);
});
```

**C3 — Verify Clients**
```bash
pnpm tsc --noEmit
pnpm test src/features/clients
```

---

## Final verification — all three features together

```bash
cd frontend
pnpm tsc --noEmit
pnpm test src/features/dashboard src/features/artists src/features/clients
```

Expected: zero TypeScript errors, all tests pass.
