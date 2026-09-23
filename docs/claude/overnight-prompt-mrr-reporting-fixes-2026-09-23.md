# Overnight Prompt — MRR Reporting Fixes and Plan-Price Drift Guards (Batch 2)

> Feed this file directly to Claude Code as the task prompt. It is self-contained: exact
> files, exact current code (re-read from live source on 2026-09-23, commit `0dbe9371`),
> exact target behaviour, exact tests, exact docs to sync. Read the whole file before writing
> anything.

**Date logged:** 2026-09-23
**Requested by:** Phi
**Origin:** Finance project subscription-plan audit, 2026-09-23 ("Subscription Plans — Solution
Plan", Problems 1 and 2; decisions in project doc `claude/subscription-plan-decisions-2026-09-23.md`).
This is Batch 2 of 3. It contains only financial-logic changes and guards that need **no
schema change**. The schema half of Problem 2 (storing the billed amount on each subscription)
is specified separately for Engineering Consultation in
`docs/claude/spec-mrr-billed-amount-snapshot-2026-09-23.md` — do not build it tonight.
**Prerequisite:** Batch 1 (`docs/claude/overnight-prompt-plan-tiers-yearly-2026-09-23.md`)
must be merged first. It edits `DataSeeder.ReconcileCoreTiersAsync` and `GetPlansQuery`, which
this batch also touches. Branch from the branch/commit that contains Batch 1.
**Mode:** Fully autonomous, no user present. Do not stop to ask questions — open product
questions are in §3 with defaults. Where this file states a fact about current code it was
re-read on 2026-09-23; if your checkout disagrees, trust your checkout, note the drift in your
final report, and do not silently change approach.

**Before starting**, run:

```bash
git add -A && git commit -m "checkpoint: before MRR reporting fixes" --allow-empty
git checkout -b feature/mrr-reporting-fixes
```

Commit at the end of each phase (§5–§8).

---

## 1. Goal

Make the two admin revenue numbers — the MRR KPI card (`GetPlatformStatsQuery`) and the MRR
history chart (`GetMrrHistoryQuery`) — use **one** definition of MRR, so they always agree,
and stop the history chart from counting revenue that was never billed (trial months, months
after an admin cancellation). Report past-due and cancelling revenue separately. Fix ARPU,
which divides by Free studios. Then block the three plan-editing paths that can silently
change reported revenue without changing what Stripe bills.

No studio is subscribed to a paid plan today, so nothing already reported to anyone changes;
this is about the numbers being right from the first real subscriber on.

Applicable `CLAUDE.md` rules: #1 (tenant isolation — the one cross-tenant read reuses approved
`IgnoreQueryFilters` usage #4, §5.3), #2 (endpoints keep `AdminOnly`), #3 (no PII in logs),
#5 (Serilog only), #6 (benchmark — §10), #7 (Help sync — §8, same branch).

---

## 2. Decisions already made — implement as specified, do not re-litigate

### 2.1 D1 — One definition of MRR

- **Headline MRR** = sum of monthly-equivalent revenue of subscriptions with
  `Status == Active` and a non-null `Plan`. Monthly-equivalent: Monthly price as is, Yearly
  price ÷ 12 (unchanged formula; Batch 2b switches its *input* to the billed amount).
- **At-risk MRR** = same sum for `Status == PastDue`. Not in headline MRR.
- **Scheduled churn MRR** = same sum for `Active` subscriptions with `CancelAtPeriodEnd == true`.
  These **stay** in headline MRR (still billed) and are also reported on this line.
- `Trialing`, `GracePeriod`, `Cancelled` → 0.
- Referral discounts do **not** reduce MRR (contracted MRR). Reporting "discounts this month"
  needs the Batch 2b columns — not tonight.
- **Suspended studios are not in headline MRR.** From tonight, suspending a studio pauses its
  billing (D6), so its revenue is reported as **paused MRR**, not MRR. Today
  `GetPlatformStatsQuery` excludes suspended studios while `GetMrrHistoryQuery` includes them;
  both now follow D6.

### 2.2 D2 — MRR for a month is measured at the end of that month

Industry convention (ChartMogul, Baremetrics, Stripe Billing analytics): the MRR figure for a
month is the MRR at that month's last moment. For the current month it is MRR **now**. So a
subscription counts toward month M if it was billing at `t = min(end of M, now)`.

### 2.3 D3 — Reconstructing past months conservatively

There is still no history table (the revenue ledger is Batch 3), and each studio has one
`Subscription` row mutated in place. Until the ledger exists, past months are reconstructed
from current state with a **billing window** that can under-count but never over-count:

- `start = min(max(sub.CreatedAt, studio.TrialExpiresAt ?? sub.CreatedAt), now)`.
  `Subscription.CreatedAt` is trial day 1 (`RegisterStudioCommand`), so on its own it credits
  trial months as revenue. `Studio.TrialExpiresAt` survives conversion
  (`Subscription.TrialExpiresAt` is nulled on activation). Clamping to `now` guarantees that
  a studio that paid mid-trial still counts today.
- `end`: `Active` or `PastDue` → open. `Cancelled` → the earlier of `CurrentPeriodEnd` and the
  latest `Subscription.CancelledByAdmin` audit entry for that studio (`AuditLogEntries`,
  `Action == AuditActions.SubscriptionCancelledByAdmin`, `TargetId == StudioId` — see
  `CancelSubscriptionCommand.AuditTargetId`). `CancelSubscriptionHandler` leaves a future
  `CurrentPeriodEnd`, so without the audit timestamp an admin-cancelled studio keeps counting.
  Any other status → no window.
- Counts at time `t` when `start <= t` and (`end` is open or `end > t`), **and** the status rule
  from D1 holds *for the current point only*: a `PastDue` subscription counts in past months
  (it was paying then) but not in the current month (at-risk).
- The chart shows a caption until Batch 3: "Past months are estimated from current
  subscriptions; plan changes aren't reflected yet."

### 2.4 D4 — Growth and ARPA

- `MrrGrowthPercent` = (MRR now − MRR at end of last month) ÷ MRR at end of last month, both
  from the D2/D3 function. When last month's MRR is 0 it is `null` (not "+100%", which is
  what the current code reports).
