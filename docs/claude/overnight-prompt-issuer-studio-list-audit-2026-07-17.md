# Overnight Prompt — Issuer Studio List: UX Audit Fixes
**Date:** 2026-07-17
**Files changed:** ~5 (1 backend, 4 frontend)
**Type:** Full-stack — two critical issues + six targeted UX fixes

---

## Context

A UX audit of `IssuerStudioListPage` identified a rendering bug (sticky header being
overpainted by list content), a data-integrity bug (Active subscriptions showing "—"
for plan name), and several impactful polish items. This prompt addresses all of them.

**Dependency note:** This prompt assumes the two previous overnight prompts have been
applied (`overnight-prompt-issuer-studio-detail-2026-07-17.md` and
`overnight-prompt-issuer-studio-subs-audit-2026-07-17.md`). Specifically:
- `STATUS_CLASSES.Suspended` is already amber (not red) in this file from the subs audit prompt
- `platform.types.ts` already has `isSuspended: boolean` on `PlatformSubscriptionResponse`

If those prompts have NOT been applied yet, verify the current state of `STATUS_CLASSES`
in `IssuerStudioListPage.tsx` before making any changes, and apply the amber color if needed.

---

## Phase 0 — Required Reading

```
frontend/src/features/platform/components/IssuerStudioListPage.tsx
frontend/src/features/platform/__tests__/IssuerStudioListPage.test.tsx
Pena_e_Arte.Application/Billing/Commands/ActivateSubscriptionManuallyCommand.cs
Pena_e_Arte.Application/Billing/Commands/CreateSubscriptionHandler.cs   ← read for Map() method
docs/claude/conventions.md
docs/claude/architecture.md
```

---

## Critical Fix #1 — Sticky header overpainted by list content

### Root cause

The page header uses `sticky top-0 z-10`. The highlighted studio card uses
`ring-2 ring-primary shadow-md`. In some browsers, `box-shadow` or `ring` on a descendant
can create a stacking context that renders above a sibling's `z-10` sticky element during a
`scrollIntoView` animation. The screenshot shows the last list card's text bleeding visually
over the header during the smooth scroll triggered by `location.state.highlight`.

### Fix — `IssuerStudioListPage.tsx`

Change the header from `z-10` to `z-20`:

```tsx
// Before (line ~484):
<header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">

// After:
<header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-20">
```

`z-20` is consistent with other sticky page headers in the app (e.g., `ClientLayout.tsx`
uses `z-20`). Check existing headers in the codebase; if they use `z-20`, adopt it here.

---

## Critical Fix #2 — "—" for plan name on Active subscriptions

### Root cause

In `IssuerStudioListPage`, `StudioRow.planDisplay` returns `sub?.planName ?? "—"` for
non-Trialing, non-NoSubscription studios. An Active subscription with `planName: null`
(which can occur when the linked Plan is deleted or when a Stripe webhook doesn't resolve
a plan name) renders a bare em-dash — indistinguishable from an intentional empty state.

In `ActivateSubscriptionManuallyCommand.cs`, the handler correctly sets `PlanId` on the
subscription, but does NOT set the `Plan` navigation property on the entity before passing
it to `CreateSubscriptionHandler.Map()`. If `Map()` reads `subscription.Plan?.Name`,
it will be null on the immediate return from the command even if the FK is set. This causes
a transient null on the return value (the list page refetches separately and loads the
plan via `ThenInclude`, so subsequent page loads are correct — but investigate the initial
return response and the Stripe webhook path).

### Fix A — Backend: set Plan navigation property in `ActivateSubscriptionManuallyCommand.cs`

After loading `Plan plan` from the DB and before `SaveChangesAsync`, explicitly attach the
Plan navigation to the subscription so `Map()` can read it:

```csharp
if (studio.Subscription is null)
{
    studio.Subscription = new Subscription
    {
        StudioId         = studio.Id,
        PlanId           = plan.Id,
        Plan             = plan,           // ← Add: set navigation for Map() return
        Status           = SubscriptionStatus.Active,
        CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1),
    };
    db.Subscriptions.Add(studio.Subscription);
}
else
{
    studio.Subscription.PlanId           = plan.Id;
    studio.Subscription.Plan             = plan;   // ← Add: set navigation for Map() return
    studio.Subscription.Status           = SubscriptionStatus.Active;
    studio.Subscription.CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1);
    studio.Subscription.TrialExpiresAt   = null;
}
```

