# Overnight Prompt — Two-Sided Referral Rewards (2026-07-18)

Implement the referrer-side reward in the referral programme: when a referred
studio subscribes and their discount lands, the referring studio gets a one-month
free coupon applied to their active Stripe subscription. Adds idempotency
tracking, a self-referral fraud guard, stats visibility, and a frontend stat row.

---

## Phase 0 — Files to read before touching anything

Read these fully before writing a single line of code.

```
docs/claude/architecture.md                                    ← check Decisions Log for last IgnoreQueryFilters usage #
docs/claude/backend.md
Pena_e_Arte.Domain/Entities/ReferralCode.cs
Pena_e_Arte.Domain/Entities/ReferralRedemption.cs
Pena_e_Arte.Domain/Interfaces/IStripeBillingService.cs
Pena_e_Arte.Domain/Interfaces/IStripeDiscountService.cs
Pena_e_Arte.Infrastructure/Services/StripeBillingService.cs
Pena_e_Arte.Infrastructure/Services/StripeDiscountService.cs
Pena_e_Arte.Application/Billing/Commands/CreateSubscriptionCommand.cs
Pena_e_Arte.Application/Billing/Commands/ActivateCheckoutSubscriptionCommand.cs
Pena_e_Arte.Application/Referrals/Queries/GetReferralStatsQuery.cs
Pena_e_Arte.Contracts/Responses/ReferralStatsResponse.cs
Pena_e_Arte.Infrastructure/Persistence/Configurations/            ← scan for ReferralRedemptionConfiguration.cs or similar
Pena_e_Arte.Infrastructure/Migrations/                            ← find the most recent migration file name for naming convention
Pena_e_Arte.Domain/Entities/Studio.cs                            ← confirm OwnerEmail field name
Pena_e_Arte.Domain/Entities/Subscription.cs                      ← confirm StripeSubscriptionId, Status field names
frontend/src/features/studios/components/ReferralCodeCard.tsx
frontend/src/features/studios/studiosApi.ts                       ← find ReferralStatsResponse / getReferralStats type
tests/Pena_e_Arte.UnitTests/Referrals/CreateSubscriptionDiscountTests.cs
tests/Pena_e_Arte.IntegrationTests/Application/ReferralFlowIntegrationTests.cs
```

After reading `architecture.md`, find all existing `IgnoreQueryFilters approved: usage #N` comments
(or however they are labelled in the Decisions Log). Note the highest `N` — the
new cross-tenant read in `ReferralRewardService` will be `N+1`.

Also find where `ISubscriptionAccessService` / `IPlanLimitService` are defined
(domain interface or application interface?) and follow that exact pattern for the
new `IReferralRewardService`.

---

## What this feature changes — a map

| # | File | Change |
|---|------|--------|
| 1 | `ReferralRedemption.cs` | Add `ReferrerRewardApplied` + `ReferrerRewardCouponId` |
| 2 | `ReferralRedemptionConfiguration.cs` (or create it) | Column defaults + EF config |
| 3 | EF migration | `AddReferrerRewardTracking` |
| 4 | `IStripeBillingService.cs` | New `ApplyCouponToActiveSubscriptionAsync` |
| 5 | `StripeBillingService.cs` | Implement that method |
| 6 | `IReferralRewardService.cs` (new) | Interface for the reward service |
| 7 | `ReferralRewardService.cs` (new) | Implementation — the main business logic |
| 8 | DI registration | Register `ReferralRewardService` |
| 9 | `CreateSubscriptionCommand.cs` | Inject + call `RewardReferrerAsync` after save |
| 10 | `ActivateCheckoutSubscriptionCommand.cs` | Same |
| 11 | `GetReferralStatsQuery.cs` | Count `ReferrerRewardApplied` |
| 12 | `ReferralStatsResponse.cs` | Add `ReferrerRewardsApplied` field |
| 13 | Frontend types (studiosApi / types file) | Add `referrerRewardsApplied` |
| 14 | `ReferralCodeCard.tsx` | Surface rewards earned stat |
| 15 | `CreateSubscriptionDiscountTests.cs` | Update + extend |
| 16 | `ReferralFlowIntegrationTests.cs` | Extend with referrer reward scenario |
| 17 | `architecture.md` | Decisions Log entry for new `IgnoreQueryFilters` usage |

---

## Phase 1 — Domain entity: `ReferralRedemption.cs`

Add two fields:

```csharp
public class ReferralRedemption
{
    public Guid     Id                     { get; init; } = Guid.NewGuid();
    public Guid     ReferralCodeId         { get; set; }
    public Guid     NewStudioId            { get; set; }
    public DateTime RedeemedAt             { get; init; } = DateTime.UtcNow;
    public bool     DiscountApplied        { get; set; }

    // ── Referrer reward tracking ─────────────────────────────────────────────
    // Set to true once the referring studio has received their reward coupon.
    // Guards idempotency: both subscription-creation paths (direct + checkout)
    // call RewardReferrerAsync; this flag ensures a webhook retry or finalize
    // re-call never issues a second coupon.
    public bool     ReferrerRewardApplied  { get; set; }

    // Stores the Stripe coupon ID issued to the referrer, for audit/support use.
    public string?  ReferrerRewardCouponId { get; set; }
}
```

