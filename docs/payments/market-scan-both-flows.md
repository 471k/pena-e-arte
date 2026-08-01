# Albanian payments market scan — every viable option for both flows

Prepared 31 July 2026. Supersedes and consolidates the earlier memos in this folder.
Fresh research, wider scope, nothing assumed from the previous rounds.
Not legal, tax or financial advice.

---

## 0. The two findings that changed my view this round

**1. Albanian bank VPOS is more capable than anyone says it is.** BKT's virtual POS runs on
**Merchant Safe Unipay (MSU)** by **Payten / Asseco SEE** — BKT's own page refers to "merchant
safe" by name. Reading the MSU API v2 reference directly, the platform ships **Recurring
Plans** (add/edit/delete, recurring plan cards, recurring payments), **Split Payment**, stored
cards, Pay-By-Link, sessions, PREAUTH/SALE, installments, and invoices. Everyone — including
my earlier memo — treats Albanian bank VPOS as a dumb one-shot redirect. The *platform* is
not. Whether a given bank has switched those modules on for merchants is a commercial
question, and it is worth one phone call.

**2. Four merchant-of-record platforms name Albania explicitly, in their own docs.** Not "we
support most countries" — Albania, listed. Polar.sh has it first on the list. Creem has it
first alphabetically with a green check. Lemon Squeezy lists it under bank payouts. Paddle's
unsupported list is sanctions-only and Albania isn't on it. Flow B is not the constrained
side of this problem; it has more good answers than Flow A does.

---

## 1. Scope of this scan, and what I ruled out with evidence

Searched: the Bank of Albania registers and press releases, all 11 Albanian commercial banks,
regional payment processors serving the Balkans, EU EMIs with Albanian licences, every
merchant-of-record platform I could find with a published country list, and the open-banking
/ instant-payments roadmap.

**Ruled out, with the reason:**

| Ruled out | Why |
|---|---|
| Stripe, Adyen, Checkout.com, Mollie | Your standing decision. Not revisited. |
| **Viva.com / Viva Wallet** | Worth checking because it's an EU-licensed neobank expanding through SEE. Its own support docs put the Viva.com payment method in **Greece, Malta and Cyprus**. No Albanian merchant onboarding found. |
| **MoRs for Flow A** | Structurally wrong, not just unsupported. A merchant of record becomes the legal *seller*. Paddle/Polar/Creem/Lemon Squeezy sell software and digital goods — they cannot be the seller of a tattoo session. Flow A is a physical service sold by the studio. |
| **PayPal** | Albania is a receive-limited market historically and PayPal is not a serious card-acquiring option for an Albanian SMB. Useful only as a secondary payout rail for Flow B. |
| **Crypto rails** | No. Regulatory exposure with zero upside for a tattoo studio's deposit. |

**Regulatory backdrop that shapes everything below:** as of April 2026 there are **10
BoA-licensed electronic money institutions** in Albania, down from 11 after Soft & Solution
("ALPay") had its licence revoked because its shareholder was arrested. That is a real risk
signal for this market — EMI concentration risk is not theoretical here. Whichever provider
you pick, keep a second one integrable behind the abstraction.

---

## 2. Market map

Three layers, and you need something from each:

```
 MONEY IN (Flow A)          MONEY IN (Flow B)              FISCAL
 client → studio            studio → you                   invoice → DPT
 ─────────────────          ────────────────────           ──────────────
 POK                        Polar / Creem / Paddle / LS    easyPos (ESDP)
 Bank VPOS (MSU)            POK · Paysera · Bank VPOS      + other certified
 Paysera Albania            Invoice + SEPA transfer          vendors
 Easypay · Pago · BKT Pay   SEPA Direct Debit (verify)
 Cash (record-only)
 PISP pay-by-bank (2027)
```

---

## 3. Flow A — client pays the studio

### A1. POK (RPAY sh.p.k.) — BoA-licensed EMI

**What it is.** Albanian EMI live since 2021, consumer wallet app plus a merchant API with
`splitWith` as a first-class field on order creation.

**Pros**
- Only provider found with **native marketplace split** — take your platform fee atomically at
  payment time, never touching principal. `splitWith.userPhoneNumber` (+355) may also solve
  artist commission splits.
- Native **ALL** with full FX fields (`originalCurrencyCode`, `appliedExchangeRate`).
- `autoCapture:false` + `expiresAfterMinutes` = a booking hold with server-side TTL. Maps onto
  the deposit flow exactly.
