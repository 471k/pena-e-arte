# Overnight Prompt — Dashboard Overhaul (KPI Widgets + Nav Cleanup)
**Date:** 2026-06-19
**Scope:** Frontend only — `OwnerLayout.tsx`, `DashboardPage.tsx`, and their test files.
**Packages:** Do NOT add any new npm packages.

---

## Pre-flight

Read in order before touching any file:
1. `CLAUDE.md`
2. `docs/claude/frontend.md`
3. `docs/claude/conventions.md`

Then read all four source files:
- `frontend/src/layouts/OwnerLayout.tsx`
- `frontend/src/layouts/__tests__/OwnerLayout.test.tsx`
- `frontend/src/features/dashboard/components/DashboardPage.tsx`
- `frontend/src/features/dashboard/__tests__/DashboardPage.test.tsx`

Understand the current state of each file before applying any change.

---

## Context

The dashboard main content area is a 8-tile shortcut grid that duplicates most of the top navigation. This makes the content area feel redundant and wastes the screen. The audit identifies three critical fixes:

1. **Navigation cleanup** — "Schedule" is currently only reachable via the tile grid (not in the top nav), and the top nav has both a "Notifications" item and a `NotificationBell` component in the header right-side, which appear adjacent on screen. Fix: Add Schedule to the nav, remove the Notifications nav item (the bell component already handles it).

2. **Remove the tile grid** — Replace it with 3 KPI stat cards that surface real operational data: appointments today, appointments this week, and deposits outstanding from week appointments.

3. **Primary action CTA** — There is no "Book Appointment" button visible when the schedule is populated. Add one to the dashboard page header so it is always present.

---

## Part 1 — OwnerLayout.tsx

**File:** `frontend/src/layouts/OwnerLayout.tsx`

### 1a — Add Schedule to NAV_ITEMS

Import `CalendarDays` from lucide-react (add to the existing import line).

Insert a Schedule item as the **second** item in `NAV_ITEMS` (immediately after Dashboard):
```ts
{ label: "Schedule", href: "/schedule", icon: <CalendarDays className="h-4 w-4" /> },
```

The final order should be: Dashboard → Schedule → Artists → Clients → Designs → Payments → Billing → Studio Settings → Notifications.

### 1b — Remove Notifications from NAV_ITEMS

Remove the `{ label: "Notifications", ... }` entry from `NAV_ITEMS`. The `NotificationBell` component already rendered in the header's right-side `div` serves as the notification access point with badge count. Having both is redundant and creates a visual duplicate.

After removing the Notifications entry, the final `NAV_ITEMS` order is:
Dashboard → Schedule → Artists → Clients → Designs → Payments → Billing → Studio Settings

If `Bell` is imported in `OwnerLayout.tsx` **only** for the NAV_ITEMS icon and is no longer used anywhere else in the file after this removal, remove `Bell` from the lucide-react import line.

---

## Part 2 — DashboardPage.tsx

**File:** `frontend/src/features/dashboard/components/DashboardPage.tsx`

### 2a — Add DepositStatus import

`DashboardPage.tsx` currently imports `AppointmentResponse` as a type-only import. Add a runtime import for `DepositStatus` so it can be used in a filter expression:

```tsx
import { DepositStatus } from "@/features/appointments/appointment.types";
```

(The existing `import type { AppointmentResponse }` stays unchanged. This is a separate, non-`type` import.)

### 2b — Add weekly appointments query

In the `DashboardPage` component body, after the existing `todayAppts` query, add:

```tsx
const weekEnd = useMemo(() => addDays(todayStart, 7), [todayStart]);
const {
  data:      weekAppts,
  isLoading: loadingWeekAppts,
} = useGetAppointmentsQuery({
  from: todayStart.toISOString(),
  to:   weekEnd.toISOString(),
});
```

Then derive the pending deposits count:
```tsx
const pendingDeposits = useMemo(
  () => weekAppts?.filter((a) => a.depositStatus === DepositStatus.Pending).length ?? 0,
  [weekAppts],
);
```

`addDays` already exists as a module-level helper in this file.

### 2c — Add navigate to DashboardPage

