# Overnight Prompt — Revenue Ledger and MRR Movements (Batch 3b)

> Feed this file directly to Claude Code as the task prompt. It is self-contained: exact
> files, exact current code (re-read from live source on 2026-09-24, on top of Batch 3a —
> see Prerequisite below), exact target behaviour, exact tests, exact docs to sync. Read the
> whole file before writing anything.

**Date logged:** 2026-09-24
**Requested by:** Phi (Finance project, via Engineering Consultation on
`docs/claude/spec-yearly-refunds-and-revenue-ledger-2026-09-23.md`, Part B)
**Origin:** That spec left schema/design open ("yours to finalise"). Section 2 below is the
resulting design — treat it as decided, not as a proposal to re-litigate. B1–B5 references
below refer to that spec's own section numbers.
**Prerequisite:** Batch 1, Batch 2, Batch 2b merged, **and Batch 3a
(`overnight-prompt-yearly-cancellation-refunds-2026-09-24.md`) merged** — this batch adds a
`Churn` ledger write to `CancelMySubscriptionCommand`/`CancelSubscriptionCommand`, both
introduced by Batch 3a, and depends on that batch's `AdminCancelledAt`→`CancelledAt` rename
in `MrrInputLoader.cs`/`MrrRules.cs`. Do not start this batch until Batch 3a's branch is
merged to `main`; if it is not yet merged, stop and say so rather than branching off stale
code.
**Mode:** Fully autonomous, no user present. Do not stop to ask questions — open product
questions are in §3 with defaults. Where this file states a fact about current code it was
re-read on 2026-09-24; if your checkout disagrees, trust your checkout, note the drift in
your final report, and do not silently change approach.

**Before starting**, run:

```bash
git add -A && git commit -m "checkpoint: before revenue ledger" --allow-empty
git checkout -b feature/revenue-ledger
```

Commit at the end of each phase (§5–§10).

---

## 1. Goal

`GetMrrHistoryHandler.cs` reconstructs every past month's MRR from *current* subscription
state (`MrrInputLoader`/`MrrRules.BillingWindow` — Batch 2's D3, "conservative, can
under-count, never over-count"). It cannot see a plan change or a since-corrected mistake
that happened and reversed within the reconstruction window, and the chart says so ("Past
months are estimated…"). This batch adds an append-only `SubscriptionRevenueEvent` ledger
written at the moment each MRR-affecting transition actually happens, so months on or after
the ledger's start are *recorded* history, not reconstruction — plus the MRR-movements chart
and GRR/NRR tiles that recorded history makes possible for the first time.

No studio is subscribed to a paid plan as of 2026-09-23, so the backfill (§9) produces demo
data only — cheapest to ship before the first real subscriber, same reasoning Batch 2b used.

Applicable `CLAUDE.md` rules: #1 (tenant isolation — `SubscriptionRevenueEvent` is
deliberately unfiltered, same class as `Subscription`; admin-only reads), #3 (no PII —
amounts/enum values/ids only), #5 (Serilog only), #6 (benchmark — §12), #7 (Help sync — §11,
a small addition since nothing user-facing changes for owners; admin-only feature).

---

## 2. Decisions made (Engineering Consultation) — implement as specified

### 2.1 Schema

```csharp
namespace Pena_e_Arte.Domain.Entities;

public class SubscriptionRevenueEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SubscriptionId { get; set; }
    public Guid StudioId { get; set; }
    public DateTime OccurredAt { get; set; }
    public RevenueEventType Type { get; set; }
    public Guid? PlanId { get; set; }
    public BillingInterval? Interval { get; set; }
    public decimal MrrBefore { get; set; }
    public decimal MrrAfter { get; set; }
    public string Source { get; set; } = string.Empty;    // the handler/job class name that wrote it
    public string? StripeEventId { get; set; }             // webhook idempotency — null for non-webhook sources
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow; // insertion time — tiebreak for same-OccurredAt events, never itself the query axis
}
```

```csharp
namespace Pena_e_Arte.Domain.Enums;

public enum RevenueEventType { New, Expansion, Contraction, Churn, Reactivation, Paused, Resumed, PastDue, Recovered }
```

`MrrBefore`/`MrrAfter` are the subscription's **contracted** monthly-equivalent MRR
(`MrrRules.MonthlyEquivalent`, reading the Batch 2b billed-amount snapshot — B1: "never from
the price list"), with one deliberate exception: **`Churn`'s `MrrAfter` is always `0`** (the
contract itself ends), whereas `Paused`/`PastDue`'s `MrrAfter` equals `MrrBefore` (the
contract is unchanged — only its *billing inclusion* changes, which §2.3's query logic
derives from `Type`, not from the stored amount). Get this distinction right — it's what
lets `Resumed`/`Recovered` "hand back" the exact figure `Paused`/`PastDue` recorded, with no
separate lookup needed.

`SubscriptionRevenueEventConfiguration.cs`:

```csharp
builder.ToTable("subscription_revenue_events");
builder.HasKey(e => e.Id).HasName("pk_subscription_revenue_events");
builder.Property(e => e.MrrBefore).HasPrecision(10, 2);
builder.Property(e => e.MrrAfter).HasPrecision(10, 2);
builder.HasIndex(e => e.SubscriptionId).HasDatabaseName("ix_subscription_revenue_events_subscription_id");
builder.HasIndex(e => e.OccurredAt).HasDatabaseName("ix_subscription_revenue_events_occurred_at");
// Partial-unique-index note: MySQL/EF Core don't support a filtered unique index the way
// SQL Server does, so this is a plain unique index — every row needs a StripeEventId to
// satisfy it, which non-webhook sources (New via CreateSubscriptionCommand, Churn via
// CancelSubscriptionCommand, Paused/Resumed) don't have. Use a synthetic non-null
// idempotency string for those instead of leaving StripeEventId null — see §2.2's writer
// helper, which builds one from SubscriptionId+OccurredAt+Type for non-webhook sources so
// this index still does useful work without needing every column nullable-and-unindexed.
builder.HasIndex(e => e.StripeEventId).IsUnique()
       .HasDatabaseName("ix_subscription_revenue_events_stripe_event_id");
builder.HasOne<Subscription>().WithMany().HasForeignKey(e => e.SubscriptionId)
       .HasConstraintName("fk_subscription_revenue_events_subscriptions").OnDelete(DeleteBehavior.Cascade);
```

Not a `TenantEntity` — admin-only, cross-tenant aggregate reads, same class as
`Subscription`/`SubscriptionInvoicePayment`/`SubscriptionRefund`. No new
`IgnoreQueryFilters()` registry row needed (nothing to ignore).

Add `DbSet<SubscriptionRevenueEvent> SubscriptionRevenueEvents { get; }` to
`IAppDbContext`, `AppDbContext`, `FakeDbContext` — same three-file pattern as every prior
batch's new table.

Migration name: `AddSubscriptionRevenueLedger`.

### 2.2 One writer, reused everywhere

