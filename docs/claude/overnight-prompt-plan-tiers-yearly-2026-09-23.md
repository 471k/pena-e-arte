# Overnight Prompt — Four-Tier Plan Catalogue, Yearly on Every Paid Tier, Referral Coupon Fix, Computed Yearly Saving (Batch 1)

> Feed this file directly to Claude Code as the task prompt. It is self-contained: exact
> files, exact current code (re-read from live source on 2026-09-23, commit `0dbe9371`),
> exact target behaviour, exact tests, exact docs to sync. Read the whole file before writing
> anything — later phases depend on decisions in §2–§4.

**Date logged:** 2026-09-23
**Requested by:** Phi
**Origin:** Finance project subscription-plan audit, 2026-09-23. Full reasoning, benchmarks
and the later batches live in the "Subscription Plans — Solution Plan" doc (claude.ai
artifact `74393df7-9f43-4e95-b1e8-03cbbb7f77b6`) and the project doc
`claude/subscription-plan-decisions-2026-09-23.md`. This file is Batch 1 of 3: plan
catalogue, pricing surface, referral coupon bug, docs. Batch 2 (MRR reporting fixes) and
Batch 3 (yearly refund flow + revenue ledger) are **not** in scope tonight — see §4.
**Mode:** Fully autonomous, no user present. Do not stop to ask questions. Where a product
decision is still open it is listed in §3 with a default — take the default and keep moving.
Where this file states a fact about current code it was re-read on 2026-09-23 — if your
checkout disagrees, trust your checkout, note the drift in your final report, and do not
silently change approach.

**Before starting**, run:

```bash
git add -A && git commit -m "checkpoint: before plan tiers batch 1" --allow-empty
git checkout -b feature/plan-tiers-yearly-batch1
```

Commit at the end of **each** phase (§5–§8), not only at the very end, so each phase is
independently reviewable and revertable.

---

## 1. Goal

Move the plan catalogue from five tiers to four (Free, Starter, Growth, Premium), give every
paid tier a yearly price at monthly × 10, close a live bug where a referral coupon makes a
yearly plan's whole first year free, replace the hand-typed "save 17%" figure with one
computed from real prices, and bring the manual, Help and `architecture.md` in line with the
result.

Nothing here changes what an existing subscriber pays: **no studio is subscribed to a paid
plan today** (confirmed by Phi, 2026-09-23), so no one is moved, migrated or grandfathered.

Applicable `CLAUDE.md` rules: #2 (every touched endpoint keeps its `.RequireAuthorization()`
policy; no new `AllowAnonymous`), #3 (no PII in logs — log `StudioId`/`PlanId` only), #4
(no Stripe keys in source), #5 (Serilog only), #6 (benchmark — see §10), #7 (Help sync — its
own phase, §8, in this same branch, never a follow-up).

---

## 2. Decisions already made — implement as specified, do not re-litigate

### 2.1 D1 — Four core tiers; Pro is retired

Target catalogue (these are the values `DataSeeder.ReconcileCoreTiersAsync` must reconcile to):

| Tier | Id constant | Monthly € | Yearly € | Artists | Appts/mo | Notifs/mo | Storage GB | Locations | Branding removal | API access | Priority support | Campaigns | `YearlyDiscountPercent` |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Free | `FreePlanId` | 0 | — | 1 | 15 | 50 | 1 | `null` | false | false | false | false | 0 |
| Starter | `StarterPlanId` | 29 | 290 | 1 | 40 | 150 | 2 | `null` | false | false | false | false | 17 |
| Growth | `GrowthPlanId` | 59 | 590 | 3 | 150 | 600 | 10 | `null` | true | false | false | true | 17 |
| Premium | `PremiumPlanId` | 79 | 790 | 10 | 1000 | 2500 | 50 | `null` | true | **true** | false | true | 17 |

Pro (`ProPlanId`, `aaaa0003-…`) is **retired, not deleted**: its `Plan` row stays wherever it
already exists (subscriptions and history may reference it), every one of its `PlanPrice`
rows is set `IsActive = false`, and a fresh database never gets a Pro row at all.

Why (for your understanding, not to re-argue): the benchmark set sells three or four tiers,
separated by team size and real features. Pro differed from Premium only by capacity plus
API access, so Premium absorbs Pro's limits and its one real feature at €79.