The `DashboardPage` component currently does not call `useNavigate()` directly (it delegates navigate to sub-components). Check whether `navigate` is already declared at the top of `DashboardPage`. If not, add:
```tsx
const navigate = useNavigate();
```
at the start of the `DashboardPage` component body. `useNavigate` is already imported.

### 2d — Add primary CTA to the page header

The `DashboardPage` renders its own inner `<header>` element (below the OwnerLayout nav) showing the "Dashboard" label and today's date. Update it to include a "+ Book Appointment" button:

Find the header block (it contains `<LayoutDashboard className="h-5 w-5" />` and `<span className="font-semibold tracking-tight">Dashboard</span>`).

Replace the right side of the header from:
```tsx
<span className="text-xs text-muted-foreground">{formatDate(today)}</span>
```
to a flex row:
```tsx
<div className="flex items-center gap-3">
  <Button size="sm" onClick={() => navigate("/appointments/new")}>
    + Book Appointment
  </Button>
  <span className="text-xs text-muted-foreground">{formatDate(today)}</span>
</div>
```

`Button` is already imported. `navigate` is available from step 2c.

### 2e — Define StatCard component

Add this module-level function (not exported) **before** the `DashboardPage` component:

```tsx
interface StatCardProps {
  label:     string;
  value:     number;
  icon:      React.ReactNode;
  isLoading: boolean;
  testId?:   string;
}

function StatCard({ label, value, icon, isLoading, testId }: StatCardProps) {
  return (
    <div
      className="rounded-xl border border-border bg-card p-4 flex flex-col gap-1"
      data-testid={testId}
    >
      <div className="flex items-center gap-1.5 text-xs font-medium text-muted-foreground">
        {icon}
        {label}
      </div>
      {isLoading ? (
        <Skeleton className="h-8 w-12 mt-1" />
      ) : (
        <span className="text-3xl font-bold tabular-nums">{value}</span>
      )}
    </div>
  );
}
```

`Skeleton` is already imported in `DashboardPage.tsx`.

### 2f — Remove QuickNav

Delete the following blocks entirely from `DashboardPage.tsx`:
- The `interface NavTile { ... }` type
- The `const NAV_TILES: NavTile[] = [...]` constant
- The `function QuickNav() { ... }` component

Remove `<QuickNav />` from the `DashboardPage` JSX (it is currently the last child inside `<main>`).

After removing QuickNav, check which lucide-react icons are **no longer used anywhere** in `DashboardPage.tsx` and remove them from the import line. Icons that may become unused: `BookOpen`, `Scroll`, `ScrollText`, `CreditCard`, `Bell`, `Users`. Do not remove `CalendarDays`, `Banknote`, `LayoutDashboard`, or `ChevronRight` as they are used in TodaySection and the page header.

### 2g — Add KPI stat cards row to DashboardPage JSX

Inside the `<main>` element, add a stat cards row **immediately after** the SubscriptionBanner line and **before** `<TodaySection ...>`:

```tsx
{/* KPI stat cards */}
<div className="grid grid-cols-3 gap-3">
  <StatCard
    label="Today"
    value={todayAppts?.length ?? 0}
    icon={<CalendarDays className="h-3.5 w-3.5" />}
    isLoading={loadingAppts}
    testId="stat-today"
  />
  <StatCard
    label="This Week"
    value={weekAppts?.length ?? 0}
    icon={<CalendarDays className="h-3.5 w-3.5" />}
    isLoading={loadingWeekAppts}
    testId="stat-week"
  />
  <StatCard
    label="Deposits Due"
    value={pendingDeposits}
    icon={<Banknote className="h-3.5 w-3.5" />}
    isLoading={loadingWeekAppts}
    testId="stat-deposits"
  />
</div>
```

`CalendarDays` and `Banknote` are already imported.

### 2h — Update "Full schedule" button copy

In `TodaySection`, find the Button that reads "Full schedule". Change the text node from:
```tsx
Full schedule
```
to:
```tsx
View schedule
```

The `<ChevronRight className="h-3 w-3" />` icon immediately after it already provides the visual arrow affordance. Only the text changes.

### 2i — Differentiate the header "Book Appointment" button text

