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

> **Update (3 Aug 2026) — a materially more complete official-docs source is now on file.** The full,
> verbatim official POK documentation set (JS / React / Vanilla-JS / CDN / React-Native / Flutter /
> PHP-SDK / WooCommerce / PrestaShop / REST-API pages) has been captured at
> `docs/payments/pok-official-docs-2026-08-03.md` and is the **authoritative technical reference for
> the implementation tickets (PENA-202/203/206).** It is more complete than what `pok-assessment.md`
> was written against, and it **corrects several claims** carried into this plan — those corrections
> are folded in below and flagged **[corrected 3 Aug 2026]**. The official `docs.pokpay.io` REST API
> page carries `lastUpdated: 2026-04-10` — roughly four months old as of this epic's writing, i.e.
> current enough to treat as reliable, but not so recent that a quick re-check of `docs.pokpay.io` /
> `payments.doc.pokpay.io` before PENA-202 implementation is unwarranted.

> **Update (3 Aug 2026, round 2) — field-level model details now confirmed from the PHP SDK's GitHub
> docs.** The POK PHP SDK's per-model reference (`github.com/pokpay-ltd/php-sdk/tree/HEAD/docs/Model`,
> the `docs/Model/*.md` files) was read directly this round and resolves several field-name questions
> the official docs bundle left open: the full `CreateSdkOrderPayload` field list (including a
> **confirmed `expiresAfterMinutes`**, plus new `failRedirectUrl` and `merchantCustomReference`), the
> returned `SdkOrder` object (including an authoritative **`expiresAt`**), the `SdkOrderSplitWith`
> fields (**`merchantId` + `percentage`**, a pure percentage split), and the `Merchant` model
> (**`nuis` / `fieldOfOperation`**). The SDK is also confirmed **OpenAPI-Generator-generated**, so a
> real OpenAPI spec exists behind POK's API. These are folded in below tagged
> **[SDK-model-confirmed 3 Aug 2026]**, and they **resolve** several previously-open questions rather
> than merely narrowing them (see § Open questions). The one thing the model docs read did **not**
> show is the order **status enum** — a newly-isolated gap for PENA-205 (new Open Q14).

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

**[corrected 3 Aug 2026] How the hold/capture mapping works mechanically is now confirmed from the
official REST API page.** Order creation is `POST /merchants/{merchantId}/sdk-orders` and takes an
**`autoCapture` boolean**: `autoCapture:true` captures in one step; **`autoCapture:false` gives the
authorise-then-`capture` flow** that `IPaymentProvider` already assumes (this is the concrete
mechanism behind the hold/capture semantics, not an inference). Completion then happens through **one
of three distinct calls depending on the flow**, all confirmed in the official docs:

| Completion call | HTTP | When |
|---|---|---|
| `guest-confirm` | `POST /sdk-orders/{id}/guest-confirm` | Customer **not** authenticated on our system (the Flow-A deposit case) |
| `confirm` | `POST /sdk-orders/{id}/confirm` | Customer authenticated on our system |
| `capture` | `POST /merchants/{merchantId}/sdk-orders/{id}/capture` | Merchant-led / server-side capture of an authorised order |

For a cardholder-present deposit paid via POK's own checkout surface, `guest-confirm` (or the
server-led `capture` after an `autoCapture:false` authorise) is the finishing call; PENA-203 picks
the exact one against observed sandbox behaviour.

**[SDK-model-confirmed 3 Aug 2026] The full create-order payload is now known.** The PHP SDK's
`CreateSdkOrderPayload` model doc lists every field, superseding the truncated REST-API quickstart
example this plan was first written against:

