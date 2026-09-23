# Spec — Yearly Cancellation Refunds and Revenue Ledger (Batch 3)

**For:** Engineering Consultation. Schema, migrations, webhook handling and Stripe mechanics
are yours to design; the financial rules below are final.
**From:** Finance project, 2026-09-23. Requested by Phi.
**Status:** Financial requirements final. Turn this into one or two overnight prompts under the
Overnight Prompt Standard. The refund flow is the launch blocker; the ledger can follow.
**Prerequisites, in order:**
1. Batch 1 — `docs/claude/overnight-prompt-plan-tiers-yearly-2026-09-23.md` (four tiers, yearly
   prices, referral fix).
2. Batch 2 — `docs/claude/overnight-prompt-mrr-reporting-fixes-2026-09-23.md` (`MrrRules`,
   suspension pauses billing, and D7: yearly → monthly waits for the end of the paid year).
3. Batch 2b — `docs/claude/spec-mrr-billed-amount-snapshot-2026-09-23.md` (billed-amount
   snapshot and an invoice record). The refund needs "amount actually paid" per yearly
   invoice; reuse that record rather than building a second one.

**Why it matters:** Starter (€290) and Growth (€590) yearly prices exist but stay unlinked
(not purchasable) until this refund flow ships, because the terms will promise the refund.
Premium yearly (€790) is already purchasable; until this ships, its cancellations are refunded
by hand in the Stripe dashboard using the table in §2.3.

No studio is subscribed to a paid plan as of 2026-09-23.

---

## Part A — Yearly cancellation and refunds

### A1. The rule (decided by Phi, 2026-09-23)

A studio that cancels a **yearly** plan gets back what it paid for the current year, minus the
months it used, charged at the **monthly** price. Access ends on the day of cancellation.

```
refund = max(0, amount paid − months used × monthly reference price)
```

- **Amount paid** = cash actually collected on the yearly invoice for the current period (after
  any referral `amount_off` coupon; customer-balance credits are not cash and are not refunded).
- **Months used** = months *started* since the current yearly period began; a started month
  counts in full. Count by monthly anniversaries of the period start
  (`periodStart.AddMonths(k) <= cancelledAt` → month k+1 has started). Cancelling on the first
  day = 1 month used. For month-end starts, use .NET `AddMonths` clamping (31 Jan → 28/29 Feb).
- **Monthly reference price** = the tier's Monthly price **in effect when this yearly period was
  purchased or renewed**, recorded on the invoice record (Batch 2b), not read from today's
  price list. That's the same display-price-vs-billed-amount bug class this whole effort fixes.
- All arithmetic in integer cents.
- Refund = 0 once months used × monthly price ≥ amount paid (10+ months at list price).

Reasoning (for the record, not to re-open): a time-based refund (790 × 9/12 = 592.50) would turn
yearly into a cheaper monthly plan with no commitment. Charging used months at the monthly price
means an early leaver pays what monthly would have cost, and a stayer keeps the 2 free months.

### A2. Worked amounts (acceptance values)

| Tier (paid) | Leaves in month 1 | Month 3 | Month 6 | Month 10+ |
|---|---|---|---|---|
| Starter (290) | 261 | 203 | 116 | 0 |
| Growth (590) | 531 | 413 | 236 | 0 |
| Premium (790) | 711 | 553 | 316 | 0 |
| Growth, referred (paid 531) | 472 | 354 | 177 | 0 |

### A3. What happens on cancellation

| Situation | Behaviour |
|---|---|
| Owner cancels a **yearly** plan | Show a quote first ("You'll get €553 back and lose access today"). On confirm: cancel the Stripe subscription **immediately, without proration**; refund the computed amount against the current yearly invoice's payment; status `Cancelled`; access ends now (read-only → `/subscribe` per `TenantMiddleware` rule 6). |
| Owner cancels a **monthly** plan | Unchanged policy: `cancel_at_period_end = true`, access to `CurrentPeriodEnd`, no refund. Offer "Keep my plan" until then. |
| Yearly studio has a **scheduled plan change** (`PendingPlanId`, a Stripe subscription schedule) | Release the schedule first, then cancel. The quote uses the current period's invoice. |
| Yearly renewal **failed** (PastDue, nothing paid for the new period) | Refund 0; cancel immediately. The previous year was fully used. |
| **Suspended** studio (billing paused, Batch 2 D6) | Suspended months count as used. Admin decides via override (A5). |
| **Cash-billed** studio | Monthly only; no refund path. |
| Leftover **customer-balance credit** (referral reward) | Not refunded as cash; it lapses with the subscription. |
| Cancel requested twice / network retry | Exactly one Stripe refund (idempotency key = subscription id + current period start). |

