# EPIC-0002 — POK payment-provider integration (Flow A)

## Status

**Status:** DRAFT — plan for human review, NOT started · **Date:** 1 Aug 2026 · **Owner:** Phi
**Blocks:** first real card deposit taken on the platform (Flow A goes from fail-closed to live)
**Blocked by:** external prerequisites in PENA-200 (a real POK account, sandbox credentials, and a
public HTTPS staging environment) — none of which exist yet.
**Predecessor:** EPIC-0001 (PENA-100→107), merged in PR #45. That epic deleted the Stripe
aggregator, shipped the provider-neutral `IPaymentProvider` + `NullPaymentProvider` (fail-closed
DI default), added `Provider`/`Currency`/`HoldExpiresAt`/`PlatformFeeAmount` to `Payment`, stood up
`ISecretsProvider`/`VaultSecretsProvider` + the `StudioCredentialRef` pointer schema, and added the
no-platform-balance architecture fitness test. **This epic is the first real `IPaymentProvider`
implementation and the first real consumer of the `StudioCredentialRef`/Vault scaffolding.**

**Source documents (all read in full while writing this, not summarised second-hand):**
`docs/payments/pok-assessment.md` (primary), `docs/payments/ADR-0001-payment-providers.md`,
`docs/payments/ADR-0001-amendment-A-verified-repo-state.md`,
`docs/payments/implementation-readiness.md`,
`docs/payments/implementation-readiness-status-2026-07-31.md`,
`docs/payments/industry-standard-payments-architecture.md`,
`docs/payments/legal-viable-payment-options.md`, `docs/payments/market-scan-both-flows.md`,
`docs/payments/easypos-assessment.md`, `docs/payments/paysera-wallet-api-assessment.md`,
`docs/infra/ADR-0002-secrets-management.md`, `docs/engineering/EPIC-0001-*`, `CLAUDE.md`.

**Legal source read directly:** Law No. 55/2020 "On Payment Services" (English translation),
`docs/Law 55_2020 On Payment Services/Ligji_Per_sherbimet_e_pagesave_anglisht_18218.pdf` — Articles
1–15, 76–94 and the definitions in Article 5 read against the assessment docs' claims (§2 below).

