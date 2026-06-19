# Overnight Prompt — Subscription Oversight Page Overhaul (2026-06-18)

> **Scope:** Complete UI/UX overhaul of `SubscriptionOversightPage.tsx`.
> This is the Issuer's subscription management view at `/platform/subscriptions`.
> No backend changes. No new npm packages.
> Commit after each numbered task.

---

## 0. Mandatory Reading (Do This First)

```
CLAUDE.md
docs/claude/frontend.md
docs/claude/conventions.md
```

Then read these source files before touching anything:

```
frontend/src/features/platform/components/SubscriptionOversightPage.tsx
frontend/src/features/platform/__tests__/SubscriptionOversightPage.test.tsx
frontend/src/features/platform/platform.types.ts
frontend/src/features/platform/platformApi.ts
```

---

## 1. Status Labels — Fix Raw Enum Display

**Problem:** Line 83 renders `{sub.status}` directly, so the badge shows
"GracePeriod" and "Trialing" — raw enum strings that look like code leaks.
The filter pills at line 269 also render `{s}` directly.

**Fix:** Add a `STATUS_LABELS` map (identical to `IssuerStudioListPage`) and
use it everywhere a status string is rendered to a user.

```tsx
// ── Status display config ──────────────────────────────────────────────────

const STATUS_CLASSES: Record<string, string> = {
  Active:         "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300",
  Trialing:       "bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300",
  PastDue:        "bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-300",
  GracePeriod:    "bg-orange-100 text-orange-700 dark:bg-orange-900/30 dark:text-orange-300",
  Cancelled:      "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300",
  NoSubscription: "bg-muted text-muted-foreground",
};

const STATUS_LABELS: Record<string, string> = {
  Active:         "Active",
  Trialing:       "In Trial",
  PastDue:        "Past Due",
  GracePeriod:    "Grace Period",
  Cancelled:      "Cancelled",
  NoSubscription: "No Subscription",
};
```

Replace the badge span in `SubscriptionRow`:

```tsx
// Before:
<span className={`text-xs px-1.5 py-0.5 rounded-full font-medium ${statusClass}`}>
  {sub.status}
</span>

// After:
<span className={`text-xs px-1.5 py-0.5 rounded-full font-medium ${statusClass}`}>
  {STATUS_LABELS[sub.status] ?? sub.status}
</span>
```

Update the filter pill text:

```tsx
// Before:
{s} ({count})

// After:
{STATUS_LABELS[s] ?? s} ({count})
```

**Commit:** `fix(subscriptions): use human-readable status labels, fix GracePeriod/Trialing display`

---

## 2. Fix Badge Wrapping — GracePeriod Rows Taller Than Others

**Problem:** The name row uses `flex-wrap`, so "Grace Period" (two words, wider
badge) drops to a second line, making GracePeriod cards noticeably taller and
breaking vertical rhythm.

**Fix:** Use `flex-nowrap min-w-0` on the row, `shrink-0` on the badge,
`truncate max-w-[180px]` on the slug.

```tsx
// Before:
<div className="flex items-center gap-2 flex-wrap">
  <span className="font-medium text-sm">{sub.studioName}</span>
  <span className="text-xs text-muted-foreground font-mono">{sub.studioSlug}</span>
  <span className={`text-xs px-1.5 py-0.5 rounded-full font-medium ${statusClass}`}>
    {STATUS_LABELS[sub.status] ?? sub.status}
  </span>
</div>

// After:
<div className="flex items-center gap-2 flex-nowrap min-w-0">
  <span className="font-medium text-sm shrink-0">{sub.studioName}</span>
  <span className="text-xs text-muted-foreground font-mono truncate max-w-[180px]"
        title={sub.studioSlug}>
    {sub.studioSlug}
  </span>
  <span className={`text-xs px-1.5 py-0.5 rounded-full font-medium shrink-0 ${statusClass}`}>
    {STATUS_LABELS[sub.status] ?? sub.status}
  </span>
</div>
```

**Commit:** `fix(subscriptions): prevent status badge from wrapping to second line`

---

## 3. Fix Date Format and Split Meta Line