### A4. Owner flow

- Today owners cancel inside the Stripe customer portal (`CreateBillingPortal` →
  `CreatePortalSessionAsync`, no `configuration` passed, so the dashboard default applies).
  The portal can't apply A1. **Turn cancellation off in the portal** (a dedicated portal
  configuration created via API and passed on every session, or the dashboard setting; your
  choice). Keep payment-method and invoice management in the portal.
- Add a "Cancel plan" action on `BillingPage.tsx`, with a quote query (refund amount, months
  used, access-end date) shown before confirming, and a clear irreversible-action dialog.
- Confirmation email: refund amount, "allow 5–10 business days", date access ended, and how
  to resubscribe.

### A5. Admin flow

`CancelSubscriptionCommand` (admin) uses the same calculator by default. The admin may
override to **full refund** or **no refund**, with a required reason. Amount, rule used and
reason go into the audit metadata. No other amounts are allowed (no free-typed refunds in v1).

### A6. Refund record (requirements)

Persist every refund with at least: studio, subscription, Stripe refund id, invoice id, amount,
currency, months used, amount paid, monthly reference price, rule
(`YearlyFormula` | `AdminFull` | `AdminNone`), initiated by (owner/admin user id), reason
(admin), created at, and status (`Pending` → `Succeeded` | `Failed`), updated from Stripe's
refund webhooks. A `Failed` refund must alert the admin (error log + visible on the studio's
admin page); the studio stays cancelled.

### A7. Reporting

- Admin dashboard: **Refunds this month** = sum of `Succeeded` refunds created this calendar month.
- MRR: the cancellation is a churn movement on the cancellation date (Part B). Refunds don't
  change MRR; they're cash.
- For Pena e Artë's own books: yearly payments are cash received up front but revenue earned
  monthly (deferred revenue); a refund reverses the unearned part. That's for the accountant,
  not code. Exporting refunds with their dates is enough.

### A8. Launch checklist (after Part A ships) — for Phi / an admin