- **Required:** `amount` (string), `currencyCode` (string, **defaults `"ALL"`** — matches this
  codebase's own `Payment.Currency` default already; no correction needed there).
- **Optional:** `autoCapture` (bool), `products` (`SdkOrderProduct[]`), `shippingCost` (float),
  `webhookUrl` (string), `redirectUrl` (string), **`failRedirectUrl` (string) — new, a distinct URL
  for failed/declined payments**, **`merchantCustomReference` (string) — new, the merchant's own
  reference id, stashed at order-creation time**, `deeplink` (string), `splitWith`
  (`SdkOrderSplitWith`), `description` (string), and **`expiresAfterMinutes` (int) — CONFIRMED to
  exist**.

Field-by-field against our state machine:

- **`autoCapture: false`** — authorise now, capture later. Maps exactly onto our documented
  `PaymentStatus.Captured` = "Card deposit authorised (held), not yet captured"
  (`Pena_e_Arte.Domain/Enums/PaymentStatus.cs:11`). **No new state machine needed.**
- **Hold self-expiry — `expiresAfterMinutes` CONFIRMED; question resolved. [SDK-model-confirmed
  3 Aug 2026]** The prior round's "narrowing" (that no expiry field was visible) is **withdrawn**:
  `expiresAfterMinutes` (int) is a real field on `CreateSdkOrderPayload` under **exactly the name the
  original assessment claimed** — it was simply absent from the *truncated* REST-API quickstart
  payload example, not absent from the API. This is what `Payment.HoldExpiresAt` (EPIC-0001) is for,
  and it drives the **HoldExpiresAt finding** in §1.6. **Better still, the returned `SdkOrder` carries
  its own authoritative `expiresAt` (DateTime)** (see the returned-object list below) — so we set the
  window via `expiresAfterMinutes` on create *and* read POK's computed `expiresAt` back rather than
  recomputing it. Open Q9 is now **resolved**.
- **`currencyCode: "ALL"`** native, with `originalCurrencyCode`/`appliedExchangeRate`/`finalAmount`
  FX fields returned. `Payment.Currency` (added in EPIC-0001, defaults `"ALL"`) exists for exactly
  this. No other reviewed provider bills in lek.
- **`splitWith: SdkOrderSplitWith` — full fields now known. [SDK-model-confirmed 3 Aug 2026]** The
  model has exactly two fields: **`merchantId` (string, optional)** and **`percentage` (float,
  optional)**. It is a **pure percentage split paid to a recipient identified by their own POK
  `merchantId`** — there is **no** currency field and **no** flat-amount field (and no
  `userPhoneNumber`; the earlier `{ merchantId | userPhoneNumber, amount }` framing was wrong). The
  fee is taken **atomically at payment time** and settled by POK directly to the recipient merchant's
  account, without the principal ever touching Pena e Artë — this is the field
  `Payment.PlatformFeeAmount` shadows. Build it now at a **0% `percentage`** per ADR-0001 monetization;
  retrofitting later is painful. Because the recipient is addressed by `merchantId`, the fee leg
  **requires Pena e Artë to hold its own POK merchant account** (Open Q7) even at 0%. This resolves
  the old "what are `SdkOrderSplitWith`'s fields" question (Open Q13) directly.
- **`merchantCustomReference`** — our idempotency handle and reconciliation anchor: set it to
  `Payment.Id` at order-creation time so a later webhook/poll can resolve order → `Payment` → studio.
  It is also returned on the `SdkOrder` (see below).
- **Assessment-named fields not seen in the SDK model docs read this round:** a `commissions`
  breakdown (`netAmount`/`totalCommissionAmount`/`grossAmount`), `selectedBranchId` (studio location),
  and `confirmUrl`/`confirmDeeplink` were named by the earlier assessment but do **not** appear in the
  `CreateSdkOrderPayload`/`SdkOrder` model docs — carry them as unverified until seen in the API
  reference.

**[SDK-model-confirmed 3 Aug 2026] The returned `SdkOrder` object fields are now known** (PHP SDK
`SdkOrder` model doc): `id`, `amount` (float), `capturedAmount` (float), `currencyCode` (defaults
`"ALL"`), `products` (optional), `shippingCost` (optional), `finalAmount` (float), `createdAt`
(DateTime), **`expiresAt` (DateTime) — the order's own authoritative computed hold-expiry timestamp**,
`redirectUrl` (optional), `failRedirectUrl` (optional), `merchantCustomReference` (optional),
`merchant` (`Merchant`, optional), and `self` (`SdkOrderSelf`, optional). **`expiresAt` is a concrete
implementation input for Finding C / PENA-203:** the reconciliation job should **read and store/compare
against POK's returned `expiresAt`** rather than computing its own expiry from
`createdAt + expiresAfterMinutes` — which avoids clock-skew and rounding disagreements between us and
POK. This is now a design decision, not just "confirm a field exists."

> **Order status enum still not seen — a specific remaining gap.** Neither the `SdkOrder` model doc nor
> `SdkOrderResponseData` (which merely wraps `SdkOrder`) exposed an order **status** field / enum
> (PENDING / AUTHORIZED / CAPTURED / REFUNDED / …) in what was read. PENA-205's status-mapping seam
> needs POK's exact status vocabulary — flagged as **new Open Q14**; the likely next place to look is
> `docs/Api/SdkOrdersApi.md` and `docs/Model/SdkOrderSelf.md` (not yet read), or live sandbox responses.

### 1.4 The checkout surface — this is where the frontend work lives

POK payment confirmation is **not** a Stripe-style client-secret-plus-Elements flow. **[corrected
3 Aug 2026]** the official docs reframe the options — there are effectively **three well-documented
web surfaces plus one to verify**:

1. **Embedded drop-in `GuestCheckoutForm`** (the React component; `PokPayment.renderForm` on the
   CDN) — **this is the best-fit default (see PENA-206).** It is **not** a redirect: it mounts
   **inline on our own page** (styled via CSS overrides scoped to `#pok-payment-container`, locale
   `en`/`it`/`al`, `countrySelect: 'dropdown' | 'modal'`), and **POK's own component — not our
   code — collects the raw card details, runs 3-D Secure, and captures the payment in a single
   flow.** Because POK's code owns card capture and 3DS, the PCI-SAQ-A / SCA-ownership argument
   (§2.5) still holds while the surface stays visually on-brand.
2. **Custom `encryptCard()` form** — a low-level escape hatch: we build the card inputs, call
   `encryptCard()` to produce a short-lived JWE, and hand it to our backend to tokenize/charge.
3. **Hosted redirect / app deep-link (to verify). ⚠️** The official create-order payload includes
   **`redirectUrl` and `deeplink`** fields, which *hint* at a hosted-redirect or POK-app deep-link
   completion flow (analogous to the `confirmUrl`/`confirmDeeplink` the earlier assessment described)
   — **but that full hosted flow is not documented in the doc set we have.** Worth checking
   `payments.doc.pokpay.io` directly (Open Q12) before finalising PENA-206.

**[corrected 3 Aug 2026] The old blanket claim that "the web SDK has no documented low-level 3DS
primitive" was wrong and is retracted.** The drop-in `GuestCheckoutForm` explicitly *"collects card
details, runs 3-D Secure, and captures the payment in a single flow,"* and the staging test cards are
specifically labelled for 3DS scenarios: `4000 0000 0000 1091` (Visa — 3DS challenge),
`4000 0000 0000 1026` (Visa — frictionless 3DS), `5200 0000 0000 1005` (Mastercard — 3DS challenge),
vs. the no-3DS `4242 4242 4242 4242`. The **only** accurate residue of the old claim: the *fully
custom* web path (option 2, `encryptCard()`) has no drop-in web 3DS-orchestration helper equivalent
to React Native's `createChallenge`, so choosing option 2 does mean owning more of the 3DS dance.
That is a reason to prefer option 1, not a gap in the web SDK overall. **This shapes PENA-206 and
Open Q6.**

### 1.5 What is absent (matters as much as what's present)

- **No webhook signature documented.** `webhookUrl` is per-order; the public docs describe no signing
  secret, no signature header, no replay protection. ⚠️ Ask POK whether one exists. Until confirmed,
  **treat every webhook as an untrusted ping and re-fetch `GET /sdk-orders/{id}`** (§PENA-205).
- **No merchant/sub-merchant onboarding API documented.** ⚠️ Studios are onboarded by POK out of
  band unless a partner programme exists (Open Q2). This decides whether PENA-204 is "self-serve
  connect" or "connect, then wait for POK KYC".
- **No .NET SDK** (PHP/JS/React Native/Flutter only) — **but the PHP SDK is confirmed
  OpenAPI-Generator-generated, so a real OpenAPI spec exists behind POK's API.** [SDK-model-confirmed
  3 Aug 2026] Rather than hand-write the client blind, **ask POK for that spec and generate the C#
  client** with OpenAPI Generator (Open Q10 / PENA-202); hand-write the thin `HttpClient` REST client
  only as a fallback if POK will not share it.