- The dashboard's "ARPU" card divides MRR by `ActiveSubscriptions`, which includes Free
  studios paying 0. Replace it with **ARPA** (average revenue per account, the B2B SaaS
  term): MRR ÷ `PayingStudios`, where `PayingStudios` = active subscriptions whose
  monthly-equivalent is > 0.

### 2.5 D5 — Plan-price guards (no schema change)

Until Batch 2b stores the billed amount per subscription, MRR is computed from the
`PlanPrice` row a subscription points at. Three existing paths can change that row without
changing what Stripe bills. Block them:

- **G1 — never delete a price in use.** `UpdatePlanHandler` removes any `PlanPrice` whose
  interval is missing from the request. If any subscription is on it (`PlanId == plan.Id &&
  BillingInterval == interval`) or scheduled onto it (`PendingPlanId == plan.Id &&
  PendingBillingInterval == interval`), set `IsActive = false` instead of removing, and log it.
- **G2 — no in-place price change on a price in use.** If a `PlanPrice` has subscribers (same
  test as G1) and the request changes its `Price` or its `StripePriceId`, reject with
  `BusinessRuleViolationException`: "This price has {n} subscribed studio(s). Changing it would
  change their reported revenue without changing what Stripe bills them. Deactivate this
  price and create a new plan instead." (Batch 2b relaxes this once revenue no longer reads the
  price row.)
- **G3 — the linked Stripe price must match.** In `CreatePlanHandler` and `UpdatePlanHandler`,
  whenever a request sets or changes a `StripePriceId`, fetch the price from Stripe and reject
  unless: it exists, `Active`, `UnitAmount / 100m == Price`, recurring interval is `month` for
  Monthly / `year` for Yearly, and `IntervalCount == 1`. Message names the mismatch
  ("Stripe price price_… is €89.00/month; this plan price is €79.00").