### 2.2 D2 — `AllowApiAccess` is a real, shipped feature

Re-verified tonight: `GenerateStudioApiKeyCommand` (line 30), `UpsertWebhookEndpointCommand`
(line 32), `UpdateStudioBrandingCommand`, `GetMyStudioQuery`, `DeveloperSettingsCard.tsx` and
`WebhookSettingsCard.tsx` all gate on `Plan.AllowApiAccess`. The comments in `Plan.cs`
(on `AllowMarketingCampaigns`) and `SendCampaignCommand.cs` (line 33) that call it
"sold-but-undelivered with zero backing implementation" are **stale** — fix the wording, and
un-hide the `allowApiAccess` toggle in `PlanEditPage.tsx` (lines 442–444). `PrioritySupport`
genuinely has no implementation: it stays hidden and is `false` on every core tier.

### 2.3 D3 — Locations are not advertised

`PlanLimitService` (line ~164) hardcodes a studio's location usage to 1 because multi-location
isn't modelled, so no `MaxLocations` value can ever bite. Set it to `null` on every core tier,
remove the "Locations" row from the owner's usage card (`BillingPage.tsx` line 109), and
leave the admin field in `PlanEditPage.tsx` with the helper text "Not enforced yet —
multi-location isn't built." Keep the column and the `QuotaType.Locations` plumbing.

### 2.4 D4 — Yearly on every paid tier, monthly stays the default

Starter and Growth gain a Yearly `PlanPrice` (290 / 590). The reconciler inserts them with
`StripePriceId = null`, exactly as it already does for every new price row. **Do not create
or link live Stripe prices** — a human does that after Batch 3 ships the refund flow (§3).
The subscribe page must therefore treat a paid price with no `StripePriceId` as not
purchasable (today it only checks `IsActive`, so it would let the owner select it and then
fail at checkout with "This plan is not available for online checkout"). The billing-cycle
toggle keeps defaulting to Monthly.

### 2.5 D5 — Free is "Free" in both toggle states

With the toggle on Yearly, the Free card is still selectable, shows "Free", and activating it
always sends `billingInterval: "Monthly"` (Free has no yearly price row and must not get one).

### 2.6 D6 — The referral coupon must not make a yearly plan free

Current behaviour: `StripeDiscountService.CreateOneMonthFreeCouponAsync` creates
`PercentOff = 100, Duration = "repeating", DurationInMonths = 1`. Stripe applies a repeating
coupon to every invoice inside that first month; a yearly subscription has exactly one invoice
in that window, so the referred studio pays **0 for the whole year**. It is attached
regardless of interval by `CreateSubscriptionCheckoutHandler.ResolveReferralCouponAsync` and
`CreateSubscriptionHandler` (the coupon block around lines 52–80). The referrer side
(`ReferralRewardService`, ~lines 96–140) has the mirror problem: on a yearly referrer the
coupon either expires unused or zeroes a whole renewal.

Target — the reward is always worth **one month of the tier's monthly price**:

- Referred studio, Monthly price → unchanged (100% off, repeating, 1 month).
- Referred studio, Yearly price → `AmountOff` = one monthly price in minor units, `Currency`
  = that Stripe price's currency, `Duration = "once"`.
- Referrer, Monthly subscription → unchanged (coupon applied to the active subscription).
- Referrer, Yearly subscription → a Stripe **customer balance credit** of one monthly price
  (negative `CustomerBalanceTransaction`), which Stripe applies to the next invoice (the
  renewal). No coupon.

"One monthly price" = the tier's Monthly `PlanPrice.Price` (active or not). If a tier has no
Monthly row, use `Math.Round(Yearly / 12m, 2)`.

### 2.7 D7 — The yearly saving is computed, never typed

The owner-facing label comes from the two real prices:

- Months free (`(Monthly × 12 − Yearly) / Monthly`) is a whole number → "2 months free"
  ("1 month free" for 1).
- Otherwise → "save N%" with N = **floor**((1 − Yearly / (Monthly × 12)) × 100). Floor, so the
  label can never overstate the saving.
- No active Monthly and Yearly, Monthly = 0, or a saving ≤ 0 → no label (log a warning for
  saving ≤ 0).

