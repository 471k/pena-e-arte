# Feature Request — Permanent Free Plan Tier

**From:** Marketing
**Branch suggestion:** `feat/free-tier-plan`
**Read first:** `docs/claude/architecture.md`, `docs/claude/backend.md`, `docs/claude/frontend.md`, `docs/claude/database.md` — this request was scoped by reading the current `Plan`/`Subscription` billing code, not written from scratch, so it should slot into existing patterns rather than invent new ones.

---

## Business context (why)

Starter (€29/mo) stays as-is. This adds a new tier *below* Starter — genuinely €0, no card required, indefinite — as a permanent lead-gen and brand-distribution vehicle. Studios on it keep the non-removable "Powered by Pena e Artë" badge on their booking widget and confirmation emails forever, which is the actual point: every free studio is a standing billboard. Starter's revenue isn't touched because Free sits under it, not in place of it.

Working name for the tier: **Free**. (Marketing will confirm final naming before this ships publicly — do not hardcode the string "Free" anywhere that would require a migration to change later.)

---

## What already exists and doesn't need to change

Read this before writing code — most of the plumbing for a zero-cost, no-Stripe plan is already there:

- `Plan` entity (`Pena_e_Arte.Domain/Entities/Plan.cs`) already has every field this tier needs: `MaxArtists`, `MaxAppointmentsPerMonth`, `MaxNotificationsPerMonth`, `MaxStorageGb`, `MaxLocations`, `AllowApiAccess`, `PrioritySupport`, `StripePriceIdMonthly`/`StripePriceIdYearly` (both nullable). No new columns needed.
- `AllowBrandingRemoval` defaults to `false` on the entity and isn't exposed in `CreatePlanRequest` at all — so a Free plan created through the existing admin flow will be non-removable by construction. This is exactly the requirement. Don't add a way to set it for this plan.
- `PlanLimitService` and the `IQuotaCheckedCommand` pipeline behavior already enforce `Plan.Max*` generically — once Free has real numbers seeded, quota enforcement (1 artist, N appointments/month, etc.) works with zero new code.
- `CreateSubscriptionCommand` (`Pena_e_Arte.Application/Billing/Commands/CreateSubscriptionCommand.cs`) already has a branch for when `plan.StripePriceIdMonthly/Yearly` is `null`: it skips Stripe entirely and activates the subscription locally. This is the exact code path a Free-plan signup should use — see the gap noted below, though.

---

## What needs to change

### 1. `CreatePlanValidator` blocks a €0 price (bug relative to this feature)

`Pena_e_Arte.Application/Plans/Commands/CreatePlanCommand.cs`:

```csharp
RuleFor(x => x.Request.PriceMonthly).GreaterThan(0);
RuleFor(x => x.Request.PriceYearly).GreaterThan(0);
```

These reject a zero-price plan outright. Change to `GreaterThanOrEqualTo(0)` for both. Confirm `PlanConfiguration.cs` doesn't have a DB-level check constraint that would also reject 0 (it doesn't currently — `decimal(18,2)`, `IsRequired()`, no range constraint — but re-verify after any DB changes elsewhere on this branch).

### 2. `CurrentPeriodEnd` has no real "never expires" concept — this is the main risk

This is the part that actually makes "indefinite" work correctly, and it's worth reading carefully.

`CreateSubscriptionCommand`'s no-Stripe branch currently does:

```csharp
else
{
    periodEnd = DateTime.UtcNow.AddMonths(1);
}
```