---

## Phase 2 — EF Core configuration + migration

### 2a — Configuration

Find the EF configuration for `ReferralRedemption` (look for a
`ReferralRedemptionConfiguration.cs` file in
`Pena_e_Arte.Infrastructure/Persistence/Configurations/`). If it exists, add:

```csharp
builder.Property(r => r.ReferrerRewardApplied)
       .HasDefaultValue(false)
       .IsRequired();

builder.Property(r => r.ReferrerRewardCouponId)
       .HasMaxLength(255);
```

If the file does not exist, create
`Pena_e_Arte.Infrastructure/Persistence/Configurations/ReferralRedemptionConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class ReferralRedemptionConfiguration : IEntityTypeConfiguration<ReferralRedemption>
{
    public void Configure(EntityTypeBuilder<ReferralRedemption> builder)
    {
        builder.Property(r => r.ReferrerRewardApplied)
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(r => r.ReferrerRewardCouponId)
               .HasMaxLength(255);
    }
}
```

Then register it in `AppDbContext.OnModelCreating` if configurations are applied
explicitly (check if other configuration classes are explicitly applied via
`modelBuilder.ApplyConfiguration(new ...)` or `modelBuilder.ApplyConfigurationsFromAssembly`).

### 2b — Migration

Run:

```bash
dotnet ef migrations add AddReferrerRewardTracking \
  --project Pena_e_Arte.Infrastructure \
  --startup-project Pena_e_Arte.API
```

Review the generated migration. It must add:
- `ReferrerRewardApplied` column as `tinyint(1) NOT NULL DEFAULT 0` (MySQL / Pomelo convention for bool)
- `ReferrerRewardCouponId` column as `varchar(255) NULL`

Do **not** run `database update` — Claude Code cannot hit the database. Leave the
migration file committed; the CI pipeline or developer applies it.

---

## Phase 3 — Stripe: apply coupon to an existing subscription

### 3a — `IStripeBillingService.cs`

Add after `CancelSubscriptionAsync`:

```csharp
/// <summary>
/// Applies a Stripe coupon to an already-active subscription. Used to reward the
/// referring studio when their referral code converts a new paying studio.
/// The coupon is applied as a discount on the subscription's next invoice.
/// </summary>
/// <remarks>
/// IMPORTANT: Verify the shape of <see cref="SubscriptionUpdateOptions"/> in the
/// pinned Stripe.net version before calling. In newer SDK versions use the
/// <c>Discounts</c> collection; in older versions use the deprecated
/// <c>Coupon</c> string property. Check the existing <c>CreateSubscriptionAsync</c>
/// implementation for which style this SDK uses.
/// </remarks>
Task ApplyCouponToActiveSubscriptionAsync(
    string stripeSubscriptionId, string couponId, CancellationToken ct);
```

### 3b — `StripeBillingService.cs`

**Before writing this method**, read the `.csproj` file for
`Pena_e_Arte.Infrastructure` and note the exact `Stripe.net` version pinned.
Then inspect `SubscriptionUpdateOptions` in the SDK to confirm whether it
exposes a `Discounts` collection or a string `Coupon` property (or both).

The existing `CreateSubscriptionAsync` already uses:

```csharp
Discounts = couponId is not null
    ? new List<SubscriptionDiscountOptions> { new() { Coupon = couponId } }
    : null,
```

If `SubscriptionUpdateOptions` also exposes `Discounts`, mirror that shape:

```csharp
public async Task ApplyCouponToActiveSubscriptionAsync(
    string stripeSubscriptionId, string couponId, CancellationToken ct)
{
    SubscriptionUpdateOptions options = new()
    {
        Discounts = new List<SubscriptionDiscountOptions>
        {
            new() { Coupon = couponId },
        },
    };

    await subscriptionService.UpdateAsync(stripeSubscriptionId, options, null, ct);
}
```

If `SubscriptionUpdateOptions.Discounts` does **not** exist in the pinned SDK
version, fall back to the deprecated `Coupon` string field:

```csharp
public async Task ApplyCouponToActiveSubscriptionAsync(
    string stripeSubscriptionId, string couponId, CancellationToken ct)
{
    SubscriptionUpdateOptions options = new()
    {
        // SubscriptionDiscountOptions not available in this SDK version.
        // Using deprecated Coupon string field instead.
        Coupon = couponId,
    };

    await subscriptionService.UpdateAsync(stripeSubscriptionId, options, null, ct);
}
```

Leave a `// TODO: migrate to Discounts collection when Stripe.net is upgraded past X.Y`
comment in whichever branch is used, referencing the actual version number found
in the `.csproj`.

---

## Phase 4 — `IReferralRewardService` interface

Determine the correct namespace by checking where `ISubscriptionAccessService`
(or `IPlanLimitService`) is defined. Follow the same pattern. Create the
interface at the matching path:

```csharp
namespace Pena_e_Arte.Application.Referrals.Services;  // adjust namespace to match codebase pattern

/// <summary>
/// Applies a reward coupon to the referring studio's active Stripe subscription
/// when their referral code successfully converts a new paying studio.
/// </summary>
public interface IReferralRewardService
{
    /// <summary>
    /// Issues a one-month-free coupon to the referring studio for a completed,
    /// discount-applied referral redemption. Idempotent — safe to call more than
    /// once for the same <paramref name="referralRedemptionId"/>; subsequent calls
    /// are no-ops if <c>ReferrerRewardApplied</c> is already true.
    /// </summary>
    Task RewardReferrerAsync(Guid referralRedemptionId, CancellationToken ct);
}
```

---

## Phase 5 — `ReferralRewardService` implementation

Create `Pena_e_Arte.Infrastructure/Services/ReferralRewardService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Referrals.Services;   // adjust to match interface namespace
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

public class ReferralRewardService(
    IAppDbContext            db,
    IStripeBillingService    billing,
    IStripeDiscountService   discounts,
    ILogger<ReferralRewardService> logger)
    : IReferralRewardService
{
    public async Task RewardReferrerAsync(Guid referralRedemptionId, CancellationToken ct)
    {
        // IgnoreQueryFilters approved: usage #N — ReferralRewardService loads
        // the referring studio's subscription cross-tenant. The referred studio
        // just subscribed (its tenant context is active), but the referrer is a
        // different tenant. No PII is written to any log statement below.
        // See architecture.md Decisions Log entry "IgnoreQueryFilters #N".
        // (Replace N with the next number from the Decisions Log.)

        ReferralRedemption? redemption = await db.ReferralRedemptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == referralRedemptionId, ct)
            ?? throw new NotFoundException(nameof(ReferralRedemption), referralRedemptionId);

        // Idempotency guard — webhook re-delivery or finalize re-call must not
        // issue a second coupon for the same redemption.
        if (redemption.ReferrerRewardApplied)
        {
            logger.LogInformation(
                "Referrer reward already applied for redemption {@RedemptionId}; skipping.",
                referralRedemptionId);
            return;
        }

        ReferralCode? code = await db.ReferralCodes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == redemption.ReferralCodeId, ct);

        if (code is null)
        {
            logger.LogWarning(
                "Referral code for redemption {@RedemptionId} not found; skipping reward.",
                referralRedemptionId);
            return;
        }

        // ── Self-referral fraud check ─────────────────────────────────────────
        // Compare owner emails in memory. Never log the email values — log only IDs.
        // TODO(product): self-referral policy — currently logs and skips reward;
        //                confirm whether to block silently, flag for support review,
        //                or rate-limit before merging.
        Studio? newStudio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == redemption.NewStudioId, ct);

        Studio? referringStudio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == code.StudioId, ct);

        if (newStudio is not null && referringStudio is not null &&
            string.Equals(referringStudio.OwnerEmail, newStudio.OwnerEmail,
                          StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Self-referral detected for redemption {@RedemptionId}: referring studio " +
                "{@ReferrerStudioId} and new studio {@NewStudioId} share an owner. " +
                "Reward skipped — review recommended.",
                referralRedemptionId, referringStudio.Id, redemption.NewStudioId);
            return;
        }

        // ── Referrer's active Stripe subscription ────────────────────────────
        Subscription? referrerSub = await db.Subscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s =>
                s.StudioId == code.StudioId &&
                s.Status   == SubscriptionStatus.Active &&
                s.StripeSubscriptionId != null, ct);

        if (referrerSub?.StripeSubscriptionId is null)
        {
            // TODO(product): referrer has no active Stripe subscription (Trialing,
            // cash-billed, Free tier, or cancelled). Decide whether to:
            //   (a) queue a pending reward to apply on their next real Stripe subscribe
            //   (b) handle via support manually
            //   (c) not reward this case at all
            // Until that decision is made, log and return — don't silently discard.
            logger.LogWarning(
                "Referrer studio {@ReferrerStudioId} has no active Stripe subscription; " +
                "reward not applied for redemption {@RedemptionId}. Manual review may be required.",
                code.StudioId, referralRedemptionId);
            return;
        }

        // ── Issue and apply the coupon ────────────────────────────────────────
        // Idempotency key scoped to THIS redemption, distinct from the referred
        // studio's coupon key ("referral-coupon-{studioId}") to avoid collision.
        string idempotencyKey = $"referrer-reward-{referralRedemptionId}";

        string couponId;
        try
        {
            couponId = await discounts.CreateOneMonthFreeCouponAsync(idempotencyKey, ct);
        }
        catch (Exception ex)
        {
            // Coupon creation failure must not roll back or corrupt the referred
            // studio's subscription, which is already committed. Log and return.
            logger.LogError(ex,
                "Failed to create referrer reward coupon for redemption {@RedemptionId}; " +
                "subscription unaffected. Referrer studio: {@ReferrerStudioId}.",
                referralRedemptionId, code.StudioId);
            return;
        }

        try
        {
            await billing.ApplyCouponToActiveSubscriptionAsync(
                referrerSub.StripeSubscriptionId, couponId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to apply referrer reward coupon {@CouponId} to Stripe subscription " +
                "for redemption {@RedemptionId}. Referrer studio: {@ReferrerStudioId}. " +
                "Coupon was created and should be applied manually.",
                couponId, referralRedemptionId, code.StudioId);
            return;
        }

        redemption.ReferrerRewardApplied  = true;
        redemption.ReferrerRewardCouponId = couponId;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Referrer reward applied for redemption {@RedemptionId}. " +
            "Referrer studio {@ReferrerStudioId} received coupon.",
            referralRedemptionId, code.StudioId);
    }
}
```