`Plan.YearlyDiscountPercent` stays in the schema and API, but becomes an admin-only input to
the "suggested yearly price" helper. No owner-facing surface may read it after tonight.

### 2.8 D8 — Four phases, each committed separately, in one session

§5 catalogue → §6 referral coupon → §7 computed saving → §8 docs/Help. Each phase includes its
own tests. Help sync is its own phase only because it spans all three; do not skip it.

---

## 3. Flag, don't decide — do not build blind

- **Yearly refund flow and linking live Stripe yearly prices for Starter/Growth.** Batch 3.
  The agreed rule (refund = amount paid − months used × monthly price, started months count
  in full) is **not** built tonight, and must not be documented in Help or the manual yet.
  Leave `StripePriceId` null on the new yearly rows.
- **Owner-facing `GET /api/v1/billing/plans` leaks platform data.** It returns
  `SubscriberCount` (platform-wide studios per plan) and each price's `StripePriceId` to every
  owner. Report it in your final report for Engineering Consultation; do not change the
  response shape tonight (the admin plans page reads the same endpoint).
- **`ReferralRedemption.ReferrerRewardCouponId`** will hold a Stripe customer-balance
  transaction id (`cbtxn_…`) for yearly referrers after §6. Report it as a naming follow-up;
  no migration tonight.
- **Referrer with no active Stripe subscription** — the existing `TODO(product)` in
  `ReferralRewardService` stays as is.
- **`PrioritySupport` column** — stays in the schema, `false` everywhere, still hidden.

---

## 4. Scope boundary — do not touch

- `GetPlatformStatsQuery`, `GetMrrHistoryQuery`, any MRR/ARR logic — Batch 2.
- `Subscription` schema, any migration. **This batch adds no migration.** If you find you
  need one, stop and report why.
- `UpdatePlanCommand` / `CreatePlanCommand` / `DeletePlanCommand` behaviour and validators —
  Batch 2 adds the price-drift guards.
- Stripe webhook handlers (`HandleSubscriptionUpdatedCommand`, `HandleInvoicePaidCommand`,
  `HandleSubscriptionDeletedCommand`, `BillingEndpoints.HandleBillingWebhook`).
- Owner cancellation, `CreateBillingPortal`, refunds — Batch 3.
- `PromoCode` (a studio-level client deposit discount, unrelated to subscriptions).
- Payment aggregator / POK / deposit code.
- `docs/user-manual.html` (stale copy) — only `frontend/public/user-manual/index.html` is live.

---

## 5. Phase A — Plan catalogue

### 5.1 `DataSeeder.ReconcileCoreTiersAsync`

File: `Pena_e_Arte.Infrastructure/Persistence/Seed/DataSeeder.cs`, the `tiers` array
(currently lines ~244–266). Current `CoreTier` positional order:
`(Id, Name, YearlyDiscountPercent, AllowBrandingRemoval, AllowApiAccess, PrioritySupport,
AllowMarketingCampaigns, MaxArtists, MaxAppointmentsPerMonth, MaxNotificationsPerMonth,
MaxStorageGb, MaxLocations, Prices)`.

Target array (exactly D1):

```csharp
CoreTier[] tiers =
[
    new CoreTier(FreePlanId, "Free", 0, false, false, false, false,
        1, 15, 50, 1, null,
        [new TierPrice(BillingInterval.Monthly, 0m)]),
    new CoreTier(StarterPlanId, "Starter", 17, false, false, false, false,
        1, 40, 150, 2, null,
        [new TierPrice(BillingInterval.Monthly, 29m), new TierPrice(BillingInterval.Yearly, 290m)]),
    new CoreTier(GrowthPlanId, "Growth", 17, true, false, false, true,
        3, 150, 600, 10, null,
        [new TierPrice(BillingInterval.Monthly, 59m), new TierPrice(BillingInterval.Yearly, 590m)]),
    new CoreTier(PremiumPlanId, "Premium", 17, true, true, false, true,
        10, 1000, 2500, 50, null,
        [new TierPrice(BillingInterval.Monthly, 79m), new TierPrice(BillingInterval.Yearly, 790m)]),
];
```

