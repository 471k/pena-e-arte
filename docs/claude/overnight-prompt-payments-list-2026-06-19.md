# Overnight Prompt — Payments List Overhaul
> Date: 2026-06-19
> Target files: `PaymentListPage.tsx`, `PaymentListPage.test.tsx`
> No new npm or NuGet packages. No new backend changes.

---

## Pre-flight

1. Read `CLAUDE.md` and `docs/claude/frontend.md` before making any changes.
2. Run `pnpm tsc --noEmit` — note any pre-existing errors; do not count them as regressions.
3. Run `pnpm test src/features/payments/PaymentListPage` — confirm all 12 existing tests pass first.

---

## Context

The audit was run against an older build. The current state of
`frontend/src/features/payments/components/PaymentListPage.tsx` (180 lines) already has:
- ✅ `PaymentRowSkeleton` and 8-row loading state
- ✅ Error state
- ✅ Cursor-based "Load more" pagination
- ✅ Row click → `/payments/:appointmentId`
- ✅ `PaymentStatusBadge` with semantic colors (yellow/orange/blue/green/slate/red)

**What is still missing and in scope for this prompt:**

| Issue | Fix |
|---|---|
| `PaymentRowSkeleton` only has 4 bars for a 6-column table | Update to mirror actual row layout |
| Counter shows "{n} loaded" — developer jargon | Change to "{n} payments" |
| `CashPending` badge renders raw enum string | Add display labels record; show "Cash Pending" |
| Column order: Status before Amount buries the key datum | Reorder: Client → Session Date → Amount → Status → Method → Date Paid |
| Column headers: "Session", "Paid" are ambiguous | Rename to "Session Date" and "Date Paid" |
| No client-name search | Add client-side text search over `allPayments` |
| No status filter | Add client-side status filter pills over `allPayments` |
| No visible row action affordance | Add "View →" button column (stopPropagation on container) |
| Empty state is DataTable's plain string | Replace with rich branded empty state when `allPayments.length === 0` |

**Key data shapes (read-only — no backend changes):**

```ts
// payment.types.ts
interface PaymentResponse {
  id, appointmentId, amount: number,
  status: PaymentStatus,       // Pending | CashPending | Captured | Paid | Refunded | Failed
  method: PaymentMethod,       // Card | Cash
  clientName: string,
  appointmentDate: string | null,
  paidAt: string | null,
  ...
}
// GetPaymentsParams only has lastSeenId and pageSize — no search/filter params on the backend
// Client-name search and status filter MUST be client-side over loaded records
```

**Out of scope for this prompt:**
- Financial summary KPI cards — no aggregation endpoint exists
- Date-range filter — no backend support in `GetPaymentsParams`
- Per-row refund action — `refundPayment` mutation exists but the confirmation UX belongs in
  the detail page, not an inline table action
- Column sorting — DataTable does not support it; adding sort would require a new component

---

## Part 1 — `PaymentStatusBadge` — fix display labels

Add a `STATUS_LABELS` record above `PAYMENT_STATUS_STYLES` so the raw enum string is never
shown to the user directly:

```tsx
const STATUS_LABELS: Record<PaymentStatus, string> = {
  [PaymentStatus.Pending]:     "Pending",
  [PaymentStatus.CashPending]: "Cash Pending",
  [PaymentStatus.Captured]:    "Captured",
  [PaymentStatus.Paid]:        "Paid",
  [PaymentStatus.Refunded]:    "Refunded",
  [PaymentStatus.Failed]:      "Failed",
};
```

Update `PaymentStatusBadge` to use it:

```tsx
function PaymentStatusBadge({ status }: { status: PaymentStatus }) {
  return (
    <Badge variant="outline" className={cn(PAYMENT_STATUS_STYLES[status])}>
      {STATUS_LABELS[status]}
    </Badge>
  );
}
```

---

## Part 2 — `PaymentRowSkeleton` — match actual row shape

The existing skeleton has only 4 bars. The rendered row has 7 columns (6 data + 1 action).
Replace:

```tsx
function PaymentRowSkeleton() {
  return (
    <div
      className="flex items-center gap-4 py-3 border-b"
      aria-hidden="true"
    >
      <Skeleton className="h-4 w-28 flex-1" />          {/* Client */}
      <Skeleton className="h-4 w-20" />                 {/* Session Date */}
      <Skeleton className="h-4 w-16 font-semibold" />   {/* Amount */}
      <Skeleton className="h-5 w-24 rounded-full" />    {/* Status badge */}
      <Skeleton className="h-4 w-10" />                 {/* Method */}
      <Skeleton className="h-4 w-20" />                 {/* Date Paid */}
      <Skeleton className="h-7 w-14 rounded-md" />      {/* Actions */}
    </div>
  );
}
```

