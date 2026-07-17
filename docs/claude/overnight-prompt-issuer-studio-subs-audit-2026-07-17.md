# Overnight Prompt — Issuer Studio Detail + Subscriptions List: UX Audit Fixes
**Date:** 2026-07-17
**Files changed:** ~7 (2 backend, 5 frontend)
**Type:** Full-stack — two critical bugs + seven visual/UX fixes

---

## Context

A UX audit identified three critical issues and several visual polish items across
`IssuerStudioDetailPage`, `SubscriptionOversightPage`, and `IssuerStudioListPage`.
The most important fix is a data-trust bug: the same studio shows two different
statuses on two different screens simultaneously. Everything else is polish.

Read ALL source files listed in Phase 0 before writing any code.

---

## Phase 0 — Required Reading

```
frontend/src/features/platform/components/IssuerStudioDetailPage.tsx
frontend/src/features/platform/components/SubscriptionOversightPage.tsx
frontend/src/features/platform/components/IssuerStudioListPage.tsx
frontend/src/features/platform/platform.types.ts
frontend/src/features/platform/__tests__/IssuerStudioDetailPage.test.tsx
frontend/src/features/platform/__tests__/SubscriptionOversightPage.test.tsx
frontend/src/features/platform/__tests__/IssuerStudioListPage.test.tsx
Pena_e_Arte.Contracts/Responses/PlatformSubscriptionResponse.cs
Pena_e_Arte.Application/Platform/Queries/GetPlatformSubscriptionsQuery.cs
docs/claude/conventions.md
docs/claude/architecture.md
```

---

## Critical Bug #1 — Status contradiction across screens

### Root cause

`SubscriptionOversightPage` renders `STATUS_LABELS[sub.status]` directly from
`PlatformSubscriptionResponse.Status`. It has no visibility into `Studio.IsActive`.
So a studio that is `Suspended` (i.e., `Studio.IsActive = false`) appears as "Active"
on the Subscriptions page because its underlying subscription record still has
`Status = Active`.

`IssuerStudioListPage` and `IssuerStudioDetailPage` both correctly override with
`badgeStatus = isSuspended ? "Suspended" : subStatus`, but `SubscriptionOversightPage`
does not, because `PlatformSubscriptionResponse` never carried `IsSuspended`.

### Fix — Backend: add `IsSuspended` to `PlatformSubscriptionResponse`

**`Pena_e_Arte.Contracts/Responses/PlatformSubscriptionResponse.cs`**

