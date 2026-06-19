# Overnight Prompt — Plans Page UI/UX Overhaul (2026-06-17)

> **Scope:** End-to-end polish of `PlanManagementPage.tsx`.
> Backend: add `SubscriberCount` to `PlanResponse` so the UI can show per-plan
> subscriber counts and contextual delete warnings.
> Frontend: layout, typography, color, accessibility, skeleton loader, and copy.
>
> No new npm packages. No new NuGet packages. No database migrations.
> Commit after each numbered task.

---

## 0. Mandatory Reading (Do This First)

```
CLAUDE.md
docs/claude/backend.md
docs/claude/frontend.md
docs/claude/conventions.md
```

Then read these source files:

```
Pena_e_Arte.Contracts/Responses/PlanResponse.cs
Pena_e_Arte.Application/Billing/Queries/GetPlansQuery.cs
Pena_e_Arte.Application/Plans/Commands/CreatePlanCommand.cs
Pena_e_Arte.Application/Plans/Commands/UpdatePlanCommand.cs
Pena_e_Arte.Application/Plans/Commands/DeletePlanCommand.cs
frontend/src/features/billing/billing.types.ts
frontend/src/features/billing/billingApi.ts
frontend/src/features/platform/components/PlanManagementPage.tsx
frontend/src/features/platform/__tests__/PlanManagementPage.test.tsx
tests/Pena_e_Arte.UnitTests/Billing/CreatePlanHandlerTests.cs
tests/Pena_e_Arte.UnitTests/Billing/UpdatePlanHandlerTests.cs
tests/Pena_e_Arte.UnitTests/Billing/DeletePlanHandlerTests.cs
```

---

## 1. Backend — Add `SubscriberCount` to `PlanResponse`

`SubscriberCount` is the number of active subscriptions currently on each plan.
The `DeletePlanHandler` already blocks deletion when subscribers exist — the
frontend just has no way to surface this count in the UI.

### 1a. Update the contract

**File:** `Pena_e_Arte.Contracts/Responses/PlanResponse.cs`

```csharp
namespace Pena_e_Arte.Contracts.Responses;

public record PlanResponse(
    Guid    Id,
    string  Name,
    string  BillingInterval,
    decimal PriceMonthly,
    decimal PriceYearly,
    int     YearlyDiscountPercent,
    bool    AllowBrandingRemoval,
    string? StripePriceIdMonthly,
    string? StripePriceIdYearly,
    int     SubscriberCount);    // ← new — count of Subscriptions with this PlanId
```

### 1b. Update `GetPlansHandler`

**File:** `Pena_e_Arte.Application/Billing/Queries/GetPlansQuery.cs`

Add `SubscriberCount` via a correlated subquery in the LINQ projection:

```csharp
public async Task<List<PlanResponse>> Handle(GetPlansQuery query, CancellationToken ct)
{
    return await db.Plans
        .OrderBy(p => p.PriceMonthly)
        .Select(p => new PlanResponse(
            p.Id,
            p.Name,
            p.BillingInterval.ToString(),
            p.PriceMonthly,
            p.PriceYearly,
            p.YearlyDiscountPercent,
            p.AllowBrandingRemoval,
            p.StripePriceIdMonthly,
            p.StripePriceIdYearly,
            db.Subscriptions.Count(s => s.PlanId == p.Id)))    // ← new
        .ToListAsync(ct);
}
```

> EF Core translates `db.Subscriptions.Count(...)` inside a `Select` into a
> correlated `COUNT(*)` subquery — no N+1 problem.

### 1c. Update `CreatePlanCommand` return

**File:** `Pena_e_Arte.Application/Plans/Commands/CreatePlanCommand.cs`

A newly created plan has zero subscribers. Update the return statement:

```csharp
return new PlanResponse(
    plan.Id, plan.Name, plan.BillingInterval.ToString(),
    plan.PriceMonthly, plan.PriceYearly, plan.YearlyDiscountPercent,
    plan.AllowBrandingRemoval,
    plan.StripePriceIdMonthly, plan.StripePriceIdYearly,
    SubscriberCount: 0);    // ← new
```