- Refund, cancel, detailed order retrieval — all first-class.
- **Real sandbox** with published 3DS test cards. Plain JWT Bearer auth.
- Consumer app with `confirmDeeplink` — "pay with the app you already have" matters in a
  cash-dominant market.
- `selectedBranchId` already models multi-location merchants.

**Cons**
- **No platform-level API key.** Each studio issues you its own `keyId`/`keySecret` (403 if
  they don't match the `merchantId`). Onboarding friction — though it is also the cleanest
  possible Article 4(g) posture.
- **No documented webhook signature.** Treat webhooks as untrusted pings; always re-fetch.
- No .NET SDK. Plain REST, so this is a day of work, not a week.
- Web SDK has no low-level 3DS primitive (React Native and Flutter do), so custom checkout UI
  on web means orchestrating 3DS yourself.
- Smaller company. Concentration risk, per the ALPay precedent.

### A2. Bank VPOS on Payten / Asseco MSU — BKT and peers

**What it is.** The acquiring bank is the merchant's own bank; MSU is the gateway layer.
Payten is Asseco SEE's payments arm; its sister platform Monri covers 30 banks across nine SEE
markets. This is the incumbent regional infrastructure.

**Pros**
- **Most established, most trusted.** A studio owner's own bank, with a real merchant
  agreement and real recourse.
- MSU platform supports **recurring plans, split payment, stored cards, pay-by-link,
  installments, PREAUTH/SALE** — far more than the "dumb redirect" reputation.
- BKT is the only Albanian bank offering **e-commerce instalments**, which is genuinely
  relevant for a €500 sleeve.
- Settlement into the studio's existing account. No new financial relationship.
- Article 4(g) posture is clean by construction.

**Cons**
- **Every capability is a per-bank commercial toggle.** The platform can do recurring; whether
  BKT sells it to a small studio is unknown. This is the single biggest unknown in this memo.
- **N integrations** if studios bank at different banks — unless you standardise on MSU, which
  only helps for MSU banks.
- Onboarding is 2–4 weeks per studio, at the studio's own bank, with paperwork.
- Fees ~1.5–3.5% plus setup ALL 10,000–50,000, negotiated per merchant.
- Settlement 2–5 business days.
- No public developer docs for the Albanian deployments; you go through the bank.

### A3. Paysera Albania — BoA-licensed EMI

**Pros**
- BoA-licensed since 2021, part of a real pan-European network (LT/LV/EE/RO/BG/ES/XK/UA/AL),
  local-language onboarding, "Fintech of the Year" locally.
- Checkout Classic works today; multi-currency; Lithuanian IBAN available.

**Cons**
- **No split payment.** You cannot take a platform fee atomically — you'd invoice studios
  separately and chase it.
- **Checkout Modern is LT/LV/EE only.** Albania gets the legacy signed-redirect integration.
- Wallet API (the one you asked about earlier) is the wrong product entirely — wallet-to-wallet
  only, and **production-only with no sandbox**.
- MAC auth, PHP-first SDKs.

### A4. Easypay — BoA-licensed EMI, 15+ years

**Pros**
- **The strongest physical distribution in Albania** — 500+ SME agent locations, 100+ services,
  the wallet ordinary Albanians actually recognise for bills and fines.
- Received the **first open banking licence** in Albania.
- Voucher payment option for merchants without an e-commerce platform — a genuine bridge for
  cash-preferring clients.

**Cons**
- **No public developer documentation found.** Everything is "contact us." For a solo founder
  that is a real cost.
- Positioned as bill-payment and wallet, not as a card gateway or marketplace platform.
- No evidence of split payments or sub-merchant provisioning.

### A5. BKT Pay — EMI, licensed November 2023

**Pros**
- Subsidiary of **BKT, the largest bank in Albania (~25% share)**. Balance-sheet strength and
  distribution that no fintech here can match.
- An EMI wrapper around the country's biggest acquirer is, on paper, the most durable
  combination available.

**Cons**
- Newest of the serious options; no public API documentation found.
- Unknown whether it exposes merchant/marketplace capability at all, or is purely consumer.

### A6. Pago (Rubicon sh.a.) and A7. Velox Pay (IuteCredit)

**Pros** — both BoA-licensed EMIs; Pago has QR-based merchant payments and a growing partner
network; Velox has IuteCredit's balance sheet and could plausibly offer instalment credit on
larger tattoo work, which is a real product idea.

**Cons** — no public merchant API documentation found for either. Both are unproven as
platform partners. Treat as future conversations, not v2 candidates.

### A8. Cash / record-only

**Pros** — zero integration, zero legal exposure, works today, and will very likely be your
highest-volume path in year one. Albania is still cash-dominant, especially outside Tirana.

**Cons** — no guarantee against no-shows, which is the whole point of a deposit. Reconciliation
depends on the studio marking it received.

**Ship this regardless. A booking flow that *requires* a card will lose bookings here.**

### A9. Pay-by-bank via a licensed PISP — the 2027 answer

First open banking licence issued **November 2024**; OTP Bank Albania and Intesa Sanpaolo Bank
Albania both run live PSD2-style developer portals with sandboxes; the BoA's **TIPS Clone**
instant-payment platform (built with Banca d'Italia, shared with Kosovo, Montenegro, BiH,
North Macedonia) was **planned to go live July 2026** — i.e. now.

**Pros** — will be dramatically cheaper than card interchange, settles in seconds, and bank
transfer is the payment method Albanians trust most.

**Cons** — you cannot do it yourself. Payment initiation is explicitly *carved back into* Law
55/2020 by Article 4(g), so you'd integrate a licensed PISP. Ecosystem is one licence old.

**Build the seam now. Ship in 12 months.**

---

## 4. Flow B — studio pays you

### B1. Polar.sh — merchant of record

**Pros**
- **Albania is the first country on their payout list**, explicitly documented. They also
  explain precisely why it works: Polar (US) is the MoR, payouts go via **Stripe Connect
  Express**, whose country coverage is far broader than Stripe Payments — so the "Stripe
  doesn't serve Albania" problem genuinely does not apply.
- Full MoR: they take international sales-tax liability.
- Modern developer experience, good docs, individual sellers supported where Stripe Connect
  Express allows the individual business type — worth checking for Person Fizik.

**Cons**
- ~5% + 50¢ (recently repriced with tiered plans — verify current rates directly).
- EUR/USD billing, not ALL.
- Newer and smaller than Paddle; less battle-tested for B2B invoicing.
- Payout depends on Stripe Connect Express accepting *you* specifically as an Albanian Person
  Fizik. Their docs give a step-by-step to check business-type eligibility — do that first.

### B2. Creem — merchant of record

**Pros**
- **Albania explicitly listed** with local bank transfer payout. 86 payout countries.
- **Cheapest headline rate** of the credible options: ~3.9% + 40¢, all-inclusive.

**Cons**
- **Payout fee: €7 or 1%, whichever is higher.** At €300/month revenue that's an extra 2.3%.
  Batch payouts quarterly or the headline saving evaporates.
- Payouts run through **Wise**, so you inherit Wise's Albania rules — verify business (not just
  personal) transfers are supported before committing.