Every write site (§2.4's table) needs identical "compute before/after, skip if unchanged,
build a deterministic idempotency key, insert" logic. One shared helper, co-located with
`MrrRules.cs`:

```csharp
namespace Pena_e_Arte.Application.Platform.Revenue;

public static class RevenueEventRecorder
{
    private static readonly HashSet<RevenueEventType> AlwaysRecord =
        [RevenueEventType.PastDue, RevenueEventType.Recovered, RevenueEventType.Paused, RevenueEventType.Resumed];

    /// <summary>Records a movement unless it's a no-op (MrrBefore == MrrAfter) for a type
    /// whose whole point IS the amount changing (New/Expansion/Contraction/Churn/Reactivation).
    /// PastDue/Recovered/Paused/Resumed are state-only movements — always record them even
    /// when the contracted amount didn't change, since Type alone carries the meaning.</summary>
    public static void Record(
        IAppDbContext db, Subscription subscription, decimal mrrBefore, decimal mrrAfter,
        RevenueEventType type, string source, string? stripeEventId, DateTime? occurredAt = null)
    {
        if (mrrBefore == mrrAfter && !AlwaysRecord.Contains(type)) return;

        db.SubscriptionRevenueEvents.Add(new SubscriptionRevenueEvent
        {
            SubscriptionId = subscription.Id,
            StudioId = subscription.StudioId,
            OccurredAt = occurredAt ?? DateTime.UtcNow,
            Type = type,
            PlanId = subscription.PlanId,
            Interval = subscription.BillingInterval,
            MrrBefore = mrrBefore,
            MrrAfter = mrrAfter,
            Source = source,
            // Webhook sources pass the real Stripe event id (idempotent across redelivery).
            // Non-webhook sources get a deterministic synthetic key so the unique index still
            // rejects an accidental double-call without needing a nullable-unindexed column.
            StripeEventId = stripeEventId ?? $"{source}:{subscription.Id}:{type}:{(occurredAt ?? DateTime.UtcNow):O}",
        });
    }
}
```

The synthetic key for non-webhook sources is deliberately coarse (subscription+type+instant,
not a full payload hash) — two distinct real transitions of the *same type* for the *same
subscription* in the *same tick* are not a real scenario this platform can produce (every
write site is triggered by one user action or one webhook at a time), so this cannot
falsely dedupe two legitimate events. It exists only to catch an accidental double-`Send` of
the same command, mirroring why `SubscriptionInvoicePayment.StripeInvoiceId`'s unique index
works even though most of *its* callers also aren't retried webhooks.

### 2.3 Ledger-based MRR queries

New pure class, mirroring `MrrRules`' own "pure, DB-free, one definition" shape:

```csharp
namespace Pena_e_Arte.Application.Platform.Revenue;

public static class RevenueLedgerRules
{
    private static readonly HashSet<RevenueEventType> BillingTypes =
        [RevenueEventType.New, RevenueEventType.Expansion, RevenueEventType.Contraction,
         RevenueEventType.Reactivation, RevenueEventType.Recovered];

    /// <summary>The latest event at-or-before t, per subscription, counts toward MRR only
    /// when its Type represents an actively-billing state (B3). Ties on OccurredAt broken by
    /// CreatedAt (insertion order) — never by Id, which is a random Guid.</summary>
    public static decimal MrrAt(IEnumerable<SubscriptionRevenueEvent> events, DateTime t) =>
        events
            .Where(e => e.OccurredAt <= t)
            .GroupBy(e => e.SubscriptionId)
            .Select(g => g.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.CreatedAt).First())
            .Where(latest => BillingTypes.Contains(latest.Type))
            .Sum(latest => latest.MrrAfter);

    /// <summary>Movement totals for one calendar month — only the five ChartMogul/Baremetrics
    /// movement types feed this (B3); Paused/Resumed/PastDue/Recovered are state, not MRR
    /// movement, and are excluded here even though they're real ledger rows.</summary>
    public static MrrMovementTotals MovementsFor(IEnumerable<SubscriptionRevenueEvent> monthEvents)
    {
        decimal Sum(RevenueEventType type) =>
            monthEvents.Where(e => e.Type == type).Sum(e => e.MrrAfter - e.MrrBefore);

        decimal newMrr = Sum(RevenueEventType.New);
        decimal expansion = Sum(RevenueEventType.Expansion);
        decimal reactivation = Sum(RevenueEventType.Reactivation);
        decimal contraction = Sum(RevenueEventType.Contraction); // already negative (MrrAfter < MrrBefore)
        decimal churn = Sum(RevenueEventType.Churn);             // already negative (MrrAfter == 0)

        return new MrrMovementTotals(newMrr, expansion, reactivation, contraction, churn,
            newMrr + expansion + reactivation + contraction + churn);
    }
}

public sealed record MrrMovementTotals(
    decimal New, decimal Expansion, decimal Reactivation, decimal Contraction, decimal Churn, decimal Net);
```

**Equality invariant (B3):** `RevenueLedgerRules.MrrAt(events, now)` must equal
`MrrRules.MrrAt(inputs, now, now)` for a fully up-to-date ledger — test this explicitly
(§7).

### 2.4 Write sites — exact insertion points

For every site below, capture `mrrBefore = MrrRules.MonthlyEquivalent(subscription)`
**before** any mutation, mutate as the handler already does, then call
`RevenueEventRecorder.Record` with the post-mutation `mrrAfter`.

| Type | Handler | File | Exact insertion point |
|---|---|---|---|
| `New`/`Reactivation` | `ActivateCheckoutSubscriptionHandler` | `Billing/Commands/ActivateCheckoutSubscriptionCommand.cs` | After line 87 (`subscription.TrialExpiresAt = null;`), before `RecordReferralRedemptionAsync` |
| `New`/`Reactivation` | `CreateSubscriptionHandler` | `Billing/Commands/CreateSubscriptionCommand.cs` | After line 132 (`subscription.BilledCurrency = ...`), before the `IsSolo` comment block |
| `New`/`Reactivation` | `ActivateSubscriptionManuallyHandler` | `Billing/Commands/ActivateSubscriptionManuallyCommand.cs` | After both branches (lines 66 and 79) — capture `mrrBefore` before the `if (studio.Subscription is null)` block at line 52 |
| `Expansion` | `ChangePlanHandler`, immediate-upgrade branch only | `Billing/Commands/ChangePlanCommand.cs` | After line 83 (`subscription.CurrentPeriodEnd = periodEnd;`), inside the `if (isUpgrade)` block |
| `Expansion`/`Contraction` | `HandleSubscriptionUpdatedHandler`, **scheduled-change-landing only** | `Billing/Commands/HandleSubscriptionUpdatedCommand.cs` | End of `Handle`, guarded by `pendingLanded` — see §2.4.1, do not also fire on a plain amount-changed webhook |
| `PastDue`/`Recovered` | `HandleSubscriptionUpdatedHandler` | same file | End of `Handle`, status-transition branch — see §2.4.1 |
| `Churn` | `HandleSubscriptionDeletedHandler` | `Billing/Commands/HandleSubscriptionDeletedCommand.cs` | After line 19 (`subscription.CancelAtPeriodEnd = false;`) |
| `Churn` | `CancelSubscriptionHandler` (admin) | `Platform/Commands/CancelSubscriptionCommand.cs` | End of Batch 3a's yearly-refund branch (§2.1.C of that batch) **and** the pre-existing non-yearly path — capture `mrrBefore` at the very top of `Handle`, before line 53's `subscription.Status = SubscriptionStatus.Cancelled;` |
| `Churn` | `CancelMySubscriptionHandler` (owner) | `Billing/Commands/CancelMySubscriptionCommand.cs` (Batch 3a) | Same shape — capture `mrrBefore` before the yearly/monthly/cash branch, record `Churn` only in the yearly branch (monthly's `CancelAtPeriodEnd=true` doesn't churn yet — the real churn happens later when Stripe's `customer.subscription.deleted` fires and lands in `HandleSubscriptionDeletedHandler` above) |
| `Churn` (only if had paid) | `GracePeriodEndJob` | `Infrastructure/Jobs/GracePeriodEndJob.cs` | After line 16 (`subscription.Status = SubscriptionStatus.Cancelled;`) — see §2.4.2 for the "had paid" check |
| `Paused` | `SuspendStudioHandler` | `Studios/Commands/SuspendStudioCommand.cs` | After line 36 (`await db.SaveChangesAsync(ct);` — no, **before** it, same transaction), guarded on `studio.Subscription is not null` |
| `Resumed` | `UnsuspendStudioHandler` | `Studios/Commands/UnsuspendStudioCommand.cs` | Same shape, before `await db.SaveChangesAsync(ct);` at line 36 |

#### 2.4.1 `HandleSubscriptionUpdatedHandler` — precise guard logic

This handler fires on *every* `customer.subscription.updated` webhook, including harmless
resends and out-of-band Stripe Dashboard edits — it must not double-write against
`ChangePlanHandler`'s own synchronous `Expansion` write for an immediate upgrade. Extend the
handler (`HandleSubscriptionUpdatedCommand.cs`) like this:

```csharp
public async Task Handle(HandleSubscriptionUpdatedCommand command, CancellationToken ct)
{
    Domain.Entities.Subscription? subscription = await db.Subscriptions
        .FirstOrDefaultAsync(s => s.StripeSubscriptionId == command.StripeSubscriptionId, ct);
    if (subscription is null) return;

    SubscriptionStatus previousStatus = subscription.Status;
    decimal mrrBefore = MrrRules.MonthlyEquivalent(subscription);
    bool pendingWasSet = subscription.PendingPlanId is not null;

    // ... existing status/price/billed-amount mutation logic, UNCHANGED ...
    // (the existing PendingPlanId-clearing block at lines 66-71 already tells us
    // definitively whether THIS call is the moment a scheduled change landed)

    bool pendingLanded = pendingWasSet && subscription.PendingPlanId is null;
    decimal mrrAfter = MrrRules.MonthlyEquivalent(subscription);

    if (subscription.Status == SubscriptionStatus.PastDue && previousStatus != SubscriptionStatus.PastDue)
        RevenueEventRecorder.Record(db, subscription, mrrBefore, mrrAfter, RevenueEventType.PastDue, nameof(HandleSubscriptionUpdatedHandler), command.StripeEventId);
    else if (previousStatus == SubscriptionStatus.PastDue && subscription.Status == SubscriptionStatus.Active)
        RevenueEventRecorder.Record(db, subscription, mrrBefore, mrrAfter, RevenueEventType.Recovered, nameof(HandleSubscriptionUpdatedHandler), command.StripeEventId);
    else if (pendingLanded && mrrBefore != mrrAfter)
        // A landed scheduled change is always a downgrade in practice (ChangePlanHandler only
        // ever schedules downgrades — upgrades apply immediately), but compare amounts rather
        // than assume the direction, in case a list-price edit between scheduling and landing
        // flipped it.
        RevenueEventRecorder.Record(db, subscription, mrrBefore, mrrAfter,
            mrrAfter > mrrBefore ? RevenueEventType.Expansion : RevenueEventType.Contraction,
            nameof(HandleSubscriptionUpdatedHandler), command.StripeEventId);
    // Any OTHER active-status amount change (e.g. a bare Stripe Dashboard price edit with no
    // pending change and no status transition) deliberately writes NO event — see §3's flag.

    await db.SaveChangesAsync(ct);
}
```

Extend `HandleSubscriptionUpdatedCommand`'s record with `string? StripeEventId` and thread
`stripeEvent.Id` through from `BillingEndpoints.cs`'s `customer.subscription.updated` case
(`BillingEndpoints.cs:213-228` — the already-in-scope `stripeEvent.Id` from the outer
`switch`, same object `HandleBillingWebhook` already parsed at line 170).

#### 2.4.2 `GracePeriodEndJob` — "only if the studio had paid"

`GracePeriodEndJob.cs` today (lines 9-18) transitions `GracePeriod → Cancelled` for a
subscription that never converted from trial — this must NOT churn (nothing was ever
billing). Guard on whether any `SubscriptionInvoicePayment` row exists for the subscription:

```csharp
public async Task ExecuteAsync(Guid studioId, CancellationToken ct = default)
{
    var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.StudioId == studioId, ct);
    if (subscription is null || subscription.Status != SubscriptionStatus.GracePeriod) return;

    decimal mrrBefore = MrrRules.MonthlyEquivalent(subscription);
    subscription.Status = SubscriptionStatus.Cancelled;

    bool hadPaid = await db.SubscriptionInvoicePayments.AnyAsync(p => p.SubscriptionId == subscription.Id, ct);
    if (hadPaid)
        RevenueEventRecorder.Record(db, subscription, mrrBefore, 0m, RevenueEventType.Churn, nameof(GracePeriodEndJob), null);

    await db.SaveChangesAsync(ct);
}
```

`GracePeriodEndJob` needs `IAppDbContext`/`AppDbContext` access to
`SubscriptionInvoicePayments` — it already has `AppDbContext db` injected (constructor,
line 7), just add the query.

#### 2.4.3 `New` vs `Reactivation` — the one true test

All three activation handlers need to distinguish these. Don't infer from `Status` alone
(a fresh `Subscription` row and a churned-then-reactivating one can look identical on
`Status` at the moment of activation). Use the ledger itself as the source of truth:

```csharp
bool everBilled = await db.SubscriptionRevenueEvents.AnyAsync(e => e.SubscriptionId == subscription.Id, ct);
RevenueEventType type = everBilled ? RevenueEventType.Reactivation : RevenueEventType.New;
RevenueEventRecorder.Record(db, subscription, mrrBefore, mrrAfter, type, nameof(ActivateCheckoutSubscriptionHandler), null);
```

(Substitute the correct `nameof(...)` per handler.) `ActivateSubscriptionManuallyHandler`'s
brand-new-`Subscription`-row branch (line 52-67) can skip the query and hardcode `New` — a
`Subscription` row that didn't exist a moment ago cannot have prior ledger rows.

### 2.5 `GetMrrHistoryQuery` — ledger for covered months, estimate before

```csharp
DateTime? ledgerStart = await db.SubscriptionRevenueEvents.MinAsync(e => (DateTime?)e.OccurredAt, ct);
```

For each of the requested months' end-points (`GetMrrHistoryHandler.cs:34-43`, unchanged
loop structure): if `ledgerStart is DateTime start && t >= start`, compute MRR via
`RevenueLedgerRules.MrrAt(allEvents, t)` (load all events once, same "load once, reuse across
the loop" style `MrrInputLoader` already established) and `IsEstimated = false`; otherwise
keep Batch 2's `MrrRules.MrrAt(inputs, t, now)` reconstruction with `IsEstimated = true`.

Add `bool IsEstimated` to `MrrDataPointResponse` (`Pena_e_Arte.Contracts/Responses/MrrDataPointResponse.cs`,
currently `record MrrDataPointResponse(string Month, decimal Mrr);`) and the frontend
`MrrDataPoint` interface (`platform.types.ts:62-65`).

### 2.6 New MRR-movements query

```csharp
public record GetMrrMovementsQuery(int Months = 12) : IRequest<List<MrrMovementsDataPointResponse>>;
```

Handler: load all events once (same pattern), bucket by calendar month of `OccurredAt`, call
`RevenueLedgerRules.MovementsFor` per bucket. `MrrMovementsDataPointResponse(string Month,
decimal New, decimal Expansion, decimal Reactivation, decimal Contraction, decimal Churn,
decimal Net)` in `Pena_e_Arte.Contracts/Responses/`. Months before `ledgerStart` return all
zeros (no movement data reconstructable pre-ledger — this chart has no estimated-mode
fallback, unlike the MRR trend chart; a month with genuinely zero rows and a month with no
ledger coverage look the same, which is acceptable since the whole platform pre-dates the
ledger by design, per B4).

### 2.7 GRR/NRR KPI tiles

```csharp
public record GetRevenueRetentionQuery : IRequest<RevenueRetentionResponse>;
public record RevenueRetentionResponse(double? GrossRevenueRetention, double? NetRevenueRetention, decimal StartMrr);
```

Handler: `DateTime monthStart = <current calendar month start>`; `startMrr =
RevenueLedgerRules.MrrAt(events, monthStart.AddTicks(-1))` (end of previous month — B3:
"existing customers only, new and reactivation excluded" is already satisfied structurally,
since `MrrAt` at *that* instant naturally excludes anything whose first event is this
month). `MrrMovementTotals thisMonth = RevenueLedgerRules.MovementsFor(events this month)`.

```csharp
double? Grr = startMrr == 0 ? null : (double)((startMrr + thisMonth.Contraction + thisMonth.Churn) / startMrr);
double? Nrr = startMrr == 0 ? null : (double)((startMrr + thisMonth.Expansion + thisMonth.Contraction + thisMonth.Churn) / startMrr);
```

(`Contraction`/`Churn` are already negative per §2.3 — adding them subtracts, matching B3's
"start − contraction − churn" written as addition of already-signed values, same convention
`MrrRules`' own `mrrGrowthPercent` uses elsewhere in this codebase.)

---

## 3. Flag, don't decide

- **Bare Stripe Dashboard price edits produce no ledger event.** §2.4.1's guard
  deliberately only fires `Expansion`/`Contraction` on a *landed scheduled change*. An admin
  manually editing a live subscription's price directly in the Stripe Dashboard (bypassing
  `ChangePlanHandler` entirely) will update `BilledUnitAmount` via the webhook's existing
  snapshot logic (Batch 2b) but leave no ledger trail. This is an accepted gap — out-of-band
  Stripe edits are already an edge case this codebase doesn't fully model elsewhere; note it
  in your report rather than adding speculative detection.
- **`ChangePlanHandler`'s `Expansion` event uses the new plan's *list* price, not a
  Stripe-verified amount** (`BilledUnitAmount` isn't refreshed until the following webhook,
  per Batch 2b's async snapshot design — see §2.4's table). In the near-certain case that
  `PlanPrice.Price` matches what Stripe actually charges (G3's existing invariant), this is
  exact; if it ever isn't, the ledger's `MrrAfter` for that one event is off with no later
  correction (the subsequent webhook is deliberately a no-op per §2.4.1, to avoid a
  double-write). Accept this for now — flag it, don't add an extra synchronous
  `GetPriceAsync` round-trip to the upgrade path to close a gap that shouldn't occur given
  G3's existing guarantee.
- **KPI-tile month.** B3 says "KPI tiles (monthly)" without naming which month. This design
  uses the current calendar month so far, matching every other "this month" figure already
  on the dashboard (`newStudiosThisMonth`, `discountsThisMonth`, Batch 3a's
  `refundsThisMonth`). Flag if Finance actually wants *last full completed month* instead —
  easy to change, not worth guessing wrong silently.
- **`RevenueEventRecorder`'s synthetic idempotency key for non-webhook sources** (§2.2) is
  coarse by design (subscription+type+instant). If a future batch ever needs two distinct
  real events of the same type for the same subscription within the same UTC tick (not
  possible today — verify this remains true if any future change adds bulk/batch
  subscription operations), the unique index would incorrectly reject the second one. Note
  this constraint in the architecture.md entry (§10) so a future author sees it.

---

## 4. Scope boundary — do not touch

- Batch 3a's refund calculator, `SubscriptionRefund` table, admin override UI — merged
  prerequisite, only touched here to add the one `Churn` event write into its two cancel
  handlers.
- `MrrRules.MonthlyEquivalent`/`BillingWindow`/`AtRiskMrr`/`ScheduledChurnMrr`/`PausedMrr` —
  unchanged; `GetPlatformStatsHandler`'s headline MRR figure keeps using `MrrRules` (the
  "now" figure), not the ledger — only *history* moves to the ledger. Do not swap the
  headline `Mrr`/`AtRiskMrr`/etc. fields in `PlatformStatsResponse` over to ledger-based
  reads; §2.3's equality test is what proves they'd agree anyway, not a reason to replace one
  with the other.
- VAT/tax, automatic cancellation after long suspension, the `/billing/plans` data leak —
  explicitly out of scope per the spec's own "Out of scope" section.

---

## 5. Phase A — Schema

Implement §2.1 in full: `SubscriptionRevenueEvent` entity + `RevenueEventType` enum +
configuration, the three-file `DbSet` addition. One migration,
`AddSubscriptionRevenueLedger`.

**Tests:** none needed beyond `dotnet ef database update` applying cleanly — schema-only
phase.

Commit: `feat: add SubscriptionRevenueEvent ledger table (Phase A)`

---

## 6. Phase B — Writer, ledger rules, and every write site

Implement §2.2 (`RevenueEventRecorder`), §2.3 (`RevenueLedgerRules`/`MrrMovementTotals`), and
every row of §2.4's table including §2.4.1–§2.4.3's precise guards.

**Tests** — new `tests/Pena_e_Arte.UnitTests/Platform/Revenue/RevenueLedgerRulesTests.cs`
and `RevenueEventRecorderTests.cs`:

| Scenario | Expect |
|---|---|
| `Record` called with `mrrBefore == mrrAfter`, type `Expansion` | No row added |
| `Record` called with `mrrBefore == mrrAfter`, type `PastDue` | Row added (state-only type) |
| `MrrAt` with a `Churn` event as the latest for a subscription | Excluded (0 contribution) |
| `MrrAt` with a `Paused` event as the latest | Excluded, even though `MrrAfter > 0` |
| `MrrAt` with a `Resumed` event as the latest | Included, uses `MrrAfter` |
| `MovementsFor` a month with one of each of the five movement types | Matches B5's worked example exactly (Growth monthly Feb→May scenario) |

Then extend each handler's existing test file (`ActivateCheckoutSubscriptionHandlerTests`,
`CreateSubscriptionHandlerTests`, `ActivateSubscriptionManuallyHandlerTests`,
`HandleSubscriptionUpdatedHandlerTests`, `ChangePlanHandlerTests`,
`HandleSubscriptionDeletedHandlerTests`, Batch 3a's `CancelSubscriptionHandlerTests`/
`CancelMySubscriptionHandlerTests`, `GracePeriodEndJobTests`,
`SuspendUnsuspendStudioHandlerTests`) per B5's acceptance table:

| Scenario | Expect |
|---|---|
| Growth monthly from 1 Feb; upgrade to Premium monthly 10 Mar; downgrade back to Growth lands 10 May | `New` 59 (Feb); `Expansion` 59→79 (Mar); `Contraction` 79→59 (May); end-Feb 59, end-Mar 79, end-Apr 79, end-May 59 |
| Growth yearly → cancelled with refund 11 Mar | `Churn` 49.17→0 on 11 Mar; end-Mar MRR excludes it |
| Suspended 12 May, unsuspended 3 Jun | `Paused` then `Resumed`; end-May MRR excludes it; end-Jun includes it |
| Renewal fails 5 Jul, paid 9 Jul | `PastDue` then `Recovered`; at-risk between |
| Same webhook delivered twice | One event (`StripeEventId` unique) |
| Immediate upgrade followed by its own `subscription.updated` webhook echo | Exactly one `Expansion` event (from `ChangePlanHandler`), not two |
| GracePeriod expiry with no prior payment | No `Churn` event |
| GracePeriod expiry after at least one paid invoice | One `Churn` event |
| Reactivation (studio churned once, resubscribes) | `Reactivation`, not `New` |

Commit: `feat: revenue-event writer and every MRR-movement write site (Phase B)`

---

## 7. Phase C — Ledger/reconstruction equality + `GetMrrHistoryQuery`

Implement §2.5. Add the equality test B3 explicitly asks for:

```csharp
[Fact]
public async Task LedgerMrrAtNow_EqualsMrrRulesMrrAtNow_ToTheCent()
```

Seed a mixed scenario (a few Active/PastDue/paused/cancelled subscriptions with full ledger
histories matching their current state) and assert `RevenueLedgerRules.MrrAt(events, now) ==
MrrRules.MrrAt(inputs, now, now)` exactly.

**Tests:** `GetMrrHistoryHandlerTests.cs` — months before `ledgerStart` have
`IsEstimated=true` and match Batch 2's existing reconstruction values (regression-check
against the current passing tests); months on/after have `IsEstimated=false` and match
ledger totals; empty ledger → every month `IsEstimated=true` (unchanged from today's
behaviour).