### 1d. Update `UpdatePlanCommand` return

**File:** `Pena_e_Arte.Application/Plans/Commands/UpdatePlanCommand.cs`

Read the file first to see its exact structure. After `SaveChangesAsync`, compute
the current subscriber count and return it:

```csharp
int subscriberCount = await db.Subscriptions
    .CountAsync(s => s.PlanId == plan.Id, ct);

return new PlanResponse(
    plan.Id, plan.Name, plan.BillingInterval.ToString(),
    plan.PriceMonthly, plan.PriceYearly, plan.YearlyDiscountPercent,
    plan.AllowBrandingRemoval,
    plan.StripePriceIdMonthly, plan.StripePriceIdYearly,
    SubscriberCount: subscriberCount);    // ← new
```

Run `dotnet build` — must succeed.

**Commit:** `feat(plans): add SubscriberCount to PlanResponse`

---

## 2. Backend Tests — Fix Constructor Calls

Adding a positional parameter to `PlanResponse` breaks every place that constructs
the record directly in tests.

**Search for all `new PlanResponse(` in the test projects:**

```bash
grep -rn "new PlanResponse(" tests/
```

For each match, add `SubscriberCount: 0` (or the appropriate count) as the last
positional argument. The typical pattern will be:

```csharp
// Before
new PlanResponse(plan.Id, plan.Name, "Monthly", 29m, 290m, 17, false, null, null)

// After
new PlanResponse(plan.Id, plan.Name, "Monthly", 29m, 290m, 17, false, null, null, SubscriberCount: 0)
```

Also check `CreatePlanHandlerTests.cs` — it asserts on `result.Name`,
`result.BillingInterval`, etc. No direct construction, so it may just need
a new assertion:

```csharp
result.SubscriberCount.Should().Be(0);
```

Add that assertion to `Handle_ValidRequest_ReturnsPlanResponse` in
`CreatePlanHandlerTests.cs`.

**New file:** `tests/Pena_e_Arte.UnitTests/Billing/GetPlansHandlerTests.cs`