- Smallest coverage (86 countries) and the youngest platform here.

### B3. Paddle — merchant of record

**Pros**
- **The most mature SaaS billing product** of the group: multi-product subscriptions, proper
  B2B invoicing, Retain for dunning, real API and webhooks with HMAC signatures, full sandbox.
- Albania is **not** on the unsupported list (which is purely sanctions-driven).
- "Sellers anywhere in the world with the exception of sanctioned countries."

**Cons**
- ~5% + $0.50.
- Albania supported by *omission* rather than by name — weaker written assurance than Polar or
  Creem. Get it confirmed in writing before you build.
- Heavier onboarding/KYC than the indie platforms.

### B4. Lemon Squeezy — merchant of record

**Pros** — **Albania explicitly listed** for bank payouts; simple, developer-friendly; PayPal
payout fallback in 200+ countries.
**Cons** — ~5% + $0.50; now inside Stripe, so strategic direction is uncertain; weaker B2B
invoicing than Paddle.

### B5. Dodo Payments — merchant of record

**Pros** — widest claimed coverage (220+ countries); **Albania is on their published
accept-payments-from list**; roughly 4% + 40¢ base.
**Cons** — **that list is the buyer side, not the seller-payout side.** I could not confirm
Albanian *seller payout* from Dodo's own documentation. Verify before considering. Youngest
and least proven of the five.

### B6. FastSpring