- **G4 — the reconciler never overwrites a linked price.** In
  `DataSeeder.ReconcileCoreTiersAsync`, when a `PlanPrice` row exists, has a non-null
  `StripePriceId`, and its `Price` differs from code, do **not** overwrite it; log a warning
  ("Core tier {PlanName} {Interval} is linked to a Stripe price; code price {CodePrice} ≠ stored
  {StoredPrice}. Not changed — create a new Stripe price and relink it in Plan management.").
  Unlinked rows keep being corrected as today.

### 2.6 D6 — Suspending a studio pauses its billing (decided by Phi, 2026-09-23)

Current behaviour: `SuspendStudioHandler` sets `Studio.IsActive = false` and invalidates the
access cache. Per Help ("admin-suspend-studio") that immediately blocks logins for the owner
and every artist, but the Stripe subscription keeps renewing and charging. Charging a customer
for a period they are locked out of is against standard B2B SaaS practice. It invites card
disputes, and Stripe provides `pause_collection` for exactly this case.

Target:

- **Suspend** → after saving, pause collection on the studio's Stripe subscription with
  `pause_collection.behavior = "void"`: renewals during the suspension generate no charge.
  The period already paid is **not** refunded (suspension is for policy violations; the terms
  should say so).
- **Unsuspend** → resume collection (clear `pause_collection`). Billing restarts at the next
  normal renewal date; the suspended time is not credited back.
- Both Stripe calls are best-effort, following the `CancelSubscriptionHandler` pattern: the
  database change stands, and a Stripe failure is logged as an error naming the subscription,
  with "manual Stripe action required".
- Cash-billed studios (no `StripeSubscriptionId`): no Stripe call. `CurrentPeriodEnd` is not
  extended.
- **Dunning:** `PastDueReminderJob` skips suspended studios. Their owner can't log in to pay,
  so reminder emails would be pointless.
- **Reporting:** a suspended studio's subscription is excluded from headline MRR at points in
  time after its latest `Studio.Suspended` audit entry (`AuditActions.StudioSuspended`,
  `TargetId == StudioId`), and reported on its own line as **paused MRR** (the Suspended KPI
  card). Months before the suspension are unchanged. Once unsuspended, it counts again from
  now; the gap isn't reconstructed until the Batch 3 ledger exists.
- Permanent removal remains the admin's existing "Cancel subscription" action. No automatic
  cancellation after N days of suspension tonight.

### 2.7 D7 — Yearly → monthly waits for the end of the paid year

Specified in full in §7.5, next to the code it changes.

---

## 3. Flag, don't decide — do not build blind

- **Suspension while PastDue.** Pausing collection stops *future* invoices. Verify in Stripe
  test mode whether an already-open past-due invoice keeps being retried. If it does, leave it
  (the debt is real), and state what you observed in the report.
- **`TrialConversionRate`** counts activation of the Free plan as a conversion. Leave the
  formula; report it as a question ("conversion to any plan" vs "to a paid plan").
- **`PlatformStatsResponse` doc comment** says suspended studios are excluded from
  `TotalStudios`, but the code includes them (`totalStudios = studios.Count`). Report; don't fix.
- **Discounts this month, billed-amount snapshot, price-change grandfathering** — Batch 2b
  spec. **Revenue ledger, MRR movements chart, refunds** — Batch 3.

---

## 4. Scope boundary — do not touch

- No migration, no new entity, no new column. If you find you need one, stop and report why.
- Stripe webhook handlers (`HandleSubscriptionUpdatedCommand`, `HandleInvoicePaidCommand`,
  `HandleSubscriptionDeletedCommand`, `BillingEndpoints.HandleBillingWebhook`).
- `CancelSubscriptionCommand`, `ExtendTrialCommand` behaviour. (`SuspendStudioCommand` /
  `UnsuspendStudioCommand` change only as §7.4 specifies: add the Stripe pause/resume call and nothing else.)
- `GetPlansQuery`, referral code (Batch 1). The subscribe page changes only as §7.5 specifies (one line of text on monthly cards for yearly studios).
- `GetIndustryReportsQuery` and the owner revenue summary (studio deposit revenue, not
  subscriptions — verified: neither reads plan prices).

---

## 5. Phase A — Shared MRR rules

### 5.1 `MrrRules`

New file `Pena_e_Arte.Application/Platform/Revenue/MrrRules.cs` (static, pure, no DB):

```csharp
public sealed record SubscriptionRevenueInput(
    Subscription Subscription,          // Plan + Prices loaded
    DateTime? StudioTrialExpiresAt,
    DateTime? AdminCancelledAt,
    bool StudioIsActive,                // false = suspended (D6)
    DateTime? SuspendedAt);             // latest Studio.Suspended audit entry, when suspended

public static class MrrRules
{
    public static decimal MonthlyEquivalent(Subscription s);          // existing formula, moved here
    public static (DateTime Start, DateTime? End)? BillingWindow(SubscriptionRevenueInput input, DateTime now); // D3
    public static bool BillingAt(SubscriptionRevenueInput input, DateTime t, DateTime now);                   // D2 + D3
    public static decimal MrrAt(IEnumerable<SubscriptionRevenueInput> inputs, DateTime t, DateTime now);
    public static decimal AtRiskMrr(IEnumerable<SubscriptionRevenueInput> inputs);          // PastDue, now
    public static decimal ScheduledChurnMrr(IEnumerable<SubscriptionRevenueInput> inputs);  // Active + CancelAtPeriodEnd, not suspended
    public static decimal PausedMrr(IEnumerable<SubscriptionRevenueInput> inputs);          // Active or PastDue, studio suspended (D6)
    public static int PayingStudios(IEnumerable<SubscriptionRevenueInput> inputs);          // Active, MonthlyEquivalent > 0
    public static DateTime EndOfMonth(DateTime monthStart);  // monthStart.AddMonths(1).AddTicks(-1)
}
```

`BillingAt(input, t, now)` when `t` is "now" (the current month) requires `Status == Active`
and `StudioIsActive`; for `t < now` it accepts `Active`, `PastDue` and `Cancelled` within the
window. For a suspended studio, the window's `end` is additionally capped at `SuspendedAt`
(D6). `AtRiskMrr` and `PayingStudios` exclude suspended studios. Delete the two
private `MonthlyEquivalentRevenue` copies in the query handlers.

### 5.2 Tests — `tests/Pena_e_Arte.UnitTests/Platform/MrrRulesTests.cs` (new)

Fix `now = 2026-06-20T12:00Z`. Premium monthly 79, Premium yearly 790.

| Scenario | Expect |
|---|---|
| Created 1 Jan, studio trial ends 15 Jan, Active monthly | End-Dec 0 · end-Jan 79 · now 79 |
| Same, Active yearly | every counted point 65.83 (790 ÷ 12, rounded to 2 dp only for display; assert with decimal precision) |
| Paid on trial day 5 (created 10 Jun, trial ends 24 Jun, Active) | now 79 (start clamped to now) · end-May 0 |
| Active since Jan, admin-cancelled 10 Mar (audit), `CurrentPeriodEnd` 5 Apr, status Cancelled | end-Feb 79 · end-Mar 0 · now 0 |
| Stripe-deleted, `CurrentPeriodEnd` 5 Apr, no audit row, status Cancelled | end-Mar 79 · end-Apr 0 |
| PastDue now, paying since Jan | end-May 79 · now 0 · `AtRiskMrr` 79 |
| Active + `CancelAtPeriodEnd` | now 79 · `ScheduledChurnMrr` 79 |
| Trialing, `PlanId` null | 0 everywhere |
| Active on Free (0) | now 0 · `PayingStudios` excludes it |
| Active since Jan; studio suspended 12 May (audit), still suspended | end-Apr 79 · end-May 0 · now 0 · `PausedMrr` 79 |
| Suspended, then unsuspended (IsActive true again), Active | now 79 · `PausedMrr` 0 |

Commit: `feat: shared MRR rules — one definition, month-end convention, conservative history (Phase A)`

---

## 6. Phase B — Both queries use the rules

### 6.1 Shared loader

New `Pena_e_Arte.Application/Platform/Revenue/MrrInputLoader.cs` (static,
`Task<List<SubscriptionRevenueInput>> LoadAsync(IAppDbContext db, CancellationToken ct)`):

- `db.Studios.IgnoreQueryFilters().Include(s => s.Subscription).ThenInclude(sub => sub!.Plan).ThenInclude(p => p!.Prices)`,
  keeping only studios with a subscription. This is the same read, for the same purpose, as
  approved usage #4 — move the approval comment here
  (`// IgnoreQueryFilters approved: usage #4 — platform KPI aggregate, AdminOnly. See architecture.md.`)
  and update row 4 of the `IgnoreQueryFilters` registry in `architecture.md` (§8.3).
- Latest `SubscriptionCancelledByAdmin` audit timestamp per studio:
  `db.AuditLogEntries.Where(a => a.Action == AuditActions.SubscriptionCancelledByAdmin && a.TargetType == AuditTargetTypes.Subscription).GroupBy(a => a.TargetId).Select(g => new { StudioId = g.Key, At = g.Max(a => a.CreatedAt) })`.
- Latest `Studio.Suspended` audit timestamp per studio, the same way
  (`AuditActions.StudioSuspended`, `AuditTargetTypes.Studio`), used only when `Studio.IsActive` is false.

### 6.2 `GetPlatformStatsQuery`

File `Pena_e_Arte.Application/Platform/Queries/GetPlatformStatsQuery.cs`. Keep every count
exactly as it is (they're status counts, not revenue). Change only revenue:

- `mrr = MrrRules.MrrAt(inputs, now, now)`.
- `lastMonthMrr = MrrRules.MrrAt(inputs, MrrRules.EndOfMonth(lastMonth), now)`; growth per D4
  (`double?`, null when `lastMonthMrr == 0`). Delete the "Approximation…" comment block.
- New: `AtRiskMrr`, `ScheduledChurnMrr`, `PausedMrr`, `PayingStudios`.

`Pena_e_Arte.Contracts/Responses/PlatformStatsResponse.cs` — `MrrGrowthPercent` becomes
`double?`; append `int PayingStudios, decimal AtRiskMrr, decimal ScheduledChurnMrr, decimal PausedMrr`.

### 6.3 `GetMrrHistoryQuery`

File `Pena_e_Arte.Application/Platform/Queries/GetMrrHistoryQuery.cs`. For each of the last
`months` months: `t = (month is current) ? now : EndOfMonth(monthStart)`;
`Mrr = MrrRules.MrrAt(inputs, t, now)`. Response shape (`MrrDataPointResponse(Month, Mrr)`,
`"yyyy-MM"`) unchanged; `Math.Clamp(months, 1, 24)` unchanged.

### 6.4 Tests

- `GetPlatformStatsHandlerTests` / `GetMrrHistoryHandlerTests`: update to the new behaviour;
  add **"current month of history == stats MRR, to the cent"** on a mixed seed (monthly,
  yearly, PastDue, cancelled, Free, suspended, trialing).
- Growth: last month 0, now 79 → `MrrGrowthPercent` null. Last month 100, now 79 → −21.0.
- `PlatformStatsIntegrationTests`: `PayingStudios` excludes Free; `AtRiskMrr` / `ScheduledChurnMrr` values.

### 6.5 Admin dashboard

`frontend/src/features/platform/platform.types.ts`: `mrrGrowthPercent: number | null`, add
`payingStudios`, `atRiskMrr`, `scheduledChurnMrr`, `pausedMrr` (numbers, no `any`).
`AdminDashboardPage.tsx`:

- MRR card subtitle: growth as today; `null` → "No MRR last month". If `scheduledChurnMrr > 0`,
  add "€X cancelling at period end".
- ARPU card (~lines 253–262) → label "ARPA", value `mrr / payingStudios` ("—" when 0),
  subtitle "MRR ÷ paying studios".
- Past Due card: subtitle "€X MRR at risk" when `atRiskMrr > 0`.
- Suspended card (~line 318): subtitle "€X MRR paused" when `pausedMrr > 0`.

`MrrChart.tsx`: the D3 caption under the chart, `text-xs text-muted-foreground`.
Frontend tests for the three card states and the caption.

Commit: `fix: stats card and MRR chart share one MRR definition; ARPA, at-risk and cancelling MRR (Phase B)`

---

## 7. Phase C — Plan-price guards (D5)

### 7.1 Stripe price lookup

`Pena_e_Arte.Domain/Interfaces/IStripeBillingService.cs` — add:

```csharp
public sealed record StripePriceInfo(bool Active, long? UnitAmount, string Currency, string? RecurringInterval, long? IntervalCount);
Task<StripePriceInfo?> GetPriceAsync(string stripePriceId, CancellationToken ct);   // null when not found
```

`StripeBillingService`: `PriceService.GetAsync`; map a 404 `StripeException` to `null`.

### 7.2 Handlers

- `UpdatePlanHandler` (`Pena_e_Arte.Application/Plans/Commands/UpdatePlanCommand.cs`): add G1 to
  the stale-price loop (currently `db.PlanPrices.Remove(stale)` for every missing interval);
  add G2 before mutating an existing row; add G3 for every row whose `StripePriceId` is new or
  changed. Inject `IStripeBillingService`. The FluentValidation validator stays synchronous;
  G2/G3 live in the handler because they need the DB and Stripe.
- `CreatePlanHandler`: G3 for every price with a `StripePriceId`.
- `DataSeeder.ReconcileCoreTiersAsync`: G4 in the `price.Price = tp.Price` branch.

### 7.3 Tests

| Test | Expect |
|---|---|
| Drop Yearly from Premium while one studio is on Premium Yearly | Row kept, `IsActive == false` |
| Drop Yearly with no subscribers | Row removed (unchanged behaviour) |
| Drop an interval a studio is *scheduled* onto (`PendingPlanId`/`PendingBillingInterval`) | Row kept, inactive |
| Change Premium Monthly 79 → 89 with one subscriber | `BusinessRuleViolationException`, nothing saved |
| Change `StripePriceId` on a row with subscribers | Rejected |
| Change price with no subscribers + matching Stripe price | Saved |
| Link `price_x` whose Stripe amount is 8900 to a price of 79 | Rejected, message names both amounts |
| Link a yearly Stripe price to a Monthly row | Rejected |
| Stripe price not found / inactive | Rejected |
| Reconciler: linked Starter Monthly stored 29, code 35 | Stays 29, warning logged |
| Reconciler: unlinked row drift | Corrected (existing test keeps passing) |

### 7.4 Suspension pauses billing (D6)

- `IStripeBillingService` — add `Task PauseCollectionAsync(string stripeSubscriptionId, CancellationToken ct);`
  (`SubscriptionUpdateOptions.PauseCollection = new() { Behavior = "void" }`) and
  `Task ResumeCollectionAsync(string stripeSubscriptionId, CancellationToken ct);`
  (clear `pause_collection`; check the Stripe.net version in the repo for the exact way to
  send an empty value, and note it in the report).
- `SuspendStudioHandler` / `UnsuspendStudioHandler`
  (`Pena_e_Arte.Application/Studios/Commands/`): inject `IStripeBillingService` and
  `ILogger<…>`; load the studio with `Subscription`; after `SaveChangesAsync` and the cache
  invalidation, call pause/resume when `StripeSubscriptionId` is not null and the subscription
  isn't `Cancelled`. Wrap the call in try/catch and `LogError` with
  `{StripeSubscriptionId}`/`{StudioId}` and "manual Stripe action required". Don't rethrow.
- `PastDueReminderJob`: add `s.Studio.IsActive` to the query (use the approved cross-tenant
  pattern the job already uses; don't add a new `IgnoreQueryFilters` usage without registering it).
- Tests:

| Test | Expect |
|---|---|
| Suspend a card-billed Active studio | `PauseCollectionAsync` called once with its subscription id |
| Unsuspend it | `ResumeCollectionAsync` called once |
| Suspend a cash-billed studio | No Stripe call |
| Suspend a Cancelled subscription | No Stripe call |
| Pause throws | Studio still suspended; error logged; no exception to caller |
| PastDue + suspended | No dunning email sent |

### 7.5 Yearly → monthly is never an immediate "upgrade" (D7)

Current behaviour (`ChangePlanHandler`, `Pena_e_Arte.Application/Billing/Commands/ChangePlanCommand.cs`
~line 65): a change is an immediate upgrade whenever the new monthly-equivalent exceeds the
current one. Premium Yearly (790 ÷ 12 = 65.83) → Premium Monthly (79) therefore counts as an
"upgrade". It switches now with `ProrationBehavior = "always_invoice"`, and Stripe credits the
unused part of the year (about €724 after one month) to the studio's balance. That is a
time-based refund at the discounted yearly rate, as account credit. It bypasses the agreed
yearly refund rule (used months charged at the monthly price), and it's live today on Premium
yearly.

**D7 (decided 2026-09-23):** any change **from Yearly to Monthly**, whatever the tier, is
scheduled for the end of the paid year (the existing downgrade path:
`ScheduleSubscriptionPriceChangeAsync` + `PendingPlanId`/`PendingBillingInterval`). All other
changes keep today's rule. A yearly studio that wants a bigger tier *now* picks that tier's
yearly price (immediate, prorated); the pricing page already offers it.

- Change the condition to `bool isUpgrade = MonthlyEquivalent(newPrice) > MonthlyEquivalent(currentPrice) && !(currentPrice.Interval == BillingInterval.Yearly && newPrice.Interval == BillingInterval.Monthly);`
  with a comment pointing to D7.
- `SubscribePage.tsx`: for a card-billed yearly studio, a monthly plan card shows "Starts when
  your paid year ends on {currentPeriodEnd}" under the price.
- Tests (`tests/Pena_e_Arte.UnitTests/Billing/ChangePlanHandlerTests.cs`):

| From → to | Expect |
|---|---|
| Premium Yearly → Premium Monthly | Scheduled; `PendingBillingInterval = Monthly`; `ChangeSubscriptionPriceAsync` not called |
| Starter Yearly → Growth Monthly | Scheduled (not immediate, although 59 > 24.17) |
| Starter Yearly → Growth Yearly | Immediate, prorated (unchanged) |
| Growth Monthly → Premium Monthly | Immediate (unchanged) |
| Premium Monthly → Premium Yearly | Scheduled at period end (unchanged: 65.83 < 79) |

Commit: `fix: plan editing can no longer change reported revenue behind Stripe's back; suspension pauses billing; yearly→monthly waits for period end (Phase C)`

---

## 8. Phase D — Help, manual, architecture docs

### 8.1 `frontend/src/features/help/helpContent.ts`

Admin dashboard article (~line 1553): "View the KPI cards for totals (Total Studios, Active
Subscriptions, MRR, ARPA)." Add tips:

- "MRR is the monthly subscription revenue from studios on an active paid plan. Yearly plans count as a twelfth of the yearly price. Referral discounts don't reduce it."
- "Past-due studios aren't in MRR — their revenue shows as 'at risk' on the Past Due card until they pay or cancel. Studios cancelling at period end still count until their period ends."
- "ARPA is MRR divided by paying studios; Free-plan studios aren't counted."
- "Each chart point is MRR at the end of that month (now, for the current month). Past months are estimated from current subscriptions until full revenue history is recorded."

Admin plan management article: add "A price that studios are subscribed to can't be changed or
deleted — deactivate it and create a new plan instead. A linked Stripe price must match the
amount and billing interval exactly."

`admin-suspend-studio` article (~line 1590): add to `warnings`: "Suspending also pauses the
studio's card billing: no renewal is charged while it's suspended, and the period already paid
isn't refunded. Reactivating resumes billing at the next renewal date. To end the subscription
for good, use Cancel Subscription." Add a tip: "Suspended studios don't get payment-reminder
emails, and their revenue shows as 'paused' on the dashboard."

Also add a tip to the dashboard article: "Suspended studios' revenue is shown as 'paused' on the Suspended card, not in MRR."

Owner "Choose or change your subscription plan" article (~line 1402): add a tip: "Switching
from yearly to monthly billing takes effect when your paid year ends. To move to a bigger plan
straight away, choose its yearly option." Mirror it in the manual's Owner → Choose / change
plan steps.

### 8.2 `frontend/public/user-manual/index.html`

- ~Line 3489: "…(studios, active subs, MRR, ARPU)…" → "…MRR, ARPA)…"; add a short definitions
  list after the steps with the four tips above.
- ~Line 3477 mockup: if a card reads "ARPU", change it to "ARPA".
- Plan management steps: add the price-in-use rule from §8.1.
- Studios section, ~line 3523: "Click Suspend / Reactivate to control public visibility
  independently of billing." is now wrong → "Click Suspend / Reactivate to lock a studio out
  (or restore it). Suspending also pauses its card billing; reactivating resumes it at the next
  renewal." Extend the warning callout (~line 3526) with the no-refund sentence from §8.1.

### 8.3 `docs/claude/architecture.md`

- `IgnoreQueryFilters` registry, row 4 (~line 1089): handler column →
  "`MrrInputLoader` (used by `GetPlatformStatsHandler`, `GetMrrHistoryHandler`)", same purpose.
- Decisions Log — new row "One MRR definition (2026-09-23)": D1–D5 in one paragraph; reason:
  the two admin figures disagreed and the history counted trial months and post-cancellation
  months; plan edits could change reported revenue without changing Stripe billing.
- Decisions Log — new row "Suspension pauses billing (2026-09-23)": D6; reason: suspension
  blocked all logins while Stripe kept charging.
- Decisions Log — new row "Yearly → monthly waits for period end (2026-09-23)": D7; reason:
  the immediate "upgrade" credited unused yearly time and bypassed the yearly refund rule.
- Tours: `adminTour.ts` names no metric — confirm "no change needed" and say why in the report.

Commit: `docs: MRR definitions, ARPA and price-in-use rules in Help, manual and architecture (Phase D)`

---

## 9. Constraints

- No `var` for non-obvious types; no `any`. No migration. Serilog structured logs with
  `StudioId`/`PlanId` only. Every Application-layer change has unit tests.
- Decimal arithmetic throughout; do not round MRR in the backend (the UI formats it).

---

## 10. Industry-standard benchmark note (CLAUDE.md rule #6)

Month-end MRR, contracted MRR with discounts reported separately, past-due shown as at-risk,
cancel-at-period-end shown as scheduled churn, and ARPA over paying accounts follow the
definitions used by ChartMogul, Baremetrics and Stripe Billing's revenue analytics, and the
admin-dashboard norm in the benchmark set's B2B SaaS platform-admin row. The remaining gap
(recorded history and an MRR-movements chart) is Batch 3 and is stated on the chart itself.

---

## 11. Final verification checklist

- [ ] On the dev seed, the MRR card equals the chart's current-month point, to the cent.
- [ ] A converted studio shows no MRR in months that ended before its trial ended.
- [ ] Admin-cancelling a studio removes it from MRR now and from the current month's point.
- [ ] Setting a subscription to PastDue moves its revenue from MRR to the Past Due card.
- [ ] Suspending a test-mode card-billed studio shows `pause_collection` on its Stripe
      subscription and moves its revenue to "paused"; reactivating clears both.
- [ ] Test mode: Premium Yearly → Premium Monthly creates a subscription schedule for period
      end, and no proration credit appears on the customer balance.
- [ ] ARPA ignores Free studios; growth shows "No MRR last month" when last month was 0.
- [ ] Every D5 guard test passes; the unlinked-drift reconciler test still passes.
- [ ] Help, manual, architecture updated; `dotnet test`, `pnpm test`, `pnpm lint` green.
- [ ] Four commits on `feature/mrr-reporting-fixes`.

---

## 12. Final deliverable spec

Four commits (Phases A–D). Final report: changes per phase with file list; drift from this
file; the §3 flags verbatim (past-due retry behaviour under pause, conversion includes Free,
stats doc comment); tours reasoning; and a pointer that Batch 2b
(`docs/claude/spec-mrr-billed-amount-snapshot-2026-09-23.md`) is for Engineering Consultation,
after which guard G2 can be relaxed.