That's fine for the existing use case (manually-activated cash-paying studios, where an issuer is expected to re-run `ActivateSubscriptionManuallyCommand` roughly monthly to push `CurrentPeriodEnd` forward). But I checked: **there is no recurring Hangfire job anywhere in `Pena_e_Arte.API/Program.cs` that checks `CurrentPeriodEnd` and transitions a lapsed subscription to `PastDue`.** The only scheduled billing jobs are the one-off trial-expiry jobs scheduled at registration (`ScheduleTrialExpiryWarning`, `ScheduleTrialExpiry`, `ScheduleGracePeriodEnd` in `RegisterStudioCommand`). Access control (`SubscriptionAccessService.GetSnapshotAsync`) treats `Active` as a pass-through status regardless of whether `CurrentPeriodEnd` has passed — so today, a cash-billed subscription's `CurrentPeriodEnd` lapsing silently does *nothing* by itself. That's an existing latent gap, not something this feature introduces, but it means "give it `+1 month` and move on" is not actually a safe way to build something that's supposed to run indefinitely — if that gap ever gets fixed with a real recurring expiry job, Free-tier studios would get incorrectly caught by it.

**Recommended fix, scoped to this feature only:** don't reuse the generic "no Stripe price → +1 month" branch as-is. Add an explicit check: if the plan being subscribed to has `PriceMonthly == 0` (i.e., is the Free tier, not just any cash-billed plan), set `CurrentPeriodEnd` to a far-future sentinel (e.g. `DateTime.UtcNow.AddYears(50)`, or introduce a proper `Subscription.NeverExpires` bool if that reads better against the rest of the schema — your call) instead of `+1 month`. This keeps Free-tier studios permanently in the `Active` pass-through state without depending on a renewal job that doesn't exist yet, and without being at risk if one gets built later for the cash-billing case specifically.

Flag this distinction explicitly in code comments so a future "let's add the missing expiry job" ticket doesn't accidentally sweep Free-tier studios into it.

### 3. `CreateSubscriptionCheckoutCommand` must never be reachable for Free

This handler throws `BusinessRuleViolationException("This plan is not available for online checkout.")` whenever `priceId is null` — which will always be true for Free since it has no Stripe price. That's correct behavior for the backend, but the **frontend plan-selection UI must not offer a "Checkout" button for Free at all** — it should call the direct subscribe endpoint (the one behind `CreateSubscriptionCommand`) instead of the Stripe checkout-session endpoint. See Frontend section below.

### 4. Referral coupon logic should skip Free-tier signups

`CreateSubscriptionCommand` and `CreateSubscriptionCheckoutCommand` both try to attach a "1 month free" Stripe coupon when a studio signs up via a referral code. That's meaningless for a plan that's already €0 with no Stripe subscription — skip the `ResolveReferralCouponAsync` / coupon-creation branch entirely when the target plan's `PriceMonthly == 0`. (It would currently fail gracefully via the existing try/catch and log a warning either way, so this isn't a correctness bug, just wasted calls and noisy logs — but worth doing cleanly since it touches referral flows other departments care about.)

Separately — flag to product/marketing: a referral code redeemed by a studio that signs up straight to Free won't produce any visible reward for the referring studio, since there's no discount to apply. Decide whether referral rewards should require the referred studio to be on a *paid* plan before the referring studio's redemption counts, or whether sign-up-to-Free should count toward referral stats at all. This is a product decision, not something to silently resolve in code.

### 5. Seed data — add the Free plan row

In `Pena_e_Arte.Infrastructure/Persistence/Seed/DataSeeder.cs`, alongside the existing `SeedPlansAsync`, add a new `Plan`:

```csharp
new Plan
{
    Id                       = FreePlanId,          // new static Guid, same pattern as StarterPlanId etc.
    Name                     = "Free",
    BillingInterval          = BillingInterval.Monthly,
    PriceMonthly             = 0m,
    PriceYearly              = 0m,
    YearlyDiscountPercent    = 0,
    // AllowBrandingRemoval intentionally omitted — defaults to false.
    MaxArtists               = 1,
    MaxAppointmentsPerMonth  = 15,   // placeholder — see "Numbers to confirm" below
    MaxNotificationsPerMonth = 50,   // placeholder
    MaxStorageGb             = 1,    // placeholder
    MaxLocations             = 1,
    // StripePriceIdMonthly / StripePriceIdYearly left null — this is what routes
    // subscription creation down the no-Stripe path.
},
```