**Problem:**
1. `toLocaleDateString("en-GB")` with no options renders "25/06/2026" — hard
   to scan in a dense list.
2. `{sub.planName ?? "No plan"} · Trial ends date · Period end date` is one
   long undifferentiated paragraph with three unrelated data points.

**Fix:** Add a `fmt` helper (same as `IssuerStudioListPage`). Split into two
lines: plan info + dates.

Add the helper above `SubscriptionRow`:

```tsx
function fmt(date: string): string {
  return new Date(date).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}
```

Replace the single `<p>` with two lines:

```tsx
// Before:
<p className="text-xs text-muted-foreground">
  {sub.planName ?? "No plan"}
  {" · "}
  Trial ends {new Date(sub.trialExpiresAt).toLocaleDateString("en-GB")}
  {" · "}
  Period end {new Date(sub.currentPeriodEnd).toLocaleDateString("en-GB")}
</p>

// After:
<div className="space-y-0.5">
  <p className="text-xs text-muted-foreground">
    {sub.status === "Trialing" ? "In Trial" : (sub.planName ?? "No paid plan")}
    {" · "}
    {trialExpired ? `Trial expired ${fmt(sub.trialExpiresAt)}` : `Trial ends ${fmt(sub.trialExpiresAt)}`}
  </p>
  <p className="text-xs text-muted-foreground">
    {periodText}
  </p>
</div>
```

Where `trialExpired` and `periodText` are computed inside `SubscriptionRow`:

```tsx
const trialExpired = new Date(sub.trialExpiresAt) < new Date();

const periodText = (() => {
  if (sub.status === "Active")       return `Renews: ${fmt(sub.currentPeriodEnd)}`;
  if (sub.status === "GracePeriod")  return `Grace ends: ${fmt(sub.currentPeriodEnd)}`;
  if (sub.status === "PastDue")      return `Overdue since: ${fmt(sub.currentPeriodEnd)}`;
  if (sub.status === "Cancelled")    return `Cancelled — expired ${fmt(sub.currentPeriodEnd)}`;
  return null; // Trialing and NoSubscription have no period end to show
})();
```

Show `periodText` only when it is not null:

```tsx
{periodText && (
  <p className="text-xs text-muted-foreground">{periodText}</p>
)}
```

**Commit:** `fix(subscriptions): readable date format, split meta line into plan + dates`

---

## 4. Action Buttons — Icons, Labels, Hierarchy, and Accessibility

This task fixes four button problems in one pass to keep the DOM structure clean.

**Problems:**
- "Extend trial" has no duration (admin clicks blind)
- "Activate — Cash Payment" is `variant="outline"` — same visual weight as "Extend trial"
- Only the Activate button has an icon (`Banknote`) — Extend and Cancel have none
- No `aria-label` on any button — screen readers announce identical button names

**Fix:** Import the missing icons at the top of the file:

```tsx
import { Banknote, Clock, Loader2, Receipt, XCircle } from "lucide-react";
```

Replace the action button block inside `SubscriptionRow`:

```tsx
<div className="flex items-center gap-1.5 shrink-0">

  {/* 1. Extend trial (outline — secondary action) */}
  {sub.status !== "Active" && !extending && !activating && !confirming && (
    <Button
      size="sm"
      variant="outline"
      className="h-7 text-xs gap-1"
      onClick={() => setExtending(true)}
      aria-label={trialExpired
        ? `Grant extension for ${sub.studioName}`
        : `Extend trial for ${sub.studioName}`}
    >
      <Clock className="h-3.5 w-3.5" />
      {trialExpired ? "Grant Extension" : "Extend Trial (+7 days)"}
    </Button>
  )}

  {/* 2. Activate (primary filled — revenue event) */}
  {canActivate && !activating && !extending && !confirming && (
    <Button
      size="sm"
      className="h-7 text-xs gap-1"
      onClick={() => setActivating(true)}
      aria-label={`Activate subscription for ${sub.studioName}`}
    >
      <Banknote className="h-3.5 w-3.5" />
      Activate
    </Button>
  )}

  {/* 3. Cancel Subscription (destructive outline — last) */}
  {canCancel && !confirming && !extending && !activating && (
    <Button
      size="sm"
      variant="outline"
      className="h-7 text-xs gap-1 text-destructive border-destructive/40
                 hover:bg-destructive/10 hover:text-destructive"
      onClick={() => setConfirming(true)}
      aria-label={`Cancel subscription for ${sub.studioName}`}
    >
      <XCircle className="h-3.5 w-3.5" />
      Cancel Subscription
    </Button>
  )}
</div>
```

