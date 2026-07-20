# Overnight Prompt — Free Plan Tier (2026-07-18)

Implement a permanent **Free** plan tier end-to-end: backend validation fix, a
no-Stripe subscription path, a far-future `CurrentPeriodEnd` sentinel, referral
coupon guard, seed data, and frontend display + activation flow.

---

## Phase 0 — Files to read before touching anything

Read every file in this list fully before writing a single line of code.

```
Pena_e_Arte.Application/Plans/Commands/CreatePlanCommand.cs
Pena_e_Arte.Application/Plans/Commands/UpdatePlanCommand.cs      ← check if it exists
Pena_e_Arte.Application/Billing/Commands/CreateSubscriptionCommand.cs
Pena_e_Arte.Application/Billing/Commands/CreateSubscriptionCheckoutCommand.cs
Pena_e_Arte.Application/Billing/Queries/GetPlansQuery.cs
Pena_e_Arte.Application/Subscriptions/Services/SubscriptionAccessService.cs   ← or wherever subscription access is checked
Pena_e_Arte.API/Endpoints/BillingEndpoints.cs                   ← check endpoint registration patterns
Pena_e_Arte.Infrastructure/Persistence/Seed/DataSeeder.cs
Pena_e_Arte.Infrastructure/Persistence/Configurations/PlanConfiguration.cs
Pena_e_Arte.Domain/Entities/Plan.cs
Pena_e_Arte.Contracts/Requests/CreatePlanRequest.cs
frontend/src/features/billing/billingApi.ts
frontend/src/features/billing/billing.types.ts
frontend/src/features/billing/components/SubscribePage.tsx
frontend/src/features/billing/components/BillingPage.tsx
frontend/src/features/platform/components/PlanManagementPage.tsx
tests/Pena_e_Arte.UnitTests/Billing/CreatePlanCommandTests.cs    ← if it exists
tests/Pena_e_Arte.IntegrationTests/Billing/SubscriptionTests.cs  ← if it exists
```

After reading, check the Decisions Log in `docs/claude/architecture.md` for any
existing decisions about free tiers, plan pricing, or subscription paths before
proceeding.

---

## Context and motivation

The platform currently requires every plan to have `PriceMonthly > 0` and every
subscription to go through Stripe Checkout. There is no way to offer studios a
permanent zero-cost tier.

**Root causes identified:**

| # | Location | Problem |
|---|----------|---------|
| 1 | `CreatePlanCommand.cs` lines 76–77 | `GreaterThan(0)` blocks €0 prices |
| 2 | `CreateSubscriptionCommand.cs` line 98 | `periodEnd = DateTime.UtcNow.AddMonths(1)` — wrong for a plan that never expires |
| 3 | `CreateSubscriptionCommand.cs` lines 47–78 | Referral coupon always runs — would attempt to create a Stripe coupon even for a free studio |
| 4 | `SubscribePage.tsx` | No branch for "subscribe without Stripe" — all plans go to Checkout |
| 5 | `BillingPage.tsx` | Free-plan Active studio shows "Cash-billed subscription" card and no upgrade path |
| 6 | `DataSeeder.cs` | No Free plan in seed data |

---

## Phase 1 — Backend: validator fix (prices may be zero)

### 1a — `CreatePlanCommand.cs`

In `CreatePlanValidator`, change lines 76–77:

```csharp
// BEFORE
RuleFor(x => x.Request.PriceMonthly).GreaterThan(0);
RuleFor(x => x.Request.PriceYearly).GreaterThan(0);

// AFTER
RuleFor(x => x.Request.PriceMonthly).GreaterThanOrEqualTo(0);
RuleFor(x => x.Request.PriceYearly).GreaterThanOrEqualTo(0);
```

Add a cross-field rule that prevents a mixed-price Free plan:

```csharp
RuleFor(x => x.Request)
    .Must(r => (r.PriceMonthly == 0) == (r.PriceYearly == 0))
    .WithName("PriceMonthly")
    .WithMessage("A plan must be either fully free (both prices = 0) or fully paid (both prices > 0).");
```

### 1b — `UpdatePlanCommand.cs` (if it exists)

