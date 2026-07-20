# Overnight Prompt — Plan/Billing-Interval Model Redesign (`PlanPrice`)

**Date:** 2026-07-19
**Type:** Architecture change — new child entity, data migration, ~20 backend files,
~5 frontend files, new + updated tests across both. This is a large, multi-phase
change. Work through the phases **in order** — later phases depend on earlier ones
compiling and passing tests.
**Spec:** this prompt implements the design in
`docs/spec-plan-billing-interval-redesign.md` (save the spec text you were given as
this file at repo root before starting, if it isn't there yet — Phase 0 assumes it
exists). Recommended option **2.1 (`PlanPrice` child table)** is the one being built
tonight, per the spec's own recommendation.

---

## Precondition — read this before touching anything

This redesign assumes `DataSeeder.ReconcileCorePlansAsync` and
`DataSeeder.RetireOrphanedNamedPlansAsync` (both already live in
`Pena_e_Arte.Infrastructure/Persistence/Seed/DataSeeder.cs` — confirmed in source,
not just in the two prior bug reports) have run at least once against whatever
database this migration applies to. That means, by the time tonight's migration
runs, the `plans` table is already guaranteed to contain **exactly six rows** —
`Free`, `Starter`, `Growth`, `Premium` (×2 — `PremiumMonthlyPlanId` and
`PremiumYearlyPlanId`), `Pro` — with no orphans and no drifted `Max*`/limit fields.
Tonight's migration's data-backfill step is written against that guarantee (static,
literal-Id SQL, not a dynamic "find whatever orphan happens to exist" scan). If this
is ever applied to an environment that has never booted the current `main` even
once, run the app once against the pre-migration code first so those two existing
reconcilers get a chance to clean house — or accept that a brand-new/never-seeded
environment has nothing to migrate anyway.

---

## Confirmed additional scope beyond the spec's own tables

The spec's Section 4/5 tables are a good starting map but are not exhaustive — the
following were found by grepping the actual codebase for
`PriceMonthly|PriceYearly|BillingInterval|StripePriceIdMonthly|StripePriceIdYearly|PairedPlanId`
across `Pena_e_Arte.Application`, `Pena_e_Arte.Infrastructure`, and `frontend/src`.
**Do not skip these** — they are real, currently-live consumers of the old flat
fields, just not the ones the spec happened to cite:

| File | What it does today | Why it's in scope |
|---|---|---|
| `Pena_e_Arte.Application/Billing/Commands/CreateSubscriptionCommand.cs` | Resolves `priceId` from `plan.BillingInterval == Monthly ? StripePriceIdMonthly : StripePriceIdYearly`, same pattern as the two files the spec cited | New-subscription checkout entry point — same fix as `CreateSubscriptionCheckoutCommand.cs` |
| `Pena_e_Arte.Application/Billing/Commands/ActivateCheckoutSubscriptionCommand.cs` | Resolves the completed-checkout's `Plan` via `p.StripePriceIdMonthly == result.PriceId \|\| p.StripePriceIdYearly == result.PriceId` | Fires from the Stripe webhook AND the owner's return-from-checkout finalize call — same class of bug as `HandleSubscriptionUpdatedCommand` (which the spec flagged as "not yet reviewed") |
| `Pena_e_Arte.Application/Platform/Queries/GetPlatformStatsQuery.cs` | MRR = `Sum(s => s.Subscription.Plan.PriceMonthly)` | **Confirmed pre-existing revenue-reporting bug**, not just an architecture nit: for any subscription on `PremiumYearlyPlanId` today, `Plan.PriceMonthly` is the *decorative reference figure* (79), not the real monthly-equivalent revenue (790/12 = 65.83) — MRR is currently overstated for every yearly-billed studio. This redesign fixes it as a side effect; treat it as a real bug fix, not a nice-to-have |
| `Pena_e_Arte.Application/Platform/Queries/GetMrrHistoryQuery.cs` | Same `Sum(... => s.Plan.PriceMonthly)` pattern, for the MRR history chart | Same bug, same fix |
| `Pena_e_Arte.Infrastructure/Persistence/Seed/StripeDemoSeeder.cs` | Provisions a real Stripe test-mode price for **both** Monthly and Yearly on **every** plan unconditionally, including Starter/Growth/Pro, which never actually sell Yearly today | Must iterate `PlanPrice` rows instead — provisioning a Yearly Stripe price for a tier that has no Yearly `PlanPrice` row would silently fabricate purchasability that doesn't exist |
| `Pena_e_Arte.Infrastructure/Persistence/Configurations/PlanConfiguration.cs` | Maps the flat fields being removed; `PairedPlanId` index | Full rewrite; new sibling `PlanPriceConfiguration.cs` needed |
| `frontend/src/features/billing/components/BillingPage.tsx` | `currentPlan.priceMonthly` used unconditionally in "Next charge" / current-plan display, even for a studio actually billed yearly | Not in the spec's frontend table at all — same display bug as the MRR one above, just customer-facing instead of issuer-facing: a yearly-billed studio's own Billing page currently always says "/ month" |

Also confirmed, so it doesn't need re-deriving: `Pena_e_Arte.Application/Billing/Commands/ActivateSubscriptionManuallyCommand.cs` (issuer manual cash-activation) never referenced `BillingInterval` before and doesn't need pricing logic changed — it only needs to set a sensible default now that the field exists on `Subscription` (see Phase 4).

---

## Design decisions made for you (don't re-litigate these mid-implementation)

1. **Two migrations, both written tonight, not two calendar-separated deploys.**
   The spec's Section 7 rollout sequencing (don't combine the additive schema change
   with dropping old columns) is sound engineering advice for a live production
   system taking real traffic. This one isn't yet — confirmed by architecture.md's
   own Decisions Log ("no deployment pipeline exists in this repo to point one at
   yet"). So: **Migration A** (additive: `plan_prices` table,
   `Subscriptions.BillingInterval`/`PendingBillingInterval`, data backfill, Premium
   row merge, orphan fold-in) and **Migration B** (drops the six now-dead `Plan`
   columns) are still two separate, separately-reviewable migration files — satisfying
   the actual engineering concern — but both get written and applied in this same
   session, once Migration A's data is verified correct and every application code
   path has been updated to stop reading the old columns (Phase 9 gates this).
2. **`Plan.cs` reaches its final target shape immediately** (spec Section 4's
   literal instruction) — the old flat fields are removed from the C# entity now,
   not kept around "just in case." Migration A's data backfill uses **raw SQL**
   (`migrationBuilder.Sql(...)`) to read the old physical columns — which still
   exist in the database until Migration B runs — so there's no conflict between
   "the C# model no longer has these properties" and "the backfill still needs their
   values." This is the standard, correct way to do this in EF Core.
3. **`Subscription` gets both `BillingInterval` and `PendingBillingInterval`.** The
   spec only asked for `BillingInterval` (Section 2.4), but once tier and interval
   are independent choices, a *scheduled* downgrade (`PendingPlanId`) also needs to
   remember which interval it's scheduled onto — otherwise `ChangePlanCommand`'s
   downgrade path has nowhere to put "switch to Starter, yearly, at period end."
   `PendingBillingInterval` mirrors `PendingPlanId` exactly: set together, cleared
   together (by `CancelPlanChangeCommand` and once `HandleSubscriptionUpdatedCommand`
   sees the change land).