Key behaviours:
- Idempotent via `ReferrerRewardApplied` flag.
- Self-referral logs and skips (policy: review, not silent block).
- No Stripe subscription → logs and returns (no-op, pending path deferred).
- Coupon creation failure → non-fatal, log only.
- Apply failure → non-fatal, log with coupon ID for manual recovery.
- No PII in any log statement — only IDs, coupon ID, exception.

---

## Phase 6 — DI registration

Find where `StripeDiscountService` is registered (look for
`services.AddScoped<IStripeDiscountService, StripeDiscountService>()` in
`Pena_e_Arte.Infrastructure/DependencyInjection.cs` or `Program.cs`).

Immediately after, add:

```csharp
services.AddScoped<IReferralRewardService, ReferralRewardService>();
```

Use the exact interface namespace resolved in Phase 4.

---

## Phase 7 — `CreateSubscriptionCommand.cs`

### 7a — Inject `IReferralRewardService`

Add `IReferralRewardService rewardService` to the primary constructor:

```csharp
public class CreateSubscriptionHandler(
    IAppDbContext                          db,
    ICurrentTenant                         tenant,
    IStripeBillingService                  billing,
    IStripeDiscountService                 discounts,
    IReferralRewardService                 rewardService,   // ← add
    ILogger<CreateSubscriptionHandler>     logger)
    : IRequestHandler<CreateSubscriptionCommand, SubscriptionResponse>
```

### 7b — Capture the new `ReferralRedemption` before save

In `Handle`, change the referral redemption block (currently around lines
107–118) to capture the entity reference:

```csharp
// Record redemption only when a discount was actually applied
ReferralRedemption? newRedemption = null;
if (pendingCode is not null && discountApplied)
{
    newRedemption = new ReferralRedemption
    {
        ReferralCodeId  = pendingCode.Id,
        NewStudioId     = tenant.StudioId,
        DiscountApplied = true,
    };
    db.ReferralRedemptions.Add(newRedemption);

    if (pendingCode.IsSingleUse)
        pendingCode.IsActive = false;
}
```

### 7c — Call `RewardReferrerAsync` after save

After `await db.SaveChangesAsync(ct)`:

```csharp
await db.SaveChangesAsync(ct);

// Reward the referrer now that the referred studio's discount is committed.
// Non-fatal: failure is logged inside RewardReferrerAsync; subscription is not rolled back.
if (newRedemption is not null)
    await rewardService.RewardReferrerAsync(newRedemption.Id, ct);

return Map(subscription);
```

Because `ReferralRedemption.Id` is `Guid.NewGuid()` at construction, the ID is
available before `SaveChangesAsync` — no second save is needed to read it back.

---

## Phase 8 — `ActivateCheckoutSubscriptionCommand.cs`

### 8a — Inject `IReferralRewardService`

```csharp
public class ActivateCheckoutSubscriptionHandler(
    IAppDbContext                                  db,
    IStripeBillingService                          billing,
    IReferralRewardService                         rewardService,   // ← add
    ILogger<ActivateCheckoutSubscriptionHandler>   logger)
```

### 8b — Refactor `RecordReferralRedemptionAsync` to return the entity

Change the return type from `void` to `ReferralRedemption?`:

```csharp
private async Task<ReferralRedemption?> RecordReferralRedemptionAsync(
    Studio? studio, bool hasDiscount, CancellationToken ct)
{
    if (studio?.PendingReferralCodeId is not Guid refCodeId) return null;

    ReferralCode? code = await db.ReferralCodes.FirstOrDefaultAsync(r => r.Id == refCodeId, ct);

    ReferralRedemption newRedemption = new()
    {
        ReferralCodeId  = refCodeId,
        NewStudioId     = studio.Id,
        DiscountApplied = hasDiscount,
    };
    db.ReferralRedemptions.Add(newRedemption);

    if (code is { IsSingleUse: true } && hasDiscount)
        code.IsActive = false;

    studio.PendingReferralCodeId = null;

    return newRedemption;
}
```

### 8c — Call `RewardReferrerAsync` after save in `Handle`

In `Handle`, update the call and the post-save block:

```csharp
ReferralRedemption? newRedemption =
    await RecordReferralRedemptionAsync(subscription.Studio, result.HasDiscount, ct);

await db.SaveChangesAsync(ct);
logger.LogInformation("Subscription activated via checkout for studio {@StudioId}", studioId);

// Reward the referrer if the referred studio's discount was applied.
if (newRedemption is { DiscountApplied: true })
    await rewardService.RewardReferrerAsync(newRedemption.Id, ct);

return CreateSubscriptionHandler.Map(subscription);
```

---

## Phase 9 — Contracts + query

### 9a — `ReferralStatsResponse.cs`

```csharp
namespace Pena_e_Arte.Contracts.Responses;

public record ReferralStatsResponse(
    string? Code,
    int     RedemptionCount,
    int     DiscountsApplied,
    int     ReferrerRewardsApplied);   // ← add
```

Check that all callers of this constructor (only `GetReferralStatsQuery`) are
updated.