**Pros** — long-established MoR with deep enterprise features.
**Cons** — **pricing is quote-only and the seller-country list is not public.** For a solo
founder with a €20–50 price point, an opaque enterprise sales motion is the wrong shape. Only
worth a call if the others reject you.

### B7. POK — if merchant-initiated transactions exist in production

**Pros** — if POK confirms unattended charging on stored tokens, you get **one vendor for both
flows**, billing in **ALL**, one reconciliation model, one sandbox, no ~5% MoR premium.
**Cons** — the published API has **no recurring endpoint**; every pay-by-token flow runs 3DS
(cardholder-present); the only unattended hint is a MOTO endpoint marked **staging-only**. Also
POK is an acquirer, not a billing engine — plans, proration, dunning are all yours. And you
handle your own VAT and invoicing, which the MoRs do for you.

### B8. Paysera Recurring Billing

**Pros** — genuine card-on-file recurring (`repeat`, `repeat_type: month`), ~1–2%, BoA-licensed
EMI, callbacks with a read-receipt model that maps well onto Hangfire.
**Cons** — Checkout Classic only in Albania; PHP-first; you own VAT, invoicing and dunning.

### B9. Bank VPOS recurring via MSU — the sleeper

**Pros** — if BKT enables MSU's **Recurring Plan** module, you get recurring card billing at
domestic acquiring rates (~1.5–3.5%), settled in **ALL**, into an Albanian account, with your
own bank as counterparty. Cheapest credible option by a wide margin and the most "normal"
arrangement for an Albanian business.
**Cons** — entirely contingent on the bank enabling it and selling it to a sole trader. No
public docs. You own everything the MoRs would have done for you.

### B10. Invoice + bank transfer (SEPA Credit Transfer)

**Pros** — zero integration, works today, and it is simply how Albanian SMEs pay suppliers.
**Albania entered SEPA geographical scope November 2024 and began full implementation
7 October 2025**, so EUR transfers now clear at near-domestic cost — cross-border B2B transfer
costs in the acceding countries fell roughly tenfold.
**Cons** — manual reconciliation, no card-on-file, involuntary churn, dunning is your problem.
**Ship this first anyway** — it de-risks everything else.

### B11. SEPA Direct Debit — the best answer if it's available

**Pros** — genuine pull from a bank account, near-zero cost, no card expiry, mandate-based.
This is the only option in this entire document that literally does what you described:
automatic transfer from the studio's bank account.
**Cons** — **unverified.** Geographical SEPA entry does not automatically mean Albanian PSPs
adhere to the SDD Core/B2B schemes. **Ask your bank directly. This is the highest-value
question in this memo.**

### B12. Structural option — a foreign entity

Incorporating an EU entity (Estonia is the usual choice) or a US LLC would unlock Stripe
Billing directly. Listing it for completeness, not recommending it.

**Pros** — best-in-class billing infrastructure; clean EU invoicing to studios.
**Cons** — permanent-establishment and transfer-pricing exposure in Albania where you actually
live and work; annual cost and accounting overhead that a €20–50/month product can't carry
early; **does nothing for Flow A**, which is the harder problem; and it adds a second tax
jurisdiction to a business you're running solo. Revisit at scale, not now.

---

## 5. Comparison matrices

### Flow A

| | POK | Bank VPOS (MSU) | Paysera AL | Easypay | BKT Pay | Cash |
|---|---|---|---|---|---|---|
| Regulator | BoA EMI | BoA bank | BoA EMI | BoA EMI | BoA EMI | — |
| Native split / platform fee | ✅ | ⚠️ platform yes, bank? | ❌ | ❌ | ❓ | — |
| Charges in ALL | ✅ | ✅ | ⚠️ | ✅ | ✅ | ✅ |
| Auth-then-capture | ✅ | ✅ | ⚠️ | ❓ | ❓ | — |
| Public API docs | ✅ good | ❌ via bank | ✅ dated | ❌ | ❌ | — |
| Sandbox | ✅ | ⚠️ | ⚠️ | ❓ | ❓ | — |
| Integrations to maintain | **1** | **N** | 1 | 1 | 1 | 0 |
| Studio onboarding | POK KYC | 2–4 wks, bank | Paysera KYC | Easypay | BKT | none |
| Cost to studio | MDR | ~1.5–3.5% + setup | ~1.5–2% | ? | ? | 0 |
| Consumer reach | app + cards | cards | cards | **500+ agents** | BKT base | universal |
| Maturity / durability | ⚠️ small | ✅✅ highest | ✅ | ✅ 15 yrs | ✅✅ | — |