```csharp
using FluentAssertions;
using Pena_e_Arte.Application.Billing.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;
using Xunit;

namespace Pena_e_Arte.UnitTests.Billing;

public class GetPlansHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetPlansHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_NoPlans_ReturnsEmptyList()
    {
        List<PlanResponse> result = await CreateSut().Handle(new GetPlansQuery(), default);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithPlan_ReturnsZeroSubscribers_WhenNoneExist()
    {
        Plan plan = new()
        {
            Id                    = Guid.NewGuid(),
            Name                  = "Starter",
            BillingInterval       = BillingInterval.Monthly,
            PriceMonthly          = 29m,
            PriceYearly           = 290m,
            YearlyDiscountPercent = 17,
        };
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        List<PlanResponse> result = await CreateSut().Handle(new GetPlansQuery(), default);

        result.Single().SubscriberCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithSubscribers_ReturnsCorrectCount()
    {
        Plan plan = new()
        {
            Id                    = Guid.NewGuid(),
            Name                  = "Pro",
            BillingInterval       = BillingInterval.Monthly,
            PriceMonthly          = 49m,
            PriceYearly           = 490m,
            YearlyDiscountPercent = 17,
        };
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();

        // Seed two subscriptions on this plan
        _db.Subscriptions.AddRange(
            new Subscription { Id = Guid.NewGuid(), PlanId = plan.Id, StudioId = Guid.NewGuid(),
                Status = Domain.Enums.SubscriptionStatus.Active,
                TrialExpiresAt = DateTime.UtcNow.AddDays(30),
                CurrentPeriodEnd = DateTime.UtcNow.AddDays(30) },
            new Subscription { Id = Guid.NewGuid(), PlanId = plan.Id, StudioId = Guid.NewGuid(),
                Status = Domain.Enums.SubscriptionStatus.Trialing,
                TrialExpiresAt = DateTime.UtcNow.AddDays(14),
                CurrentPeriodEnd = DateTime.UtcNow.AddDays(14) });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        List<PlanResponse> result = await CreateSut().Handle(new GetPlansQuery(), default);

        result.Single().SubscriberCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_MultiplePlans_SubscriberCountsArePerPlan()
    {
        Plan planA = new() { Id = Guid.NewGuid(), Name = "A", BillingInterval = BillingInterval.Monthly,
            PriceMonthly = 10m, PriceYearly = 100m, YearlyDiscountPercent = 17 };
        Plan planB = new() { Id = Guid.NewGuid(), Name = "B", BillingInterval = BillingInterval.Monthly,
            PriceMonthly = 20m, PriceYearly = 200m, YearlyDiscountPercent = 17 };
        _db.Plans.AddRange(planA, planB);
        await _db.SaveChangesAsync();

        // 1 subscriber on planA, 2 on planB
        _db.Subscriptions.Add(new Subscription { Id = Guid.NewGuid(), PlanId = planA.Id,
            StudioId = Guid.NewGuid(), Status = Domain.Enums.SubscriptionStatus.Active,
            TrialExpiresAt = DateTime.UtcNow.AddDays(30), CurrentPeriodEnd = DateTime.UtcNow.AddDays(30) });
        _db.Subscriptions.AddRange(
            new Subscription { Id = Guid.NewGuid(), PlanId = planB.Id, StudioId = Guid.NewGuid(),
                Status = Domain.Enums.SubscriptionStatus.Active,
                TrialExpiresAt = DateTime.UtcNow.AddDays(30), CurrentPeriodEnd = DateTime.UtcNow.AddDays(30) },
            new Subscription { Id = Guid.NewGuid(), PlanId = planB.Id, StudioId = Guid.NewGuid(),
                Status = Domain.Enums.SubscriptionStatus.Active,
                TrialExpiresAt = DateTime.UtcNow.AddDays(30), CurrentPeriodEnd = DateTime.UtcNow.AddDays(30) });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        List<PlanResponse> result = await CreateSut().Handle(new GetPlansQuery(), default);

        result.OrderBy(r => r.PriceMonthly).First().SubscriberCount.Should().Be(1);
        result.OrderBy(r => r.PriceMonthly).Last().SubscriberCount.Should().Be(2);
    }
}
```

> Adapt entity constructors to match `FakeDbContext.Create()` and your existing
> entity seeding patterns — do not invent a pattern that doesn't exist in other
> tests in the same directory.

Run `dotnet test` — all tests must pass.

**Commit:** `test(plans): GetPlansHandler subscriber count tests, fix constructor calls`

---

## 3. Frontend — Update `PlanResponse` Type

**File:** `frontend/src/features/billing/billing.types.ts`

Add `subscriberCount` to `PlanResponse`:

```typescript
export interface PlanResponse {
  id:                    string;
  name:                  string;
  billingInterval:       "Monthly" | "Yearly";
  priceMonthly:          number;
  priceYearly:           number;
  yearlyDiscountPercent: number;
  allowBrandingRemoval:  boolean;
  stripePriceIdMonthly?: string | null;
  stripePriceIdYearly?:  string | null;
  subscriberCount:       number;   // ← new
}
```

Run `pnpm --dir frontend tsc --noEmit` — must produce zero errors.

**Commit:** `feat(plans): add subscriberCount to PlanResponse TypeScript type`

---

## 4. Frontend — `PlanManagementPage.tsx` Full Overhaul

This is the main task. Apply every sub-section below in a single editing pass on
`frontend/src/features/platform/components/PlanManagementPage.tsx`.

### 4a. Fix `formatCurrency` — international format

```typescript
function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-GB", {
    style:                 "currency",
    currency:              "EUR",
    minimumFractionDigits: 0,
    maximumFractionDigits: 2,
  }).format(amount);
}
```

