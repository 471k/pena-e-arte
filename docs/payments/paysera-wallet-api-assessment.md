# Paysera Wallet API — assessment for Flow B, and how it compares

Prepared 31 July 2026. Companion to `legal-viable-payment-options.md`.
Not legal advice.

---

## 0. First: your premise needs one correction

You wrote that funds *will* land in an account controlled by Pena e Artë, and described the
case as *"the studio owner transfers money automatically from his bank account to the issuer
the moment he subscribes to a plan."*

In your role model `issuer` = you, the platform. So the money in question is **your own
subscription revenue**.

**Collecting money that is owed to you is not a payment service. Anywhere. Ever.**
Law 55/2020 regulates moving money *between other people*. A software vendor being paid by
its customer is a supplier being paid — there is no payer/payee pair for whom you are an
intermediary. No licence, no exemption needed, no Article 4(g) analysis.

So the Article 4(g) constraint from the previous memo is **unchanged and still intact**. It
binds Flow A only:

| | Money moves | Are you an intermediary? | Regulated? |
|---|---|---|---|
| **Flow A** | client → studio | Yes, if it routes through you | **Yes** — keep funds out of your accounts |
| **Flow B** | studio → you | No, you are the payee | **No** — take the money, it's yours |

The one thing that would break this: if a Flow B collection account ever also holds Flow A
funds in transit. Keep them in separate legal accounts and never commingle. That is a schema
and an ops rule, not a licensing problem.

**Bottom line: nothing about Flow B auto-collection requires you to change the Flow A design.**

---

## 1. Does the Wallet API solve the problem?

**No.** It is the wrong product, for one structural reason and several practical ones.

### The structural reason

From Paysera's own overview:

> The Paysera Wallet API provides comprehensive access to manage Paysera wallet accounts,
> user information, and **payments between Paysera accounts**.

and:

> **Payments** flow from payer's wallet to project's default wallet

The Wallet API moves money **from a Paysera wallet to a Paysera wallet**. It does not reach
into a studio owner's bank account at BKT or Credins. For a studio to pay you this way it
must:

1. open a Paysera account,
2. pass Paysera KYC,
3. **fund that wallet** from its bank, and keep it funded,
4. then grant you an allowance.

Step 3 is the killer. "Automatically from his bank account" becomes "manually top up a
second account every month, or your subscription lapses." That is worse than the invoice
you already have, and you have added a mandatory third-party signup to your activation
funnel — the single most expensive thing you can put in front of an SMB owner.

### The allowance primitive is genuinely good — on the wrong rail

Credit where due. `POST /rest/v1/allowance` is a clean pre-authorization object:

```json
{
  "description": "Monthly subscription",
  "currency": "EUR",
  "max_price": 5000,
  "valid": { "for": 2592000 }
}
```

The user consents once, then `POST /rest/v1/transaction` with `allowance_id` charges without
interaction. The transaction lifecycle is well designed —
`new → waiting → reserved → done`, with `auto_confirm`, `freeze_until` for
reserve-now-charge-later, and finalize-at-lower-amount. If you were building a closed-loop
wallet, this is exactly the shape you would want.

### Practical blockers, all from the docs

| Issue | Detail | Why it hurts |
|---|---|---|
| **No sandbox** | *"Wallet API operates in production environment only. All testing is done with real transactions, so plan carefully and use small amounts."* | You cannot write integration tests. Directly violates your rule "never skip a test for Application-layer business logic." Every CI run would move real money or be mocked-only. |
| **One active allowance per wallet** | *"Only one allowance can be active for a wallet at a time."* | No plan + add-on, no seat top-ups, no parallel charges. Any upsell means cancel + re-consent. |
| **Allowance expiry** | `valid.for` is a window; `max_price` is a hard cap; `new` allowances auto-delete after 1 month | Price increases and plan upgrades both force a fresh user consent. Renewal churn you have to build and monitor. |
| **MAC auth, not OAuth2** | Custom HMAC-SHA256 over a newline-joined normalized string with URL-encoded body hash | Bespoke handler, easy to get subtly wrong, no maintained .NET SDK (PHP/JS only). Compare Checkout Modern, which is plain OAuth2 Bearer. |
| **Contradictory rate limits** | API reference says 60/min per token and 1000/hr per account; the examples page says 100/min | Small thing, but it tells you how closely these docs are maintained. |
| **Access is gated** | Signed agreement required, credentials emailed after Paysera reviews your use case, volumes and timeline | Weeks of lead time before you can write a line of integration code. |

**Verdict: do not build Flow B on the Wallet API.**

---

## 2. The Paysera product you actually want is Recurring Billing

Paysera has a purpose-built subscription API and it is not the one you sent me.

**Recurring Billing API** — host `checkout-eu-a.paysera.com`, base path
`/checkout/rest/v1/`, classic gateway endpoint `https://www.paysera.com/pay/`.

The model is standard card-on-file MIT:

> Recurring billing requires a prior completed payment with user interaction to obtain a
> token. The first Payment Request must be completed with user interaction before subsequent
> automated charges can occur.

In Checkout Classic terms, the first redirect carries:

```php
'repeat'        => 1,       // enable recurring
'repeatrequest' => 1,       // request permission
'repeat_type'   => 'month', // day | week | month | year
'repeat_count'  => 0,       // 0 = unlimited
```

and every subsequent charge fires a callback to `callbackurl` carrying `repeat` and
`payment_number`. Notifications use a read-receipt model: receive `notification_id`, fetch
details, mark as read so it stops retrying — which is a genuinely good at-least-once design,
and maps cleanly onto Hangfire + an idempotency table.

**Setup path:** register → order the *"Online payment collection via e-banking and other
systems"* service → create a project (gives `project_id` + `project_password`) → add your
domain → verify domain ownership → integrate.

### Two caveats you must not miss

**a) It is card-based, not bank-account-based.** Paysera's own wording: *"charge subsequent
amounts from a user's credit card for monthly subscriptions."* This is a stored card token,
not a bank mandate. It gives you the *outcome* you described — money arrives automatically,
no owner action — but the rail is a card, so it inherits card economics: expiry, replacement,
issuer declines, involuntary churn, dunning.

If you specifically want **pull from a bank account**, the only real mechanisms are a
**direct debit mandate** (SEPA Direct Debit — confirm scheme adherence with Albanian banks;
geographical SEPA entry does not automatically mean SDD is live) or a licensed PISP with a
standing consent. Paysera's Open Banking PIS does **single payment initiation only** and
requires a **QWAC certificate** — i.e. you would have to be a licensed TPP yourself. Ruled
out.

**b) Albania gets Checkout Classic, not Checkout Modern.** Paysera's own regional-availability
notice:

> **Available now**: Lithuania, Latvia, Estonia … **Coming soon**: Additional markets

Checkout Modern is the good one — OAuth2 Bearer, real-time webhooks with HMAC signatures,
idempotent APIs, Google Pay / Apple Pay, sub-second responses. You do not get it yet. You get
Classic: signed-data redirect, `WebToPay` PHP-first helper, older callback semantics. Workable,
but you are writing the .NET client yourself.

---

## 3. Comparison

Scoring what matters for a solo founder selling €20–50/month to Albanian tattoo studios.

| | Paysera **Wallet** | Paysera **Recurring Billing** (Classic) | **Paddle** / **Lemon Squeezy** (MoR) | **Albanian bank VPOS** recurring | **SEPA Direct Debit** | **Invoice + transfer** |
|---|---|---|---|---|---|---|
| Truly hands-off after signup | ✅ | ✅ | ✅ | ⚠️ if MIT supported | ✅ | ❌ |
| Pulls from a **bank account** | ❌ (wallet balance) | ❌ (card) | ❌ (card) | ❌ (card) | ✅ | ❌ |
| Studio needs a new account | ❌ **Paysera wallet required** | ✅ no | ✅ no | ✅ no | ✅ no | ✅ no |
| Sandbox / testable in CI | ❌ **production only** | ⚠️ `test=1` flag | ✅ full sandbox | ⚠️ varies | ⚠️ varies | ✅ n/a |
| DX / .NET fit | ❌ MAC, no .NET SDK | ⚠️ Classic, PHP-first | ✅ modern REST + webhooks | ❌ bespoke per bank | ⚠️ file-based | ✅ trivial |
| Handles VAT/invoicing for you | ❌ | ❌ | ✅ **MoR is seller of record** | ❌ | ❌ | ❌ |
| Dunning / retries built in | ❌ | ⚠️ basic | ✅ strong (Paddle Retain) | ❌ | ⚠️ | ❌ |
| Cost | low | ~1–2% | ~5% + $0.50 | ~1.5–3.5% | very low | ~0 |
| Charges in ALL | ⚠️ confirm | ⚠️ confirm | ⚠️ likely EUR only | ✅ | ❌ EUR | ✅ |
| Time to first payment | months (agreement) | weeks | days–weeks (KYC) | 2–4 weeks | months | today |
| Albania availability | ✅ EMI licensed 2021 | ✅ Classic | ✅ LS lists Albania; Paddle doesn't exclude it | ✅ | ❓ verify SDD | ✅ |

### Reading the table

- **Wallet loses on two independent grounds** — it needs your customer to open and fund a
  Paysera wallet, and it has no test environment. Either alone would be disqualifying.
- **Nothing except SEPA Direct Debit actually pulls from a bank account.** If that phrasing in
  your requirement is literal, SDD is the only answer and you need to confirm with an Albanian
  bank whether SDD Core/B2B is live post-SEPA-accession. If the requirement is really "the
  owner sets it up once and never thinks about it again," card-on-file satisfies it.