- **No recurring/MIT primitive in production** (only a **staging-only** MOTO endpoint). This is the
  Flow B question and is **out of scope for this epic** — Flow A deposits are cardholder-present.
- **No payouts/settlement API and no chargeback/dispute API documented** — so we cannot show a studio
  its payout schedule or handle disputes in-app for v2. Out of scope; note as a product gap.

### 1.6 How POK maps onto the `IPaymentProvider` interface EPIC-0001 shipped — and three findings

The interface (`Pena_e_Arte.Domain/Interfaces/IPaymentProvider.cs`) has five methods and a
`PaymentProviderCapabilities` record. POK fills the capability record cleanly:
`SupportsSplit: true` (`SdkOrderSplitWith` = `merchantId` + `percentage` — confirmed),
`SupportsAuthCapture: true` (`autoCapture:false`+capture — confirmed on the create-order payload),
`SupportsHoldExpiry:` **`true`** (confirmed: `expiresAfterMinutes` on create, authoritative `expiresAt`
returned on the order — Open Q9 now resolved; see Finding C / §1.3), `SupportedCurrencies:
["ALL", …]`.

But mapping the five *methods* onto POK surfaces **three real gaps that update what EPIC-0001
built**. These are the substance of PENA-201.

**Finding A — the interface has no per-tenant/studio context, but POK requires per-tenant
credentials. [more strongly confirmed 3 Aug 2026 — do NOT weaken.]** The methods take only primitives
(`CreatePaymentHoldAsync(amountInCents, currency, paymentId, ct)`; `GetStatusAsync(providerReferenceId,
ct)`; `CancelAsync(providerReferenceId, ct)`). That shape is a residue of the Stripe *aggregator*
model, where one platform-level key served every tenant. POK has **no platform key**: to call
`GET /merchants/{merchantId}/sdk-orders/{id}` you need *that studio's* `merchantId` +
`keyId`/`keySecret`. **The official docs make this harder evidence, not softer:**
- The REST API troubleshooting table states a `403` on `POST .../sdk-orders` means *"`merchantId` in
  the URL doesn't belong to the merchant tied to your `keyId`/`keySecret`"* — i.e. **`keyId`/`keySecret`
  are bound 1:1 to a specific `merchantId`.** There is no credential that spans merchants.