**Commit:** `fix(subscriptions): action button icons, hierarchy, aria-labels, Activate as primary`

---

## 5. Extend Trial Form — Add Contextual Label

**Problem:** The extend trial form (lines 122–141) shows a bare number input
with no text explaining what value to enter. The label appears AFTER the
input (just "days") with no prefix context.

**Fix:** Add a leading label and a border separator:

```tsx
{extending && (
  <div className="flex items-center gap-2 pt-2 border-t">
    <span className="text-xs text-muted-foreground">
      {trialExpired ? "Grant extension of" : "Extend trial by"}
    </span>
    <Input
      type="number"
      min="1"
      max="90"
      value={days}
      onChange={(e) => setDays(e.target.value)}
      className="h-7 w-20 text-xs"
    />
    <span className="text-xs text-muted-foreground">days</span>
    <Button
      size="sm"
      className="h-7 px-2 text-xs"
      disabled={extending_}
      onClick={handleExtend}
    >
      {extending_ ? <Loader2 className="h-3 w-3 animate-spin" /> : "Confirm"}
    </Button>
    <Button
      size="sm"
      variant="ghost"
      className="h-7 px-2 text-xs"
      onClick={() => setExtending(false)}
    >
      Cancel
    </Button>
  </div>
)}
```

**Commit:** `fix(subscriptions): extend trial form — add contextual label and border separator`

---

## 6. Cancel Confirmation — Include Studio Name

**Problem:** The confirmation panel says "Cancel this subscription?" with no
studio name. An admin with many tabs open has no idea which subscription
they're about to cancel.

**Fix:** Name the studio in the confirmation text:

```tsx
// Before:
<span className="text-xs text-destructive font-medium">
  Cancel this subscription?
</span>

// After:
<span className="text-xs text-destructive font-medium">
  Cancel subscription for <strong>{sub.studioName}</strong>?
</span>
```

Also update the confirm button label from `"Confirm"` to `"Yes, cancel"` for
clarity (destructive confirm buttons should name the action):

```tsx
{cancelling_ ? <Loader2 className="h-3 w-3 animate-spin" /> : "Yes, cancel"}
```

And change `"Back"` to `"Keep"` — "Back" is ambiguous; "Keep" clearly means
"keep the subscription":

```tsx
<Button size="sm" variant="ghost" className="h-7 px-2 text-xs"
  onClick={() => setConfirming(false)}>
  Keep
</Button>
```

**Commit:** `fix(subscriptions): cancel confirmation names studio, clearer button labels`

---

## 7. Replace Spinner with Skeleton Loader

**Problem:** Lines 276–280 show a `Loader2` spinner + "Loading…" text on
first paint. The rest of the codebase uses skeleton cards.

**Add** a `SubscriptionRowSkeleton` component above `SubscriptionRow`:

```tsx
function SubscriptionRowSkeleton() {
  return (
    <Card>
      <CardContent className="p-4 space-y-2">
        <div className="flex items-start justify-between gap-4">
          <div className="space-y-1.5 flex-1">
            <div className="flex items-center gap-2">
              <Skeleton className="h-4 w-32" />
              <Skeleton className="h-3 w-28" />
              <Skeleton className="h-5 w-20 rounded-full" />
            </div>
            <Skeleton className="h-3 w-56" />
            <Skeleton className="h-3 w-40" />
          </div>
          <div className="flex items-center gap-1.5">
            <Skeleton className="h-7 w-28" />
            <Skeleton className="h-7 w-20" />
          </div>
        </div>
      </CardContent>
    </Card>
  );
}
```

Import `Skeleton`:

```tsx
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Card, CardContent } from "@/shared/components/ui/card";
```

Replace the spinner block in `SubscriptionOversightPage`:

```tsx
// Before:
{isLoading && (
  <div className="flex items-center justify-center py-16 gap-2 text-muted-foreground">
    <Loader2 className="h-5 w-5 animate-spin" />
    <span className="text-sm">Loading…</span>
  </div>
)}

// After:
{isLoading && (
  <div className="space-y-3">
    {[1, 2, 3, 4, 5].map((i) => <SubscriptionRowSkeleton key={i} />)}
  </div>
)}
```

Also remove `Loader2` from the top-level import if it is no longer used after
this change (it's still used inside `SubscriptionRow` for mutation loading
states — keep it).

**Commit:** `fix(subscriptions): replace spinner with skeleton cards on load`

---

## 8. Add Search Input and Sort Control

**Problem:** All subscriptions are rendered at once with no way to find a
specific studio by name. At 50+ tenants this is unusable. There is also no
way to sort by urgency (trial expiring soonest).

**Changes in `SubscriptionOversightPage`:**

Add state:

```tsx
const [search, setSearch] = useState("");
const [sortKey, setSortKey] = useState<"name" | "trialEnd" | "periodEnd">("trialEnd");
```

Import `Search` icon (already available in lucide-react; check imports):

```tsx
import { Banknote, Clock, Loader2, Receipt, Search, XCircle } from "lucide-react";
```

Add search + sort controls above the filter pills. The search and sort controls
must be left-aligned, under the sticky header, above the pill row:

```tsx
{/* ── Search + sort toolbar ────────────────────────────────────── */}
<div className="flex gap-2 flex-wrap mb-3">
  <div className="relative flex-1 min-w-48">
    <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5
                       text-muted-foreground pointer-events-none" />
    <Input
      placeholder="Search by studio name or slug…"
      value={search}
      onChange={(e) => setSearch(e.target.value)}
      className="pl-8 h-8 text-sm"
    />
  </div>
  <select
    value={sortKey}
    onChange={(e) => setSortKey(e.target.value as typeof sortKey)}
    className="h-8 rounded-md border border-input bg-background px-2 text-xs"
    aria-label="Sort subscriptions"
  >
    <option value="trialEnd">Trial end (soonest first)</option>
    <option value="periodEnd">Period end (soonest first)</option>
    <option value="name">Studio name (A–Z)</option>
  </select>
</div>
```

Apply search + sort to the filtered list (currently at lines 227–229):

```tsx
const baseFiltered = subscriptions?.filter((s) =>
  statusFilter ? s.status === statusFilter : true
) ?? [];

const q = search.trim().toLowerCase();

const searched = q
  ? baseFiltered.filter((s) =>
      s.studioName.toLowerCase().includes(q) ||
      s.studioSlug.toLowerCase().includes(q)
    )
  : baseFiltered;

const filtered = [...searched].sort((a, b) => {
  if (sortKey === "name")      return a.studioName.localeCompare(b.studioName);
  if (sortKey === "trialEnd")  return new Date(a.trialExpiresAt).getTime() - new Date(b.trialExpiresAt).getTime();
  if (sortKey === "periodEnd") return new Date(a.currentPeriodEnd).getTime() - new Date(b.currentPeriodEnd).getTime();
  return 0;
});
```

Update the header count to reflect the filtered result:

```tsx
{subscriptions && (
  <span className="text-xs text-muted-foreground ml-1">
    {filtered.length === subscriptions.length
      ? `(${subscriptions.length})`
      : `(${filtered.length} of ${subscriptions.length})`}
  </span>
)}
```

**Commit:** `feat(subscriptions): search input + sort by trial end / period end / name`

---

## 9. Contextual Empty State with Clear Filter CTA

**Problem:** The empty state (line 288) says `"No studios found."` with no
context about whether a filter or search caused it, and no way to clear it
from within the message.

**Fix:** Replace the generic empty state with contextual copy and an action:

```tsx
{!isLoading && !isError && filtered.length === 0 && (
  <div className="flex flex-col items-center justify-center py-24 gap-3">
    <Receipt className="h-10 w-10 text-muted-foreground/30" />
    <p className="text-sm text-muted-foreground">
      {subscriptions?.length === 0
        ? "No subscriptions yet."
        : q && statusFilter
          ? `No ${STATUS_LABELS[statusFilter] ?? statusFilter} subscriptions matching "${search}".`
          : q
            ? `No subscriptions matching "${search}".`
            : `No ${STATUS_LABELS[statusFilter] ?? statusFilter} subscriptions.`}
    </p>
    {(q || statusFilter) && (
      <Button
        size="sm"
        variant="outline"
        className="text-xs"
        onClick={() => {
          setSearch("");
          setSearchParams({});
        }}
      >
        Clear filters
      </Button>
    )}
  </div>
)}
```

**Commit:** `fix(subscriptions): contextual empty state with clear filters CTA`

---

## 10. Widen Container and Left-Align Filter Pills

**Problem:** `max-w-2xl` (42 rem) is very narrow. With three action buttons
per GracePeriod row, the layout becomes cramped. The filter pills should be
left-aligned with the cards below them (they currently are, but a `justify-center`
was present in earlier versions — confirm and clean up).

**Fix:**

1. Change `max-w-2xl` → `max-w-3xl` on the `<main>` element.
2. Verify the filter pill `<div>` does NOT have `justify-center`. It should be
   `flex flex-wrap gap-2` with no centering.
3. Move the filter pill block so it renders inside `<main>` directly (not only
   when `!isLoading && subscriptions`) — render the search/sort toolbar always,
   render filter pills when `subscriptions` is available. This prevents layout
   shift when data loads.

**Commit:** `fix(subscriptions): widen to max-w-3xl, confirm left-aligned pill bar`

---

## 11. Activate Form — Label the Form Header and Add "Record Cash Payment" Context

**Problem:** The Activate form header at line 145 says "Activate — Cash Payment"
as a `<p>` element — a duplicate of the old button label. Users don't know what
"Activate" means: does it generate an invoice, charge the card, or just flip a
status flag?

**Fix:** Replace the form header with a more explicit label and a brief
one-line explanation:

```tsx
// Before:
<p className="text-xs font-medium text-muted-foreground">Activate — Cash Payment</p>

// After:
<div className="space-y-0.5">
  <p className="text-xs font-medium">Record Cash Payment</p>
  <p className="text-xs text-muted-foreground">
    Manually activates the subscription — use when payment was collected offline.
  </p>
</div>
```

**Commit:** `fix(subscriptions): clarify activate form header with explanation`

---

## 12. Update Tests

**File:** `frontend/src/features/platform/__tests__/SubscriptionOversightPage.test.tsx`

### 12a. Update `PLANS` seed with `subscriberCount`

When the Plans Page overnight prompt runs, `PlanResponse` gains a
`subscriberCount` field. Pre-empt the type error now:

```typescript
const PLANS: PlanResponse[] = [
  {
    id:                    "plan-1",
    name:                  "Starter",
    billingInterval:       "Monthly",
    priceMonthly:          29,
    priceYearly:           290,
    yearlyDiscountPercent: 17,
    allowBrandingRemoval:  false,
    subscriberCount:       2,    // ← add this field
  },
];
```

> If `PlanResponse` doesn't yet have `subscriberCount`, add it to
> `frontend/src/features/billing/billing.types.ts` first (as a required field).
> Do this only if the type doesn't already have it.

### 12b. Loading test — spinner → skeleton

```typescript
// Before:
it("shows a loading spinner while loading", () => {
  renderPage();
  expect(screen.getByText("Loading…")).toBeInTheDocument();
});

// After:
it("shows skeleton cards while loading, not a spinner", () => {
  renderPage();
  expect(document.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
  expect(screen.queryByText("Loading…")).not.toBeInTheDocument();
});
```

### 12c. Status badge tests — fix raw enum assertions

```typescript
// Before (line 142–146):
it("shows the Trialing status badge", async () => {
  renderPage();
  await screen.findByText("Trialing Studio");
  expect(screen.getByText("Trialing")).toBeInTheDocument();
});

// After:
it("shows the 'In Trial' status badge for trialing subscriptions", async () => {
  renderPage();
  await screen.findByText("Trialing Studio");
  // STATUS_LABELS["Trialing"] = "In Trial"
  const badges = screen.getAllByText("In Trial", { selector: "span" });
  expect(badges.length).toBeGreaterThan(0);
  // The raw string "Trialing" must not appear as a badge
  expect(screen.queryByText("Trialing", { selector: "span" })).not.toBeInTheDocument();
});
```