Commit: `feat: GetMrrHistoryQuery reads the ledger for covered months (Phase C)`

---

## 8. Phase D — MRR movements query + GRR/NRR + frontend

Implement §2.6, §2.7, and the platform endpoint additions
(`PlatformEndpoints.cs`, next to `mrr-history`):

```csharp
group.MapGet("mrr-movements", GetMrrMovements);
group.MapGet("revenue-retention", GetRevenueRetention);
```

### Frontend

- `platformApi.ts`: `getMrrMovements: builder.query<MrrMovementsDataPoint[], number |
  void>({ query: (months) => \`platform/mrr-movements${months ? \`?months=${months}\` :
  ""}\` })` (mirror `getMrrHistory`'s existing shape, `platformApi.ts:35`) and
  `getRevenueRetention: builder.query<RevenueRetentionResponse, void>({ query: () =>
  "platform/revenue-retention" })`.
- New `MrrMovementsChart.tsx` (`frontend/src/features/platform/components/`, next to
  `MrrChart.tsx`) — a stacked-bar chart, hand-rolled inline SVG matching `MrrChart.tsx`'s
  existing style (viewBox/PAD constants, `currentColor`/`hsl(var(--primary))` theming, no
  charting library — this codebase has none and Batch 2's chart didn't introduce one). Five
  segments per month bar: New/Expansion/Reactivation stacked upward in shades of the success
  accent, Contraction/Churn stacked downward in shades of the danger accent, plus a net-line
  overlay (polyline, same technique `MrrChart.tsx` already uses for its single line). Mount
  it in `AdminDashboardPage.tsx` directly below `<MrrChart />` (`AdminDashboardPage.tsx:350`).
- `MrrChart.tsx` changes: shade/hatch points where `d.isEstimated` is true (a distinct dot
  style or reduced-opacity segment of the line — your call on exact rendering, keep it
  legible in both light/dark per this codebase's existing theming convention). Replace the
  unconditional caption (`MrrChart.tsx:201-203`, "Past months are estimated from current
  subscriptions…") with one that only appears when at least one visible point
  `isEstimated === true`; when every visible point is recorded, show nothing (or a neutral
  "All figures recorded" note — your call, note the choice in your report).
- Two new KPI tiles, "Row 4 — retention" in `AdminDashboardPage.tsx` (after the existing Row
  3, `AdminDashboardPage.tsx:317-345`): Gross Revenue Retention and Net Revenue Retention,
  `formatPercent`-styled (reuse `formatPercent`, `AdminDashboardPage.tsx:195-197`), showing
  "—" when the query returns `null` (§2.7's `startMrr == 0` case).

Run `pnpm test` and `pnpm lint` after.

**Tests:** `GetMrrMovementsHandlerTests.cs`, `GetRevenueRetentionHandlerTests.cs` — cover
B5's Feb–May Growth/Premium scenario end-to-end (bar totals per month, net line, GRR/NRR for
at least one month with a contraction and one with none).

Commit: `feat: MRR movements chart, GRR/NRR tiles (Phase D)`

---

## 9. Phase E — Backfill

New `BackfillRevenueLedgerCommand` (`AdminOnly`, MediatR command +
`POST /api/v1/platform/subscriptions/backfill-revenue-ledger` endpoint, same
`PlatformEndpoints.cs` group as Batch 2b's backfill-billed-amounts endpoint). Per B4: for
every currently-Active, contracted-MRR-above-zero subscription with **no existing
`SubscriptionRevenueEvent` row**, write one `New` event at
`MrrRules.BillingWindow(input, now)?.Start` (the same conservative reconstruction start
Batch 2 already computes — reuse `MrrInputLoader.LoadAsync` to get `SubscriptionRevenueInput`
per subscription), `MrrAfter = MrrRules.MonthlyEquivalent(subscription)`, `Source =
"Backfill"`, `StripeEventId = null` (the synthetic key from §2.2 covers idempotency —
re-running the backfill is a no-op for already-backfilled subscriptions since they now have
a ledger row, satisfying the "no existing row" guard directly, not just the unique index).
Idempotent on rerun by construction.

**Tests:** `BackfillRevenueLedgerHandlerTests.cs` — one Active subscription with no ledger
row gets exactly one `New` event; a subscription that already has any ledger row is
untouched; a Cancelled/Trialing/zero-MRR subscription gets no event; rerun is a no-op.

Commit: `feat: revenue-ledger backfill command (Phase E)`

---

## 10. Phase F — Architecture docs

`architecture.md`: new Decisions Log entry, "Subscription revenue ledger (2026-09-24)" —
summarize §2.1–§2.7, the `Churn`-vs-`Paused`/`PastDue` `MrrAfter` convention (§2.1, worth
restating plainly since it's the one non-obvious modeling choice here), the §2.4.1 guard
that prevents `ChangePlanHandler`/webhook-echo double-writes, and the §3 flags (bare
Dashboard edits produce no event; list-price approximation on immediate upgrades; the
synthetic-idempotency-key constraint). Confirm no new `IgnoreQueryFilters()` registry row is
needed and say so explicitly.

No Help Menu/manual changes — this is an admin-only feature (the new chart/tiles live on
`/platform`, not any owner/artist/client surface), and CLAUDE.md rule #7 concerns
user-facing features. If this project's Help content has a platform-admin-facing section
covering the MRR chart already (check `helpContent.ts` for an existing "admin-dashboard"
article, referenced by Batch 2b's Phase E), add one line there noting recorded vs. estimated
months and the new movements chart — small enough to include in the same commit without
delaying the batch.

Commit: `docs: revenue ledger decisions log entry (Phase F)`

---

## 11. CLAUDE.md rule #7 note

Unlike Batch 3a, this batch does **not** need a rule-7 exception — nothing here is a
client/artist/owner-facing feature. The one Help touch (§10, admin-dashboard article) ships
in the same change per the rule's normal cadence.

---

## 12. Industry-standard benchmark note (CLAUDE.md rule #6)

An append-only revenue-movement ledger with a `New`/`Expansion`/`Contraction`/`Churn`/
`Reactivation` taxonomy and month-over-month GRR/NRR derived from it is exactly how
ChartMogul and Baremetrics (and Stripe's own Billing analytics, at the API level) model
subscription revenue — computing MRR from *event history* rather than *current-state
reconstruction* is the standard this whole batch closes the gap on, the same category of fix
Batch 2b made for per-subscription billed amounts vs. the price list.

---

## 13. Final verification checklist

- [ ] Every write site in §2.4's table fires exactly once per real transition, zero times on
      a no-op resend.
- [ ] `ChangePlanHandler`'s immediate upgrade produces exactly one `Expansion` event, not two.
- [ ] `RevenueLedgerRules.MrrAt(events, now)` equals `MrrRules.MrrAt(inputs, now, now)`
      exactly, for a mixed-state seed.
- [ ] B5's full acceptance table passes.
- [ ] `GetMrrHistoryQuery` correctly splits estimated/recorded months at `ledgerStart`.
- [ ] MRR movements chart renders B5's Feb–May scenario with the right stacked segments and
      net line.
- [ ] GRR/NRR show "—" when start MRR is 0, correct percentages otherwise.
- [ ] Backfill is idempotent, only touches currently-paid subscriptions with no prior ledger
      row.
- [ ] `dotnet test`, `pnpm test`, `pnpm lint` all green.
- [ ] Six commits on `feature/revenue-ledger` (Phases A–F).

---

## 14. Final deliverable spec

Six commits (Phases A–F). Final report: changes per phase with file list; drift from this
file; the §3 flags verbatim (bare Dashboard edits, list-price approximation on immediate
upgrades, KPI-tile month choice, synthetic-idempotency-key constraint); confirmation of the
`RevenueLedgerRules.MrrAt` == `MrrRules.MrrAt` equality test result; and — since this is the
spec's final batch — a short note on what, if anything, from the original spec
(`spec-yearly-refunds-and-revenue-ledger-2026-09-23.md`) remains unimplemented across both
Batch 3a and this batch.
