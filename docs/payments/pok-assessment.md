# POK Payments — full assessment

Prepared 31 July 2026. Third in the series with `legal-viable-payment-options.md` and
`paysera-wallet-api-assessment.md`. Not legal advice.

Docs reviewed in full: [docs.pokpay.io](https://docs.pokpay.io/) (index),
[/react](https://docs.pokpay.io/react/), [/cdn](https://docs.pokpay.io/cdn/),
[/rest-api](https://docs.pokpay.io/rest-api), plus the authoritative Postman reference at
[payments.doc.pokpay.io](https://payments.doc.pokpay.io/) — every folder expanded and the
`Create an Order` schema read field by field. Also the vanilla-JS, React Native, Flutter,
PHP SDK, WooCommerce and PrestaShop pages via `llms-full.txt`.

---

## Verdict up front

**Flow A (client → studio): yes. This is the best option found so far — better than
per-tenant bank VPOS and better than Paysera.**

**Flow B (studio → you): probably, but it hinges on one unanswered question — whether POK
supports unattended merchant-initiated charges in production. The published API does not
document one.**

The thing that makes POK different from everything else reviewed: **`splitWith` is a
first-class field on order creation.** POK is shaped like a marketplace platform, not just a
gateway. Nothing else in the Albanian market is.

---

## 1. Who POK actually is — and why that matters legally

POK is the trading name of **RPAY SH.P.K.**, a Tirana fintech founded in 2021, **licensed by
the Bank of Albania as an electronic money institution**, live since September 2021. The PHP
SDK package name (`rpay/pokpay-payments-sdk`) matches. The JS packages ship under
`@nebula-ltd/*`, which appears to be the development entity.

This is the whole ballgame for the Article 4(g) question. POK is the licensed party. Money
moves POK → studio. Pena e Artë creates orders and reads statuses over HTTPS and **never
comes into possession of funds** — the exact wording of the technical-service-provider
exclusion in Law 55/2020.

> **Verify:** confirm RPAY SH.P.K. on the Bank of Albania's official EMI register
> (`bankofalbania.org` → Supervision → Licensed institutions → Electronic Money
> Institutions → *List of electronic money institutions*, .xlsx). Do not take a third-party
> directory's word for a licence. This is a five-minute check and it underwrites your entire
> compliance posture.

---

## 2. The complete API surface

Fourteen endpoints, four folders. This is the whole thing — worth knowing precisely, because
what's *absent* matters as much as what's present.

**Authentication API**

| | |
|---|---|
| `POST /auth/sdk/login` | `keyId` + `keySecret` → JWT. Returns `accessToken`, `expiresIn`, `expiresAt`. |

**Merchant Orders API**

| | |
|---|---|
| `POST /merchants/{merchantId}/sdk-orders` | Create an order. The important one — see below. |
| `POST /merchants/{merchantId}/sdk-orders/{id}/capture` | Capture an authorized order. |
| `POST` … `/refund` | **Refund the payment for an order.** |
| `POST` … `/cancel` | **Cancel the payment for an order.** |
| `GET` … | Retrieve an order (detailed). |
| `POST` … MOTO | **[Staging environment only] Perform MOTO transaction.** |

**Orders Retrieval API** — `GET /sdk-orders/{id}`

**Card Tokenization API**

| | |
|---|---|
| `GET` get flex card encryption key (+ a deprecated variant still listed) |
| `POST` tokenize card · `POST` setup tokenized card 3ds · `POST` check 3ds enrollment |
| `POST` guest confirm · `GET` get guest cards information |

### What's present that I did not expect

- **Refund and cancel.** Real, first-class. Critical for a deposit workflow where clients
  cancel appointments.
- **`autoCapture: false`** — authorize now, capture later. Maps exactly onto "hold the
  deposit at booking, capture when the artist confirms the slot."
- **`expiresAfterMinutes`** — the order self-expires and can no longer be paid. A booking
  hold with a built-in TTL, enforced server-side by POK rather than by your Hangfire job.
- **Native ALL**, with `originalCurrencyCode`, `currencyCode`, `originalAmount`,
  `appliedExchangeRate` and `finalAmount` all returned. The example payload is literally
  `"currencyCode": "ALL"`. Neither Paddle nor Lemon Squeezy will do this.
- **`commissions` breakdown** — `netAmount`, `totalCommissionAmount`, `grossAmount` returned
  at creation, so you can show a studio its fee before it accepts.
- **`selectedBranchId`** — POK already models merchants as having branches. Maps directly
  onto a studio with multiple locations.
- **`confirmUrl` and `confirmDeeplink`** — you can either redirect to a hosted confirm page
  or deeplink straight into the POK mobile app. In a market where cash dominates and card
  penetration is low, "pay with the app you already have" is a real conversion lever.
- **Prefill query params on `confirmUrl`** — `firstName`, `email`, `country`, `city`,
  `language=AL|EN|IT`. Cheap UX win.
- **A real staging environment** — `api-staging.pokpay.io`, explicitly non-billing, with
  published test cards including 3DS-challenge and frictionless-3DS variants. Paysera Wallet
  has none. You can actually put this in CI.

### What's absent

- **No subscription or recurring-billing endpoint of any kind.**
- **No merchant/sub-merchant onboarding API.** Studios are onboarded by POK, out of band.
- **No payouts or settlement API.** You can't show a studio its payout schedule in-app.
- **No account-level webhook configuration, and no documented webhook signature.**
- **No .NET SDK.** PHP, JS/React, React Native, Flutter. You're on ASP.NET Core 10.

---

## 3. Flow A — client pays the studio

### The architecture POK enables

```
Pena e Artë  ──POST /merchants/{studioMerchantId}/sdk-orders──▶  POK
                { amount, currencyCode: "ALL", autoCapture: false,
                  splitWith: { merchantId: <penaEArteMerchantId>, amount: <fee> },
                  webhookUrl, redirectUrl, expiresAfterMinutes: 1440,
                  merchantCustomReference: <appointmentId> }

Client  ──confirmUrl or confirmDeeplink──▶  POK checkout / POK app  ──▶  studio's POK account
                                                                    └──▶  your fee
```

You orchestrate. POK settles. You never hold funds. **Article 4(g) intact.**

`splitWith` is what makes this genuinely better than per-tenant bank VPOS: you can take a
platform fee **at the moment of payment**, atomically, without ever touching the principal.
With bank VPOS you'd have to invoice the studio separately for your cut and chase it.

Note `splitWith.userPhoneNumber` as an alternative to `merchantId` — split to an
individual's POK account by `+355…` number. That is an artist-commission-split primitive
sitting right there in the API, which is exactly the "session splits" feature in your
CLAUDE.md scope. Worth exploring seriously.

### Credentials model — the operational catch

The PHP SDK docs list `403 Forbidden` on `createOrder` as: *"Your `keyId` / `keySecret` is
for a different merchant than the `merchantId` in the URL."*

So **there is no platform-level key that can create orders for any tenant.** Each studio
opens its own POK merchant account, generates its own `keyId`/`keySecret`, and hands them to
you. You store them per-tenant in Vault.

Two readings of this:

- **Bad:** onboarding friction, and a credential-handling burden. Every studio must go
  through POK KYC before it can take a deposit.
- **Good, and I think this dominates:** it is the cleanest possible Article 4(g) posture.
  Every order is created *under the studio's own credentials*, against the studio's own
  merchant account, settling to the studio's own balance. There is no reading under which
  you are the payee. And it is still **one API integration** instead of one per bank.

> **Verify with POK:** whether they offer a platform/partner programme with delegated
> onboarding — i.e. can Pena e Artë refer studios and provision merchants via API, or is it
> always a manual POK sales process? This single answer determines whether your activation
> funnel is "sign up and take deposits today" or "sign up, then wait two weeks for POK."

### Where it falls short for Flow A

- **Only POK-app users and cardholders can pay.** Cash-paying clients still need the
  record-only path from the first memo. Keep it.
- **Card brands: Visa, Visa Electron, Mastercard, Maestro.** Fine for Albania.
- **Checkout UI.** `GuestCheckoutForm` mounts POK's own form; you style it via CSS overrides
  scoped to `#pok-payment-container`. Against a shadcn/ui + Tailwind design system that will
  read as a visual seam. The escape hatch is `encryptCard()` + your own form — but note that
  React Native and Flutter get a documented low-level 3DS primitive (`createChallenge`,
  device-data collection, step-up modal) and **the web SDK does not**. Custom web checkout UI
  means orchestrating 3DS step-up yourself against `check-3ds-enrollment` /
  `setup-tokenized-3ds` with no documented helper. Budget for it or accept POK's form.
- **React 19.** Docs say React 17+, with `--legacy-peer-deps` only needed on 17 *or older*.
  You're on 19. Almost certainly fine — verify on day one, it's a 30-second check.

---

## 4. Flow B — studio pays you

You would be a POK merchant; studios pay you by card or from the POK app. Legally trivial —
you're the payee collecting your own revenue.

**The problem: there is no recurring-billing primitive.**

You can save a card (`AddCardForm` → `tokenize card`) and charge it later
(`setup-tokenized-3ds` → `payByToken`). But every documented pay-by-token flow runs a
3-D Secure step — `payerAuthentication`, device-data collection, and a challenge modal the
cardholder may have to complete. **That is a cardholder-present flow.** Nothing in the
published docs describes an unattended merchant-initiated transaction.

The one hint is `[Staging environment only] Perform MOTO transaction`. MOTO is the
cardholder-not-present rail — which is what unattended recurring runs on. But the published
collection marks it **staging only**, which strongly suggests production MOTO is gated,
restricted, or not generally available.

> **This is the single question that decides whether POK can be your Flow B:**
> *"Can a merchant charge a stored card token in production without cardholder interaction —
> MIT / MOTO / recurring? If yes, what's the enrolment process and what SCA exemption applies
> under the BoA authentication regulation?"*

**If yes:** POK does both flows, in ALL, with one vendor, one integration, one reconciliation
model, and a real sandbox. That is a materially better outcome than anything in the previous
two memos.

**If no:** Flow B via POK degrades to *push a payment request to the owner's phone each
month* — create an order, send the `confirmDeeplink`, owner taps confirm in the POK app.
Honestly that is not terrible; it is one tap, in an app they already have, with no card
expiry and no dunning. But it is not "automatic," and it will produce involuntary churn from
owners who ignore the notification. In that case keep invoice+transfer as the default and an
MoR for anyone who wants true set-and-forget.

Either way, **POK is an acquirer, not a billing engine.** Plans, proration, upgrades, grace
periods, dunning and invoicing are all yours to build. Paddle and Lemon Squeezy hand you all
of that plus merchant-of-record tax handling for ~5%.

---

## 5. Security and reliability notes worth acting on

**Webhooks are per-order and unsigned (as documented).** `webhookUrl` is a field on order
creation. The public docs describe no signing secret, no signature header, no replay
protection. Treat every webhook as an **untrusted ping**, never as a source of truth:

```
webhook received → ignore the body → GET /sdk-orders/{id} with your bearer token
                 → trust only that response → transition the appointment state
```

Ask POK whether a signature scheme exists and isn't documented. If it does, verify it *and*
still re-fetch.

**Token refresh is manual and the docs contradict themselves** — the REST/PHP docs say
`expiresIn: 3600`, the Postman example response says `"expiresIn": "3600000"` (milliseconds,
as a string). Use `expiresAt` (an ISO timestamp) and ignore `expiresIn` entirely.

**Idempotency.** `merchantCustomReference` is explicitly recommended as a unique per-order
value. Set it to your appointment/invoice ID and use it as the idempotency key — the API has
no `Idempotency-Key` header.

**`409 Conflict` on capture means already-captured.** The docs say plainly: don't retry
blindly, fetch the order and inspect status. Your Hangfire retry policy must special-case it.

**Never store the JWE.** `encryptCard()` returns a single-use, short-lived JWE. Exchange it
server-side immediately via `tokenize card`. This keeps you at PCI SAQ-A.

---

## 6. Comparison

| | **POK** | **Paysera Recurring Billing** | **Paysera Wallet** | **Paddle / Lemon Squeezy** | **Bank VPOS (per studio)** |
|---|---|---|---|---|---|
| **Flow A — split to studio + fee** | ✅ `splitWith`, native | ❌ | ❌ | ❌ not for third-party sales | ⚠️ no split; invoice your fee separately |
| **Flow B — unattended recurring** | ❓ **must confirm** | ✅ card token | ✅ allowance (wallet only) | ✅ best in class | ⚠️ if MIT supported |
| Charges in **ALL** | ✅ native, with FX fields | ⚠️ confirm | ⚠️ confirm | ❌ EUR | ✅ |
| Customer needs a new account | ❌ no (card) / ✅ has POK app | ❌ no | ✅ **Paysera wallet required** | ❌ no | ❌ no |
| Real sandbox | ✅ `api-staging`, test cards | ⚠️ `test=1` | ❌ **production only** | ✅ | ⚠️ varies |
| Auth model | ✅ JWT Bearer | ⚠️ MAC / signed redirect | ⚠️ MAC | ✅ OAuth2 / Bearer | ❌ bespoke per bank |
| .NET client | ❌ write it (plain REST) | ❌ write it (MAC) | ❌ write it (MAC) | ✅ / easy | ❌ per bank |
| Refunds / cancel / auth-then-capture | ✅ all three | ⚠️ partial | ⚠️ | ✅ | ⚠️ varies |
| Webhook signing documented | ❌ | ⚠️ signed callback | ⚠️ | ✅ HMAC | ⚠️ |
| Handles VAT / is seller of record | ❌ | ❌ | ❌ | ✅ | ❌ |
| Dunning / subscription engine | ❌ | ⚠️ basic | ⚠️ | ✅ | ❌ |
| Local trust / distribution | ✅ **BoA-licensed EMI, consumer app, deeplink** | ✅ BoA-licensed EMI | ✅ | ❌ unknown brand locally | ✅ |
| Integrations to maintain | **1** | 1 | 1 | 1 | **N (one per bank)** |

---

## 7. Recommendation

**Adopt POK as the Flow A provider.** It beats per-tenant bank VPOS on every axis that
matters: one integration instead of N, native ALL, atomic platform fee via `splitWith`,
authorize-then-capture with server-side expiry, refunds and cancels, a real sandbox, and a
licensed EMI carrying the regulatory weight while you stay squarely inside Article 4(g).
`splitWith.userPhoneNumber` may also solve artist session splits, which is on your roadmap
anyway.

**Keep the record-only cash path.** POK does not change the fact that most Albanian tattoo
deposits will be cash in year one.

**Do not commit Flow B to POK until they answer the MIT question in writing.** Until then:
invoice + bank transfer as default, MoR (Paddle / Lemon Squeezy) for self-serve card
subscriptions. If POK confirms production unattended charging, consolidate onto POK and drop
the MoR — one vendor, ALL-denominated, single reconciliation model, and your invoicing
adapter has one counterparty shape instead of two.

### Questions to send POK, in priority order

1. **Can a merchant charge a stored card token in production with no cardholder interaction
   (MIT / MOTO / recurring)?** If yes: enrolment process, and which SCA exemption applies
   under the BoA authentication regulation.
2. Is there a **platform / partner programme** — can Pena e Artë provision studio merchants
   via API, or is onboarding always a manual POK process? What is the realistic time from
   studio signup to first deposit taken?
3. **Are webhooks signed?** If so, what is the header and secret-rotation process? If not,
   is it on the roadmap?
4. **Pricing:** merchant discount rate for ALL card transactions, POK-app transactions, the
   `splitWith` leg, refunds, and chargebacks. Settlement timing.
5. Any **chargeback/dispute API**, or is that dashboard-only?
6. Is `splitWith.userPhoneNumber` suitable for **recurring artist commission splits**, or is
   it intended for one-off peer splits?
7. Is there an **OpenAPI/Swagger spec**? A generated C# client beats a hand-written one.

---

## 8. Build implications

1. **`PokPaymentProvider : IPaymentProvider`** in `Pena_e_Arte.Infrastructure`. Plain
   `HttpClient` + JWT Bearer — no exotic auth. Cache the token against `expiresAt`, refresh
   with a safety margin, and never let two requests race the refresh.
2. **Per-tenant `keyId`/`keySecret` in Vault**, resolved by tenant at request time. Never in
   the DB, never in config, never logged. This is CLAUDE.md rule 4 and it is also what keeps
   you inside Article 4(g).
3. **Webhook endpoint is a trigger, not a source of truth.** Always re-fetch
   `GET /sdk-orders/{id}`. Write the architecture test that fails if any handler reads the
   webhook body to decide payment state.
4. **`merchantCustomReference` = your appointment ID.** It is your only idempotency handle.
5. **Map the deposit lifecycle onto POK's, don't invent a parallel one.** `autoCapture:false`
   + `expiresAfterMinutes` ≈ your "pending confirmation" state. Let POK expire the hold;
   don't build a competing Hangfire timer that can disagree with it.
6. **Model `splitWith` in the domain now**, even if v2 ships with a zero fee. Retrofitting a
   money split into an existing payment aggregate is painful.
7. **`selectedBranchId` maps to studio location.** Carry it through so multi-location studios
   reconcile per branch.
8. **Decide the checkout-UI question early** — POK's `GuestCheckoutForm` (fast, visual seam)
   vs `encryptCard()` + your own form (on-brand, but you own 3DS orchestration on web with no
   documented helper). This decision shapes the whole booking-payment screen; make it before
   you build it, not after.
9. **Help stays in sync** — `helpContent.ts`, the standalone manual, and the studio
   onboarding tour all need a "connecting your POK account" step, in the same change.

---

## Sources

- [POK Payments — Documentation index](https://docs.pokpay.io/)
- [POK — React SDK](https://docs.pokpay.io/react/)
- [POK — CDN integration](https://docs.pokpay.io/cdn/)
- [POK — REST API overview](https://docs.pokpay.io/rest-api)
- [POK Payments API — full Postman reference](https://payments.doc.pokpay.io/)
- [POK — concatenated docs for AI (`llms-full.txt`)](https://docs.pokpay.io/llms-full.txt)
- [pokpay-ltd/php-sdk on GitHub](https://github.com/pokpay-ltd/php-sdk)
- [RPAY SH.P.K. (Albania) — EMI profile](https://thebanks.eu/emis/rpay-355514) — third-party directory; verify against the BoA register
- [Bank of Albania — Electronic money institutions register](https://www.bankofalbania.org/Supervision/Licensed_institutions/Electronic_Money_Institutions/)
- [POK PAY — LinkedIn](https://al.linkedin.com/company/pok-pay) · [pokpay.io news](https://pokpay.io/en/news)
- [Law No. 55/2020 "On Payment Services" (English)](https://www.bankofalbania.org/rc/doc/Ligji_Per_sherbimet_e_pagesave_anglisht_18199.pdf) — Article 4(g)
