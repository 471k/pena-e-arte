# Spec: Redesign Plan/Billing-Interval Model to Industry Standard

**Author:** Phi
**Related:** `bug-report-plans-page-data-mismatch.md`,
`bug-report-premium-plan-duplicate-legacy-row.md` — those documents cover the
symptoms (stale data, a triplicated Premium card); this document proposes the
underlying architecture change that prevents that whole bug class going forward, and
makes yearly billing actually usable across every tier, not just Premium.

**Goal:** One plan = one row. A studio owner can pick Monthly or Yearly for *any*
tier without the platform needing a second copy of that tier in the database.

---

## 1. Why the Current Model Needs to Change (not just the data)

Today, `Plan.BillingInterval` is a **locked, permanent attribute of the row itself**
(`Pena_e_Arte.Domain/Entities/Plan.cs`, line 9), and the codebase's own Decisions Log
treats that as intentional: a plan wanting both cadences gets a second, fully
duplicated row, linked by `PairedPlanId`.

This is *not* the industry-standard shape. The standard pattern (the one Stripe
itself is built around) separates two different ideas that this model conflates:

- **What you get** — the tier: feature limits, seats, storage, branding removal,
  API access, priority support. This should exist once per tier.
- **How often you pay for it** — the interval: Monthly or Yearly, each with its own
  price and its own Stripe Price ID. A tier can offer one interval, both, or (in the
  future) more, without changing what the tier *is*.

Concretely, this is already half-true in the schema and nobody has finished the job:
`Plan.cs` already carries **both** `StripePriceIdMonthly` and `StripePriceIdYearly`
(lines 14–15) on every single row — including Starter, Growth, and Pro, which only
have one row each. The blocker isn't the schema; it's that `BillingInterval` is used
throughout the Application layer as a gate that only lets one of those two Stripe IDs
ever be reachable per row:

```csharp
// CreateSubscriptionCheckoutCommand.cs, lines 51–53
string? priceId = plan.BillingInterval == BillingInterval.Monthly
    ? plan.StripePriceIdMonthly
    : plan.StripePriceIdYearly;
```

```csharp
// ChangePlanCommand.cs, lines 96–99
private static string? ChargedPriceId(Domain.Entities.Plan plan) =>
    plan.BillingInterval == BillingInterval.Monthly
        ? plan.StripePriceIdMonthly
        : plan.StripePriceIdYearly;
```

Both of these read the *row's own* locked interval, not a choice made by the person
checking out. That's why Premium needed a second row at all — there was no other way
to expose `StripePriceIdYearly` through either of these two call sites for the same
tier.

### This is a live, customer-facing bug today, not just a data-hygiene concern

`frontend/src/features/billing/components/SubscribePage.tsx` (the studio owner's own
subscribe screen, not the issuer admin page) already has a Monthly/Yearly toggle
built for a world where every tier supports both:

```tsx
// SubscribePage.tsx, line 114
const filteredPlans = plans.filter((p) => p.billingInterval === billingCycle);
```

Flip that toggle to "Yearly" today and **Free, Starter, Growth, and Pro all disappear
from the list** — not disabled, not shown with an upsell prompt, just gone — because
none of them has a row where `billingInterval === "Yearly"`. Only Premium remains
visible. A studio owner who prefers annual billing is silently steered into Premium
regardless of which tier actually fits their studio. This needs to be fixed
regardless of anything else in this document.

---

## 2. Target Design

### 2.1 Recommended: `PlanPrice` child table (fully scalable, the actual long-term fix)

Move interval-specific data off `Plan` entirely and into its own table, one row per
`(Plan, Interval)` combination that's actually offered:

```csharp
// New entity: Pena_e_Arte.Domain/Entities/PlanPrice.cs
public class PlanPrice
{
    public Guid            Id              { get; init; } = Guid.NewGuid();
    public Guid            PlanId          { get; set; }
    public BillingInterval Interval        { get; set; }   // Monthly | Yearly | (future: Quarterly, etc.)
    public decimal         Price           { get; set; }
    public string?         StripePriceId   { get; set; }
    public bool            IsActive        { get; set; } = true; // lets an interval be retired without deleting history
    public Plan Plan { get; set; } = null!;
}
```