---

## Part 3 — New state and derived values in `PaymentListPage`

### 3a — New imports

Add `useMemo` to the existing react import:

```tsx
import { useMemo, useState } from "react";
```

Add to lucide imports:

```tsx
import { ChevronRight, CreditCard, Loader2, Plus, Search } from "lucide-react";
```

Add `Input`:

```tsx
import { Input } from "@/shared/components/ui/input";
```

### 3b — Search and filter state

Add immediately before the `return` statement (after the existing `hasMore` computation):

```tsx
const [search,       setSearch]       = useState("");
const [statusFilter, setStatusFilter] = useState<PaymentStatus | null>(null);

// Status values that actually appear in the loaded records — used to build filter pills
const presentStatuses = useMemo<PaymentStatus[]>(() => {
  const set = new Set<PaymentStatus>();
  allPayments.forEach((p) => set.add(p.status));
  return [...set];
}, [allPayments]);

// Client-side filter over ALL loaded pages combined.
// Note: this only covers loaded records — search does not trigger additional API fetches.
const filteredPayments = useMemo<PaymentResponse[]>(() => {
  let result = allPayments;
  const term = search.trim().toLowerCase();
  if (term) {
    result = result.filter((p) => p.clientName.toLowerCase().includes(term));
  }
  if (statusFilter) {
    result = result.filter((p) => p.status === statusFilter);
  }
  return result;
}, [allPayments, search, statusFilter]);

const tableEmptyMessage = search
  ? `No payments match "${search}".`
  : statusFilter
  ? `No ${STATUS_LABELS[statusFilter]} payments found.`
  : "No payments yet.";
```

---

## Part 4 — Update the header counter copy

Change:

```tsx
{allPayments.length > 0 && (
  <span className="text-xs text-muted-foreground">{allPayments.length} loaded</span>
)}
```

to:

```tsx
{allPayments.length > 0 && (
  <span className="text-xs text-muted-foreground">
    {allPayments.length} payment{allPayments.length !== 1 ? "s" : ""}
  </span>
)}
```

---

## Part 5 — Add search input + status filter pills to `<main>`

Insert immediately after the `<main ...>` opening tag, before the `{isLoading && ...}` block:

```tsx
{/* Search */}
<div className="relative">
  <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
  <Input
    placeholder="Search by client name…"
    value={search}
    onChange={(e) => setSearch(e.target.value)}
    className="pl-9"
  />
</div>

{/* Status filter pills — only shown when loaded data contains multiple statuses */}
{!isLoading && !isError && presentStatuses.length > 1 && (
  <div
    className="flex flex-wrap items-center gap-2"
    aria-label="Filter by payment status"
  >
    {presentStatuses.map((s) => (
      <button
        key={s}
        type="button"
        aria-pressed={statusFilter === s}
        onClick={() => setStatusFilter(statusFilter === s ? null : s)}
        className={cn(
          "rounded-full border px-3 py-0.5 text-xs font-medium transition-colors",
          statusFilter === s
            ? "border-foreground bg-foreground text-background"
            : "border-border bg-background text-muted-foreground hover:border-foreground hover:text-foreground",
        )}
      >
        {STATUS_LABELS[s]}
      </button>
    ))}
  </div>
)}
```

---

## Part 6 — Rich empty state + updated DataTable

Replace the current single conditional block:

```tsx
{!isLoading && !isError && (
  <DataTable<PaymentResponse> ... />
)}
```

with two separate blocks:

```tsx
{/* Rich empty state — zero payments loaded at all */}
{!isLoading && !isError && allPayments.length === 0 && (
  <div className="flex flex-col items-center gap-3 py-16 text-center">
    <CreditCard className="h-8 w-8 text-muted-foreground/40" />
    <p className="text-sm font-medium">No payments yet</p>
    <p className="text-xs text-muted-foreground">
      Record your first payment to start tracking studio revenue.
    </p>
    <Button
      size="sm"
      onClick={() => navigate("/payments/new")}
      className="gap-1.5 mt-1"
    >
      <Plus className="h-3.5 w-3.5" />
      Record payment
    </Button>
  </div>
)}

{/* Table — when payments are loaded (search/filter applies to filteredPayments) */}
{!isLoading && !isError && allPayments.length > 0 && (
  <DataTable<PaymentResponse>
    columns={[
      {
        header: "Client",
        cell: (p) => (
          <span className="text-sm font-medium">
            {p.clientName || <span className="text-muted-foreground">—</span>}
          </span>
        ),
      },
      {
        header: "Session Date",
        cell: (p) =>
          p.appointmentDate ? (
            <span className="text-sm text-muted-foreground">
              {formatDate(p.appointmentDate)}
            </span>
          ) : (
            "—"
          ),
      },
      {
        header: "Amount",
        cell: (p) => (
          <span className="font-semibold">{formatCurrency(p.amount)}</span>
        ),
      },
      {
        header: "Status",
        cell: (p) => <PaymentStatusBadge status={p.status} />,
      },
      {
        header: "Method",
        cell: (p) => p.method,
      },
      {
        header: "Date Paid",
        cell: (p) => (p.paidAt ? formatDate(p.paidAt) : "—"),
      },
      {
        header: "",
        cell: (p) => (
          <div
            className="flex items-center justify-end"
            onClick={(e) => e.stopPropagation()}
          >
            <Button
              variant="ghost"
              size="sm"
              className="h-7 text-xs gap-1 text-muted-foreground hover:text-foreground"
              onClick={() => navigate(`/payments/${p.appointmentId}`)}
            >
              View
              <ChevronRight className="h-3 w-3" />
            </Button>
          </div>
        ),
      },
    ]}
    data={filteredPayments}
    keyExtractor={(p) => p.id}
    onRowClick={(p) => navigate(`/payments/${p.appointmentId}`)}
    emptyMessage={tableEmptyMessage}
  />
)}
```

Note the column order change: Client → **Session Date** → **Amount** → **Status** → Method → **Date Paid** → (actions).

---

## Part 7 — Update `PaymentListPage.test.tsx`

### 7a — Three existing tests to UPDATE

