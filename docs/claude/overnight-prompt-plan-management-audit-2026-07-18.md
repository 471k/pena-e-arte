# Overnight Prompt — Plan Management Page: UX Audit Fixes
**Date:** 2026-07-18
**Files changed:** ~2 (frontend only — 1 component, 1 test file)
**Type:** Frontend polish — 1 data-correctness bug + 5 targeted UX fixes

---

## Context

A UX audit of `PlanManagementPage` identified a data bug (savings badge shows a stored
percentage that can silently desync from actual prices), a layout inconsistency (Pro card
header collapses to a single line because the plan name is short), and several quick polish
items. No backend changes are needed: `yearlyDiscountPercent` stays in the data model and
form (it's used to suggest the yearly price during editing), but it will no longer drive the
displayed badge.

**Important note on the delete safeguard:** The audit flagged "no visible delete safeguard"
but the code already has a confirm state (`deleting`) with subscriber count warnings. This
is correctly implemented. Do not change the delete flow — this prompt contains no delete
changes.

---

## Phase 0 — Required Reading

```
frontend/src/features/platform/components/PlanManagementPage.tsx
frontend/src/features/platform/__tests__/PlanManagementPage.test.tsx
frontend/src/features/billing/billing.types.ts
frontend/src/features/billing/billingApi.ts
Pena_e_Arte.Application/Billing/Queries/GetPlansQuery.cs
docs/claude/conventions.md
```

---

## Critical Fix #1 — Savings badge computes from actual prices, not the stored field

### Root cause

`PlanCard` renders:
```tsx
{plan.yearlyDiscountPercent > 0 && (
  <span ...>
    Save {plan.yearlyDiscountPercent}% vs monthly billing
  </span>
)}
```

`yearlyDiscountPercent` is a stored field entered when the plan was created. It is used in
the edit form to suggest what yearly price to use, but is not recomputed when prices are
updated. If an admin edits the monthly or yearly price without touching the discount field,
the badge silently shows a stale percentage. In production, the Premium plan currently shows
"Save 17%" when the actual math (€200/yr vs €30×12=€360) gives ~44%.

### Fix — `PlanManagementPage.tsx` in `PlanCard`

**1. Compute the real savings at render time:**

Add a local constant inside `PlanCard` before the JSX return:

```tsx
// Compute savings from actual prices — not from the stored yearlyDiscountPercent field,
// which can silently desync if prices are edited without updating the discount.
const computedSavingsPct =
  plan.priceMonthly > 0
    ? Math.round((1 - plan.priceYearly / (plan.priceMonthly * 12)) * 100)
    : 0;
```

**2. Replace the badge rendering:**

```tsx
// Before:
{plan.yearlyDiscountPercent > 0 && (
  <span className="inline-flex items-center text-xs px-2 py-0.5 rounded-full
                   bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30
                   dark:text-emerald-300 font-medium">
    Save {plan.yearlyDiscountPercent}% vs monthly billing
  </span>
)}

// After:
{computedSavingsPct > 0 && (
  <span className="inline-flex items-center text-xs px-2 py-0.5 rounded-full
                   bg-emerald-50 text-emerald-600 dark:bg-emerald-900/20
                   dark:text-emerald-400 font-medium">
    Save {computedSavingsPct}% annually
  </span>
)}
```

Two changes bundled here:
- `plan.yearlyDiscountPercent` → `computedSavingsPct` (the data fix)
- `"vs monthly billing"` → `"annually"` (removes the copy contradiction when a plan is
  billed yearly — "Billing: Yearly · Save 17% vs monthly billing" is internally incoherent)
- Slightly muted badge (`emerald-50`/`emerald-600`) to reduce visual competition with the
  price figures, which are the more important numbers for an admin auditing pricing

**3. Do NOT remove `yearlyDiscountPercent` from the form.** The field still serves the
edit form's "Suggested yearly price" helper (`suggestedYearly` computation) and the backend
schema. Leave `PlanForm`, `billingApi.ts`, and the backend untouched.

---

## Fix #2 — Card header: plan name always on its own line

### Root cause

The card header uses a single `flex items-center gap-2 flex-wrap` row containing three
items: plan name, "Billing: X" label, and optional "White-label" badge. When the plan name
is short (e.g. "Pro", 3 characters), all three items fit on one line and don't wrap —
breaking the expected two-line rhythm established by "Starter"/"Premium"/"Growth" where the
name + label + badge DO wrap. This looks like a broken card for short plan names.

### Fix — `PlanManagementPage.tsx` in `PlanCard`, info-left section

Change from a single flex row to a stacked layout where the name is always on its own line:

```tsx
// Before:
<div className="space-y-1 min-w-0">
  <div className="flex items-center gap-2 flex-wrap">
    <span className="text-base font-semibold">{plan.name}</span>
    <span className="text-xs text-muted-foreground">Billing: {plan.billingInterval}</span>
    {plan.allowBrandingRemoval && (
      <span className="text-xs px-1.5 py-0.5 rounded-full bg-purple-100 text-purple-700
                       dark:bg-purple-900/30 dark:text-purple-300">
        White-label
      </span>
    )}
  </div>
  ...
</div>

// After:
<div className="space-y-1 min-w-0">
  <p className="text-base font-semibold truncate" title={plan.name}>{plan.name}</p>
  <div className="flex items-center gap-1.5 flex-wrap">
    <span className="text-xs text-muted-foreground">Billing: {plan.billingInterval}</span>
    {plan.allowBrandingRemoval && (
      <span className="text-xs px-1.5 py-0.5 rounded-full bg-purple-100 text-purple-700
                       dark:bg-purple-900/30 dark:text-purple-300">
        White-label
      </span>
    )}
  </div>
  ...
</div>
```

Name is now always on its own `<p>` line. `truncate` + `title` handles very long plan
names gracefully. The billing/badge row underneath uses `flex-wrap` for the White-label
badge, which is fine on a second line.

---

## Fix #3 — Trash icon: destructive color by default

### Root cause

Both the Edit (pencil) and Delete (trash) buttons use identical `text-muted-foreground`
color with only a `hover:text-destructive` change. An admin scanning cards cannot
distinguish a destructive action from an edit action until they hover — a safety gap on a
page where delete is permanent.

### Fix — `PlanManagementPage.tsx` in `PlanCard`, delete button

```tsx
// Before:
<Button
  size="sm"
  variant="ghost"
  className="h-7 w-7 p-0 text-muted-foreground hover:text-destructive transition-colors"
  onClick={() => setDeleting(true)}
  aria-label={`Delete ${plan.name} plan`}
  title="Delete"
>
  <Trash2 className="h-3.5 w-3.5" />
</Button>

// After:
<Button
  size="sm"
  variant="ghost"
  className="h-7 w-7 p-0 text-destructive/50 hover:text-destructive hover:bg-destructive/10
             transition-colors"
  onClick={() => setDeleting(true)}
  aria-label={`Delete ${plan.name} plan`}
  title="Delete"
>
  <Trash2 className="h-3.5 w-3.5" />
</Button>
```

`text-destructive/50` gives a muted-red default that is clearly different from the neutral
edit button but not alarmingly bright in the resting state. `hover:bg-destructive/10`
provides a subtle red tint on hover consistent with common destructive button patterns.
The edit button stays `text-muted-foreground` with no change.

---

## Fix #4 — Ghost "+ Add plan" tile for partial grid rows

### Root cause

When plans.length % 3 !== 0 (e.g. 4 plans in a 3-column grid), the last row has one or
two empty slots. Empty grid cells read as an unfinished layout rather than an intentional
design.

### Fix — `PlanManagementPage.tsx` in the plans grid section

After the `plans.map(...)` call, add a conditional ghost tile inside the same grid:

```tsx
{!isLoading && !isError && plans && plans.length > 0 && (
  <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
    {plans.map((p) => (
      <PlanCard key={p.id} plan={p} />
    ))}

    {/* Ghost tile: only shown when the grid has an unfilled last slot */}
    {plans.length % 3 !== 0 && (
      <button
        type="button"
        onClick={() => setCreating(true)}
        className="flex flex-col items-center justify-center gap-2 rounded-lg border-2
                   border-dashed border-border/40 p-8 text-muted-foreground/40
                   hover:border-border/70 hover:text-muted-foreground/60
                   transition-colors cursor-pointer min-h-[100px]"
        aria-label="Create new plan"
      >
        <Plus className="h-5 w-5" />
        <span className="text-xs">Add plan</span>
      </button>
    )}
  </div>
)}
```

This renders exactly ONE ghost tile in the first empty slot, regardless of how many empty
slots remain (a 4-plan grid shows [P1, P2, P3 / P4, ghost, ∅], a 5-plan grid shows
[P1, P2, P3 / P4, P5, ghost]). When plans.length % 3 === 0, no ghost is shown — the grid
is perfectly filled. The ghost tile opens the create form when clicked, the same as the
"New plan" header button.

---

## Fix #5 — Subscriber count: accessible label and non-interactive clarity

The subscriber count `<span>` has `title="Studios on this plan"` but no `aria-label`,
meaning screen readers will announce only the number. Add an explicit `aria-label`:

```tsx
// Before:
<span className="flex items-center gap-1 text-xs text-muted-foreground"
      title="Studios on this plan">
  <Users className="h-3.5 w-3.5" />
  {plan.subscriberCount}
</span>

// After:
<span
  className="flex items-center gap-1 text-xs text-muted-foreground"
  title={`${plan.subscriberCount} studio${plan.subscriberCount !== 1 ? "s" : ""} subscribed`}
  aria-label={`${plan.subscriberCount} studio${plan.subscriberCount !== 1 ? "s" : ""} subscribed to ${plan.name}`}
>
  <Users className="h-3.5 w-3.5" />
  {plan.subscriberCount}
</span>
```

The tooltip now uses correct singular/plural phrasing ("1 studio subscribed" vs
"4 studios subscribed") and the aria-label adds the plan name for full screen-reader
context. The element remains a non-interactive `<span>` — drilling into the subscriber
list is a longer-term improvement that requires the Studios list to accept a plan filter
via URL params, which is out of scope for this prompt.

---

## Fix #6 — Sticky header z-index: `z-10 → z-20`

The page header uses `z-10`. For consistency with other issuer pages (which were updated to
`z-20` in prior overnight prompts) and to prevent card hover effects from painting over the
sticky header, change:

```tsx
// Before (line ~365):
<header className="flex items-center justify-between px-6 py-3 border-b
                   bg-background sticky top-0 z-10">

// After:
<header className="flex items-center justify-between px-6 py-3 border-b
                   bg-background sticky top-0 z-20">
```

---

## Phase 2 — Tests

### Existing tests — required updates

One test must be updated because the badge copy changes:

```typescript
// Before (currently at line ~297-302):
it("shows 'Save X% vs monthly billing' savings badge", async () => {
  renderPage();
  await screen.findByText("Starter");
  expect(screen.getAllByText(/save 17% vs monthly billing/i).length).toBe(2);
});

// After:
it("shows computed savings badge using actual prices", async () => {
  renderPage();
  await screen.findByText("Starter");
  // Seed: Starter priceMonthly=29, priceYearly=290 → computed = Math.round((1-290/348)*100) = 17%
  //       Pro    priceMonthly=49, priceYearly=490 → computed = Math.round((1-490/588)*100) = 17%
  // Both match — two badges should appear
  expect(screen.getAllByText(/save 17% annually/i).length).toBe(2);
  // Confirm the old "vs monthly billing" copy is gone
  expect(screen.queryByText(/vs monthly billing/i)).not.toBeInTheDocument();
});
```

All other existing tests remain valid — verify they still pass before submitting.

### New tests to add

Append inside the existing `describe("PlanManagementPage", ...)` block:

```typescript
// ── Fix #1: Computed savings badge ──────────────────────────────────────────

it("badge shows computed savings even when yearlyDiscountPercent is wrong", async () => {
  // Simulate the Premium bug: stored discount=17% but actual math gives 44%
  server.use(
    http.get("http://localhost/api/v1/billing/plans", () =>
      HttpResponse.json([{
        id:                    "plan-premium",
        name:                  "Premium",
        billingInterval:       "Yearly",
        priceMonthly:          30,
        priceYearly:           200,        // 200 / (30*12) = 55.6% of monthly → 44% saving
        yearlyDiscountPercent: 17,         // stored value is wrong
        allowBrandingRemoval:  false,
        stripePriceIdMonthly:  null,
        stripePriceIdYearly:   null,
        subscriberCount:       1,
      }]),
    ),
  );
  renderPage();
  await screen.findByText("Premium");
  // Badge should show computed 44%, NOT stored 17%
  expect(screen.getByText(/save 44% annually/i)).toBeInTheDocument();
  expect(screen.queryByText(/save 17%/i)).not.toBeInTheDocument();
});

it("does not show savings badge when yearly price is not cheaper than 12x monthly", async () => {
  server.use(
    http.get("http://localhost/api/v1/billing/plans", () =>
      HttpResponse.json([{
        id:                    "plan-odd",
        name:                  "Odd",
        billingInterval:       "Monthly",
        priceMonthly:          10,
        priceYearly:           130,        // 130 vs 120 → actually MORE expensive yearly
        yearlyDiscountPercent: 8,          // stored value claims a discount
        allowBrandingRemoval:  false,
        stripePriceIdMonthly:  null,
        stripePriceIdYearly:   null,
        subscriberCount:       0,
      }]),
    ),
  );
  renderPage();
  await screen.findByText("Odd");
  // computedSavingsPct = Math.round((1 - 130/120) * 100) = Math.round(-0.083*100) = -8 < 0 → no badge
  expect(screen.queryByText(/save/i)).not.toBeInTheDocument();
});

it("badge copy never contains 'vs monthly billing'", async () => {
  renderPage();
  await screen.findByText("Starter");
  expect(screen.queryByText(/vs monthly billing/i)).not.toBeInTheDocument();
});

// ── Fix #2: Card header layout ───────────────────────────────────────────────

it("plan name and billing interval are in separate DOM elements", async () => {
  renderPage();
  await screen.findByText("Pro");
  // Plan name "Pro" should be in a <p> element
  const nameEl = screen.getByText("Pro", { selector: "p" });
  expect(nameEl).toBeInTheDocument();
  // Billing label should NOT be inside the same element as the name
  // (it's a sibling <span>, not a child of the <p>)
  expect(nameEl.textContent).toBe("Pro");
  expect(nameEl.textContent).not.toContain("Billing");
});

// ── Fix #3: Trash icon destructive color ─────────────────────────────────────

it("delete button has destructive color class by default (not muted-foreground)", async () => {
  renderPage();
  await screen.findByText("Starter");
  const deleteBtn = screen.getByRole("button", { name: /delete starter plan/i });
  // Default class should include text-destructive (at some opacity), not text-muted-foreground
  expect(deleteBtn.className).toMatch(/text-destructive/);
  expect(deleteBtn.className).not.toMatch(/text-muted-foreground/);
});

it("edit button does NOT have destructive color (it stays neutral)", async () => {
  renderPage();
  await screen.findByText("Starter");
  const editBtn = screen.getByRole("button", { name: /edit starter plan/i });
  expect(editBtn.className).not.toMatch(/text-destructive/);
});

// ── Fix #4: Ghost tile ────────────────────────────────────────────────────────

it("shows ghost 'Add plan' tile when plan count is not a multiple of 3", async () => {
  // PLANS seed has 2 plans (2 % 3 !== 0)
  renderPage();
  await screen.findByText("Starter");
  expect(screen.getByRole("button", { name: /create new plan/i })).toBeInTheDocument();
});

it("does NOT show ghost tile when plan count is a multiple of 3", async () => {
  server.use(
    http.get("http://localhost/api/v1/billing/plans", () =>
      HttpResponse.json([
        ...PLANS,
        {
          id:                    "plan-3",
          name:                  "Enterprise",
          billingInterval:       "Monthly",
          priceMonthly:          99,
          priceYearly:           990,
          yearlyDiscountPercent: 17,
          allowBrandingRemoval:  true,
          stripePriceIdMonthly:  null,
          stripePriceIdYearly:   null,
          subscriberCount:       0,
        },
      ]),
    ),
  );
  renderPage();
  await screen.findByText("Enterprise");
  // 3 plans → 3 % 3 === 0 → no ghost tile
  // The New Plan header button is "new plan" not "create new plan"
  expect(screen.queryByRole("button", { name: /create new plan/i })).not.toBeInTheDocument();
});

it("clicking ghost tile opens the create form", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Starter");

  const ghostTile = screen.getByRole("button", { name: /create new plan/i });
  await user.click(ghostTile);

  expect(screen.getByLabelText(/^name$/i)).toBeInTheDocument();
});

// ── Fix #5: Subscriber count accessibility ────────────────────────────────────

it("subscriber count has an accessible aria-label describing plan name and count", async () => {
  renderPage();
  await screen.findByText("Starter");
  // Starter has 4 subscribers
  expect(screen.getByRole("generic", {
    // The span doesn't have an implicit role, so verify by selector
  }));
  // Check the aria-label directly
  const spans = document.querySelectorAll('[aria-label*="studios subscribed to"]');
  expect(spans.length).toBe(2); // One for Starter, one for Pro
  const starterSpan = document.querySelector('[aria-label*="subscribed to Starter"]');
  expect(starterSpan?.getAttribute("aria-label")).toMatch(/4 studios subscribed to Starter/i);
});
```

---

## Phase 3 — Quality Gates

```bash
pnpm --filter frontend test -- --reporter=verbose 2>&1 | grep -E "(PASS|FAIL|✓|✗)"
pnpm --filter frontend lint 2>&1 | grep -E "^.*error" | head -20
```

Both must be clean. No backend builds are required (no backend changes).

---

## Phase 4 — Forbidden Actions

- Do not remove `yearlyDiscountPercent` from `PlanResponse`, `billingApi.ts`, the form,
  or the backend. It is still used for the form's suggested yearly price calculation.
- Do not change the delete confirmation flow — it is correctly implemented.
- Do not add a plan lifecycle/status system (Active/Draft/Archived) — that is a
  longer-term improvement noted in the audit, not in scope here.
- Do not add a click-through on the subscriber count span to a filtered Studios list —
  that requires URL param support in `IssuerStudioListPage`, which is also out of scope.
- Do not introduce new npm packages.

---

## Completion Checklist

- [ ] Fix #1 — `computedSavingsPct` computed from `priceMonthly`/`priceYearly`
- [ ] Fix #1 — Badge renders `computedSavingsPct`, not `yearlyDiscountPercent`
- [ ] Fix #1 — Badge copy changed to "Save X% annually" (no "vs monthly billing")
- [ ] Fix #1 — Badge uses `emerald-50`/`emerald-600` (slightly muted)
- [ ] Fix #2 — Plan name in `<p>`, billing/badge in separate child row
- [ ] Fix #2 — Short names (e.g. "Pro") no longer collapse onto one line with billing label
- [ ] Fix #3 — Trash button default class is `text-destructive/50`, not `text-muted-foreground`
- [ ] Fix #3 — Edit button unchanged (stays neutral)
- [ ] Fix #4 — Ghost "+ Add plan" tile appears when `plans.length % 3 !== 0`
- [ ] Fix #4 — Ghost tile opens create form on click
- [ ] Fix #4 — No ghost tile when `plans.length % 3 === 0`
- [ ] Fix #5 — Subscriber count span has `aria-label` with name + count
- [ ] Fix #5 — Subscriber count tooltip uses singular/plural phrasing
- [ ] Fix #6 — Header `z-10 → z-20`
- [ ] Existing `"shows 'Save X% vs monthly billing'"` test updated to `/save 17% annually/i`
- [ ] All existing tests pass
- [ ] 9 new tests pass
- [ ] `pnpm test` clean
- [ ] `pnpm lint` clean