Apply the same two changes: `GreaterThan(0)` → `GreaterThanOrEqualTo(0)` for
both price fields, plus the cross-field must-be-symmetric rule.

---

## Phase 2 — Backend: `CreateSubscriptionCommand.cs` — two surgical fixes

### 2a — Referral coupon guard

Locate the referral coupon block (approximately lines 47–78). Wrap the entire
block in a price check so it is never attempted for a free plan:

```csharp
// BEFORE
if (studio.PendingReferralCodeId is Guid referralCodeId)
{
    // ... coupon creation logic
}

// AFTER
if (plan.PriceMonthly > 0 && studio.PendingReferralCodeId is Guid referralCodeId)
{
    // ... coupon creation logic (unchanged)
}
```

Do **not** touch the `ReferralRedemption` recording block — it is already safely
gated on `discountApplied == true`, which will be false because coupon creation
was skipped.

### 2b — Far-future sentinel for free plans

Locate the no-Stripe else branch (approximately line 98):

```csharp
// BEFORE
periodEnd = DateTime.UtcNow.AddMonths(1);

// AFTER
// Free plans never expire; use a far-future sentinel so no recurring-expiry
// job accidentally lapses a studio that owes nothing.
periodEnd = plan.PriceMonthly == 0
    ? DateTime.UtcNow.AddYears(50)
    : DateTime.UtcNow.AddMonths(1);
```

---

## Phase 3 — Backend: new `ActivateFreeSubscriptionCommand`

This command lets a studio owner self-activate a Free plan without going through
Stripe Checkout.

### 3a — Command + handler

Create `Pena_e_Arte.Application/Billing/Commands/ActivateFreeSubscriptionCommand.cs`:

```csharp
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Billing.Commands;

public record ActivateFreeSubscriptionCommand(Guid PlanId, Guid StudioId)
    : IRequest<SubscriptionResponse>;

public class ActivateFreeSubscriptionHandler(IAppDbContext db)
    : IRequestHandler<ActivateFreeSubscriptionCommand, SubscriptionResponse>
{
    public async Task<SubscriptionResponse> Handle(
        ActivateFreeSubscriptionCommand command, CancellationToken ct)
    {
        Plan plan = await db.Plans
            .FirstOrDefaultAsync(p => p.Id == command.PlanId, ct)
            ?? throw new NotFoundException(nameof(Plan), command.PlanId);

        if (plan.PriceMonthly != 0)
            throw new BusinessRuleViolationException(
                "ActivateFreeSubscription requires a plan with PriceMonthly = 0.");

        Subscription? existing = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.StudioId == command.StudioId, ct);

        if (existing is not null && existing.Status == SubscriptionStatus.Active)
            throw new BusinessRuleViolationException(
                "This studio already has an active subscription.");

        DateTime now        = DateTime.UtcNow;
        // Far-future sentinel: free plans never expire.
        DateTime periodEnd  = now.AddYears(50);

        if (existing is null)
        {
            Subscription sub = new()
            {
                StudioId             = command.StudioId,
                PlanId               = plan.Id,
                Status               = SubscriptionStatus.Active,
                CurrentPeriodEnd     = periodEnd,
                GracePeriodEnd       = periodEnd,
                StripeSubscriptionId = null,
                TrialExpiresAt       = null,
            };
            db.Subscriptions.Add(sub);
            await db.SaveChangesAsync(ct);
            return Map(sub, plan);
        }

        // Upgrade from trial / grace period / cancelled → free active
        existing.PlanId               = plan.Id;
        existing.Status               = SubscriptionStatus.Active;
        existing.CurrentPeriodEnd     = periodEnd;
        existing.GracePeriodEnd       = periodEnd;
        existing.StripeSubscriptionId = null;
        existing.TrialExpiresAt       = null;
        await db.SaveChangesAsync(ct);
        return Map(existing, plan);
    }

    private static SubscriptionResponse Map(Subscription s, Plan p) => new(
        s.Id, s.StudioId, s.PlanId, p.Name,
        s.Status.ToString(),
        s.TrialExpiresAt?.ToString("O"),
        s.CurrentPeriodEnd.ToString("O"),
        s.GracePeriodEnd?.ToString("O"),
        s.StripeSubscriptionId,
        s.PendingPlanId);
}

public class ActivateFreeSubscriptionValidator
    : AbstractValidator<ActivateFreeSubscriptionCommand>
{
    public ActivateFreeSubscriptionValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.StudioId).NotEmpty();
    }
}
```