### Flow B

| | Polar | Creem | Paddle | Lemon Sq. | Dodo | POK | Paysera RB | MSU recurring | Invoice+SEPA | SEPA DD |
|---|---|---|---|---|---|---|---|---|---|---|
| Albania confirmed in vendor docs | ✅ **named** | ✅ **named** | ⚠️ by omission | ✅ **named** | ⚠️ buyer only | ✅ local | ✅ local | ✅ local | ✅ | ❓ |
| True unattended recurring | ✅ | ✅ | ✅ | ✅ | ✅ | ❓ | ✅ | ⚠️ if enabled | ❌ | ✅ |
| Pulls from **bank account** | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ |
| Is seller of record (VAT for you) | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Dunning built in | ✅ | ⚠️ | ✅✅ | ✅ | ⚠️ | ❌ | ⚠️ | ❌ | ❌ | ❌ |
| Bills in ALL | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ⚠️ | ✅ | ✅ | ❌ |
| Headline cost | ~5%+50¢ | ~3.9%+40¢ ᵃ | ~5%+50¢ | ~5%+50¢ | ~4%+40¢ ᵃ | MDR | ~1–2% | ~1.5–3.5% | ~0 | very low |
| Sandbox / DX | ✅ | ✅ | ✅✅ | ✅ | ✅ | ✅ | ⚠️ | ⚠️ | — | — |
| Maturity | ⚠️ young | ⚠️ youngest | ✅✅ | ✅ | ⚠️ young | ⚠️ | ✅ | ✅✅ | ✅✅ | ✅✅ |

ᵃ Creem adds a **€7-or-1% payout fee**; Dodo adds +1.5% international and +0.5% subscriptions.
At your price point these change the ranking — model them on real volume before choosing.

---

## 6. Recommendation

### The stack

**Flow A — POK as primary, bank VPOS as the enterprise escape hatch, cash always.**

POK is the only Albanian provider with native split, native ALL, authorize-then-capture with
server-side expiry, and a real sandbox — behind one integration instead of N. Ship the
record-only cash path on day one regardless. Keep bank VPOS behind the same abstraction for
large studios that already have a merchant agreement and won't move.

**Flow B — invoice + bank transfer now, Polar or Creem next, watch two things.**

Ship invoice+transfer immediately: zero integration, zero risk, and it is how Albanian SMEs
already pay suppliers. In parallel apply to **Polar** (Albania named first on their payout
list, and they document exactly why Stripe's Albania gap doesn't apply) and **Creem** (also
named, cheapest headline rate — but model the €7 payout fee against your actual volume).
Paddle remains the strongest *product*; its weakness is that Albania is supported by omission
rather than by name, so get written confirmation before you build on it.

Then watch two switches, either of which would beat all of the above:

1. **Does BKT enable MSU Recurring Plans for a sole trader?** Recurring card billing at
   ~1.5–3.5%, in ALL, into an Albanian account, with your own bank. Nothing else comes close
   on cost or normality.
2. **Do Albanian banks adhere to SEPA Direct Debit?** The only option that actually pulls from
   a bank account. Highest-value unknown in this document.

**Fiscal layer — easyPos (ESDP), unchanged.** DPT/AKSHI-certified, L7,000/yr, and it completes
Flow A. It cannot do Flow B (no NIPT in the Public API, and e-Fatura is desktop-Local-API
only), so **issuing a compliant B2B e-Fatura to each studio from a cloud backend remains the
one genuinely unsolved problem in this project.** If you use an MoR for Flow B, the MoR invoices
the studio and you invoice only the MoR — which sidesteps it entirely. That is a real argument
for the MoR route beyond the fee comparison.

### Ranked shortlist

| Rank | Flow | Provider | Why |
|---|---|---|---|
| 1 | A | **POK** | Split, ALL, sandbox, one integration |
| 1 | A | **Cash record-only** | Ship day one, highest volume year one |
| 2 | A | **Bank VPOS / MSU** | Most durable; keep as fallback |
| 3 | A | *PISP pay-by-bank* | Build the seam, ship 2027 |
| 1 | B | **Invoice + SEPA transfer** | Revenue with zero integration risk |
| 2 | B | **Polar** | Albania named; Stripe-gap explained away |
| 2 | B | **Creem** | Albania named; cheapest — check payout fee |
| 3 | B | **Paddle** | Best product; confirm Albania in writing |
| 4 | B | *MSU recurring / SEPA DD* | Would win if available — go find out |