### 9b — `GetReferralStatsQuery.cs`

Add `ReferrerRewardsApplied` to the return statement:

```csharp
return new ReferralStatsResponse(
    active?.Code,
    redemptions.Count,
    redemptions.Count(r => r.DiscountApplied),
    redemptions.Count(r => r.ReferrerRewardApplied));   // ← add
```

---

## Phase 10 — Frontend

### 10a — TypeScript type

Find the TypeScript type for `ReferralStatsResponse` (search for `redemptionCount`
or `discountsApplied` in the frontend source — likely in `studiosApi.ts` as an
inline type or in a `studio.types.ts` file). Add `referrerRewardsApplied: number`.

Example (adapt to wherever the type actually lives):

```typescript
export interface ReferralStatsResponse {
  code:                   string | null;
  redemptionCount:        number;
  discountsApplied:       number;
  referrerRewardsApplied: number;   // ← add
}
```

### 10b — `ReferralCodeCard.tsx`

Currently shows a 2-column stats grid when `stats.redemptionCount > 0 || referralCode`.
Add a third stat cell for rewards earned.

Replace the `grid grid-cols-2` section:

```tsx
{stats && (stats.redemptionCount > 0 || referralCode) && (
  <div className="grid grid-cols-3 gap-3 pt-1">
    <div className="rounded-md border px-3 py-2 text-center">
      <p className="text-xl font-semibold">{stats.redemptionCount}</p>
      <p className="text-xs text-muted-foreground">
        Studio{stats.redemptionCount !== 1 ? "s" : ""} referred
      </p>
    </div>
    <div className="rounded-md border px-3 py-2 text-center">
      <p className="text-xl font-semibold">{stats.discountsApplied}</p>
      <p className="text-xs text-muted-foreground">Discounts applied</p>
    </div>
    {/* TODO(marketing): confirm copy for referrer reward stat label */}
    <div className="rounded-md border px-3 py-2 text-center">
      <p className="text-xl font-semibold">{stats.referrerRewardsApplied}</p>
      <p className="text-xs text-muted-foreground">Rewards earned</p>
    </div>
  </div>
)}
```

Also update the card description paragraph to surface the two-sided benefit:

```tsx
<p className="text-sm text-muted-foreground">
  Share your referral link. New studios that sign up with your code get one
  month free when they subscribe — and so do you.
  {/* TODO(marketing): confirm copy before shipping */}
</p>
```

---

## Phase 11 — Tests

### 11a — `CreateSubscriptionDiscountTests.cs`

**Update the constructor and `CreateSut`** to inject `IReferralRewardService`:

```csharp
private readonly IReferralRewardService  _rewardService = Substitute.For<IReferralRewardService>();

// In CreateSut():
private CreateSubscriptionHandler CreateSut() =>
    new(_db, _tenant, _billing, _discounts, _rewardService,
        NullLogger<CreateSubscriptionHandler>.Instance);
```

**Verify existing tests still pass** — `_rewardService` is mocked and calls are
fire-and-forget; no existing test assertions should break.

**Add new tests for the referrer reward side:**

```csharp
[Fact]
public async Task Handle_WithValidPendingReferralCode_CallsRewardReferrerAsync()
{
    // Existing flow: discount applied → RewardReferrerAsync must be called once
    // with the ID of the newly created ReferralRedemption.
    Guid planId = await SeedPlan(stripePriceId: "price_monthly_ref");
    ReferralCode code = await SeedReferralCode(isActive: true, expiresAt: null);
    await SeedSubscription(planId: null, pendingReferralCodeId: code.Id);

    await CreateSut().Handle(
        new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId)), default);

    await _rewardService.Received(1).RewardReferrerAsync(
        Arg.Any<Guid>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task Handle_WithoutDiscount_DoesNotCallRewardReferrerAsync()
{
    // Expired code → no discount → reward service must NOT be called.
    Guid planId = await SeedPlan();
    ReferralCode code = await SeedReferralCode(isActive: true, expiresAt: DateTime.UtcNow.AddDays(-1));
    await SeedSubscription(planId: null, pendingReferralCodeId: code.Id);

    await CreateSut().Handle(
        new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId)), default);

    await _rewardService.DidNotReceive().RewardReferrerAsync(
        Arg.Any<Guid>(), Arg.Any<CancellationToken>());
}
```

**Add `ReferralRewardServiceTests.cs`** in the same `Referrals` test folder:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Referrals.Services;   // adjust
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Referrals;

public class ReferralRewardServiceTests
{
    private readonly FakeDbContext           _db        = FakeDbContext.Create();
    private readonly IStripeBillingService   _billing   = Substitute.For<IStripeBillingService>();
    private readonly IStripeDiscountService  _discounts = Substitute.For<IStripeDiscountService>();

    public ReferralRewardServiceTests()
    {
        _discounts.CreateOneMonthFreeCouponAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                  .Returns("coup_referrer_reward");
    }

    private ReferralRewardService CreateSut() =>
        new(_db, _billing, _discounts, NullLogger<ReferralRewardService>.Instance);