Check the existing `SubscriptionResponse` constructor signature in
`Pena_e_Arte.Contracts/Responses/` before writing the `Map` method — match it
exactly.

### 3b — Endpoint registration

In `Pena_e_Arte.API/Endpoints/BillingEndpoints.cs` (or wherever billing
endpoints are registered), add:

```csharp
group.MapPost("/subscriptions/free", async (
    [FromBody]          ActivateFreeSubscriptionRequest req,
    ClaimsPrincipal     user,
    ISender             mediator,
    CancellationToken   ct) =>
{
    Guid studioId = user.GetTenantId();   // use whatever helper exists in the project
    ActivateFreeSubscriptionCommand cmd = new(req.PlanId, studioId);
    SubscriptionResponse result = await mediator.Send(cmd, ct);
    return Results.Ok(result);
})
.RequireAuthorization("OwnerOnly")
.WithName("ActivateFreeSubscription");
```

Use the same helper methods (e.g. `user.GetTenantId()`, `user.GetUserId()`) and
the same `Results.Ok` / error-handling pattern used by the other subscription
endpoints in the file. Do not invent a new pattern.

### 3c — Contract request model

Create `Pena_e_Arte.Contracts/Requests/ActivateFreeSubscriptionRequest.cs`:

```csharp
namespace Pena_e_Arte.Contracts.Requests;

public record ActivateFreeSubscriptionRequest(Guid PlanId);
```

---

## Phase 4 — Backend: seed data

### 4a — New Free plan constant

In `DataSeeder.cs`, add to the Issuer-level IDs block (after `PremiumYearlyPlanId`):

```csharp
private static readonly Guid FreePlanId = new("aaaa0006-0000-0000-0000-000000000000");
```

### 4b — Idempotent Free plan seed

In `SeedAsync`, **before** the existing guard that returns early if plans exist,
add a separate Free-plan-only guard so existing databases pick it up without
triggering a full re-seed:

```csharp
public static async Task SeedAsync(IServiceProvider services)
{
    using IServiceScope scope = services.CreateScope();
    AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    UserManager<IdentityUser> userManager =
        scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

    // Always run: ensure seed credentials + artist slugs are correct
    await EnsureSeedUsersAsync(userManager);
    await EnsureArtistSlugsAsync(db);

    // ── Free plan: idempotent — adds to existing databases without full re-seed ──
    if (!await db.Plans.AnyAsync(p => p.Id == FreePlanId))
        await SeedFreePlanAsync(db);

    // Guard: run entity seeding only once (when plans don't yet exist)
    if (await db.Plans.AnyAsync(p => p.Id == StarterPlanId))
        return;

    await SeedPlansAsync(db);
    // ... rest unchanged
}
```

### 4c — `SeedFreePlanAsync` method

Add after `SeedPlansAsync`:

```csharp
private static async Task SeedFreePlanAsync(AppDbContext db)
{
    db.Plans.Add(new Plan
    {
        Id                       = FreePlanId,
        Name                     = "Free",
        BillingInterval          = BillingInterval.Monthly,
        PriceMonthly             = 0m,
        PriceYearly              = 0m,
        YearlyDiscountPercent    = 0,
        AllowBrandingRemoval     = false,
        AllowApiAccess           = false,
        PrioritySupport          = false,
        MaxArtists               = 1,
        MaxAppointmentsPerMonth  = 10,
        MaxNotificationsPerMonth = 30,
        MaxStorageGb             = 1,
        MaxLocations             = 1,
    });
    await db.SaveChangesAsync();
}
```

### 4d — Also add Free plan inside `SeedPlansAsync`

So fresh databases get the Free plan too. Add it as the **first** plan in
`SeedPlansAsync` (before Starter), so the Free tier is always at the top of the
`ORDER BY PriceMonthly` sort in `GetPlansQuery`:

```csharp
new Plan
{
    Id                       = FreePlanId,
    Name                     = "Free",
    BillingInterval          = BillingInterval.Monthly,
    PriceMonthly             = 0m,
    PriceYearly              = 0m,
    YearlyDiscountPercent    = 0,
    AllowBrandingRemoval     = false,
    AllowApiAccess           = false,
    PrioritySupport          = false,
    MaxArtists               = 1,
    MaxAppointmentsPerMonth  = 10,
    MaxNotificationsPerMonth = 30,
    MaxStorageGb             = 1,
    MaxLocations             = 1,
},
// ... Starter, Growth, Premium, Pro follow
```

---

## Phase 5 — Audit `SubscriptionAccessService`

Read `SubscriptionAccessService.cs` (or wherever subscription access checks
live). Search for any condition that reads `PriceMonthly`, `PriceYearly`, or
`StripePriceId`. If such a check exists and would incorrectly deny access to a
Free plan studio with `Status = Active`, fix it. Add a code comment explaining
the Free plan semantics (`Active + null StripeSubscriptionId + far-future
periodEnd`).

If no such check exists, add a comment at the top of the file:

```csharp
// Free plan studios have: Status = Active, StripeSubscriptionId = null,
// CurrentPeriodEnd = ~50 years in the future. Access checks must not assume
// that Active + null StripeSubscriptionId means cash-billed — it could also
// be a free plan.
```

---

## Phase 6 — Frontend: `billingApi.ts`

Add the mutation. Follow the exact pattern used by the existing mutations in the
file:

```typescript
activateFreeSubscription: builder.mutation<SubscriptionResponse, { planId: string }>({
  query: (body) => ({
    url: "billing/subscriptions/free",
    method: "POST",
    body,
  }),
  invalidatesTags: ["Subscription"],
}),
```

Export the hook: `useActivateFreeSubscriptionMutation`.

Make sure `"Subscription"` is already in the `tagTypes` list — add it if not.

---

## Phase 7 — Frontend: `billing.types.ts`

No new fields are required on `PlanResponse`. Verify that `priceMonthly: number`
exists (it should). If `yearlyDiscountPercent` is used in `SubscribePage` for
the Free plan savings badge, it will be `0` — the conditional `yearlyDiscount > 0`
already gates that badge, so no change needed there.

---

## Phase 8 — Frontend: `SubscribePage.tsx`

### 8a — Import the new mutation

```typescript
import {
  useGetPlansQuery,
  useGetSubscriptionQuery,
  useCreateCheckoutMutation,
  useChangePlanMutation,
  useActivateFreeSubscriptionMutation,   // ← add
} from "../billingApi";
```

### 8b — Declare the mutation and a `selectedPlan` derived value

After the existing `useState` declarations (around line 94), add:

```typescript
const [activateFree, { isLoading: activating }] =
  useActivateFreeSubscriptionMutation();

const selectedPlan = filteredPlans.find((p) => p.id === selectedPlanId) ?? null;
const isFreePlanSelected = selectedPlan?.priceMonthly === 0;
```

Update `busy`:

```typescript
const busy = checkingOut || switching || activating;
```

### 8c — Branch `onSubscribe` for Free plans

Replace the current `onSubscribe` function with:

```typescript
async function onSubscribe() {
  if (!selectedPlanId) return;
  setSubmitError(null);

  // Free plan: activate directly, no Stripe.
  if (isFreePlanSelected) {
    const result = await activateFree({ planId: selectedPlanId });
    if ("error" in result) {
      const err = result.error as { data?: { message?: string } } | undefined;
      setSubmitError(err?.data?.message ?? "Failed to activate Free plan. Please try again.");
      return;
    }
    toast.success("Free plan activated. Welcome!");
    navigate("/billing");
    return;
  }

  if (isCardBilled) {
    const result = await changePlan({ planId: selectedPlanId });
    if ("error" in result) {
      const err = result.error as { data?: { message?: string } } | undefined;
      setSubmitError(err?.data?.message ?? "Failed to change plan. Please try again.");
      return;
    }
    if (result.data.pendingPlanId) {
      toast.success("Plan change scheduled for the end of your current billing period.");
    } else {
      toast.success("Plan upgraded — the prorated difference has been charged.");
    }
    navigate("/billing");
    return;
  }

  // New subscription OR cash → card switch → Stripe-hosted Checkout.
  const origin = window.location.origin;
  const result = await createCheckout({
    planId:     selectedPlanId,
    successUrl: `${origin}/billing?session_id={CHECKOUT_SESSION_ID}`,
    cancelUrl:  `${origin}/billing/subscribe`,
  });
  if ("error" in result) {
    const err = result.error as { data?: { message?: string } } | undefined;
    setSubmitError(err?.data?.message ?? "Could not start checkout. Please try again.");
    return;
  }
  window.location.href = result.data.url;
}
```