```csharp
// Plan.cs — remove BillingInterval, PriceMonthly, PriceYearly, StripePriceIdMonthly,
// StripePriceIdYearly, PairedPlanId, YearlyDiscountPercent stays (still a per-tier
// marketing figure, see 2.3) and add:
public ICollection<PlanPrice> Prices { get; set; } = [];
```

A tier is now one row, full stop. Offering it monthly-only, yearly-only, both, or
(down the road) quarterly is just a matter of which `PlanPrice` rows exist under it —
**no schema change needed to add a third interval later**, which is the actual
scalability win being asked for here. This is the option to build if there's room in
the roadmap for it; if not, see 2.2 for a smaller version of the same idea.

### 2.2 Minimal version (ships faster, same principle, less scalable past 2 intervals)

Keep `Plan` flat, but make both intervals live on every row instead of gated by a
lock:

- Remove `BillingInterval` and `PairedPlanId` from `Plan`.
- Keep `PriceMonthly`, `PriceYearly`, `StripePriceIdMonthly`, `StripePriceIdYearly` —
  all **nullable**. A tier that doesn't offer yearly billing simply leaves the yearly
  pair null (this replaces "the row doesn't exist" with "the row exists, yearly just
  isn't configured yet," which is a far safer and clearer state for both code and the
  issuer editing it).

This fixes everything in this document with a smaller migration, at the cost of
needing another schema change if a third interval is ever added. Given the ask was
explicitly for "long-term and easily scalable," 2.1 is the recommended target — but
2.2 is worth having in the back pocket if timeline pressure is real, since it removes
the exact same bug class this document exists to fix.

**The rest of this document assumes 2.1. Where 2.2 differs, it's called out inline.**

### 2.3 What stays on `Plan`

Everything that describes the tier itself, independent of billing cadence, stays put:
`Name`, `MaxArtists`, `MaxAppointmentsPerMonth`, `MaxNotificationsPerMonth`,
`MaxStorageGb`, `MaxLocations`, `AllowBrandingRemoval`, `AllowApiAccess`,
`PrioritySupport`, `YearlyDiscountPercent` (kept as a display/marketing figure for
"Save X% annually" copy — it's not itself a price, and doesn't need to move).

### 2.4 `Subscription` needs its own interval field

Today `Subscription.PlanId` (`Subscription.cs`, line 9) is the only thing that
determines billing cadence — because the *Plan itself* encodes cadence. Once `Plan`
stops encoding cadence, a subscription needs to remember which cadence it's actually
on, independent of which tier it's on:

```csharp
// Subscription.cs — add:
public BillingInterval BillingInterval { get; set; }
```

This is the single most important conceptual change in this proposal: **"which tier"
and "how often billed" become two independent choices, exactly like they are for the
customer**, instead of being fused into "which row did you click."

---

## 3. Data Migration Plan

This must run as a real, reviewed migration (EF Core migration + a data-backfill
step), not manual `UPDATE` statements, since it touches every existing subscription.

1. **Add** the new `PlanPrice` table (or the nullable columns, if going with 2.2) and
   `Subscription.BillingInterval`, without dropping anything old yet.
