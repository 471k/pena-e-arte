# Overnight Prompt — Fix Stale Plan Seed Data (Issuer Plans Page)

**Date:** 2026-07-19
**Files changed:** ~3 (1 backend seed file, 1 new backend test file, 1 docs update)
**Type:** Backend data-correctness bug fix — no schema migration, no frontend changes

---

## Context

`bug-report-plans-page-data-mismatch.md` (repo root) documents that the issuer
Plans page (`/platform/plans`, `PlanManagementPage.tsx`) shows wrong data for four
of five plans:

- Starter, Growth, Premium, and Pro all show **"Unlimited"** for Artists,
  Appointments/mo, and Storage — only Free shows real numbers.
- Premium's price/discount is wrong: shows **€30/mo · €200/yr · "Save 44%
  annually" · "Billed yearly only"** instead of the correct **€79/mo · €790/yr ·
  17%**, and only ONE Premium row exists where there should be two (Monthly +
  Yearly, paired).

This has already been root-caused in the bug report and independently confirmed
by reading the current source below — **do not re-investigate the frontend**.
The frontend is correct. This is a backend seed-data bug only.

### Confirmed root cause

`Pena_e_Arte.Infrastructure/Persistence/Seed/DataSeeder.cs` → `SeedAsync()` has:

```csharp
// Guard: run entity seeding only once (when plans don't yet exist)
if (await db.Plans.AnyAsync(p => p.Id == StarterPlanId))
    return;

await SeedPlansAsync(db);
```

