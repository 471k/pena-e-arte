# Overnight Prompt — Store the Billed Amount on Each Subscription (Batch 2b)

> Feed this file directly to Claude Code as the task prompt. It is self-contained: exact
> files, exact current code (re-read from live source on 2026-09-23, on top of Batch 2's
> merged commit `8723098b`), exact target behaviour, exact tests, exact docs to sync. Read
> the whole file before writing anything.

**Date logged:** 2026-09-23
**Requested by:** Phi (Finance project, via Engineering Consultation on
`docs/claude/spec-mrr-billed-amount-snapshot-2026-09-23.md`)
**Origin:** That spec left schema/migration/webhook design open ("Status: Financial
requirements final; implementation design open"). Section 2 below is the resulting design —
treat it as decided, not as a proposal to re-litigate. R1–R7 numbers below refer to that
spec's own section 2.
**Prerequisite:** Batch 1 (`overnight-prompt-plan-tiers-yearly-2026-09-23.md`) and Batch 2
(`overnight-prompt-mrr-reporting-fixes-2026-09-23.md`) merged. Batch 2 introduced `MrrRules`
(`Pena_e_Arte.Application/Platform/Revenue/MrrRules.cs`) — the one place MRR is computed —
and `MrrInputLoader`; this batch changes their input, not their status/window logic.
**Mode:** Fully autonomous, no user present. Do not stop to ask questions — open product
questions are in §3 with defaults. Where this file states a fact about current code it was
re-read on 2026-09-23; if your checkout disagrees, trust your checkout, note the drift in
your final report, and do not silently change approach.

**Before starting**, run:

```bash
git add -A && git commit -m "checkpoint: before MRR billed-amount snapshot" --allow-empty
git checkout -b feature/mrr-billed-amount-snapshot
```

Commit at the end of each phase (§6–§10).

---

## 1. Goal

Every MRR figure currently reads `Plan.Prices.FirstOrDefault(pp => pp.Interval ==
s.BillingInterval).Price` at query time — the price list for *new* sales, not what an
existing studio is actually billed. Give each `Subscription` a snapshot of what Stripe (or,
for cash, the platform) actually bills it, make `MrrRules.MonthlyEquivalent` read that
snapshot first, keep every studio's snapshot current on every billing-relevant event, add a
"Discounts this month" figure from real paid-invoice data, and relax Batch 2's G2 guard now
that changing a `PlanPrice` no longer changes existing subscribers' reported revenue.

No studio is subscribed to a paid plan today (confirmed in Batch 2's report), so the backfill
(§9) only touches demo/test data. Cheapest to ship before the first real subscriber.

Applicable `CLAUDE.md` rules: #1 (tenant isolation — the new entity is deliberately
unfiltered, same class as `Subscription` itself; see §6.2), #3 (no PII in logs — fallback/
exclusion counts are numbers only), #5 (Serilog only), #6 (benchmark — §11), #7 (Help sync —
§10, same branch).

---

## 2. Decisions made (Engineering Consultation) — implement as specified

### 2.1 Schema — one migration, two changes

**A. Five new nullable columns on `Subscription`** (R1). No new column for "billed interval" —
reuse `Subscription.BillingInterval` as the spec says.

```csharp
public decimal? BilledUnitAmount { get; set; }      // decimal(10,2)
public int? BilledQuantity { get; set; }            // always 1 today (per-studio pricing)
public string? BilledCurrency { get; set; }          // varchar(3), ISO 4217 lowercase ("eur")
public decimal? RecurringDiscountPercent { get; set; } // decimal(5,2); null except a `forever` coupon
```

Null until populated (R1). `SubscriptionConfiguration.cs` gets the column precisions; no
index needed (never queried on their own).

