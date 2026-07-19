# Feature Request — Two-Sided Referral Rewards

**From:** Marketing
**Branch suggestion:** `feat/two-sided-referral-rewards`
**Read first:** `docs/claude/architecture.md`, `docs/claude/backend.md` — plus the referral code that already exists (see below). This was scoped by reading the actual `ReferralCode`/`ReferralRedemption` flow, not written from a blank page.

---

## Business context (why)

Today the referral program only rewards the *new* studio (one month free on their first paid subscription). The *referring* studio gets nothing but visibility into redemption stats. That's a real gap if marketing wants to run "refer a friend" messaging — copy implying mutual benefit would overpromise against what's built. This request makes the reward two-sided: the referring studio gets something back when their code converts a new paying studio.

This does **not** ship copy or decide the exact reward size — that's flagged as an open question for whoever owns pricing. It scopes the engineering work needed to make a symmetric reward *possible*, using the same mechanism already used for the referred side (a Stripe coupon) as the default assumption.

---

## What already exists (read this before writing code)

- `ReferralCode` (`Pena_e_Arte.Domain/Entities/ReferralCode.cs`): `Id`, `StudioId` (the referrer — always populated, including for issuer-generated codes via `IssuerGenerateReferralCodeCommand`), `Code`, `IsActive`, `IsSingleUse` (defaults `true`), `CreatedAt`, `ExpiresAt`.
- `ReferralRedemption` (`Pena_e_Arte.Domain/Entities/ReferralRedemption.cs`): `Id`, `ReferralCodeId`, `NewStudioId`, `RedeemedAt`, `DiscountApplied`. **No field tracks anything about the referrer's side** — this is the main gap.
- Redemptions get recorded in exactly two places, and both need the new reward logic added:
  - `CreateSubscriptionCommand.Handle` (`Pena_e_Arte.Application/Billing/Commands/CreateSubscriptionCommand.cs`) — the direct/no-checkout subscribe path.
  - `ActivateCheckoutSubscriptionHandler.RecordReferralRedemptionAsync` (`Pena_e_Arte.Application/Billing/Commands/ActivateCheckoutSubscriptionCommand.cs`) — the Stripe Checkout path, fired from the webhook or the finalize endpoint.
- `IStripeDiscountService.CreateOneMonthFreeCouponAsync` (`Pena_e_Arte.Infrastructure/Services/StripeDiscountService.cs`) creates a reusable Stripe `Coupon` (100% off, 1 month, `Duration = "repeating"`). Both redemption sites already call this for the *referred* studio and pass the resulting coupon ID into `CreateSubscriptionAsync` / `CreateSubscriptionCheckoutAsync`.
- **`IStripeBillingService` has no method to apply a coupon to a subscription that already exists.** Every current use of a coupon happens at subscription-creation time. The referring studio, by definition, usually already has an active subscription — this is the actual gap that needs closing, not just a data-model tweak.
- `GetReferralStatsQuery` / `ReferralStatsResponse` (`Pena_e_Arte.Application/Referrals/Queries/GetReferralStatsQuery.cs`) currently returns only: active code, total redemption count, discounts-applied count. No referrer-reward field yet.
- Existing tests to extend rather than duplicate: `tests/Pena_e_Arte.UnitTests/Referrals/CreateSubscriptionDiscountTests.cs`, `tests/Pena_e_Arte.IntegrationTests/Application/ReferralFlowIntegrationTests.cs`.

---

## What needs to change

### 1. New Stripe capability — apply a coupon to an active subscription

Add to `IStripeBillingService`:

```csharp
Task ApplyCouponToActiveSubscriptionAsync(string stripeSubscriptionId, string couponId, CancellationToken ct);
```

Implementation wraps Stripe's subscription update call. **Verify against the currently pinned Stripe.net SDK version before implementing** — Stripe's API has shifted between a single `Coupon` field and a `Discounts` collection on `SubscriptionUpdateOptions` across API versions, and this codebase should match whatever `StripeBillingService.cs` is already targeting elsewhere. Don't assume the same shape used in `CreateSubscriptionAsync`.

### 2. New field(s) on `ReferralRedemption` — migration required

```csharp
public bool     ReferrerRewardApplied { get; set; }
public string?  ReferrerRewardCouponId { get; set; }
```

This is what makes reward application idempotent — both redemption-recording sites, plus any retry (webhook redelivery, finalize-endpoint re-call), must check this flag before attempting a reward again. Follow the same migration naming convention as `AddReferralSystem` (`Pena_e_Arte.Infrastructure/Migrations/20260610161203_AddReferralSystem.cs`) — name it something like `AddReferrerRewardTracking`.

### 3. New shared service — `IReferralRewardService`

Don't inline this logic into both `CreateSubscriptionCommand.Handle` and `ActivateCheckoutSubscriptionHandler.RecordReferralRedemptionAsync` — pull it into a service both call, matching how `SubscriptionAccessService` and `PlanLimitService` already centralize cross-cutting billing logic in this codebase.

```csharp
Task RewardReferrerAsync(Guid referralRedemptionId, CancellationToken ct);
```