- Both the **WooCommerce** and **PrestaShop** plugin configuration screens require a **separate
  Key ID + Key Secret + Merchant ID per store install** — the official docs' own model of "one
  merchant = one credential triple" maps directly onto "one studio = one credential triple."
- Every official example (JS/React, PHP SDK, both plugins, REST) obtains `keyId`/`keySecret` "from
  the POK merchant dashboard" for a single merchant; none shows a platform/parent key.

This is concrete, cited justification that **PENA-201's per-tenant context change is necessary**, not
a hypothesis. The create path can resolve the studio from
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

**Finding C — `HoldExpiresAt` is double-enforced; POK is the authority. [SDK-model-confirmed
3 Aug 2026 — Open Q9 resolved.]** EPIC-0001 added a third `ReleaseExpiredHoldsAsync` pass to
`PaymentReconciliationJob` to auto-cancel holds past `HoldExpiresAt`. The earlier assessment held that
POK **already** expires the order server-side and was explicit: *"Let POK expire the hold; don't build
a competing Hangfire timer that can disagree with it."* **This is now confirmed, not a caveat:** the
PHP SDK's `CreateSdkOrderPayload` carries **`expiresAfterMinutes` (int)** and the returned `SdkOrder`
carries **`expiresAt` (DateTime)** — POK's own authoritative computed expiry timestamp. The prior
round's "narrowing" of this to an open question is **withdrawn**; the field exists under exactly the
name the original assessment claimed, and was only missing from the *truncated* REST quickstart
example. The design is therefore now concrete, not conditional:
  - **Set the hold window via `expiresAfterMinutes` on create**, and treat POK as the source of truth.
  - **Read POK's returned `expiresAt` back and store it into `Payment.HoldExpiresAt`** — mirror the
    value POK actually applied; do **not** compute our own expiry from
    `createdAt + expiresAfterMinutes` (avoids clock-skew / rounding disagreement with POK).
  - **Downgrade `ReleaseExpiredHoldsAsync` from enforcer to tolerant safety-net:** when it calls
    `CancelAsync` on a hold POK has already expired, a `409 Conflict`/already-cancelled is a
    **success**, not an error.
**This is a semantic update to code merged in EPIC-0001** and belongs in PENA-203/205.

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
POK, not Pena e Artë.** This is a direct argument for shipping a **POK-owned checkout surface** for the
pilot (Open Q6): it keeps SCA/3DS orchestration — and its regulatory weight — with POK, and keeps us
at PCI **SAQ-A** (card data never touches our infra or forms). **[refined 3 Aug 2026]** the official
docs confirm the ideal fit is POK's **embedded drop-in `GuestCheckoutForm`** — POK's own component
renders inline on our page and runs card capture + 3DS itself, so we keep the SCA/PCI posture *and*
stay on-brand (no redirect required). A custom `encryptCard()` web form would instead have us
orchestrating a flow subject to POK's SCA duty with no drop-in web 3DS helper — more risk, no
regulatory upside. **Recommendation: the embedded POK drop-in for the pilot.**

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