**B. New entity `SubscriptionInvoicePayment`** (R3's "per-invoice record" — Finance's stated
preference, and reusable by Batch 3's refund calc and revenue ledger):

```csharp
namespace Pena_e_Arte.Domain.Entities;

public class SubscriptionInvoicePayment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SubscriptionId { get; set; }
    public Guid StudioId { get; set; }              // denormalized — same convenience as Subscription.StudioId
    public string StripeInvoiceId { get; set; } = string.Empty;
    public decimal AmountPaid { get; set; }
    public decimal DiscountAmount { get; set; }      // coupon total_discount_amounts + any balance credit consumed
    public string Currency { get; set; } = string.Empty;
    public DateTime PaidAt { get; set; }             // when Stripe actually paid it — NOT the period it covers
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public Subscription Subscription { get; set; } = null!;
}
```

Deliberately **not** a `TenantEntity` — same reasoning as `Subscription` itself (no
`HasQueryFilter`, admin needs a cross-tenant read for the monthly aggregate, same "no filter
to bypass" class as `Subscription`/`PastDueReminderJob`). No new `IgnoreQueryFilters()`
registry row is needed because none is called — reads of this table go through
`db.SubscriptionInvoicePayments` directly, same as `db.Subscriptions` today.

`SubscriptionInvoicePaymentConfiguration.cs` (mirror `SubscriptionConfiguration.cs`'s style):

```csharp
builder.ToTable("subscription_invoice_payments");
builder.HasKey(p => p.Id).HasName("pk_subscription_invoice_payments");
builder.HasIndex(p => p.StripeInvoiceId).IsUnique().HasDatabaseName("ix_subscription_invoice_payments_stripe_invoice_id");
builder.HasIndex(p => p.StudioId).HasDatabaseName("ix_subscription_invoice_payments_studio_id");
builder.HasIndex(p => p.PaidAt).HasDatabaseName("ix_subscription_invoice_payments_paid_at");
builder.HasOne(p => p.Subscription).WithMany().HasForeignKey(p => p.SubscriptionId)
       .HasConstraintName("fk_subscription_invoice_payments_subscriptions").OnDelete(DeleteBehavior.Cascade);
```

The unique index on `StripeInvoiceId` is the idempotency mechanism — Stripe's at-least-once
webhook delivery must not double-count a paid invoice (see §8.1).

Add `DbSet<SubscriptionInvoicePayment> SubscriptionInvoicePayments { get; }` to
`IAppDbContext` and `AppDbContext`/`FakeDbContext`.

Migration name: `AddSubscriptionBilledAmountSnapshot`
(`dotnet ef migrations add AddSubscriptionBilledAmountSnapshot --project Pena_e_Arte.Infrastructure`).

### 2.2 `MrrRules.MonthlyEquivalent` (R2)

```csharp
public const string PlatformCurrency = "eur"; // single source — StripeDemoSeeder and cash-billed snapshots both use this

public static decimal MonthlyEquivalent(Subscription s)
{
    if (s.BilledUnitAmount is decimal billed)
    {
        if (s.BilledCurrency is string currency && currency != PlatformCurrency)
            return 0m; // R7 — never convert; caller logs the exclusion (see §7.2)

        decimal discountFactor = 1m - (s.RecurringDiscountPercent ?? 0m) / 100m;
        decimal monthlyAmount = billed * (s.BilledQuantity ?? 1) * discountFactor;
        return s.BillingInterval == BillingInterval.Monthly ? monthlyAmount : monthlyAmount / 12m;
    }

    // Fallback — pre-backfill or a snapshot write was missed. Same PlanPrice lookup Batch 2 shipped.
    return s.Plan?.Prices.FirstOrDefault(pp => pp.Interval == s.BillingInterval) is PlanPrice pp
        ? (pp.Interval == BillingInterval.Monthly ? pp.Price : pp.Price / 12m)
        : 0m;
}
```

Stays pure/DB-free (no logging inside `MrrRules` itself — see §7.2 for where the fallback/
exclusion *counts* get logged).

### 2.3 Snapshot on every billing-change path (R4)

Populate/refresh `BilledUnitAmount`/`BilledQuantity`/`BilledCurrency` (never
`RecurringDiscountPercent` unless noted) at every site in the table below. Exact current code
at each site is in §6.

| Path | File | Source of the snapshot |
|---|---|---|
| Checkout completed/finalize | `ActivateCheckoutSubscriptionCommand.cs` | `IStripeBillingService.GetPriceAsync(result.PriceId)` — already exists (Batch 2, used by G3) |
| Direct create (Free/cash/first card sub) | `CreateSubscriptionCommand.cs` | The already-loaded, G3-validated `PlanPrice price` — no extra Stripe call needed |
| `customer.subscription.updated` (**every** event, not only on price-id change) | `HandleSubscriptionUpdatedCommand.cs` via `BillingEndpoints.HandleBillingWebhook` | The webhook payload's `sub.Items.Data[0].Price` + `sub.Discounts` |
| Cash activation by admin | `ActivateSubscriptionManuallyCommand.cs` | The Monthly `PlanPrice.Price` on the plan being activated |
| Demo provisioning | `StripeDemoSeeder.cs` | The just-created `stripeSub.Items.Data[0].Price` |

`HandleSubscriptionDeletedCommand.cs` needs no change (status → Cancelled; Batch 2's window
rules already exclude it).

### 2.4 Discounts this month (R3)

`HandleInvoicePaidCommand` upserts a `SubscriptionInvoicePayment` row (idempotent on
`StripeInvoiceId`) from the `invoice.paid` webhook payload. `GetPlatformStatsHandler` sums
`DiscountAmount` for rows with `PaidAt` inside the current calendar month into a new
`PlatformStatsResponse.DiscountsThisMonth` field, shown as an addition to the existing MRR
card subtitle (no new KPI card — the three-per-row grid is full).

### 2.5 G2 relaxation (R5)

Batch 2's `UpdatePlanHandler` G2 unconditionally rejects a `Price`/`StripePriceId` change on a
`PlanPrice` with subscribers. Change the condition: reject **only if any of that price's
current or pending subscribers still has `BilledUnitAmount == null`** (not yet snapshotted —
changing the row would still silently change what the fallback path reports for them). Once
every affected subscription has a snapshot, allow the change — updating `PlanPrice` no longer
touches Stripe (`UpdatePlanHandler` never calls `IStripeBillingService`), so an existing
subscriber's Stripe subscription keeps billing at its original Stripe price regardless of what
the `PlanPrice` row says afterward; only new sales (`CreateSubscriptionCommand`/
`ChangePlanCommand`) read the changed row. G1, G3, G4 are unchanged.

### 2.6 Backfill (R6)

New `BackfillSubscriptionBilledAmountsCommand` (`AdminOnly`, MediatR command +
`POST /api/v1/platform/subscriptions/backfill-billed-amounts` endpoint), not an automatic
startup step (an unconditional Stripe call per subscription on every boot isn't the kind of
thing that belongs unguarded in `Program.cs`). For each `Subscription` with a
`StripeSubscriptionId` and `BilledUnitAmount == null`: call
`IStripeBillingService`'s existing subscription-lookup path (reuse the same
`SubscriptionService.GetAsync` pattern `ChangeSubscriptionPriceAsync` already uses) and
write the snapshot from its price item. For cash-billed (`StripeSubscriptionId == null`) with
`BilledUnitAmount == null`: snapshot the current Monthly `PlanPrice.Price`, and return them in
the response body as `CashBilledSnapshots: [{StudioId, Price}]` for the admin to see/confirm
(R6 says "list them for the admin to confirm" — a response list satisfies that; no separate
confirmation step is required since nothing is billed by this action). Idempotent — a second
run only touches rows still null.

### 2.7 Currency (R7)

Only `MrrRules.MonthlyEquivalent` needs the exclusion (§2.2). Log the excluded/fallback counts
where the queries already have a logger — see §7.2.

---

## 3. Flag, don't decide

- **`RecurringDiscountPercent` population.** No `forever`-duration coupon exists anywhere in
  `StripeDiscountService.cs` today (confirmed: only `"repeating"`/1-month and `"once"` are
  used). Leave this field write-only-when-applicable and always null in practice for now —
  do not invent a forever-coupon path to exercise it.
- **Tax/VAT** (spec §4): confirm whether `PriceCreateOptions`/existing Stripe prices are
  tax-exclusive `unit_amount` today. If Stripe Tax is not enabled anywhere in this codebase
  (check `Stripe:` config and `StripeDemoSeeder.cs`), state that explicitly in the report
  rather than guessing.
- **`SubscriptionInvoicePayment.DiscountAmount` exact composition** (coupon
  `total_discount_amounts` vs. a customer-balance credit consumed on that invoice, or both
  summed) — verify against a real Stripe test-mode invoice payload during implementation
  (property names given in §8.1 are based on Stripe.net 52.4.1's compiled shape, not a live
  payload) and note in the report which fields were actually present and used.
- **Backfill trigger surface** — a bare endpoint with no admin UI button is acceptable for
  this pass (zero paying studios today); note in the report whether a Plan Management page
  button was also wired up, and if not, that it's a fast-follow.

---

## 4. Scope boundary — do not touch

- `HandleSubscriptionDeletedCommand.cs` (no change needed, per §2.3).
- Batch 2's status/window rules (`BillingWindow`, `BillingAt`, `AtRiskMrr`,
  `ScheduledChurnMrr`, `PausedMrr`, `PayingStudios`) — only `MonthlyEquivalent`'s input
  changes.
- The yearly-refund flow and the revenue-event ledger — Batch 3. `SubscriptionInvoicePayment`
  is designed so Batch 3 can reuse it (one row per paid invoice), not to implement refunds now.
- The owner-facing `GET /api/v1/billing/plans` `StripePriceId`/`SubscriberCount` leak — a
  separate, already-flagged Engineering Consultation item (Batch 1's report).
- `ReferralRewardService.cs`/`StripeDiscountService.cs` internals — read for context (§2.4),
  not modified.

---

## 5. Phase A — Schema

Add the five `Subscription` columns (§2.1.A) and the new `SubscriptionInvoicePayment` entity +
configuration (§2.1.B). Update `IAppDbContext`, `AppDbContext`, `FakeDbContext`. One migration,
`AddSubscriptionBilledAmountSnapshot`. No data changes in this phase — every column stays
null until Phase B populates it going forward and Phase D backfills existing rows.

**Tests:** none needed beyond the migration applying cleanly against a real MySQL instance
(`dotnet ef database update`) — schema-only phase.

Commit: `feat: add BilledUnitAmount snapshot columns and SubscriptionInvoicePayment table (Phase A)`

---

## 6. Phase B — Snapshot on every billing-change path

Implement §2.3's table. For each site, also set `BilledQuantity = 1` and
`BilledCurrency = MrrRules.PlatformCurrency` (or the Stripe price's actual `Currency`, lower-
cased, where a live Stripe price is already being read — e.g. `ActivateCheckoutSubscriptionCommand`
and the webhook path — rather than hardcoding `"eur"` there).

### 6.1 `ActivateCheckoutSubscriptionCommand.cs` (~line 60)

Already loads `PlanPrice? price` by `result.PriceId`. Add, right after that lookup succeeds:

```csharp
if (result.PriceId is not null)
{
    StripePriceInfo? stripePrice = await billing.GetPriceAsync(result.PriceId, ct);
    if (stripePrice is not null)
    {
        subscription.BilledUnitAmount = (stripePrice.UnitAmount ?? 0) / 100m;
        subscription.BilledQuantity = 1;
        subscription.BilledCurrency = stripePrice.Currency;
    }
}
```

### 6.2 `CreateSubscriptionCommand.cs` (~line 107 card-billed branch, ~line 119 free/cash branch)

Both branches already have `price` (`PlanPrice`, G3-validated). After either branch sets
`periodEnd`:

```csharp
subscription.BilledUnitAmount = price.Price;
subscription.BilledQuantity = 1;
subscription.BilledCurrency = MrrRules.PlatformCurrency;
```

(Card-billed: `price.Price` already matches what Stripe was just told to charge — no extra
API call needed. Free: `price.Price` is 0, correctly snapshotting zero MRR.)

### 6.3 `HandleSubscriptionUpdatedCommand.cs` + `BillingEndpoints.HandleBillingWebhook`

Extend the command record:

```csharp
public record HandleSubscriptionUpdatedCommand(
    string StripeSubscriptionId,
    string StripeStatus,
    DateTime CurrentPeriodEnd,
    string? StripePriceId,
    long? UnitAmount,
    string? Currency,
    long? Quantity,
    decimal? RecurringDiscountPercent,
    bool CancelAtPeriodEnd = false) : IRequest;
```

In the handler, after the existing `PlanId`/`BillingInterval` update block, unconditionally
(every event, not gated on `command.StripePriceId is not null`):

```csharp
if (command.UnitAmount is long amount)
{
    subscription.BilledUnitAmount = amount / 100m;
    subscription.BilledQuantity = command.Quantity ?? 1;
    subscription.BilledCurrency = command.Currency;
}
subscription.RecurringDiscountPercent = command.RecurringDiscountPercent;
```

In `BillingEndpoints.cs`'s `customer.subscription.updated` case (~line 200), extract from the
same `sub` object already in scope:

```csharp
Stripe.SubscriptionItem? item = sub.Items?.Data?.FirstOrDefault();
decimal? recurringDiscountPercent = sub.Discounts?
    .Select(d => d.Coupon)
    .FirstOrDefault(c => c?.Duration == "forever" && c.PercentOff is not null)
    ?.PercentOff;

await mediator.Send(
    new HandleSubscriptionUpdatedCommand(
        sub.Id, sub.Status, periodEnd, item?.Price?.Id,
        item?.Price?.UnitAmount, item?.Price?.Currency, item?.Quantity,
        recurringDiscountPercent, sub.CancelAtPeriodEnd), ct);
```

Verify `Stripe.Subscription.Discounts` is the correct property name (vs. a single `Discount`)
against the installed Stripe.net version — Stripe's API has carried both shapes across
versions; use whichever this account's SDK actually exposes and note it in the report.

### 6.4 `ActivateSubscriptionManuallyCommand.cs`

The `Plan` query (~line 44) doesn't currently `.Include(p => p.Prices)`. Add it, then in both
the new-subscription and existing-subscription branches:

```csharp
PlanPrice? monthlyPrice = plan.Prices.FirstOrDefault(pp => pp.Interval == BillingInterval.Monthly);
// ... on studio.Subscription (new or existing):
studio.Subscription.BilledUnitAmount = monthlyPrice?.Price ?? 0m;
studio.Subscription.BilledQuantity = 1;
studio.Subscription.BilledCurrency = MrrRules.PlatformCurrency;
```

### 6.5 `StripeDemoSeeder.cs` (~line 116, after `sub.CurrentPeriodEnd` is set)

```csharp
Stripe.Price? billedPrice = stripeSub.Items?.Data?.FirstOrDefault()?.Price;
sub.BilledUnitAmount = (billedPrice?.UnitAmount ?? 0) / 100m;
sub.BilledQuantity = 1;
sub.BilledCurrency = billedPrice?.Currency ?? MrrRules.PlatformCurrency;
```

### 6.6 Tests

- `ActivateCheckoutSubscriptionHandlerTests`: snapshot populated from `GetPriceAsync` on
  activation; unaffected when `result.PriceId` is null (Free-via-checkout edge case, if one
  exists — check current tests for this path first).
- `CreateSubscriptionHandlerTests`: card-billed and Free-plan paths both snapshot
  `price.Price`/1/`"eur"`.
- `HandleSubscriptionUpdatedHandlerTests`: snapshot refreshed on a plain status-only event
  (no price-id change) — this is the regression Batch 2b exists to fix; `RecurringDiscountPercent`
  set/cleared correctly.
- `ActivateSubscriptionManuallyHandlerTests`: snapshot uses the plan's Monthly price.
- Extend `DataSeederPlanReconciliationTests`-adjacent or a new `StripeDemoSeederTests` if one
  exists; otherwise this path is only integration-testable against real Stripe test mode —
  note that in the report rather than fabricating a unit test with no real assertion value.

Commit: `feat: snapshot the billed amount on every subscription create/update/checkout/webhook path (Phase B)`

---

## 7. Phase C — MRR formula reads the snapshot

### 7.1 `MrrRules.MonthlyEquivalent`

Replace with §2.2's implementation. `MonthlyEquivalent` is called from `MrrAt`, `AtRiskMrr`,
`ScheduledChurnMrr`, `PausedMrr`, `PayingStudios` — all unchanged callers, only the function
body changes.

### 7.2 Fallback/exclusion logging (R2, R7)

`GetPlatformStatsHandler` and `GetMrrHistoryHandler` already have `List<SubscriptionRevenueInput>
inputs` from `MrrInputLoader`. After computing MRR figures, add (once per handler, not per
data point in the history loop):

```csharp
int fallbackCount = inputs.Count(i => i.Subscription.BilledUnitAmount is null && i.Subscription.PlanId is not null);
int currencyExcludedCount = inputs.Count(i =>
    i.Subscription.BilledCurrency is string c && c != MrrRules.PlatformCurrency);
if (fallbackCount > 0 || currencyExcludedCount > 0)
{
    logger.LogInformation(
        "MRR computed with {FallbackCount} subscription(s) on the pre-snapshot PlanPrice fallback " +
        "and {CurrencyExcludedCount} excluded for non-platform currency",
        fallbackCount, currencyExcludedCount);
}
```

Both handlers need an injected `ILogger<T>` — neither has one today; add it to the
constructor (mirrors every other handler's DI shape in this codebase).

### 7.3 Tests — extend `MrrRulesTests.cs`

| Scenario | Expect |
|---|---|
| `BilledUnitAmount` 79, code/admin `PlanPrice.Price` now 89 | MRR 79 (snapshot wins) |
| `BilledUnitAmount` 790, Yearly | MRR 65.8333… (full decimal precision, not rounded) |
| `BilledUnitAmount` null, `Plan.Prices` has 49 | Falls back to 49 (Batch 2 behaviour) |
| `BilledUnitAmount` 79, `RecurringDiscountPercent` null | MRR 79 (no discount applied — R3: temporary discounts never reduce MRR, and none set `RecurringDiscountPercent` today) |
| `BilledCurrency` "usd" on a "eur" platform | MRR 0 for that subscription |
| `BilledUnitAmount` 79, `BilledQuantity` null | Treated as 1 (MRR 79, not 0) |

`GetPlatformStatsHandlerTests`/`GetMrrHistoryHandlerTests`: one test per handler asserting the
new log line fires with the right counts on a mixed fallback/current/excluded-currency seed
(use a captured-logger pattern — see `CapturingLogger<T>` in
`tests/Pena_e_Arte.UnitTests/ConsentForms/GetConsentFormByIdHandlerTests.cs`, already reusable
across the test assembly).

Commit: `fix: MRR reads the billed-amount snapshot, falls back to the price list only pre-backfill (Phase C)`

---

## 8. Phase D — Discounts this month, backfill, G2 relaxation

### 8.1 `HandleInvoicePaidCommand.cs` + `BillingEndpoints.cs`

Extend the command to carry what's needed for the ledger row:

```csharp
public record HandleInvoicePaidCommand(
    string StripeSubscriptionId, DateTime PeriodEnd,
    string StripeInvoiceId, decimal AmountPaid, decimal DiscountAmount,
    string Currency, DateTime PaidAt) : IRequest;
```

In the handler, after the existing status/period-end update, upsert by `StripeInvoiceId`:

```csharp
bool alreadyRecorded = await db.SubscriptionInvoicePayments
    .AnyAsync(p => p.StripeInvoiceId == command.StripeInvoiceId, ct);
if (!alreadyRecorded)
{
    db.SubscriptionInvoicePayments.Add(new SubscriptionInvoicePayment
    {
        SubscriptionId = subscription.Id,
        StudioId = subscription.StudioId,
        StripeInvoiceId = command.StripeInvoiceId,
        AmountPaid = command.AmountPaid,
        DiscountAmount = command.DiscountAmount,
        Currency = command.Currency,
        PaidAt = command.PaidAt,
    });
}
```

In `BillingEndpoints.cs`'s `invoice.paid` case (~line 192), extract:

```csharp
decimal discountAmount = (invoice.TotalDiscountAmounts?.Sum(d => d.Amount) ?? 0) / 100m;
// A balance credit consumed on this invoice also counts as a discount (R3, the Yearly-referrer case):
decimal balanceCredit = invoice.StartingBalance < invoice.EndingBalance
    ? 0m
    : (invoice.StartingBalance - invoice.EndingBalance) / 100m;
await mediator.Send(new HandleInvoicePaidCommand(
    stripeSubId, invoice.PeriodEnd, invoice.Id,
    invoice.AmountPaid / 100m, discountAmount + balanceCredit,
    invoice.Currency, invoice.StatusTransitions?.PaidAt ?? DateTime.UtcNow), ct);
```

Verify the exact sign/semantics of `StartingBalance`/`EndingBalance` against a real test-mode
invoice where a customer-balance credit was actually consumed (§3) — Stripe balances are
negative-for-credit; get the arithmetic direction right from a real payload, not from this
description alone.

### 8.2 `GetPlatformStatsHandler` — `DiscountsThisMonth`

```csharp
DateTime monthStart = ...; // already computed
decimal discountsThisMonth = await db.SubscriptionInvoicePayments
    .Where(p => p.PaidAt >= monthStart)
    .SumAsync(p => p.DiscountAmount, ct);
```

Add `decimal DiscountsThisMonth` to `PlatformStatsResponse` (after `PausedMrr`) and
`platform.types.ts`. `AdminDashboardPage.tsx`'s `mrrSubtitle` helper (Batch 2) gains a third
clause: when `discountsThisMonth > 0`, append `· €X discounted this month`.

### 8.3 G2 relaxation — `UpdatePlanHandler.cs`

Change the G2 block (Batch 2's `changesRevenue` guard) from "reject if `subscriberCount > 0`"
to:

```csharp
if (changesRevenue)
{
    bool anyUnsnapshotted = await db.Subscriptions.AnyAsync(s =>
        ((s.PlanId == plan.Id && s.BillingInterval == interval)
         || (s.PendingPlanId == plan.Id && s.PendingBillingInterval == interval))
        && s.BilledUnitAmount == null, ct);
    if (anyUnsnapshotted)
        throw new BusinessRuleViolationException(
            $"{await CountSubscribersAsync(db, plan.Id, interval, ct)} subscribed studio(s) on this "
            + "price have not been billing-amount-snapshotted yet. Run the backfill "
            + "(POST /platform/subscriptions/backfill-billed-amounts) before changing it.");
}
```

G1/G3/G4 unchanged.

### 8.4 Backfill command (R6)

New `Pena_e_Arte.Application/Platform/Commands/BackfillSubscriptionBilledAmountsCommand.cs`
(`AdminOnly`) per §2.6. Endpoint under the existing `/api/v1/platform` group in whichever file
registers `GetPlatformSubscriptionsQuery`/`ExtendTrialCommand` today (check
`Pena_e_Arte.API/Endpoints/` for the platform-subscriptions endpoint file and add it there,
matching that file's existing route-group pattern).

### 8.5 Tests

| Test | Expect |
|---|---|
| `invoice.paid` webhook fires twice for the same invoice id | One `SubscriptionInvoicePayment` row (idempotent) |
| Invoice with a coupon discount paid this month | `DiscountsThisMonth` includes it |
| Invoice paid last month | Excluded from this month's figure |
| `UpdatePlanHandler`: change price, every subscriber snapshotted | Saved |
| `UpdatePlanHandler`: change price, one subscriber still null | Rejected, names the backfill endpoint |
| Backfill: card-billed subscription with null snapshot | Populated from Stripe; idempotent on rerun |
| Backfill: cash-billed subscription with null snapshot | Populated from Monthly `PlanPrice`; listed in `CashBilledSnapshots` |

Commit: `feat: discounts-this-month ledger, billed-amount backfill, relax G2 once snapshotted (Phase D)`

---

## 9. Phase E — Help, manual, architecture docs

- `helpContent.ts` admin-dashboard article: add a tip — `"'Discounts this month' sums referral
  discounts and credits actually applied to paid invoices — it doesn't reduce MRR (MRR is
  contracted revenue)."`
- `helpContent.ts` admin plan-management article (`admin-plan-edit`): update the existing
  price-in-use tip — once every subscriber on a price has a billed-amount snapshot, changing
  that price's amount is now allowed (existing subscribers keep their own billed price;
  grandfathered).
- Mirror both in `frontend/public/user-manual/index.html`'s equivalent sections.
- `architecture.md`: new Decisions Log row, "Subscription billed-amount snapshot (2026-09-23)"
  — summarize §2.1–§2.6 and why (the pre-July `Plan.PriceMonthly` MRR overstatement was the
  same bug class: a metric reading a display/list price instead of the billed amount).
  Confirm no new `IgnoreQueryFilters()` registry row is needed (§2.1.B) and say so explicitly,
  the way earlier entries in this file do when a change deliberately adds no new usage.

Commit: `docs: billed-amount snapshot, discounts-this-month and relaxed price-in-use rule in Help, manual and architecture (Phase E)`

---

## 10. Constraints

No `var` for non-obvious types; no `any`. Decimal arithmetic throughout the MRR path — do not
round in the backend. Every Application-layer change has unit tests. Serilog structured logs
with counts only, never PII, never raw Stripe payload contents.

---

## 11. Industry-standard benchmark note (CLAUDE.md rule #6)

Billing systems that report MRR from the actual billed amount rather than a live price
catalogue (Stripe Billing's own MRR metric explicitly does this — it's computed per-
subscription-item from what that item is actually charged, immune to later catalogue price
changes) is the baseline every benchmark platform in this category matches; reading the price
list instead is the bug this batch removes.

---

## 12. Final verification checklist

- [ ] A subscription's MRR survives a later `PlanPrice.Price` change unchanged.
- [ ] `customer.subscription.updated` with no price-id change still refreshes the snapshot.
- [ ] Free/cash-billed subscriptions snapshot correctly (0 and Monthly price respectively).
- [ ] `DiscountsThisMonth` matches a real Stripe test-mode invoice's discount + balance-credit
      amount, spot-checked against the Stripe dashboard.
- [ ] `invoice.paid` replay (same event delivered twice) does not double-count.
- [ ] G2 rejects a price change with any un-snapshotted subscriber, allows it once all are
      snapshotted.
- [ ] Backfill is idempotent and reports cash-billed snapshots for admin review.
- [ ] Currency-mismatch and fallback counts are logged, never silently swallowed.
- [ ] Help, manual, architecture updated; `dotnet test`, `pnpm test`, `pnpm lint` green.
- [ ] Five commits on `feature/mrr-billed-amount-snapshot`.

---

## 13. Final deliverable spec

Five commits (Phases A–E). Final report: changes per phase with file list; drift from this
file; the §3 flags verbatim (forever-coupon non-existence, tax/VAT assumption, exact
`DiscountAmount` composition verified against a real payload, backfill UI button or not);
and confirmation that Batch 3 (revenue ledger, MRR movements chart, yearly refunds) can build
directly on `SubscriptionInvoicePayment` without a second migration for the same data.