**Test 1** — empty state text changed (rich state now shows instead of DataTable's plain string):

```ts
// BEFORE
it("shows 'No payments yet.' when the list is empty", async () => {
  renderPage();
  expect(await screen.findByText("No payments yet.")).toBeInTheDocument();
});

// AFTER
it("shows rich empty state when no payments exist", async () => {
  renderPage(); // default handler returns []
  expect(await screen.findByText("No payments yet")).toBeInTheDocument();
  expect(screen.getByText(/record your first payment/i)).toBeInTheDocument();
});
```

**Test 2** — counter copy changed:

```ts
// BEFORE
it("shows '20 loaded' counter with a full page", async () => {
  ...
  expect(screen.getByText("20 loaded")).toBeInTheDocument();
});

// AFTER
it("shows '20 payments' counter with a full page", async () => {
  ...
  expect(screen.getByText("20 payments")).toBeInTheDocument();
});
```

**Test 3** — "CashPending" badge now displays as "Cash Pending":

```ts
// BEFORE
expect(screen.getByText("CashPending")).toBeInTheDocument();

// AFTER
expect(screen.getByText("Cash Pending")).toBeInTheDocument();
```

### 7b — Add `waitFor` to the testing-library import if not already present

```ts
import { render, screen, cleanup, within, waitFor } from "@testing-library/react";
```

### 7c — New tests to append inside `describe("PaymentListPage", ...)`

```ts
// ── Rich empty state ──────────────────────────────────────────────────────────

it("rich empty state shows a Record payment CTA button", async () => {
  renderPage(); // default handler returns []
  await screen.findByText("No payments yet");
  expect(screen.getByRole("button", { name: /record payment/i })).toBeInTheDocument();
});

it("rich empty state Record payment button navigates to /payments/new", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("No payments yet");
  await user.click(screen.getByRole("button", { name: /record payment/i }));
  expect(screen.getByTestId("new-page")).toBeInTheDocument();
});

// ── View action button ────────────────────────────────────────────────────────

it("renders a View button for each loaded payment", async () => {
  server.use(
    http.get("http://localhost/api/v1/payments", () =>
      HttpResponse.json([PAYMENT_CARD, PAYMENT_CASH]),
    ),
  );
  renderPage();
  await screen.findByText("Maria Silva");
  expect(screen.getAllByRole("button", { name: /^view$/i })).toHaveLength(2);
});

it("clicking the View button navigates to /payments/:appointmentId", async () => {
  server.use(
    http.get("http://localhost/api/v1/payments", () =>
      HttpResponse.json([PAYMENT_CARD]),
    ),
  );
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Maria Silva");
  await user.click(screen.getByRole("button", { name: /^view$/i }));
  expect(screen.getByTestId("detail-page")).toBeInTheDocument();
});

// ── Client-name search ────────────────────────────────────────────────────────

it("search input is present on the page", async () => {
  server.use(
    http.get("http://localhost/api/v1/payments", () =>
      HttpResponse.json([PAYMENT_CARD, PAYMENT_CASH]),
    ),
  );
  renderPage();
  await screen.findByText("Maria Silva");
  expect(screen.getByPlaceholderText(/search by client name/i)).toBeInTheDocument();
});

it("typing a client name filters the visible payments", async () => {
  server.use(
    http.get("http://localhost/api/v1/payments", () =>
      HttpResponse.json([PAYMENT_CARD, PAYMENT_CASH]),
    ),
  );
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Maria Silva");

  await user.type(screen.getByPlaceholderText(/search by client name/i), "Maria");

  expect(screen.getByText("Maria Silva")).toBeInTheDocument();
  expect(screen.queryByText("João Santos")).not.toBeInTheDocument();
});

it("search is case-insensitive", async () => {
  server.use(
    http.get("http://localhost/api/v1/payments", () =>
      HttpResponse.json([PAYMENT_CARD, PAYMENT_CASH]),
    ),
  );
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("João Santos");

  await user.type(screen.getByPlaceholderText(/search by client name/i), "joão");

  expect(screen.getByText("João Santos")).toBeInTheDocument();
  expect(screen.queryByText("Maria Silva")).not.toBeInTheDocument();
});

// ── Status filter pills ───────────────────────────────────────────────────────

it("status filter pills appear when multiple statuses are present", async () => {
  server.use(
    http.get("http://localhost/api/v1/payments", () =>
      HttpResponse.json([PAYMENT_CARD, PAYMENT_CASH]),
    ),
  );
  renderPage();
  await screen.findByText("Maria Silva");

  // PAYMENT_CARD is "Paid", PAYMENT_CASH is "CashPending" (displayed as "Cash Pending")
  expect(screen.getByRole("button", { name: "Paid" })).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Cash Pending" })).toBeInTheDocument();
});

it("clicking a status filter pill shows only matching payments", async () => {
  server.use(
    http.get("http://localhost/api/v1/payments", () =>
      HttpResponse.json([PAYMENT_CARD, PAYMENT_CASH]),
    ),
  );
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Maria Silva");

  await user.click(screen.getByRole("button", { name: "Paid" }));

  expect(screen.getByText("Maria Silva")).toBeInTheDocument();   // Paid
  expect(screen.queryByText("João Santos")).not.toBeInTheDocument(); // CashPending
});

it("clicking the active status pill again clears the filter", async () => {
  server.use(
    http.get("http://localhost/api/v1/payments", () =>
      HttpResponse.json([PAYMENT_CARD, PAYMENT_CASH]),
    ),
  );
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Maria Silva");

  await user.click(screen.getByRole("button", { name: "Paid" }));
  expect(screen.queryByText("João Santos")).not.toBeInTheDocument();

  await user.click(screen.getByRole("button", { name: "Paid" }));
  expect(screen.getByText("João Santos")).toBeInTheDocument();
});
```

---

## Part 8 — Verify

```bash
# TypeScript — must be clean (no new errors beyond pre-existing)
pnpm tsc --noEmit

# Payments list tests — all must pass (12 original → 3 updated + 11 new = 23 total)
pnpm test src/features/payments/PaymentListPage --run

# Smoke-check other payments tests are not broken
pnpm test src/features/payments --run

# Full suite smoke-check
pnpm test --run
```

If `pnpm test --run` surfaces failures in unrelated files, note them and move on.

---

## Constraints (from CLAUDE.md)

- Do NOT add new npm packages — `Input`, `Skeleton`, `Badge`, `Button`, `Search`,
  `ChevronRight` are all already in the project.
- No useEffect for data fetching. Search and filter state is derived via `useMemo` only.
  No additional API calls are triggered by typing in the search box.
- TypeScript strict mode — no `any`, explicit types everywhere.
- No default exports on components.
- Do NOT add per-row refund/capture actions — confirmation UX belongs in the detail page.
- Do NOT add financial KPI summary cards — no aggregation endpoint exists.
- Do NOT add date-range filter — `GetPaymentsParams` has no date params.