Call this immediately after `DiscountApplied` is set `true` in both handlers (only when the referred studio's own discount actually landed — no `DiscountApplied`, no reward, matching the existing semantics of that field).

Logic inside:

1. Load the `ReferralRedemption`, then the `ReferralCode` → `StudioId` (the referrer) → that studio's `Subscription`.
2. If `ReferrerRewardApplied` is already `true`, return immediately (idempotency guard).
3. **If the referring studio has an active Stripe subscription** (`Subscription.StripeSubscriptionId is not null` and `Status == Active`): create a coupon via the existing `IStripeDiscountService.CreateOneMonthFreeCouponAsync` (idempotency key scoped to the redemption, e.g. `referrer-reward-{redemptionId}`, not reused from the referred-studio coupon), then call the new `ApplyCouponToActiveSubscriptionAsync`. Set `ReferrerRewardApplied = true` and store the coupon ID.
4. **If the referring studio has no active Stripe subscription** (still Trialing, cash-billed with no Stripe subscription, or — if the separate Free-tier feature ships — on Free): there's nothing to attach a Stripe coupon to. Recommended approach: mirror the existing `PendingReferralCodeId` pattern already on `Studio` — add a `PendingReferrerRewardCount` (or similar) that gets honored automatically the first time that studio starts a real Stripe subscription. This is flagged as an open decision below rather than fully specified, since it's a second async code path and deserves a deliberate call, not a rushed clone of the existing pattern.
5. Log the outcome with `tenant_id`/`studio_id`, not the studio's email or any PII.

### 4. Fraud consideration — read this before building, don't skip it

Right now, a self-referral (owner opens a second studio under their own control and redeems their own code) only benefits the *new* studio — limited incentive to bother. **Making this two-sided changes that math**: self-referral becomes a way to mint a free month on both accounts simultaneously. Recommend adding a check in `RewardReferrerAsync` (or earlier, at redemption time) comparing the referring studio's `OwnerEmail` against the new studio's `OwnerEmail` and blocking or flagging the reward when they match or look like an obvious variant (e.g. `+`-suffixed Gmail addresses). This is a policy call — block silently, block with a support flag, or just log for manual review — not something to resolve unilaterally in code. Surface it as a decision before merging, not after.

### 5. Stacking / cost control for reusable codes

`ReferralCode.IsSingleUse` can be `false`. If a studio's code gets reused repeatedly, a referrer could accumulate many consecutive one-month-free rewards. Decide whether to cap total referrer rewards per studio (lifetime max, or "only one active reward at a time — a second reward extends the existing coupon's duration rather than stacking a second one"). Also verify how Stripe's `repeating, 1 month` coupon behaves if a second coupon is applied while the first hasn't finished its cycle — this needs to be tested against Stripe directly, not assumed. This is the same kind of "guessed number now, validate against real usage later" risk flagged in the plan-limits work already in `architecture.md` — don't let it ship unexamined.

### 6. API / response changes

- `ReferralStatsResponse` (check exact shape in `Pena_e_Arte.Contracts/Responses/` before editing) needs a new field — something like `ReferrerRewardsAppliedCount` — so an owner can see rewards earned, not just redemptions counted.
- `PlatformReferralCodeResponse` (issuer-facing, used by `IssuerGenerateReferralCodeCommand`) should probably expose the same for platform visibility/support purposes — confirm with whoever owns the issuer dashboard whether this is in scope for this branch or a fast-follow.

---

## Frontend changes

`ReferralCodeCard.tsx` (owner settings, `features/studios/components/`) currently shows the code and basic redemption stats. Add a line surfacing rewards actually earned by the referrer, not just redemptions counted — exact copy pending from marketing, don't invent wording here.

---

## Tests

- Unit: `RewardReferrerAsync` applies a coupon when the referrer has an active Stripe subscription; correctly no-ops (or queues, per whichever pending-credit design is chosen) when they don't; is idempotent on repeat calls against the same `ReferralRedemption`.
- Unit: self-referral guard blocks/flags when owner emails match (exact behavior per the policy decision in point 4).
- Unit: extend `CreateSubscriptionDiscountTests.cs` to cover the referrer side, not just the referred side.
- Integration: extend `ReferralFlowIntegrationTests.cs` — Studio A refers Studio B, B subscribes and its discount lands, verify A's subscription receives the reward coupon (via whatever Stripe test double is already used in that suite — don't hit real Stripe).

---

## Constraints (per project rules)

- Tenant isolation: `RewardReferrerAsync` touches a studio other than the current tenant (the referrer, while the caller's tenant context is the referred studio) — this needs the same kind of explicit `IgnoreQueryFilters()` + documented exception already used elsewhere (see the `IgnoreQueryFilters approved: usage #N` comment convention in `architecture.md` — this would be a new numbered entry, not a silent bypass).
- No unprotected endpoints — no new public endpoints are needed for this feature; it's internal service logic triggered from existing authenticated flows.
- Never log PII — reward logging must use `tenant_id`/`studio_id`, never `OwnerEmail`, even when checking for the self-referral fraud case above (compare emails in-memory, don't log them).
- Structured logs only.

## Open questions for whoever picks this up

1. **Reward size/type** — "one month free" mirrors the referred side, but that's an assumption, not a decision. A flat credit, a percentage discount, or a capped-value reward are all alternatives. This is a pricing/business call, not engineering's to pick — same caution as the plan-limit numbers flagged elsewhere in this codebase's history.
2. **Reward mechanism when the referrer has no active Stripe subscription** — pending-credit-on-next-subscription (point 3.4 above) vs. a manual/support-handled path vs. simply not rewarding that case at all. Needs a decision before the "pending" field gets added.
3. **Self-referral fraud policy** — block, flag-for-review, or rate-limit.
4. **Stacking cap** for reusable codes.
5. Whether `PlatformReferralCodeResponse` (issuer/platform view) is in scope for this branch.