`SeedPlansAsync()` is the single source of truth for Starter/Growth/Premium/Pro —
it already contains the **correct** values in the current source (Premium at
€79/€790/17%, all five `Max*` fields populated, two Premium rows linked via
`PairedPlanId`). But because of the early `return` above, this method only ever
executes on a database's very first boot. Any environment whose `Plans` table
was first populated before the `Max*` fields existed (added same-day as Premium's
price correction, 2026-07-18 — see architecture.md Decisions Log, "Plan usage
limits") is permanently stuck on that older snapshot. `SeedFreePlanAsync()` avoids
this because it's guarded independently, by its own `FreePlanId` check — which is
why Free is the only plan showing correct data today.

**Most likely shape of the stale row set** (do not assume this needs verifying
against a live database — it isn't reachable from this repo; the fix must be
correct regardless of the exact prior state): the existing environment has one
Premium row at `PremiumYearlyPlanId` with the old placeholder values
(`PriceMonthly=30, PriceYearly=200, YearlyDiscountPercent=44`, `Max*` all null),
and no row at all at `PremiumMonthlyPlanId` (that second row was added to
`SeedPlansAsync()`'s source *after* this environment's first boot, so the
guarded seed never inserted it). Starter/Growth/Pro exist with correct prices but
null `Max*`. The fix below self-heals correctly whether or not this guess is
exactly right — it reconciles by fixed `Id`, inserting any of the five canonical
rows that's missing and correcting any that exists with stale field values.

---

## Phase 0 — Required Reading

```
bug-report-plans-page-data-mismatch.md                                   (repo root)
Pena_e_Arte.Infrastructure/Persistence/Seed/DataSeeder.cs                 (SeedAsync, SeedPlansAsync, SeedFreePlanAsync — lines 118–262)
Pena_e_Arte.Domain/Entities/Plan.cs
Pena_e_Arte.Application/Plans/Commands/UpdatePlanCommand.cs
Pena_e_Arte.Application/Billing/Queries/GetPlansQuery.cs
Pena_e_Arte.Application/Persistence/IAppDbContext.cs
frontend/src/features/platform/components/PlanManagementPage.tsx          (read only — confirm no changes needed, see "Not in scope" below)
tests/Pena_e_Arte.UnitTests/Billing/CreatePlanHandlerTests.cs             (test-style reference)
tests/Pena_e_Arte.UnitTests/Helpers/FakeDbContext.cs
docs/claude/architecture.md — Decisions Log entries on "Plan billing interval stays
  locked per-row", "Plan usage limits", "Plan Monthly/Yearly pairing" (around lines 997–1001)
docs/claude/conventions.md
```

After reading, confirm for yourself (do not skip this): `UpdatePlanHandler` never
touches `Name`, `PriceMonthly`, `PriceYearly`, `BillingInterval`, or Stripe price
IDs on the **paired** row when syncing — only limit/feature fields and the pairing
itself. This matters for the decision below about what the reconciliation is and
isn't allowed to overwrite.

---

## The fix

Two changes to `DataSeeder.cs`:

1. Widen visibility of the five plan-ID constants from `private` to `internal`
   (the `Pena_e_Arte.Infrastructure.csproj` already grants
   `InternalsVisibleTo("Pena_e_Arte.UnitTests")` — no project file changes
   needed) so tests can reference the real IDs instead of duplicating GUID
   literals.
2. Replace the one-time `SeedPlansAsync()` + early-return guard with an
   always-run `ReconcileCorePlansAsync()` that inserts any of the five
   canonical plan rows that's missing and corrects the mutable fields on any
   that already exists.

### Step 1 — widen the five plan-ID constants

```csharp
// Before (lines 18–23):
private static readonly Guid StarterPlanId        = new("aaaa0001-0000-0000-0000-000000000000");
private static readonly Guid GrowthPlanId         = new("aaaa0002-0000-0000-0000-000000000000");
private static readonly Guid ProPlanId            = new("aaaa0003-0000-0000-0000-000000000000");
private static readonly Guid PremiumMonthlyPlanId = new("aaaa0004-0000-0000-0000-000000000000");
private static readonly Guid PremiumYearlyPlanId  = new("aaaa0005-0000-0000-0000-000000000000");
private static readonly Guid FreePlanId           = new("aaaa0006-0000-0000-0000-000000000000");

// After:
internal static readonly Guid StarterPlanId        = new("aaaa0001-0000-0000-0000-000000000000");
internal static readonly Guid GrowthPlanId         = new("aaaa0002-0000-0000-0000-000000000000");
internal static readonly Guid ProPlanId            = new("aaaa0003-0000-0000-0000-000000000000");
internal static readonly Guid PremiumMonthlyPlanId = new("aaaa0004-0000-0000-0000-000000000000");
internal static readonly Guid PremiumYearlyPlanId  = new("aaaa0005-0000-0000-0000-000000000000");
internal static readonly Guid FreePlanId           = new("aaaa0006-0000-0000-0000-000000000000");
```

Every other constant in the file (studio IDs, user IDs, etc.) stays `private` —
only these six change, and only because tests need them.

### Step 2 — `SeedAsync()`: replace the guard with an always-run reconciliation

```csharp
// Before (lines 118–143):
public static async Task SeedAsync(IServiceProvider services)
{
    using IServiceScope scope = services.CreateScope();
    AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    UserManager<IdentityUser> userManager =
        scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

    // Always run: ensure seed credentials + artist slugs are correct
    await EnsureSeedUsersAsync(userManager);
    await EnsureArtistSlugsAsync(db);

    // Always run: the Free plan is seeded independently of the one-time entity seed
    // guard below, so a database that already has Starter/Growth/etc. still picks it
    // up on the next deploy without re-running the full seed.
    if (!await db.Plans.AnyAsync(p => p.Id == FreePlanId))
        await SeedFreePlanAsync(db);

    // Guard: run entity seeding only once (when plans don't yet exist)
    if (await db.Plans.AnyAsync(p => p.Id == StarterPlanId))
        return;

    await SeedPlansAsync(db);
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
```

### Step 3 — replace `SeedPlansAsync` with `ReconcileCorePlansAsync`

Delete the existing `SeedPlansAsync(AppDbContext db)` method (lines ~147–239)
entirely and replace it with:

```csharp
// ─── Core plans (always reconciled) ────────────────────────────────────────

// Starter/Growth/Premium (x2)/Pro are system-defined tiers. Unlike SeedFreePlanAsync
// (insert-once, by design — see its own comment) and the demo studios/appointments/etc.
// below, these five rows are reconciled to the literal values below on EVERY startup:
// any of the five that's missing gets inserted, and any that already exists gets its
// mutable fields corrected back to these values. This intentionally mirrors
// UpdatePlanHandler's own exclusion list for the cross-row pairing sync — Name,
// BillingInterval, PriceMonthly, PriceYearly, YearlyDiscountPercent, Stripe price IDs,
// and AllowBrandingRemoval are all included here (unlike the pairing sync, which
// excludes price/interval/Stripe IDs deliberately, because pairing sync only keeps two
// *existing* rows from drifting apart, it does not define what "correct" looks like).
//
// Practical consequence, spelled out because it's a real behavior change: if an issuer
// edits Starter, Growth, Premium, or Pro in place via PlanManagementPage, that edit will
// be reverted back to these values on the next app restart/deploy. That's the intended
// trade-off — see architecture.md Decisions Log, "Core plan reconciliation replaces
// one-time plan seed", for the reasoning and for what an issuer should do instead
// (clone a new Plan row rather than editing one of these five).
internal static async Task ReconcileCorePlansAsync(IAppDbContext db)
{
    Plan[] canonical =
    [
        new Plan
        {
            Id                       = StarterPlanId,
            Name                     = "Starter",
            BillingInterval          = BillingInterval.Monthly,
            PriceMonthly             = 29m,
            PriceYearly              = 290m,
            YearlyDiscountPercent    = 17,
            MaxArtists               = 1,
            MaxAppointmentsPerMonth  = 40,
            MaxNotificationsPerMonth = 150,
            MaxStorageGb             = 2,
            MaxLocations             = 1,
        },
        new Plan
        {
            Id                       = GrowthPlanId,
            Name                     = "Growth",
            BillingInterval          = BillingInterval.Monthly,
            PriceMonthly             = 59m,
            PriceYearly              = 590m,
            YearlyDiscountPercent    = 17,
            AllowBrandingRemoval     = true,
            MaxArtists               = 3,
            MaxAppointmentsPerMonth  = 150,
            MaxNotificationsPerMonth = 600,
            MaxStorageGb             = 10,
            MaxLocations             = 1,
        },
        // Premium sits between Growth and Pro. Two rows, not one — see Decisions Log:
        // "Plan billing interval stays locked per-row". PairedPlanId links them so
        // UpdatePlanHandler keeps their limit/feature fields in sync.
        new Plan
        {
            Id                       = PremiumMonthlyPlanId,
            Name                     = "Premium",
            BillingInterval          = BillingInterval.Monthly,
            PriceMonthly             = 79m,
            PriceYearly              = 790m,
            YearlyDiscountPercent    = 17,
            AllowBrandingRemoval     = true,
            PrioritySupport          = true,
            MaxArtists               = 6,
            MaxAppointmentsPerMonth  = 400,
            MaxNotificationsPerMonth = 1200,
            MaxStorageGb             = 25,
            MaxLocations             = 2,
            PairedPlanId             = PremiumYearlyPlanId,
        },
        new Plan
        {
            Id                       = PremiumYearlyPlanId,
            Name                     = "Premium",
            BillingInterval          = BillingInterval.Yearly,
            PriceMonthly             = 79m,
            PriceYearly              = 790m,
            YearlyDiscountPercent    = 17,
            AllowBrandingRemoval     = true,
            PrioritySupport          = true,
            MaxArtists               = 6,
            MaxAppointmentsPerMonth  = 400,
            MaxNotificationsPerMonth = 1200,
            MaxStorageGb             = 25,
            MaxLocations             = 2,
            PairedPlanId             = PremiumMonthlyPlanId,
        },
        new Plan
        {
            Id                       = ProPlanId,
            Name                     = "Pro",
            BillingInterval          = BillingInterval.Monthly,
            PriceMonthly             = 99m,
            PriceYearly              = 990m,
            YearlyDiscountPercent    = 17,
            AllowBrandingRemoval     = true,
            AllowApiAccess           = true,
            PrioritySupport          = true,
            // Soft caps, not true unlimited — protects against a single runaway
            // account inflating Twilio/Hangfire/DB load (owner decision, 2026-07-18).
            MaxArtists               = 10,
            MaxAppointmentsPerMonth  = 1000,
            MaxNotificationsPerMonth = 2500,
            MaxStorageGb             = 50,
            MaxLocations             = 10,
        },
    ];

    Guid[] canonicalIds = canonical.Select(p => p.Id).ToArray();
    Dictionary<Guid, Plan> existingById = await db.Plans
        .Where(p => canonicalIds.Contains(p.Id))
        .ToDictionaryAsync(p => p.Id);

    foreach (Plan source in canonical)
    {
        if (existingById.TryGetValue(source.Id, out Plan? row))
        {
            row.Name                     = source.Name;
            row.BillingInterval          = source.BillingInterval;
            row.PriceMonthly             = source.PriceMonthly;
            row.PriceYearly              = source.PriceYearly;
            row.YearlyDiscountPercent    = source.YearlyDiscountPercent;
            row.AllowBrandingRemoval     = source.AllowBrandingRemoval;
            row.MaxArtists               = source.MaxArtists;
            row.MaxAppointmentsPerMonth  = source.MaxAppointmentsPerMonth;
            row.MaxNotificationsPerMonth = source.MaxNotificationsPerMonth;
            row.MaxStorageGb             = source.MaxStorageGb;
            row.MaxLocations             = source.MaxLocations;
            row.AllowApiAccess           = source.AllowApiAccess;
            row.PrioritySupport          = source.PrioritySupport;
            row.PairedPlanId             = source.PairedPlanId;
            // Stripe price IDs are deliberately left untouched — those are populated
            // by StripeDemoSeeder / real Stripe dashboard configuration, not here.
        }
        else
        {
            db.Plans.Add(source);
        }
    }

    await db.SaveChangesAsync();
}
```

Note the parameter type: `IAppDbContext`, not the concrete `AppDbContext` that
every other method in this file takes. `AppDbContext` implements `IAppDbContext`
so the call site (`await ReconcileCorePlansAsync(db);` inside `SeedAsync`) needs
no change, but the narrower interface is what makes this testable against
`FakeDbContext` (EF Core InMemory) in the fast unit test project instead of
requiring a live MySQL instance. Add `using Pena_e_Arte.Application.Persistence;`
to the top of `DataSeeder.cs` for `IAppDbContext`.

---

## Not in scope — do not touch

- **`PlanManagementPage.tsx` / any frontend file.** Already confirmed correct:
  `formatLimit()` renders `null` as `"Unlimited {unit}"`, matching
  `Plan.Max* : int?` where `null` means unlimited (architecture.md, "Plan usage
  limits"). Once the database has real numbers, the page renders them correctly
  with zero frontend changes.
- **The "one merged Premium card vs. two cards" question the bug report raises
  in §4.** Already resolved by existing code — `PlanCard` renders one card per
  `Plan` row, and shows the *other* billing interval's price as a small
  "reference only, not charged" line (`{formatCurrency(plan.priceYearly)}/yr
  ref.` or the mirror for a Yearly row). Once `ReconcileCorePlansAsync` produces
  both a `PremiumMonthlyPlanId` and `PremiumYearlyPlanId` row, the page will
  show two Premium cards automatically — "Billed monthly" and "Billed yearly
  only" respectively. No frontend change needed.
- **The subscriber-count question in bug report §5.** Already verified correct
  in source: `GetPlansHandler` computes `db.Subscriptions.Count(s => s.PlanId ==
  p.Id)` — correctly scoped per plan, not a shared/global count. The numbers on
  screen reflect real subscription rows, not a bug. One thing worth a manual
  check after this fix ships to the real environment (not something to test
  here, since it depends on data this repo can't see): DataSeeder's own demo
  subscriptions point at `GrowthPlanId` and `StarterPlanId` only, never at
  either Premium row — so if that environment's "Premium: 1" subscriber came
  from a real signup rather than seed data, confirm after deploy which of the
  two Premium rows (Monthly or Yearly) it's still correctly attached to. This
  is a read-only verification, not a code change.
- **`SeedFreePlanAsync` and its independent guard.** Already correct and
  already the model this fix follows for the insert-half of the behavior.
  Leave it exactly as-is.
- **Any EF Core migration.** All five `Max*` / `AllowApiAccess` /
  `PrioritySupport` / `PairedPlanId` columns already exist (migration
  `20260718133713_AddPlanUsageLimitsAndStudioStorageUsage`). This is a pure
  data-seeding logic change — no schema change, no new migration file.

---

## Phase 2 — Tests

New file: `tests/Pena_e_Arte.UnitTests/Infrastructure/DataSeederPlanReconciliationTests.cs`

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Persistence.Seed;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Infrastructure;

public class DataSeederPlanReconciliationTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    [Fact]
    public async Task ReconcileCorePlansAsync_EmptyDatabase_InsertsAllFiveCanonicalPlans()
    {
        // Act
        await DataSeeder.ReconcileCorePlansAsync(_db);

        // Assert
        _db.Plans.Should().HaveCount(5);
        _db.Plans.Select(p => p.Name).Should()
            .Contain(["Starter", "Growth", "Premium", "Pro"]);
        _db.Plans.Count(p => p.Name == "Premium").Should().Be(2);
    }

    [Fact]
    public async Task ReconcileCorePlansAsync_StaleStarterMissingMaxFields_BackfillsThem()
    {
        // Arrange — exact stale shape from the bug report: correct price, null limits
        _db.Plans.Add(new Plan
        {
            Id                    = DataSeeder.StarterPlanId,
            Name                  = "Starter",
            BillingInterval       = BillingInterval.Monthly,
            PriceMonthly          = 29m,
            PriceYearly           = 290m,
            YearlyDiscountPercent = 17,
            MaxArtists            = null,
            MaxAppointmentsPerMonth = null,
            MaxStorageGb          = null,
        });
        await _db.SaveChangesAsync();

        // Act
        await DataSeeder.ReconcileCorePlansAsync(_db);

        // Assert
        Plan starter = _db.Plans.Single(p => p.Id == DataSeeder.StarterPlanId);
        starter.MaxArtists.Should().Be(1);
        starter.MaxAppointmentsPerMonth.Should().Be(40);
        starter.MaxNotificationsPerMonth.Should().Be(150);
        starter.MaxStorageGb.Should().Be(2);
        starter.MaxLocations.Should().Be(1);
        // Price was already correct — must not have changed
        starter.PriceMonthly.Should().Be(29m);
    }

    [Fact]
    public async Task ReconcileCorePlansAsync_OnlyStalePremiumYearlyRowExists_CorrectsItAndInsertsMissingMonthlyRow()
    {
        // Arrange — reproduces the exact bug: one Premium row at the Yearly id, with
        // the old placeholder pricing and no Max* fields, and NO row at all for the
        // Monthly id.
        _db.Plans.Add(new Plan
        {
            Id                    = DataSeeder.PremiumYearlyPlanId,
            Name                  = "Premium",
            BillingInterval       = BillingInterval.Yearly,
            PriceMonthly          = 30m,
            PriceYearly           = 200m,
            YearlyDiscountPercent = 44,
            MaxArtists            = null,
            MaxAppointmentsPerMonth = null,
            MaxStorageGb          = null,
            PairedPlanId          = null,
        });
        await _db.SaveChangesAsync();

        // Act
        await DataSeeder.ReconcileCorePlansAsync(_db);

        // Assert — exactly two Premium rows now exist
        List<Plan> premiumRows = _db.Plans.Where(p => p.Name == "Premium").ToList();
        premiumRows.Should().HaveCount(2);

        Plan yearly = premiumRows.Single(p => p.Id == DataSeeder.PremiumYearlyPlanId);
        yearly.PriceMonthly.Should().Be(79m);
        yearly.PriceYearly.Should().Be(790m);
        yearly.YearlyDiscountPercent.Should().Be(17);
        yearly.MaxArtists.Should().Be(6);
        yearly.MaxAppointmentsPerMonth.Should().Be(400);
        yearly.MaxStorageGb.Should().Be(25);
        yearly.PairedPlanId.Should().Be(DataSeeder.PremiumMonthlyPlanId);

        Plan monthly = premiumRows.Single(p => p.Id == DataSeeder.PremiumMonthlyPlanId);
        monthly.BillingInterval.Should().Be(BillingInterval.Monthly);
        monthly.PriceMonthly.Should().Be(79m);
        monthly.PriceYearly.Should().Be(790m);
        monthly.PairedPlanId.Should().Be(DataSeeder.PremiumYearlyPlanId);
    }

    [Fact]
    public async Task ReconcileCorePlansAsync_ProMissingMaxFields_BackfillsThemWithoutTouchingPrice()
    {
        _db.Plans.Add(new Plan
        {
            Id                    = DataSeeder.ProPlanId,
            Name                  = "Pro",
            BillingInterval       = BillingInterval.Monthly,
            PriceMonthly          = 99m,
            PriceYearly           = 990m,
            YearlyDiscountPercent = 17,
        });
        await _db.SaveChangesAsync();

        await DataSeeder.ReconcileCorePlansAsync(_db);

        Plan pro = _db.Plans.Single(p => p.Id == DataSeeder.ProPlanId);
        pro.MaxArtists.Should().Be(10);
        pro.MaxAppointmentsPerMonth.Should().Be(1000);
        pro.MaxNotificationsPerMonth.Should().Be(2500);
        pro.MaxStorageGb.Should().Be(50);
        pro.MaxLocations.Should().Be(10);
        pro.AllowApiAccess.Should().BeTrue();
        pro.PrioritySupport.Should().BeTrue();
        pro.PriceMonthly.Should().Be(99m);
    }

    [Fact]
    public async Task ReconcileCorePlansAsync_CalledTwice_IsIdempotent()
    {
        await DataSeeder.ReconcileCorePlansAsync(_db);
        await DataSeeder.ReconcileCorePlansAsync(_db);

        _db.Plans.Should().HaveCount(5);
        _db.Plans.Count(p => p.Id == DataSeeder.PremiumMonthlyPlanId).Should().Be(1);
        _db.Plans.Count(p => p.Id == DataSeeder.PremiumYearlyPlanId).Should().Be(1);
    }

    [Fact]
    public async Task ReconcileCorePlansAsync_DoesNotTouchUnrelatedPlanRows()
    {
        // A hand-created Plan (e.g. issuer-cloned custom tier) with an Id outside the
        // five canonical constants must be left completely alone.
        Guid customPlanId = Guid.NewGuid();
        _db.Plans.Add(new Plan
        {
            Id                    = customPlanId,
            Name                  = "Studio X Custom Deal",
            BillingInterval       = BillingInterval.Monthly,
            PriceMonthly          = 149m,
            PriceYearly           = 1490m,
            YearlyDiscountPercent = 17,
            MaxArtists            = 20,
        });
        await _db.SaveChangesAsync();

        await DataSeeder.ReconcileCorePlansAsync(_db);

        _db.Plans.Should().HaveCount(6); // 5 canonical + 1 untouched custom
        Plan custom = _db.Plans.Single(p => p.Id == customPlanId);
        custom.Name.Should().Be("Studio X Custom Deal");
        custom.PriceMonthly.Should().Be(149m);
        custom.MaxArtists.Should().Be(20);
    }
}
```

Existing tests to check (should already pass unchanged — verify, don't just
assume): `GetPlansHandlerTests.cs`, `CreatePlanHandlerTests.cs`,
`UpdatePlanHandlerTests.cs`, `DeletePlanHandlerTests.cs`,
`GetPlanUsageReportHandlerTests.cs`. None of these construct plans via
`DataSeeder`, so none should be affected — running them is a sanity check that
nothing in `Plan.cs` or the handlers was touched.

---

## Phase 3 — Local verification (manual, in addition to automated tests)

This repo can't reach the actual stale environment described in the bug report
— the fix has to be correct by construction, verified via the unit tests above.
But also do this locally to sanity-check the real startup path
(`DataSeeder.SeedAsync`, not just the extracted method):

1. `docker compose up -d` to bring up local MySQL + Redis.
2. Run the API once against a **fresh** local database — confirm five plans
   exist (Starter/Growth/Premium×2/Pro) plus Free, all six with populated
   `Max*` fields, via `GET /api/v1/billing/plans` (issuer JWT required) or a
   direct query against the local `plans` table.
3. Stop the API, restart it against the **same** (now non-empty) database —
   confirm no duplicate rows appear and values are unchanged (idempotency,
   same property the unit test above checks, but exercised through the real
   `SeedAsync` entry point with the real `UserManager` DI path).
4. Load `/platform/plans` in the frontend against this local backend — confirm
   all five cards show numeric Artists/Appointments/Storage (not "Unlimited"),
   and Premium renders as two cards, €79/mo and €790/yr respectively.

---

## Phase 4 — Update the Decisions Log

Append one row to the Decisions Log table in `docs/claude/architecture.md`
(the table currently ends around line 1001, just before "## Client QA Pass —
2026-07-02" — add this as the new last row):

```
| Core plan reconciliation replaces one-time plan seed | `DataSeeder.SeedPlansAsync()` (insert-once, guarded by `Plans.Any(Id == StarterPlanId)`) replaced with `DataSeeder.ReconcileCorePlansAsync()` (always runs on startup; upserts by fixed Id — inserts Starter/Growth/PremiumMonthly/PremiumYearly/Pro if missing, corrects Name/BillingInterval/price/discount/branding/Max*/AllowApiAccess/PrioritySupport/PairedPlanId if the row already exists with stale values). Stripe price IDs are excluded from reconciliation (populated by `StripeDemoSeeder`/real Stripe config, not source-controlled here). `SeedFreePlanAsync`'s independent insert-once-by-Id guard is unchanged. | The one-time guard left any environment whose `Plans` table was first populated before the `Max*` fields and Premium's corrected pricing existed permanently stuck on that stale snapshot — see `bug-report-plans-page-data-mismatch.md`. Consequence worth flagging explicitly: an issuer editing Starter/Growth/Premium/Pro in place via `PlanManagementPage` will have that edit reverted on the next deploy, since these five rows are now source-of-truth-owned, not database-owned. An issuer who needs a bespoke arrangement for one studio should clone a new `Plan` row with its own Id instead of editing one of the five canonical tiers. |
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

All must be clean. No new frontend tests are required since no frontend files
change.

---

## Phase 6 — Forbidden Actions

- Do not modify `PlanManagementPage.tsx` or any other frontend file — the
  frontend is already correct (see "Not in scope" above).
- Do not modify `SeedFreePlanAsync` or its guard.
- Do not add a new EF Core migration — no schema change is needed.
- Do not widen visibility of any `DataSeeder` constant other than the six
  plan-ID `Guid`s (`StarterPlanId`, `GrowthPlanId`, `ProPlanId`,
  `PremiumMonthlyPlanId`, `PremiumYearlyPlanId`, `FreePlanId`).
- Do not change `UpdatePlanHandler`'s pairing-sync exclusion list (price,
  `BillingInterval`, Stripe IDs stay excluded there — that logic is unrelated
  and correct as-is; only `DataSeeder`'s reconciliation includes those fields).
- Do not attempt to connect to or query any real/staging/production database —
  this fix must be verified via the unit tests and local `docker compose`
  environment only.
- Do not introduce new npm or NuGet packages.

---

## Completion Checklist

- [ ] Six plan-ID constants changed from `private` to `internal`
- [ ] `SeedAsync()` snapshots `coreEntitiesAlreadySeeded` before calling reconciliation
- [ ] `SeedPlansAsync` deleted; `ReconcileCorePlansAsync(IAppDbContext db)` added in its place
- [ ] `ReconcileCorePlansAsync` inserts missing canonical rows and updates existing ones by Id
- [ ] `using Pena_e_Arte.Application.Persistence;` added to `DataSeeder.cs`
- [ ] Demo studio/subscription/appointment seeding still gated behind the one-time guard (unchanged behavior)
- [ ] 6 new unit tests added and passing (empty DB insert, Starter backfill, Premium stale-row correction + missing-row insert, Pro backfill, idempotency, unrelated-plan-untouched)
- [ ] Existing Billing test files (`GetPlansHandlerTests`, `CreatePlanHandlerTests`, `UpdatePlanHandlerTests`, `DeletePlanHandlerTests`, `GetPlanUsageReportHandlerTests`) verified still passing
- [ ] Local `docker compose` verification: fresh DB → 6 plans with populated `Max*`; restart → no duplicates; frontend shows numeric values + two Premium cards
- [ ] Decisions Log entry appended to `architecture.md`
- [ ] `dotnet build` clean
- [ ] `dotnet test` clean
- [ ] `pnpm lint` clean
- [ ] `pnpm test` clean (no frontend files changed, confirms nothing else broke)
