# Spec — Store the Billed Amount on Each Subscription (Batch 2b)

**For:** Engineering Consultation (schema, migration and webhook design are yours to decide).
**From:** Finance project, 2026-09-23. Requested by Phi.
**Status:** Financial requirements final; implementation design open. Turn this into an
overnight prompt under the Overnight Prompt Standard.
**Prerequisites:** Batch 1 (`docs/claude/overnight-prompt-plan-tiers-yearly-2026-09-23.md`) and
Batch 2 (`docs/claude/overnight-prompt-mrr-reporting-fixes-2026-09-23.md`) merged. Batch 2
introduces `MrrRules` (`Pena_e_Arte.Application/Platform/Revenue/MrrRules.cs`), the one place
MRR is computed; this spec changes only its input.
**Context:** Full reasoning in the "Subscription Plans — Solution Plan" doc (Problem 2) and the
Finance project doc `claude/subscription-plan-decisions-2026-09-23.md`.

---

## 1. The problem

Every MRR figure is computed from the `PlanPrice` row a subscription points at, looked up at
read time (`Plan.Prices.FirstOrDefault(pp => pp.Interval == s.BillingInterval).Price`). That
row is the **price list for new sales**, not what an existing studio is billed. Whenever the
two diverge, reported revenue is wrong while Stripe keeps billing correctly:

- a price is changed in code or by an admin (Stripe prices are immutable; existing
  subscribers stay on the old Stripe price);
- a price row is deleted or deactivated while studios are on it;
- a studio is on a discount (referral coupons, and any future permanent coupon).

This is the same bug class as the pre-July `Plan.PriceMonthly` MRR overstatement: a metric
reading a display/list price instead of the billed amount. Batch 2 blocks the three editing
paths with guards (G1–G4). This spec removes the root cause so G2 can be relaxed and price
changes with grandfathering become possible.

No studio is subscribed to a paid plan as of 2026-09-23, so the backfill only touches
demo/test data. It's cheapest to ship this before the first real subscriber.

---

## 2. Financial requirements

### R1 — Snapshot of what Stripe bills, per subscription

Each `Subscription` must carry, at minimum:

| Field (suggested name) | Meaning | Source |
|---|---|---|
| `BilledUnitAmount` | Recurring price per billing interval, **before** temporary discounts | Stripe subscription item `price.unit_amount` ÷ 100 |
| `BilledQuantity` | Item quantity (always 1 today — pricing is per studio) | Stripe item `quantity` |
| `BilledCurrency` | ISO 4217 code | Stripe `price.currency` |
| `BilledInterval` | Can reuse the existing `Subscription.BillingInterval` | Stripe `price.recurring.interval` |
| `RecurringDiscountPercent` | Only for `forever` coupons (none exist today) | Stripe `discounts[].coupon` with `duration = forever` |

Nullable until populated. Cash-billed studios have no Stripe object: snapshot the plan's
Monthly `PlanPrice.Price` at activation, in the platform currency (EUR — `StripeDemoSeeder`
creates prices with `Currency = "eur"`).

### R2 — The MRR formula

`MrrRules.MonthlyEquivalent(s)` becomes:

```
MRR(s) = BilledUnitAmount × BilledQuantity × (1 − RecurringDiscountPercent / 100) ÷ months per interval
months per interval: Monthly = 1, Yearly = 12
```

While `BilledUnitAmount` is null, fall back to today's `PlanPrice` lookup, and have the MRR
queries log (Serilog, count only) how many subscriptions used the fallback, so it's visible
when the backfill is complete. Every status/window rule from Batch 2 is unchanged.

### R3 — Contracted MRR; discounts reported separately (decided by Phi, 2026-09-23)

Temporary discounts do **not** reduce MRR. That covers the referral reward in all three forms
Batch 1 produces:

- monthly referred studio: 100% off, `repeating`, 1 month;
- yearly referred studio: `amount_off` of one monthly price, `once`;
- yearly referrer: customer balance credit of one monthly price.