### 8d — Update the submit button label

Replace the button content block so it reflects the Free plan state:

```tsx
{busy ? (
  <>
    <Loader2 className="h-4 w-4 animate-spin" />
    {isFreePlanSelected
      ? "Activating…"
      : isCardBilled
        ? "Switching…"
        : "Redirecting to checkout…"}
  </>
) : isFreePlanSelected ? (
  <>
    <CheckCircle className="h-4 w-4" />
    Activate Free plan
  </>
) : isCardBilled ? (
  <>
    <RefreshCw className="h-4 w-4" />
    Switch plan
  </>
) : (
  <>
    <Zap className="h-4 w-4" />
    Continue to checkout
  </>
)}
```

Add `CheckCircle` to the lucide-react import line.

### 8e — Fix header copy when arriving from a Free plan

The header currently shows "Set up card billing" when `isCashBilled`. A Free plan
active studio is also `isCashBilled`, but that copy is wrong for them.

Introduce:

```typescript
const isFreePlanActive = isActive && sub?.priceMonthly === 0;
// Note: sub doesn't carry priceMonthly — derive from currentPlan on BillingPage.
// On SubscribePage we only have sub; check plans instead:
const currentSubPlan = plans.find((p) => p.id === sub?.planId);
const isFreePlanActive = isActive && (currentSubPlan?.priceMonthly ?? -1) === 0;
```

Update the header title:

```typescript
{isCardBilled
  ? "Change Plan"
  : isFreePlanActive
    ? "Upgrade from Free"
    : isCashBilled
      ? "Set up card billing"
      : "Choose a Plan"}
```

Update the description paragraph:

```typescript
{isCardBilled
  ? "Upgrades apply immediately…"
  : isFreePlanActive
    ? "You're on the Free plan. Choose a paid plan to unlock higher limits and additional features."
    : isCashBilled
      ? "Switch from cash to automatic card billing…"
      : "Select a plan to unlock full access for your studio."}
```

### 8f — `PlanCard` — Free plan price display

In `PlanCard`, add a branch for free plans before the price paragraph:

```tsx
<div className="text-right shrink-0">
  {plan.priceMonthly === 0 ? (
    <p className="font-semibold text-green-600 dark:text-green-400">Free</p>
  ) : (
    <p className="font-semibold">
      {formatPrice(price)}
      <span className="text-xs font-normal text-muted-foreground">
        /{isYearly ? "yr" : "mo"}
      </span>
    </p>
  )}
  {isYearly && plan.yearlyDiscountPercent > 0 && plan.priceMonthly > 0 && (
    <p className="text-xs text-green-600 dark:text-green-400">
      {formatPrice(perMonth)}/mo · save {plan.yearlyDiscountPercent}%
    </p>
  )}
</div>
```

---

## Phase 9 — Frontend: `BillingPage.tsx`

### 9a — Detect `isFreePlan`

After `currentPlan` is resolved (after the `useMemo` on line ~159), add:

```typescript
const isFreePlan = (currentPlan?.priceMonthly ?? -1) === 0;
```

### 9b — Update `canSubscribe` and `canChangePlan`

```typescript
// canSubscribe: non-active OR on free plan wanting to upgrade
const canSubscribe  = sub.status !== "Active" || isFreePlan;

// canChangePlan: card-billed active only (paid plan → paid plan switch via Stripe)
const canChangePlan = sub.status === "Active" && !isCashBilled;
```

### 9c — Update the Subscribe / Upgrade button label