---

## 7. Due-diligence checklist

Ordered by how much each answer changes the architecture.

1. **BKT:** does your VPOS expose MSU **Recurring Plans** and **Split Payment** to merchants?
   At what rate? (Changes both flows.)
2. **Your bank:** do Albanian banks adhere to **SEPA Direct Debit** Core/B2B, or only SCT/SCT
   Inst? (Only true bank-account pull.)
3. **POK:** can a merchant charge a stored card token in **production** with no cardholder
   interaction? Is there a **platform/partner programme** for provisioning studio merchants?
   Are webhooks **signed**?
4. **Polar:** confirm Stripe Connect Express accepts an Albanian **Person Fizik** as an
   individual business type — their docs give the exact steps to check.
5. **Creem:** confirm Wise supports **business** (not just personal) transfers to Albania, and
   model the €7-or-1% payout fee at €300–3,000/month.
6. **Paddle:** written confirmation that an Albanian Person Fizik can onboard.
7. **ESDP:** per-tenant API tokens issued programmatically? Is `docId` idempotent? Cloud
   e-Fatura on the roadmap? Reseller terms?
8. **Verify every licence claim against the BoA register directly** —
   `bankofalbania.org` → Supervision → Licensed institutions. The ALPay revocation in
   February 2026 is exactly why.
9. **Your accountant:** VAT treatment of a tattoo service (code `B`, 20%?), and whether a
   deposit + balance should be one fiscal invoice or two.

---

## 8. Architectural consequences

1. **`IPaymentProvider` (Flow A), `ISubscriptionBillingProvider` (Flow B), and
   `IFiscalizationProvider` are three separate abstractions.** Different regulators, vendors,
   failure modes and deadlines. Never collapse them.
2. **Assume you will change providers.** One EMI lost its licence in this market five months
   ago. Every provider goes behind an interface, with a second candidate identified for each.
3. **No platform balance, ever.** Flow A funds settle provider → studio. The moment a
   `PlatformLedger` or `PayoutQueue` appears in the schema, the Article 4(g) technical-service-
   provider exclusion is gone and you need a Bank of Albania licence. Add an architecture test
   that fails the build on such an entity.
4. **Flow B money is your own revenue** — collecting it is not a payment service under any
   regime. Just keep it in a legally separate account from anything Flow A touches.
5. **Webhooks are triggers, never sources of truth.** Re-fetch canonical state from the
   provider on every callback. This is true for POK (unsigned), and good hygiene everywhere.
6. **Model the mandate, not the charge.** Card tokens expire, Paysera allowances cap and
   expire, SDD mandates lapse after 36 months. One `BillingMandate` concept, one "is this still
   chargeable?" check before every cycle.
7. **Fiscalization runs off a settled-payment event, never inline.** A DPT outage must not fail
   a client's card payment. Durable queue, backoff, hard alert against the legal deadline.
8. **Help stays in sync** — `helpContent.ts`, the standalone manual, and the affected
   onboarding-tour steps, in the same change as the feature.

---

## Sources