Read the current record definition. Add a `bool IsSuspended` parameter — its position
in the positional record must match the change to the constructor call in the handler.
The field name is `IsSuspended` (C# convention).

**`Pena_e_Arte.Application/Platform/Queries/GetPlatformSubscriptionsQuery.cs`**

In `GetPlatformSubscriptionsHandler.Handle`, the `studios.Select(s => new PlatformSubscriptionResponse(...))` 
call currently maps subscription fields. Add `!s.IsActive` for the new `IsSuspended` field.
The mapping must remain in the same positional order as the record definition above.

No new migration, no new endpoint. This is a purely additive change to an existing response.

### Fix — Frontend: propagate `isSuspended` everywhere

**`frontend/src/features/platform/platform.types.ts`**

Add to `PlatformSubscriptionResponse`:
```typescript
isSuspended: boolean;
```

**`frontend/src/features/platform/components/SubscriptionOversightPage.tsx`**

1. Add `Suspended` to `STATUS_CLASSES`:
   ```typescript
   Suspended: "bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-300",
   ```

2. Add `Suspended` to `STATUS_LABELS`:
   ```typescript
   Suspended: "Suspended",
   ```

3. Add `"Suspended"` to `ALL_STATUSES` array (at the front — suspended studios need
   the most attention):
   ```typescript
   const ALL_STATUSES = [
     "Suspended", "Active", "Trialing", "GracePeriod",
     "PastDue", "Cancelled", "NoSubscription",
   ];
   ```

4. In `SubscriptionRow`, derive the effective status before computing `statusClass`:
   ```typescript
   const effectiveStatus = sub.isSuspended ? "Suspended" : sub.status;
   const statusClass     = STATUS_CLASSES[effectiveStatus] ?? STATUS_CLASSES.NoSubscription;
   const canActivate     = CASH_ACTIVATABLE.has(sub.status);  // use raw status for action gates
   const canCancel       = CANCELLABLE.has(sub.status);
   ```

   Replace `STATUS_LABELS[sub.status] ?? sub.status` in the pill with
   `STATUS_LABELS[effectiveStatus] ?? effectiveStatus`.

5. When `sub.isSuspended`, the row has no reactivation button (that's a studio-access
   action done from the detail page, not the subscription page). Instead, add a visual
   cue on the card: add `border-amber-400/40 dark:border-amber-600/30` to the `<Card>`
   className when `sub.isSuspended`:
   ```tsx
   <Card className={sub.isSuspended ? "border-amber-400/40 dark:border-amber-600/30" : ""}>
   ```

6. In `SubscriptionOversightPage`, update the filter logic:
   ```typescript
   const baseFiltered = subscriptions?.filter((s) => {
     const effective = s.isSuspended ? "Suspended" : s.status;
     return statusFilter ? effective === statusFilter : true;
   }) ?? [];
   ```

---

## Critical Bug #2 — Stale "Trial expiry: Expired" shown for paid plans

### Root cause

Three places render trial expiry info based solely on "does `trialDate` exist?" rather
than "is the studio actually in a trial-relevant state?". A studio that graduated from
trial to a paid Active subscription still has `trialExpiresAt` set in the DB — this is
correct for data-retention reasons — but should not surface that date in the UI when
the studio is actively paying.

**Trial-relevant states** (the only ones where surfacing trial dates makes UX sense):
`"Trialing" | "GracePeriod" | "NoSubscription"`

### Fix A — `IssuerStudioDetailPage.tsx`

**Current** (two unconditional conditional fields):
```tsx
{sub?.currentPeriodEnd && sub.status === "Active" && (
  <div>Renews...</div>
)}
{trialDate && (
  <div>Trial expiry...</div>
)}
```

**Replace with:**
```tsx
{/* Renews: only for active, non-suspended studios */}
{sub?.currentPeriodEnd && sub.status === "Active" && !isSuspended && (
  <div>
    <span className="text-muted-foreground">Renews</span>
    <p>{fmt(sub.currentPeriodEnd)}</p>
  </div>
)}
{/* Trial expiry: only when subscription is in a trial-relevant state */}
{trialDate && (subStatus === "Trialing" || subStatus === "GracePeriod" || subStatus === "NoSubscription") && (
  <div>
    <span className="text-muted-foreground">Trial expiry</span>
    <p className="flex items-center gap-1.5 flex-wrap">
      {fmt(trialDate)}
      {trialExpired && (
        <span className="inline-flex items-center px-1.5 py-0.5 rounded-full text-[10px] font-medium
                         bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300">
          Expired
        </span>
      )}
    </p>
  </div>
)}
```

The `!isSuspended` guard on "Renews" removes the misleading "Renews: 14 Jul 2026" that
appears next to a "Suspended" badge — billing is paused, the renewal date is irrelevant.

### Fix B — `SubscriptionOversightPage.tsx`

In `SubscriptionRow`, the trial info currently renders whenever `sub.trialExpiresAt` is
set, inline with the plan name:

```tsx
{sub.trialExpiresAt && (
  <>{" · "}{trialExpired ? `Trial expired ${fmt(sub.trialExpiresAt)}` : `Trial ends ${fmt(sub.trialExpiresAt)}`}</>
)}
```

Replace the guard:
```tsx
{sub.trialExpiresAt &&
  (effectiveStatus === "Trialing" || effectiveStatus === "GracePeriod" || effectiveStatus === "NoSubscription") && (
  <>{" · "}{trialExpired ? `Trial expired ${fmt(sub.trialExpiresAt)}` : `Trial ends ${fmt(sub.trialExpiresAt)}`}</>
)}
```

Note: use `effectiveStatus` (which you computed in Bug #1 Fix step 4 above), not `sub.status`.

### Fix C — `IssuerStudioListPage.tsx`

In `StudioRow`, the `periodText` computation falls back to trial date for unhandled statuses
(e.g. `Cancelled`). Change the fallback to only apply when in a trial-relevant state:

```typescript
const periodText = (() => {
  if (sub?.status === "Active" && sub?.currentPeriodEnd && !isSuspended) {
    return `Renews: ${fmt(sub.currentPeriodEnd)}`;
  }
  if (sub?.status === "GracePeriod") {
    return `Grace ends: ${fmt(sub.currentPeriodEnd)}`;
  }
  if (sub?.status === "PastDue" && sub?.currentPeriodEnd) {
    return `Overdue since: ${fmt(sub.currentPeriodEnd)}`;
  }
  if (sub?.status === "Cancelled") {
    return `Cancelled — ended ${sub.currentPeriodEnd ? fmt(sub.currentPeriodEnd) : ""}`.trim();
  }
  // Trial dates only for trial-relevant states
  const isTrialState = !sub || sub.status === "Trialing" || sub.status === "NoSubscription";
  if (isTrialState && trialDate) {
    return trialExpired ? `Trial expired: ${fmt(trialDate)}` : `Trial ends: ${fmt(trialDate)}`;
  }
  return null;
})();
```

Update the rendering to handle `periodText` being `null`:
```tsx
{periodText && <>{" · "}{periodText}</>}
```
instead of always interpolating `" · "}{periodText}`.

---

## Critical Issue #3 — Inverted action hierarchy in Actions card

### Context

On `IssuerStudioDetailPage`, when a studio is suspended:
- "Reactivate Studio" is `variant="ghost"` — lowest visual weight
- "Cancel Subscription" is `variant="outline"` with destructive coloring — draws eye first
- This sends the admin toward the wrong button

### Fix — `IssuerStudioDetailPage.tsx`

When `isSuspended`, make "Reactivate Studio" a filled default button and demote
"Cancel Subscription" to match:

```tsx
<Button
  size="sm"
  variant={isSuspended ? "default" : "ghost"}
  className="h-9 text-xs gap-1"
  onClick={() => setConfirmPlatform(isSuspended ? "unsuspend" : "suspend")}
>
  {isSuspended
    ? <><PlayCircle className="h-3.5 w-3.5" /> Reactivate Studio</>
    : <><PauseCircle className="h-3.5 w-3.5" /> Suspend Studio</>}
</Button>
```

The `variant="default"` produces a filled primary button when the studio is suspended,
correctly signalling "this is the likely intended action." When NOT suspended, `ghost`
keeps the suspend action de-emphasised (correct, as it's a rare operation).

---

## Visual / UX Fix #4 — Suspended color token (amber, not red)

Suspended and Cancelled both use `bg-red-100 text-red-700` in `IssuerStudioDetailPage`
and `IssuerStudioListPage`. They convey different business events. Fix:

In **both** `IssuerStudioDetailPage.tsx` and `IssuerStudioListPage.tsx`, change the
`Suspended` entry in `STATUS_CLASSES`:

```typescript
// Before:
Suspended: "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300",

// After:
Suspended: "bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-300",
```

`SubscriptionOversightPage` will also get this (you're adding it fresh in Bug #1 Fix).

---

## Visual / UX Fix #5 — Card title typographic hierarchy

In `IssuerStudioDetailPage.tsx`, the `CardTitle` for the Studio Info card currently uses:
```tsx
<CardTitle className="text-sm flex items-center gap-2">
```

Change to:
```tsx
<CardTitle className="text-base font-semibold flex items-center gap-2">
```

`text-base` (16px) vs `text-xs` (12px) field labels gives a meaningful ~4px size
differential. Do this ONLY for the Studio Info card's `CardTitle` — the Actions card
and Studio Overview card headings stay at `text-sm`.

---

## Visual / UX Fix #6 — "OWNER" micro-label legibility

In `IssuerStudioDetailPage.tsx`, the Studio Overview card's owner section label:

```tsx
// Before:
<p className="text-[10px] text-muted-foreground font-medium uppercase tracking-wider">
  Owner
</p>

// After:
<p className="text-[11px] text-muted-foreground font-medium uppercase">
  Owner
</p>
```

Removing `tracking-wider` from a 10px uppercase label meaningfully improves character
legibility at small sizes. Bumping to 11px gives a minimum viable point size.

---

## Visual / UX Fix #7 — "Appts" → "Appointments" (abbreviation consistency)

In `IssuerStudioDetailPage.tsx`, the Studio Overview metrics row:

```tsx
// Before:
<p className="text-[10px] text-muted-foreground mt-0.5">Appts</p>

// After:
<p className="text-[10px] text-muted-foreground mt-0.5">Appointments</p>
```

"Artists" and "Clients" are spelled out fully. "Appointments" follows suit.

---

## Visual / UX Fix #8 — Sticky Actions card

In `IssuerStudioDetailPage.tsx`, the right-column Actions card sits in a
`lg:grid-cols-[1fr_288px]` layout with `lg:items-start`. The Actions card is short;
the left column is long — this creates empty dead space below Actions.

Change the right column `<div>` to sticky:
```tsx
{/* Right column */}
<div className="lg:sticky lg:top-[72px]">
  <Card>...</Card>  {/* Actions card */}
</div>
```

`72px` matches the sticky header height (`py-3` top + `py-3` bottom + border + content ≈
`12 + 12 + 1 + ~47px` ≈ 72px). Use `top-[72px]` — do not use a CSS custom property here
unless `--topbar-h` is already defined in the global stylesheet (check before using it;
if defined, use `lg:top-[calc(var(--topbar-h)+0.5rem)]` instead).

---

## Visual / UX Fix #9 — Studio Overview skeleton matches loaded layout

In `IssuerStudioDetailPage.tsx`, the Studio Overview card shows three stacked `Skeleton`
lines while loading, but the loaded state is a horizontal 3-column metrics grid. This
causes a brief layout jump (CLS) when data arrives.

Replace the skeleton:
```tsx
{/* Current skeleton — causes layout jump: */}
{summaryLoading ? (
  <div className="space-y-2">
    <Skeleton className="h-4 w-full" />
    <Skeleton className="h-4 w-2/3" />
    <Skeleton className="h-4 w-1/2" />
  </div>
) : ...}

{/* Replacement — matches loaded state shape: */}
{summaryLoading ? (
  <div className="space-y-3">
    {/* Owner section skeleton */}
    <div className="space-y-1.5">
      <Skeleton className="h-3 w-10" />   {/* "Owner" label */}
      <Skeleton className="h-4 w-32" />   {/* display name */}
      <Skeleton className="h-3.5 w-44" /> {/* email */}
    </div>
    {/* Metrics skeleton — 3 columns */}
    <div className="border-t pt-3 grid grid-cols-3 gap-2 text-center">
      <div className="flex flex-col items-center gap-1">
        <Skeleton className="h-6 w-8" />
        <Skeleton className="h-2.5 w-10" />
      </div>
      <div className="flex flex-col items-center gap-1">
        <Skeleton className="h-6 w-8" />
        <Skeleton className="h-2.5 w-10" />
      </div>
      <div className="flex flex-col items-center gap-1">
        <Skeleton className="h-6 w-8" />
        <Skeleton className="h-2.5 w-14" />
      </div>
    </div>
  </div>
) : summary ? (...) : (...)}
```

---

## Visual / UX Fix #10 — Search input accessible label (`SubscriptionOversightPage`)

The search input in `SubscriptionOversightPage` uses placeholder text only. Add a
visible label (visually hidden is acceptable) or at minimum an `aria-label`:

```tsx
<Input
  aria-label="Search subscriptions by studio name or slug"
  placeholder="Search by studio name or slug…"
  value={search}
  onChange={(e) => setSearch(e.target.value)}
  className="pl-8 h-8 text-sm"
/>
```

The same fix applies to `IssuerStudioListPage`:
```tsx
<Input
  aria-label="Search studios by name or slug"
  placeholder="Search by name or slug…"
  ...
/>
```

---

## Phase 2 — Tests

### Existing tests — do not break

After reading each test file, confirm all existing tests still pass. The changes in this
prompt are non-breaking for tests UNLESS:
- A test asserts on the text "Active" for a studio that should now show "Suspended" on
  the subscriptions page — check `SubscriptionOversightPage.test.tsx`
- A test asserts on trial expiry text for an Active studio — this text will now be hidden

Adjust any test that was asserting on the old (incorrect) behavior. Document each
changed test with a comment: `// Updated: previously asserted stale trial text for active studio`.

### New tests — `IssuerStudioDetailPage.test.tsx`

Add an MSW server override within a `describe` block or individual test to set
`STUDIO.isActive = false` and add an entry with `isSuspended: true` to the SUB mock.
Then:

```typescript
describe("when studio is suspended", () => {
  beforeEach(() => {
    server.use(
      http.get("http://localhost/api/v1/studios/s1", () =>
        HttpResponse.json({ ...STUDIO, isActive: false }),
      ),
      http.get("http://localhost/api/v1/platform/subscriptions", () =>
        HttpResponse.json([{ ...SUB, isSuspended: true }]),
      ),
    );
  });

  it("shows Suspended badge in amber (not red) in the card header", async () => {
    renderPage();
    await screen.findAllByText("Ink Soul");
    const badge = screen.getByText("Suspended");
    expect(badge.className).toMatch(/amber/);
    expect(badge.className).not.toMatch(/red/);
  });

  it("Reactivate Studio button has variant=default (filled)", async () => {
    renderPage();
    await screen.findAllByText("Ink Soul");
    // The button should not be ghost — check it renders with a bg class
    const btn = screen.getByRole("button", { name: /reactivate studio/i });
    // shadcn default variant includes bg-primary class
    expect(btn.className).toMatch(/bg-primary/);
  });

  it("does NOT show 'Renews' date when studio is suspended", async () => {
    renderPage();
    await screen.findAllByText("Ink Soul");
    expect(screen.queryByText(/renews/i)).not.toBeInTheDocument();
  });
});

describe("trial expiry only shown in trial-relevant states", () => {
  it("does NOT show trial expiry for an Active studio", async () => {
    // SUB has status: "Active" and a future trialExpiresAt (converted studio)
    server.use(
      http.get("http://localhost/api/v1/platform/subscriptions", () =>
        HttpResponse.json([{
          ...SUB,
          status: "Active",
          isSuspended: false,
          trialExpiresAt: new Date(Date.now() - 30 * 86_400_000).toISOString(), // expired 30 days ago
        }]),
      ),
    );
    renderPage();
    await screen.findAllByText("Ink Soul");
    expect(screen.queryByText(/trial expiry/i)).not.toBeInTheDocument();
  });

  it("shows trial expiry for a Trialing studio", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/subscriptions", () =>
        HttpResponse.json([{
          ...SUB,
          status: "Trialing",
          isSuspended: false,
          trialExpiresAt: new Date(Date.now() + 7 * 86_400_000).toISOString(),
        }]),
      ),
    );
    renderPage();
    await screen.findAllByText("Ink Soul");
    expect(await screen.findByText(/trial expiry/i)).toBeInTheDocument();
  });
});
```

### New tests — `SubscriptionOversightPage.test.tsx`

After reading the existing test file, add:

```typescript
it("shows Suspended (amber pill) for a studio with isSuspended: true", async () => {
  server.use(
    http.get("http://localhost/api/v1/platform/subscriptions", () =>
      HttpResponse.json([{ ...SUB, status: "Active", isSuspended: true }]),
    ),
  );
  renderPage();
  const badge = await screen.findByText("Suspended");
  expect(badge).toBeInTheDocument();
  expect(badge.className).toMatch(/amber/);
});

it("does NOT show Active badge for a suspended studio", async () => {
  server.use(
    http.get("http://localhost/api/v1/platform/subscriptions", () =>
      HttpResponse.json([{ ...SUB, status: "Active", isSuspended: true }]),
    ),
  );
  renderPage();
  await screen.findByText("Suspended");
  // "Active" pill should not be present
  expect(screen.queryByText("Active")).not.toBeInTheDocument();
});

it("does NOT show trial expiry text for an Active (paid) studio", async () => {
  server.use(
    http.get("http://localhost/api/v1/platform/subscriptions", () =>
      HttpResponse.json([{
        ...SUB,
        status: "Active",
        isSuspended: false,
        trialExpiresAt: new Date(Date.now() - 60 * 86_400_000).toISOString(),
      }]),
    ),
  );
  renderPage();
  await screen.findByText(SUB.studioName);
  expect(screen.queryByText(/trial expired/i)).not.toBeInTheDocument();
  expect(screen.queryByText(/trial ends/i)).not.toBeInTheDocument();
});
```

---

## Phase 3 — Quality Gates

```bash
# Frontend
pnpm --filter frontend test -- --reporter=verbose 2>&1 | grep -E "(PASS|FAIL|✓|✗)"
pnpm --filter frontend lint 2>&1 | grep -E "^.*error" | head -20

# Backend
dotnet build Pena_e_Arte.sln 2>&1 | grep -E " error " | grep -v "^Build succeeded"
dotnet test tests/Pena_e_Arte.UnitTests/ 2>&1 | tail -5
```

All tests must pass. All TypeScript and C# errors must be resolved.

---

## Phase 4 — Forbidden Actions

- Do not rename `STATUS_CLASSES` or `STATUS_LABELS` — extend them in place.
- Do not change the `CANCELLABLE` or `CASH_ACTIVATABLE` sets — these use raw `sub.status`
  intentionally (subscription actions are based on subscription state, not access state).
- Do not add `IgnoreQueryFilters` to any query that isn't IssuerOnly.
- Do not add any new npm package or NuGet package.
- Do not introduce a shared `StudioStatusHelper` utility unless you can do it without
  adding a new file that requires touching more than 2 existing import lists. The fix
  here is adding `isSuspended` to the response DTO and letting each consumer compute
  `effectiveStatus` locally — this is sufficient for now.

---

## Completion Checklist

### Critical Bug #1 — Status contradiction
- [ ] `PlatformSubscriptionResponse.cs` — `IsSuspended: bool` added
- [ ] `GetPlatformSubscriptionsHandler` — `!s.IsActive` mapped to `IsSuspended`
- [ ] `platform.types.ts` — `isSuspended: boolean` added
- [ ] `SubscriptionOversightPage` — `Suspended` in STATUS_CLASSES (amber), STATUS_LABELS, ALL_STATUSES
- [ ] `SubscriptionOversightPage` — `effectiveStatus` computed per row, pill uses it
- [ ] `SubscriptionOversightPage` — filter logic uses `effectiveStatus`
- [ ] `SubscriptionOversightPage` — suspended card gets amber border

### Critical Bug #2 — Stale trial expiry
- [ ] `IssuerStudioDetailPage` — trial expiry only shown for Trialing/GracePeriod/NoSubscription
- [ ] `IssuerStudioDetailPage` — "Renews" hidden when `isSuspended`
- [ ] `SubscriptionOversightPage` — trial text only shown for trial-relevant states
- [ ] `IssuerStudioListPage` — `periodText` fallback fixed (no trial text for Cancelled/Active)

### Critical Issue #3 — Action hierarchy
- [ ] `IssuerStudioDetailPage` — "Reactivate Studio" → `variant="default"` when suspended

### Visual fixes
- [ ] Fix #4 — Suspended badge: amber in `IssuerStudioDetailPage` and `IssuerStudioListPage`
- [ ] Fix #5 — Studio Info card `CardTitle` → `text-base font-semibold`
- [ ] Fix #6 — "OWNER" label: `text-[11px]`, no `tracking-wider`
- [ ] Fix #7 — "Appts" → "Appointments"
- [ ] Fix #8 — Actions card sticky (`lg:sticky lg:top-[72px]`)
- [ ] Fix #9 — Studio Overview skeleton matches 3-column loaded layout
- [ ] Fix #10 — `aria-label` on search inputs (SubscriptionOversightPage + IssuerStudioListPage)

### Tests
- [ ] No existing tests broken (adjusted with comments where behavior corrected)
- [ ] Suspended badge amber + correct status tests (IssuerStudioDetailPage)
- [ ] "Renews" hidden for suspended studio test
- [ ] Trial expiry only for Trialing state tests (both pages)
- [ ] `SubscriptionOversightPage` suspended status + trial expiry tests
- [ ] All `pnpm test` pass
- [ ] `dotnet build` clean