**Verified against the source tree** at `epic-0002/pok-integration-planning` (branched off
`origin/main` @ `c45154c`, the PR-#45 merge). Every file/line reference below is real, not inferred.

---

## How to use this document

Seven tickets, **PENA-200 through PENA-206**, plus a phased rollout with explicit go/no-go gates
(§ Rollout). Unlike EPIC-0001, **this epic cannot start top-to-bottom today** — PENA-200 is an
external-dependency gate (a real POK account, sandbox credentials, a public HTTPS staging URL for
webhooks). Everything downstream can be *designed* now but can only be *validated* against POK once
PENA-200 clears. PENA-201 (the interface-shape reconciliation) is the one piece of pure design work
that should happen first regardless, because it changes an interface EPIC-0001 already shipped.

This is a **plan for the founder to review and decide on.** It contains no code, no migrations, and
touches nothing outside `docs/`. Several claims in the POK assessment are flagged "verify with POK"
in that document itself; those are carried forward here as **open questions (§ Open questions)**,
not resolved by guessing.

### Definition of Ready (every ticket must meet this before it starts)

- Problem statement traces to a specific file/line or a specific POK API behaviour — not a hypothesis.
- Acceptance criteria are testable against the POK **sandbox** (`api-staging.pokpay.io`), not just
  asserted in prose.
- The ticket names which CLAUDE.md non-negotiable rule(s) it is in service of, and flags any it is
  in tension with (PENA-205's `AllowAnonymous` webhook is in tension with Rule 2 — see there).
- No ticket downstream of PENA-200 starts before PENA-200's go/no-go gate is green.

### Definition of Done (applies to every ticket — do not close without all eight)

1. Acceptance criteria demonstrated against the POK sandbox (test-card trace, CI run, or `curl`
   trace in the PR), not merely unit-mocked.
2. Unit **and** integration tests added per the ticket's **Testing** section. CLAUDE.md lists
   "skip writing a test for business logic in the Application layer" as a non-negotiable *never*.
3. **No PII and no card/credential data in logs.** POK payloads carry cardholder name, email,
   phone (prefill params), and the JWE. Log only `tenant_id`/`studio_id`, `payment_id`,
   `request_id`, POK order id, and POK status — grep the diff for the client's actual data and for
   `keyId`/`keySecret`/JWE before requesting review.
4. **No secret in source.** POK `keyId`/`keySecret` resolve through `ISecretsProvider` (Vault) via a
   `StudioCredentialRef` pointer only — never `appsettings*.json`, never a DB value column, never a
   log line. This is CLAUDE.md Rule 4 and it is also what keeps the platform inside Article 4(g).
5. Every new endpoint has `.RequireAuthorization()` with the correct policy — **except** the POK
   webhook (PENA-205), which is a deliberately-documented third `AllowAnonymous` exception
   (alongside `/auth` and `/health`), mitigated by signature-or-refetch (§2, §PENA-205).
6. The architecture fitness test (`PaymentArchitectureTests`) stays green, and PENA-205 extends it
   so a handler that reads payment state from a webhook body fails the build.
7. Help kept in sync in the same PR when user-visible: `frontend/src/features/help/helpContent.ts`,
   `frontend/public/user-manual/index.html`, and the affected tour under
   `frontend/src/features/help/tours/` (CLAUDE.md Rule 7). PENA-203/204/206 are user-visible.
8. PR description states which CLAUDE.md rule(s) the change serves and which it is in tension with;
   reviewer independently re-runs the sandbox demonstration.

### Sequencing

| # | Ticket | Track | Depends on | External blocker |
|---|---|---|---|---|
| 0 | PENA-200 — External prerequisites & go/no-go gate | Ops / founder | — | POK account, BoA-register verify, sandbox creds, public HTTPS staging |
| 1 | PENA-201 — Interface-shape reconciliation (per-tenant context, confirm-URL vs client-secret, status vocabulary) | Backend (design) | — | none (design), PENA-200 to validate |
| 2 | PENA-202 — POK REST client + JWT auth + per-tenant credential resolution | Backend | PENA-201, PENA-200 (sandbox creds) | sandbox creds |
| 3 | PENA-203 — `PokPaymentProvider : IPaymentProvider` mapping + wire `Payment` fields + `splitWith` at 0% | Backend | PENA-202 | none |
| 4 | PENA-204 — Per-tenant POK onboarding / credential-provisioning flow (owner UI) | Full-stack | PENA-202 | POK partner-programme answer |
| 5 | PENA-205 — Webhook/callback endpoint + reconciliation alignment + arch-test extension | Backend | PENA-203, PENA-200 (staging) | public HTTPS |
| 6 | PENA-206 — Frontend checkout migration (Stripe Elements → POK) | Frontend | PENA-203 | checkout-UI decision (Open Q6) |

PENA-201 has no external dependency and should be done first — it revises `IPaymentProvider`, an
interface every other ticket builds on. PENA-202→206 are all gated on PENA-200.

---

## 1. What POK actually is, and the shape its API forces on us

Sourced from `pok-assessment.md` (which read `docs.pokpay.io`, the Postman reference at
`payments.doc.pokpay.io`, and `llms-full.txt` on 31 Jul 2026). **Every factual claim here inherits
that document's as-of date and should be re-verified against live POK docs and POK's own written
answers before building — see § Open questions.** Where a claim is the assessment's own "verify
with POK" flag, it is marked ⚠️.

### 1.1 Who POK is (the legal linchpin)

POK is the trading name of **RPAY SH.P.K.**, a Tirana fintech **licensed by the Bank of Albania as
an electronic money institution (EMI)**, live since September 2021. This is the entire basis of the
Article 4(g) posture (§2): **POK is the licensed party; money moves POK → studio; Pena e Artë only
creates orders and reads statuses over HTTPS and never comes into possession of the funds.**

⚠️ **Must verify before any build:** confirm RPAY SH.P.K. on the BoA official EMI register
(`bankofalbania.org` → Supervision → Licensed institutions → Electronic Money Institutions). The
market-scan doc records that one Albanian EMI ("ALPay"/Soft & Solution) had its licence **revoked in
February 2026** — EMI concentration risk here is real, not theoretical. This underwrites the whole
compliance posture and is a five-minute check (PENA-200).

### 1.2 Auth model

Plain **JWT Bearer**. `POST /auth/sdk/login` with `keyId` + `keySecret` returns `accessToken`,
`expiresIn`, `expiresAt`. No OAuth dance, no mTLS, no bespoke MAC signing (unlike Paysera). The
assessment flags two gotchas the client must handle:

- **Token lifetime docs contradict themselves** — REST/PHP docs say `expiresIn: 3600`, the Postman
  example says `"expiresIn": "3600000"` (ms, as a string). **Use `expiresAt` (ISO timestamp), ignore
  `expiresIn`.**
- **No platform-level key exists.** `403 Forbidden` on order creation is documented as "your
  `keyId`/`keySecret` is for a different merchant than the `merchantId` in the URL." **Each studio
  issues its own `keyId`/`keySecret` against its own POK merchant account.** This is per-tenant
  credentials (PENA-202/204), and it is also the cleanest possible Article 4(g) posture — every
  order is created under the studio's own credentials, settling to the studio's own balance.

### 1.3 The order lifecycle — maps onto our deposit state machine

| POK operation | HTTP | Maps to |
|---|---|---|
| Create order (`autoCapture:false`) | `POST /merchants/{merchantId}/sdk-orders` | `IPaymentProvider.CreatePaymentHoldAsync` → `PaymentStatus.Pending`→`Captured` (authorised/held) |
| Capture | `POST …/sdk-orders/{id}/capture` | `CaptureAsync` → `Captured`→`Paid` |
| Cancel | `POST …/cancel` | `CancelAsync` (release an uncaptured hold) → `Failed` |
| Refund | `POST …/refund` | `RefundAsync` (full or partial) → `Refunded` |
| Retrieve order | `GET /sdk-orders/{id}` | `GetStatusAsync` — **and the source of truth after any webhook** |

Key order-creation fields the assessment confirmed present:

- **`autoCapture: false`** — authorise now, capture later. Maps exactly onto our documented
  `PaymentStatus.Captured` = "Card deposit authorised (held), not yet captured"
  (`Pena_e_Arte.Domain/Enums/PaymentStatus.cs:11`). **No new state machine needed.**
- **`expiresAfterMinutes`** — the order self-expires server-side and can no longer be paid. This is
  what `Payment.HoldExpiresAt` (added in EPIC-0001) is for — but see the **HoldExpiresAt finding**
  in §1.6, because POK enforcing this itself changes what our own reconciliation pass should do.
- **`currencyCode: "ALL"`** native, with `originalCurrencyCode`/`appliedExchangeRate`/`finalAmount`
  FX fields returned. `Payment.Currency` (added in EPIC-0001, defaults `"ALL"`) exists for exactly
  this. No other reviewed provider bills in lek.
- **`splitWith: { merchantId | userPhoneNumber, amount }`** — a platform fee taken **atomically at
  payment time**, settled by POK directly to Pena e Artë's own POK merchant account, without the
  principal ever touching Pena e Artë. This is the field `Payment.PlatformFeeAmount` shadows. Build
  it now at a **0% fee** per ADR-0001 monetization; retrofitting a split later is painful.
- **`commissions` breakdown** (`netAmount`/`totalCommissionAmount`/`grossAmount`), `selectedBranchId`
  (maps to a studio location), `merchantCustomReference` (our idempotency handle — set it to the
  appointment/payment id), `webhookUrl`, `redirectUrl`, and `confirmUrl` / `confirmDeeplink`.

### 1.4 The checkout surface — this is where the frontend work lives

POK payment confirmation is **not** a Stripe-style client-secret-plus-Elements flow. The client
either (a) is redirected to POK's hosted `confirmUrl` (with `firstName`/`email`/`country`/`city`/
`language` prefill params), (b) is deep-linked into the POK mobile app via `confirmDeeplink`, or
(c) completes POK's own `GuestCheckoutForm` mounted in-page and styled via CSS overrides scoped to
`#pok-payment-container`. There is an `encryptCard()` escape hatch for a fully custom form, **but**:
⚠️ the assessment notes the **web SDK has no documented low-level 3DS primitive** (React Native and
Flutter do), so a custom web form means orchestrating 3DS step-up yourself against
`check-3ds-enrollment`/`setup-tokenized-3ds` with no helper. **This is a real fork in the road
(Open Q6)** and it shapes PENA-206.

### 1.5 What is absent (matters as much as what's present)

- **No webhook signature documented.** `webhookUrl` is per-order; the public docs describe no signing
  secret, no signature header, no replay protection. ⚠️ Ask POK whether one exists. Until confirmed,
  **treat every webhook as an untrusted ping and re-fetch `GET /sdk-orders/{id}`** (§PENA-205).
- **No merchant/sub-merchant onboarding API documented.** ⚠️ Studios are onboarded by POK out of
  band unless a partner programme exists (Open Q2). This decides whether PENA-204 is "self-serve
  connect" or "connect, then wait for POK KYC".
- **No .NET SDK.** PHP/JS/React Native/Flutter only. We hand-write a thin `HttpClient` REST client
  (PENA-202). ⚠️ Ask whether an OpenAPI/Swagger spec exists (a generated client beats a hand-written
  one).
- **No recurring/MIT primitive in production** (only a **staging-only** MOTO endpoint). This is the
  Flow B question and is **out of scope for this epic** — Flow A deposits are cardholder-present.
- **No payouts/settlement API and no chargeback/dispute API documented** — so we cannot show a studio
  its payout schedule or handle disputes in-app for v2. Out of scope; note as a product gap.

### 1.6 How POK maps onto the `IPaymentProvider` interface EPIC-0001 shipped — and three findings

The interface (`Pena_e_Arte.Domain/Interfaces/IPaymentProvider.cs`) has five methods and a
`PaymentProviderCapabilities` record. POK fills the capability record cleanly:
`SupportsSplit: true` (splitWith), `SupportsAuthCapture: true` (`autoCapture:false`+capture),
`SupportsHoldExpiry: true` (`expiresAfterMinutes`), `SupportedCurrencies: ["ALL", …]`.

But mapping the five *methods* onto POK surfaces **three real gaps that update what EPIC-0001
built**. These are the substance of PENA-201.

**Finding A — the interface has no per-tenant/studio context, but POK requires per-tenant
credentials.** The methods take only primitives (`CreatePaymentHoldAsync(amountInCents, currency,
paymentId, ct)`; `GetStatusAsync(providerReferenceId, ct)`; `CancelAsync(providerReferenceId, ct)`).
That shape is a residue of the Stripe *aggregator* model, where one platform-level key served every
tenant. POK has **no platform key**: to call `GET /merchants/{merchantId}/sdk-orders/{id}` you need
*that studio's* `merchantId` + `keyId`/`keySecret`. The create path can resolve the studio from
`ICurrentTenant` (it runs inside a request), but **`PaymentReconciliationJob`
(`Pena_e_Arte.Infrastructure/Jobs/PaymentReconciliationJob.cs`) calls `GetStatusAsync`/`CancelAsync`
cross-tenant, under `IgnoreQueryFilters()`, with only a `ProviderReferenceId` and no ambient
tenant** — so `PokPaymentProvider` cannot know whose credentials to use. This is a genuine interface
defect for a per-tenant provider. **PENA-201 must resolve it**, options:
  - (a) add a `studioId` (or a `PaymentContext`) parameter to each method (cleanest, explicit);
  - (b) have the provider internally resolve `ProviderReferenceId → Payment → StudioId → StudioCredentialRef`
    via the DB on every call (hides a query inside the provider; also needs a DB dependency in a
    class that currently has none);
  - (c) pass the whole `Payment` aggregate.
  Recommend (a) or a small context object. The reconciliation job already loads the `Payment` rows,
  so it can pass `payment.StudioId` at no extra cost.

**Finding B — the `(ProviderReferenceId, ClientSecret)` return tuple is Stripe-shaped and wrong for
POK.** `CreatePaymentHoldAsync` returns a `ClientSecret` the frontend feeds to Stripe.js. POK
returns no such thing — it returns `confirmUrl`/`confirmDeeplink` (and, for the in-page form, needs
the order id + the studio's public context, not a secret). `Payment.ClientSecret`
(`Payment.cs:16`) and the endpoint `GET /{id}/client-secret` (`PaymentEndpoints.cs:43`) and the
frontend `DepositCheckoutPage` all assume a Stripe client secret. **PENA-201 must decide** whether
to repurpose the second tuple slot to carry `confirmUrl` (least churn) or widen the return type to a
`PaymentHoldResult { ProviderReferenceId, ConfirmUrl?, ConfirmDeeplink?, ClientSecret? }`. The
frontend consequence is PENA-206.

**Finding C — `HoldExpiresAt` is now double-enforced.** EPIC-0001 added a third
`ReleaseExpiredHoldsAsync` pass to `PaymentReconciliationJob` to auto-cancel holds past
`HoldExpiresAt`. POK **already** expires the order server-side via `expiresAfterMinutes`, and the POK
assessment is explicit: *"Let POK expire the hold; don't build a competing Hangfire timer that can
disagree with it."* So the just-shipped pass should be **downgraded from enforcer to tolerant
safety-net**: it should treat POK as the source of truth, set `Payment.HoldExpiresAt` to mirror the
`expiresAfterMinutes` POK actually applied (not a value we pick independently), and when it does call
`CancelAsync` on a hold POK has already released it must handle a `409 Conflict`/already-cancelled
gracefully rather than erroring. **This is a semantic update to code merged three days ago** and
belongs in PENA-203/205.

**Additional wiring gap (not a finding, just unfinished):** `CreatePaymentIntentCommand`
(`Pena_e_Arte.Application/Payments/Commands/CreatePaymentIntentCommand.cs`) still injects the
provider as `stripePayments`, does **not** set `Provider`, `Currency`, `HoldExpiresAt`, or
`PlatformFeeAmount` on the new `Payment`, and passes `req.Currency` straight through without
defaulting to `"ALL"`. PENA-203 closes these.

**Also:** `PaymentReconciliationJob.ReconcileCapturedAsync` promotes a payment to `Paid` only when
`status is "succeeded"` — a **Stripe status string**. POK's status vocabulary differs; PENA-203/205
must add a POK-status → `PaymentStatus` mapping rather than string-matching `"succeeded"`.

---

## 2. Legal / compliance — checked against the actual statute, not the summaries

The founder asked for this to be verified against the law text itself. I read Law 55/2020 (English)
directly. **The assessment docs' central legal claim holds verbatim** — with several nuances and one
correction worth stating precisely.

### 2.1 Article 4(g) — the exclusion, confirmed word-for-word

Law 55/2020, Article 4, letter g (read on p.3 of the PDF, verbatim):

> "services provided by **technical service providers, which support the provision of payment
> services, without them entering at any time into possession of the funds to be transferred**,
> including processing and storage of data, trust and privacy protection services, data and entity
> authentication, information technology (IT) and communication network provision, provision and
> maintenance of terminals and devices used for payment services, **with the exclusion of payment
> initiation services and account information services**."

This matches what `legal-viable-payment-options.md`, `pok-assessment.md` and Amendment A all quote.
**The POK design sits inside it cleanly:** POK (the licensed EMI) is the acquirer; the studio is the
payee; Pena e Artë creates orders and reads statuses and never possesses the funds. The trailing
carve-back ("with the exclusion of payment initiation services") is why the *pay-by-bank/PISP* option
(A3 in the market scan) is a future integration of a licensed PISP, not something we build — but POK
is card/EMI acquiring, not payment initiation, so it is unaffected.

### 2.2 Article 5(35) "Acquiring" — confirms POK, not us, is the PSP

Definition read on p.6: *"'Acquiring of payment transactions' means a payment service provided by a
payment service provider **contracting with a payee to accept and process payment transactions**,
which results in a transfer of funds to the payee."* POK contracts with the studio (payee) — that is
the acquiring relationship, and it is POK's, established under the studio's own POK merchant account.
Pena e Artë has no contract to accept/process and no fund transfer runs to it. Confirmed.

### 2.3 Article 4(a) cash exclusion — the record-only path is clean

Article 4(a) (p.2): *"payment transactions made exclusively in cash directly from the payer to the
payee, without any intermediary intervention"* are outside the law entirely. The always-on
cash/record-only path (already built end-to-end: `ClientPaymentMethod.Cash`,
`PaymentStatus.CashPending`, owner confirmation) carries **zero** payment-services exposure. Keep it.

### 2.4 Article 4(b) commercial-agent exclusion — correctly NOT relied on

Article 4(b) (p.2) excludes agents authorised to negotiate/conclude a sale "**on behalf of only the
payer or only the payee.**" `legal-viable-payment-options.md` correctly warns against building on
this (it requires actual authority to conclude the sale, which a booking platform lacks, and EBA
practice reads it narrowly). The POK route does **not** rely on 4(b) — it relies on 4(g). Good.

### 2.5 Article 90 — Strong Customer Authentication is POK's obligation, not ours

Article 90 (p.49) requires the **payment service provider** to apply SCA when the payer "initiates an
electronic payment transaction," with dynamic linking to amount and payee (90.2). **The PSP here is
POK, not Pena e Artë.** This is a direct argument for shipping POK's **hosted** checkout form for the
pilot (Open Q6): it keeps SCA/3DS orchestration — and its regulatory weight — with POK, and keeps us
at PCI **SAQ-A** (card data never touches our infra or forms). A custom `encryptCard()` web form
would have us orchestrating a flow that is subject to POK's SCA duty with no documented web 3DS
helper — more risk, no regulatory upside. **Recommendation: hosted form for the pilot.**

### 2.6 Article 94 — a correction to the "enormous fine" framing

`implementation-readiness.md` §2b and §6 warn of fines "up to 4% of total annual worldwide turnover"
and call the maximum "enormous." **That 4% figure is the Law 124/2024 *data-protection* penalty — it
is not the payment-services penalty.** Law 55/2020 Article 94 (p.52) sets the administrative penalty
for providing payment services **without a licence** (or outside a registered exemption) at **ALL
50,000 to ALL 250,000** (~€500–2,500). The real teeth of getting the Article 4(g) posture wrong are
therefore: (1) the BoA's power under Article 6.3 to **order the activity stopped**, (2) reputational
damage with studios, and (3) the *separate and much larger* data-protection exposure — not the
payments fine itself. This does not change the decision (stay inside 4(g) regardless), but the plan
should not overstate the payments-law fine. The largest quantified regulatory risk in this whole
product remains **health-data** under Law 124/2024 (EPIC-0001's territory), not payments.

### 2.7 Safeguarding / commingling (Article 12) — why the architecture test exists

Article 12 (p.10–11) is the safeguarding regime that binds *licensed* institutions: funds of payment
users must not be commingled with the institution's own. Pena e Artë avoids this regime entirely by
never holding user funds — which is exactly what `PaymentArchitectureTests` enforces (no
`PlatformLedger`/`PayoutQueue`/`PlatformBalance` type). PENA-205 extends that test (no reading
payment state from a webhook body). **`splitWith` does not create a balance:** POK settles the fee
leg directly to Pena e Artë's own merchant account as earned software revenue; the principal never
lands on a Pena e Artë ledger. ⚠️ **But taking a percentage of transaction value may raise an
activity-code / entity question** (`implementation-readiness.md` §2a: is a share of transaction
value a "service fee" or something "payment-adjacent"?) — Open Q8, for the accountant/lawyer.

### 2.8 Law is a PSD2 approximation

Footnote 1 (p.1) states the law "partially approximates" EU Directive 2015/2366 (PSD2). This
validates the assessment docs' reliance on PSD2 Art. 3(j) interpretive practice as persuasive for the
Albanian 4(g) reading. It also means the SCA regulatory technical standards (Art. 91) will track the
EU RTS — another reason to let POK, the licensed party, own SCA.

**Net legal verdict: nothing in the statute blocks launching Flow A via POK.** The one written
assurance still worth obtaining (cheap insurance, and a sales asset with studios) is a payments
lawyer confirming in writing that a non-custodial booking platform sits in 4(g) and that a
`splitWith` software fee does not recharacterise it (Open Q8). Not a code blocker.

---

## PENA-200 — External prerequisites & go/no-go gate

**Priority:** P0, blocks every other ticket · **Track:** Ops / founder · **Est:** external lead time

### Why this ticket exists

Every ticket below needs something that does not exist in the repo or the business yet. Per
`implementation-readiness.md` §5: *"you need a deployed, publicly reachable HTTPS staging environment
before you can integration-test anything… `localhost` will not work."* POK calls back to a
`webhookUrl`. This ticket is the gate; it is mostly not code.

### Acceptance criteria (all must be true before PENA-202 starts)

- [ ] **RPAY SH.P.K. confirmed on the BoA EMI register** directly (not a third-party directory).
- [ ] **A POK merchant account exists** for Pena e Artë (needed as the `splitWith` fee recipient,
      even at 0%) and **sandbox credentials** (`keyId`/`keySecret` for `api-staging.pokpay.io`) are in
      hand and stored in dev-mode Vault under a `StudioCredentialRef`-shaped path.
- [ ] **Written answers received from POK** to the § Open questions block (partner programme / MIT /
      webhook signing / pricing / OpenAPI). These change the design of PENA-204/205 and must not be
      guessed.
- [ ] **Public HTTPS staging environment is live** with a stable URL for the webhook endpoint. This
      is the K3s deploy that `docs/claude/overnight-prompt-k3s-production-deploy-2026-07-26.md` specs;
      it is a hard prerequisite, tracked here, out of this epic's code scope.
- [ ] **HCP Vault decision from ADR-0002 confirmed** as the production secrets backend the real POK
      credential will resolve through (no new bespoke mechanism).
- [ ] Day-one 30-second check: **POK React SDK works on React 19** (docs say React 17+; almost
      certainly fine, verify).

### Explicitly out of scope

- The K3s deployment build itself (its own spec/epic).
- Any real *studio's* POK account (that is PENA-204, per pilot studio).

### Go/no-go

**GO** when all boxes are checked. **NO-GO** blockers to surface immediately: BoA register does not
list RPAY; POK has no sandbox; POK confirms studio onboarding takes weeks with no partner programme
(ADR-0001's "activation killer" reversal trigger — re-evaluate Flow A primary before building).

---

## PENA-201 — Interface-shape reconciliation

**Priority:** P0, revises an interface every downstream ticket depends on · **Track:** Backend
(design + refactor) · **Est:** 2–3 days · **Depends on:** — (do first)

### Problem, verified

`IPaymentProvider` as shipped in EPIC-0001 carries three Stripe-aggregator residues that break for a
per-tenant, hosted-checkout, POK-status provider — **Findings A, B, C in §1.6**. This ticket resolves
the interface *before* `PokPaymentProvider` is written against it, so we don't build the concrete
class twice.

### Acceptance criteria

- [ ] **Per-tenant context (Finding A):** every `IPaymentProvider` method that POK must route to a
      specific merchant account takes a studio identifier (a `studioId` parameter or a small
      `PaymentContext { StudioId }`). `PaymentReconciliationJob` passes `payment.StudioId` (it already
      has the row). `NullPaymentProvider` and all tests updated. Decide and document the choice.
- [ ] **Return shape (Finding B):** `CreatePaymentHoldAsync` returns a type that can express POK's
      `confirmUrl`/`confirmDeeplink` (recommend a `PaymentHoldResult` record superseding the
      `(ProviderReferenceId, ClientSecret)` tuple; keep an optional `ClientSecret` slot so a future
      Stripe-shaped provider still fits). `Payment.ClientSecret`, `GET /{id}/client-secret`
      (`PaymentEndpoints.cs:43`) and `GetPaymentClientSecretQuery` are renamed/generalised to
      "confirm target" semantics, or documented as carrying the confirm URL.
- [ ] **Status vocabulary (Finding C, part):** define a provider-status → `PaymentStatus` mapping seam
      so `PaymentReconciliationJob` no longer string-matches Stripe's `"succeeded"`
      (`PaymentReconciliationJob.cs:31`). The mapping's POK values are filled in PENA-203 once the
      sandbox's real status strings are observed.
- [ ] `NullPaymentProvider` still compiles and still fails closed under the new signatures; the DI
      default is unchanged until PENA-203 registers `PokPaymentProvider`.
- [ ] `PaymentArchitectureTests` stays green.

### Explicitly out of scope

- Writing `PokPaymentProvider` (PENA-203) or any HTTP call (PENA-202).
- Touching `IStripeBillingService`/Flow B — untouched, exactly as in EPIC-0001.

### Industry standard

A payment abstraction that gates on capability and carries provider-neutral context is the ADR-0001
Consequence-2 requirement; making the interface per-tenant-credential-aware from the first real
implementation avoids the Stripe-aggregator shape ADR-0001 Consequence 1 forbids.

### Testing

- Unit tests: `NullPaymentProvider` fails closed under the new signatures; the status-mapping seam
  maps unknown strings to a safe default (never silently to `Paid`).
- Regression: `PaymentReconciliationJobTests` updated to the new method signatures and still asserts
  the three passes.

---

## PENA-202 — POK REST client + JWT auth + per-tenant credential resolution

**Priority:** P0 · **Track:** Backend · **Est:** 3–4 days · **Depends on:** PENA-201, PENA-200

### Problem, verified

There is no POK code anywhere (grep confirms zero hits). We need a thin, testable HTTP client for the
14-endpoint POK API, JWT auth with correct token caching, and per-tenant credential resolution that
never puts a secret in the DB or a log — the first real use of `ISecretsProvider` + `StudioCredentialRef`.

### Acceptance criteria

- [ ] `PokApiClient` (Infrastructure) over `HttpClient` (registered via `IHttpClientFactory`), base
      URL configurable (`api-staging.pokpay.io` in non-prod), covering auth, order create/capture/
      cancel/refund, and `GET /sdk-orders/{id}`.
- [ ] **Token cache keyed by `merchantId`/`keyId`**, refreshed against **`expiresAt`** (not
      `expiresIn`) with a safety margin, and **guarded so two requests cannot race the refresh** (the
      assessment calls this out explicitly). Cache is in Redis, not in-memory (CLAUDE.md: never store
      in-memory what belongs in Redis).
- [ ] **Per-tenant credential resolution:** given a `studioId`, load its `StudioCredentialRef`
      (`Provider == CredentialProvider.Pok`), resolve `keyId`/`keySecret` via
      `ISecretsProvider.GetSecretAsync("studios/{studioId}/pok:keyId" / ":keySecret")`. Fails closed
      if the ref or secret is missing (Vault provider already throws — do not swallow).
- [ ] **Idempotency:** `merchantCustomReference` set deterministically from our payment/appointment id
      and persisted before the first call (the API has no `Idempotency-Key` header — this is the only
      handle). **`409 Conflict` on capture = already-captured:** the client must not blind-retry; it
      re-fetches the order and inspects status.
- [ ] **Zero PII / zero secret in logs:** log `studio_id`, `payment_id`, POK order id, POK status,
      `request_id` only. Never the JWE, `keySecret`, cardholder name/email/phone, or full payloads.
- [ ] Resilience: timeouts, bounded retries with backoff on idempotent calls, and structured
      failure surfacing (Serilog, no `Console.WriteLine`).

### Explicitly out of scope

- Mapping onto `IPaymentProvider` (PENA-203). This ticket is the raw client + auth + credential seam.
- The onboarding UI that *writes* a credential (PENA-204) — this ticket only *reads*.
- Card tokenization endpoints (not needed for a one-off hosted-checkout deposit; revisit only if a
  custom form is chosen in Open Q6).

### Industry standard

Plain Bearer + `IHttpClientFactory` + Redis-cached token with single-flight refresh is the standard
.NET shape for a REST payment API with no vendor SDK. OWASP ASVS V6 secrets handling; CWE-798.

### Testing

- Unit: token cache returns cached token before `expiresAt`, refreshes after, single-flights
  concurrent refreshes; `409` on capture triggers re-fetch not retry.
- Integration (sandbox, gated like the EPIC-0001 Vault test): a real `POST /auth/sdk/login` against
  `api-staging.pokpay.io` returns a usable token; credential resolution fails closed when the Vault
  path is absent.

---

## PENA-203 — `PokPaymentProvider : IPaymentProvider` + wire `Payment` fields + `splitWith` at 0%

**Priority:** P0, the core of the epic · **Track:** Backend · **Est:** 4–6 days · **Depends on:** PENA-202

### Problem, verified

`NullPaymentProvider` fails closed; this ticket delivers the real provider that maps §1.3's order
lifecycle onto the (PENA-201-revised) five methods, and closes the `CreatePaymentIntentCommand`
wiring gaps from §1.6.

### Acceptance criteria

- [ ] `PokPaymentProvider : IPaymentProvider` in `Pena_e_Arte.Infrastructure/Services/`, using
      `PokApiClient`. `Capabilities` = `SupportsSplit:true, SupportsAuthCapture:true,
      SupportsHoldExpiry:true, SupportedCurrencies:["ALL", …observed]`.
- [ ] `CreatePaymentHoldAsync` creates a POK order with `autoCapture:false`, `currencyCode` from the
      payment (default `"ALL"`), `expiresAfterMinutes` derived from the booking-confirmation window,
      `merchantCustomReference` = payment id, `webhookUrl`/`redirectUrl` to our endpoints, and
      `splitWith` present **but at a 0% fee** (field wired end-to-end, value zero). Returns the
      `PaymentHoldResult` (POK order id as `ProviderReferenceId`, `confirmUrl`/`confirmDeeplink`).
- [ ] `CaptureAsync`/`CancelAsync`/`RefundAsync`/`GetStatusAsync` map onto the POK endpoints;
      `GetStatusAsync` returns POK's real status strings and the **status-mapping seam** (PENA-201) is
      filled with observed POK values → `PaymentStatus` (unknown ⇒ never silently `Paid`).
- [ ] **`HoldExpiresAt` = POK's authority (Finding C):** set `Payment.HoldExpiresAt` to mirror the
      `expiresAfterMinutes` POK applied. `PaymentReconciliationJob.ReleaseExpiredHoldsAsync` becomes a
      **tolerant safety-net** — when it cancels a hold POK already expired, a `409`/already-cancelled
      is a success, not an error.
- [ ] **Close the `CreatePaymentIntentCommand` gaps** (`CreatePaymentIntentCommand.cs`): rename the
      injected `stripePayments` → `paymentProvider`; set `Provider = "pok"`, `Currency` (default
      `"ALL"`), `HoldExpiresAt`, and `PlatformFeeAmount` (0) on the `Payment`; persist
      `merchantCustomReference` mapping.
- [ ] **`splitWith` modelled in the domain now** even at 0% (ADR-0001 build-implication #6): the fee
      is `Payment.PlatformFeeAmount`, kept **outside** the `SessionSplit` exact-sum-to-`Amount`
      invariant (comment already on the field — do not unify them; Amendment A Finding 4).
- [ ] **DI:** register `PokPaymentProvider` as the `IPaymentProvider` implementation **only where a
      POK credential exists** — a studio with no `StudioCredentialRef` must still fail closed
      (capability-gated), so payments read "unavailable" rather than throwing an unhandled error
      (§PENA-204 handles the UI gate). `NullPaymentProvider` remains the fallback for unconnected
      studios.
- [ ] `PaymentArchitectureTests` green; no method on `PokPaymentProvider` could hold platform-owned
      funds.

### Explicitly out of scope

- The webhook endpoint (PENA-205) and the checkout UI (PENA-206).
- Any non-zero platform fee (deferred per ADR-0001 monetization; wire the field, ship 0%).
- Fiscalization (easyPos) — a **separate epic**. Note only the seam: fiscalization must fire off a
  *settled-payment domain event*, never inline in this provider (ADR-0001 Consequence 7).
- `selectedBranchId`/multi-location reconciliation beyond carrying the field through (revisit when a
  multi-location pilot studio appears).

### Industry standard

One embedded provider behind a capability-gated abstraction with an atomic platform fee is the
vertical-SaaS standard (`industry-standard-payments-architecture.md`): `splitWith` is what lets
Pena e Artë run the markup/take-rate model rather than being stuck in referral. PCI SAQ-A preserved.

### Testing

- Unit: capability flags; `CreatePaymentIntentCommand` now sets `Provider`/`Currency`/`HoldExpiresAt`/
  `PlatformFeeAmount`; `PlatformFeeAmount` never participates in the `SessionSplit` sum (regression
  for Amendment A Finding 4); status mapping (unknown ⇒ safe).
- Integration (sandbox): full lifecycle against `api-staging.pokpay.io` with the published 3DS test
  cards — create hold → capture → status `Paid`; create hold → let it expire → job releases
  tolerantly; create hold → capture → refund (full and partial); create hold → cancel.

### Help sync (DoD 7)

Flow-A card copy was neutralised (not POK-specific) in EPIC-0001. Now name POK where a client sees
it: `client-deposit-pay` (helpContent.ts:219), `owner-payments` (:611), `owner-payment-create`
(:625), the `user-manual` deposit/checkout sections, and the faq payment-methods entry. Tours: the
client tour covers deposit paying — update copy if the confirm step changes (PENA-206).

---

## PENA-204 — Per-tenant POK onboarding / credential-provisioning flow

**Priority:** P0, gates any real studio taking a deposit · **Track:** Full-stack · **Est:** 4–6 days
· **Depends on:** PENA-202 · **External:** POK partner-programme answer (Open Q2)

### Problem, verified

`StudioCredentialRef` is scaffolding — "no real credential is issued or stored anywhere in this
session; this ticket only makes the pointer mechanism exist" (its own doc-comment). This ticket is
its **first real consumer**: how a studio actually gets a POK merchant account and how its
`StudioCredentialRef` gets populated with a real Vault-stored credential. The owner tour has a
deposit-rules step and a billing step (`ownerTour.ts`) but **no "connect your payment provider"
step** — a gap CLAUDE.md Rule 6 (match the vertical-SaaS standard: payments in the onboarding flow,
not a buried settings page) says to flag and close.

### Acceptance criteria

- [ ] Owner-facing "Connect POK payments" surface in studio settings (and, per attach-rate standard,
      surfaced in owner onboarding): explains what connecting enables, links to POK signup, and
      accepts the studio's `merchantId` + `keyId`/`keySecret`.
- [ ] **The credential value goes straight to Vault via `ISecretsProvider`; only a `StudioCredentialRef`
      pointer (`Provider = Pok`, `SecretPath = "studios/{studioId}/pok"`) is written to MySQL.** No
      value column, no plaintext, no log line (the entity has no value column by design — keep it).
      New MediatR command is `IAuditableCommand` (connecting/rotating a payment credential is
      money-adjacent; audit it — the health-data/consent audit precedent from EPIC-0001).
- [ ] **Endpoint has `.RequireAuthorization("OwnerOnly")`** and a FluentValidation validator
      (CLAUDE.md: no endpoint without a validator). Tenant-scoped — a studio can only write its own
      credential (id from `ICurrentTenant`, never trusted from the request; IDOR-proof).
- [ ] **Capability gate:** until a studio has a valid `StudioCredentialRef`, its card-deposit surface
      shows "card payments not yet available — connect POK" and the backend keeps the fail-closed
      `NullPaymentProvider` path for that studio, rather than a 500. Gate the UI on
      `PaymentProviderCapabilities`/connection state, never assume support.
- [ ] Depending on Open Q2's answer: if POK offers a **partner/delegated-onboarding API**, provision
      the studio merchant via API for a "connect and take deposits today" funnel; if not, the flow is
      "enter the credentials POK issued you after their KYC," and the copy sets that expectation.
- [ ] **Credential rotation path** exists (re-submit new keys → new Vault version → same pointer),
      covered by `docs/infra/secrets-rotation-runbook.md`.

### Explicitly out of scope

- The actual POK merchant KYC (POK's process, external).
- easyPos credential onboarding (separate epic — but build this so the same pattern extends to
  `CredentialProvider.EasyPos` without a rewrite).

### Industry standard

Attach-rate maximisation: payments connection belongs in the onboarding flow, not a settings backwater
(`industry-standard-payments-architecture.md` §6). Per-tenant secret in Vault, pointer-only in the DB,
is the ADR-0001 Article 4(g) posture and OWASP ASVS V6.

### Testing

- Unit/integration: connecting writes a pointer + a Vault secret and **never** a value to MySQL
  (assert the row has no credential material); the command produces an `AuditLogEntry`; a studio
  cannot write another studio's credential; a studio with no ref is capability-gated (fails closed,
  no 500).

### Help sync (DoD 7)

New owner article "Connecting your POK account"; update `owner-studio-profile` (helpContent.ts:655);
new `user-manual` section; **add the missing "connect your payment provider" step to `ownerTour.ts`.**

---

## PENA-205 — Webhook/callback endpoint + reconciliation alignment + arch-test extension

**Priority:** P0 · **Track:** Backend · **Est:** 3–4 days · **Depends on:** PENA-203, PENA-200 (staging)

### Problem, verified

POK calls back to the per-order `webhookUrl`. The public docs describe **no signature** (⚠️ Open Q3).
CLAUDE.md Rule 2 allows unprotected endpoints only for `/auth` and `/health`; a POK webhook is a
**third, deliberately-documented `AllowAnonymous` exception** — and its safety comes not from auth but
from **never trusting the body**: the assessment's rule is *"webhook received → ignore the body → `GET
/sdk-orders/{id}` → trust only that response → transition state."*

The existing `PaymentReconciliationJob` polls. The founder asked whether the job becomes redundant,
complementary, or changes. **Answer: complementary.** The webhook is a low-latency *trigger* that
re-fetches; the job remains the durable *safety net* for missed/duplicate/late webhooks and for
hold-expiry reconciliation (Finding C). Neither is the source of truth — the POK order is.

### Acceptance criteria

- [ ] New endpoint (e.g. `POST /api/v1/payments/pok/callback`) marked **`AllowAnonymous`**, documented
      inline and in `CONTRIBUTING.md`/architecture notes as the third sanctioned anonymous endpoint,
      with the reason (unsigned provider callback, trust established by re-fetch).
- [ ] **Body is never trusted.** The handler extracts only the order id, then re-fetches
      `GET /sdk-orders/{id}` under the owning studio's credentials (resolve `studioId` from the
      persisted `merchantCustomReference` → `Payment`), maps POK status → `PaymentStatus`, and
      transitions the payment/appointment. If a signature scheme turns out to exist (Open Q3), verify
      it **and still re-fetch**.
- [ ] **Idempotent & replay-safe:** duplicate callbacks for the same order converge to the same state;
      `merchantCustomReference`/order id is the dedupe key. Rate-limited (it is anonymous and public).
- [ ] **Reconciliation alignment:** `PaymentReconciliationJob` is documented as the complementary
      safety net; its `ReconcileCapturedAsync` uses the status-mapping seam (not `"succeeded"`); its
      `ReleaseExpiredHoldsAsync` is the tolerant safety-net from Finding C. No behavioural conflict
      between the webhook and the job — both converge on the re-fetched order state.
- [ ] **Arch-test extension:** `PaymentArchitectureTests` gains a rule that fails the build if a
      webhook handler reads payment state from the request body to decide a transition (enforce
      "webhook is a trigger, not a source of truth" — ADR-0001 Consequence 5). Perfect static
      enforcement is impossible; guard the obvious shape and pair with the re-fetch code review.
- [ ] **Zero PII in the endpoint's logs** (it will receive prefill data — name/email/phone).

### Explicitly out of scope

- Fiscalization on the settled-payment event (separate epic; note the seam only).
- Dispute/chargeback callbacks (no documented POK API; product gap noted).

### Industry standard

Webhook-as-trigger-plus-canonical-refetch is standard for any unsigned or replay-prone callback
(ADR-0001 Consequence 5; the same discipline the reconciliation job already embodies). Rate-limiting
and idempotency on a public anonymous endpoint is baseline.

### Testing

- Integration (sandbox): a real POK sandbox callback drives a hold→captured→paid transition **via the
  re-fetch**, not the body; a forged body with a wrong status does **not** move state (re-fetch wins);
  duplicate callbacks are idempotent.
- Arch test proven: a handler that reads status from the body fails the build; passes once it re-fetches.

### Help sync (DoD 7)

No user-visible surface (background reconciliation) — state the "no Help change" verdict explicitly
per the EPIC-0001 pattern.

---

## PENA-206 — Frontend checkout migration (Stripe Elements → POK)

**Priority:** P0, without it a client cannot actually pay · **Track:** Frontend · **Est:** 3–5 days ·
**Depends on:** PENA-203 · **Decision:** Open Q6 (hosted form vs custom)

### Problem, verified

`frontend/src/features/payments/components/DepositCheckoutPage.tsx` is hardcoded to Stripe:
`loadStripe`, `@stripe/react-stripe-js` `Elements`/`PaymentElement`, `stripe.confirmPayment`,
`VITE_STRIPE_PUBLISHABLE_KEY`, and the footer literally says "Secured by Stripe." EPIC-0001 left this
in place because it can't function without a backend provider (completion summary Deviation #5). POK
does not use a Stripe client secret (Finding B); it uses `confirmUrl`/`confirmDeeplink` or its own
`GuestCheckoutForm`. This page must be rewritten. `PaymentMethodSelector.tsx` and 3 pre-existing
Stripe `PaymentMethodSelector` tests (the known-failing ones in the EPIC-0001 self-check) are also in
scope.

### Acceptance criteria

- [ ] `DepositCheckoutPage` no longer imports Stripe.js. Depending on Open Q6:
      - **Hosted (recommended for pilot):** consume the `confirmUrl`/`confirmDeeplink` from
        `PaymentHoldResult` — redirect to POK's hosted confirm page (with prefill params) or deep-link
        into the POK app; handle the `redirectUrl` return to show authorised/failed state. Keeps SCA
        with POK and PCI SAQ-A (§2.5).
      - **In-page form:** mount POK's `GuestCheckoutForm` in `#pok-payment-container`, styled via
        scoped CSS overrides — accept the visual seam against shadcn/ui, documented.
- [ ] No `useEffect`-for-data-fetching (CLAUDE.md frontend rule) — use RTK Query as the existing page
      does; no `any` types.
- [ ] **Capability-driven UI:** the deposit surface reflects whether the studio is POK-connected
      (PENA-204) and whether card is available — never a hard Stripe assumption.
- [ ] Remove/replace `VITE_STRIPE_PUBLISHABLE_KEY` usage and the "Secured by Stripe" copy on the
      Flow-A checkout (Flow-B billing keeps Stripe — do not touch `billing/` which still uses Stripe
      Billing).
- [ ] The 3 pre-existing Stripe `PaymentMethodSelector` tests are updated (they were the only
      persistent failures in EPIC-0001's suite); `pnpm test`/`pnpm build`/`pnpm lint` green.

### Explicitly out of scope

- Flow-B billing UI (`SubscribePage`/`BillingPage`) — still Stripe, untouched.
- Custom 3DS orchestration on web unless Open Q6 chooses the custom form (and even then, budget for
  the missing web 3DS helper).

### Industry standard

Embedded, platform-branded checkout is the vertical-SaaS standard; the hosted form keeps SCA/PCI with
the licensed party (§2.5). WCAG 2.1 AA on the checkout as on any page.

### Testing

- Component tests: hosted-redirect path builds the correct `confirmUrl` with prefill params and no PII
  leak to console; return-status handling; capability-gated empty state; no Stripe import remains.

### Help sync (DoD 7)

Update `client-deposit-pay` and the `user-manual` checkout section to describe the POK confirm flow
(hosted page or app deep-link); update the client tour's deposit step copy. Remove "Secured by Stripe"
wording from Flow-A Help.

---

## Rollout — phased, with go/no-go per phase

Mirrors EPIC-0001's phase discipline and `implementation-readiness.md` §7's "credentials-gated" order.

### Phase 0 — Prerequisites (PENA-200)

**Do:** BoA-register verify, POK account + sandbox creds, written POK answers, public HTTPS staging,
HCP Vault confirmed, React-19 SDK check.
**GO to Phase 1 when:** all PENA-200 boxes checked.
**NO-GO / re-evaluate Flow A:** RPAY not on the register; no sandbox; POK confirms weeks-long manual
onboarding with no partner programme (ADR-0001's activation-killer trigger).

### Phase 1 — Sandbox integration (PENA-201→203, 205, 206) against `api-staging.pokpay.io`

**Do:** interface reconciliation, REST client + auth, `PokPaymentProvider`, webhook, checkout — all
tested against POK's sandbox test cards, no real money.
**GO to Phase 2 when:** the full deposit lifecycle is green in CI against the sandbox — hold →
capture → `Paid`; hold → expire → tolerant release; refund (full + partial); cancel; webhook drives
state via re-fetch (forged body ignored); arch test green; `dotnet build/format/test` and
`pnpm lint/build/test` all green.

### Phase 2 — One pilot studio in production (PENA-204 with a real credential)

**Do:** onboard one named pilot studio (Open Q5) with its real POK merchant credential in production
Vault; take one **low-value real deposit** end-to-end, capture it, refund it.
**GO to Phase 3 when:** the pilot's real deposit authorises, captures, settles to the studio, and
refunds cleanly; the studio's DPA + Studio Services Agreement are signed (`implementation-readiness.md`
§3 — the studio is merchant of record, bears chargebacks); the fiscalization gap is explicitly
acknowledged as a separate epic (the pilot re-types into easyPos manually for now); no PII/secret in
any log reviewed post-transaction.

### Phase 3 — General availability

**Do:** open POK connection to all studios via the onboarding flow.
**GO when:** capability-gated UI proven for unconnected studios (fail-closed, no 500); attach/adoption
instrumentation live (the four standard metrics from `industry-standard-payments-architecture.md` §6);
secrets-rotation runbook covers POK; a support/monitoring runbook exists; the payments lawyer's 4(g)
+ `splitWith` written confirmation is in hand (Open Q8) or explicitly accepted-as-risk by the founder.

---

## Open questions for the founder (do not guess — these gate design)

Numbered continuing from the POK assessment's own "questions to send POK" so answers can be tracked.

1. **POK account & sandbox** — is a Pena e Artë POK merchant account open, with sandbox
   `keyId`/`keySecret` in hand? (Gates PENA-200.)
2. **Partner / delegated-onboarding programme** — can Pena e Artë provision studio merchants via API,
   or is every studio a manual POK KYC? *This decides whether PENA-204 is "connect and take deposits
   today" or "connect, then wait for POK."* Realistic time from studio signup to first deposit?
3. **Webhook signing** — does a signature scheme exist (undocumented)? If yes, header + rotation. If
   no, we re-fetch as the only trust (PENA-205 assumes this).
4. **Pricing** — MDR for ALL card and POK-app transactions, the `splitWith` leg fee, refund and
   chargeback costs, settlement timing. Needed to model the take-rate and to set the (currently 0%)
   `PlatformFeeAmount` later.
5. **Pilot studio** — which named studio pilots first (Phase 2)? Their existing bank/processor also
   tells us whether the bank-VPOS off-ramp is theoretical or urgent.
6. **Checkout UI decision** — POK **hosted** `GuestCheckoutForm`/`confirmUrl` (fast, on-brand seam,
   SCA + PCI SAQ-A stay with POK — **recommended for the pilot**) vs `encryptCard()` **custom** web
   form (on-brand, but you own 3DS orchestration on web with no documented helper). Shapes PENA-206.
7. **Pena e Artë's own POK merchant account** — confirmed needed as the `splitWith` fee recipient even
   at 0%? Or defer `splitWith` wiring until a fee is charged (still model `PlatformFeeAmount`)?
8. **Legal/accountant sign-off (cheap insurance):** (a) a payments lawyer confirming in writing that a
   non-custodial booking platform sits in Law 55/2020 Art. 4(g) and that a `splitWith` **software fee**
   does not recharacterise Pena e Artë as an intermediary; (b) the accountant on whether taking a
   share of transaction value needs an activity-code change / Person Fizik → SH.P.K.
   (`implementation-readiness.md` §2a). Not a code blocker; a launch/GA blocker.
9. **Hold-expiry window** — confirm the deposit-hold duration (`expiresAfterMinutes`) tied to the
   booking-confirmation window, so POK is the single source of truth and our job is only a safety net
   (Finding C).
10. **OpenAPI/Swagger** — does POK publish one? A generated C# client beats the hand-written PENA-202
    client.
11. **Assessment freshness** — the POK assessment is dated 31 Jul 2026 and self-flags several claims
    (webhook signing absent, MOTO staging-only, no OpenAPI, web-SDK 3DS absent, React 17+) as "verify
    with POK." Re-verify against live docs and POK's written answers before PENA-202 builds against
    them.

**Explicitly deferred / out of this epic (tracked so not silently dropped):**
- **Flow B (studio → Pena e Artë subscriptions):** POK has no production recurring/MIT primitive
  (only staging MOTO). Stays on Stripe Billing / an MoR (Polar per ADR-0001). Not this epic.
- **easyPos fiscalization:** the "missing second half" of Flow A (a settled-payment event triggers a
  DPT invoice). A **separate epic**; this epic only preserves the domain-event seam.
- **Bank-VPOS second `IPaymentProvider`:** the identified conversion off-ramp / EMI-loss fallback.
  Built only if a named studio is blocked on it (ADR-0001).
- **Chargeback/dispute and payout-schedule surfaces:** no documented POK API; product gap noted.