In the header button (around line 220):

```tsx
{canSubscribe && (
  <Button size="sm" onClick={() => navigate("/billing/subscribe")} className="gap-1.5">
    <Zap className="h-3.5 w-3.5" />
    {isFreePlan && sub.status === "Active"
      ? "Upgrade"
      : sub.status === "Trialing" || sub.status === "GracePeriod"
        ? "Subscribe"
        : "Reactivate"}
  </Button>
)}
```

### 9d — Price display for Free plan (Active section)

In the `sub.status === "Active"` block (around line 284), update the price row:

```tsx
{sub.status === "Active" && (
  <div className="space-y-1">
    {currentPlan && (
      isFreePlan ? (
        <p className="text-sm font-medium text-green-600 dark:text-green-400">Free</p>
      ) : (
        <p className="text-sm font-medium">
          {formatEur(currentPlan.priceMonthly)}
          <span className="text-muted-foreground font-normal"> / month</span>
        </p>
      )
    )}
    {/* Suppress renewal date for free plan (far-future sentinel would confuse users) */}
    {!isFreePlan && (
      <div className="flex items-center gap-1.5 text-sm text-muted-foreground">
        <Calendar className="h-3.5 w-3.5 shrink-0" />
        {isCashBilled
          ? <span>Active until {formatDate(sub.currentPeriodEnd)}</span>
          : currentPlan
            ? <span>Next charge: {formatEur(currentPlan.priceMonthly)} on {formatDate(sub.currentPeriodEnd)}</span>
            : <span>Renews {formatDate(sub.currentPeriodEnd)}</span>
        }
      </div>
    )}
  </div>
)}
```

### 9e — Suppress "Cash-billed subscription" card for Free plan

The cash-billed card (around line 406) renders when `sub.status === "Active" && isCashBilled`. Gate it:

```tsx
{sub.status === "Active" && isCashBilled && !isFreePlan && (
  <Card>
    {/* Cash-billed subscription card — unchanged content */}
  </Card>
)}
```

### 9f — Add "Free plan" informational card

After the status card, when the studio is on a Free plan and Active, show:

```tsx
{isFreePlan && sub.status === "Active" && (
  <Card>
    <CardContent className="p-5 space-y-3 text-sm">
      <p className="font-medium flex items-center gap-2">
        <Zap className="h-4 w-4" />
        Free plan
      </p>
      <p className="text-muted-foreground">
        You're on the permanent Free plan. Upgrade to a paid plan to unlock more
        artists, appointments, storage, and features.
      </p>
      <Button
        size="sm"
        className="w-full gap-1.5"
        onClick={() => navigate("/billing/subscribe")}
      >
        <Zap className="h-3.5 w-3.5" />
        Upgrade plan
      </Button>
    </CardContent>
  </Card>
)}
```

---

## Phase 10 — Tests

### 10a — Backend unit tests