Produces `€29` or `€29.50` — not `29,00 €`.

### 4b. Layout — wider container + comparison grid

Change the page `main` container from `max-w-xl` to `max-w-4xl`:

```tsx
<main className="max-w-4xl mx-auto px-4 py-6">
```

Inside the main, plan cards are currently a `space-y-3` vertical list. Replace
with a responsive comparison grid when there are plans:

```tsx
{!isLoading && !isError && plans && plans.length > 0 && (
  <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
    {plans.map((p) => (
      <PlanCard key={p.id} plan={p} />
    ))}
  </div>
)}
```

The create form (`creating`) renders above the grid in full width — keep it at
full width (it's a form, not a card):

```tsx
{creating && (
  <div className="max-w-xl mb-6">
    <PlanForm ... />
  </div>
)}
```

### 4c. Typography — plan name hierarchy

In `PlanCard`, change the plan name span from `text-sm font-medium` to
`text-base font-semibold`:

```tsx
<span className="text-base font-semibold">{plan.name}</span>
```

### 4d. Billing interval label

Change `{plan.billingInterval}` (bare text) to a labeled field:

```tsx
<span className="text-xs text-muted-foreground">
  Billing: {plan.billingInterval}
</span>
```

### 4e. Green savings badge for yearly discount

Replace the flat gray inline discount text with a styled green badge when the
discount is greater than 0:

```tsx
{/* Before: */}
<p className="text-xs text-muted-foreground">
  {formatCurrency(plan.priceMonthly)}/mo · {formatCurrency(plan.priceYearly)}/yr
  {" · "}{plan.yearlyDiscountPercent}% yearly discount
</p>

{/* After: */}
<div className="space-y-1 mt-1">
  <p className="text-sm font-mono">
    <span>{formatCurrency(plan.priceMonthly)}<span className="text-xs text-muted-foreground">/mo</span></span>
    <span className="text-muted-foreground mx-1">·</span>
    <span>{formatCurrency(plan.priceYearly)}<span className="text-xs text-muted-foreground">/yr</span></span>
  </p>
  {plan.yearlyDiscountPercent > 0 && (
    <span className="inline-flex items-center text-xs px-2 py-0.5 rounded-full
                     bg-emerald-100 text-emerald-700
                     dark:bg-emerald-900/30 dark:text-emerald-300 font-medium">
      Save {plan.yearlyDiscountPercent}% vs monthly billing
    </span>
  )}
</div>
```

### 4f. Subscriber count badge

Show subscriber count before the action icons. Use the `Users` icon from lucide:

```tsx
import { CreditCard, Edit2, Loader2, Plus, Trash2, Users, X } from "lucide-react";
```

In `PlanCard`, in the action area alongside the edit/delete buttons:

```tsx
<div className="flex items-center gap-2 shrink-0">
  {/* Subscriber count */}
  <span className="flex items-center gap-1 text-xs text-muted-foreground">
    <Users className="h-3.5 w-3.5" />
    {plan.subscriberCount}
  </span>

  {/* Edit */}
  <Button
    size="sm"
    variant="ghost"
    className="h-7 w-7 p-0"
    onClick={() => setEditing(true)}
    aria-label={`Edit ${plan.name} plan`}
    title={`Edit ${plan.name}`}
  >
    <Edit2 className="h-3.5 w-3.5" />
  </Button>

  {/* Delete */}
  {deleting ? (
    <>
      <span className="text-xs text-destructive self-center whitespace-nowrap">
        {plan.subscriberCount > 0
          ? `${plan.subscriberCount} studio${plan.subscriberCount !== 1 ? "s" : ""} on this plan`
          : "Delete?"}
      </span>
      <Button size="sm" variant="destructive" className="h-7 px-2 text-xs"
        disabled={removing} onClick={handleDelete}>
        {removing ? <Loader2 className="h-3 w-3 animate-spin" /> : "Yes"}
      </Button>
      <Button size="sm" variant="ghost" className="h-7 px-2 text-xs"
        onClick={() => setDeleting(false)}>No</Button>
    </>
  ) : (
    <Button
      size="sm"
      variant="ghost"
      className="h-7 w-7 p-0 text-muted-foreground hover:text-destructive transition-colors"
      onClick={() => setDeleting(true)}
      aria-label={`Delete ${plan.name} plan`}
      title={`Delete ${plan.name}`}
    >
      <Trash2 className="h-3.5 w-3.5" />
    </Button>
  )}
</div>
```

### 4g. Delete dialog — contextual subscriber warning

When `deleting` is true and `plan.subscriberCount > 0`, show a more complete
warning above the Yes/No buttons:

```tsx
{deleting && (
  <div className="mt-2 pt-2 border-t space-y-2">
    {plan.subscriberCount > 0 ? (
      <p className="text-xs text-amber-600 dark:text-amber-400">
        <strong>{plan.subscriberCount} studio{plan.subscriberCount !== 1 ? "s" : ""}</strong>{" "}
        {plan.subscriberCount === 1 ? "is" : "are"} on this plan.
        Deleting it will prevent new signups — existing subscribers are not affected.
      </p>
    ) : (
      <p className="text-xs text-muted-foreground">No active subscribers. Safe to delete.</p>
    )}
    <div className="flex items-center gap-2">
      <span className="text-xs text-destructive font-medium">Delete {plan.name}?</span>
      <Button size="sm" variant="destructive" className="h-7 px-2 text-xs"
        disabled={removing} onClick={handleDelete}>
        {removing ? <Loader2 className="h-3 w-3 animate-spin" /> : "Yes, delete"}
      </Button>
      <Button size="sm" variant="ghost" className="h-7 px-2 text-xs"
        onClick={() => setDeleting(false)}>Cancel</Button>
    </div>
  </div>
)}
```

Move the delete confirmation into the card body rather than the header action row —
it's a significant message that needs space. The icon-only trigger stays in the
header; the expanded confirmation panel appears below the card content.

To do this cleanly, restructure `PlanCard` so the card body has two sections:
the info row (always visible) and the confirmation panel (conditional):

```tsx
<Card className="hover:border-border/60 transition-colors">
  <CardContent className="p-4 space-y-3">
    {/* ── Info row ─────────────────────────── */}
    <div className="flex items-start justify-between gap-4">
      {/* left: info */}
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
        <div className="space-y-1 mt-1">
          <p className="text-sm font-mono">
            <span>{formatCurrency(plan.priceMonthly)}
              <span className="text-xs text-muted-foreground">/mo</span>
            </span>
            <span className="text-muted-foreground mx-1">·</span>
            <span>{formatCurrency(plan.priceYearly)}
              <span className="text-xs text-muted-foreground">/yr</span>
            </span>
          </p>
          {plan.yearlyDiscountPercent > 0 && (
            <span className="inline-flex items-center text-xs px-2 py-0.5 rounded-full
                             bg-emerald-100 text-emerald-700
                             dark:bg-emerald-900/30 dark:text-emerald-300 font-medium">
              Save {plan.yearlyDiscountPercent}% vs monthly billing
            </span>
          )}
        </div>
      </div>

      {/* right: subscriber count + actions */}
      <div className="flex items-center gap-2 shrink-0">
        <span className="flex items-center gap-1 text-xs text-muted-foreground" title="Studios on this plan">
          <Users className="h-3.5 w-3.5" />
          {plan.subscriberCount}
        </span>

        {!editing && !deleting && (
          <>
            <Button size="sm" variant="ghost" className="h-7 w-7 p-0"
              onClick={() => setEditing(true)}
              aria-label={`Edit ${plan.name} plan`}
              title="Edit">
              <Edit2 className="h-3.5 w-3.5" />
            </Button>
            <Button size="sm" variant="ghost"
              className="h-7 w-7 p-0 text-muted-foreground hover:text-destructive transition-colors"
              onClick={() => setDeleting(true)}
              aria-label={`Delete ${plan.name} plan`}
              title="Delete">
              <Trash2 className="h-3.5 w-3.5" />
            </Button>
          </>
        )}
      </div>
    </div>

    {/* ── Edit form ────────────────────────── */}
    {editing && (
      <div className="border-t pt-3">
        <PlanForm
          defaultValues={{ ... }}
          onSave={handleUpdate}
          onClose={() => setEditing(false)}
          saving={saving}
        />
      </div>
    )}

    {/* ── Delete confirmation ──────────────── */}
    {deleting && (
      <div className="border-t pt-3 space-y-2">
        {plan.subscriberCount > 0 ? (
          <p className="text-xs text-amber-600 dark:text-amber-400">
            <strong>{plan.subscriberCount} studio{plan.subscriberCount !== 1 ? "s" : ""}</strong>{" "}
            {plan.subscriberCount === 1 ? "is" : "are"} on this plan.
            Deleting it will prevent new signups — existing subscribers are not affected.
          </p>
        ) : (
          <p className="text-xs text-muted-foreground">No active subscribers. Safe to delete.</p>
        )}
        <div className="flex items-center gap-2">
          <span className="text-xs text-destructive font-medium">
            Delete "{plan.name}" permanently?
          </span>
          <Button size="sm" variant="destructive" className="h-7 px-2 text-xs"
            disabled={removing} onClick={handleDelete}>
            {removing ? <Loader2 className="h-3 w-3 animate-spin" /> : "Yes, delete"}
          </Button>
          <Button size="sm" variant="ghost" className="h-7 px-2 text-xs"
            onClick={() => setDeleting(false)}>
            Cancel
          </Button>
        </div>
      </div>
    )}
  </CardContent>
</Card>
```

> Note: The `editing` check in the action buttons area. When `editing` is true,
> the edit form is visible inside the card body — no need to show Edit/Delete
> buttons simultaneously. The `PlanCard` no longer replaces itself with
> `<PlanForm>` as the top-level element; instead it shows the form inside the
> card. This removes the flash of layout shift when clicking Edit.
>
> The new `handleUpdate` should call `setEditing(false)` on success (same as
> before). The `onClose` handler is `() => setEditing(false)`.

Update `handleDelete` to not toggle — it only fires when `deleting` is true:

```typescript
async function handleDelete() {
  await deletePlan(plan.id).unwrap();
  // RTK Query's invalidatesTags will refetch the list automatically
}
```

(The guard `if (!deleting) { setDeleting(true); return; }` pattern is now replaced
by the separate button that sets `deleting` and the separate "Yes, delete" confirm
button.)

### 4h. Skeleton loading state

Replace the spinner with plan-shaped skeleton cards:

```tsx
import { Skeleton } from "@/shared/components/ui/skeleton";

function PlanCardSkeleton() {
  return (
    <Card>
      <CardContent className="p-4 space-y-3">
        <div className="flex items-start justify-between gap-4">
          <div className="space-y-2 flex-1">
            <Skeleton className="h-5 w-24" />
            <Skeleton className="h-4 w-40" />
            <Skeleton className="h-5 w-28 rounded-full" />
          </div>
          <div className="flex items-center gap-1.5">
            <Skeleton className="h-6 w-6" />
            <Skeleton className="h-7 w-7 rounded" />
            <Skeleton className="h-7 w-7 rounded" />
          </div>
        </div>
      </CardContent>
    </Card>
  );
}
```

In `PlanManagementPage`:

```tsx
{isLoading && (
  <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
    <PlanCardSkeleton />
    <PlanCardSkeleton />
    <PlanCardSkeleton />
  </div>
)}
```

Remove `Loader2` from the loading block (it may still be needed inside
`PlanForm`'s Save button and inside the delete confirm — keep the import).

### 4i. Empty state with CTA

Replace the bare text empty state:

```tsx
{/* Before: */}
{!isLoading && !isError && plans?.length === 0 && !creating && (
  <p className="text-center text-sm text-muted-foreground py-16">No plans yet.</p>
)}

{/* After: */}
{!isLoading && !isError && plans?.length === 0 && !creating && (
  <div className="flex flex-col items-center justify-center py-24 gap-3">
    <CreditCard className="h-10 w-10 text-muted-foreground/30" />
    <p className="text-sm text-muted-foreground">No plans yet.</p>
    <p className="text-xs text-muted-foreground max-w-xs text-center">
      Create your first plan to allow studios to subscribe.
    </p>
    <Button size="sm" variant="outline" className="gap-1.5 mt-2"
      onClick={() => setCreating(true)}>
      <Plus className="h-4 w-4" />
      Create first plan
    </Button>
  </div>
)}
```

### 4j. "White-label" badge rename

The `no-branding` badge is developer-speak. Rename:

```tsx
{/* Before: */}
<span className="... bg-purple-100 text-purple-700 ...">
  no-branding
</span>

{/* After: */}
<span className="... bg-purple-100 text-purple-700 ...">
  White-label
</span>
```

### 4k. Remove stale `formatCurrency` locale

`PlanManagementPage.tsx` currently has its OWN `formatCurrency` using `"pt-PT"`.
Check that `billingApi.ts` or `billing.types.ts` does NOT also export one. If
they don't, the fix is purely local to this file (already covered in 4a). Just
confirm there are no other `pt-PT` currency formatters in any platform component
files:

```bash
grep -rn "pt-PT" frontend/src/
```

Fix any that remain.

Run `pnpm lint` — must pass.

**Commit:** `fix(plans): layout grid, typography, currency, savings badge, subscriber count, skeleton`

---

## 5. Frontend — Update `PlanManagementPage.test.tsx`

**File:** `frontend/src/features/platform/__tests__/PlanManagementPage.test.tsx`

### 5a. Update `PLANS` seed with `subscriberCount`

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
    stripePriceIdMonthly:  "price_monthly_starter",
    stripePriceIdYearly:   null,
    subscriberCount:       4,    // ← new
  },
  {
    id:                    "plan-2",
    name:                  "Pro",
    billingInterval:       "Yearly",
    priceMonthly:          49,
    priceYearly:           490,
    yearlyDiscountPercent: 17,
    allowBrandingRemoval:  true,
    stripePriceIdMonthly:  null,
    stripePriceIdYearly:   "price_yearly_pro",
    subscriberCount:       0,    // ← new
  },
];
```

### 5b. Update the loading test

The loading test asserts `"Loading…"` text exists. The skeleton does not contain
that text — update the test to check for skeleton elements instead:

```typescript
it("shows skeleton cards while plans are loading", () => {
  renderPage();
  // Skeleton uses .animate-pulse
  expect(document.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
  expect(screen.queryByText("Loading…")).not.toBeInTheDocument();
});
```

### 5c. Update the edit button selector

The existing tests click `screen.getAllByRole("button", { name: "" })` to find the
icon-only buttons. Now those buttons have `aria-label` — the selector must change:

```typescript
// Before:
const editBtns = screen.getAllByRole("button", { name: "" });
await user.click(editBtns[0]);

// After:
await user.click(screen.getByRole("button", { name: /edit starter plan/i }));
```

And for the trash:

```typescript
// Before:
const trashBtns = screen.getAllByRole("button", { name: "" });
await user.click(trashBtns[1]);

// After:
await user.click(screen.getByRole("button", { name: /delete starter plan/i }));
```

Update ALL tests that use `getAllByRole("button", { name: "" })` to use the
explicit `aria-label`.

### 5d. Update the delete confirmation text

The delete confirmation text changed from `"Delete?"` to
`"Delete \"Starter\" permanently?"`. Update assertions:

```typescript
// Before:
expect(screen.getByText(/delete\?/i)).toBeInTheDocument();

// After:
expect(screen.getByText(/delete "starter" permanently\?/i)).toBeInTheDocument();
```

And the confirm button text changed from `"Yes"` to `"Yes, delete"`:

```typescript
expect(screen.getByRole("button", { name: /yes, delete/i })).toBeInTheDocument();
```

### 5e. Add new tests

```typescript
it("shows subscriber count badge on plan cards", async () => {
  renderPage();
  await screen.findByText("Starter");
  // Starter has 4 subscribers in seed
  expect(screen.getByText("4")).toBeInTheDocument();
  // Pro has 0 subscribers
  expect(screen.getAllByText("0").length).toBeGreaterThan(0);
});

it("shows subscriber warning in delete dialog when plan has subscribers", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Starter");

  await user.click(screen.getByRole("button", { name: /delete starter plan/i }));

  // Starter has 4 subscribers
  expect(screen.getByText(/4 studios are on this plan/i)).toBeInTheDocument();
});