**Numbers to confirm before this ships — do not treat the placeholders above as final.** The existing per-tier limits (Starter = 40 appointments/month, etc.) are flagged in `architecture.md` as same-day guesses never validated against real usage. Free's limits need to be tight enough that it's clearly a lead-gen tier and not a substitute for Starter (undercutting the €29 tier defeats the point of this feature), but not so tight it's useless for an actual solo artist's real booking volume. This is a business call for whoever owns pricing, not an engineering one — loop marketing back in before finalizing.

### API / Application — no new endpoints needed

`GetPlansQuery`, `CreatePlanCommand`, `CreateSubscriptionCommand` all already handle an arbitrary `Plan` row generically. No new endpoint group required — this is a data + two-small-code-change feature, not a new subsystem.

---

## Frontend changes

1. **Plans/pricing display** (wherever `GetPlansQuery` results are rendered — public pricing page and in-app upgrade/plan-selection screens): add a card for Free. CTA should read something like "Get started free" rather than "Subscribe" or "Upgrade" — copy to be provided by marketing once this is scoped for build, not invented here.

2. **Plan selection / signup flow**: the component that currently calls the Stripe-checkout-session endpoint on plan selection needs a branch — if the selected plan has no Stripe price configured (i.e. `stripePriceIdMonthly` and `stripePriceIdYearly` are both null in the `GetPlansQuery` response), call the direct subscribe endpoint (`CreateSubscriptionCommand`'s endpoint) instead of redirecting to Stripe Checkout. No card form should ever render for this plan.

3. **Trial-to-plan conversion screen** (wherever a studio picks a plan at trial end / from Trialing status): Free needs to appear there too, not just on first registration, so a studio that trials and doesn't want to pay yet has somewhere to land other than suspension.

---

## Tests

- Unit: `CreatePlanValidator` accepts `PriceMonthly = 0` / `PriceYearly = 0`.
- Unit: `CreateSubscriptionHandler` — subscribing to a plan with `PriceMonthly == 0` sets a far-future (or "never expires") period end, not `+1 month`, and does not attempt Stripe customer/coupon creation.
- Unit: `CreateSubscriptionCheckoutHandler` still correctly rejects Free (no behavior change expected here, just confirm the existing guard covers it).
- Integration: full flow — register studio, subscribe to Free via the direct endpoint (no Stripe calls made), confirm `AllowBrandingRemoval` is `false` and the booking widget still renders the platform badge, confirm quota enforcement rejects a 2nd artist under `MaxArtists = 1`.
- Integration: referral code applied at registration, studio then subscribes to Free — confirm no coupon-creation call is attempted and no `ReferralRedemption` with a real discount is recorded (per whatever the product decision in point 4 above turns out to be).

---

## Constraints (per project rules)

- No new ORM/data access pattern — this is standard EF Core, matches `PlanConfiguration.cs` conventions already in place.
- Every new/changed endpoint path must keep its existing `RequireAuthorization()` policy — no policy changes needed since no new endpoints are being added.
- Never log PII — none of the above touches anything that would introduce a new log line with client/owner personal data.
- Structured logs only, no `Console.WriteLine`/`console.log`.

## Open questions for whoever picks this up

1. Final tier name ("Free" vs. something on-brand) — confirm with marketing before it's user-visible anywhere.
2. Final `Max*` limit numbers — placeholders above need a business decision, not an engineering guess (see note above; the last time limits were guessed same-day, it created exactly the kind of validation debt `GetPlanUsageReportHandler` was later built to catch).
3. Whether referral redemption should count/reward when the referred studio signs up straight to Free instead of a paid plan.
4. Whether to introduce a proper `Subscription.NeverExpires` flag (cleaner, more explicit) vs. a far-future sentinel date on `CurrentPeriodEnd` (smaller change, reuses existing field) — implementer's call, flagged above.
