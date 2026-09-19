# Payment Processing — Full State Report

**Prepared:** 2026-09-07 · **Scope:** every payment-processing-related file in the repo (`docs/claude/*.md`, `docs/payments/*.md`, `DECISIONS.md`), cross-checked against live source (`Pena_e_Arte.Domain`, `.Application`, `.Infrastructure`, `.API`, `frontend/src/features/{payments,billing}`) and `git log`.

---

## 1. Executive summary

Pena e Artë (shipping as "TattooOS") has two independent payment flows, and as of today neither is running on a real, live-capable payment processor:

- **Flow A — client → studio (appointment deposits).** The card path is **intentionally disabled**. `IPaymentProvider`'s DI default is `NullPaymentProvider`, which throws on every call by design ("fails closed"). The frontend correctly detects this via a `/api/v1/payments/capabilities` check and disables the card option, showing "Card payments are temporarily unavailable — use Cash." **Cash is the only working deposit path today**, and it is fully built end-to-end.
- **Flow B — studio → platform (SaaS subscriptions).** Still calls **Stripe.net directly** (`IStripeBillingService`/`StripeBillingService`) — code-complete and tested, but **commercially unusable**: Stripe does not onboard merchants registered in Albania, which is where the operating entity is registered. This was confirmed as a country-level block (not Connect-specific) on 2026-09-03.
- **Why this happened:** the original design collected client card payments into the platform's own Stripe account (an aggregator model). A legal audit on 2026-07-31 found that this exact shape make the platform a payment service under Albanian law (Law 55/2020), requiring a Bank of Albania licence it doesn't have — unless client funds never pass through the platform. Stripe also doesn't support Albanian merchants at all, which made the whole existing Flow A design both legally unviable and commercially impossible simultaneously.
- **The decided fix (ADR-0001, accepted 2026-07-31):** replace Flow A with **POK** (an Albanian EMI-backed payment gateway with a first-class `splitWith` platform-fee field) plus an always-on cash path; replace Flow B with **Polar** as merchant-of-record (Paddle as fallback), which absorbs subscription billing, VAT, and B2B e-Fatura invoicing that the platform cannot otherwise issue itself. **easyPos** was selected as the fiscalization adapter (a separate, unrelated concern — it registers invoices with Albanian tax authorities, it does not move money).
- **What has actually shipped since the decision:** the provider-neutral `IPaymentProvider` abstraction, the `Payment` entity migration (`ProviderReferenceId`, `Currency`, `HoldExpiresAt`, `PlatformFeeAmount`), the `NullPaymentProvider` fail-closed placeholder, an architecture fitness test forbidding a platform-balance/ledger entity, plus a large adjacent compliance pass (legal-entity disclosure, versioned consent, retention/erasure, a secrets provider). Most recently (**PR #95, 2026-09-05**) the frontend was fixed to gate the card option on the real capability flag instead of just an env-var proxy, and `RefundPaymentCommand`/`ConfirmCashDepositCommand` gained audit logging.
- **What has not shipped at all:** no POK, easyPos, or Polar integration code exists anywhere in the repo (zero hits for any of the three). No `IFiscalizationProvider`, no `ISubscriptionBillingProvider`, no `BillingMandate`. Flow B has not been re-pointed at Polar — it is still 100% Stripe, which per the 2026-09-03 finding cannot go live for this entity either.
- **Documentation gap worth flagging:** `docs/claude/architecture.md`'s own "Payment Architecture — Card & Cash Only" section (lines 189–219) predates all of this and still describes `IStripePaymentService`/`StripeConnectService` as the live Flow A design. That section is stale; the Decisions Log further down the same file (and `docs/payments/`) is current. Recommend updating or removing that section so a future reader doesn't anchor on it.

---

## 2. The two-flow model

| | Flow A | Flow B |
|---|---|---|
| **Who pays whom** | Client → studio (appointment deposit) | Studio owner → platform (SaaS subscription) |
| **Purpose** | Booking deposit, held then captured at session end (or forfeited/refunded per cancellation policy) | Recurring access fee for the platform itself |
| **Card processor (designed)** | POK (ADR-0001) | Polar as merchant of record, Paddle fallback (ADR-0001) |
| **Card processor (live today)** | None — `NullPaymentProvider`, fails closed | Stripe Billing — code-complete but commercially blocked for this entity |
| **Cash path** | Yes, fully built, always on | Yes — `ActivateSubscriptionManuallyCommand`, admin-only, out-of-band |
| **Regulatory posture** | Must stay inside Law 55/2020 Art. 4(g)'s technical-service-provider exclusion — the platform must never take possession of client funds | Collecting the platform's own revenue is not a regulated payment service; the blocker is Stripe's Albania merchant restriction, not licensing |

---

## 3. Current live implementation (verified against source, 2026-09-07)

### 3.1 Domain model

**`Payment`** (`Pena_e_Arte.Domain/Entities/Payment.cs`) — tenant-scoped:
- `AppointmentId`, `ClientId`, `Amount`, `Status` (`PaymentStatus`), `Method` (`ClientPaymentMethod`)
- `ProviderReferenceId` (renamed from `StripePaymentIntentId` across ~22 files/~74 call sites), `ClientSecret`
- `Provider` (string, e.g. `"pok"` once wired; empty for cash/legacy rows)
- `Currency` (ISO 4217, default `"ALL"` — Albanian lek)
- `HoldExpiresAt` (maps onto POK's `expiresAfterMinutes`, not yet enforced by any real provider)
- `PlatformFeeAmount` (0% today; deliberately **not** modeled as a `SessionSplit` row — see §5)
- `CashNote`, `CashConfirmedByUserId` (cash-only fields)
- `PaidAt`, `RefundedAmount` (nullable — distinguishes a partial refund from a full one; there is no separate `PartiallyRefunded` status)
- `SessionSplits` (owner-defined artist/studio splits of the deposit — unrelated to `PlatformFeeAmount`)

**`PaymentStatus` enum:** `Pending` → `CashPending` (cash only) → `Captured` (card, authorized/held, not yet captured) → `Paid` → `Refunded` / `Failed`.

**`ClientPaymentMethod` enum:** `Card` | `Cash` only (no PayPal; `Stripe` was removed as a named value in favor of the technology-agnostic `Card`).

**`SessionSplit`** — `PaymentId`, `Label`, `Amount`, `PaidAt`. Must sum exactly to `Payment.Amount` (enforced in `UpdateSessionSplitsCommand`). This is a studio-internal artist/owner split of the deposit, unrelated to the platform's own `PlatformFeeAmount`.

**`Subscription`** (admin-level, not tenant-scoped) — `StudioId`, `PlanId`, `BillingInterval` (own property, independent of which `Plan` it's on), `PendingPlanId`/`PendingBillingInterval` (scheduled downgrade), `Status` (`trialing`/`active`/`past_due`/`cancelled`/`grace_period`), `CancelAtPeriodEnd`, `TrialExpiresAt`, `CurrentPeriodEnd`, `GracePeriodEnd`, `StripeSubscriptionId`.

**`Plan`** (admin-level) — `Name`, `YearlyDiscountPercent` (display only), `AllowBrandingRemoval`, usage limits (`MaxArtists`, `MaxAppointmentsPerMonth`, `MaxNotificationsPerMonth`, `MaxStorageGb`, `MaxLocations`, all nullable = unlimited), `AllowApiAccess`, `PrioritySupport`, and a collection of `PlanPrice` rows.

**`PlanPrice`** — one row per billing cadence a tier actually offers: `PlanId`, `Interval`, `Price`, `StripePriceId` (account-specific, never reconciled by the seeder once set), `IsActive`. This child-entity split (`Plan`/`PlanPrice`) replaced an earlier design where `Plan` itself carried `PriceMonthly`/`PriceYearly`/`StripePriceIdMonthly`/`StripePriceIdYearly`/`PairedPlanId` directly — that design caused two separate data-integrity bugs (`bug-report-plans-page-data-mismatch.md`, `bug-report-premium-plan-duplicate-legacy-row.md`) because a plan's billing cadence and its identity as a tier were the same row.

### 3.2 Flow A — `IPaymentProvider` (card) + cash (manual)

```csharp
public interface IPaymentProvider
{
    PaymentProviderCapabilities Capabilities { get; }   // SupportsSplit, SupportsAuthCapture,
                                                          // SupportsHoldExpiry, SupportedCurrencies
    Task<(string ProviderReferenceId, string ClientSecret)> CreatePaymentHoldAsync(...);
    Task CaptureAsync(string providerReferenceId, CancellationToken ct);
    Task CancelAsync(string providerReferenceId, CancellationToken ct);
    Task<string?> GetStatusAsync(string providerReferenceId, CancellationToken ct);
    Task<string> RefundAsync(string providerReferenceId, long? amountInCents, CancellationToken ct);
}
```

This replaced the deleted `IStripePaymentService`/`StripePaymentService` (aggregator model — every `PaymentIntent` went to the platform's own Stripe account). The DI default, `NullPaymentProvider`, implements every method as an immediate throw ("No payment provider is configured... this is the expected state post-refactor") and reports all capabilities `false`. This is deliberate fail-closed behavior, not a bug.

**Cash flow** (fully functional, no external service touched):
1. Client books → `DeclareCashDepositCommand` → `Payment { Method = Cash, Status = CashPending }`.
2. Owner/artist confirms physical receipt → `ConfirmCashDepositCommand` → `Status = Paid`, `PaidAt` set, `CashConfirmedByUserId` set, mirrors `Appointment.DepositStatus = Paid`.
3. `ConfirmCashDepositCommand` now implements `IAuditableCommand` (added 2026-09-05, PR #95) — previously cash confirmations left zero audit trail.

**`GET /api/v1/payments/capabilities`** exposes `IPaymentProvider.Capabilities.SupportsAuthCapture` as `CardPaymentsAvailable`, so the frontend gates on real backend state rather than only checking whether a Stripe publishable key happens to be configured. `DepositCheckoutPage.tsx`, `PaymentMethodSelector.tsx`, and `CreatePaymentIntentPage.tsx` all consume it (fixed 2026-09-05, PR #95 — before that fix, a configured test-mode Stripe key could make the UI show a working-looking card form that failed server-side every time).

### 3.3 Flow B — `IStripeBillingService` (still Stripe, unchanged)

`IStripeBillingService`/`StripeBillingService`/`StripeDiscountService` are byte-for-byte unchanged by the EPIC-0001 refactor. Full surface: create customer, create a hosted Checkout session (subscription mode, with optional coupon and deferred trial-end for crediting a cash period), read back a completed Checkout session, create a subscription directly, upgrade (`ChangeSubscriptionPriceAsync`, immediate proration) or downgrade (`ScheduleSubscriptionPriceChangeAsync`, end-of-period) a subscription's price, cancel a scheduled price change, create a Stripe Customer Portal session, cancel a subscription (idempotent, best-effort — logged not rethrown), and apply a coupon to an active subscription (used by the two-sided referral reward).

Two webhook endpoints exist under `POST /api/v1/webhooks/stripe/`:
- **`/billing`** — live, handles `checkout.session.completed`, `invoice.paid`, `customer.subscription.updated`, `customer.subscription.deleted`. This is Flow B's real webhook.
- **`/connect`** — **orphaned**. It still dispatches `MarkPaymentAuthorizedCommand`/`ConfirmPaymentCommand`/`MarkPaymentFailedCommand`, which are Flow A commands from the pre-refactor, Stripe-Connect-based era. Nothing can trigger these events anymore since `NullPaymentProvider` never creates a real Stripe `PaymentIntent`. Confirmed live in `BillingEndpoints.cs` as of today — not yet removed.

**Both webhook handlers swallow processing exceptions after signature verification** (log and return 200) so Stripe doesn't retry an event whose failure was a platform bug — a deliberate fix from the July QA pass, still in place.

`StripeHealthCheck` was made optional (PR #81, 2026-09-03) so an unconfigured `Stripe:SecretKey` no longer fails Kubernetes readiness — a direct consequence of Flow B's production viability being an open question.

### 3.4 Other payment-adjacent services

- **`IPaymentInvoiceService`/`PaymentInvoiceService`** — generates a PDF receipt (`GET /api/v1/payments/{id}/invoice`) for a client's own deposit payment (studio name, client/artist names, amount, method, status, line items). This is a courtesy document, unrelated to Albanian tax fiscalization/easyPos — no fiscalization adapter exists yet (see §4).
- **`PaymentReconciliationJob`** (Hangfire) — treats webhooks as triggers, not sources of truth; re-fetches canonical payment state. Gained a third pass for hold-expiry auto-release as part of the EPIC-0001 refactor.
- **Revenue reporting** — `GetRevenueSummaryQuery` (`GET /api/v1/reports/revenue-summary`) includes both `Paid` and `Refunded` payments and sums `Amount - (RefundedAmount ?? 0)` clamped at 0, so a partial refund still shows the retained portion (fixed 2026-07-21 — it previously excluded any refunded payment entirely, undercounting real revenue).

### 3.5 API surface

**`PaymentEndpoints.cs`** (`/api/v1/payments`, all under `RequireAuthorization()`):
`POST /` (create payment intent, owner-only), `POST /cash` (declare cash deposit, client+), `POST /deposit` (create card deposit, client+), `GET /` (list, owner-only), `GET /appointment/{id}`, `PUT /{id}/splits`, `POST /{id}/capture`, `POST /{id}/cash/confirm` (artist+), `POST /{id}/refund` (owner-only), `GET /{id}/client-secret`, `GET /{id}/invoice`, `GET /capabilities` (anonymous-accessible read of provider capabilities). Rate-limited (`billing` policy) on every money-moving action.

**`BillingEndpoints.cs`** (`/api/v1/billing`): plan CRUD (admin-only for writes), `GET /subscription`, `GET /usage`, `POST /subscription`, `POST /subscription/checkout` + `/checkout/finalize`, `PUT /subscription/plan` (change), `DELETE /subscription/plan/pending` (cancel a scheduled change), `POST /portal` (Stripe Customer Portal). Plus the two `/api/v1/webhooks/stripe/{billing,connect}` routes (anonymous, HMAC-verified via `Stripe-Signature`).

### 3.6 Frontend

- `frontend/src/features/payments/components/`: `CreatePaymentIntentPage.tsx`, `DepositCheckoutPage.tsx`, `PaymentMethodSelector.tsx`, `PaymentDetailPage.tsx`, `PaymentListPage.tsx`, `CashDepositConfirmButton.tsx`, `SessionSplitsEditor.tsx`.
- `frontend/src/features/billing/components/`: `BillingPage.tsx`, `SubscribePage.tsx`.
- Help content already covers this surface: `client-deposit-pay`, `owner-payments`, `owner-payment-create`, `owner-cash-confirm`, `owner-billing`, `owner-subscribe`, `issuer-activate-cash-sub` (83 articles total in `helpContent.ts`, four onboarding tours).

---

## 4. The legal story behind the current state

### 4.1 What went wrong with the original design

The original Flow A design routed every client card payment into the **platform's own Stripe account** (an aggregator model — `IStripePaymentService`'s own doc comment: *"all PaymentIntents go directly to the platform's Stripe account. No connected account headers."*). A same-day migration (`RemoveStripeConnect`, 2026-06-11) had already dropped `studios.StripeAccountId`, cementing this shape.

Under Albanian Law 55/2020, a technical payment-service provider is excluded from licensing requirements **only** if it never takes possession of client funds. An aggregator model does take possession. On the shipped design, the platform would have needed a Bank of Albania payment-institution licence it doesn't have. It's also moot commercially: **Stripe does not onboard Albanian merchants at all**, which is presumably why Connect support was removed in the first place — the removal quietly converted a legally clean (if inoperable) design into a legally unviable one.

The mitigating fact, stated explicitly in the audit: nothing was deployed and no money had moved, so this was a **launch blocker, not a live exposure**.

### 4.2 ADR-0001 — the decided replacement (accepted 2026-07-31)

| Flow | Decision | Why |
|---|---|---|
| **A — client → studio** | **POK**, plus an always-on cash/record-only path. Bank VPOS (per-bank card terminals) rejected for v2. | POK is the only Albanian provider offering a first-class `splitWith` platform-fee field at payment time — without it the platform is stuck in a referral model with no pricing control. Native ALL currency. `autoCapture:false` + `expiresAfterMinutes` map directly onto a deposit-hold-then-capture flow. Real sandbox, JWT auth, testable in CI. Backed by a Bank-of-Albania-licensed EMI (RPAY sh.p.k.), keeping the platform inside the Art. 4(g) exclusion. |
| **B — studio → platform** | **Polar** as merchant of record, **Paddle** as fallback. Manual invoice + bank transfer retained as a path (not a provider) for holdouts. | An MoR removes a compliance dependency the platform can't otherwise satisfy: B2B invoicing a studio directly would require issuing an e-Fatura carrying their NIPT, which easyPos's public API explicitly can't do and its desktop-only local API can't be called from a cloud backend. With an MoR, Polar invoices the studio and the platform issues one monthly export-of-services invoice to a single foreign counterparty instead. Albania is explicitly named in Polar's own payout documentation (payouts run over Stripe Connect Express, whose country coverage is wider than Stripe Payments proper). |
| **Fiscalization** | **easyPos (ESDP)**, unrelated to either money-moving flow | Registers invoices with Albanian tax authorities and returns NIVF/NSLF codes — it moves no money and cannot carry a NIPT, so it's structurally unusable for Flow B; it's the "missing second half" for Flow A (every studio payment must be fiscalized). |

Explicit non-negotiables from the ADR's "Consequences for the codebase" section: three separate abstractions (`IPaymentProvider`, `ISubscriptionBillingProvider`, `IFiscalizationProvider`) that must never be collapsed; capability flags gate logic, never assumed lowest-common-denominator behavior; **no platform balance, no `PlatformLedger`, no `PayoutQueue`** — an architecture test fails the build if such an entity appears, specifically to keep the platform inside the Art. 4(g) exclusion; webhooks are triggers only, never sources of truth (always re-fetch canonical state); Flow B revenue must live in a legally separate account from anything Flow A touches.

### 4.3 Amendment A (same day) — the existing code was worse than assumed, in a specific way

A repo audit against the actual `main` branch (commit `7e4196c`) found the original ADR was written as if this were a greenfield payments layer. It wasn't — but the existing Stripe implementation's value was as a **shape**, not as working software, because neither half could go live as-is:

- **Flow A** needed deleting, not migrating (the Art. 4(g) exposure) — but its five methods (create intent, capture, cancel, status, refund) mapped almost 1:1 onto POK's shape, so the *interface* was worth keeping even as its implementation was thrown away.
- **Flow B** was legally fine but commercially blocked (Stripe won't onboard the entity) — genuinely valuable as a **porting template** for Polar (checkout + portal + webhooks + discounts is the same shape).

The amendment also flagged three previously-unpriced gaps that were on the critical path regardless of which payment provider gets chosen: dead `/privacy`/`/terms` links reachable from the login page (since fixed), health-data consent forms with no stored consent text or version (Article 9 exposure — since partially fixed, see §5), and a brand-name mismatch between the live site ("TattooOS") and the legal entity ("Pena e Artë") that a KYC reviewer would catch (resolved by design — brand stays "TattooOS," legal entity disclosed site-wide).

It also formally separated the platform-fee concept from the pre-existing `SessionSplit` entity: `SessionSplit` rows must sum exactly to the payment total (an owner-facing artist/studio split), so the new platform fee is `Payment.PlatformFeeAmount` — a distinct field, never a `SessionSplit` row.

### 4.4 What's shipped since the ADR (EPIC-0001, 2026-07-31, and later)

- `IPaymentProvider` + `NullPaymentProvider` (fail-closed default) — replacing the deleted `IStripePaymentService`.
- `Payment` migration: `StripePaymentIntentId` → `ProviderReferenceId` (rename, no data loss), new `Provider`, `Currency` (default `"ALL"`), `HoldExpiresAt`, `PlatformFeeAmount` columns.
- `PaymentReconciliationJob` gained a hold-expiry auto-release pass.
- `GET /api/v1/payments/capabilities` so UI logic gates on real backend capability, not an env-var proxy.
- An architecture fitness test (NetArchTest.Rules) enforcing the "no platform balance/ledger" rule.
- Adjacent compliance work bundled into the same epic: platform legal-entity disclosure + public policy pages (ToS/Privacy/Refund/Contact), versioned consent templates with immutable snapshots, a two-stage retention/erasure purge job, and a per-tenant secrets provider (`ISecretsProvider`, fail-closed, local Vault dev mode).
- **2026-09-05 (PR #95):** frontend card-option gating fixed to trust the real capabilities endpoint (previously it could show a working-looking Stripe form that always failed server-side); `RefundPaymentCommand` and `ConfirmCashDepositCommand` now implement `IAuditableCommand` (previously refunds and cash confirmations left no audit trail); intake-form submissions (free-text medical/tattoo-history data) now require and record consent via a new `ConsentTemplateKind.IntakeFormConsent`.

### 4.5 What has not shipped

- **No POK, easyPos, or Polar integration code exists anywhere in the repo** (confirmed: zero file-name or symbol hits for any of the three across `Domain`/`Application`/`Infrastructure`).
- No `IFiscalizationProvider`, no `ISubscriptionBillingProvider`, no `BillingMandate` — all named in ADR-0001's "Consequences" section, none built yet.
- Flow B has **not** been re-pointed at Polar; it is still 100% `Stripe.net`, and per the 2026-09-03 finding below, that path may not be viable to take live at all under the current entity.
- `.env.example` only defines `STRIPE_*` keys — no POK/easyPos/Polar configuration keys exist yet.
- `DepositCheckoutPage.tsx`/`PaymentMethodSelector.tsx` are UI shells built against `IPaymentProvider` but have no real provider to actually complete a card payment against — this is expected and by design until POK lands, not a bug.

### 4.6 The Sept 3 discovery: Flow B faces the same problem as Flow A

While building a real K3s staging environment (2026-09-03), a `StripeHealthCheck` fix's commit message surfaced a fact the staging plan hadn't accounted for: **Stripe's Albania block is country-level, not Connect-specific** — it blocks *any* live Stripe merchant account for this entity, not just the aggregator model Flow A used. Phi confirmed this explicitly the same day. Consequence, stated directly in `production-readiness-gap-audit-2026-09-03.md`:

> "`Flow B` (`IStripeBillingService`, subscription billing) still calls Stripe.net directly and needs the same provider rethink [Flow A already got]. This is an external/business decision, not something an overnight session can resolve — the master prompt should either leave Flow B's production Stripe secrets unset... or wait on the POK/alternative-provider decision before wiring real keys."

Practically: local dev and the planned test-mode staging environment are unaffected (Stripe test mode works regardless of merchant country), but **going live with real subscription billing on Stripe is currently not possible for this entity**, and ADR-0001's Polar decision for Flow B has not yet been implemented in code.

---

## 5. Bugs and audit history (payment-specific)

### Fixed

| Date | Finding | Fix |
|---|---|---|
| 2026-07-02 | — | Added `Stripe.BalanceService`-backed health check (`/health/ready` probes Stripe) |
| 2026-07-20 | `CancelAppointmentCommand`'s refund branch only checked `PaymentStatus.Captured`, never `Paid` — a reachable path left a payment `Paid` directly, so cancelling skipped the real refund call but still marked `DepositStatus = Refunded` (UI claimed a refund that never happened) | Refund on both `Captured` and `Paid`; only flip `DepositStatus` when a refund action actually occurred |
| 2026-07-20 | Session splits had no read path (`PaymentResponse` never returned `Splits`) — owner couldn't see splits they just saved | Added `PaymentResponse.Splits`, wired into `GetPaymentByAppointmentQuery` |
| 2026-07-20 | Both Stripe webhook handlers let unhandled exceptions bubble into a non-200 response, causing Stripe to retry an event whose failure was a platform bug | Wrapped in try/catch, log and return 200 |
| 2026-07-20 | `paymentsApi.ts`'s `confirmCashDeposit` only invalidated its own `Payment` RTK Query tag — appointment deposit-status badges never refreshed after cash confirmation | Cross-slice `dispatch(appointmentsApi.util.invalidateTags(...))` inside `onQueryStarted` |
| 2026-07-21 | `GetRevenueSummaryQuery` filtered strictly on `Status == Paid`, so a partially-refunded payment vanished from revenue reports entirely, including the retained portion | Added `Payment.RefundedAmount`; query now includes `Paid \|\| Refunded` and sums `Amount - RefundedAmount` |
| 2026-07-21 | `CancelSubscriptionCommand`'s Stripe cancellation could roll back an already-committed local cancellation on a Stripe timeout | Best-effort Stripe call **after** `SaveChangesAsync`; errors logged, never rethrown |
| 2026-07-19 | `GetPlatformStatsQuery`/`GetMrrHistoryQuery` computed MRR from `Plan.PriceMonthly` unconditionally, overstating revenue for every yearly-billed subscription (e.g. showing 79 instead of the real 790/12 ≈ 65.83 monthly-equivalent) | Fixed as part of the `Plan`/`PlanPrice` split — MRR now uses the `PlanPrice` matching the subscription's actual `BillingInterval` |
| 2026-07-19 | Two consecutive data-integrity bugs from the old single-row `Plan` billing-cadence design: a stale-seed issue (`bug-report-plans-page-data-mismatch.md`) and a duplicate-legacy-Premium-row issue (`bug-report-premium-plan-duplicate-legacy-row.md`) | `Plan`/`PlanPrice` split; one-time plan seed replaced by an always-on reconciliation pass |
| 2026-09-05 (PR #95) | Card-payment UI trusted only a client-side Stripe-publishable-key proxy — a configured test-mode key with no working provider behind it (i.e. today's actual state) rendered a working-looking card form that failed server-side every time | `GET /api/v1/payments/capabilities` exposes real `IPaymentProvider.Capabilities`; frontend gates on that |
| 2026-09-05 (PR #95) | `RefundPaymentCommand`/`ConfirmCashDepositCommand` had zero audit trail — the only payments command implementing `IAuditableCommand` was `UpdateSessionSplitsCommand` | Both now implement `IAuditableCommand` |
| 2026-09-05 (PR #95) | Intake forms collect free-text medical/tattoo-history data with no consent UI at all | New `ConsentTemplateKind.IntakeFormConsent`, required + recorded on submission |

### Reviewed and intentionally left as-is (not bugs)

- **`DeleteDepositRuleCommand` "in use" check** — not implemented, and correctly so: no `DepositRuleId` FK exists on `Appointment` (the deposit amount is snapshotted at booking time), so a rule is always safely deletable.
- **`GetSubscriptionQuery` 404-on-missing-subscription** — unreachable in practice; `RegisterStudioCommand` unconditionally creates a `Trialing` subscription row at signup.
- **`CashPending` self-cancel exemption from `ClientCancellationPolicy`** — deliberate: `CashPending` means the client only *declared intent* to pay cash; no money has actually been collected yet, so there's nothing to forfeit or partially refund.

### Open (per `production-readiness-gap-audit-2026-09-03.md`, Tier 3 — now closed by PR #95, see §4.4)

All four Tier-3 "Compliance + payment-flow correctness" items (3.1 deposit-checkout/backend mismatch, 3.2 intake-form consent, 3.3 refund/cash-confirmation audit logging, 3.4 the frontend test for 3.1) were closed in PR #95 (2026-09-05) — two days after the audit that raised them. **This has not yet been reflected in `docs/claude/architecture.md`'s Decisions Log**, which is the one gap in this report's source docs worth calling out explicitly per this project's own "trust the source over the map" rule.

---

## 6. Key architectural decisions (condensed reference table)

| Decision | Choice | Reason |
|---|---|---|
| Payment model: aggregator vs. marketplace | Neither, going forward — moving to POK (which supports a marketplace-style `splitWith`) | Stripe Connect unavailable in-country; the old aggregator model created the Art. 4(g) exposure |
| Client payment methods | Card (provider TBD, POK designed) + Cash — no PayPal | Matches actual studio workflow; many studios take cash in person |
| Platform billing (Flow B) | Designed: Polar (MoR) w/ Paddle fallback. Live today: Stripe Billing (blocked for this entity) | MoR absorbs VAT/e-Fatura/PCI/dunning the platform can't self-serve; Stripe was the pre-existing implementation, now known non-viable to go live |
| Fiscalization | easyPos (ESDP), not yet integrated | Only credible cloud-callable Albanian fiscalization API found |
| Studio payouts | Not handled by the platform — explicitly out of scope | Platform collects (or will collect) a deposit; studio-to-artist split is an internal business matter |
| Cash payment flow | `DeclareCashDepositCommand` (client) → `ConfirmCashDepositCommand` (owner/artist) | Two-step prevents fraud; owner must physically confirm before status flips to `Paid` |
| Cash subscription activation | `ActivateSubscriptionManuallyCommand` (admin-only) | Admin confirms an out-of-band cash payment, then activates in-platform |
| Platform fee vs. session split | `Payment.PlatformFeeAmount`, a distinct field outside `SessionSplit`'s exact-sum invariant | `SessionSplit` already means something else (owner-defined artist/studio split); conflating them would break or misrepresent both |
| `Plan`/`PlanPrice` split | Billing cadence lives on `PlanPrice` (child entity) and `Subscription.BillingInterval`, not on `Plan` itself | A tier's identity and its billing cadence being the same row caused two consecutive data-integrity bugs |
| No platform balance | Architecture test fails the build if a `PlatformLedger`/`PayoutQueue`-shaped entity appears | The one structural guarantee keeping the platform inside the Art. 4(g) licensing exclusion |
| Webhooks | Triggers only, never sources of truth — always re-fetch canonical state | POK's webhooks are undocumented for signature verification; Stripe webhook processing failures must not silently corrupt state either |
| Yearly subscription pricing | Monthly price × 10 (2 months free, ~17% discount) | Standard SaaS incentive |

---

## 7. Outstanding work (in dependency order)

1. **Business decision, not engineering:** finalize the POK/Polar/easyPos account setup + KYC path, given Stripe's confirmed Albania block on both flows. This is named in the Sept 3 audit as an explicit external/business blocker, not something an overnight coding session can resolve. **Update 2026-09-11:** this is no longer just "finalize the path" — real correspondence with POK/RPay (§9 below) has confirmed, contractually, that POK will only hold a merchant account for a business whose registered head office ("zyrë qendrore") is in Albania. Every studio outside Albania is therefore ineligible for its own POK merchant account under the current `splitWith`-per-studio design; this needs an explicit decision (Albania-only studio onboarding for v1, a second provider for non-Albania studios, or some other structural change) before Flow A implementation proceeds, not just KYC-path scheduling.
2. **Flow B production-readiness decision:** either leave Flow B's production Stripe secrets unset and keep it non-functional in production (app already tolerates unconfigured Stripe per the `StripeHealthCheck` fix), or wait on the Polar/alternative-provider integration before wiring anything live.
3. **POK integration** (Flow A) — build against the existing `IPaymentProvider` interface; `NullPaymentProvider` was written to make this a drop-in replacement.
4. **easyPos integration** (`IFiscalizationProvider`, does not exist yet) — fires off a settled-payment event, never inline in the payment path, per ADR-0001's consequence #7.
5. **Polar integration** (`ISubscriptionBillingProvider`, does not exist yet) — porting the existing, tested `StripeBillingService` shape (checkout, portal, webhooks, discounts) is explicitly called out as cheaper than a rewrite.
6. Remove the orphaned `/api/v1/webhooks/stripe/connect` route and its dead Flow-A-Connect-era command dispatches once a real provider decision lands (currently harmless dead code, not a live risk).
7. Update `docs/claude/architecture.md`'s "Payment Architecture — Card & Cash Only" section, which still describes the deleted `IStripePaymentService`/`StripeConnectService` design rather than the current `IPaymentProvider`/`NullPaymentProvider`/POK-pending state.

---

## 8. Source inventory

## 9. Ground-truth update — real POK/RPay correspondence + sample merchant agreement (2026-09-11)

Everything in §§1–8 above was built from internal docs and public POK documentation. Since then, real correspondence has happened: an actual outreach email was sent to POK (as "Ali Kreku," Phi Software Solutions) and POK's support team (`support@rpay.ai`) replied on 2026-09-10 with direct answers. Alongside that reply, real attachments POK/RPay had sent by email over the course of the correspondence — not third-party or public material — were reviewed: a dated sample merchant agreement (`Marrëveshje Bashkëpunimi` / "Cooperation Agreement," a blank-signatory RPay↔"Biznesi" contract template dated 24.08.2026), RPay's official business-registry extract (`RPAY_nipt.pdf`, QKB, current as of 03/09/2026), and a commercial sales deck from an earlier June 2026 exchange (see the correction below). This section records what's now *confirmed* rather than assessed from docs, and supersedes the equivalent open items in §6/§7 above and in `docs/payments/pok-assessment.md`.

**RPay's regulatory status — confirmed.** The registry extract confirms RPAY sh.p.k. (NUIS `M11328018F`, incorporated 27/01/2021, registered office Rruga Frang Bardhi, Kristal Center, Tirana, status "Aktiv" as of 03/09/2026) has a company-registry scope of activity that includes e-money issuance and payment services. More importantly, the sample merchant agreement itself states RPay is **"e regjistruar nga Banka e Shqipërisë me licensë nr. 50 më datë 9 gusht 2021 si Institucion i Parasë Elektronike"** — registered by the Bank of Albania under **license No. 50, dated 9 August 2021, as an Electronic Money Institution**. This closes the "verify RPAY sh.p.k. on the Bank of Albania EMI register" item from ADR-0001: it's confirmed, with a specific license number, directly from RPay's own merchant contract, though it hasn't been independently cross-checked against the BoA's public register.

**Merchant eligibility — confirmed, and it's a hard constraint, not a soft one.** Two independent sources now agree: POK support's reply states plainly, "POK currently onboards only businesses registered in Albania." The sample agreement goes further and makes it a *signing condition*: access to RPay's services is conditioned on the Business being a natural or legal person who is not a "US Person" (FATCA sense) **and has its registered head office in Albania** ("që vepron për qëllime profesionale dhe ka zyrën qendrore në Shqipëri," Art. 4.1). This is now a contractual eligibility gate, not just a support rep's informal answer — a studio incorporated outside Albania cannot become a POK merchant under this template at all. Separately, cardholder eligibility is unrestricted by card-issuing country — clients can pay with foreign-issued cards, and POK explicitly holds ALL and EUR balances (ALL is the default; a studio must add a EUR sub-account separately to avoid conversion). If a foreign card triggers currency conversion, that happens on the cardholder's side with no extra POK fee to the merchant.

**Pricing — no longer entirely open.** The sample agreement's "Kushtet e Veçanta" (Special Conditions) section lists concrete rates for this contract template (confirm whether these are TattooOS-specific or POK's standard published rate card before relying on them for financial modeling):

| Transaction type | Fee |
|---|---|
| Fund transfer between businesses inside the POK Merchant app | 0% |
| Online card payment from a payer not registered on the POK app (guest/card checkout) | 2.5% + 20 Lekë / €0.20 |
| Payment from an Individual payer who *is* registered on the POK app | 1.7% |
| Physical (POS) card payment | 2.5% + 20 Lekë / €0.20 |
| Withdrawal to the business's own bank account | 0% |
| Chargeback procedure requiring a merchant refund | €75 per case |
| Visa Business card annual maintenance | €0 year 1, then 100 Lekë/year |
| Signature Business card annual maintenance | 1,600 Lekë/year |
| POS annual maintenance | 5,900 Lekë/year (or 3,500 Lekë every 6 months) |
| Installation | Free |

No pricing tier by volume is shown in this template, and the online-card-payment rate does not appear to vary by domestic vs. foreign card issuance — a flat rate applies regardless. Nothing in the template sets a minimum monthly volume or minimum monthly fee. Refunds themselves carry no explicit fee (only chargebacks do, at €75 plus, if the business's balance can't cover it, RPay fronts the funds as an interest-free-for-3-months credit, then interest at 1yr Euribor+4% for EUR or 1yr T-bill+3.5% for ALL).

**Settlement — clarified, still not an exact number of days.** The agreement describes a pull model, not a payout schedule: the business can withdraw its balance to its declared bank account "at any moment," and the transfer follows "normal banking timeframes" — RPay disclaims responsibility for bank-side delays. There's no stated settlement SLA (e.g., "T+2"); this is worth a direct, narrower follow-up question if exact timing matters for cash-flow planning.

**Platform/reseller model — a negative finding, not just an open question.** The sample agreement is a strictly bilateral RPay↔Biznesi contract with no provision for a platform intermediary provisioning multiple merchants. This corroborates support's "onboarding is a manual process that each business must complete individually" and effectively answers outreach-email Q6/Q7 in the negative for now: there is no visible partner/platform program in what POK actually sends prospective merchants — each studio would sign this same agreement directly with RPay.

**Other confirmed items (from the support-team reply, POK support, `support@rpay.ai`, 2026-09-10), consistent with and adding to the above:** `splitWith` uses the platform's merchant ID, set per order, with each studio holding its own credentials; authorize-now/capture-later plus full and partial refunds and cancellations are supported via the API; webhooks are not guaranteed signed — always re-fetch order status via the API before acting; no OpenAPI/Swagger spec is published — POK points to `https://docs.pokpay.io/rest-api` "including the instructions at the bottom of the page" (re-checked directly for this update — see note below); web (non-native) checkouts must use POK's own UI for the 3-D Secure step, there is no unlisted way to do 3DS in a fully custom web UI; `splitWith.userPhoneNumber` is per-order only — a recurring commission split must be re-specified on every order, there's no persistent/repeatable configuration; there is currently no dispute/chargeback API, only the merchant dashboard. POK's own words: pricing detail beyond the sample template, exact settlement timing, formal partnership terms, and production availability of merchant-initiated/MOTO payments "require separate confirmation" — those remain genuinely open.

**No embedded hyperlinks found.** Both `RPAY_nipt.pdf` files (identical content, confirmed byte-for-byte duplicates, sent by mistake) are a static government registry extract with no link annotations. The `.docx` agreement contains no hyperlinks either — its only relationship targets are internal (styles, footer, fonts, the embedded logo image); the `contact@rpay.ai` / `support@rpay.ai` addresses appear as plain text, not clickable links. There was nothing further to follow in either file.

**Correction — a POK sales deck (`POK_Prezantim.pptx`), not a second NIPT PDF.** The user's actual intended fourth attachment was a 4-slide POK commercial presentation ("CONFIDENTIAL · June 2026"), sent by POK's commercial team by email during an earlier stage of the correspondence, not the duplicate registry PDF. No hyperlinks in it either. It adds:

- **A concrete onboarding SLA:** POK's standard rollout is **go-live within 7 days** — days 1–3 onboarding/KYB and contract signing, days 3–5 technical installation (POS, website/system integration, QR activation, staff cards), days 5–7 training and go-live with on-site support. Answers the "how long from signup to first live payment" half of the outstanding onboarding question, for a single direct merchant (still no platform-provisioning path — each studio would go through this individually, consistent with §9 above).
- **Pricing corroboration, with an open thread.** The deck's own "standard rates" annex (2.5% + €0.20/20 Lekë for POS/QR/web-gateway, 1.7% for a POK-app-registered payer) is identical to the sample merchant agreement's rates in §9 — which answers the "is this rate card standard or TattooOS-specific" question only partially: the deck's cover text claims a more-favorable, business-specific offer exists on a preceding page, but that page isn't present in the file we received (the slide before the rate annex has no pricing on it). So the rates in hand may already be a below-standard offer that happens to equal the standard card, or the actual discounted offer was never delivered — worth asking POK directly rather than assuming either. New, previously unseen fees: IBAN/SEPA Instant transfers at €1.5/transfer, and a staff business card at €16/year (this conflicts in currency/figure with the Lekë-denominated card fees in the sample contract — unreconciled).
- **MOTO — a supportive signal, still not a confirmation.** The deck lists MOTO (remote card-number payments) as a core commercial service alongside QR, Pay-by-Link, and SEPA Instant, with no staging-only caveat — softening, but not resolving, the open question in §9/§7 item 1's original wording about production MOTO availability. POK support's own reply still calls this out as needing separate confirmation.
- **A named commercial contact, likely stale.** "Ekipi Komercial i POK": `Amalushi@rpay.ai`, +355 68 402 9649. The deck is marked valid for 60 days from June 2026 — that window has passed, so its pricing/terms should be reconfirmed as current, not relied on as-is.
- Context only: POK states BoA licensing since 2021 (among the first EMI licensees in Albania), EU-PSD-aligned regulatory posture, 50,000+ monthly transactions, 1,700+ registered merchants, 6,000+ active issued cards platform-wide.

---

**Primary docs read for this report:**
- `CLAUDE.md`, `docs/claude/backend.md`, `docs/claude/database.md`, `docs/claude/architecture.md` (Payment Architecture section, Platform Subscription Architecture section, Feature Module Map rows 01/05/08/11/18/24/30/31/39, Decisions Log, IgnoreQueryFilters/AllowAnonymous tables, P0/P-02/full-app-audit/guest-checkout sections)
- `docs/payments/ADR-0001-payment-providers.md`, `docs/payments/ADR-0001-amendment-A-verified-repo-state.md`
- `docs/payments/legal-viable-payment-options.md`, `market-scan-both-flows.md`, `pok-assessment.md`, `easypos-assessment.md`, `paysera-wallet-api-assessment.md`, `industry-standard-payments-architecture.md`, `implementation-readiness.md`, `implementation-readiness-status-2026-07-31.md`
- `docs/claude/overnight-prompt-epic-0001-pre-implementation-hardening-2026-07-31.md`, `production-readiness-gap-audit-2026-09-03.md`, `overnight-prompt-staging-environment-2026-09-03.md`, `overnight-prompt-compliance-payment-correctness-2026-09-03.md`
- `docs/claude/overnight-prompt-payments-list-2026-06-19.md`, `-billing-page-2026-06-19.md`, `-subscription-oversight-2026-06-18.md`, `-plan-management-audit-2026-07-18.md`, `-plan-price-model-redesign-2026-07-19.md`, `-plans-seed-reconciliation-2026-07-19.md`, `-free-plan-tier-2026-07-18.md`, `-orphaned-premium-plan-2026-07-19.md`, `-issuer-studio-subs-audit-2026-07-17.md`, `-stripe-health-check-2026-07-02.md`
- `payment-fallback-prompt.md`, `payment-simplified-prompt.md`, `spec-plan-pricing-model-redesign.md`, `feature-request-free-tier-plan.md`, `bug-report-plans-page-data-mismatch.md`, `bug-report-premium-plan-duplicate-legacy-row.md`, `DECISIONS.md` (root — no payment-specific content found)

**Live source verified directly:**
- `Pena_e_Arte.Domain/Entities/{Payment,SessionSplit,Subscription,Plan,PlanPrice}.cs`
- `Pena_e_Arte.Domain/Enums/{PaymentStatus,ClientPaymentMethod}.cs`
- `Pena_e_Arte.Domain/Interfaces/{IPaymentProvider,IStripeBillingService,IPaymentInvoiceService,IStripeDiscountService}.cs`
- `Pena_e_Arte.Infrastructure/Services/{NullPaymentProvider,StripeBillingService,StripeDiscountService,PaymentInvoiceService}.cs`
- `Pena_e_Arte.API/Endpoints/{PaymentEndpoints,BillingEndpoints}.cs`
- `Pena_e_Arte.Application/Payments/**` (full command/query inventory)
- `frontend/src/features/{payments,billing}/components/**`
- `.env.example`, `git log` (commit `73f595d` / PR #95, and recent history)
