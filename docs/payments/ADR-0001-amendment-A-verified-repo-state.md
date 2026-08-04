# ADR-0001 Amendment A — verified repo state, and one finding that changes the ordering

**Date:** 31 July 2026 · **Status:** Accepted
**Amends:** `ADR-0001-payment-providers.md` (decision unchanged)
**Triggered by:** `implementation-readiness-status-2026-07-31.md` (repo audit at `7e4196c`)
**Independently verified:** the four claims below were re-checked against the source tree.

---

## Why this amendment exists

ADR-0001 and `implementation-readiness.md` were written assuming a greenfield payments layer.
That assumption was wrong. There is a substantial existing Stripe implementation covering both
flows. The **decision** in ADR-0001 is unchanged. The **sequencing, the effort estimate, and
the framing of the existing code** all change.

---

## Finding 1 — the existing Flow A design is not shippable, and not for the obvious reason

Verified verbatim at `Pena_e_Arte.Domain/Interfaces/IStripePaymentService.cs:3-6`:

```csharp
/// <summary>
/// Aggregator model: all PaymentIntents go directly to the platform's Stripe account.
/// No connected account headers.
/// </summary>
```

and `Migrations/20260611223749_RemoveStripeConnect.cs` drops `studios.StripeAccountId`.

**Read together: client money paid for a studio's services is designed to land in Pena e Artë's
own account, then be owed onward to the studio.**

That is the precise fact pattern Article 4(g) of Law 55/2020 excludes you from *only if it is
absent*. The exclusion covers technical service providers **"without them entering at any time
into possession of the funds to be transferred."** An aggregator model enters into possession
of them. On this design Pena e Artë is providing a payment service and needs a Bank of Albania
payment institution licence.

It is also commercially impossible independently, since Stripe does not onboard Albanian
merchants — which is presumably why Connect was removed in June, and the removal converted a
legally-clean design into a legally-unviable one.

**Mitigating fact: nothing is deployed.** No K3s manifests, no server, no live traffic. No
money has moved. This is a **launch blocker, not a live exposure** — but it must be understood
as a defect to unwind, not an asset to migrate.

**Consequence:** the ADR-0001 architecture test ("build fails if a platform-balance entity
exists") would fail against today's `main`. Write the test early and let it fail loudly until
the refactor lands. That is the point of it.

---

## Finding 2 — "working, tested subsystem" needs splitting in two

The audit concludes that migrating Flow B to Polar means *"replacing a substantial, working,
tested subsystem, not writing a new one,"* and that ADR-0001 did not price this in. Half right.

**The Stripe subsystem is tested but unshippable.** Stripe will not onboard an Albanian
merchant, so neither half can go live as-is. Its value is as a *shape*, not as working software:

| | Verdict | Value |
|---|---|---|
| **Flow A** (`IStripePaymentService`, 5 methods) | Design must be deleted — aggregator model is legally unviable | The five methods (create intent, capture, cancel, status, refund) map almost 1:1 onto POK. Keep the **interface shape**, discard the **money flow**. |
| **Flow B** (`IStripeBillingService`, 12 methods, `StripeBillingService`, `StripeDiscountService`, webhook handlers) | Legally fine — collecting your own revenue is not a payment service — but commercially unavailable | Genuinely valuable as a porting template. Checkout + portal + webhooks + discounts is the same shape Polar needs. |

So: **Flow B is a port (cheaper than the audit implies). Flow A is a rewrite (more expensive
than ADR-0001 implied).** They net out to roughly the audit's overall conclusion, for different
reasons.

---

## Finding 3 — the effort estimate was wrong

`implementation-readiness.md` said *"three to four weeks of engineering."* Withdrawn. Verified
gaps make it substantially more:

- Public shell (home, ToS, Privacy, Refund, Contact, Pricing) — no public marketing surface
  exists; `IndexRedirect` sends visitors to `/discover`.
- `/privacy` and `/terms` are **linked from `LoginPage.tsx:251,255` and have no routes** —
  the catch-all silently redirects them. A live broken link during PSP review.
- Health-data consent and consent-form versioning — `ConsentForm` stores a typed name and a
  timestamp but **no consent text and no version**. Unprovable consent for Article 9 data.
- Retention and deletion — zero hits for `retention`, `purge`, `RightToErasure`.
- Secrets management — zero hits for `vault`; secrets are plain env vars.
- `IPaymentProvider` refactor **plus a data migration** on `Payment` (provider-neutral
  references, a missing `Currency` column, hold-expiry TTL).
- Deployment — K3s work is spec-only.

The paperwork estimate (6–10 weeks) stands and still runs in parallel.

---

## Finding 4 — naming collision, accepted as stated

`SessionSplit` already exists and is **not** the ADR's platform fee.
`UpdateSessionSplitsCommand.cs:32-35` requires splits to sum exactly to the payment total, so a
platform fee modelled as a `SessionSplit` row would either break the invariant or understate
what the studio received.

**Decision:** the POK platform fee is a distinct field on the payment aggregate, named
`PlatformFeeAmount` — never "split" unqualified. `SessionSplit` keeps its current meaning.

---

## Revised ordering

The audit's recommended order of attack is adopted, with one reframing and one addition.

| # | Work | Change from ADR-0001 |
|---|---|---|
| 0 | **Decide the brand name.** Repo ships as "TattooOS"/`tattooos.co`; entity and ADR say "Pena e Artë". KYC compares the live site's trading name to the QKB extract. | **New.** Blocks every application. |
| 1 | Fix the dead `/privacy` and `/terms` links | **New.** Trivial, highest asymmetry. |
| 2 | Public shell + policy routes (placeholder copy; lawyer supplies text) | Was §1, now explicitly a frontend deliverable with no backend dependency |
| 3 | **Health-data consent + consent versioning + `AllowCrossTenantRead` review** | **Promoted from step 17 to near-front.** Larger exposure than payments, already in v2 scope, independent of every provider. |
| 4 | Retention + deletion job | Promoted |
| 5 | Secrets management | Unchanged — required before the first studio credential is held |
| 6 | **`IPaymentProvider` refactor + `Payment` migration + `PlatformFeeAmount` at 0% + architecture test** | **Reframed: this is the fix for a launch-blocking legal defect, not architecture hygiene.** |
| 7 | K3s deploy → public HTTPS staging | Unchanged; gates all webhook testing |
| 8+ | POK → easyPos → Polar integrations | Unchanged |

Steps 0–6 need no external credentials and can start today.

---

## What ADR-0001 got right and keeps

POK for Flow A, Polar for Flow B (Paddle fallback), easyPos for fiscalization, cash always on,
`PlatformFeeAmount` built at 0% from day one, three separate provider abstractions, no platform
balance, webhooks as triggers only. Nothing in the audit disturbs any of it.

The audit also confirms more is already built than assumed — the cash path end to end, the
deposit rules engine, auth-then-capture state machine, partial refunds, the
one-payment-per-appointment unique index, `PaymentReconciliationJob`, Hangfire, Serilog/OTel,
studio NIPT capture, audit-log infrastructure, and 83 Help articles with four onboarding tours.
POK's `autoCapture:false` lands on an existing state machine. Only the hold TTL
(`expiresAfterMinutes`) has nowhere to go yet.