The admin dashboard needs a **"Discounts this month"** figure: the sum of discount and credit
amounts actually applied to subscription invoices paid in the current calendar month. Stripe's
`invoice.paid` payload carries `total_discount_amounts` and the starting/ending balance, and
`invoice.paid` is already handled (`HandleInvoicePaidCommand`). How you store it (a per-invoice
record, or a monthly aggregate) is your call. A per-invoice record also serves the Batch 3
refund calculation ("amount actually paid") and the revenue ledger, so it's the Finance
preference.

### R4 — Snapshot must follow every billing change

Populate or refresh the snapshot wherever the billed amount can change:

| Path | File |
|---|---|
| Checkout completed / finalize | `ActivateCheckoutSubscriptionCommand.cs` |
| Direct subscription create | `CreateSubscriptionCommand.cs` |
| Any `customer.subscription.updated` (plan change, interval change, proration landing, discount added/removed) — on **every** event, not only when the price id changes | `BillingEndpoints.HandleBillingWebhook` → `HandleSubscriptionUpdatedCommand.cs` |
| Cash activation by admin | `ActivateSubscriptionManuallyCommand.cs` |
| Demo provisioning | `StripeDemoSeeder.cs` |

`HandleSubscriptionDeletedCommand` doesn't need to touch it (status goes to Cancelled; the
window rules already exclude it).

### R5 — Relax guard G2 once R1–R4 ship

Batch 2's `UpdatePlanHandler` G2 rejects changing a `PlanPrice.Price`/`StripePriceId` that
studios are on. After this ships, allow it: existing subscribers keep their snapshot (and
their Stripe price) and are grandfathered; only new sales use the new price. G1 (never
delete a price in use), G3 (Stripe price must match) and G4 (reconciler never overwrites a
linked price) stay.

### R6 — Backfill

One-off, admin-only, idempotent: for each subscription with a `StripeSubscriptionId`, retrieve
it from Stripe and write the snapshot; for cash-billed, snapshot the current Monthly
`PlanPrice.Price` and list them for the admin to confirm. With zero paying studios today this
is demo data only, but it must exist for staging and future re-runs.

### R7 — Currency

All MRR arithmetic assumes one currency. If any snapshot's currency differs from the platform
currency, exclude it from MRR and log a warning. Don't convert.

---

## 3. Acceptance tests (financial)

| Scenario | Expect |
|---|---|
| Premium studio billed 79; code/admin price changed to 89; redeploy | That studio's MRR stays 79; a new checkout bills and reports 89 |
| Premium yearly billed 790 | MRR 65.83… (790 ÷ 12, full decimal precision) |
| Referred Growth monthly studio in its free month | MRR 59; "Discounts this month" 59 |
| Referred Growth yearly studio | MRR 49.17 (590 ÷ 12); first invoice 531; "Discounts this month" 59 in the month it's paid |
| Yearly referrer receives a balance credit of 79 | MRR unchanged; the credit appears in "Discounts this month" in the month the renewal invoice uses it |
| Admin drops Yearly from Premium while studios are on it | G1 deactivates it; their MRR is unchanged |
| Snapshot null (pre-backfill) | Falls back to the price list; fallback count logged |
| Snapshot currency USD on an EUR platform | Excluded from MRR; warning logged |
| Stripe dashboard spot check: 3 studios (monthly, yearly, referred) | Each studio's MRR matches Stripe's own subscription amount ÷ interval months |

---

## 4. Out of scope here

- Refund flow for yearly cancellations and the revenue event ledger (Batch 3). Design the
  invoice record so both can reuse it.
- Tax/VAT: MRR is net of tax. If Stripe Tax is ever enabled, `unit_amount` must stay the
  tax-exclusive figure. Flag if that isn't how prices are configured.
- The owner-facing `/billing/plans` data leak (separate Engineering Consultation item from
  Batch 1's final report).