2. **Backfill `PlanPrice`** from the five existing canonical rows
   (`StarterPlanId`, `GrowthPlanId`, `PremiumMonthlyPlanId`, `PremiumYearlyPlanId`,
   `ProPlanId`) plus `FreePlanId`:
   - Free, Starter, Growth, Pro: one `PlanPrice` row each (`Interval = Monthly`),
     carrying over that row's existing `PriceMonthly`/`StripePriceIdMonthly`.
   - Premium: **merge** `PremiumMonthlyPlanId` and `PremiumYearlyPlanId` into a
     single `Plan` row (recommend keeping `PremiumMonthlyPlanId` as the surviving
     `Id` — it's referenced first in `DataSeeder.cs` and is the more likely
     candidate for existing external references). Create two `PlanPrice` rows under
     it: `Monthly` (from the old `PremiumMonthlyPlanId` row's price/Stripe ID) and
     `Yearly` (from the old `PremiumYearlyPlanId` row's price/Stripe ID).
   - **Before merging, diff the two Premium rows' price and Stripe ID fields.**
     `UpdatePlanHandler`'s pairing sync (lines 53–69 of `UpdatePlanCommand.cs`)
     explicitly excludes price and Stripe IDs from sync — meaning they *could* have
     drifted independently since the rows were created, even though the limits were
     kept in sync. Don't assume they match; verify.
3. **Reassign subscriptions.** For every `Subscription` currently pointing at
   `PremiumYearlyPlanId`, set `PlanId = PremiumMonthlyPlanId` (the surviving merged
   row) and `BillingInterval = Yearly`. For every other subscription, set
   `BillingInterval = Monthly` (today, that's every subscription that isn't on
   legacy/duplicate Premium — confirm this with a query, don't assume).
4. **Fold in the other Premium cleanup.** `bug-report-premium-plan-duplicate-legacy-row.md`
   already identified a third, pre-split legacy Premium row still holding a live
   subscription. Handle that reassignment in this same migration — find it with:
   ```sql
   SELECT * FROM Plans WHERE Name = 'Premium'
     AND Id NOT IN ('<PremiumMonthlyPlanId>', '<PremiumYearlyPlanId>');
   ```
   Reassign any subscriptions on it the same way (Step 3), then delete the row.
5. **Drop the old columns/row** once nothing references them: `Plan.BillingInterval`,
   `Plan.PairedPlanId`, `Plan.PriceMonthly`, `Plan.PriceYearly`,
   `Plan.StripePriceIdMonthly`, `Plan.StripePriceIdYearly`, and the now-redundant
   `PremiumYearlyPlanId` row. Do this in a **separate, later migration** after the
   application code (Section 4) has shipped and been running against the new columns
   for at least one full deploy cycle — don't drop and rewrite the read/write paths
   in the same release.
6. Update `DataSeeder.cs`'s `ReconcileCorePlansAsync` to seed `PlanPrice` rows
   instead of flat monthly/yearly fields, and to reconcile by `(PlanId, Interval)`
   instead of by a single hardcoded `Id` list — this is what actually closes the gap
   that caused both prior bug reports; a reconciler keyed on tier name + interval
   can't produce an "orphan" the way one keyed on a fixed ID list did.

---

## 4. Application Layer Changes

| File | Current behavior | Required change |
|---|---|---|
| `Plan.cs` | `BillingInterval`, `PriceMonthly`, `PriceYearly`, `StripePriceIdMonthly`, `StripePriceIdYearly`, `PairedPlanId` all live here | Remove; add `ICollection<PlanPrice> Prices` |
| `Subscription.cs` | Cadence inferred from `Plan.BillingInterval` via `PlanId` | Add `BillingInterval` directly on `Subscription` |
| `CreateCheckoutRequest` (Contracts) | Only carries `PlanId` | Add required `BillingInterval` field — the owner's chosen cycle, decoupled from which `Plan` row exists |
| `CreateSubscriptionCheckoutCommand.cs` (lines 51–53) | Derives `priceId` from `plan.BillingInterval` | Derive `priceId` from `db.PlanPrices.First(pp => pp.PlanId == plan.Id && pp.Interval == request.BillingInterval)`; 404/`BusinessRuleViolationException` if that combination doesn't exist (replaces the current "plan not available for online checkout" case) |
| `ChangePlanRequest` / `ChangePlanCommand.cs` (lines 58–105) | `ChargedPriceId`/`MonthlyEquivalent` read `plan.BillingInterval` | Accept `BillingInterval` in the request; look up the matching `PlanPrice` for both the current and requested tier+interval; `MonthlyEquivalent` normalizes using whichever `PlanPrice.Interval` applies instead of the row's fixed interval |
| `HandleSubscriptionUpdatedCommand.cs` | Not yet reviewed in this pass — likely reads `Plan.BillingInterval` off the Stripe webhook's resolved plan | Audit and update to read `Subscription.BillingInterval` (or derive from the Stripe Price ID returned by the webhook, matched back to a `PlanPrice`) |
| `CreatePlanCommand.cs` / `UpdatePlanCommand.cs` | Validate/write a single `PriceMonthly`/`PriceYearly` pair; `UpdatePlanHandler` runs the `PairedPlanId` sync (lines 53–69) | Replace with create/update of one or more `PlanPrice` child rows per request; delete the `PairedPlanId` sync block entirely — it becomes meaningless once a tier is one row |
| `GetPlansQuery.cs` / `PlanResponse` | Returns one `billingInterval` per plan (a plan *is* an interval) | Return a `prices: [{ interval, price, isActive }]` array per plan (one entry per available interval) |
| `DataSeeder.cs` — `ReconcileCorePlansAsync` | Reconciles by matching a fixed list of five `Plan.Id`s | Reconcile by tier `Name` (or a stable non-interval key) + upsert child `PlanPrice` rows per interval; see Section 3, step 6 |

---

## 5. Frontend Changes

| File | Current behavior | Required change |
|---|---|---|
| `SubscribePage.tsx` (line 114) | `filteredPlans = plans.filter(p => p.billingInterval === billingCycle)` — tiers without a matching row vanish | Show all plans always; for each, look up `plan.prices.find(p => p.interval === billingCycle)`. If found, render that price and enable subscribing. If not found, either render the card in a disabled state ("Yearly billing not available for this plan yet") or fall back to showing the Monthly price with a note — **do not silently drop the tier from the list** |
| `SubscribePage.tsx` — `onSubscribe` / checkout & change-plan calls | Sends only `planId` | Send `planId` + `billingCycle` together, matching the updated `CreateCheckoutRequest`/`ChangePlanRequest` shape |
| `PlanManagementPage.tsx` | Single "Billing interval" dropdown (Monthly/Yearly) per plan form; separate `priceMonthly`/`priceYearly` fields tied to that one row | Remove the Billing Interval dropdown entirely. Replace with two independent, optional sections — "Monthly price" and "Yearly price" — each with its own price + Stripe Price ID inputs and an on/off toggle for "offer this interval." Editing a tier now always edits one row |
| Issuer Plans page (card grid) | One card per `Plan` row, so Premium shows twice (or three times, per the duplicate-row bug) | One card per tier again. Card can show both prices ("€79/mo · €790/yr") the same way Starter/Growth/Pro/Free already do today for their (currently decorative) "ref." line — except now the yearly figure is real and clickable, not just a reference number |
| `billing.types.ts` / `billingApi.ts` | `Plan` type has flat `billingInterval`/`priceMonthly`/`priceYearly` | Update `Plan` type to carry a `prices` array; update RTK Query response mapping accordingly |

---

## 6. Resulting Owner Experience ("easy for owners to pick one")

- One toggle, Monthly ⟷ Yearly, at the top of the Subscribe page (already built —
  just needs to stop hiding tiers).
- Every tier stays visible and comparable in both toggle states. If a specific tier
  genuinely isn't offered yearly yet, it's shown disabled with a reason, not silently
  removed — the owner should never wonder "where did Starter go?"
- Switching a studio's own subscription between Monthly and Yearly on the *same*
  tier becomes its own explicit action (`BillingInterval` change), separate from
  switching tiers — matching how a real person thinks about the decision ("same
  plan, just bill me yearly" vs. "different plan entirely").

---

## 7. Rollout Sequencing (avoid a repeat of the last two bug reports)

1. Ship the new `PlanPrice` table + `Subscription.BillingInterval` additions
   (Section 3, steps 1–2) with old columns still present and still being read by old
   code paths.
2. Ship the Application-layer changes (Section 4) reading from the new shape,
   contracts updated, with the old flat fields on `Plan` fully unused by that point.
3. Ship the frontend changes (Section 5).
4. Only after a full deploy cycle running clean on the new path: drop the old
   `Plan` columns and the redundant Premium row in a dedicated cleanup migration
   (Section 3, step 5). Do not combine this with step 1 or 2 — that combination is
   exactly how the current mess (stale rows nothing points at, but nothing safely
   removes either) came about in the first place.

---

## 8. Verification Checklist

- [ ] Every tier (Free, Starter, Growth, Premium, Pro) has exactly one `Plan` row
      after migration — query `SELECT Name, COUNT(*) FROM Plans GROUP BY Name
      HAVING COUNT(*) > 1` returns zero rows.
- [ ] Every pre-existing `Subscription` has a non-null `BillingInterval` that matches
      what the studio was actually being charged before the migration (spot-check
      against Stripe's own subscription/price records, not just the local DB).
- [ ] Toggling "Yearly" on `SubscribePage` shows all five tiers (some possibly
      disabled, none silently missing).
- [ ] Changing a studio from Monthly Premium to Yearly Premium no longer changes
      `PlanId` at all — only `BillingInterval` changes, tier stays the same.
- [ ] Issuer Plans page shows exactly one card per tier — six total, no duplicates,
      no orphans.
- [ ] Adding a hypothetical new interval (e.g., Quarterly) to one tier as a test
      requires zero schema changes under the 2.1 design (only a new `PlanPrice` row)
      — confirms the scalability goal is actually met, not just claimed.