### 12d. Cancel confirmation text

```typescript
// Before (line 217):
expect(screen.getByText(/cancel this subscription\?/i)).toBeInTheDocument();

// After:
expect(screen.getByText(/cancel subscription for active studio\?/i)).toBeInTheDocument();
```

### 12e. Cancel button labels

```typescript
// Before (line 218–219):
expect(screen.getByRole("button", { name: /confirm/i })).toBeInTheDocument();
expect(screen.getByRole("button", { name: /back/i })).toBeInTheDocument();

// After:
expect(screen.getByRole("button", { name: /yes, cancel/i })).toBeInTheDocument();
expect(screen.getByRole("button", { name: /keep/i })).toBeInTheDocument();
```

```typescript
// Also update the calls-the-API test (line 237):
// Before:
await user.click(screen.getByRole("button", { name: /confirm/i }));
// After:
await user.click(screen.getByRole("button", { name: /yes, cancel/i }));
```

### 12f. Extend trial button name

```typescript
// Update any test that looks for /extend trial/i to also accept /extend trial \(\+7 days\)/i:
const extendBtns = screen.getAllByRole("button", { name: /extend trial/i });
// This regex already matches "Extend Trial (+7 days)" — no change needed here.

// But the form confirmation test (line 185) asserts /confirm/i:
// The confirm button in the extend form is still labeled "Confirm" — no change needed.
```

### 12g. Add new tests

```typescript
it("shows aria-label with studio name on Cancel Subscription button", async () => {
  renderPage();
  await screen.findByText("Active Studio");
  expect(
    screen.getByRole("button", { name: /cancel subscription for active studio/i })
  ).toBeInTheDocument();
});

it("shows aria-label with studio name on Extend Trial button", async () => {
  renderPage();
  await screen.findByText("Trialing Studio");
  expect(
    screen.getByRole("button", { name: /extend trial for trialing studio/i })
  ).toBeInTheDocument();
});

it("search input filters by studio name", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Active Studio");

  await user.type(
    screen.getByPlaceholderText(/search by studio name or slug/i),
    "active"
  );

  expect(screen.getByText("Active Studio")).toBeInTheDocument();
  expect(screen.queryByText("Trialing Studio")).not.toBeInTheDocument();
  expect(screen.queryByText("Cancelled Studio")).not.toBeInTheDocument();
});

it("shows 'No subscriptions matching' when search has no results", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Active Studio");

  await user.type(
    screen.getByPlaceholderText(/search by studio name or slug/i),
    "zzznomatch"
  );

  expect(screen.getByText(/no subscriptions matching/i)).toBeInTheDocument();
});

it("shows 'Clear filters' button when search yields no results", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Active Studio");

  await user.type(
    screen.getByPlaceholderText(/search by studio name or slug/i),
    "zzznomatch"
  );

  const clearBtn = screen.getByRole("button", { name: /clear filters/i });
  expect(clearBtn).toBeInTheDocument();

  await user.click(clearBtn);

  expect(screen.getByText("Active Studio")).toBeInTheDocument();
});

it("sort dropdown is present and defaults to trial end (soonest first)", async () => {
  renderPage();
  await screen.findByText("Active Studio");
  const sortSelect = screen.getByDisplayValue(/trial end/i);
  expect(sortSelect).toBeInTheDocument();
});

it("filter pills show human-readable labels, not raw enum values", async () => {
  renderPage();
  await screen.findByText("Active Studio");
  // "In Trial" pill, not "Trialing" pill
  const trialPill = screen.getByRole("button", { name: /in trial/i });
  expect(trialPill).toBeInTheDocument();
  // "Grace Period" pill, not "GracePeriod" pill
  // (only rendered if count > 0 — seed has none, so only check the negative)
  expect(screen.queryByRole("button", { name: /^GracePeriod/i })).not.toBeInTheDocument();
});

it("shows 'Grace Period' label in status badge, not 'GracePeriod'", async () => {
  server.use(
    http.get("http://localhost/api/v1/platform/subscriptions", () =>
      HttpResponse.json([
        {
          studioId:        "sg1",
          studioName:      "Grace Studio",
          studioSlug:      "grace-studio",
          subscriptionId:  "sub-g1",
          status:          "GracePeriod",
          planName:        "Pro",
          trialExpiresAt:  new Date(Date.now() - 14 * 86_400_000).toISOString(),
          currentPeriodEnd: new Date(Date.now() + 3 * 86_400_000).toISOString(),
        },
      ]),
    ),
  );
  renderPage();
  await screen.findByText("Grace Studio");
  expect(screen.getByText("Grace Period", { selector: "span" })).toBeInTheDocument();
  expect(screen.queryByText("GracePeriod", { selector: "span" })).not.toBeInTheDocument();
});

it("extend trial form shows 'Extend trial by' label", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Trialing Studio");

  const extendBtns = screen.getAllByRole("button", { name: /extend trial/i });
  await user.click(extendBtns[0]);

  expect(screen.getByText(/extend trial by/i)).toBeInTheDocument();
});

it("'Record Cash Payment' form header appears when Activate is clicked", async () => {
  server.use(
    http.get("http://localhost/api/v1/platform/subscriptions", () =>
      HttpResponse.json([
        {
          studioId:        "sg1",
          studioName:      "Grace Studio",
          studioSlug:      "grace-studio",
          subscriptionId:  "sub-g1",
          status:          "GracePeriod",
          planName:        "Pro",
          trialExpiresAt:  new Date(Date.now() - 14 * 86_400_000).toISOString(),
          currentPeriodEnd: new Date(Date.now() + 3 * 86_400_000).toISOString(),
        },
      ]),
    ),
  );
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Grace Studio");

  await user.click(screen.getByRole("button", { name: /activate subscription for grace studio/i }));

  expect(screen.getByText(/record cash payment/i)).toBeInTheDocument();
});
```

