# Overnight Prompt — Retire Orphaned Legacy Premium Plan Row

**Date:** 2026-07-19
**Files changed:** ~2 (1 backend seed file, 1 backend test file — appended to existing files, no new files)
**Type:** Backend data-correctness bug fix — follow-up to the previous night's plan-seed
reconciliation fix. No schema migration, no frontend changes.

---

## Context

This is a same-night follow-up to `bug-report-plans-page-data-mismatch.md` /
`docs/claude/overnight-prompt-plans-seed-reconciliation-2026-07-19.md`. That fix landed
(`ReconcileCorePlansAsync` is live in `DataSeeder.cs` — verified in source, see below)
and correctly repaired Starter/Growth/Pro's null limit fields and Premium's stale
pricing. It also had a side effect its own design couldn't avoid: `bug-report-premium-
plan-duplicate-legacy-row.md` (repo root) reports the issuer Plans page now shows
**three** "Premium" cards instead of two.

Read `bug-report-premium-plan-duplicate-legacy-row.md` in full before starting — it
already contains the correct root-cause analysis. Do not re-derive it from scratch,
and do not treat this as evidence the previous fix was wrong; it wasn't. Summary of
why this happens, confirmed against current source below:

`ReconcileCorePlansAsync` (`DataSeeder.cs`, lines 180–304 as of this fix) upserts
exactly five rows matched **by fixed Id** (`StarterPlanId`, `GrowthPlanId`,
`ProPlanId`, `PremiumMonthlyPlanId`, `PremiumYearlyPlanId`). Before that fix, this
environment's Premium plan existed as **one row under a different, pre-Monthly/Yearly-
split Id** — a leftover from before the "Plan billing interval stays locked per-row"
decision (architecture.md Decisions Log). That row's `Id` isn't in the canonical set,
so the reconciler's `Where(p => canonicalIds.Contains(p.Id))` query never selects it —
it's neither updated nor removed — while `PremiumMonthlyPlanId` and
`PremiumYearlyPlanId` were both missing from the database (Premium had never existed
as two rows before), so both got freshly inserted. Net result: 1 stale orphan + 2
correct new rows = 3 "Premium" cards.

### Additional fact this repo confirmed, not present in the bug report

The bug report's Step 3 says: *"Confirm no other FK ... points at them first — grep
the codebase for `PlanId` foreign keys before assuming `Subscriptions` is the only
table involved."* This has been done already, so it doesn't need to be redone:

```
Pena_e_Arte.Domain/Entities/Subscription.cs
```

`Subscription` has **two** Guid columns that reference `Plan.Id`, not one:

```csharp
public Guid?  PlanId        { get; set; }   // the active plan
public Guid?  PendingPlanId { get; set; }   // a scheduled downgrade/change, not yet effective
```

Confirmed in `Pena_e_Arte.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`:
both `PlanId` (`fk_subscriptions_plans`) and `PendingPlanId`
(`fk_subscriptions_pending_plans`) are real FK constraints with
`DeleteBehavior.Restrict`. `PendingPlanId` is live, used by `ChangePlanCommand`
(sets it when a downgrade is scheduled), `CancelPlanChangeCommand` and
`CancelSubscriptionCommand` (clear it), and `HandleSubscriptionUpdatedCommand`
(clears it once the change lands). **Any fix must reassign both columns, not just
`PlanId`** — a subscription with a scheduled downgrade onto the orphan row would
otherwise still block deletion (or worse, get silently missed) even after `PlanId`
is fixed.

Also confirmed: `Plan.PairedPlanId` is a self-reference with **no FK constraint**
(index only — `ix_plans_paired_plan_id`), and no other entity in the codebase
(`ReferralRedemption`, `Payment`, etc.) has any column referencing `Plan.Id`. So the
complete set of things that can point at a `Plan` row is: `Subscription.PlanId`,
`Subscription.PendingPlanId`, and `Plan.PairedPlanId` (the last one only ever set
between the two Premium rows themselves, by `ReconcileCorePlansAsync` or
`UpdatePlanHandler`).

---

## Phase 0 — Required Reading

```
bug-report-premium-plan-duplicate-legacy-row.md                          (repo root)
bug-report-plans-page-data-mismatch.md                                   (repo root — prior context)
Pena_e_Arte.Infrastructure/Persistence/Seed/DataSeeder.cs                 (SeedAsync, ReconcileCorePlansAsync)
Pena_e_Arte.Infrastructure/Persistence/Seed/StripeDemoSeeder.cs           (ILogger pattern to reuse — see Step 2 below)
Pena_e_Arte.Domain/Entities/Subscription.cs
Pena_e_Arte.Domain/Entities/Plan.cs
Pena_e_Arte.Application/Plans/Commands/DeletePlanCommand.cs              (existing precedent for clearing a sibling's PairedPlanId before removing a Plan row)
Pena_e_Arte.Application/Billing/Commands/ChangePlanCommand.cs             (how PendingPlanId gets set)
tests/Pena_e_Arte.UnitTests/Infrastructure/DataSeederPlanReconciliationTests.cs  (append to this file, don't create a new one)
tests/Pena_e_Arte.UnitTests/Billing/ChangePlanHandlerTests.cs            (reference for constructing a full Subscription in a test)
tests/Pena_e_Arte.UnitTests/Helpers/FakeDbContext.cs
docs/claude/architecture.md — Decisions Log, last entry ("Core plan reconciliation
  replaces one-time plan seed") — this fix adds the next entry right after it
docs/claude/conventions.md
```

---

## The fix

Add one new method to `DataSeeder.cs`, `RetireOrphanedNamedPlansAsync`, called from
`SeedAsync` immediately after `ReconcileCorePlansAsync`. It finds any `Plan` row
named exactly one of the five canonical tier names whose `Id` is **not** one of the
six canonical constants, reassigns every `Subscription` row referencing it (both
`PlanId` and `PendingPlanId`) to the correct canonical replacement, clears any
sibling's `PairedPlanId` that points at it, and deletes it.

**Design choice, stated explicitly so it isn't re-litigated:** the bug report
suggests either "a lightweight check... that flags" a duplicate (detection only) or
implies a manual migration/script (Step 2–3) to actually fix it. This prompt does
both, but as one thing: an **always-run, idempotent retirement** living next to
`ReconcileCorePlansAsync`, not a hand-run SQL script. Reasoning:

- It self-heals every environment (staging, prod, any future issuer instance) the
  moment this code deploys, without anyone needing to remember to run a script per
  environment — which is exactly the class of problem (a guard that only fires once,
  or a fix that only applies to environments someone remembered to patch) that caused
  both this bug and the one before it.
- It's a no-op the instant no orphan remains, so leaving it running on every boot
  indefinitely is safe — same property `ReconcileCorePlansAsync` already has.
- It's unit-testable via `FakeDbContext` (EF InMemory), consistent with how the
  previous fix was tested, instead of living in a raw-SQL migration that the
  existing integration test harness can't exercise (`DatabaseFixture` uses
  `EnsureCreatedAsync`, which builds schema from the current model and never runs
  migrations — confirmed by reading it during the previous fix).

**Known, accepted trade-off — document this, don't silently accept it:** this
targets *any* plan row named exactly "Starter"/"Growth"/"Premium"/"Pro"/"Free" whose
`Id` isn't canonical. It cannot distinguish "genuine pre-split leftover" from "an
issuer manually created a custom plan and happened to name it identically." Given
this environment's own evidence (`DataSeeder`'s demo subscriptions only ever point at
`GrowthPlanId`/`StarterPlanId`, so any subscription on a non-canonical "Premium" row
is real usage, not seed data, and the bug report's own root-cause section already
establishes with high confidence what this specific row is), automatic retirement is
the right call here. Record this trade-off in the Decisions Log (Step 5 below) and
note the mitigation: an issuer who needs a bespoke plan for one studio should name it
distinctly (e.g. "Premium — Studio X Custom"), not one of the five reserved tier
names.

### Step 1 — add the method

Insert this immediately after `ReconcileCorePlansAsync` (after its closing brace,
before the `SeedFreePlanAsync` comment block):

```csharp
private static readonly string[] CanonicalPlanNames =
    ["Free", "Starter", "Growth", "Premium", "Pro"];

// ─── Orphaned legacy plan retirement (always runs, after reconciliation) ───────
//
// ReconcileCorePlansAsync only ever touches the six fixed Ids above. Any environment
// where a canonically-named plan exists under a DIFFERENT Id — e.g. Premium's
// pre-Monthly/Yearly-split row from before the two-row pairing decision — is
// invisible to that method: neither updated nor removed, so its own insert-if-
// missing branch adds a fresh correct row *alongside* the leftover instead of
// replacing it. See bug-report-premium-plan-duplicate-legacy-row.md.
//
// This reassigns every Subscription referencing the orphan — both the active PlanId
// and a scheduled-downgrade PendingPlanId; confirmed via
// AppDbContextModelSnapshot.cs these are the ONLY two FKs anywhere that reference
// Plan.Id, Plan.PairedPlanId is a self-reference with no FK constraint — to the
// correct canonical replacement, clears any sibling's PairedPlanId still pointing at
// the orphan (mirrors DeletePlanHandler's own handling of that case), then deletes
// it. Runs every boot; becomes a no-op once no orphan remains, so — like
// ReconcileCorePlansAsync — it's safe to leave running indefinitely rather than
// requiring a one-time migration per environment.
//
// Accepted trade-off (see architecture.md Decisions Log — "Orphaned legacy plan
// retirement"): this matches ANY plan row named exactly one of the five reserved
// tier names with a non-canonical Id. It cannot distinguish a genuine pre-split
// leftover from an issuer-created custom plan that happens to share the name. An
// issuer needing a bespoke plan should give it a distinct name to avoid this.
internal static async Task RetireOrphanedNamedPlansAsync(IAppDbContext db, ILogger logger)
{
    List<Plan> orphans = await db.Plans
        .Where(p => CanonicalPlanNames.Contains(p.Name)
                 && p.Id != StarterPlanId
                 && p.Id != GrowthPlanId
                 && p.Id != ProPlanId
                 && p.Id != PremiumMonthlyPlanId
                 && p.Id != PremiumYearlyPlanId
                 && p.Id != FreePlanId)
        .ToListAsync();

    if (orphans.Count == 0)
        return;

    foreach (Plan orphan in orphans)
    {
        Guid replacementId = orphan.Name switch
        {
            "Starter" => StarterPlanId,
            "Growth"  => GrowthPlanId,
            "Pro"     => ProPlanId,
            "Free"    => FreePlanId,
            "Premium" => orphan.BillingInterval == BillingInterval.Yearly
                ? PremiumYearlyPlanId
                : PremiumMonthlyPlanId,
            _ => throw new InvalidOperationException(
                $"Unreachable: '{orphan.Name}' is not one of the five canonical plan names."),
        };

        List<Subscription> activeSubs = await db.Subscriptions
            .Where(s => s.PlanId == orphan.Id)
            .ToListAsync();
        foreach (Subscription sub in activeSubs)
            sub.PlanId = replacementId;

        List<Subscription> pendingSubs = await db.Subscriptions
            .Where(s => s.PendingPlanId == orphan.Id)
            .ToListAsync();
        foreach (Subscription sub in pendingSubs)
            sub.PendingPlanId = replacementId;

        // Don't leave a sibling pointing at a row we're about to delete.
        Plan? siblingPointingAtOrphan = await db.Plans
            .FirstOrDefaultAsync(p => p.PairedPlanId == orphan.Id);
        if (siblingPointingAtOrphan is not null)
            siblingPointingAtOrphan.PairedPlanId = null;

        db.Plans.Remove(orphan);

        logger.LogWarning(
            "Retired orphaned legacy plan {OrphanPlanId} ({PlanName}, {BillingInterval}) — " +
            "reassigned {ActiveCount} active and {PendingCount} pending subscription(s) to {ReplacementPlanId}.",
            orphan.Id, orphan.Name, orphan.BillingInterval, activeSubs.Count, pendingSubs.Count, replacementId);
    }

    await db.SaveChangesAsync();
}
```

Add `using Microsoft.Extensions.Logging;` to the top of `DataSeeder.cs` (needed for
`ILogger`/`ILoggerFactory` — not currently imported there; `StripeDemoSeeder.cs`
already imports it, use the same package, no new dependency).

### Step 2 — wire it into `SeedAsync`

```csharp
// Before:
public static async Task SeedAsync(IServiceProvider services)
{
    using IServiceScope scope = services.CreateScope();
    AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    UserManager<IdentityUser> userManager =
        scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

    // Always run: ensure seed credentials + artist slugs are correct
    await EnsureSeedUsersAsync(userManager);
    await EnsureArtistSlugsAsync(db);

    // Always run: the Free plan is seeded independently of the demo-entity guard
    // below, so a database that already has Starter/Growth/etc. still picks it up
    // on the next deploy without re-running the full seed.
    if (!await db.Plans.AnyAsync(p => p.Id == FreePlanId))
        await SeedFreePlanAsync(db);

    // Snapshot BEFORE reconciling. ReconcileCorePlansAsync will insert StarterPlanId
    // if it's missing, which would make a post-reconcile check always true and
    // silently skip demo-entity seeding on a genuinely fresh database.
    bool coreEntitiesAlreadySeeded = await db.Plans.AnyAsync(p => p.Id == StarterPlanId);

    // Always run: Starter/Growth/Premium (x2)/Pro are system-defined tiers, not
    // issuer-owned data — their canonical values live in source control, not the
    // database. This replaced a one-time "insert once, skip forever" guard that
    // left multiple environments permanently stuck on a stale snapshot after the
    // Max* limit fields and Premium's corrected pricing were added on 2026-07-18.
    // See bug-report-plans-page-data-mismatch.md and architecture.md Decisions Log
    // — "Core plan reconciliation replaces one-time plan seed".
    await ReconcileCorePlansAsync(db);

    // Guard: demo studios/subscriptions/appointments/designs/etc. still seed only
    // once — unlike the five canonical plans, this fake data has no "correct"
    // canonical state to reconcile toward on every boot.
    if (coreEntitiesAlreadySeeded)
        return;

    await SeedStudiosAndSubscriptionsAsync(db);
    await SeedStudio1EntitiesAsync(db);
    await SeedStudio2EntitiesAsync(db);
}

// After:
public static async Task SeedAsync(IServiceProvider services)
{
    using IServiceScope scope = services.CreateScope();
    AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    UserManager<IdentityUser> userManager =
        scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    ILogger logger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>().CreateLogger("DataSeeder");

    // Always run: ensure seed credentials + artist slugs are correct
    await EnsureSeedUsersAsync(userManager);
    await EnsureArtistSlugsAsync(db);

    // Always run: the Free plan is seeded independently of the demo-entity guard
    // below, so a database that already has Starter/Growth/etc. still picks it up
    // on the next deploy without re-running the full seed.
    if (!await db.Plans.AnyAsync(p => p.Id == FreePlanId))
        await SeedFreePlanAsync(db);

    // Snapshot BEFORE reconciling. ReconcileCorePlansAsync will insert StarterPlanId
    // if it's missing, which would make a post-reconcile check always true and
    // silently skip demo-entity seeding on a genuinely fresh database.
    bool coreEntitiesAlreadySeeded = await db.Plans.AnyAsync(p => p.Id == StarterPlanId);

    // Always run: Starter/Growth/Premium (x2)/Pro are system-defined tiers, not
    // issuer-owned data — their canonical values live in source control, not the
    // database. See architecture.md Decisions Log — "Core plan reconciliation
    // replaces one-time plan seed".
    await ReconcileCorePlansAsync(db);

    // Always run: retires any canonically-named plan row left behind under a
    // non-canonical Id (e.g. Premium's pre-Monthly/Yearly-split row) and reassigns
    // any Subscription still pointing at it. Must run AFTER ReconcileCorePlansAsync
    // so the correct replacement rows already exist to reassign onto. See
    // bug-report-premium-plan-duplicate-legacy-row.md and architecture.md Decisions
    // Log — "Orphaned legacy plan retirement".
    await RetireOrphanedNamedPlansAsync(db, logger);

    // Guard: demo studios/subscriptions/appointments/designs/etc. still seed only
    // once — unlike the five canonical plans, this fake data has no "correct"
    // canonical state to reconcile toward on every boot.
    if (coreEntitiesAlreadySeeded)
        return;

    await SeedStudiosAndSubscriptionsAsync(db);
    await SeedStudio1EntitiesAsync(db);
    await SeedStudio2EntitiesAsync(db);
}
```

---

## Not in scope — do not touch

- **`PlanManagementPage.tsx` / any frontend file.** Once the orphan row is gone, the
  page naturally shows one fewer card — no frontend logic references plan count or
  needs updating. If `PlanManagementPage.test.tsx`'s MSW fixture (`PLANS` constant)
  hardcodes a plan list, check it, but it almost certainly already reflects the
  intended two-Premium-row shape from the previous fix's frontend audit, not the
  three-row bug (that bug is backend-seed-only and was never reproduced in a
  frontend test fixture).
- **§5 of the bug report** ("Starter's price may show something other than €29/mo
  mid-session after a manual edit, until next restart") — already-documented,
  intentional behavior from the previous fix's Decisions Log entry. No action here.
- **A raw SQL / EF Core migration.** Deliberately not the approach — see "Design
  choice" above. Do not add one.
- **`ReconcileCorePlansAsync` itself.** Leave its body untouched; add the new method
  as a sibling, called separately from `SeedAsync`.
- **`SeedFreePlanAsync`.** Unrelated, unaffected, still correct.
- **`DeletePlanHandler` / `UpdatePlanHandler`.** Read for reference (the
  clear-sibling-`PairedPlanId` pattern), but don't modify — this fix's cleanup logic
  lives entirely in `DataSeeder.cs`.
- **Any live/staging/production database connection.** Verify via unit tests and
  local `docker compose` only, exactly as instructed in the previous fix's prompt.

---

## Phase 2 — Tests

Append to the existing `tests/Pena_e_Arte.UnitTests/Infrastructure/
DataSeederPlanReconciliationTests.cs` (do not create a new file — same class, same
`FakeDbContext _db` field already declared there). Add a `using
Microsoft.Extensions.Logging.Abstractions;` (for `NullLogger.Instance`, so tests
don't need a real logging pipeline) and a `using Pena_e_Arte.Domain.Enums;` if not
already present (it already is, per the current file).

```csharp
// ── RetireOrphanedNamedPlansAsync ───────────────────────────────────────────

[Fact]
public async Task RetireOrphanedNamedPlansAsync_NoOrphans_DoesNothing()
{
    await DataSeeder.ReconcileCorePlansAsync(_db);

    await DataSeeder.RetireOrphanedNamedPlansAsync(_db, NullLogger.Instance);

    _db.Plans.Should().HaveCount(5);
}

[Fact]
public async Task RetireOrphanedNamedPlansAsync_OrphanPremiumYearlyWithActiveSubscription_ReassignsAndDeletes()
{
    // Arrange — reproduces the exact bug: canonical rows already reconciled, PLUS a
    // legacy pre-split Premium row under an unrelated Id, with a real subscription
    // pointing at it.
    await DataSeeder.ReconcileCorePlansAsync(_db);

    Guid legacyPremiumId = Guid.NewGuid();
    Guid studioId        = Guid.NewGuid();
    _db.Plans.Add(new Plan
    {
        Id                    = legacyPremiumId,
        Name                  = "Premium",
        BillingInterval       = BillingInterval.Yearly,
        PriceMonthly          = 30m,
        PriceYearly           = 200m,
        YearlyDiscountPercent = 44,
    });
    _db.Subscriptions.Add(new Subscription
    {
        StudioId         = studioId,
        PlanId           = legacyPremiumId,
        Status           = SubscriptionStatus.Active,
        CurrentPeriodEnd = DateTime.UtcNow.AddDays(20),
        GracePeriodEnd   = DateTime.UtcNow.AddDays(27),
    });
    await _db.SaveChangesAsync();

    // Act
    await DataSeeder.RetireOrphanedNamedPlansAsync(_db, NullLogger.Instance);

    // Assert
    _db.Plans.Should().HaveCount(5); // orphan removed, still exactly the 5 canonical
    _db.Plans.Any(p => p.Id == legacyPremiumId).Should().BeFalse();

    Subscription sub = _db.Subscriptions.Single(s => s.StudioId == studioId);
    sub.PlanId.Should().Be(DataSeeder.PremiumYearlyPlanId);
}

[Fact]
public async Task RetireOrphanedNamedPlansAsync_OrphanWithMonthlyBillingInterval_ReassignsToPremiumMonthly()
{
    await DataSeeder.ReconcileCorePlansAsync(_db);

    Guid legacyPremiumId = Guid.NewGuid();
    Guid studioId        = Guid.NewGuid();
    _db.Plans.Add(new Plan
    {
        Id                    = legacyPremiumId,
        Name                  = "Premium",
        BillingInterval       = BillingInterval.Monthly,
        PriceMonthly          = 30m,
        PriceYearly           = 200m,
        YearlyDiscountPercent = 44,
    });
    _db.Subscriptions.Add(new Subscription
    {
        StudioId         = studioId,
        PlanId           = legacyPremiumId,
        Status           = SubscriptionStatus.Active,
        CurrentPeriodEnd = DateTime.UtcNow.AddDays(20),
        GracePeriodEnd   = DateTime.UtcNow.AddDays(27),
    });
    await _db.SaveChangesAsync();

    await DataSeeder.RetireOrphanedNamedPlansAsync(_db, NullLogger.Instance);

    Subscription sub = _db.Subscriptions.Single(s => s.StudioId == studioId);
    sub.PlanId.Should().Be(DataSeeder.PremiumMonthlyPlanId);
}

[Fact]
public async Task RetireOrphanedNamedPlansAsync_OrphanReferencedByPendingPlanIdOnly_ReassignsPendingAndDeletes()
{
    // A studio with an active canonical Growth subscription that has a SCHEDULED
    // downgrade onto the legacy orphan row — PlanId is fine, only PendingPlanId
    // points at the orphan. This is the gap the bug report's own FK grep prompted us
    // to find; not mentioned in the report itself.
    await DataSeeder.ReconcileCorePlansAsync(_db);

    Guid legacyPremiumId = Guid.NewGuid();
    Guid studioId        = Guid.NewGuid();
    _db.Plans.Add(new Plan
    {
        Id                    = legacyPremiumId,
        Name                  = "Premium",
        BillingInterval       = BillingInterval.Yearly,
        PriceMonthly          = 30m,
        PriceYearly           = 200m,
        YearlyDiscountPercent = 44,
    });
    _db.Subscriptions.Add(new Subscription
    {
        StudioId         = studioId,
        PlanId           = DataSeeder.GrowthPlanId,
        PendingPlanId    = legacyPremiumId,
        Status           = SubscriptionStatus.Active,
        CurrentPeriodEnd = DateTime.UtcNow.AddDays(5),
        GracePeriodEnd   = DateTime.UtcNow.AddDays(12),
    });
    await _db.SaveChangesAsync();

    await DataSeeder.RetireOrphanedNamedPlansAsync(_db, NullLogger.Instance);

    _db.Plans.Any(p => p.Id == legacyPremiumId).Should().BeFalse();
    Subscription sub = _db.Subscriptions.Single(s => s.StudioId == studioId);
    sub.PlanId.Should().Be(DataSeeder.GrowthPlanId); // untouched — was never the orphan
    sub.PendingPlanId.Should().Be(DataSeeder.PremiumYearlyPlanId);
}

[Fact]
public async Task RetireOrphanedNamedPlansAsync_OrphanWithNoSubscriptions_DeletesCleanly()
{
    await DataSeeder.ReconcileCorePlansAsync(_db);

    Guid legacyPremiumId = Guid.NewGuid();
    _db.Plans.Add(new Plan
    {
        Id                    = legacyPremiumId,
        Name                  = "Premium",
        BillingInterval       = BillingInterval.Yearly,
        PriceMonthly          = 30m,
        PriceYearly           = 200m,
        YearlyDiscountPercent = 44,
    });
    await _db.SaveChangesAsync();

    await DataSeeder.RetireOrphanedNamedPlansAsync(_db, NullLogger.Instance);

    _db.Plans.Should().HaveCount(5);
}

[Fact]
public async Task RetireOrphanedNamedPlansAsync_CalledTwice_IsIdempotent()
{
    await DataSeeder.ReconcileCorePlansAsync(_db);

    Guid legacyPremiumId = Guid.NewGuid();
    _db.Plans.Add(new Plan
    {
        Id                    = legacyPremiumId,
        Name                  = "Premium",
        BillingInterval       = BillingInterval.Yearly,
        PriceMonthly          = 30m,
        PriceYearly           = 200m,
        YearlyDiscountPercent = 44,
    });
    await _db.SaveChangesAsync();

    await DataSeeder.RetireOrphanedNamedPlansAsync(_db, NullLogger.Instance);
    await DataSeeder.RetireOrphanedNamedPlansAsync(_db, NullLogger.Instance); // no-op second time

    _db.Plans.Should().HaveCount(5);
}

[Fact]
public async Task RetireOrphanedNamedPlansAsync_DoesNotTouchDistinctlyNamedCustomPlan()
{
    // Same custom-plan fixture used in the ReconcileCorePlansAsync tests above —
    // confirms the accepted-trade-off boundary: a DIFFERENTLY named plan is safe
    // regardless of Id, only the five reserved tier names are ever swept up.
    await DataSeeder.ReconcileCorePlansAsync(_db);

    Guid customPlanId = Guid.NewGuid();
    _db.Plans.Add(new Plan
    {
        Id                    = customPlanId,
        Name                  = "Studio X Custom Deal",
        BillingInterval       = BillingInterval.Monthly,
        PriceMonthly          = 149m,
        PriceYearly           = 1490m,
        YearlyDiscountPercent = 17,
    });
    await _db.SaveChangesAsync();

    await DataSeeder.RetireOrphanedNamedPlansAsync(_db, NullLogger.Instance);

    _db.Plans.Should().HaveCount(6); // 5 canonical + 1 untouched custom
    _db.Plans.Any(p => p.Id == customPlanId).Should().BeTrue();
}

[Fact]
public async Task RetireOrphanedNamedPlansAsync_OrphanWasPairedPlanIdTargetOfCanonicalRow_ClearsTheReference()
{
    // Defensive case: some other Plan row's PairedPlanId still points at the orphan.
    // Not expected in practice for this specific bug (PairedPlanId postdates the
    // orphan), but RetireOrphanedNamedPlansAsync must not leave a dangling reference
    // if it ever happens.
    await DataSeeder.ReconcileCorePlansAsync(_db);

    Guid legacyPremiumId = Guid.NewGuid();
    _db.Plans.Add(new Plan
    {
        Id                    = legacyPremiumId,
        Name                  = "Premium",
        BillingInterval       = BillingInterval.Yearly,
        PriceMonthly          = 30m,
        PriceYearly           = 200m,
        YearlyDiscountPercent = 44,
    });
    await _db.SaveChangesAsync();

    Plan proPlan = _db.Plans.Single(p => p.Id == DataSeeder.ProPlanId);
    proPlan.PairedPlanId = legacyPremiumId; // contrived, but must be handled safely
    await _db.SaveChangesAsync();

    await DataSeeder.RetireOrphanedNamedPlansAsync(_db, NullLogger.Instance);

    _db.Plans.Single(p => p.Id == DataSeeder.ProPlanId).PairedPlanId.Should().BeNull();
}
```

Existing tests to verify still pass unchanged (nothing above touches them, but
confirm): all six `ReconcileCorePlansAsync_*` tests already in this file,
`GetPlansHandlerTests`, `ChangePlanHandlerTests`, `CancelPlanChangeHandlerTests`,
`DeletePlanHandlerTests`, `UpdatePlanHandlerTests`.

---

## Phase 3 — Local verification (manual, in addition to automated tests)

Same constraint as the previous fix: this repo can't reach the actual environment
described in the bug report. Verify via the unit tests above, plus:

1. `docker compose up -d`, run the API against a local database that already has the
   post-first-fix state (5 plans, no orphan) — confirm `RetireOrphanedNamedPlansAsync`
   finds nothing and logs nothing.
2. Manually insert a fake orphan row into the local `plans` table with `Name =
   'Premium'`, a random `Id`, and a `Subscriptions` row pointing at it via `PlanId`
   (SQL, direct to the local dev DB — this is local `docker compose`, not the real
   environment, so this is fine). Restart the API. Confirm: the orphan row is gone,
   the subscription's `PlanId` now matches one of the two canonical Premium Ids
   (check its `BillingInterval` to know which), and a `LogWarning` line appears in
   the console output naming the retirement.
3. Load `/platform/plans` in the frontend against this local backend — confirm
   exactly **six** cards total (Free, Starter, Growth, Premium × 2, Pro), not seven.

---

## Phase 4 — Update the Decisions Log

Append one row to the Decisions Log table in `docs/claude/architecture.md`,
immediately after the "Core plan reconciliation replaces one-time plan seed" row
added by the previous fix:

```
| Orphaned legacy plan retirement | `DataSeeder.RetireOrphanedNamedPlansAsync()` — always runs, immediately after `ReconcileCorePlansAsync()`. Finds any `Plan` row named exactly "Free"/"Starter"/"Growth"/"Premium"/"Pro" whose `Id` isn't one of the six canonical constants, reassigns every referencing `Subscription.PlanId` and `Subscription.PendingPlanId` to the correct canonical replacement (Premium's replacement chosen by the orphan's own `BillingInterval`), clears any sibling `Plan.PairedPlanId` still pointing at it, then deletes it. No-op once no orphan remains, so safe to run every boot indefinitely. | `ReconcileCorePlansAsync` (previous entry) only ever matches by fixed Id — a canonically-named plan under any other Id (e.g. Premium's pre-Monthly/Yearly-split row) is invisible to it, so its insert-if-missing branch adds a correct row *alongside* the leftover rather than replacing it, producing a visible duplicate card (`bug-report-premium-plan-duplicate-legacy-row.md`). Accepted trade-off: this matches by name only, so it cannot distinguish a genuine pre-split leftover from an issuer-created custom plan that happens to share a reserved tier name — an issuer needing a bespoke plan should use a distinct name. Confirmed via `AppDbContextModelSnapshot.cs` that `Subscription.PlanId` and `Subscription.PendingPlanId` are the only two FKs anywhere referencing `Plan.Id`; `Plan.PairedPlanId` is a self-reference with no FK constraint. |
```

---

## Phase 5 — Quality Gates

```bash
# Backend
dotnet build
dotnet test

# Frontend (no changes expected, confirm nothing broke)
pnpm --filter frontend test
pnpm --filter frontend lint
```

---

## Phase 6 — Forbidden Actions

- Do not write a raw SQL migration for this — see "Design choice" above.
- Do not modify `ReconcileCorePlansAsync`'s own body.
- Do not modify any frontend file.
- Do not modify `DeletePlanHandler` or `UpdatePlanHandler` — read-only references.
- Do not reassign a `Subscription` based on the UI label ("Billed yearly only")
  instead of the orphan row's actual `BillingInterval` field — the bug report
  explicitly warns against this exact shortcut.
- Do not connect to any real/staging/production database.
- Do not introduce new npm or NuGet packages (`Microsoft.Extensions.Logging` is
  already a transitive dependency via ASP.NET Core — no new package reference
  needed, just a `using`).

---

## Completion Checklist

- [ ] `using Microsoft.Extensions.Logging;` added to `DataSeeder.cs`
- [ ] `CanonicalPlanNames` array added
- [ ] `RetireOrphanedNamedPlansAsync(IAppDbContext db, ILogger logger)` added, matching signature and logic above
- [ ] Reassigns both `Subscription.PlanId` and `Subscription.PendingPlanId`
- [ ] Clears any sibling `PairedPlanId` pointing at the orphan before deleting it
- [ ] Premium orphan replacement chosen by the orphan's own `BillingInterval`, not by name alone
- [ ] Logs a structured `LogWarning` per retired orphan (no PII — plan id/name/interval, subscription counts only)
- [ ] `SeedAsync` calls `RetireOrphanedNamedPlansAsync` right after `ReconcileCorePlansAsync`, before the demo-entity guard
- [ ] 8 new unit tests appended to `DataSeederPlanReconciliationTests.cs` and passing
- [ ] Existing tests in that file and in `Billing/` still passing
- [ ] Local `docker compose` verification: injected orphan is retired, subscription reassigned correctly, log line observed, frontend shows exactly six Plan cards
- [ ] Decisions Log entry appended to `architecture.md`, directly after the previous fix's entry
- [ ] `dotnet build` clean
- [ ] `dotnet test` clean
- [ ] `pnpm lint` clean
- [ ] `pnpm test` clean