1. In Stripe **live mode**, create yearly recurring prices: Starter €290, Growth €590.
2. Link each in Plan management (Batch 2's G3 check verifies amount and interval).
3. Confirm portal cancellation is off in live mode.
4. Publish the terms: yearly refund rule with the Premium example; monthly = no refund; no
   refund of the paid period on suspension for policy violations. Have the wording checked
   by a lawyer (outside this project).
5. Only then add the refund rule to Help and the manual (A9). Batches 1–2 deliberately left it out.

### A9. Help and manual (CLAUDE.md rule 7, same change)

- Owner: new article "Cancel your plan": monthly vs yearly behaviour, the formula in words,
  the Premium example, "access ends when you cancel a yearly plan", refund timing.
- Owner "Choose or change your plan": link to it.
- Admin: cancel article gains the override options and the audit note.
- Manual: mirror both; tours only if a new button is introduced into a toured screen.

### A10. Acceptance tests (financial)

| Scenario | Expect |
|---|---|
| Premium yearly bought 1 Jan, cancelled 11 Mar | Months used 3; refund €553; status Cancelled; access ended 11 Mar |
| Same, cancelled 1 Jan (same day) | Months used 1; refund €711 |
| Bought 31 Jan, cancelled 29 Feb (leap year) | Months used 2 (anniversary clamped to 29 Feb) |
| Premium yearly, cancelled in month 11 | Refund €0; cancelled immediately |
| Referred Growth yearly (paid €531), month 3 | Refund €354 |
| Monthly reference price was 79 at purchase; list price later changed to 89 | Formula uses 79 |
| Renewal failed (PastDue), owner cancels | Refund €0 |
| Cancel sent twice | One Stripe refund |
| Admin override "full" with reason | Refund = amount paid; audit metadata has rule, amount, reason |
| Stripe refund fails | Record `Failed`; admin alerted; studio still cancelled |
| Monthly studio cancels | `cancel_at_period_end`; no refund; access to period end |

---

## Part B — Revenue ledger and MRR movements

Replaces Batch 2's reconstructed history (D3) with recorded history, and removes the chart
caption "Past months are estimated…" for the months the ledger covers.

### B1. Requirement

Record every change to a subscription's MRR as an event, so any past month's MRR is a sum of
recorded values, never a reconstruction. Suggested shape (yours to finalise):
`SubscriptionRevenueEvent { Id, SubscriptionId, StudioId, OccurredAt, Type, PlanId, Interval,
MrrBefore, MrrAfter, Source, StripeEventId? (unique, for webhook idempotency) }`. Admin-only
reads; not tenant-scoped.

`MrrBefore` / `MrrAfter` are monthly-equivalent contracted MRR computed by `MrrRules` from
the Batch 2b snapshot (never from the price list).

### B2. Event types and where they're written

| Type | Meaning | Written from |
|---|---|---|
| `New` | 0 → >0, first paid subscription | `ActivateCheckoutSubscriptionHandler`, `CreateSubscriptionHandler`, `ActivateSubscriptionManuallyHandler` |
| `Expansion` | MRR up | `HandleSubscriptionUpdatedHandler` (upgrade lands), `ChangePlanHandler` immediate upgrade |
| `Contraction` | MRR down, still > 0 | `HandleSubscriptionUpdatedHandler` (scheduled downgrade lands) |
| `Churn` | >0 → 0 | `HandleSubscriptionDeletedHandler`, `CancelSubscriptionHandler`, owner yearly cancel (A3), `GracePeriodEndJob` only if the studio had paid |
| `Reactivation` | 0 → >0 for a studio that churned before | the same activation handlers |
| `Paused` / `Resumed` | Suspension (Batch 2 D6) | `SuspendStudioHandler` / `UnsuspendStudioHandler` |
| `PastDue` / `Recovered` | Moves between MRR and at-risk | `HandleSubscriptionUpdatedHandler` status transitions |

Free-plan subscriptions (MRR 0) produce no events. Referral discounts produce no events
(contracted MRR).

### B3. Queries

- **MRR at time t** = for each subscription, the latest event with `OccurredAt <= t`: its
  `MrrAfter` counts toward MRR if the resulting state is billing (not paused, not past-due,
  not churned). This must equal `MrrRules.MrrAt(…, now)` for t = now. Test that equality.
- `GetMrrHistoryQuery`: month-end points from the ledger for months on or after the ledger
  start; Batch 2's reconstruction for earlier months, marked `IsEstimated = true` per point
  (add the flag to `MrrDataPointResponse`), so the chart can shade them.
- New **MRR movements** query/chart (admin dashboard): per month, stacked bars for new,
  expansion, reactivation (positive), contraction and churn (negative), plus a net line. This
  is the standard ChartMogul/Baremetrics view.
- KPI tiles (monthly):
  - Gross revenue retention = (start MRR − contraction − churn) ÷ start MRR
  - Net revenue retention = (start MRR + expansion − contraction − churn) ÷ start MRR
  - "Start MRR" = MRR at the end of the previous month, existing customers only (new and
    reactivation excluded). Show "—" when start MRR is 0.

### B4. Backfill

One event per currently-paid subscription: `New` at Batch 2's conservative start date,
`Source = Backfill`. Ledger start = the backfill run date. Earlier months stay estimated. With
no paying studios today, this is demo data only. Ship the ledger before the first real
subscriber and history is complete from day one.

### B5. Acceptance tests (financial)

| Scenario | Expect |
|---|---|
| Growth monthly from 1 Feb; upgrade to Premium monthly 10 Mar; downgrade back to Growth lands 10 May | `New` 59 (Feb); `Expansion` 59→79 (Mar); `Contraction` 79→59 (May); end-Feb 59, end-Mar 79, end-Apr 79, end-May 59 |
| Growth yearly → cancelled with refund 11 Mar | `Churn` 49.17→0 on 11 Mar; end-Mar MRR excludes it; refund on its own report |
| Suspended 12 May, unsuspended 3 Jun | `Paused` then `Resumed`; end-May MRR excludes it; end-Jun includes it |
| Renewal fails 5 Jul, paid 9 Jul | `PastDue` then `Recovered`; at-risk between |
| Stripe sends the same webhook twice | One event (unique `StripeEventId`) |
| Upgrade today | No past month's MRR changes |
| Ledger MRR at now vs `MrrRules` at now | Equal to the cent |

---

## Out of scope

- VAT/tax (MRR and refunds net of tax; flag if Stripe Tax is ever enabled).
- Automatic cancellation after long suspension.
- The owner `/billing/plans` data leak (separate Engineering Consultation item).