it("shows safe-to-delete message when plan has no subscribers", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Pro");

  await user.click(screen.getByRole("button", { name: /delete pro plan/i }));

  expect(screen.getByText(/no active subscribers/i)).toBeInTheDocument();
});

it("shows 'Save X% vs monthly billing' savings badge", async () => {
  renderPage();
  await screen.findByText("Starter");
  expect(screen.getAllByText(/save 17% vs monthly billing/i).length).toBe(2);
});

it("shows 'White-label' badge for plans with allowBrandingRemoval", async () => {
  renderPage();
  await screen.findByText("Pro");
  expect(screen.getByText("White-label")).toBeInTheDocument();
  // old "no-branding" text must be gone
  expect(screen.queryByText("no-branding")).not.toBeInTheDocument();
});

it("clicking empty state CTA opens the create form", async () => {
  server.use(
    http.get("http://localhost/api/v1/billing/plans", () =>
      HttpResponse.json([]),
    ),
  );
  const user = userEvent.setup();
  renderPage();
  await screen.findByText(/no plans yet/i);

  await user.click(screen.getByRole("button", { name: /create first plan/i }));

  expect(screen.getByLabelText(/^name$/i)).toBeInTheDocument();
});
```

Run `pnpm test` — all tests must pass.

**Commit:** `test(plans): update tests for aria-labels, new delete dialog, subscriber count`

---

## 6. Final Verification

1. `dotnet build` — zero errors.
2. `dotnet test` — all tests pass.
3. `pnpm --dir frontend tsc --noEmit` — zero TypeScript errors.
4. `pnpm --dir frontend lint` — zero errors.
5. `pnpm --dir frontend test` — all tests pass.
6. Verify no `pt-PT` currency formatter remains in any issuer platform file:
   ```bash
   grep -rn "pt-PT" frontend/src/features/platform/
   ```
7. Verify the delete form inside the card is properly reset when clicking
   "Cancel" — `setDeleting(false)` must fire on Cancel click.
8. Verify the green savings badge does NOT render when `yearlyDiscountPercent === 0`.
9. `git log --oneline -10` — confirm all commits are present.

---

## Reference: Audit Issue → Task Map

| Audit Issue                                          | Task    |
|------------------------------------------------------|---------|
| No subscriber count on cards (critical gap)          | Task 1 + 4f |
| Single-click delete with no context (#2 critical)    | Task 4g |
| Narrow layout / black void (#3 critical)             | Task 4b |
| Price format `29,00 €` instead of `€29`             | Task 4a |
| Plan name insufficient hierarchy                     | Task 4c |
| "Monthly" label ambiguous                            | Task 4d |
| "17% yearly discount" buried in gray text            | Task 4e |
| No skeleton loading state                            | Task 4h |
| Empty state has no CTA                               | Task 4i |
| "no-branding" badge is developer-speak               | Task 4j |
| Icon buttons have no `aria-label`                    | Task 4f (aria-label) |
| Trash hover same color as edit                       | Task 4f (hover:text-destructive) |
| Delete form showed no subscriber count context       | Task 4g |
