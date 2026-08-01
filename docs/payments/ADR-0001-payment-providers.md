# ADR-0001 — Payment providers for Flow A and Flow B

**Status:** Accepted · **Date:** 31 July 2026 · **Decider:** Ali Kreku
**Supersedes:** the open questions in `market-scan-both-flows.md`
**Context docs:** `legal-viable-payment-options.md`, `pok-assessment.md`,
`easypos-assessment.md`, `paysera-wallet-api-assessment.md`,
`industry-standard-payments-architecture.md`

---

## Decision

**Flow A (client → studio): POK, plus an always-on cash/record-only path.**
Bank VPOS is not built for v2.

**Flow B (studio → Pena e Artë): Polar as merchant of record.**
Fallback Paddle. Manual invoice + bank transfer retained as a path, not a provider.

**Fiscal layer: easyPos (ESDP)** for Flow A fiscalization, unchanged.

---

## Flow A — POK

### Why

1. **It is the only Albanian provider that permits the industry-standard business model.**
   `splitWith` takes a platform fee atomically at payment time. Without it, Pena e Artë is
   stuck in the referral model — no pricing control, no merchant ownership, structurally hard
   to escape. This is the deciding factor, not the API quality.
2. **Native ALL** with full FX fields. No competitor in the Flow B set can bill in lek at all.
3. **`autoCapture:false` + `expiresAfterMinutes`** maps exactly onto a deposit hold with a
   server-enforced TTL. Refund and cancel are first-class.
4. **Real sandbox** with published 3DS test cards, and plain JWT Bearer auth. Testable in CI —
   which Paysera Wallet is not.
5. **BoA-licensed EMI (RPAY sh.p.k.)** carries the regulatory weight. Pena e Artë stays inside
   the Law 55/2020 Article 4(g) technical-service-provider exclusion and needs no licence.
6. **One integration**, versus one per bank.

### Why not bank VPOS

The benchmark set consolidated *away* from multi-processor: Mindbody onto Stripe Connect in
2020, Booksy onto Stripe. Multi-provider in this industry is a geographic coverage mechanism,
never a customer-choice feature. "We already have a processor" is the most common objection in
embedded payments and the standard play is to compete on operational friction at contract
renewal, not to accommodate it. Offering processor choice at onboarding permanently damages
attach rate.

**Bank VPOS is a conversion off-ramp, not a supported tier.** Build only if a named studio
worth having is blocked on it. Never in the onboarding flow, never on the pricing page, no
commitment to feature parity.

### Why cash stays

Albania is cash-dominant outside Tirana. A booking flow that requires a card loses bookings.
Record-only is zero integration, zero legal exposure, and probably the highest-volume path in
year one. Not a deviation from the standard — a market reality the benchmark set never faced.

### Accepted risks

| Risk | Mitigation |
|---|---|
| POK webhooks have no documented signature | Treat every webhook as an untrusted ping; always re-fetch `GET /sdk-orders/{id}`. Architecture test forbids reading payment state from a webhook body. |
| No platform-level API key — each studio issues its own credentials | Accepted. It is also the cleanest Article 4(g) posture. Per-tenant secrets in Vault. |
| Small company; one Albanian EMI lost its licence in Feb 2026 | `IPaymentProvider` abstraction with bank VPOS as the identified second implementation. Never build a provider-shaped domain model. |
| No documented low-level 3DS primitive on web | Ship POK's `GuestCheckoutForm` for v2 and accept the visual seam. Revisit custom UI later. |

---

## Flow B — Polar

### Why an MoR at all

**It removes a compliance dependency we cannot otherwise satisfy.** Invoicing studios directly
means issuing a B2B e-Fatura with their NIPT. The easyPos Public API explicitly excludes NUIS,
and easyInvoice is Local-API-only (desktop over LAN) — uncallable from a cloud backend. With an
MoR, the MoR invoices the studio and Pena e Artë issues one monthly export-of-services invoice
to a single foreign counterparty. The problem disappears rather than being deferred.

Secondary: subscriptions, dunning, VAT and PCI scope all move to the vendor, which is the right
trade for a solo founder at €20–50 price points.

### Why Polar over the alternatives

- **Albania is named explicitly in Polar's own payout documentation** — first on the list — and
  they document the mechanism: Polar (US) is the merchant of record, payouts run over **Stripe
  Connect Express**, whose country coverage is far wider than Stripe Payments. That is the
  strongest written assurance available in this market.
- **Paddle** is the better *product* (B2B invoicing, Retain, maturity) but supports Albania only
  by **omission** from a sanctions list. The failure mode — rejection after building — is
  expensive. Held as fallback.
- **Creem** has the cheapest headline (~3.9% + 40¢) but adds a **€7-or-1% payout fee** that is
  punitive at early volume, routes through Wise, and is the youngest platform reviewed.