4. **`PlanPrice.Price` is reconciled by the new seeder (source-of-truth, like the
   old flat `PriceMonthly`/`PriceYearly` were); `PlanPrice.StripePriceId` is never
   touched by the reconciler once a row exists** — same split the original
   `ReconcileCorePlansAsync` already established ("Stripe price IDs are deliberately
   left untouched"). Stripe price IDs are account-specific and either come from
   `StripeDemoSeeder` or real issuer configuration via `PlanManagementPage`.
5. **Starter/Growth/Pro do not gain a real Yearly `PlanPrice` row tonight.** The
   migration backfill preserves current real purchasing behavior exactly (only
   Premium has ever had a working Yearly checkout). The new model *supports* adding
   Yearly to any tier — that's the whole point — but fabricating a Yearly price for
   a tier that's never had a real Stripe Yearly price configured is a product
   decision for an issuer to make later via the updated `PlanManagementPage`, not
   something to invent in a data migration. `Plan.YearlyDiscountPercent` stays on
   `Plan` for exactly this reason (spec 2.3) — it's what powers the "suggested yearly
   price" helper the day an issuer flips that toggle on.
6. **The two existing reconciler methods (`ReconcileCorePlansAsync`,
   `RetireOrphanedNamedPlansAsync`) are deleted and replaced by one new method,
   `ReconcileCoreTiersAsync`**, per spec Section 3 step 6 — keyed on tier `Name` +
   `(PlanId, Interval)` for prices, not a fixed `Plan.Id` list. This is what actually
   closes the bug class both prior reports hit; a reconciler that can't produce an
   "orphan" by construction doesn't need a companion cleanup method.

---

## Phase 0 — Required Reading

```
bug-report-plans-page-data-mismatch.md                                   (repo root)
bug-report-premium-plan-duplicate-legacy-row.md                          (repo root)
docs/claude/overnight-prompt-plans-seed-reconciliation-2026-07-19.md      (prior fix — context)
docs/claude/overnight-prompt-orphaned-premium-plan-2026-07-19.md          (prior fix — context)

Pena_e_Arte.Domain/Entities/Plan.cs
Pena_e_Arte.Domain/Entities/Subscription.cs
Pena_e_Arte.Infrastructure/Persistence/Configurations/PlanConfiguration.cs
Pena_e_Arte.Infrastructure/Persistence/Configurations/SubscriptionConfiguration.cs
Pena_e_Arte.Infrastructure/Persistence/Seed/DataSeeder.cs
Pena_e_Arte.Infrastructure/Persistence/Seed/StripeDemoSeeder.cs
Pena_e_Arte.Infrastructure/Migrations/AppDbContextModelSnapshot.cs        (confirm exact current column names/types before writing raw SQL)

Pena_e_Arte.Contracts/Requests/CreateCheckoutRequest.cs
Pena_e_Arte.Contracts/Requests/ChangePlanRequest.cs
Pena_e_Arte.Contracts/Requests/CreatePlanRequest.cs
Pena_e_Arte.Contracts/Requests/UpdatePlanRequest.cs
Pena_e_Arte.Contracts/Responses/PlanResponse.cs
Pena_e_Arte.Contracts/Responses/SubscriptionResponse.cs

Pena_e_Arte.Application/Billing/Commands/CreateSubscriptionCommand.cs
Pena_e_Arte.Application/Billing/Commands/CreateSubscriptionCheckoutCommand.cs
Pena_e_Arte.Application/Billing/Commands/ChangePlanCommand.cs
Pena_e_Arte.Application/Billing/Commands/CancelPlanChangeCommand.cs       (no logic change, re-verify it stays correct)
Pena_e_Arte.Application/Billing/Commands/HandleSubscriptionUpdatedCommand.cs
Pena_e_Arte.Application/Billing/Commands/ActivateCheckoutSubscriptionCommand.cs
Pena_e_Arte.Application/Billing/Commands/ActivateSubscriptionManuallyCommand.cs
Pena_e_Arte.Application/Billing/Queries/GetPlansQuery.cs
Pena_e_Arte.Application/Plans/Commands/CreatePlanCommand.cs
Pena_e_Arte.Application/Plans/Commands/UpdatePlanCommand.cs
Pena_e_Arte.Application/Plans/Commands/DeletePlanCommand.cs
Pena_e_Arte.Application/Platform/Queries/GetPlatformStatsQuery.cs
Pena_e_Arte.Application/Platform/Queries/GetMrrHistoryQuery.cs

frontend/src/features/billing/billing.types.ts
frontend/src/features/billing/billingApi.ts
frontend/src/features/billing/components/SubscribePage.tsx
frontend/src/features/billing/components/BillingPage.tsx
frontend/src/features/platform/components/PlanManagementPage.tsx

tests/Pena_e_Arte.UnitTests/Billing/*.cs                                 (every file in this folder)
tests/Pena_e_Arte.UnitTests/Infrastructure/DataSeederPlanReconciliationTests.cs
tests/Pena_e_Arte.UnitTests/Platform/GetPlatformStatsHandlerTests.cs
tests/Pena_e_Arte.IntegrationTests/Application/PlatformStatsIntegrationTests.cs
tests/Pena_e_Arte.UnitTests/Helpers/FakeDbContext.cs

docs/claude/architecture.md — Decisions Log entries: "Plan billing interval stays
  locked per-row", "Plan usage limits", "Plan Monthly/Yearly pairing", "Core plan
  reconciliation replaces one-time plan seed", "Orphaned legacy plan retirement"
docs/claude/database.md
docs/claude/conventions.md
```

---

## Phase 1 — Domain entities

### 1a. New entity: `Pena_e_Arte.Domain/Entities/PlanPrice.cs`

```csharp
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// One billing cadence a Plan (tier) is actually offered under. A tier that offers
/// only Monthly has one row; a tier offering both has two. Adding a third interval in
/// the future (e.g. Quarterly) needs a new BillingInterval enum member and new rows —
/// no schema change. See architecture.md Decisions Log — "Plan/PlanPrice split".
/// </summary>
public class PlanPrice
{
    public Guid            Id            { get; init; } = Guid.NewGuid();
    public Guid            PlanId        { get; set; }
    public BillingInterval Interval      { get; set; }
    public decimal         Price         { get; set; }

    /// <summary>
    /// Account-specific — never hardcoded/reconciled by DataSeeder once set. Populated
    /// by StripeDemoSeeder or an issuer via PlanManagementPage. Null means this
    /// interval is defined (shows in the issuer's editor) but not yet purchasable
    /// online — see IsActive below for the distinct "temporarily disabled" case.
    /// </summary>
    public string?         StripePriceId { get; set; }

    /// <summary>
    /// Lets an interval be retired (hidden from SubscribePage, rejected by checkout)
    /// without deleting pricing history for studios already on it.
    /// </summary>
    public bool            IsActive      { get; set; } = true;

    public Plan Plan { get; set; } = null!;
}
```

### 1b. `Plan.cs` — remove flat fields, add `Prices`

```csharp
// Before:
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

public class Plan
{
    public Guid            Id                    { get; init; } = Guid.NewGuid();
    public string          Name                  { get; set; }  = string.Empty;
    public BillingInterval BillingInterval       { get; set; }
    public decimal         PriceMonthly          { get; set; }
    public decimal         PriceYearly           { get; set; }
    public int             YearlyDiscountPercent { get; set; }  = 17;
    public bool            AllowBrandingRemoval  { get; set; }  = false;
    public string?         StripePriceIdMonthly  { get; set; }
    public string?         StripePriceIdYearly   { get; set; }
    public DateTime        CreatedAt             { get; init; } = DateTime.UtcNow;

    public int?  MaxArtists             { get; set; }
    public int?  MaxAppointmentsPerMonth { get; set; }
    public int?  MaxNotificationsPerMonth { get; set; }
    public int?  MaxStorageGb           { get; set; }
    public int?  MaxLocations           { get; set; }
    public bool  AllowApiAccess         { get; set; } = false;
    public bool  PrioritySupport        { get; set; } = false;

    public Guid? PairedPlanId           { get; set; }

    public ICollection<Subscription> Subscriptions { get; set; } = [];
}

// After:
namespace Pena_e_Arte.Domain.Entities;

public class Plan
{
    public Guid     Id                    { get; init; } = Guid.NewGuid();
    public string   Name                  { get; set; }  = string.Empty;

    /// <summary>
    /// Marketing/display figure for "Save X% annually" copy and the issuer editor's
    /// suggested-yearly-price helper — NOT itself a price. Real prices live on
    /// PlanPrice, one row per interval this tier actually offers. See architecture.md
    /// Decisions Log — "Plan/PlanPrice split".
    /// </summary>
    public int      YearlyDiscountPercent { get; set; }  = 17;
    public bool     AllowBrandingRemoval  { get; set; }  = false;
    public DateTime CreatedAt             { get; init; } = DateTime.UtcNow;

    public int?  MaxArtists              { get; set; }
    public int?  MaxAppointmentsPerMonth { get; set; }
    public int?  MaxNotificationsPerMonth { get; set; }
    public int?  MaxStorageGb            { get; set; }
    public int?  MaxLocations            { get; set; }
    public bool  AllowApiAccess          { get; set; } = false;
    public bool  PrioritySupport         { get; set; } = false;

    public ICollection<PlanPrice>    Prices        { get; set; } = [];
    public ICollection<Subscription> Subscriptions { get; set; } = [];
}
```

### 1c. `Subscription.cs` — add `BillingInterval` + `PendingBillingInterval`

```csharp
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

public class Subscription
{
    public Guid               Id                   { get; init; } = Guid.NewGuid();
    public Guid                StudioId             { get; set; }
    public Guid?               PlanId               { get; set; }

    /// <summary>Which cadence this subscription is actually billed on. Independent of
    /// PlanId — see architecture.md Decisions Log, "Plan/PlanPrice split".</summary>
    public BillingInterval      BillingInterval      { get; set; } = BillingInterval.Monthly;

    /// <summary>Plan a scheduled downgrade switches to at the end of the current period. Null when no change is pending.</summary>
    public Guid?                PendingPlanId        { get; set; }

    /// <summary>Interval that PendingPlanId will apply under, once it lands. Set and
    /// cleared together with PendingPlanId — always both null or both non-null.</summary>
    public BillingInterval?     PendingBillingInterval { get; set; }

    public SubscriptionStatus Status               { get; set; }
    public DateTime?          TrialExpiresAt       { get; set; }
    public DateTime           CurrentPeriodEnd     { get; set; }
    public DateTime           GracePeriodEnd       { get; set; }
    public string?            StripeSubscriptionId { get; set; }
    public DateTime           CreatedAt            { get; init; } = DateTime.UtcNow;

    public Studio Studio { get; set; } = null!;
    public Plan?  Plan   { get; set; }
}
```

---

## Phase 2 — EF Core configurations

### 2a. `PlanConfiguration.cs` — full rewrite

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("plans");
        builder.HasKey(p => p.Id).HasName("pk_plans");

        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.Property(p => p.AllowApiAccess).HasDefaultValue(false);
        builder.Property(p => p.PrioritySupport).HasDefaultValue(false);

        builder.HasMany(p => p.Prices)
               .WithOne(pp => pp.Plan)
               .HasForeignKey(pp => pp.PlanId)
               .HasConstraintName("fk_plan_prices_plans")
               .OnDelete(DeleteBehavior.Cascade);
    }
}
```

### 2b. New: `PlanPriceConfiguration.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class PlanPriceConfiguration : IEntityTypeConfiguration<PlanPrice>
{
    public void Configure(EntityTypeBuilder<PlanPrice> builder)
    {
        builder.ToTable("plan_prices");
        builder.HasKey(pp => pp.Id).HasName("pk_plan_prices");

        builder.Property(pp => pp.Interval)
               .HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(pp => pp.Price).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(pp => pp.StripePriceId).HasMaxLength(255);
        builder.Property(pp => pp.IsActive).HasDefaultValue(true);

        // One row per (tier, interval) — this is the invariant that makes an "orphan
        // duplicate" structurally impossible going forward.
        builder.HasIndex(pp => new { pp.PlanId, pp.Interval })
               .IsUnique()
               .HasDatabaseName("ux_plan_prices_plan_id_interval");
    }
}
```

### 2c. `SubscriptionConfiguration.cs` — add interval column mappings

```csharp
// Add inside Configure(), alongside the existing builder.Property(s => s.Status)... line:
builder.Property(s => s.BillingInterval)
       .HasConversion<string>().HasMaxLength(32).IsRequired().HasDefaultValue(Domain.Enums.BillingInterval.Monthly);