Update the block comment above it: "five tiers" → "four tiers", and add one line: "Pro retired
2026-09-23 (four-tier catalogue) — see RetireTiersAsync below and architecture.md Decisions Log."

### 5.2 Retirement step

In the same class, after the tier loop and before `SaveChangesAsync`:

```csharp
// Retired core tiers: the Plan row is kept (subscriptions/history may reference it),
// every price is deactivated so it can't be bought, and a fresh DB never gets the row.
private static readonly Guid[] RetiredTierIds = [ProPlanId];
```

Set `IsActive = false` on every `PlanPrice` whose `PlanId` is in `RetiredTierIds`. Do **not**
insert a Pro row if none exists. Keep the `ProPlanId` constant (existing databases have it).
Idempotent: running twice changes nothing the second time.

### 5.3 `GetPlansQuery` — hide retired plans from owners

File: `Pena_e_Arte.Application/Billing/Queries/GetPlansQuery.cs`.

- Change to `public record GetPlansQuery(bool IncludeRetired = false) : IRequest<List<PlanResponse>>;`
- When `IncludeRetired` is false, filter `p.Prices.Any(pp => pp.IsActive)`.
- `BillingEndpoints.GetPlans` (the `MapGet("/plans", …)` handler, policy `OwnerOnly`, which
  admits `owner` and `admin`): pass `IncludeRetired: user.IsInRole("admin")`. Admin's
  `useGetAdminPlansQuery` hits the same route, so the admin page keeps seeing Pro.
- `PlanManagementPage.tsx`: a plan with no active price shows a "Retired" badge, and its
  delete/edit affordances stay as they are. If the card's limit summary (~line 92) lists
  locations, remove it.

### 5.4 Subscribe page — purchasable prices and Free in yearly view

File: `frontend/src/features/billing/components/SubscribePage.tsx` (+ `billing.types.ts`).

- Add `purchasablePriceFor(plan, interval)` in `billing.types.ts`: `priceFor(plan, interval)`
  and (`price === 0` or `stripePriceId !== null`). Use it for `plansWithPrice` (line ~127)
  and `selectedPrice` (line ~130) on this page only. `BillingPage.tsx` keeps `priceFor` (it
  must still display a current plan even if unlinked).
- Free card: when the toggle is Yearly and the plan's Monthly price is 0, use the Monthly
  price for that card (selectable, shows "Free", no "billed yearly" text).
- `onSubscribe` free path (line ~152): always send `billingInterval: "Monthly"` for Free.
- The unavailable text "Not available on this billing cycle yet" stays for paid tiers whose
  yearly price is unlinked.

### 5.5 `StripeDemoSeeder`