**[SDK-model-confirmed 3 Aug 2026] POK's own onboarding forces this question regardless.** The PHP
SDK's `Merchant` model (`id`, `name`, `address`, `verificationStatus`, `isVerified`) requires a
**`nuis` (string)** — Albania's business-registration number, *Numri i Identifikimit të Subjektit* —
and a declared **`fieldOfOperation` (`FieldOfOperation[]`, an array of business-activity categories)**.
So the activity-code / entity-type question the accountant must answer (currently framed above as a
domestic-tax-only concern) is something **POK's own merchant registration structurally requires**, not
just Albanian tax law: Pena e Artë (and every studio) must declare a `nuis` and a field-of-operation to
POK to onboard at all, and Pena e Artë's declared field-of-operation must be consistent with earning a
`splitWith` software fee. This raises the practical priority of Open Q8's accountant sub-question — it
is a precondition of getting a POK merchant account, not only a domestic-tax nicety.

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
- [x] ~~Day-one check: POK React SDK works on React 19~~ **[confirmed 3 Aug 2026 — no longer an open
      check].** The official React page states React 17+ is supported (`--legacy-peer-deps` needed
      only for React 17 or older) **and explicitly** *"This package supports React 19. Use it with
      `GuestCheckoutForm`, `AddCardForm`, and `usePOK` the same way as on React 18."* That matches
      this codebase's actual React 19. Note only: the published peer-dep range may still list React
      18, so `pnpm add @nebula-ltd/pok-payments-js` may warn — install path is documented, not a
      blocker.

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