    [Fact]
    public async Task RewardReferrerAsync_AppliesCoupon_WhenReferrerHasActiveStripeSubscription()
    {
        // Arrange
        (ReferralRedemption redemption, string referrerSubId) =
            await SeedFullReferralScenario(referrerHasStripeSub: true);

        // Act
        await CreateSut().RewardReferrerAsync(redemption.Id, default);

        // Assert: coupon created with a referrer-scoped idempotency key
        await _discounts.Received(1).CreateOneMonthFreeCouponAsync(
            Arg.Is<string>(k => k.StartsWith("referrer-reward-")),
            Arg.Any<CancellationToken>());

        // Assert: coupon applied to referrer's Stripe subscription
        await _billing.Received(1).ApplyCouponToActiveSubscriptionAsync(
            referrerSubId, "coup_referrer_reward", Arg.Any<CancellationToken>());

        // Assert: redemption updated in DB
        ReferralRedemption updated = _db.ReferralRedemptions
            .Single(r => r.Id == redemption.Id);
        updated.ReferrerRewardApplied.Should().BeTrue();
        updated.ReferrerRewardCouponId.Should().Be("coup_referrer_reward");
    }

    [Fact]
    public async Task RewardReferrerAsync_IsIdempotent_WhenCalledTwice()
    {
        (ReferralRedemption redemption, _) =
            await SeedFullReferralScenario(referrerHasStripeSub: true);

        await CreateSut().RewardReferrerAsync(redemption.Id, default);
        await CreateSut().RewardReferrerAsync(redemption.Id, default);  // second call

        // Coupon created exactly once despite two calls
        await _discounts.Received(1).CreateOneMonthFreeCouponAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RewardReferrerAsync_NoOp_WhenReferrerHasNoActiveStripeSub()
    {
        (ReferralRedemption redemption, _) =
            await SeedFullReferralScenario(referrerHasStripeSub: false);

        await CreateSut().RewardReferrerAsync(redemption.Id, default);

        // Nothing applied
        await _discounts.DidNotReceive().CreateOneMonthFreeCouponAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _billing.DidNotReceive().ApplyCouponToActiveSubscriptionAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        ReferralRedemption updated = _db.ReferralRedemptions.Single(r => r.Id == redemption.Id);
        updated.ReferrerRewardApplied.Should().BeFalse();
    }

    [Fact]
    public async Task RewardReferrerAsync_SkipsReward_WhenSelfReferral()
    {
        // Same owner email on both studios → self-referral
        string sharedEmail = "same@owner.com";
        (ReferralRedemption redemption, _) =
            await SeedFullReferralScenario(referrerHasStripeSub: true,
                                           referrerOwnerEmail: sharedEmail,
                                           newStudioOwnerEmail: sharedEmail);

        await CreateSut().RewardReferrerAsync(redemption.Id, default);

        await _billing.DidNotReceive().ApplyCouponToActiveSubscriptionAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.ReferralRedemptions.Single(r => r.Id == redemption.Id)
           .ReferrerRewardApplied.Should().BeFalse();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<(ReferralRedemption, string referrerSubId)> SeedFullReferralScenario(
        bool   referrerHasStripeSub,
        string referrerOwnerEmail   = "referrer@studio.com",
        string newStudioOwnerEmail  = "new@studio.com")
    {
        Studio referringStudio = new()
        {
            Id         = Guid.NewGuid(),
            Name       = "Referring Studio",
            Slug       = "ref",
            City       = "Porto",
            OwnerEmail = referrerOwnerEmail,
            IsActive   = true,
        };

        Studio newStudio = new()
        {
            Id         = Guid.NewGuid(),
            Name       = "New Studio",
            Slug       = "new",
            City       = "Lisbon",
            OwnerEmail = newStudioOwnerEmail,
            IsActive   = true,
        };

        _db.Studios.AddRange(referringStudio, newStudio);

        ReferralCode code = new()
        {
            StudioId    = referringStudio.Id,
            Code        = "REFTEST1",
            IsActive    = true,
            IsSingleUse = true,
        };
        _db.ReferralCodes.Add(code);

        string referrerSubId = "sub_referrer_test";
        if (referrerHasStripeSub)
        {
            _db.Subscriptions.Add(new Subscription
            {
                StudioId             = referringStudio.Id,
                Status               = SubscriptionStatus.Active,
                StripeSubscriptionId = referrerSubId,
                CurrentPeriodEnd     = DateTime.UtcNow.AddMonths(1),
            });
        }

        ReferralRedemption redemption = new()
        {
            ReferralCodeId = code.Id,
            NewStudioId    = newStudio.Id,
            DiscountApplied = true,
        };
        _db.ReferralRedemptions.Add(redemption);

        await _db.SaveChangesAsync();
        return (redemption, referrerSubId);
    }
}
```

### 11b — `ReferralFlowIntegrationTests.cs`

Add two scenarios at the end of the class:

```csharp
[Fact]
public async Task FullReferralFlow_WithReferrerOnStripeSub_AppliesRewardCoupon()
{
    // 1. Referring studio is seeded with an active Stripe subscription.
    Guid referringStudioId = await SeedReferringStudio();
    string referrerStripeSubId = await SeedReferrerSubscription(referringStudioId);
    string code = await GenerateCode(referringStudioId);

    // 2. New studio registers with the referral code.
    Guid planId   = await SeedPlan();
    Guid newStudioId = await RegisterNewStudio(code);

    // 3. Mock Stripe services.
    IStripeBillingService billing = Substitute.For<IStripeBillingService>();
    billing.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns("cus_two_sided");
    billing.CreateSubscriptionAsync(
               Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
           .Returns(("sub_two_sided", DateTime.UtcNow.AddMonths(1)));

    IStripeDiscountService discounts = Substitute.For<IStripeDiscountService>();
    discounts.CreateOneMonthFreeCouponAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns("coup_two_sided");

    // Set up reward service with real implementation backed by a fresh DbContext.
    ReferralRewardService rewardSvc = new(
        fixture.CreateDbContext(Guid.Empty), billing, discounts,
        NullLogger<ReferralRewardService>.Instance);

    CurrentTenantService tenantSvc = new();
    tenantSvc.SetTenant(newStudioId);

    CreateSubscriptionHandler subHandler = new(
        fixture.CreateDbContext(Guid.Empty), tenantSvc, billing, discounts,
        rewardSvc, NullLogger<CreateSubscriptionHandler>.Instance);

    // 4. New studio subscribes.
    await subHandler.Handle(
        new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId)), default);

    // 5. Verify ReferralRedemption has ReferrerRewardApplied = true.
    await using AppDbContext verifyDb = fixture.CreateDbContext(Guid.Empty);
    ReferralRedemption? redemption = await verifyDb.ReferralRedemptions
        .FirstOrDefaultAsync(r => r.NewStudioId == newStudioId);
    redemption.Should().NotBeNull();
    redemption!.ReferrerRewardApplied.Should().BeTrue();
    redemption.ReferrerRewardCouponId.Should().Be("coup_two_sided");

    // 6. Verify ApplyCouponToActiveSubscriptionAsync was called with the referrer's sub ID.
    await billing.Received(1).ApplyCouponToActiveSubscriptionAsync(
        referrerStripeSubId, "coup_two_sided", Arg.Any<CancellationToken>());
}

[Fact]
public async Task FullReferralFlow_ReferrerNotOnStripeSub_RedemptionSaved_RewardNotApplied()
{
    // Referring studio has no Stripe subscription → reward not applied, but
    // redemption is still recorded and the new studio's discount is unaffected.
    Guid referringStudioId = await SeedReferringStudio();  // no Stripe sub added
    string code = await GenerateCode(referringStudioId);

    Guid planId      = await SeedPlan();
    Guid newStudioId = await RegisterNewStudio(code);

    IStripeBillingService billing = Substitute.For<IStripeBillingService>();
    billing.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns("cus_no_reward");
    billing.CreateSubscriptionAsync(
               Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
           .Returns(("sub_no_reward", DateTime.UtcNow.AddMonths(1)));

    IStripeDiscountService discounts = Substitute.For<IStripeDiscountService>();
    discounts.CreateOneMonthFreeCouponAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns("coup_no_reward");

    ReferralRewardService rewardSvc = new(
        fixture.CreateDbContext(Guid.Empty), billing, discounts,
        NullLogger<ReferralRewardService>.Instance);

    CurrentTenantService tenantSvc = new();
    tenantSvc.SetTenant(newStudioId);

    await new CreateSubscriptionHandler(
        fixture.CreateDbContext(Guid.Empty), tenantSvc, billing, discounts,
        rewardSvc, NullLogger<CreateSubscriptionHandler>.Instance)
        .Handle(new CreateSubscriptionCommand(new CreateSubscriptionRequest(planId)), default);

    await using AppDbContext verifyDb = fixture.CreateDbContext(Guid.Empty);
    ReferralRedemption? redemption = await verifyDb.ReferralRedemptions
        .FirstOrDefaultAsync(r => r.NewStudioId == newStudioId);

    redemption!.DiscountApplied.Should().BeTrue();     // referred-side discount still applied
    redemption.ReferrerRewardApplied.Should().BeFalse(); // referrer side not applied

    await billing.DidNotReceive().ApplyCouponToActiveSubscriptionAsync(
        Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
}
```

Add these helpers to the `ReferralFlowIntegrationTests` class (they may partially
overlap with existing helpers — check before adding):

```csharp
private async Task<string> SeedReferrerSubscription(Guid studioId)
{
    string stripeSubId = $"sub_referrer_{studioId:N}";
    await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);

    Plan plan = new() { Name = "Referrer Plan", BillingInterval = BillingInterval.Monthly, PriceMonthly = 49m };
    db.Plans.Add(plan);
    db.Subscriptions.Add(new Subscription
    {
        StudioId             = studioId,
        PlanId               = plan.Id,
        Status               = SubscriptionStatus.Active,
        StripeSubscriptionId = stripeSubId,
        CurrentPeriodEnd     = DateTime.UtcNow.AddMonths(1),
    });
    await db.SaveChangesAsync();
    return stripeSubId;
}

private async Task<Guid> RegisterNewStudio(string referralCode)
{
    RegisterStudioHandler handler = new(
        fixture.CreateDbContext(Guid.Empty),
        Substitute.For<IJobScheduler>(),
        NullLogger<RegisterStudioHandler>.Instance);

    string slug = ("rwd-" + Guid.NewGuid().ToString("N"))[..20];
    StudioResponse studio = await handler.Handle(
        new RegisterStudioCommand(new RegisterStudioRequest(
            Name: "Reward Test Studio", Slug: slug, City: "Lisbon",
            Latitude: 38.7, Longitude: -9.1,
            OwnerEmail: $"{slug}@test.com", ReferralCode: referralCode)),
        default);

    return studio.Id;
}
```

---

## Phase 12 — Architecture decisions log

In `docs/claude/architecture.md`, add a new entry to the Decisions Log under
`IgnoreQueryFilters` approved usages. Use the number `N` found in Phase 0 + 1.

Template (adapt wording to match the existing log style):

```markdown
### IgnoreQueryFilters approved: usage #N — ReferralRewardService cross-tenant reward

**Date:** 2026-07-18
**File:** `Pena_e_Arte.Infrastructure/Services/ReferralRewardService.cs`
**Reads:** `ReferralRedemption`, `ReferralCode`, `Studio` (referrer), `Studio` (new),
           `Subscription` (referrer)
**Why:** When a new studio subscribes, the active request's tenant context is the
new studio. The referring studio is a *different* tenant. Reading the referring
studio's subscription to apply a reward coupon is an intentional cross-tenant
operation — the tenant filter must be bypassed.
**Safeguards:**
- No tenant data from the referrer is written back to the new studio's tenant context.
- No PII (emails, names) appears in any log statement. Only IDs and the coupon ID are logged.
- Self-referral guard prevents reward abuse when `OwnerEmail` matches between studios.
- Idempotency flag `ReferrerRewardApplied` prevents double-application on retries.
```

---

## Quality gates

Before marking this work done, verify all of the following:

- [ ] `dotnet build` — zero errors, zero warnings
- [ ] `dotnet test` — all existing tests pass; all new tests pass
- [ ] `pnpm lint` — no errors
- [ ] `pnpm test` — all existing frontend tests pass
- [ ] Migration file exists with correct Up/Down for two new columns
- [ ] `ApplyCouponToActiveSubscriptionAsync` is defined on both `IStripeBillingService` and `StripeBillingService`
- [ ] `ReferralRewardService` is registered in DI
- [ ] Both `CreateSubscriptionHandler` and `ActivateCheckoutSubscriptionHandler` inject `IReferralRewardService`
- [ ] `ReferralStatsResponse` has four fields (breaking change: confirm no other callers are broken)
- [ ] `ReferralCodeCard` shows three stat cells
- [ ] `RewardReferrerAsync` is NOT called when `discountApplied = false` (covers expired/inactive codes)

---

## Open questions — do not resolve in code; surface as `// TODO(owner):` comments

1. **Reward size/type** — "one month free" mirrors the referred side. A flat
   credit, percentage off, or capped discount are all alternatives. This is a
   pricing/business call. Leave `// TODO(product): confirm reward size before launch`.

2. **Pending path** — when the referrer has no active Stripe subscription at
   reward time, the current implementation logs and skips. Decide between:
   (a) `PendingReferrerRewardCount` on `Studio`, honoured on next Stripe subscribe;
   (b) manual/support path;
   (c) not rewarding that case.
   Leave `// TODO(product): pending-reward path for non-Stripe referrers — see architecture.md`.

3. **Self-referral policy** — currently logs and skips. Decide whether to:
   (a) silently block;
   (b) flag for support review (Slack alert, support ticket);
   (c) rate-limit per studio.
   Leave `// TODO(product): self-referral policy — see Phase 5 comment`.

4. **Stacking cap** — reusable codes (`IsSingleUse = false`) could accumulate
   multiple rewards for one referrer. Decide: lifetime max, "one active at a time",
   or uncapped. Also verify Stripe's behaviour when two `repeating, 1 month` coupons
   are applied while one is still active. Leave `// TODO(product): stacking cap
   for reusable referral codes — validate against Stripe test environment`.

5. **`PlatformReferralCodeResponse`** (issuer-facing view) — confirm whether
   issuer-side visibility of `ReferrerRewardsApplied` is in scope for this branch
   or a fast-follow. Leave `// TODO(product): issuer-side referrer reward stats
   in scope for this branch?`.

---

## Forbidden actions

- Do NOT add new ORM, HTTP client, or NuGet package — `Stripe.net` is already
  installed; `IStripeDiscountService` and `IStripeBillingService` are the only
  Stripe abstraction layers permitted.
- Do NOT bypass global query filters without a documented `IgnoreQueryFilters
  approved: usage #N` comment and matching Decisions Log entry.
- Do NOT log any PII — `OwnerEmail` is compared in-memory and discarded; only
  IDs appear in Serilog calls.
- Do NOT write coupon creation/application logic in both handlers inline —
  `IReferralRewardService` is the single canonical location.
- Do NOT call `RewardReferrerAsync` when `discountApplied` is `false` — the
  referred studio's discount not landing is a signal that the referral did not
  convert, and the referrer should not be rewarded.
- Do NOT introduce stacking behaviour without an explicit product decision.
- Do NOT use `any` in TypeScript.
- Do NOT skip tests for `ReferralRewardService` — it contains non-trivial business
  logic (idempotency, self-referral check, non-Stripe path).