### Fix B — Backend investigation: `CreateSubscriptionHandler.cs` (Stripe webhook path)

Read `CreateSubscriptionHandler.cs`. Check whether it:
1. Sets `Subscription.PlanId` to a known `Plan.Id` from the DB (not just a Stripe plan ID)
2. Either sets the `Plan` navigation OR ensures `Map()` handles a null plan gracefully

If `Map()` uses `subscription.Plan?.Name` without a fallback, add one:
```csharp
// In CreateSubscriptionHandler.Map() or wherever SubscriptionResponse is constructed:
PlanName = subscription.Plan?.Name ?? "[Plan unresolved]",
```

Document your findings in a one-line comment at the top of `ActivateSubscriptionManuallyCommand.cs`:
```csharp
// Plan navigation set explicitly so Map() can read Plan.Name without a DB round-trip.
```

### Fix C — Frontend: `IssuerStudioListPage.tsx`

**1. Change `planDisplay` fallback:**
```tsx
const planDisplay = (() => {
  if (subStatus === "Trialing") return "In Trial";
  if (subStatus === "NoSubscription") return "No subscription";
  return sub?.planName ?? "No plan assigned";
})();
```

**2. Add a warning icon for Active/paid studios with no plan name.**
Import `AlertTriangle` from `lucide-react` (add to the existing import). Then in the
meta-line rendering, after the plan display text:

```tsx
<p className="text-xs text-muted-foreground">
  {studio.city}
  {" · "}Registered {fmt(studio.createdAt)}
  {" · "}{planDisplay}
  {subStatus !== "Trialing" && subStatus !== "NoSubscription" && !sub?.planName && (
    <span
      title="Active subscription has no linked plan — check billing data"
      className="inline-flex items-center ml-1 text-amber-500"
    >
      <AlertTriangle className="h-3 w-3" />
    </span>
  )}
  {periodText && <>{" · "}{periodText}</>}
</p>
```