builder.Property(s => s.PendingBillingInterval)
       .HasConversion<string>().HasMaxLength(32);
```

Confirm `AppDbContext` picks these up automatically — it already uses
`ApplyConfigurationsFromAssembly` (confirmed in `AppDbContext.cs`), so a new
`PlanPriceConfiguration` class needs no separate registration.

---

## Phase 3 — Migration A (additive + data backfill)

Generate with `dotnet ef migrations add AddPlanPriceAndSubscriptionBillingInterval
--project Pena_e_Arte.Infrastructure` after Phases 1–2 compile. EF Core will
auto-generate the `CreateTable`/`AddColumn`/`DropColumn` calls (it will also want to
drop `plans.BillingInterval`/`PriceMonthly`/`PriceYearly`/`StripePriceIdMonthly`/
`StripePriceIdYearly`/`PairedPlanId` and their index, since `Plan.cs` no longer maps
them — **do not let it**; see the split into Migration A/B below).

**Manually edit the generated migration file** so `Up()` contains, in this order:

1. `CreateTable("plan_prices", ...)` + the unique index (auto-generated, keep as-is).
2. `AddColumn<string>("BillingInterval", "subscriptions", nullable: false, defaultValue: "Monthly")` (auto-generated, keep as-is — the default backfills every existing row to Monthly, which Step 5 below then corrects for the two rows that were actually yearly).
3. `AddColumn<string>("PendingBillingInterval", "subscriptions", nullable: true)` (auto-generated, keep as-is).
4. **Remove** any auto-generated `DropColumn` calls for `plans.BillingInterval` /
   `PriceMonthly` / `PriceYearly` / `StripePriceIdMonthly` / `StripePriceIdYearly` /
   `PairedPlanId`, and the `DropIndex("ix_plans_paired_plan_id", ...)` call — these
   move to Migration B (Phase 8). This is the one deliberate deviation from what
   `dotnet ef migrations add` will generate — leave a comment at this spot in the
   file explaining why, referencing this prompt and architecture.md's new Decisions
   Log entry.
5. Hand-append the following `migrationBuilder.Sql(...)` calls, in order, using the
   real GUID literals from `DataSeeder.cs` (`StarterPlanId`, `GrowthPlanId`,
   `ProPlanId`, `PremiumMonthlyPlanId`, `PremiumYearlyPlanId`, `FreePlanId`):

```csharp
// 5a. One Monthly PlanPrice for every canonical plan except the redundant Yearly
//     Premium row (handled separately in 5b/5c — it doesn't survive as its own Plan).
migrationBuilder.Sql($"""
    INSERT INTO plan_prices (Id, PlanId, Interval, Price, StripePriceId, IsActive)
    SELECT UUID(), Id, 'Monthly', PriceMonthly, StripePriceIdMonthly, 1
    FROM plans
    WHERE Id <> '{PremiumYearlyPlanIdLiteral}';
    """);

// 5b. The surviving Premium row's Yearly PlanPrice — sourced from the YEARLY row's
//     own PriceYearly/StripePriceIdYearly (the fields actually gated live by that
//     row's BillingInterval = 'Yearly' today), NOT PremiumMonthlyPlanId's own
//     decorative "reference only" PriceYearly/StripePriceIdYearly fields, which per
//     UpdatePlanHandler's pairing-sync exclusion could have drifted independently.
migrationBuilder.Sql($"""
    INSERT INTO plan_prices (Id, PlanId, Interval, Price, StripePriceId, IsActive)
    SELECT UUID(), '{PremiumMonthlyPlanIdLiteral}', 'Yearly', PriceYearly, StripePriceIdYearly, 1
    FROM plans
    WHERE Id = '{PremiumYearlyPlanIdLiteral}';
    """);

// 5c. Reassign every subscription (active AND pending) off the redundant Yearly row
//     onto the surviving Monthly row, recording that they're actually billed yearly.
migrationBuilder.Sql($"""
    UPDATE subscriptions
    SET PlanId = '{PremiumMonthlyPlanIdLiteral}', BillingInterval = 'Yearly'
    WHERE PlanId = '{PremiumYearlyPlanIdLiteral}';
    """);
migrationBuilder.Sql($"""
    UPDATE subscriptions
    SET PendingPlanId = '{PremiumMonthlyPlanIdLiteral}', PendingBillingInterval = 'Yearly'
    WHERE PendingPlanId = '{PremiumYearlyPlanIdLiteral}';
    """);

// 5d. Delete the now-redundant Yearly row — its price data was copied in 5b, every
//     subscription was moved off it in 5c.
migrationBuilder.Sql($"""
    DELETE FROM plans WHERE Id = '{PremiumYearlyPlanIdLiteral}';
    """);
```

Use the literal GUID string values (e.g. `aaaa0005-0000-0000-0000-000000000000` for
`PremiumYearlyPlanId`) directly in the SQL — `DataSeeder`'s constants aren't
reachable from a migration file, and hardcoding them here is fine, they're fixed
platform constants, not tenant data.

**Verify before moving on:** after this migration, `plans` has exactly 5 rows
(Free/Starter/Growth/Premium/Pro), `plan_prices` has exactly 6 rows (one each for
Free/Starter/Growth/Pro, two for Premium), and every pre-existing `Subscription`
has a non-null `BillingInterval` matching what it was actually being charged before
(spec Section 8's own verification item — confirm this with a query against a local
copy of representative data, not just by reading the SQL).

---

## Phase 4 — `DataSeeder.cs` — replace both reconcilers with one

Delete `ReconcileCorePlansAsync` and `RetireOrphanedNamedPlansAsync` in full,
delete the now-unused `CanonicalPlanNames` array and the now-unused
`PremiumYearlyPlanId` constant (the tier is one row now — rename
`PremiumMonthlyPlanId` to `PremiumPlanId` throughout `DataSeeder.cs`, since
"Monthly" no longer describes anything about the row itself). Replace with:

```csharp
internal static readonly Guid StarterPlanId = new("aaaa0001-0000-0000-0000-000000000000");
internal static readonly Guid GrowthPlanId  = new("aaaa0002-0000-0000-0000-000000000000");
internal static readonly Guid ProPlanId     = new("aaaa0003-0000-0000-0000-000000000000");
internal static readonly Guid PremiumPlanId = new("aaaa0004-0000-0000-0000-000000000000");
internal static readonly Guid FreePlanId    = new("aaaa0006-0000-0000-0000-000000000000");

private sealed record TierPrice(BillingInterval Interval, decimal Price);

private sealed record CoreTier(
    Guid Id, string Name, int YearlyDiscountPercent, bool AllowBrandingRemoval,
    bool AllowApiAccess, bool PrioritySupport,
    int? MaxArtists, int? MaxAppointmentsPerMonth, int? MaxNotificationsPerMonth,
    int? MaxStorageGb, int? MaxLocations, TierPrice[] Prices);