- **MoR's ~5% buys something real.** It is not just card acceptance — it is being the legal
  seller, which means Paddle/Lemon Squeezy issue the studio's invoice and handle the tax
  position, and you issue one export-of-services invoice a month to a foreign company. On a
  €30 plan the premium over Paysera is roughly €1 per studio per month. Against that: no VAT
  logic, no dunning engine, no PCI scope, no chargeback handling, and a much smaller
  fiscalization surface for your vendor adapter.
- **Paysera's real edge is ALL and local trust**, not the API. An Albanian studio owner
  recognises Paysera; it is a BoA-licensed EMI and won "Fintech of the Year" locally. If
  billing in lek turns out to matter for conversion — and with €20–50 price points it might —
  Paysera or a domestic VPOS is your only route, because the MoRs will bill in EUR.

---

## 4. Recommendation

**Ship now — B2, invoice + bank transfer.** Zero integration, works today, and Albanian SMEs
already pay suppliers this way. Build the reconciliation job (statement import + reference
matching); you need it regardless.

**Then — Paddle or Lemon Squeezy as MoR.** Best DX, real sandbox, dunning included, and it
collapses your invoicing surface to one counterparty. Start KYC early; Albanian Person Fizik
onboarding will take longer than the docs suggest. Lemon Squeezy names Albania explicitly for
bank payouts; Paddle's exclusion list is sanctions-only and Albania is not on it.

**Keep Paysera Recurring Billing as the pricing-sensitive alternative**, especially if you
end up needing to bill in ALL or if 5% MoR fees hurt at scale. Not Wallet — Recurring Billing.
Revisit when Checkout Modern reaches Albania; at that point Paysera becomes genuinely
competitive on DX too.

**Do not build on Wallet API.** Wrong rail, mandatory customer signup, no sandbox.

**Watch SEPA Direct Debit.** If Albanian banks adhere to SDD, it beats everything above for
B2B subscription collection: pull-based, near-zero cost, no card expiry. Ask your bank
directly — this is the single highest-value open question in this memo.

---

## 5. Build implications

1. **Flow B is a separate provider abstraction from Flow A.** Do not reuse `IPaymentProvider`.
   Subscription billing has different concerns (plans, proration, dunning, mandates) than a
   one-off client deposit. `ISubscriptionBillingProvider` in `Pena_e_Arte.Domain`.
2. **Model the mandate, not the charge.** Whatever the rail, persist a
   `BillingMandate { TenantId, Provider, ExternalRef, Status, ValidUntil, MaxAmount }`. Paysera
   allowances expire and cap; card tokens expire; SDD mandates lapse after 36 months of
   inactivity. All four need the same "is this still chargeable?" check before a cycle runs.
3. **Flow B funds go to a legally separate account from anything Flow A touches.** Add this as
   an explicit note in `docs/claude/architecture.md` so it is not lost.
4. **Never store card data.** Every option above is redirect-or-token; PCI scope stays SAQ-A.
   No card fields in any Pena e Artë form, ever.
5. **Dunning is a product feature, not a cron job.** Failed charge → grace period → in-app
   banner → email → downgrade to read-only, never hard delete. Tattoo studios lose card
   access at exactly the wrong moment and you do not want to lock a studio out of its own
   appointment book over a €30 decline.
6. **Help must cover it** — `helpContent.ts`, the standalone manual, and the billing step of
   the owner onboarding tour, in the same change.

---

## Sources

- [Paysera — Wallet API Reference](https://developers.paysera.com/api/wallet)
- [Paysera — Wallet API Overview](https://developers.paysera.com/guides/wallet)
- [Paysera — Create allowance](https://developers.paysera.com/api/wallet/create-allowance)
- [Paysera — Payments Overview (transaction lifecycle)](https://developers.paysera.com/guides/wallet/payments)
- [Paysera — Obtaining API Credentials (production-only, agreement required)](https://developers.paysera.com/guides/wallet/getting-started/obtaining-credentials)
- [Paysera — Wallet API examples](https://developers.paysera.com/guides/wallet/examples)
- [Paysera — Recurring Billing API Overview](https://developers.paysera.com/guides/recurring-billing)
- [Paysera — Manage Recurring Billing (setup steps, repeat parameters)](https://developers.paysera.com/manage-recurring-billing)
- [Paysera — Checkout Modern (regional availability: LT/LV/EE only)](https://developers.paysera.com/guides/checkout-modern)
- [Paysera — Use Open Banking (PIS: single payments, QWAC required)](https://developers.paysera.com/use-open-banking)
- [Paysera — EMI licence issued by Bank of Albania](https://www.paysera.com/v2/en/blog/paysera-albania-emi)
- [Law No. 55/2020 "On Payment Services" (English)](https://www.bankofalbania.org/rc/doc/Ligji_Per_sherbimet_e_pagesave_anglisht_18199.pdf)
- [Lemon Squeezy — Supported Countries](https://docs.lemonsqueezy.com/help/getting-started/supported-countries)
- [Paddle — Which countries are supported by Paddle?](https://www.paddle.com/help/start/intro-to-paddle/which-countries-are-supported-by-paddle)