Run `pnpm test` — ALL tests must pass (zero failures).

**Commit:** `test(subscriptions): update + add tests for labels, search, sort, aria-labels`

---

## 13. Final Verification

1. `pnpm --dir frontend tsc --noEmit` — zero TypeScript errors.
2. `pnpm --dir frontend lint` — zero errors.
3. `pnpm --dir frontend test` — all tests pass.
4. Visually verify:
   - No raw enum values visible in the UI ("GracePeriod", "Trialing" must not appear)
   - All cards are the same height regardless of status (badge stays inline)
   - GracePeriod rows have the same compact layout as Active/Trialing rows
   - "Activate" button is visually heavier (filled) than "Extend Trial" and "Cancel Subscription"
   - Search input is above filter pills, left-aligned
   - Sort dropdown is next to the search input
   - Dates read "25 Jun 2026" not "25/06/2026"
   - Meta line is two short lines, not one long run-on line
5. `git log --oneline -15` — confirm all commits are present.

---

## Reference: Audit Issue → Task Map

| Audit Issue                                              | Task |
|----------------------------------------------------------|------|
| "GracePeriod" / "Trialing" raw enums in badge + pills    | 1    |
| GracePeriod badge wrapping to second line                | 2    |
| "25/06/2026" date format hard to scan                    | 3    |
| Meta line — three unrelated items in one `<p>`           | 3    |
| "Extend trial" no duration label                         | 4    |
| "Activate — Cash Payment" same visual weight as others   | 4    |
| Inconsistent icons (only Activate has one)               | 4    |
| No `aria-label` on any button (a11y critical)            | 4    |
| Extend trial form has no leading label                   | 5    |
| Cancel confirmation doesn't name the studio              | 6    |
| "Confirm" / "Back" labels ambiguous for destructive op   | 6    |
| Spinner instead of skeleton on first load                | 7    |
| No search input — list unusable at scale                 | 8    |
| No sort controls — can't find "trials expiring soon"     | 8    |
| Empty state says "No studios found." with no action      | 9    |
| Page too narrow (max-w-2xl) for 3-button rows            | 10   |
| "Activate — Cash Payment" form header ambiguous          | 11   |
| Tests fail after all the above changes                   | 12   |