// ─── Core tiers (always reconciled) ─────────────────────────────────────────
//
// Replaces ReconcileCorePlansAsync + RetireOrphanedNamedPlansAsync (see
// architecture.md Decisions Log — "Plan/PlanPrice split"). Keyed on tier Name, not a
// fixed Plan.Id list, and on (PlanId, Interval) for prices — a reconciler with this
// shape cannot produce the "orphan row under an unrecognized Id" bug class the prior
// two fixes had to clean up after, by construction: there is nowhere for a second row
// with the same Name to hide.
internal static async Task ReconcileCoreTiersAsync(IAppDbContext db)
{
    CoreTier[] tiers =
    [
        new CoreTier(FreePlanId, "Free", 0, false, false, false,
            1, 15, 50, 1, 1,
            [new TierPrice(BillingInterval.Monthly, 0m)]),
        new CoreTier(StarterPlanId, "Starter", 17, false, false, false,
            1, 40, 150, 2, 1,
            [new TierPrice(BillingInterval.Monthly, 29m)]),
        new CoreTier(GrowthPlanId, "Growth", 17, true, false, false,
            3, 150, 600, 10, 1,
            [new TierPrice(BillingInterval.Monthly, 59m)]),
        new CoreTier(PremiumPlanId, "Premium", 17, true, false, true,
            6, 400, 1200, 25, 2,
            [new TierPrice(BillingInterval.Monthly, 79m), new TierPrice(BillingInterval.Yearly, 790m)]),
        new CoreTier(ProPlanId, "Pro", 17, true, true, true,
            10, 1000, 2500, 50, 10,
            [new TierPrice(BillingInterval.Monthly, 99m)]),
    ];

    foreach (CoreTier tier in tiers)
    {
        Plan? plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == tier.Id);
        if (plan is null)
        {
            plan = new Plan { Id = tier.Id };
            db.Plans.Add(plan);
        }

        plan.Name                     = tier.Name;
        plan.YearlyDiscountPercent     = tier.YearlyDiscountPercent;
        plan.AllowBrandingRemoval      = tier.AllowBrandingRemoval;
        plan.AllowApiAccess            = tier.AllowApiAccess;
        plan.PrioritySupport           = tier.PrioritySupport;
        plan.MaxArtists                = tier.MaxArtists;
        plan.MaxAppointmentsPerMonth   = tier.MaxAppointmentsPerMonth;
        plan.MaxNotificationsPerMonth  = tier.MaxNotificationsPerMonth;
        plan.MaxStorageGb              = tier.MaxStorageGb;
        plan.MaxLocations              = tier.MaxLocations;

        foreach (TierPrice tp in tier.Prices)
        {
            PlanPrice? price = await db.PlanPrices
                .FirstOrDefaultAsync(pp => pp.PlanId == tier.Id && pp.Interval == tp.Interval);

            if (price is null)
            {
                db.PlanPrices.Add(new PlanPrice
                {
                    PlanId   = tier.Id,
                    Interval = tp.Interval,
                    Price    = tp.Price,
                    // StripePriceId intentionally left null — populated by
                    // StripeDemoSeeder or an issuer, never reconciled here (matches
                    // the established precedent from the pre-PlanPrice reconciler).
                });
            }
            else
            {
                price.Price = tp.Price; // reconcile price only — StripePriceId untouched
            }
        }
    }

    await db.SaveChangesAsync();
}
```

Update `SeedAsync()`:

```csharp
// Before (the two-call sequence from the prior two fixes):
await ReconcileCorePlansAsync(db);
await RetireOrphanedNamedPlansAsync(db, logger);

// After:
await ReconcileCoreTiersAsync(db);
```

`ILogger logger` is no longer needed by the plan-reconciliation step — check whether
anything else in `SeedAsync` still needs it before removing the
`GetRequiredService<ILoggerFactory>()` line; if nothing else uses it, remove that
line too (dead code).

**`SeedStudiosAndSubscriptionsAsync`** (further down in `DataSeeder.cs`) sets
`Subscription.PlanId` for the two demo studios (`GrowthPlanId`, `StarterPlanId`) —
add `BillingInterval = BillingInterval.Monthly` explicitly to both, even though it's
also the column default, for the same reason every other field in that method is
explicit rather than relying on defaults (matches the file's existing style).

---

## Phase 5 — Contracts

```csharp
// New: Pena_e_Arte.Contracts/Responses/PlanPriceResponse.cs
namespace Pena_e_Arte.Contracts.Responses;

public record PlanPriceResponse(
    Guid    Id,
    string  Interval,
    decimal Price,
    string? StripePriceId,
    bool    IsActive);

// New: Pena_e_Arte.Contracts/Requests/PlanPriceRequest.cs
namespace Pena_e_Arte.Contracts.Requests;

public record PlanPriceRequest(
    string  Interval,
    decimal Price,
    string? StripePriceId = null,
    bool    IsActive      = true);
```

```csharp
// PlanResponse.cs — remove BillingInterval/PriceMonthly/PriceYearly/
// StripePriceIdMonthly/StripePriceIdYearly/PairedPlanId, add Prices:
public record PlanResponse(
    Guid    Id,
    string  Name,
    int     YearlyDiscountPercent,
    bool    AllowBrandingRemoval,
    int     SubscriberCount,
    int?    MaxArtists,
    int?    MaxAppointmentsPerMonth,
    int?    MaxNotificationsPerMonth,
    int?    MaxStorageGb,
    int?    MaxLocations,
    bool    AllowApiAccess,
    bool    PrioritySupport,
    List<PlanPriceResponse> Prices);
```

```csharp
// CreatePlanRequest.cs / UpdatePlanRequest.cs — remove BillingInterval/PriceMonthly/
// PriceYearly/StripePriceIdMonthly/StripePriceIdYearly/PairedPlanId, add Prices:
public record CreatePlanRequest(
    string  Name,
    int     YearlyDiscountPercent,
    List<PlanPriceRequest> Prices,
    int?    MaxArtists               = null,
    int?    MaxAppointmentsPerMonth  = null,
    int?    MaxNotificationsPerMonth = null,
    int?    MaxStorageGb             = null,
    int?    MaxLocations             = null,
    bool    AllowApiAccess           = false,
    bool    PrioritySupport          = false,
    bool    AllowBrandingRemoval     = false);

public record UpdatePlanRequest(
    string  Name,
    int     YearlyDiscountPercent,
    List<PlanPriceRequest> Prices,
    bool    AllowBrandingRemoval     = false,
    int?    MaxArtists               = null,
    int?    MaxAppointmentsPerMonth  = null,
    int?    MaxNotificationsPerMonth = null,
    int?    MaxStorageGb             = null,
    int?    MaxLocations             = null,
    bool    AllowApiAccess           = false,
    bool    PrioritySupport          = false);
```

```csharp
// CreateCheckoutRequest.cs — add BillingInterval:
public record CreateCheckoutRequest(Guid PlanId, string BillingInterval, string SuccessUrl, string CancelUrl);

// ChangePlanRequest.cs — add BillingInterval:
public record ChangePlanRequest(Guid PlanId, string BillingInterval);

// SubscriptionResponse.cs — add BillingInterval + PendingBillingInterval:
public record SubscriptionResponse(
    Guid      Id,
    Guid      StudioId,
    Guid?     PlanId,
    string    BillingInterval,
    Guid?     PendingPlanId,
    string?   PendingBillingInterval,
    string    Status,
    DateTime? TrialExpiresAt,
    DateTime  CurrentPeriodEnd,
    DateTime  GracePeriodEnd,
    string?   StripeSubscriptionId);