- **Lemon Squeezy** names Albania but now sits inside Stripe; strategic direction uncertain.
- **Dodo** lists Albania on the *buyer* side only; seller payout unconfirmed.
- **POK** has no recurring endpoint; the only unattended hint is a **staging-only** MOTO
  endpoint. Cannot be committed to without written confirmation of production MIT.

### Blocking pre-condition

Polar's payout depends on **Stripe Connect Express accepting an Albanian Person Fizik as an
individual business type.** Polar's docs give the exact steps to verify. **Do this before
writing any integration code.** If it fails → Paddle. If Paddle rejects → Creem. If all three
reject → invoice + transfer, with monthly e-Fatura issued manually via the easyInvoice desktop
app, accepted as toil until volume forces a better answer.

### Why not multi-provider

One merchant: us. There is no heterogeneity to absorb. Card tokens belong to the merchant of
record, so switching means every studio re-subscribes — choose once, carefully. Running two
live MoRs fragments invoicing, tax position, customer portals and MRR reporting. No platform in
the benchmark set runs two billing stacks.

**Manual invoice + bank transfer is retained as a payment *path*, not a provider** — a
reconciliation workflow in our own code for studios that refuse card. It carries the e-Fatura
obligation for that subset; accepted at low volume.

---

## Monetization — deliberately deferred, but not foreclosed

Build `splitWith` into the domain model and the POK integration **from day one, even at a 0%
fee.** Retrofitting a money split into an existing payment aggregate is painful; carrying an
unused field is free.

Keep subscription pricing for v2. Revisit the Fresha-style payments-first model (free or cheap
software, revenue from a take rate on deposits) once there is real volume data. Albania's price
sensitivity makes it unusually attractive here, and it would shrink Flow B to a rounding error —
but it is a pricing decision that needs evidence, not a research memo.

---

## What would reverse this

| Trigger | Consequence |
|---|---|
| BKT enables MSU **Recurring Plans + Split Payment** for small merchants at competitive rates | Re-evaluate both flows. Domestic acquiring in ALL into an Albanian account beats everything on cost. |
| Albanian banks adhere to **SEPA Direct Debit** | Best Flow B rail available — pull-based, near-zero cost, no card expiry. Revisit immediately. |
| POK confirms **production MIT / unattended charging** | Consider consolidating Flow B onto POK: one vendor, ALL-denominated — but weigh against losing the MoR's e-Fatura sidestep. |
| POK has no partner programme and studio onboarding takes weeks | Activation killer. Re-evaluate Flow A primary. |
| POK loses its BoA licence | Fall through to bank VPOS via the existing abstraction. |

---

## Immediate actions, cheapest first

1. **Verify Stripe Connect Express accepts an Albanian Person Fizik** as an individual business
   type, per Polar's documented steps. Free, ~10 minutes, gates the whole Flow B decision.
2. **Verify RPAY sh.p.k. on the Bank of Albania EMI register** directly. Five minutes.
3. **Email POK**: production MIT? partner/onboarding programme? webhook signing? pricing on the
   `splitWith` leg?
4. **Call BKT**: does your VPOS expose MSU Recurring Plans and Split Payment to merchants?
5. **Ask your bank**: SEPA Direct Debit Core/B2B — live or not?
6. **Email ESDP**: per-tenant API tokens issued programmatically? is `docId` idempotent? cloud
   e-Fatura on the roadmap? reseller terms?
7. **Ask your accountant**: VAT code for a tattoo service; deposit + balance as one fiscal
   invoice or two.

---

## Consequences for the codebase

1. Three separate abstractions — `IPaymentProvider` (Flow A), `ISubscriptionBillingProvider`
   (Flow B), `IFiscalizationProvider`. Different regulators, vendors, failure modes, deadlines.
   Never collapse them.
2. `IPaymentProvider` carries **capability flags** (`SupportsSplit`, `SupportsAuthCapture`,
   `SupportsHoldExpiry`, `SupportedCurrencies`). Gate UI on capability; never degrade to a
   lowest common denominator.
3. **No platform balance. No `PlatformLedger`. No `PayoutQueue`.** Architecture test fails the
   build if such an entity appears — that is what keeps us inside Article 4(g).
4. Flow B funds are our own revenue and need no licence, but live in a legally separate account
   from anything Flow A touches. Never commingle.
5. Webhooks are triggers, never sources of truth. Re-fetch canonical state.
6. One `BillingMandate` concept covering card tokens, allowances and mandates — all expire, all
   need the same "still chargeable?" check before a cycle runs.
7. Fiscalization fires off a settled-payment event, never inline. Durable queue, backoff, hard
   alert against the legal deadline. A DPT outage must not fail a client's card payment.
8. `merchantCustomReference` (POK) and `docId` (easyPos) are the idempotency keys — derive both
   deterministically from our own IDs and persist before first call.
9. Help updated in the same change: `helpContent.ts`, the standalone manual, and the studio
   onboarding tour step for connecting POK and easyPos.