**Regulator and legal**
- [Law No. 55/2020 "On Payment Services" (English)](https://www.bankofalbania.org/rc/doc/Ligji_Per_sherbimet_e_pagesave_anglisht_18199.pdf) — Article 4(g)
- [BoA — Electronic money institutions register](https://www.bankofalbania.org/Supervision/Licensed_institutions/Electronic_Money_Institutions/) · [Payment institutions register](https://www.bankofalbania.org/Supervision/Licensed_institutions/Payment_Institutions/)
- [Regulation 59/2021 on licensing PIs and EMIs](https://www.bankofalbania.org/Supervision/Regulatory_Framework/Licensing_Regulations/Document_Title_30416_1.html)
- [Monitor — BoA revokes Soft & Solution (ALPay) licence; EMIs drop from 11 to 10](https://monitor.al/banka-e-shqiperise-shfuqizon-licencen-e-institucionit-te-parase-elektronike-soft-solution/)
- [BoA — first open banking licence granted](https://www.bankofalbania.org/Press/Press_Releases/Open_banking_Bank_of_Albania_grants_the_licence_to_the_first_financial_entity.html)
- [BoA — Albania officially part of SEPA geographical scope](https://www.bankofalbania.org/Press/Press_Releases/Albania_officially_part_of_SEPA_geographical_scope.html) · [European Payments Council](https://www.europeanpaymentscouncil.eu/news-insights/news/inclusion-montenegro-and-albania-sepa-payment-schemes-geographical-scope) · [World Bank on SEPA impact](https://www.worldbank.org/en/news/feature/2026/03/25/cheaper-and-faster-payments-sepa-opens-new-horizons-for-the-western-balkans)
- [BoA — TIPS Clone](https://www.bankofalbania.org/rc/doc/2_TIPS_Clone_Bank_of_Albania_32368.pdf) · [ECB — Western Balkans instant payments](https://www.ecb.europa.eu/press/intro/news/html/ecb.mipnews250117_2.en.html)
- [BKT Pay licensed as EMI](https://www.bkt.com.al/BKT-Pay-Alb.pdf) · [IuteCredit/Velox Pay EMI licence](https://shqiptarja.com/lajm/iutecredit-europe-merr-licencen-per-te-operuar-si-institucion-i-parase-elektronike-ne-shqiperi)

**Flow A providers**
- [POK Payments docs](https://docs.pokpay.io/) · [full API reference](https://payments.doc.pokpay.io/) · [RPAY SH.P.K. EMI profile](https://thebanks.eu/emis/rpay-355514)
- [BKT — Virtual POS](https://www.bkt.com.al/en/business/daily-operations/virtual-pos-services/virtual-pos) · [Raiffeisen Albania — POS & e-commerce](https://www.raiffeisen.al/en/sme-businesses/products-and-services/se-business/pos-and-e-commerce-service.html)
- [Payten (Asseco SEE) — payments](https://www.payten.com/en/) · [MSU API v2 reference](https://merchantsafeunipay.com/msu/api/v2/doc) · [ASEE — Merchant Safe Unipay](https://see.asseco.com/en/news-events/news/1559/) · [ASEE — Virtual POS](https://see.asseco.com/payment/for-merchants/e-commerce/virtual-pos-496/)
- [Paysera — Albania EMI licence](https://www.paysera.com/v2/en/blog/paysera-albania-emi) · [Recurring Billing API](https://developers.paysera.com/guides/recurring-billing) · [Checkout Modern regional availability](https://developers.paysera.com/guides/checkout-modern)
- [EasyPay Albania — IAMTN profile](https://www.iamtn.org/companies/easypay)
- [OTP Bank Albania — open banking](https://otpbank.al/en/corporate/open-banking/) · [Intesa Sanpaolo Bank Albania — XS2A API docs](https://isbd.openbanking.intesasanpaolo.com/en/api_docs/isp-al) · [Credins — open banking](https://bankacredins.com/en/open-banking1/)
- [Viva.com — payment method availability (GR/MT/CY)](https://euhelp.viva.com/en/articles/10165167-payments-via-viva-com)
- [ecommerce4all.al — Albanian e-payment processing (GIZ/CEFTA)](https://ecommerce4all.al/en/e-payment-processing/)

**Flow B providers**
- [Polar — supported countries (Albania listed; Stripe Connect Express explanation)](https://polar.sh/docs/merchant-of-record/supported-countries)
- [Creem — supported countries (Albania listed; Wise payout restrictions)](https://docs.creem.io/merchant-of-record/supported-countries)
- [Lemon Squeezy — supported countries (Albania listed)](https://docs.lemonsqueezy.com/help/getting-started/supported-countries)
- [Paddle — which countries are supported](https://www.paddle.com/help/start/intro-to-paddle/which-countries-are-supported-by-paddle)
- [Dodo Payments — countries eligible for payment acceptance (buyer side)](https://docs.dodopayments.com/miscellaneous/list-of-countries-we-accept-payments-from)
- [FastSpring — payouts portal](https://developer.fastspring.com/docs/fastspring-payouts-portal)

**Fiscal layer**
- [easyPos Public API — Postman](https://documenter.getpostman.com/view/21155107/2sBXVkCA3S) · [easyPos API guide](https://easypos.al/api) · [easyPos / ESDP](https://easypos.al/)