Note: also change `{" · "}{periodText}` to `{periodText && <>{" · "}{periodText}</>}` so
null `periodText` (from the previous prompt's fix to `periodText`) doesn't render a stray ` · `.

---

## Fix #3 — aria-label on status and plan `<select>` filters

The two filter `<select>` elements have no `aria-label` — a screen reader announces only
the currently-selected option. Add labels:

```tsx
<select
  value={statusFilter}
  onChange={(e) => setStatusFilter(e.target.value)}
  aria-label="Filter by status"
  className="h-8 rounded-md border border-input bg-background px-2 text-xs"
>

<select
  value={planFilter}
  onChange={(e) => setPlanFilter(e.target.value)}
  aria-label="Filter by plan"
  className="h-8 rounded-md border border-input bg-background px-2 text-xs"
>
```

---

## Fix #4 — Status group section dividers

The list is sorted by `STATUS_SORT_ORDER` (Suspended → PastDue → GracePeriod → Trialing →
Active → NoSubscription → Cancelled), but there's no visual grouping to make the sort
logic visible while scanning. Add a lightweight section header between status clusters.

### Step 1 — Compute groups after `filtered`

Add this `useMemo` immediately after the existing `filtered` `useMemo`:

```tsx
const groups = useMemo(() => {
  const result: Array<{ status: string; items: StudioResponse[] }> = [];
  for (const s of filtered) {
    const sub = subMap.get(s.id);
    const eff = !s.isActive ? "Suspended" : (sub?.status ?? "NoSubscription");
    const last = result.at(-1);
    if (last?.status === eff) {
      last.items.push(s);
    } else {
      result.push({ status: eff, items: [s] });
    }
  }
  return result;
}, [filtered, subMap]);
```

### Step 2 — Replace the flat `.map()` in the JSX

Replace:
```tsx
<div ref={listRef} className="space-y-3">
  {!isLoading && !studiosError && filtered.map((s) => (
    <div
      key={s.id}
      data-studio-id={s.id}
      className={`rounded-lg transition-shadow duration-700 ${
        highlightId === s.id && !dimHighlight
          ? "ring-2 ring-primary shadow-md"
          : ""
      }`}
    >
      <StudioRow studio={s} sub={subMap.get(s.id)} plans={plans} />
    </div>
  ))}
</div>
```

With:
```tsx
<div ref={listRef} className="space-y-4">
  {!isLoading && !studiosError && groups.map((group) => (
    <div key={group.status} className="space-y-3">
      {/* Show section divider only when multiple groups are visible */}
      {groups.length > 1 && (
        <div className="flex items-center gap-2 pt-1">
          <span className={`text-xs px-1.5 py-0.5 rounded-full font-medium shrink-0 ${STATUS_CLASSES[group.status] ?? ""}`}>
            {STATUS_LABELS[group.status] ?? group.status}
          </span>
          <span className="text-xs text-muted-foreground shrink-0">
            {group.items.length} {group.items.length === 1 ? "studio" : "studios"}
          </span>
          <div className="flex-1 h-px bg-border" />
        </div>
      )}
      {group.items.map((s) => (
        <div
          key={s.id}
          data-studio-id={s.id}
          className={`rounded-lg transition-shadow duration-700 ${
            highlightId === s.id && !dimHighlight
              ? "ring-2 ring-primary shadow-md"
              : ""
          }`}
        >
          <StudioRow studio={s} sub={subMap.get(s.id)} plans={plans} />
        </div>
      ))}
    </div>
  ))}
</div>
```

**Important:** The `data-studio-id` attribute stays on the innermost studio wrapper. The
`listRef.current.querySelector('[data-studio-id="..."]')` call in `useEffect` performs a
deep search and will still find it through the group container. No change needed to `useEffect`.

When the status filter or search narrows results to a single status group, `groups.length === 1`
and no section divider is shown — the divider only appears when there's something to divide.

---

## Fix #5 — Copy-to-clipboard for slugs

Replace the plain slug `<span>` in `StudioRow` with a clickable button that copies on click:

**Add to imports:**
```tsx
import {
  ...,
  Copy,
} from "lucide-react";
```

**Replace the slug span:**
```tsx
// Before:
<span
  className="text-xs text-muted-foreground font-mono truncate max-w-[180px]"
  title={studio.slug}
>
  {studio.slug}
</span>

// After:
<button
  type="button"
  onClick={(e) => {
    e.stopPropagation();
    void navigator.clipboard.writeText(studio.slug);
    toast.success("Slug copied");
  }}
  aria-label={`Copy slug ${studio.slug}`}
  className="group flex items-center gap-0.5 text-xs text-muted-foreground
             font-mono hover:text-foreground transition-colors cursor-pointer
             max-w-[180px]"
>
  <span className="truncate" title={studio.slug}>{studio.slug}</span>
  <Copy className="h-2.5 w-2.5 shrink-0 opacity-0 group-hover:opacity-50
                   transition-opacity" />
</button>
```

The `Copy` icon is invisible until hover, keeping the default view clean. The `title`
tooltip stays on the inner `<span>` for the native fallback on non-hover environments.
`toast.success` uses the existing `sonner` import.

---

## Fix #6 — Action button hierarchy: weight tracks risk, not workflow step

**Current problem:** "Activate" is the only filled/primary button on the page
(`variant` defaults to `"default"` = filled in shadcn). For Cancelled studios it dominates
visually, even though reactivating a dead subscription is a lower-urgency action than
addressing Suspended or PastDue studios at the top of the sorted list.

**New rule:**
- For **Cancelled** studios: "Activate" → `variant="outline"` (de-emphasised; revival is
  optional, not urgent)
- For **PastDue** and **GracePeriod** studios: "Activate" stays `variant="default"` (urgent
  — billing is actively failing and manual activation may be the intended resolution)
- "Reactivate Studio" (platform unsuspend): → `variant="default"` when `isSuspended`
  (currently `variant="ghost"`, which is too low-emphasis for the main recovery action)

In `StudioRow`:

```tsx
{/* 2. Activate (primary for at-risk, outline for already-dead) */}
{!anyExpanded && canActivate && (
  <Button
    size="sm"
    variant={badgeStatus === "Cancelled" ? "outline" : "default"}
    className="h-7 text-xs gap-1"
    onClick={() => setActivating(true)}
    aria-label={`Activate subscription for ${studio.name}`}
  >
    <Banknote className="h-3.5 w-3.5" />
    Activate
  </Button>
)}

{/* 3. Suspend Studio / Reactivate Studio */}
{confirmPlatform ? (
  <>
    <span className="text-xs text-muted-foreground">
      {confirmPlatform === "suspend" ? "Suspend?" : "Reactivate?"}
    </span>
    <Button
      size="sm"
      variant={confirmPlatform === "suspend" ? "destructive" : "default"}
      className="h-7 px-2 text-xs"
      disabled={suspending || unsuspending}
      onClick={executePlatform}
    >
      {(suspending || unsuspending) ? <Loader2 className="h-3 w-3 animate-spin" /> : "Yes"}
    </Button>
    <Button size="sm" variant="ghost" className="h-7 px-2 text-xs"
      onClick={() => setConfirmPlatform(null)}>
      No
    </Button>
  </>
) : (
  !anyExpanded && (
    <Button
      size="sm"
      variant={isSuspended ? "default" : "ghost"}
      className="h-7 px-2 text-xs gap-1"
      onClick={() => setConfirmPlatform(isSuspended ? "unsuspend" : "suspend")}
    >
      {isSuspended
        ? <><PlayCircle className="h-3.5 w-3.5" /> Reactivate</>
        : <><PauseCircle className="h-3.5 w-3.5" /> Suspend</>}
    </Button>
  )
)}
```

Note: button labels ("Reactivate", "Suspend") do NOT change — the existing test at line 215
checks `{ name: /reactivate/i }` and the test at line 370 uses `/^suspend$/i` matching exact
text content "Suspend". Keep these exact strings.

---

## Fix #7 — Widen container from `max-w-3xl` to `max-w-5xl`

The list is centered in `max-w-3xl` (768px), leaving large gutters on wide monitors and
forcing the 4-button action row on Cancelled-subscription cards to wrap unnecessarily.

Change both container elements:

```tsx
// Search bar container (line ~497):
<div className="max-w-5xl mx-auto px-4 pt-4 flex gap-2 flex-wrap">

// Main list container (line ~531):
<main className="max-w-5xl mx-auto px-4 py-4">
```

`max-w-5xl` (1024px) is consistent with the `IssuerStudioDetailPage` (widened in the
previous overnight prompt). Do not change the `StudioRow` card width — it will naturally
expand to fill the wider container.

---

## Fix #8 — "Grant Extension (+7 days)" label for expired trials

The button label "Grant extension" (for expired trials) doesn't show the day count, while
"Extend Trial (+7 days)" (for active trials) does. Make them consistent:

```tsx
// Before:
{trialExpired ? "Grant extension" : "Extend Trial (+7 days)"}

// After:
{trialExpired ? "Grant Extension (+7 days)" : "Extend Trial (+7 days)"}
```

The default in the form is 7 days, so the label accurately reflects the default. Apply
this change in **all places** this pattern appears in `IssuerStudioListPage.tsx` — there
are two (the button text in the row action area, line ~262, and possibly in the inline
form). Use `replace_all: true` in the Edit tool.

---

## Phase 2 — Tests

### Existing tests — verify no regressions

Read `IssuerStudioListPage.test.tsx` before writing new tests. The following tests are
sensitive to changes in this prompt and must be verified:

- **Line 370-373** (`Cancel Subscription button appears last...`): Uses
  `getAllByRole("button")` then `findIndex` with `/^suspend$/i`. The regex requires
  button textContent to be exactly "Suspend" (trimmed). Confirm the button label stays
  "Suspend" (not "Suspend Studio") in the list page — it does, since the rename was only
  applied to `IssuerStudioDetailPage` in the previous prompts.

- **Line 288-294** (`shows Extend trial button...`): Uses `{ name: /extend trial|grant extension/i }`.
  After Fix #8, "Grant Extension (+7 days)" still matches `/grant extension/i`. ✓

- **Line 347-352** (`does not render 'No plan' in studio meta lines for studios in trial`):
  After Fix #2C, Trialing studios still show "In Trial" (not "No plan"). The `SUB_ACTIVE`
  seed has `planName: "Pro"`, so Active studio shows "Pro". No `<p>` element will contain
  /no plan/i with the current test data. ✓

### New tests to add

Append to the existing `describe("IssuerStudioListPage", ...)` block:

```typescript
// ── Fix #1: z-index / header ──────────────────────────────────────────────────

it("page header has z-20 class to prevent list content overlap", () => {
  renderPage();
  // Header element uses sticky top-0 z-20 — check via class
  const header = document.querySelector("header");
  expect(header?.className).toMatch(/z-20/);
});

// ── Fix #2: Plan display ──────────────────────────────────────────────────────

it("shows 'No plan assigned' for Active studio with null planName", async () => {
  const STUDIO_NO_PLAN: StudioResponse = {
    ...STUDIO_ACTIVE,
    id:   "s-noplan",
    name: "No Plan Studio",
    slug: "no-plan-studio",
  };
  const SUB_NO_PLAN: PlatformSubscriptionResponse = {
    studioId:         "s-noplan",
    studioName:       "No Plan Studio",
    studioSlug:       "no-plan-studio",
    subscriptionId:   "sub-np",
    status:           "Active",
    planName:         null,
    trialExpiresAt:   "",
    currentPeriodEnd: new Date(Date.now() + 30 * 86_400_000).toISOString(),
    isSuspended:      false,
  };
  server.use(
    http.get("http://localhost/api/v1/studios", () =>
      HttpResponse.json([STUDIO_NO_PLAN]),
    ),
    http.get("http://localhost/api/v1/platform/subscriptions", () =>
      HttpResponse.json([SUB_NO_PLAN]),
    ),
  );
  renderPage();
  expect(await screen.findByText("No Plan Studio")).toBeInTheDocument();
  // Meta line shows "No plan assigned" not "—"
  expect(screen.getByText(/no plan assigned/i)).toBeInTheDocument();
});

it("shows AlertTriangle icon for Active studio with null planName", async () => {
  const STUDIO_NO_PLAN: StudioResponse = {
    ...STUDIO_ACTIVE,
    id:   "s-noplan2",
    name: "No Plan Studio 2",
    slug: "no-plan-studio-2",
  };
  server.use(
    http.get("http://localhost/api/v1/studios", () =>
      HttpResponse.json([STUDIO_NO_PLAN]),
    ),
    http.get("http://localhost/api/v1/platform/subscriptions", () =>
      HttpResponse.json([{
        studioId: "s-noplan2", studioName: "No Plan Studio 2",
        studioSlug: "no-plan-studio-2", subscriptionId: "sub-np2",
        status: "Active", planName: null, trialExpiresAt: "",
        currentPeriodEnd: new Date(Date.now() + 30 * 86_400_000).toISOString(),
        isSuspended: false,
      }]),
    ),
  );
  renderPage();
  await screen.findByText("No Plan Studio 2");
  // The warning span title includes "no linked plan"
  const warnEl = document.querySelector('[title*="no linked plan"]');
  expect(warnEl).not.toBeNull();
});

// ── Fix #3: aria-labels ───────────────────────────────────────────────────────

it("status filter select has aria-label", async () => {
  renderPage();
  await screen.findByText("Ink Soul");
  expect(screen.getByRole("combobox", { name: /filter by status/i })).toBeInTheDocument();
});

it("plan filter select has aria-label", async () => {
  renderPage();
  await screen.findByText("Ink Soul");
  expect(screen.getByRole("combobox", { name: /filter by plan/i })).toBeInTheDocument();
});

// ── Fix #4: Group dividers ────────────────────────────────────────────────────

it("shows status group divider headers when multiple status groups are present", async () => {
  renderPage();
  await screen.findByText("Ink Soul");
  // Seed has Suspended, Trialing, Active studios → 3 groups → dividers show
  // Each divider shows a pill with the status label and studio count
  expect(screen.getByText("1 studio", { selector: "span" })).toBeInTheDocument(); // Suspended group
});

it("does NOT show group divider when filtered to a single status", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Ink Soul");

  // Filter to Suspended only
  const statusSelect = screen.getByRole("combobox", { name: /filter by status/i });
  await user.selectOptions(statusSelect, "Suspended");

  // Only one group → no "X studios" divider text
  expect(screen.queryByText(/1 studio/, { selector: "span.rounded-full" })).not.toBeInTheDocument();
});

// ── Fix #5: Copy slug ─────────────────────────────────────────────────────────

it("slug is wrapped in a button with accessible copy label", async () => {
  renderPage();
  await screen.findByText("Ink Soul");
  expect(screen.getByRole("button", { name: /copy slug ink-soul/i })).toBeInTheDocument();
});

// ── Fix #6: Button hierarchy ──────────────────────────────────────────────────

it("Activate button for a Cancelled studio is an outline button, not filled", async () => {
  server.use(
    http.get("http://localhost/api/v1/studios", () =>
      HttpResponse.json([STUDIO_ACTIVE]),
    ),
    http.get("http://localhost/api/v1/platform/subscriptions", () =>
      HttpResponse.json([{
        ...SUB_ACTIVE,
        status: "Cancelled",
        isSuspended: false,
      }]),
    ),
  );
  renderPage();
  await screen.findByText("Ink Soul");
  const activateBtn = screen.getByRole("button", { name: /activate/i });
  // shadcn outline variant uses border class; default (filled) variant uses bg-primary
  expect(activateBtn.className).toMatch(/border/);
  expect(activateBtn.className).not.toMatch(/bg-primary/);
});

it("Reactivate button for a Suspended studio is a filled (default) button", async () => {
  renderPage();
  await screen.findByText("Suspended Studio");
  const reactivateBtn = screen.getByRole("button", { name: /reactivate/i });
  // default variant uses bg-primary
  expect(reactivateBtn.className).toMatch(/bg-primary/);
});

// ── Fix #8: Grant Extension label ─────────────────────────────────────────────

it("expired trial shows 'Grant Extension (+7 days)' label", async () => {
  server.use(
    http.get("http://localhost/api/v1/studios", () =>
      HttpResponse.json([{
        ...STUDIO_ACTIVE,
        id:             "s-expired",
        name:           "Expired Trial Studio",
        slug:           "expired-trial",
        trialExpiresAt: new Date(Date.now() - 5 * 86_400_000).toISOString(),
      }]),
    ),
    http.get("http://localhost/api/v1/platform/subscriptions", () =>
      HttpResponse.json([{
        ...SUB_ACTIVE,
        studioId:       "s-expired",
        studioName:     "Expired Trial Studio",
        studioSlug:     "expired-trial",
        status:         "Trialing",
        trialExpiresAt: new Date(Date.now() - 5 * 86_400_000).toISOString(),
        isSuspended:    false,
      }]),
    ),
  );
  renderPage();
  await screen.findByText("Expired Trial Studio");
  expect(
    screen.getByRole("button", { name: /grant extension \(\+7 days\)/i })
  ).toBeInTheDocument();
});
```

---

## Phase 3 — Quality Gates

```bash
pnpm --filter frontend test -- --reporter=verbose 2>&1 | grep -E "(PASS|FAIL|✓|✗)"
pnpm --filter frontend lint 2>&1 | grep -E "^.*error" | head -20
dotnet build Pena_e_Arte.sln 2>&1 | grep -E " error " | grep -v "^Build succeeded"
dotnet test tests/Pena_e_Arte.UnitTests/ 2>&1 | tail -5
```

All must pass. Fix any errors before marking complete.

---

## Phase 4 — Forbidden Actions

- Do not rename "Suspend" / "Reactivate" button labels in the list page — tests assert
  on the exact text and the rename was intentionally applied only to the detail page.
- Do not add virtualization or pagination — flagged as a future task once studio count
  grows; not in scope for this prompt.
- Do not introduce bulk-select checkboxes — longer-term improvement, not in scope.
- Do not replace `<select>` with shadcn `<Select>` component — a larger refactor; the
  `aria-label` fix (Fix #3) is sufficient for now.
- Do not add a new NuGet or npm package.

---

## Completion Checklist

- [ ] Fix #1 — Header `z-10` → `z-20`
- [ ] Fix #2A — `ActivateSubscriptionManuallyCommand.cs`: `Plan = plan` set on navigation property
- [ ] Fix #2B — `CreateSubscriptionHandler.cs` investigated; `Map()` handles null plan
- [ ] Fix #2C — `planDisplay` fallback: "No plan assigned" + `AlertTriangle` warning icon
- [ ] Fix #2C — `periodText && <> · {periodText}</>` null-safe rendering
- [ ] Fix #3 — `aria-label` on status and plan `<select>` filters
- [ ] Fix #4 — `groups` `useMemo` computed; group divider section headers rendered
- [ ] Fix #4 — `data-studio-id` still on inner wrapper; `useEffect` `querySelector` still works
- [ ] Fix #5 — Slug copy button with `Copy` icon and `toast.success`
- [ ] Fix #6 — "Activate" → `variant="outline"` for Cancelled; `variant="default"` for PastDue/GracePeriod
- [ ] Fix #6 — "Reactivate Studio" → `variant="default"` when `isSuspended`
- [ ] Fix #7 — Container widened from `max-w-3xl` to `max-w-5xl`
- [ ] Fix #8 — "Grant Extension (+7 days)" label for expired trials
- [ ] All existing tests pass (verify line 370-373 ordering test specifically)
- [ ] 10 new tests pass
- [ ] `pnpm test` clean
- [ ] `dotnet build` clean