**File:** `tests/Pena_e_Arte.UnitTests/Billing/CreatePlanCommandTests.cs`
(create if it doesn't exist; follow the same pattern as other unit test files)

```csharp
[Fact]
public async Task Validator_AllowsZeroPrices_ForFreePlan()
{
    CreatePlanCommand cmd = new(new CreatePlanRequest(
        Name: "Free",
        BillingInterval: "Monthly",
        PriceMonthly: 0,
        PriceYearly: 0,
        YearlyDiscountPercent: 0,
        // ... other required fields with valid defaults
    ));

    CreatePlanValidator validator = new();
    FluentValidation.Results.ValidationResult result = await validator.ValidateAsync(cmd);
    Assert.True(result.IsValid);
}

[Fact]
public async Task Validator_Rejects_MixedFreePaidPrices()
{
    CreatePlanCommand cmd = new(new CreatePlanRequest(
        Name: "Broken",
        BillingInterval: "Monthly",
        PriceMonthly: 29,
        PriceYearly: 0,           // ← asymmetric
        YearlyDiscountPercent: 0,
        // ... other required fields
    ));

    CreatePlanValidator validator = new();
    FluentValidation.Results.ValidationResult result = await validator.ValidateAsync(cmd);
    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == "PriceMonthly");
}
```

**File:** `tests/Pena_e_Arte.UnitTests/Billing/CreateSubscriptionCommandTests.cs`
(create if it doesn't exist)

```csharp
[Fact]
public async Task Handle_FreePlan_SetsFarFuturePeriodEnd()
{
    // Arrange: mock IAppDbContext returning a Free plan, no existing subscription.
    // Act: send CreateSubscriptionCommand (the cash-billed path).
    // Assert: saved subscription.CurrentPeriodEnd > DateTime.UtcNow.AddYears(49).
}

[Fact]
public async Task Handle_FreePlan_SkipsReferralCoupon()
{
    // Arrange: studio has PendingReferralCodeId set; plan has PriceMonthly = 0.
    // Act: send CreateSubscriptionCommand.
    // Assert: IDiscountService.CreateOneMonthFreeCouponAsync was NOT called.
    //         (Mock IDiscountService, verify no coupon creation.)
}
```

### 10b — Backend integration test

**File:** `tests/Pena_e_Arte.IntegrationTests/Billing/ActivateFreeSubscriptionTests.cs`

```csharp
[Fact]
public async Task ActivateFreeSubscription_CreatesActiveSubscription_WithFarFuturePeriodEnd()
{
    // Arrange: factory client authenticated as OwnerOnly; Free plan in DB.
    // Act: POST /billing/subscriptions/free { planId: freePlanId }
    // Assert: 200 OK; response.status == "Active";
    //         subscription in DB has CurrentPeriodEnd > DateTime.UtcNow.AddYears(49).
}

[Fact]
public async Task ActivateFreeSubscription_Rejects_PaidPlan()
{
    // Act: POST /billing/subscriptions/free with a paid plan ID.
    // Assert: 400 / 422 (BusinessRuleViolationException).
}

[Fact]
public async Task ActivateFreeSubscription_Rejects_AlreadyActive()
{
    // Arrange: studio already has Active subscription.
    // Act: POST /billing/subscriptions/free again.
    // Assert: 400 / 422.
}
```

### 10c — Frontend unit tests

**File:** `frontend/src/features/billing/__tests__/SubscribePage.test.tsx`
(add to existing file or create if it doesn't exist)

```typescript
it("shows 'Activate Free plan' button when a Free plan is selected", async () => {
  // Arrange: mock getPlans to include a Free plan (priceMonthly: 0),
  //          mock getSubscription to return Trialing.
  // Act: render SubscribePage, click the Free plan card.
  // Assert: button text is /activate free plan/i.
});

it("calls activateFreeSubscription mutation (not createCheckout) for Free plan", async () => {
  // Arrange: mock activateFreeSubscription.
  // Act: render SubscribePage, click Free plan card, click "Activate Free plan".
  // Assert: activateFreeSubscription called with { planId: freePlanId };
  //         createCheckout NOT called.
});

it("shows 'Upgrade from Free' header when studio is on active Free plan", async () => {
  // Arrange: mock getSubscription returning Active; mock getPlans including Free plan
  //          with matching planId.
  // Act: render SubscribePage.
  // Assert: heading text is /upgrade from free/i.
});
```

**File:** `frontend/src/features/billing/__tests__/BillingPage.test.tsx`
(add to existing file)

```typescript
it("shows 'Upgrade' button when studio is on active Free plan", async () => {
  // Arrange: sub.status = "Active", currentPlan.priceMonthly = 0,
  //          sub.stripeSubscriptionId = null.
  // Assert: Button with text /upgrade/i is visible.
});

it("hides 'Cash-billed subscription' card when studio is on Free plan", async () => {
  // Same arrangement as above.
  // Assert: text /cash-billed subscription/i is NOT in the document.
});

it("shows 'Free' price label (not €0) when studio is on Free plan", async () => {
  // Assert: text "Free" is visible; "€0" is NOT visible.
});

it("does NOT show renewal date when studio is on Free plan", async () => {
  // Assert: no date-like text near the price; /active until/i is not present;
  //         /next charge/i is not present.
});
```

---

## Phase 11 — `PlanManagementPage.tsx` — issuer-side Free plan display

Read the current `PlanManagementPage.tsx`. The issuer sees all plans including
the Free one. Verify these cases render correctly:

1. **Price display**: The dual-price logic from `overnight-prompt-plan-management-audit-2026-07-18b.md`
   should already handle `priceMonthly === 0`. If it tries to show `/yr ref.` or
   compute savings for a Free plan, guard it:
   ```tsx
   {plan.priceMonthly > 0 && /* savings badge and reference price */}
   ```

2. **Delete button**: The Free plan should be deletable only if `subscriberCount === 0`.
   This is already handled by the existing delete guard if one exists — verify it.

3. **Billing label**: For a Free plan, `billingInterval` is `"Monthly"` but the
   concept of "Billed monthly" is wrong. Show "Free forever" instead of "Billed monthly"
   when `priceMonthly === 0`.

Make only the changes needed. Do not refactor anything else in this file.

---

## Phase 12 — EF Core migration (if required)

Run:
```bash
dotnet ef migrations add AddFreePlanSeedData --project Pena_e_Arte.Infrastructure
```

Check the generated migration. If it only contains data seeding (not schema
changes), that's expected. The `PlanConfiguration` has no `CHECK > 0` constraint
at DB level, so no schema migration is needed. If the migration file is empty or
only contains an `Up()` comment, delete it — the seeder handles data at runtime.

---

## Quality gates

Before marking this work done, verify all of the following:

- [ ] `dotnet build` passes with zero errors and zero warnings
- [ ] `dotnet test` — all existing tests pass; new tests pass
- [ ] `pnpm lint` — no errors
- [ ] `pnpm test` — all existing tests pass; new tests pass
- [ ] Free plan can be created via `POST /platform/plans` with `{ priceMonthly: 0, priceYearly: 0 }` returning 200
- [ ] `ActivateFreeSubscription` integration test passes
- [ ] On SubscribePage: Free plan card shows "Free" price, button says "Activate Free plan"
- [ ] On BillingPage: active Free plan shows "Upgrade" button, hides cash-billed card, shows "Free" price
- [ ] `DataSeeder` idempotent guard runs cleanly on a database that already has `StarterPlanId` seeded

---

## Open questions for marketing / product

Do not block implementation on these. Proceed with the conservative defaults
listed below, and leave a `// TODO(product):` comment near each affected line.

1. **Feature limits**: The Free plan is seeded with `MaxArtists=1`,
   `MaxAppointmentsPerMonth=10`, `MaxNotificationsPerMonth=30`, `MaxStorageGb=1`,
   `MaxLocations=1`. Confirm or adjust these caps before the first production deploy.

2. **Trial-to-Free downgrade path**: Studios whose trial expires currently enter
   GracePeriod. Should the BillingPage offer a "Continue for free" call-to-action
   during GracePeriod (alongside "Subscribe"), allowing them to choose the Free
   tier rather than a paid one? Currently this prompt leaves GracePeriod
   unchanged — the owner can navigate to SubscribePage and select Free manually.

3. **Free plan in public plan selection**: `GET /billing/plans` returns all plans
   including Free. If the public marketing/pricing page (if any) calls this
   endpoint, the Free tier will appear there automatically. Confirm whether that
   is the intended experience.

4. **Referral redemption on Free**: The referral coupon block is skipped
   entirely for Free plan subscriptions, so the referrer earns no credit. Confirm
   this is the desired policy (not: "referrer earns credit on first upgrade").

---

## Forbidden actions

- Do NOT bypass `IAppDbContext` — all DB access through EF Core and global query filters
- Do NOT add a new ORM, HTTP client, or package (`Stripe.net` is already installed and must not be invoked for Free plan activation)
- Do NOT store per-request state in Redis
- Do NOT create a REST endpoint without a corresponding FluentValidation validator
- Do NOT skip writing tests for `ActivateFreeSubscriptionCommand` — it contains business logic
- Do NOT use `IgnoreQueryFilters()` outside the `issuer` role
- Do NOT use TypeScript `any` — type everything explicitly
- Do NOT use `useEffect` for data fetching on the frontend — RTK Query mutations only
- Do NOT hardcode the `FreePlanId` GUID in the frontend — plans are discovered dynamically via `priceMonthly === 0`