File: `Pena_e_Arte.Infrastructure/Persistence/Seed/StripeDemoSeeder.cs`, step 1 (~lines 51–62).
Skip `!pp.IsActive` rows. Replace the comment ("Starter/Growth/Pro correctly get Monthly
only…") with: "Every active PlanPrice row gets a test-mode Stripe price. Retired tiers'
prices are inactive and skipped." The demo subscription must never land on Pro; with the
filter it picks the demo studio's own plan (Growth) as today.

### 5.6 Owner usage card, stale comments, admin toggle

- `BillingPage.tsx` line 109: remove the Locations `UsageRow`.
- `Plan.cs` comment on `AllowMarketingCampaigns` and `SendCampaignCommand.cs` line 33:
  reword so they no longer call `AllowApiAccess` undelivered ("`AllowApiAccess` gates the
  read-only API keys and webhooks; `PrioritySupport` has no implementation and stays hidden").
- `PlanEditPage.tsx` lines 442–444: un-hide the `allowApiAccess` toggle (label "API access &
  webhooks"); keep `prioritySupport` hidden with its existing comment. Add the Locations
  helper text from D3.

### 5.7 Tests (Phase A)

`tests/Pena_e_Arte.UnitTests/Infrastructure/DataSeederPlanReconciliationTests.cs` — update and add:

| Test | Expect |
|---|---|
| Empty DB | 4 plans: Free, Starter, Growth, Premium; no Pro row |
| Empty DB price rows | 7 (Free 1, Starter 2, Growth 2, Premium 2) |
| Premium values | Artists 10, Appts 1000, Notifs 2500, Storage 50, Locations null, AllowApiAccess true, PrioritySupport false |
| Existing Pro row with Monthly 99 price | Row kept, its price `IsActive == false`, price value untouched |
| Existing Starter Yearly row with a real `StripePriceId` | `StripePriceId` untouched |
| Called twice | Idempotent, same counts |

Rename/replace the existing `…InsertsAllFiveCanonicalPlans`, `…InsertsSixPlanPriceRows` and
`…ProMissingMaxFields…` tests accordingly (the last becomes the retirement test).

`tests/Pena_e_Arte.UnitTests/Billing/GetPlansHandlerTests.cs`: plan with only inactive
prices is excluded when `IncludeRetired = false`, included when true.

Integration: `CreateSubscriptionCheckout` for Pro's (inactive) Monthly price is rejected —
the handler's existing `pp.IsActive` filter (line ~55) already does this; add the test.

`frontend/src/features/billing/__tests__/SubscribePage.test.tsx`: (1) Yearly toggle — a paid
tier whose yearly `stripePriceId` is null renders disabled with the unavailable text; (2) Free
is selectable in Yearly view and activation sends `billingInterval: "Monthly"`; (3) a retired
plan is absent (mock response simply omits it).

Commit: `feat: four-tier plan catalogue, yearly prices for Starter/Growth, retire Pro (Phase A)`

---

## 6. Phase B — Referral coupon fix

### 6.1 Discount service

`Pena_e_Arte.Domain/Interfaces/IStripeDiscountService.cs` — replace the single method with:

```csharp
public sealed record ReferralCouponRequest(
    BillingInterval Interval, string StripePriceId, decimal MonthlyPrice, string IdempotencyKey);

Task<string> CreateReferralCouponAsync(ReferralCouponRequest request, CancellationToken ct);
```

`StripeDiscountService`:

- `Monthly` → today's options (`PercentOff = 100, Duration = "repeating", DurationInMonths = 1`).
- `Yearly` → retrieve the Stripe price (`PriceService.GetAsync(StripePriceId)`) for its
  currency; create `AmountOff = (long)Math.Round(MonthlyPrice * 100m)`, `Currency = price.Currency`,
  `Duration = "once"`, `Name = "Referral: 1 month free"`.
- Idempotency key is passed in; callers build it as
  `referral-coupon-{studioId}-{stripePriceId}` so a studio that abandons a monthly checkout and
  then picks yearly doesn't collide with Stripe's 24-hour idempotency cache.

### 6.2 Call sites (referred studio)

- `CreateSubscriptionCheckoutHandler.ResolveReferralCouponAsync(Studio studio, …)` → also takes
  the resolved `PlanPrice price` and the plan's monthly-equivalent (D6 rule). Keep every
  existing validity check and the non-fatal `catch`.
- `CreateSubscriptionHandler` coupon block → same change; keep the `price.Price > 0` guard.
- Put the "one monthly price" rule in one place: `Pena_e_Arte.Application/Billing/ReferralRewardAmount.cs`
  (static, pure): `decimal OneMonthOf(Plan plan)` = Monthly `PlanPrice.Price` if a Monthly row
  exists (active or not), else `Math.Round(Yearly / 12m, 2)`.

### 6.3 Referrer reward

`IStripeBillingService` — add:

```csharp
/// Credits the subscription's customer balance (applied by Stripe to the next invoice).
/// Returns the customer balance transaction id.
Task<string> CreditCustomerBalanceAsync(
    string stripeSubscriptionId, decimal amount, string idempotencyKey, string description, CancellationToken ct);
```

`StripeBillingService`: retrieve the subscription for `Customer` and `Currency`, then create a
`CustomerBalanceTransaction` with `Amount = -(long)Math.Round(amount * 100m)`.

`ReferralRewardService`: load the referrer subscription's `Plan` (+ `Prices`). If
`referrerSub.BillingInterval == Monthly` → today's coupon path via
`CreateReferralCouponAsync(Monthly, …)`. If `Yearly` → `CreditCustomerBalanceAsync(…,
ReferralRewardAmount.OneMonthOf(plan), $"referrer-reward-{referralRedemptionId}",
"Referral reward: 1 month free", ct)` and store the returned id in
`ReferrerRewardCouponId` (flagged in §3). Keep every existing guard, log and non-fatal catch.

### 6.4 Tests (Phase B)

Use the existing fake/substitute pattern for `IStripeDiscountService` / `IStripeBillingService`
(see `StudioHandlerIntegrationTests.cs`, `RegisterStudioHandlerTests.cs`).

| Scenario | Expect |
|---|---|
| Referred studio, Growth Monthly checkout | Request `Interval = Monthly` |
| Referred studio, Growth Yearly checkout | Request `Interval = Yearly`, `MonthlyPrice = 59` |
| Referred studio, Free | No coupon requested |
| Yearly-only custom tier (Yearly 600, no Monthly) | `MonthlyPrice = 50` |
| Referrer on Premium Monthly | Coupon applied to subscription; no balance credit |
| Referrer on Premium Yearly | `CreditCustomerBalanceAsync` called with 79; no coupon; redemption marked applied |
| Balance credit throws | Logged, redemption not marked applied, nothing rethrown |
| `ReferralRewardAmount.OneMonthOf` | 79 for Premium; 50 for the yearly-only tier |

Commit: `fix: referral reward worth one month on yearly plans, not a free year (Phase B)`

---

## 7. Phase C — Computed yearly saving

### 7.1 API

`Pena_e_Arte.Contracts/Responses/PlanResponse.cs` — append two fields at the end:
`decimal? YearlySavingAmount, decimal? YearlyMonthsFree`. `GetPlansHandler` materialises the
projection, then computes per D7 from the **active** Monthly and Yearly rows:
`YearlySavingAmount = M × 12 − Y`; `YearlyMonthsFree = Math.Round(saving / M, 2)`; both null
when either row is missing/inactive, `M == 0`, or saving ≤ 0 (log a warning with `PlanId` for
the last case). Update `billing.types.ts` / `billingApi.ts` types (`number | null`, no `any`).

### 7.2 Shared label helper

New `frontend/src/features/billing/utils/yearlySavingLabel.ts`:
`export function yearlySavingLabel(monthly: number, yearly: number): string | null` implementing
D7 exactly, plus `yearlySavingPercentFloor(monthly, yearly): number | null`.

### 7.3 Subscribe page

- Plan card (line ~78): `{formatPrice(perMonth)}/mo · {label}`; no label → nothing after the
  per-month price.
- Toggle badge (lines ~120–123 and ~284–286): look only at paid tiers with a *purchasable*
  yearly price (§5.4). All share one label → that label ("2 months free"); labels differ →
  "Save up to N%" using the max floored percent; none → no badge. Delete the
  `yearlyDiscountPercent` reads.

### 7.4 Trial warning email

`Pena_e_Arte.Infrastructure/Jobs/TrialExpiryWarningJob.cs` — `BuildEmailBody(Studio studio)`
currently hardcodes `<p>Yearly plans save you 2 months — that's ~17% off. 🎉</p>` (~line 79).
Replace with a line computed from plans whose yearly price is purchasable (active,
`StripePriceId` not null, Monthly > 0):

- Every paid tier has one and all give the same whole months free → `Pay yearly and get 2 months free.`
- Only some tiers → `Yearly billing is available on Premium — 2 months free.` (list the tier names).
- None → omit the paragraph.

Pass the computed line into `BuildEmailBody` (keep it static and pure). No emoji.

### 7.5 Admin plan editor

`frontend/src/features/platform/components/PlanEditPage.tsx`:

- Replace the "Yearly discount (%)" input (lines ~350–356) with "Months free on yearly"
  (local form field, number, 0–11, default `Math.round(plan.yearlyDiscountPercent / 100 * 12)`,
  i.e. 2 for 17).
- Suggested yearly (lines ~243–245 and ~408–412) = monthly × (12 − monthsFree), text
  "Suggested: €790 (monthly × 10)".
- On submit send `yearlyDiscountPercent = Math.round(monthsFree / 12 * 100)` so the API
  contract and validators are unchanged.

### 7.6 Tests (Phase C)

| Where | Case | Expect |
|---|---|---|
| `GetPlansHandlerTests` | 79 / 790 | saving 158, months free 2 |
| | 79 / 800 | saving 148, months free 1.87 |
| | 79 / no yearly | both null |
| | 79 / 1000 | both null, warning logged |
| | 0 / none | both null |
| `yearlySavingLabel.test.ts` | 79/790, 29/290 | "2 months free" |
| | 79/800 | "save 15%" |
| | 79/1000, 0/0 | null |
| `SubscribePage.test.tsx` | only Premium yearly purchasable | badge "2 months free"; Starter/Growth cards disabled in Yearly |
| `TrialExpiryWarningJobTests` | all three / only Premium / none | the three lines in §7.4 |
| `PlanEditPage` test | monthly 79, months free 2 | "Suggested: €790"; payload `yearlyDiscountPercent` 17 |

Commit: `feat: compute yearly saving from real prices everywhere (Phase C)`

---

## 8. Phase D — Help, manual, architecture docs

### 8.1 Standalone manual — `frontend/public/user-manual/index.html`

Edit only `<text>` contents and prose; keep SVG coordinates.

| ~Line | Now | Change to |
|---|---|---|
| 3328–3340 (Owner → Choose / change plan mockup) | toggle "Yearly (Save 17%)"; card "Professional · Billed monthly · €49/mo" | "Yearly (2 months free)"; "Premium · Billed monthly · €79/mo" (keep the Starter card; if it shows a price, €29/mo) |
| 3512 (admin studio list mockup) | "… · Professional · Renews 3 Aug" | "… · Growth · Renews 3 Aug" |
| 3621 (Admin → Plan management intro) | "…yearly discount, usage limits (… Locations …)…" | "…months free on yearly (used to suggest a yearly price), usage limits (Artists, Appointments/mo, Notifications/mo, Storage), API access…" |
| 3627–3630 (plan management mockup) | "Professional · €49/mo · €490/yr · Save 17% vs monthly" | "Premium · €79/mo · €790/yr · 2 months free" |
| Plan management steps | "fill in name, yearly discount %…"; "Click … to edit it in place" | "months free on yearly"; add a `callout callout-warning` (same component as the campaigns note ~line 3417): "Free, Starter, Growth and Premium are reset from code on every deploy. To change one, create a new plan instead of editing it. Retired plans (like Pro) show a Retired badge and can't be bought." |
| Owner choose-plan steps | — | add: "Yearly billing gives 2 months free on plans that offer it. Free stays free in either view." |

Check the mockups at 375 px and desktop width. Do **not** mention refunds (Batch 3).

### 8.2 `frontend/src/features/help/helpContent.ts`

- Line ~1408: → "Toggle between Monthly and Yearly billing — yearly gives you 2 months free on plans that offer it. The Free plan stays free either way."
- Line ~1661: → "Enter the plan Name and how many months free yearly billing should give (used to suggest a yearly price)."
- Line ~1664: drop "Locations" from the limits list; add a tip: "Locations isn't enforced yet — multi-location isn't built."
- Lines ~1325 and ~1347: "Only available on plans that include API access" → "Only available on the Premium plan…".
- Admin plans article: add a tip on core tiers being reset from code and retired plans (same text as the manual callout).
- Search `helpContent.ts` for "Pro plan", "Professional", "Save 17%", "€49" and fix any hit.

### 8.3 Tours

`adminTour.ts` ("Subscription plans" step) and `ownerTour.ts` (billing step) name no tier or
price — expected "no change needed"; confirm and state the reason in the final report.

### 8.4 `docs/claude/architecture.md`

- "Platform Subscription Architecture" → "Entities": replace the `Plan` block
  (`BillingInterval`, `PriceMonthly`, `PriceYearly`) with `Plan` + `PlanPrice` (one row per
  interval: `Interval`, `Price`, `StripePriceId`, `IsActive`) + `Subscription.BillingInterval`,
  pointing to the "Plan/PlanPrice split" Decisions Log row.
- "Yearly Discount": "Every paid core tier offers Yearly at Monthly × 10 (2 months free).
  The owner-facing saving is computed from the two prices; `Plan.YearlyDiscountPercent` only
  feeds the admin's suggested-price helper."
- Decisions Log — one new row, "Four-tier catalogue + yearly on every paid tier (2026-09-23)":
  Pro retired (prices inactive, row kept), Premium absorbs Pro's limits and API access,
  Locations not advertised, yearly Starter/Growth rows unlinked until the Batch 3 refund flow,
  referral reward = one monthly price (yearly: amount-off coupon / customer balance credit),
  saving computed. Reason: benchmark set sells 3–4 tiers separated by team size; repeating
  100% coupon made a yearly plan free.

### 8.5 Drift guard test

New `frontend/src/features/help/__tests__/planNamesInDocs.test.ts`: read `helpContent.ts`'s
exported content and `public/user-manual/index.html` (via `fs`), and fail on any of:
`/\bProfessional\b/`, `/Save 17%/`, `/€49\/mo/`, `/€490\/yr/`, `/\bPro plan\b/`.

Commit: `docs: help, manual and architecture for four-tier catalogue and yearly pricing (Phase D)`

---

## 9. Constraints (all phases)

- No `var` for non-obvious types; no `any` in TypeScript.
- No new endpoint, so no new validator is required; the `GetPlans` handler change keeps its
  `OwnerOnly` policy.
- Structured Serilog logs only, with `StudioId`/`PlanId`/`RedemptionId`; never names, emails,
  or card data.
- No migration. No Stripe secret or price id hardcoded anywhere.
- Every Application-layer change ships with unit tests (CLAUDE.md "never skip a test").

---

## 10. Industry-standard benchmark note (CLAUDE.md rule #6)

Checked 2026-09-23 against the Industry-Standard Benchmark Set: GlossGenius (3 tiers by team
size, yearly 12–14% off), Boulevard (3 + Enterprise, per location, yearly 20% limited offer),
Square Appointments (Free + 2 + custom, per location), Venue Ink (Free/Pro per segment, per
active artist), Fresha (2 + Enterprise, per team member), Booksy (1 tier + per user), Vagaro
(1 + per calendar). Four flat-priced tiers with an optional 16.7% yearly saving is inside the
norm and fits the audience finding (flat, predictable, no pressure). Monthly as the default
and "Free stays free" avoid annual-first dark patterns.

---

## 11. Test requirements summary

`dotnet test` and `pnpm test` green, with the new/updated tests in §5.7, §6.4, §7.6 and §8.5.
`pnpm lint` and `dotnet build` clean.

---

## 12. Final verification checklist — do not declare done until every row passes

- [ ] Fresh DB (`docker compose down -v && docker compose up -d`, then run the API): exactly
      Free, Starter, Growth, Premium with the D1 values; 7 `plan_prices` rows; no Pro.
- [ ] Existing dev DB with Pro: Pro row present, all its prices `IsActive = 0`; owner
      `GET /api/v1/billing/plans` omits it; admin sees it with "Retired".
- [ ] Subscribe page, Monthly: four cards. Yearly: Free selectable as "Free"; Premium shows
      €790/yr, "€65.83/mo · 2 months free" (dev, where `StripeDemoSeeder` links test prices, all
      three paid tiers are purchasable; with `StripePriceId` null they show as unavailable).
- [ ] Test-mode checkout, referred studio, Growth yearly: first invoice €531, not €0.
- [ ] Trial warning email body matches §7.4 for the current catalogue.
- [ ] No owner-facing code reads `yearlyDiscountPercent` (`grep -rn yearlyDiscountPercent frontend/src`
      → only `PlanEditPage.tsx`, types and tests).
- [ ] Owner Billing page has no Locations row; API key/webhook cards appear on Premium only.
- [ ] Manual, Help, tours and `architecture.md` updated; §8.5 test passes.
- [ ] Four commits on `feature/plan-tiers-yearly-batch1`.

---

## 13. Final deliverable spec

Four commits on `feature/plan-tiers-yearly-batch1` (Phases A–D). Final report must include:

1. What changed per phase, with file list.
2. Any drift between this file and your checkout.
3. The §3 flags, verbatim, as follow-ups: owner `/billing/plans` leaking `SubscriberCount`
   and `StripePriceId`; `ReferrerRewardCouponId` now also holding `cbtxn_` ids; refund flow
   and live Stripe yearly prices pending Batch 3.
4. The tours "no change needed" reasoning (§8.3).
5. A reminder for the human: create live-mode yearly Stripe prices for Starter (€290) and
   Growth (€590) **only after** Batch 3 ships, then link them in Plan management.