To avoid two identically labelled "Book Appointment" elements on screen when the appointment list is empty (one in the header, one in the empty state), the header button added in step 2d uses `"+ Book Appointment"` (with the `+` prefix). The empty state button inside `TodaySection` keeps the label `"Book Appointment"` (no `+`).

Confirm the empty state button reads exactly `"Book Appointment"` — if it was previously labelled differently, correct it now.

---

## Part 3 — DashboardPage.test.tsx

**File:** `frontend/src/features/dashboard/__tests__/DashboardPage.test.tsx`

### 3a — Add `within` import

Add `within` to the `@testing-library/react` import:
```ts
import { render, screen, cleanup, waitFor, within } from "@testing-library/react";
```

Remove `waitFor` if it is not already used elsewhere in the file. Add `within`.

### 3b — Add `/schedule` route to renderPage

The `renderPage` helper's `<Routes>` block already has a `<Route path="/schedule" ...>` — confirm it is present. If it is missing, add:
```tsx
<Route path="/schedule" element={<div data-testid="schedule-page" />} />
```

### 3c — Fix loading state test

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

(This test may already be correct if the previous prompt already ran. Check first.)

### 3d — Delete QuickNav tiles test

Find and **delete** the entire test block:
```ts
it("renders all 8 quick-nav tiles", async () => {
  ...
});
```

The tile grid has been removed. This test is no longer valid.

### 3e — Update "Full schedule" → "View schedule" test

Find the test that clicks the full schedule button:
```ts
it("Full schedule button navigates to /schedule", async () => {
  ...
  await user.click(screen.getByRole("button", { name: /full schedule/i }));
  ...
});
```

Update:
- Test description: `"Full schedule button navigates to /schedule"` → `"View schedule button navigates to /schedule"`
- Button selector: `{ name: /full schedule/i }` → `{ name: /view schedule/i }`

### 3f — Fix the "Book Appointment" tests for header button vs empty-state button

The dashboard now has two "Book Appointment"-related buttons when the appointment list is empty:
- Header button: `"+ Book Appointment"` (always visible)
- Empty state button: `"Book Appointment"` (only when no appointments)

Update or add tests accordingly:

```ts
// ── Header CTA ──────────────────────────────────────────────────────────────

it("header shows '+ Book Appointment' button always", async () => {
  // With appointments present, the empty state is gone but header CTA stays
  server.use(
    http.get("http://localhost/api/v1/appointments", () =>
      HttpResponse.json([APPOINTMENT]),
    ),
  );
  renderPage();
  await screen.findByText("Ana Costa");
  expect(screen.getByRole("button", { name: /\+ book appointment/i })).toBeInTheDocument();
});

it("header '+ Book Appointment' button navigates to /appointments/new", async () => {
  const user = userEvent.setup();
  renderPage();
  // Wait for page to settle (no appointments)
  await screen.findByText("No appointments today.");

  await user.click(screen.getByRole("button", { name: /\+ book appointment/i }));

  expect(screen.getByTestId("new-appointment-page")).toBeInTheDocument();
});
```

If the previous prompt added tests for the empty-state "Book Appointment" button using `getByRole("button", { name: /book appointment/i })`, those tests will now find **two** matching elements and throw. Update them to use the more specific `getAllByRole` and target index `[1]` (the empty state button), or change them to target the empty state button by a different label. The simplest fix is:

For the empty state CTA test, change the selector from `{ name: /book appointment/i }` to `{ name: /^Book Appointment$/i }` (exact match, no `+`). This will match only the empty state button, not the header button.

The "Book Appointment" button that navigates test should also be updated to use the exact match:
```ts
await user.click(screen.getByRole("button", { name: /^Book Appointment$/i }));
```

### 3g — Add KPI stat card tests

Add these tests in a new `// ── KPI stat cards ──` section:

```ts
// ── KPI stat cards ──────────────────────────────────────────────────────────

it("stat cards section renders Today, This Week, and Deposits Due labels", async () => {
  renderPage();
  await screen.findByText("No appointments today.");
  expect(screen.getByTestId("stat-today")).toBeInTheDocument();
  expect(screen.getByTestId("stat-week")).toBeInTheDocument();
  expect(screen.getByTestId("stat-deposits")).toBeInTheDocument();
});

it("Today stat shows 0 when no appointments", async () => {
  renderPage();
  await screen.findByText("No appointments today.");
  expect(within(screen.getByTestId("stat-today")).getByText("0")).toBeInTheDocument();
});

it("Today stat shows correct count when appointments exist", async () => {
  server.use(
    http.get("http://localhost/api/v1/appointments", () =>
      HttpResponse.json([APPOINTMENT]),
    ),
  );
  renderPage();
  await screen.findByText("Ana Costa");
  expect(within(screen.getByTestId("stat-today")).getByText("1")).toBeInTheDocument();
});

it("stat cards show skeleton while appointments are loading", () => {
  renderPage();
  // Before data arrives, stat-today should show a skeleton (not the number)
  const todayCard = screen.getByTestId("stat-today");
  // The Skeleton component renders — the number span is absent
  expect(within(todayCard).queryByRole("heading")).not.toBeInTheDocument();
  // And no numeric text yet
  expect(within(todayCard).queryByText("0")).not.toBeInTheDocument();
});

it("Deposits Due stat shows count of Pending-deposit appointments", async () => {
  const pendingDepositAppt: AppointmentResponse = {
    ...APPOINTMENT,
    id:            "appt-deposit-pending",
    depositStatus: "Pending",
  };
  server.use(
    http.get("http://localhost/api/v1/appointments", () =>
      HttpResponse.json([pendingDepositAppt]),
    ),
  );
  renderPage();
  await screen.findByText("Ana Costa");
  expect(within(screen.getByTestId("stat-deposits")).getByText("1")).toBeInTheDocument();
});
```

---

## Part 4 — OwnerLayout.test.tsx

**File:** `frontend/src/layouts/__tests__/OwnerLayout.test.tsx`

### 4a — Add `/schedule` route to renderLayout

In the `renderLayout` helper's `<Routes>` block, add a Schedule route:
```tsx
<Route path="/schedule" element={<div data-testid="outlet" />} />
```

### 4b — Update the nav links test

Find:
```ts
it("renders all eight owner nav links", () => {
  renderLayout();
  expect(screen.getByRole("link", { name: /^dashboard$/i })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: /^artists$/i })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: /^clients$/i })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: /^designs$/i })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: /^payments$/i })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: /^billing$/i })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: /studio settings/i })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: /notifications/i })).toBeInTheDocument();
});
```

Replace with:
```ts
it("renders all eight owner nav links", () => {
  renderLayout();
  expect(screen.getByRole("link", { name: /^dashboard$/i })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: /^schedule$/i })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: /^artists$/i })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: /^clients$/i })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: /^designs$/i })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: /^payments$/i })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: /^billing$/i })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: /studio settings/i })).toBeInTheDocument();
});
```

Changes: added Schedule, removed Notifications. The count stays at eight.

### 4c — Add test confirming Notifications is not a nav link

Add a test asserting the Notifications nav item has been removed (access is via the header bell icon only):

```ts
it("Notifications is not a top-nav link (access via header bell icon)", () => {
  renderLayout();
  // The NotificationBell in the header header handles notifications — no nav link
  expect(screen.queryByRole("link", { name: /^notifications$/i })).not.toBeInTheDocument();
});
```

---

## Part 5 — Verify

```bash
cd frontend
pnpm tsc --noEmit
pnpm test src/layouts/OwnerLayout
pnpm test src/features/dashboard
```

Expected outcomes:
- `OwnerLayout.test.tsx` — all existing tests pass; the nav now has Schedule and lacks Notifications.
- `DashboardPage.test.tsx` — tile test is deleted; loading test checks skeleton; "View schedule" button test passes; 5 new KPI stat card tests pass; header CTA tests pass.
- Zero TypeScript errors.
- The `DashboardPage` renders: header with "+ Book Appointment" CTA → SubscriptionBanner (conditional) → 3 KPI stat cards → TodaySection → CashPendingSection.
- No `QuickNav` component or `NAV_TILES` constant remains anywhere in `DashboardPage.tsx`.