**[SDK-model-confirmed 3 Aug 2026] Prefer generating the client over hand-writing it.** The POK PHP
SDK is confirmed **auto-generated by OpenAPI Generator** (`org.openapitools.codegen.languages.PhpClientCodegen`,
per its README plus the tell-tale `.openapi-generator-ignore` and `git_push.sh` in the repo) — which
means a **real OpenAPI spec exists behind POK's API** (the PHP, and presumably other, SDKs are
generated from it). So **before hand-writing the REST client, PENA-202 should ask POK directly for
that spec** (or inspect `payments.doc.pokpay.io` more thoroughly — it is a JS SPA, so read its network
requests, not a plain fetch that can't render it). A C# client generated via OpenAPI Generator from
POK's real spec would be **strictly better** than a blind hand-written one; fall back to hand-writing
only if POK will not share the spec. This resolves the "does an OpenAPI spec exist" open question in
principle (Open Q10) — the residual is only obtaining the file.

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
      `PokApiClient`. `Capabilities` = `SupportsSplit:true` (`SdkOrderSplitWith` = `merchantId` +
      `percentage`), `SupportsAuthCapture:true`, **`SupportsHoldExpiry:true`** (confirmed:
      `expiresAfterMinutes` on create, authoritative `expiresAt` returned — Open Q9 resolved),
      `SupportedCurrencies:["ALL", …observed]`.
- [ ] `CreatePaymentHoldAsync` creates a POK order with `autoCapture:false`, `currencyCode` from the
      payment (default `"ALL"`), **`expiresAfterMinutes`** to set the hold window (confirmed field),
      **`merchantCustomReference` = `Payment.Id`** — the reconciliation anchor, so a later webhook/poll
      resolves order → `Payment` → studio through it — `webhookUrl`/`redirectUrl`/**`failRedirectUrl`**
      to our endpoints, and `splitWith` present **but at a 0% `percentage`** to Pena e Artë's own
      `merchantId` (field wired end-to-end, value zero). Returns the `PaymentHoldResult` (POK order id
      as `ProviderReferenceId`, plus the confirm target for the chosen checkout surface — for the
      recommended embedded `GuestCheckoutForm` that is simply the `orderId`).
- [ ] `CaptureAsync`/`CancelAsync`/`RefundAsync`/`GetStatusAsync` map onto the POK endpoints;
      `GetStatusAsync` returns POK's real status strings and the **status-mapping seam** (PENA-201) is
      filled with observed POK values → `PaymentStatus` (unknown ⇒ never silently `Paid`).
- [ ] **`HoldExpiresAt` mirrors POK's `expiresAt` (Finding C, Open Q9 resolved):** read the returned
      `SdkOrder.expiresAt` and store it into `Payment.HoldExpiresAt` — do **not** compute expiry locally
      from `createdAt + expiresAfterMinutes` (avoids clock-skew/rounding). Make
      `PaymentReconciliationJob.ReleaseExpiredHoldsAsync` a **tolerant safety-net** — when it cancels a
      hold POK already expired, a `409`/already-cancelled is a success, not an error.
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
**Depends on:** PENA-203 · **Decision:** Open Q6 (embedded drop-in — recommended default — vs custom
`encryptCard()` vs a possible hosted redirect, Open Q12)

### Problem, verified

`frontend/src/features/payments/components/DepositCheckoutPage.tsx` is hardcoded to Stripe:
`loadStripe`, `@stripe/react-stripe-js` `Elements`/`PaymentElement`, `stripe.confirmPayment`,
`VITE_STRIPE_PUBLISHABLE_KEY`, and the footer literally says "Secured by Stripe." EPIC-0001 left this
in place because it can't function without a backend provider (completion summary Deviation #5). POK
does not use a Stripe client secret (Finding B). This page must be rewritten. `PaymentMethodSelector.tsx`
and 3 pre-existing Stripe `PaymentMethodSelector` tests (the known-failing ones in the EPIC-0001
self-check) are also in scope.

**[corrected 3 Aug 2026] The choice is not the binary "POK-hosted page vs custom `encryptCard()` form"
this plan originally framed.** The official docs surface a **third, better-fitting option** that is now
the recommended default: POK's **`GuestCheckoutForm` is an embedded drop-in React component, rendered
inline on our own page — not a redirect to a POK-hosted page.** POK's own component (not our code)
captures the raw card details and runs 3-D Secure, so the PCI-SAQ-A / SCA-ownership argument for
"let POK own card capture" (§2.5) still holds, **while the checkout stays visually on-brand inside our
`DepositCheckoutPage`.** That is strictly better than either the redirect-away option (off-brand) or
the custom `encryptCard()` form (we'd own 3DS orchestration on web). Recommend the **embedded drop-in
`GuestCheckoutForm`** as the PENA-206 default. (A genuine hosted-redirect flow may also exist via the
order payload's `redirectUrl`/`deeplink` fields — Open Q12 — but it is not needed if the embedded
form is used, and is not fully documented in the doc set we have.)

### Acceptance criteria

- [ ] `DepositCheckoutPage` no longer imports Stripe.js. Per the checkout decision (Open Q6):
      - **Embedded drop-in `GuestCheckoutForm` (recommended default):** mount POK's own
        `GuestCheckoutForm` React component (fed the SDK `orderId` from `PaymentHoldResult`) **inline**
        in `#pok-payment-container`, styled via scoped CSS overrides, `locale` set from the client's
        language, `countrySelect: 'modal'` on small screens. POK's component captures the card and runs
        3-D Secure in-flow (test cards `4000…1091`/`…1026`/`5200…1005`), then fires `onSuccess`/`onError`.
        Keeps SCA with POK and PCI SAQ-A (§2.5); stays on-brand. Accept and document the minor visual
        seam against shadcn/ui.
      - **Custom `encryptCard()` form (only if a fully bespoke UI is required):** we own the card inputs
        and must own web 3DS orchestration (no drop-in web helper) — more risk, no regulatory upside.
      - **Hosted redirect / app deep-link (only if Open Q12 confirms it exists):** consume a
        `redirectUrl`/`deeplink` from order creation, redirect/deep-link out, handle the return state.
        Off-brand; use only if the embedded form is unavailable.
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

Embedded, platform-branded checkout is the vertical-SaaS standard, and the embedded drop-in
`GuestCheckoutForm` delivers exactly that while keeping SCA/PCI with the licensed party (§2.5) — the
best of both. WCAG 2.1 AA on the checkout as on any page.

### Testing

- Component tests: the embedded `GuestCheckoutForm` mounts with the `orderId` from `PaymentHoldResult`,
  its `onSuccess`/`onError` drive the correct post-payment state, no PII leaks to console, capability-
  gated empty state renders for an unconnected studio, and no Stripe import remains. If a hosted-redirect
  path is chosen instead (Open Q12), test that it builds the correct redirect URL and handles the return.

### Help sync (DoD 7)

Update `client-deposit-pay` and the `user-manual` checkout section to describe the POK deposit flow
(the on-page POK card form, or — if chosen — an app deep-link/redirect return); update the client tour's
deposit step copy. Remove "Secured by Stripe" wording from Flow-A Help.

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
Re-annotated **3 Aug 2026** against `docs/payments/pok-official-docs-2026-08-03.md`, then again
(**round 2, 3 Aug 2026**) against the PHP SDK's GitHub model docs
(`github.com/pokpay-ltd/php-sdk/tree/HEAD/docs/Model`). Each item is tagged **[unchanged]**,
**[narrowed]** (was broader, now tighter), **[RESOLVED]** (now answered — no longer a founder
question), or **[new]**. Round 2 **resolves four** previously-open items (Q7 in part, Q9, Q10, Q13)
and **adds one** (Q14), so the open count is lower than the ~13 of the prior round.

1. **[unchanged] POK account & sandbox** — is a Pena e Artë POK merchant account open, with sandbox
   `keyId`/`keySecret` in hand? (Gates PENA-200.)
2. **[narrowed] Partner / delegated-onboarding programme** — can Pena e Artë provision studio
   merchants via API, or is every studio a manual POK KYC? *This decides whether PENA-204 is "connect
   and take deposits today" or "connect, then wait for POK."* Realistic time from studio signup to
   first deposit? **Narrowed by absence of evidence:** nothing in the official docs shows any
   merchant-provisioning / delegated-onboarding API — every example (JS/React SDK, PHP SDK,
   WooCommerce, PrestaShop, REST) assumes a `merchantId` + credential pair you *already hold*,
   obtained "from the POK merchant dashboard" ("E-payments dropdown → API Keys option"). This leans
   the answer toward **"no delegated onboarding; each studio needs its own manual POK KYC via its own
   dashboard"** — but that is an absence of evidence, not a confirmed negative; still worth asking
   POK directly.
3. **[unchanged] Webhook signing** — does a signature scheme exist (undocumented)? If yes, header +
   rotation. If no, we re-fetch as the only trust (PENA-205 assumes this). **Note:** the official
   create-order payload *does* take a **`webhookUrl`** field, so the webhook mechanism itself is
   confirmed to exist — but nothing in this doc set describes any signature/verification of the
   payload POK POSTs to it. Status therefore unchanged (still unconfirmed).
4. **[unchanged] Pricing** — MDR for ALL card and POK-app transactions, the `splitWith` leg fee, refund
   and chargeback costs, settlement timing. Needed to model the take-rate and to set the (currently
   0%) `PlatformFeeAmount` later.
5. **[unchanged] Pilot studio** — which named studio pilots first (Phase 2)? Their existing
   bank/processor also tells us whether the bank-VPOS off-ramp is theoretical or urgent.
6. **[narrowed] Checkout UI decision** — the official docs turn this from a two-way into a three-way
   with a clear default: **(a) embedded drop-in `GuestCheckoutForm`** — POK's own React component
   rendered *inline* on our page, running card capture + 3DS itself, SCA + PCI SAQ-A stay with POK,
   stays on-brand (**recommended default**); **(b) custom `encryptCard()` form** — bespoke UI, but we
   own web 3DS orchestration with no drop-in helper; **(c) hosted redirect/app deep-link** — only if
   Q12 confirms it exists. Confirm (a) is acceptable for the pilot. Shapes PENA-206.
7. **[RESOLVED in part] Pena e Artë's own POK merchant account** — **now on firm footing.**
   `SdkOrderSplitWith` = **`merchantId` + `percentage`** (confirmed, ex-Q13): the split pays a
   `percentage` to a recipient **identified by their own POK `merchantId`**, so the fee leg
   *structurally requires* Pena e Artë to hold its own POK merchant account to receive it — the
   "does the recipient need a separately-registered account" half of the old question is answered
   **yes, by `merchantId`**. The **only** residual is a founder *timing* decision: wire `splitWith`
   now at 0% `percentage` to Pena e Artë's `merchantId` (still model `PlatformFeeAmount`), or defer the
   wiring until a fee is actually charged. Not a design unknown any more.
8. **[unchanged, priority raised] Legal/accountant sign-off (cheap insurance):** (a) a payments lawyer
   confirming in writing that a non-custodial booking platform sits in Law 55/2020 Art. 4(g) and that a
   `splitWith` **software fee** does not recharacterise Pena e Artë as an intermediary; (b) the
   accountant on whether taking a share of transaction value needs an activity-code change /
   Person Fizik → SH.P.K. (`implementation-readiness.md` §2a). **Priority raised:** the PHP SDK's
   `Merchant` model requires a **`nuis`** and a declared **`fieldOfOperation`** to onboard at all
   (§2.7), so the activity-code/entity question is a **precondition of getting any POK merchant
   account**, not just a domestic-tax nicety. Still not a code blocker; a launch/GA blocker.
9. **[RESOLVED] Hold-expiry auto-cancel timer** — **answered by the PHP SDK model docs.**
   `CreateSdkOrderPayload` carries **`expiresAfterMinutes` (int)** and the returned `SdkOrder` carries
   **`expiresAt` (DateTime)** — POK's authoritative computed expiry. The prior round's "narrowing"
   (that no expiry field could be found) was an artefact of the *truncated* REST quickstart example,
   not a real absence, and is withdrawn. Design consequence (Finding C / PENA-203): set the window via
   `expiresAfterMinutes`, store POK's returned `expiresAt` into `Payment.HoldExpiresAt`, and make the
   reconciliation pass a tolerant safety-net. No longer a founder question — only confirm the **default
   window / whether it is configurable** against sandbox behaviour during PENA-203 (a build-time
   observation, not a blocker).
10. **[RESOLVED in principle] OpenAPI/Swagger** — **a real OpenAPI spec exists.** The PHP SDK is
    confirmed **auto-generated by OpenAPI Generator** (`org.openapitools.codegen.languages.PhpClientCodegen`,
    per its README + `.openapi-generator-ignore` + `git_push.sh`), which is only possible from an
    OpenAPI spec. The question shifts from "does a spec exist?" to the action **"ask POK for the spec
    file"** (or mine it from `payments.doc.pokpay.io`'s network requests — it is a JS SPA). If obtained,
    generate the C# client with OpenAPI Generator rather than hand-writing PENA-202's client (strictly
    better); hand-write only if POK will not share it.
11. **[narrowed] Assessment freshness** — the older `pok-assessment.md` (31 Jul 2026) self-flagged
    five claims. Two are now **resolved** by `pok-official-docs-2026-08-03.md`: *"web-SDK 3DS absent"*
    is **corrected** — wrong; the React `GuestCheckoutForm` runs 3DS in-flow (§1.4); and *"React 17+"*
    is **confirmed accurate** (React 19 explicitly supported, §PENA-200). Three sub-claims remain
    **unverified** because this newer doc set does not address them at all: *webhook signing absent*,
    *MOTO staging-only*, *no OpenAPI*. Also note the official REST API page carries
    `lastUpdated: 2026-04-10` (~4 months old): reliable, but do a quick re-check of `docs.pokpay.io` /
    `payments.doc.pokpay.io` before PENA-202 builds.
12. **[new] Hosted-redirect checkout flow** — the order-creation payload includes `redirectUrl` and
    `deeplink` fields, hinting at a hosted-redirect or POK-app deep-link completion flow as an
    alternative to the embedded `GuestCheckoutForm`. Does such a flow actually exist and is it
    documented at `payments.doc.pokpay.io`? Relevant to finalising PENA-206's design (and to Q6c).
13. **[RESOLVED] `SdkOrderSplitWith` fields & fee-recipient requirement** — **answered by the PHP SDK
    model doc.** `SdkOrderSplitWith` has exactly two fields: **`merchantId` (string, optional)** and
    **`percentage` (float, optional)**. It is a **pure percentage split to a recipient identified by
    their own POK `merchantId`** — no currency field, no flat-amount field, no `userPhoneNumber`. Both
    halves of the old question are answered: the fee is a `percentage`, and the recipient **must** be a
    POK merchant (addressed by `merchantId`) — which is what puts Q7 on firm footing (Pena e Artë needs
    its own POK merchant account for the fee leg).
14. **[new] Order status enum values (blocks PENA-205's status-mapping seam)** — the PHP SDK model
    docs read this round (`SdkOrder`, `SdkOrderResponseData`) show **no order-status field / enum**
    (PENDING / AUTHORIZED / CAPTURED / REFUNDED / …). PENA-203/205 need POK's exact status vocabulary to
    fill the provider-status → `PaymentStatus` mapping (replacing the Stripe `"succeeded"` string-match,
    §1.6). Where to look next: `docs/Api/SdkOrdersApi.md` and `docs/Model/SdkOrderSelf.md` (not yet
    read), or observe live sandbox responses during PENA-203. Until confirmed, the mapping seam must
    fail closed (unknown status ⇒ never silently `Paid`).

**Explicitly deferred / out of this epic (tracked so not silently dropped):**
- **Flow B (studio → Pena e Artë subscriptions):** POK has no production recurring/MIT primitive
  (only staging MOTO). Stays on Stripe Billing / an MoR (Polar per ADR-0001). Not this epic.
- **easyPos fiscalization:** the "missing second half" of Flow A (a settled-payment event triggers a
  DPT invoice). A **separate epic**; this epic only preserves the domain-event seam.
- **Bank-VPOS second `IPaymentProvider`:** the identified conversion off-ramp / EMI-loss fallback.
  Built only if a named studio is blocked on it (ADR-0001).
- **Chargeback/dispute and payout-schedule surfaces:** no documented POK API; product gap noted.