```

Update every `CreateSubscriptionHandler.Map(...)` / `CreatePlanHandler.Map(...)` call
site (both are the internal mapping helpers reused across several handlers — grep for
`CreateSubscriptionHandler.Map(` and `CreatePlanHandler.Map(` to find every caller)
to match the new record shapes.

---

## Phase 6 — Application layer: pricing resolution

### 6a. `ChangePlanCommand.cs` — full rewrite (the trickiest one)

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Billing.Commands;

public record ChangePlanCommand(ChangePlanRequest Request) : IRequest<SubscriptionResponse>;

public class ChangePlanHandler(
    IAppDbContext              db,
    ICurrentTenant             tenant,
    IStripeBillingService      billing,
    ILogger<ChangePlanHandler> logger)
    : IRequestHandler<ChangePlanCommand, SubscriptionResponse>
{
    public async Task<SubscriptionResponse> Handle(ChangePlanCommand command, CancellationToken ct)
    {
        Subscription subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.StudioId == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Subscription), tenant.StudioId);

        if (subscription.Status != SubscriptionStatus.Active)
            throw new BusinessRuleViolationException(
                "Plan changes require an active subscription. Use the subscribe flow instead.");
        if (subscription.StripeSubscriptionId is null)
            throw new BusinessRuleViolationException(
                "This subscription is billed outside Stripe. Contact the platform to change plans.");
        if (subscription.PendingPlanId is not null)
            throw new BusinessRuleViolationException(
                "A plan change is already scheduled. Cancel it before choosing another plan.");

        BillingInterval requestedInterval =
            Enum.Parse<BillingInterval>(command.Request.BillingInterval, ignoreCase: true);

        if (subscription.PlanId == command.Request.PlanId && subscription.BillingInterval == requestedInterval)
            throw new BusinessRuleViolationException("The studio is already on this plan.");

        PlanPrice newPrice = await db.PlanPrices
            .FirstOrDefaultAsync(pp =>
                pp.PlanId == command.Request.PlanId && pp.Interval == requestedInterval && pp.IsActive, ct)
            ?? throw new BusinessRuleViolationException(
                "The selected plan is not available at that billing interval. Contact the platform.");

        PlanPrice currentPrice = await db.PlanPrices
            .FirstOrDefaultAsync(pp => pp.PlanId == subscription.PlanId && pp.Interval == subscription.BillingInterval, ct)
            ?? throw new BusinessRuleViolationException(
                "The current plan's pricing could not be determined. Contact the platform.");

        if (newPrice.StripePriceId is null || currentPrice.StripePriceId is null)
            throw new BusinessRuleViolationException(
                "The selected plan is not available for online billing. Contact the platform.");

        if (MonthlyEquivalent(newPrice) > MonthlyEquivalent(currentPrice))
        {
            // Upgrade — switch now, charge the prorated difference immediately
            DateTime periodEnd = await billing.ChangeSubscriptionPriceAsync(
                subscription.StripeSubscriptionId, newPrice.StripePriceId, ct);

            subscription.PlanId           = command.Request.PlanId;
            subscription.BillingInterval  = requestedInterval;
            subscription.CurrentPeriodEnd = periodEnd;

            logger.LogInformation(
                "Plan upgraded immediately for studio {@StudioId} to plan {@PlanId} ({@Interval})",
                subscription.StudioId, command.Request.PlanId, requestedInterval);
        }
        else
        {
            // Downgrade — the studio keeps what it paid for; switch at period end
            string newPriceInterval = requestedInterval == BillingInterval.Monthly ? "month" : "year";
            await billing.ScheduleSubscriptionPriceChangeAsync(
                subscription.StripeSubscriptionId, currentPrice.StripePriceId!, newPrice.StripePriceId, newPriceInterval, ct);

            subscription.PendingPlanId           = command.Request.PlanId;
            subscription.PendingBillingInterval  = requestedInterval;

            logger.LogInformation(
                "Plan downgrade scheduled at period end for studio {@StudioId} to plan {@PlanId} ({@Interval})",
                subscription.StudioId, command.Request.PlanId, requestedInterval);
        }

        await db.SaveChangesAsync(ct);
        return CreateSubscriptionHandler.Map(subscription);
    }

    // Normalise to a per-month cost so monthly and yearly plans compare fairly
    private static decimal MonthlyEquivalent(PlanPrice price) =>
        price.Interval == BillingInterval.Monthly ? price.Price : price.Price / 12m;
}
```

### 6b. `HandleSubscriptionUpdatedCommand.cs` — resolve by `PlanPrice.StripePriceId`

```csharp
if (command.StripePriceId is not null)
{
    PlanPrice? price = await db.PlanPrices
        .FirstOrDefaultAsync(pp => pp.StripePriceId == command.StripePriceId, ct);

    if (price is not null)
    {
        subscription.PlanId          = price.PlanId;
        subscription.BillingInterval = price.Interval;

        // A scheduled change has landed — the pending change is no longer pending
        if (subscription.PendingPlanId == price.PlanId
            && subscription.PendingBillingInterval == price.Interval)
        {
            subscription.PendingPlanId          = null;
            subscription.PendingBillingInterval = null;
        }
    }
}
```

### 6c. `ActivateCheckoutSubscriptionCommand.cs` — same resolution pattern

```csharp
// Before:
Domain.Entities.Plan? plan = result.PriceId is null
    ? null
    : await db.Plans.FirstOrDefaultAsync(
        p => p.StripePriceIdMonthly == result.PriceId || p.StripePriceIdYearly == result.PriceId, ct);
...
if (plan is not null) subscription.PlanId = plan.Id;

// After:
PlanPrice? price = result.PriceId is null
    ? null
    : await db.PlanPrices.FirstOrDefaultAsync(pp => pp.StripePriceId == result.PriceId, ct);
...
if (price is not null)
{
    subscription.PlanId          = price.PlanId;
    subscription.BillingInterval = price.Interval;
}
```

### 6d. `CreateSubscriptionCommand.cs` — resolve requested `PlanPrice`, set interval

`CreateSubscriptionRequest` needs a `BillingInterval` field too (it's the "activate a
Free plan, or subscribe directly with no checkout" path — check `CreateSubscriptionRequest.cs`,
which the spec didn't list; add `BillingInterval` there the same way as
`CreateCheckoutRequest`). Then:

```csharp
// Before:
string? priceId = plan.BillingInterval == BillingInterval.Monthly
    ? plan.StripePriceIdMonthly
    : plan.StripePriceIdYearly;
...
if (plan.PriceMonthly > 0 && ...)   // Free-plan check
...
periodEnd = plan.PriceMonthly == 0 ? ... : ...;
...
subscription.PlanId = command.Request.PlanId;

// After:
BillingInterval requestedInterval =
    Enum.Parse<BillingInterval>(command.Request.BillingInterval, ignoreCase: true);

PlanPrice price = await db.PlanPrices
    .FirstOrDefaultAsync(pp => pp.PlanId == plan.Id && pp.Interval == requestedInterval && pp.IsActive, ct)
    ?? throw new BusinessRuleViolationException(
        "This plan is not available at that billing interval. Please contact the platform.");

string? priceId = price.StripePriceId;
...
if (price.Price > 0 && ...)   // Free-plan check — was plan.PriceMonthly > 0
...
periodEnd = price.Price == 0 ? DateTime.UtcNow.AddYears(50) : DateTime.UtcNow.AddMonths(1);
...
subscription.PlanId          = command.Request.PlanId;
subscription.BillingInterval = requestedInterval;
```

Note the Free-plan check moves from `plan.PriceMonthly > 0` to `price.Price > 0` —
Free only ever has a Monthly `PlanPrice` row anyway (Phase 4), so this is exact, not
an approximation.

### 6e. `CreateSubscriptionCheckoutCommand.cs` — same pattern as 6d for the Checkout path

```csharp
// Before:
string? priceId = plan.BillingInterval == BillingInterval.Monthly
    ? plan.StripePriceIdMonthly
    : plan.StripePriceIdYearly;

// After:
BillingInterval requestedInterval =
    Enum.Parse<BillingInterval>(req.BillingInterval, ignoreCase: true);

PlanPrice? price = await db.PlanPrices
    .FirstOrDefaultAsync(pp => pp.PlanId == plan.Id && pp.Interval == requestedInterval && pp.IsActive, ct);

string? priceId = price?.StripePriceId;
```

Add `RuleFor(x => x.Request.BillingInterval).NotEmpty().Must(v => Enum.TryParse<BillingInterval>(v, true, out _))`
to `CreateSubscriptionCheckoutValidator`, matching the existing pattern in
`CreatePlanValidator` for its old `BillingInterval` field.

### 6f. `ActivateSubscriptionManuallyCommand.cs` — default the new field

```csharp
// In both the "new subscription" and "existing subscription" branches, add:
Status           = SubscriptionStatus.Active,
BillingInterval  = BillingInterval.Monthly,   // cash-billed studios are always Monthly-equivalent
CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1),
```

### 6g. `CreatePlanCommand.cs` / `UpdatePlanCommand.cs` — create/upsert `PlanPrice` children

```csharp
// CreatePlanHandler.Handle — replace the flat-field Plan construction with:
Plan plan = new()
{
    Name                     = req.Name,
    YearlyDiscountPercent    = req.YearlyDiscountPercent,
    AllowBrandingRemoval     = req.AllowBrandingRemoval,
    MaxArtists               = req.MaxArtists,
    MaxAppointmentsPerMonth  = req.MaxAppointmentsPerMonth,
    MaxNotificationsPerMonth = req.MaxNotificationsPerMonth,
    MaxStorageGb             = req.MaxStorageGb,
    MaxLocations             = req.MaxLocations,
    AllowApiAccess           = req.AllowApiAccess,
    PrioritySupport          = req.PrioritySupport,
};
foreach (PlanPriceRequest pr in req.Prices)
{
    plan.Prices.Add(new PlanPrice
    {
        Interval      = Enum.Parse<BillingInterval>(pr.Interval, ignoreCase: true),
        Price         = pr.Price,
        StripePriceId = pr.StripePriceId,
        IsActive      = pr.IsActive,
    });
}
db.Plans.Add(plan);
await db.SaveChangesAsync(ct);
return Map(plan, subscriberCount: 0);

// Map(...) needs rewriting to build the Prices response list from plan.Prices —
// straightforward projection, no business logic.
```

```csharp
// UpdatePlanHandler.Handle — remove the entire PairedPlanId block (lines ~25-33 and
// ~53-74 of the current file) and replace the flat-field writes with an upsert over
// req.Prices, keyed by Interval, mirroring ReconcileCoreTiersAsync's own upsert
// pattern (Phase 4) — same shape, same reasoning:
plan.Name                     = req.Name;
plan.YearlyDiscountPercent    = req.YearlyDiscountPercent;
plan.AllowBrandingRemoval     = req.AllowBrandingRemoval;
plan.MaxArtists               = req.MaxArtists;
plan.MaxAppointmentsPerMonth  = req.MaxAppointmentsPerMonth;
plan.MaxNotificationsPerMonth = req.MaxNotificationsPerMonth;
plan.MaxStorageGb             = req.MaxStorageGb;
plan.MaxLocations             = req.MaxLocations;
plan.AllowApiAccess           = req.AllowApiAccess;
plan.PrioritySupport          = req.PrioritySupport;

List<PlanPrice> existingPrices = await db.PlanPrices.Where(pp => pp.PlanId == plan.Id).ToListAsync(ct);
foreach (PlanPriceRequest pr in req.Request.Prices)
{
    BillingInterval interval = Enum.Parse<BillingInterval>(pr.Interval, ignoreCase: true);
    PlanPrice? existing = existingPrices.FirstOrDefault(pp => pp.Interval == interval);
    if (existing is not null)
    {
        existing.Price         = pr.Price;
        existing.StripePriceId = pr.StripePriceId;
        existing.IsActive      = pr.IsActive;
    }
    else
    {
        db.PlanPrices.Add(new PlanPrice
        {
            PlanId = plan.Id, Interval = interval, Price = pr.Price,
            StripePriceId = pr.StripePriceId, IsActive = pr.IsActive,
        });
    }
}
// A price interval present on the existing plan but NOT in the request is removed —
// this is how an issuer turns an interval off from the editor (distinct from
// IsActive = false, which keeps the row but hides it from checkout; removing it
// entirely means "this tier never offered this interval").
foreach (PlanPrice stale in existingPrices.Where(ep => req.Request.Prices.All(pr =>
    Enum.Parse<BillingInterval>(pr.Interval, true) != ep.Interval)))
{
    db.PlanPrices.Remove(stale);
}

await db.SaveChangesAsync(ct);
```

Update `CreatePlanValidator`/`UpdatePlanValidator` (both in the same files): remove
the old `BillingInterval`/price/Stripe-ID rules, add validation on `req.Prices`:
non-empty, each interval parseable, no duplicate intervals within one request, price
`>= 0`, and the existing "fully free or fully paid" rule now needs to apply
per-`PlanPrice` (`Price == 0` for every row, or `> 0` for every row — not mixed).

### 6h. `DeletePlanCommand.cs` — remove the `PairedPlanId` block

```csharp
// Remove entirely (no longer meaningful — a tier is one row now):
Domain.Entities.Plan? paired = await db.Plans
    .FirstOrDefaultAsync(p => p.PairedPlanId == command.PlanId, ct);
if (paired is not null)
    paired.PairedPlanId = null;
```

The subscriber-count check above it (`db.Subscriptions.AnyAsync(s => s.PlanId ==
command.PlanId)`) is unchanged and still correct. `PlanPrice` children cascade-delete
automatically (configured in Phase 2a) — no explicit cleanup needed.

### 6i. `GetPlansQuery.cs` — return `Prices`

```csharp
return await db.Plans
    .Include(p => p.Prices)
    .OrderBy(p => p.Prices.Min(pp => pp.Price))   // cheapest-first, replaces OrderBy(p => p.PriceMonthly)
    .Select(p => new PlanResponse(
        p.Id, p.Name, p.YearlyDiscountPercent, p.AllowBrandingRemoval,
        db.Subscriptions.Count(s => s.PlanId == p.Id),
        p.MaxArtists, p.MaxAppointmentsPerMonth, p.MaxNotificationsPerMonth,
        p.MaxStorageGb, p.MaxLocations, p.AllowApiAccess, p.PrioritySupport,
        p.Prices.Select(pp => new PlanPriceResponse(
            pp.Id, pp.Interval.ToString(), pp.Price, pp.StripePriceId, pp.IsActive)).ToList()))
    .ToListAsync(ct);
```

### 6j. `GetPlatformStatsQuery.cs` / `GetMrrHistoryQuery.cs` — fix the real MRR bug

Both need `.Include(s => s.Plan).ThenInclude(p => p.Prices)` (or an equivalent
`PlanPrices` join/dictionary lookup) and to replace every
`s.Subscription.Plan.PriceMonthly` / `s.Plan.PriceMonthly` with the monthly-
equivalent of the `PlanPrice` matching `(s.Subscription.PlanId,
s.Subscription.BillingInterval)`:

```csharp
// Helper (add to both files, or extract to a shared static helper if you prefer —
// check whether a shared Billing helpers class already exists before adding a new one):
private static decimal MonthlyEquivalentRevenue(Subscription s) =>
    s.Plan?.Prices.FirstOrDefault(pp => pp.Interval == s.BillingInterval) is PlanPrice pp
        ? (pp.Interval == BillingInterval.Monthly ? pp.Price : pp.Price / 12m)
        : 0m;
```

Replace both `Sum(s => s.Subscription!.Plan!.PriceMonthly)` calls in
`GetPlatformStatsQuery.cs` and the `Sum(s => s.Plan!.PriceMonthly)` call in
`GetMrrHistoryQuery.cs` with `Sum(MonthlyEquivalentRevenue)` (adjusting for whichever
variable name each file uses for the subscription). This is the fix for the
confirmed pre-existing revenue-reporting bug — call it out as such in the PR/commit
message, not just as incidental architecture cleanup.

---

## Phase 7 — `StripeDemoSeeder.cs`

```csharp
// Before (step 1 — provisions Monthly AND Yearly for every plan unconditionally):
List<Plan> plans = await db.Plans.ToListAsync();
foreach (Plan plan in plans)
{
    plan.StripePriceIdMonthly = await EnsurePriceAsync(
        prices, plan, "month", plan.PriceMonthly, plan.StripePriceIdMonthly);
    plan.StripePriceIdYearly = await EnsurePriceAsync(
        prices, plan, "year", plan.PriceYearly, plan.StripePriceIdYearly);
}
await db.SaveChangesAsync();

// After — provisions a Stripe price only for intervals that actually have a
// PlanPrice row (so Starter/Growth/Pro correctly get Monthly only, matching Phase 4's
// migration decision not to fabricate a Yearly price for tiers that never had one):
List<PlanPrice> planPrices = await db.PlanPrices.Include(pp => pp.Plan).ToListAsync();
foreach (PlanPrice pp in planPrices)
{
    string interval = pp.Interval == BillingInterval.Monthly ? "month" : "year";
    pp.StripePriceId = await EnsurePriceAsync(prices, pp.Plan, interval, pp.Price, pp.StripePriceId);
}
await db.SaveChangesAsync();
```

`EnsurePriceAsync`'s signature takes a `Plan plan` for `plan.Name`/`plan.Id` in the
product name and metadata — change its parameter to accept `Plan plan` still (via
`pp.Plan`) or add a `PlanPrice` overload; either is fine, keep the metadata
(`plan_id`, `interval`) exactly as today since that's what the Stripe-side
`SearchAsync` fallback query matches against.

Further down, where the demo subscription is created:

```csharp
// Before:
Plan? currentPlan = plans.FirstOrDefault(p => p.Id == sub.PlanId) ?? plans.FirstOrDefault();
if (currentPlan?.StripePriceIdMonthly is null) { ... }
...
Items = new List<Stripe.SubscriptionItemOptions> { new() { Price = currentPlan.StripePriceIdMonthly } },
...
sub.PlanId = currentPlan.Id;

// After:
PlanPrice? currentPrice = planPrices.FirstOrDefault(pp => pp.PlanId == sub.PlanId && pp.Interval == BillingInterval.Monthly)
    ?? planPrices.FirstOrDefault(pp => pp.Interval == BillingInterval.Monthly);
if (currentPrice?.StripePriceId is null) { logger.LogWarning(...); return; }
...
Items = new List<Stripe.SubscriptionItemOptions> { new() { Price = currentPrice.StripePriceId } },
...
sub.PlanId          = currentPrice.PlanId;
sub.BillingInterval = BillingInterval.Monthly;
```

---

## Phase 8 — Migration B (drop legacy `Plan` columns)

Only write and apply this after Phases 1–7 are fully implemented and **Phase 9's
quality gates are green** — this is the literal "don't combine with the additive
step" instruction from the spec, expressed as a second migration file rather than a
second calendar day (see "Design decisions" above for why that's the right call
here).

Generate with `dotnet ef migrations add DropLegacyPlanBillingFields --project
Pena_e_Arte.Infrastructure`. This should now cleanly produce exactly the
`DropColumn`/`DropIndex` calls that were deliberately withheld from Migration A:
`plans.BillingInterval`, `plans.PriceMonthly`, `plans.PriceYearly`,
`plans.StripePriceIdMonthly`, `plans.StripePriceIdYearly`, `plans.PairedPlanId`, and
`ix_plans_paired_plan_id`. Review the generated file — it should contain nothing
else. Apply it (`dotnet ef database update` locally, or let the existing
`migDb.Database.MigrateAsync()` startup call pick it up) only after confirming the
full test suite is green with Migration A alone.

---

## Phase 9 — Tests

This touches a lot of test files. Work through each; some need small edits, some
need full rewrites, a few are new.

**Rewrite entirely** (old assertions reference removed fields):
- `tests/Pena_e_Arte.UnitTests/Infrastructure/DataSeederPlanReconciliationTests.cs` —
  replace every test with equivalents against `ReconcileCoreTiersAsync`: empty-DB
  insert (5 plans, 6 `PlanPrice` rows), existing-tier-missing-limits backfill,
  existing-`PlanPrice`-price-drift correction (reconciled), `StripePriceId` NOT
  overwritten on an existing `PlanPrice` row, idempotency (called twice → no
  duplicate `PlanPrice` rows, no duplicate `Plan` rows), a differently-named custom
  plan left untouched. Drop every orphan/`PairedPlanId`-specific test — structurally
  impossible now, nothing to test.
- `tests/Pena_e_Arte.UnitTests/Billing/CreatePlanHandlerTests.cs`,
  `UpdatePlanHandlerTests.cs` — rewrite around `Prices: [...]` request shape; keep
  the same scenarios (valid create, persists, returns new id, Stripe IDs round-trip,
  limit fields, no-limit-fields-means-null) translated to the new shape. Delete the
  `PairedPlanId`-specific tests (`Handle_SelfPairedPlanId_...`,
  `Handle_PairedPlanIdPointsToNonexistentPlan_...`,
  `Handle_PairedPlanId_PropagatesLimitFieldsToPairedPlan_...`) — add a replacement
  test confirming `UpdatePlanHandler` correctly upserts/removes `PlanPrice` rows
  (add a Yearly price to a Monthly-only plan; remove a price the request no longer
  includes).
- `tests/Pena_e_Arte.UnitTests/Billing/ChangePlanHandlerTests.cs` — rewrite the
  upgrade/downgrade scenarios around `(PlanId, BillingInterval)` pairs instead of a
  single locked-interval `Plan`; add a same-tier-different-interval case ("Premium
  Monthly → Premium Yearly changes only BillingInterval, PlanId stays the same" —
  this is spec Section 8's explicit verification item, make sure it's a real test,
  not just a manual check).
- `tests/Pena_e_Arte.UnitTests/Billing/HandleSubscriptionUpdatedHandlerTests.cs`,
  `ActivateCheckoutSubscriptionHandlerTests.cs`, `CreateSubscriptionHandlerTests.cs`,
  `CreateSubscriptionCheckoutHandlerTests.cs` — update fixtures to seed `PlanPrice`
  rows instead of flat fields; confirm `BillingInterval` is set correctly on the
  resulting `Subscription` in each.
- `tests/Pena_e_Arte.UnitTests/Billing/GetPlansHandlerTests.cs` — update expected
  response shape to the new `Prices` array.
- `tests/Pena_e_Arte.UnitTests/Platform/GetPlatformStatsHandlerTests.cs` — add a
  case proving the MRR bug fix: a subscription on a Yearly `PlanPrice` (e.g. €790)
  contributes €65.83 to MRR, not €79 — this is the regression test for the
  confirmed pre-existing bug, don't skip it.
- `tests/Pena_e_Arte.IntegrationTests/Application/PlatformStatsIntegrationTests.cs`
  — uses `DatabaseFixture`/`EnsureCreatedAsync`, so it builds schema from the
  current model automatically; just update whatever fixture data it seeds to the
  new shape.

**New tests to add:**
- `GetMrrHistoryHandlerTests.cs` (no existing test file found — add one), same
  Yearly-monthly-equivalent regression case as `GetPlatformStatsHandlerTests`.
- A `PlanPriceConfiguration`/`PlanConfiguration` sanity test isn't necessary — the
  unique index behavior is better proven via `ReconcileCoreTiersAsync`'s own
  idempotency test above (attempting a second insert for the same
  `(PlanId, Interval)` would violate the unique constraint if the upsert logic were
  wrong).

**Frontend:**
- `SubscribePage.test.tsx`, `PlanManagementPage.test.tsx` — update MSW fixtures to
  the new `PlanResponse` shape (`prices: [...]` instead of flat fields); add cases
  for: toggling to Yearly still shows a tier with no Yearly `PlanPrice` (disabled,
  not missing — spec Section 8's explicit checklist item), the same-tier
  interval-only switch, and the "Prices" section toggle in `PlanManagementPage`'s
  form correctly omitting a `PlanPriceRequest` for a disabled interval.
- If `BillingPage.test.tsx` exists, add a case confirming a yearly-billed
  subscription's display no longer says "/ month".

---

## Phase 10 — Frontend

### 10a. `billing.types.ts`

```typescript
export interface PlanPriceResponse {
  id:            string;
  interval:      "Monthly" | "Yearly";
  price:         number;
  stripePriceId: string | null;
  isActive:      boolean;
}

export interface PlanResponse {
  id:                       string;
  name:                     string;
  yearlyDiscountPercent:    number;
  allowBrandingRemoval:     boolean;
  subscriberCount:          number;
  maxArtists:               number | null;
  maxAppointmentsPerMonth:  number | null;
  maxNotificationsPerMonth: number | null;
  maxStorageGb:             number | null;
  maxLocations:             number | null;
  allowApiAccess:           boolean;
  prioritySupport:          boolean;
  prices:                   PlanPriceResponse[];
}

export interface SubscriptionResponse {
  id:                     string;
  studioId:               string;
  planId:                 string | null;
  billingInterval:        "Monthly" | "Yearly";
  pendingPlanId:          string | null;
  pendingBillingInterval: "Monthly" | "Yearly" | null;
  status:                 "Trialing" | "Active" | "PastDue" | "Cancelled" | "GracePeriod";
  trialExpiresAt:         string | null;
  currentPeriodEnd:       string;
  gracePeriodEnd:         string;
  stripeSubscriptionId:   string | null;
}

export interface CreateSubscriptionRequest {
  planId:          string;
  billingInterval: "Monthly" | "Yearly";
}
```

Add a small helper both `SubscribePage.tsx` and `BillingPage.tsx` will need:

```typescript
export function priceFor(plan: PlanResponse, interval: "Monthly" | "Yearly"): PlanPriceResponse | undefined {
  return plan.prices.find((p) => p.interval === interval && p.isActive);
}
```

### 10b. `billingApi.ts`

```typescript
export interface PlanPriceRequest {
  interval:       string;
  price:          number;
  stripePriceId?: string | null;
  isActive?:      boolean;
}

export interface CreatePlanRequest {
  name:                     string;
  yearlyDiscountPercent:    number;
  prices:                   PlanPriceRequest[];
  maxArtists?:              number | null;
  maxAppointmentsPerMonth?: number | null;
  maxNotificationsPerMonth?: number | null;
  maxStorageGb?:            number | null;
  maxLocations?:            number | null;
  allowApiAccess?:          boolean;
  prioritySupport?:         boolean;
  allowBrandingRemoval?:    boolean;
}

export interface UpdatePlanRequest {
  name:                     string;
  yearlyDiscountPercent:    number;
  prices:                   PlanPriceRequest[];
  allowBrandingRemoval:     boolean;
  maxArtists?:              number | null;
  maxAppointmentsPerMonth?: number | null;
  maxNotificationsPerMonth?: number | null;
  maxStorageGb?:            number | null;
  maxLocations?:            number | null;
  allowApiAccess?:          boolean;
  prioritySupport?:         boolean;
}

// createCheckout and changePlan mutations both need billingInterval added to their body type:
createCheckout: builder.mutation<
  { url: string },
  { planId: string; billingInterval: string; successUrl: string; cancelUrl: string }
>({ ... }),

changePlan: builder.mutation<SubscriptionResponse, { planId: string; billingInterval: string }>({ ... }),
```

### 10c. `SubscribePage.tsx` — the core rewrite

Replace the `filteredPlans = plans.filter(p => p.billingInterval === billingCycle)`
line and every other place `plan.billingInterval`/`plan.priceMonthly`/
`plan.priceYearly` is read (the `PlanCard` component, `yearlyDiscount` lookup,
`isFreePlanSelected`, `currentSubPlan`/`isFreePlanActive`, and the `onSubscribe`
payloads) — this component conflates tier and interval throughout, so treat it as a
full pass, not a single line-fix:

```tsx
// yearlyDiscount: was plans.find(p => p.billingInterval === "Yearly")?.yearlyDiscountPercent
// Now every plan carries its own YearlyDiscountPercent regardless of which intervals
// it offers — use the currently-selected tier once one is picked, or the first plan
// that HAS a Yearly price otherwise (keeps the toggle's "Save X%" badge meaningful
// even before a tier is selected):
const yearlyDiscount =
  (selectedPlanId ? plans.find((p) => p.id === selectedPlanId) : undefined)?.yearlyDiscountPercent
  ?? plans.find((p) => priceFor(p, "Yearly"))?.yearlyDiscountPercent
  ?? 0;

// filteredPlans: DO NOT filter tiers out — every tier stays visible in both toggle
// states (spec Section 5's explicit requirement: "do not silently drop the tier").
// Instead, pair each plan with the price for the current cycle, which may be undefined.
const plansWithPrice = plans.map((p) => ({ plan: p, price: priceFor(p, billingCycle) }));

// PlanCard now takes `price: PlanPriceResponse | undefined` instead of reading
// plan.billingInterval directly. When price is undefined, render the card disabled
// with "Not available yearly yet" (or the Monthly equivalent) instead of omitting it.
```

```tsx
function PlanCard({
  plan, price, selected, onSelect, disabled, isCurrent = false,
}: {
  plan:       PlanResponse;
  price:      PlanPriceResponse | undefined;
  selected:   boolean;
  onSelect:   () => void;
  disabled:   boolean;
  isCurrent?: boolean;
}) {
  const unavailable = price === undefined;
  return (
    <button
      type="button"
      onClick={onSelect}
      disabled={disabled || isCurrent || unavailable}
      className={cn(/* add unavailable to the disabled-style branch */)}
    >
      {/* ...name/current-plan badge unchanged... */}
      {unavailable ? (
        <p className="text-xs text-muted-foreground">Not available on this billing cycle yet</p>
      ) : price.price === 0 ? (
        <p className="font-semibold text-green-600 dark:text-green-400">Free</p>
      ) : (
        <p className="font-semibold">
          {formatPrice(price.price)}
          <span className="text-xs font-normal text-muted-foreground">/{price.interval === "Yearly" ? "yr" : "mo"}</span>
        </p>
      )}
      {/* per-month-equivalent + "save X%" line: use price.price / 12 for Yearly, same as before */}
    </button>
  );
}
```

`onSubscribe` needs `billingInterval: billingCycle` added to every one of the three
payloads (`activateFree`, `changePlan`, `createCheckout`).
`isCurrent={isCardBilled && plan.id === sub?.planId && billingCycle === sub?.billingInterval}`
— a plan card is "current" only when BOTH tier and interval match now, since
switching interval on the same tier is itself a real, selectable action (spec
Section 6's explicit UX goal).
`currentSubPlan`/`isFreePlanActive`: `(currentSubPlan?.prices.find(p => p.interval === sub?.billingInterval)?.price ?? -1) === 0`.

### 10d. `PlanManagementPage.tsx` — two independent price sections

Remove the single `billingInterval` `<select>` and the single `priceMonthly`/
`priceYearly` field pair from the Zod `schema` and `PlanForm`. Replace with:

```typescript
const priceSectionSchema = z.object({
  enabled:       z.boolean(),
  price:         z.number().min(0).optional(),
  stripePriceId: z.string().max(200).optional().nullable(),
}).refine((v) => !v.enabled || v.price !== undefined, {
  message: "Price is required when this interval is enabled.",
  path: ["price"],
});

const schema = z.object({
  name:                     z.string().min(1, "Name is required").max(100),
  yearlyDiscountPercent:    z.number({ message: "Required" }).min(0).max(100),
  monthly:                  priceSectionSchema,
  yearly:                   priceSectionSchema,
  // ...allowBrandingRemoval, allowApiAccess, prioritySupport, the five Max* fields — unchanged
}).refine((v) => v.monthly.enabled || v.yearly.enabled, {
  message: "At least one billing interval must be enabled.",
  path: ["monthly"],
});
```

Render two clearly-labeled, independently-toggleable sections ("Monthly price",
"Yearly price"), each with an on/off switch, a price input (only enabled/required
when the section's switch is on), and a Stripe Price ID input — matching the spec's
Section 5 instruction exactly. On submit, build `prices: PlanPriceRequest[]` from
whichever sections are enabled (an omitted section = that interval is removed
entirely from the plan, per Phase 6g's `UpdatePlanHandler` behavior). The existing
"suggested yearly price" helper (`watchedMonthly * 12 * (1 - discount/100)`) still
works unchanged — it only needs `monthly.price` and `yearlyDiscountPercent` as
inputs, both of which still exist on the form.

`PlanCard` (the issuer-facing card, not `SubscribePage`'s) needs its price/badge
section rewritten to read from `plan.prices` instead of
`plan.billingInterval`/`plan.priceMonthly`/`plan.priceYearly` — show both configured
prices when both exist (this is where Premium's real dual pricing finally becomes
"real" instead of a "reference only, not charged" decorative line — update or
remove the `title="Reference only — not charged at checkout for this plan"` tooltip,
since after this change the second price IS charged at checkout when a studio picks
that interval).

### 10e. `BillingPage.tsx`

```tsx
// Before:
const isFreePlan = (currentPlan?.priceMonthly ?? -1) === 0;
...
{formatEur(currentPlan.priceMonthly)}
<span className="text-muted-foreground font-normal"> / month</span>
...
Next charge: {formatEur(currentPlan.priceMonthly)} on ...

// After — use the subscription's actual billing interval, not always Monthly:
const currentPrice = currentPlan ? priceFor(currentPlan, sub.billingInterval) : undefined;
const isFreePlan   = (currentPrice?.price ?? -1) === 0;
...
{formatEur(currentPrice?.price ?? 0)}
<span className="text-muted-foreground font-normal"> / {sub.billingInterval === "Yearly" ? "year" : "month"}</span>
...
Next charge: {formatEur(currentPrice?.price ?? 0)} on ...
```

---

## Phase 11 — Quality Gates

```bash
# Backend
dotnet build
dotnet test

# Frontend
pnpm --filter frontend test
pnpm --filter frontend lint
pnpm --filter frontend build   # tsc strict mode will catch any missed PlanResponse/SubscriptionResponse field reference
```

All must be clean before writing Migration B (Phase 8) or considering this done.

---

## Phase 12 — Update the Decisions Log

Append to `docs/claude/architecture.md`'s Decisions Log, after the "Orphaned legacy
plan retirement" entry:

```
| Plan/PlanPrice split | `Plan.BillingInterval`/`PriceMonthly`/`PriceYearly`/`StripePriceIdMonthly`/`StripePriceIdYearly`/`PairedPlanId` removed; new child entity `PlanPrice` (`PlanId`, `Interval`, `Price`, `StripePriceId`, `IsActive`, unique on `(PlanId, Interval)`) holds one row per cadence a tier actually offers. `Subscription` gained `BillingInterval` (required) and `PendingBillingInterval` (nullable, mirrors `PendingPlanId`) — cadence is now the subscription's own property, independent of which `Plan` it's on. `DataSeeder.ReconcileCoreTiersAsync` replaced both `ReconcileCorePlansAsync` and `RetireOrphanedNamedPlansAsync`, keyed on tier `Name` + `(PlanId, Interval)` rather than a fixed `Plan.Id` list. Migration split in two: additive `plan_prices`/`Subscription` columns + data backfill + Premium-row merge shipped together with a later, separate `DropLegacyPlanBillingFields` migration for the six dead `Plan` columns — both written in the same session (no live deploy pipeline exists yet to force a real waiting period between them, per this table's own "Structured-log correlation fields" entry), but kept as two distinct, separately-reviewable migration files rather than one. | Directly supersedes "Plan billing interval stays locked per-row" and "Plan Monthly/Yearly pairing" above — those decisions produced two data-integrity bugs in two consecutive nights (`bug-report-plans-page-data-mismatch.md`, `bug-report-premium-plan-duplicate-legacy-row.md`) because a plan's billing cadence and its identity as a tier were the same database row. Also fixed as a confirmed side effect, not scope creep: `GetPlatformStatsQuery`/`GetMrrHistoryQuery` were computing MRR from `Plan.PriceMonthly` unconditionally, overstating revenue for every yearly-billed subscription (79 vs the real 790/12 = 65.83 monthly-equivalent) — now uses the `PlanPrice` matching the subscription's actual `BillingInterval`. |
```

---

## Forbidden Actions

- Do not apply Migration B before Phase 11's quality gates are fully green.
- Do not fabricate a real Stripe Yearly price for Starter/Growth/Pro in the
  migration backfill — that's an issuer decision made later through
  `PlanManagementPage`, not something to invent as part of this fix.
- Do not let `ReconcileCoreTiersAsync` touch `PlanPrice.StripePriceId` on an
  existing row — insert-only for that field, exactly like its predecessor.
- Do not silently drop a tier from `SubscribePage` for a billing cycle it doesn't
  offer — render it disabled with a reason (spec Section 5's explicit instruction,
  restated because it's the single most visible behavior change to a real user).
- Do not introduce new npm or NuGet packages.
- Do not skip the `GetPlatformStatsHandlerTests`/`GetMrrHistoryHandlerTests`
  Yearly-monthly-equivalent regression test — this is a real, confirmed revenue-
  reporting bug fix, not decoration.

---

## Completion Checklist

Mirrors the spec's own Section 8 verification checklist, plus this prompt's
additions:

- [ ] `PlanPrice` entity + configuration added; `Plan`/`Subscription` updated
- [ ] Migration A applies cleanly against a copy of representative pre-migration data
- [ ] `SELECT Name, COUNT(*) FROM Plans GROUP BY Name HAVING COUNT(*) > 1` returns zero rows after migration
- [ ] Every pre-existing `Subscription` has a non-null `BillingInterval` matching what it was actually being charged (spot-checked, not assumed)
- [ ] `ReconcileCoreTiersAsync` replaces both prior reconcilers; old methods and `CanonicalPlanNames` deleted
- [ ] All 5 pricing-resolution call sites updated (`ChangePlanCommand`, `HandleSubscriptionUpdatedCommand`, `ActivateCheckoutSubscriptionCommand`, `CreateSubscriptionCommand`, `CreateSubscriptionCheckoutCommand`)
- [ ] `ActivateSubscriptionManuallyCommand` defaults `BillingInterval = Monthly`
- [ ] `CreatePlanCommand`/`UpdatePlanCommand`/`DeletePlanCommand` rewritten; `PairedPlanId` sync fully removed
- [ ] `GetPlansQuery` returns `Prices` array
- [ ] `GetPlatformStatsQuery`/`GetMrrHistoryQuery` MRR bug fixed and regression-tested
- [ ] `StripeDemoSeeder` iterates `PlanPrice` rows, provisions only intervals that exist
- [ ] Toggling "Yearly" on `SubscribePage` shows all five tiers, none silently missing
- [ ] Changing Premium Monthly → Premium Yearly changes only `BillingInterval`, `PlanId` unchanged (real test, not manual check)
- [ ] Issuer Plans page shows exactly one card per tier — five total, no duplicates
- [ ] `PlanManagementPage` form has two independent Monthly/Yearly sections, no single Billing Interval dropdown
- [ ] `BillingPage` shows the correct interval label and price for a yearly-billed studio
- [ ] All Phase 9 test files updated/added and passing
- [ ] `dotnet build`, `dotnet test`, `pnpm lint`, `pnpm test`, `pnpm build` all clean
- [ ] Migration B written, reviewed, and applied only after the above is green
- [ ] Decisions Log entry appended to `architecture.md`
