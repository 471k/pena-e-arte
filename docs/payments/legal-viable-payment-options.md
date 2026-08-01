# Pena e Artë — Legally Viable Payment Options (Albania, v2 scope)

Prepared 31 July 2026. For Ali Kreku, Person Fizik, NIPT M12219042B.
Not legal advice — this is a research memo to take to an Albanian payments lawyer.

---

## 0. The one question that decides everything

**Do funds ever land in an account controlled by Pena e Artë?**

If yes, you are providing a payment service under Law 55/2020 and you need a Bank of
Albania licence. If no, you are outside the law entirely — by explicit statutory carve-out.

Law 55/2020, Article 4, letter "g" (verbatim):

> services provided by **technical service providers, which support the provision of
> payment services, without them entering at any time into possession of the funds to be
> transferred**, including processing and storage of data, trust and privacy protection
> services, data and entity authentication, information technology (IT) and communication
> network provision, provision and maintenance of terminals and devices used for payment
> services, with the exclusion of payment initiation services and account information
> services

This is the exclusion Pena e Artë should be built to sit inside. It is unambiguous, it is
the same carve-out PSD2 Art. 3(j) gives European booking platforms, and it costs nothing.

### The exemption you should *not* rely on

Article 4, letter "b" — the commercial agent exclusion:

> payment transactions from the payer to the payee through a commercial agent authorized
> via an agreement to negotiate or conclude the sale or purchase of goods or services on
> behalf of **only the payer or only the payee**

This is the exemption marketplaces reach for when they want to hold funds. Two problems:

1. It requires you to have actual authority to *negotiate or conclude* the sale. A booking
   platform that surfaces an artist's calendar and takes a deposit generally does not have
   that authority — the studio sets the price and concludes the contract.
2. EBA supervisory practice on the identical PSD2 wording has been consistently narrow, and
   Albanian supervision follows the EU reading. Building on it is a bet you would have to
   defend to BoA later, with no upside over posture A below.

### Licensing, if you ever wanted it

Payment Institution / EMI licensing under Regulation 59/2021 is real: minimum capital,
governance, AML officer, outsourcing policy, ongoing reporting. 12–18 months and six
figures. Not a solo-founder path. Rule it out for v2 and design so you never need it.

---

## Flow A — client pays the studio

**Design rule: Pena e Artë never appears in the money flow. The studio is the merchant.
You orchestrate, tokenize nothing, settle nothing.**

### A1 — Per-tenant bank VPOS (recommended default)

Each studio opens its own e-commerce merchant account (VPOS) with its own Albanian bank.
Pena e Artë stores that studio's VPOS credentials per tenant and initiates the redirect or
server-to-server call. Funds settle bank → studio. You never see them.

Banks with e-commerce acquiring in Albania: **BKT**, **Raiffeisen Bank Albania**, **Credins**,
**Intesa Sanpaolo Bank Albania**, **OTP Bank Albania**, **Union Bank**, **ABI**, **Tirana Bank**,
**ProCredit**. BKT publishes VPOS requirements openly (own the website, hold a BKT account,
valid SSL) and is the only Albanian bank offering e-commerce instalments — useful for
larger tattoo pieces.

| | |
|---|---|
| Legal posture | Art. 4(g) technical service provider — clean |
| Your licence | None |
| Who holds risk | The studio (chargebacks, refunds, KYC) |
| Cost to you | Zero |
| Cost to studio | ~1.5–3.5% + setup ALL 10k–50k, negotiable |
| Onboarding | 2–4 weeks per studio, at the studio's own bank |
| Downside | You maintain N bank adapters; onboarding friction is the studio's problem, but it becomes your churn problem |
| Settlement | 2–5 business days, ALL (EUR available at most banks) |

**Verify before building:** whether each target bank's VPOS supports card-on-file /
merchant-initiated transactions. Albanian VPOS is historically single-payment redirect
only. If MIT is unavailable, "deposit now, balance on the day" must be modelled as two
separate authorizations, not a stored token.

### A2 — Single EMI aggregator, studios as sub-merchants (recommended alternative)

Integrate once against a BoA-licensed EMI that onboards each studio as its own merchant
under a platform/partner agreement. You get one API; the studio still has its own merchant
relationship and its own settlement account.

Realistic candidates, all BoA-licensed:

- **Paysera Albania** — EMI licensed by BoA since April 2021, part of the Paysera network
  (LT/LV/EE/RO/BG/ES/XK/UA/AL). Offers a payment gateway for e-shops, multi-currency,
  Albanian-language onboarding, and a Lithuanian-IBAN path. The most credible aggregator
  for a small Albanian SaaS.
- **EasyPay** — 16-year-old Albanian EMI, 580+ physical locations, strongest local wallet
  brand and bill-payment rails. Good for cash-adjacent flows; weaker as a card gateway.
- **Pago (Rubicon sh.a.)** — newer EMI, QR-based merchant payments, growing partner network.

| | |
|---|---|
| Legal posture | Art. 4(g) — still clean, provided the EMI contracts directly with each studio and settles to the studio |
| Your licence | None |
| Cost to you | Zero, or a partner rev-share |
| Onboarding | Much faster than per-bank; one commercial relationship for you |
| Downside | Concentration risk; if the EMI drops your vertical, every tenant breaks at once |

**Verify before building:** that Paysera Albania actually supports a platform model where
Pena e Artë provisions sub-merchants via API and never appears as merchant of record. If
they can only offer you one merchant account that all studios' money passes through, this
option becomes an unlicensed payment service and must be rejected.

### A3 — Pay-by-bank / PISP (build the seam now, ship in 12 months)

Law 55/2020 transposes PSD2 open banking. Albania issued its **first open banking licence in
November 2024**. **OTP Bank Albania** and **Intesa Sanpaolo Bank Albania** both run live
developer portals with PSD2-style payment-initiation APIs and sandboxes. Meanwhile the BoA
is standing up domestic instant payments on a **TIPS Clone** built with Banca d'Italia,
shared with Kosovo, Montenegro, BiH and North Macedonia, **go-live planned July 2026**.

Combined, that means: within roughly a year, a client should be able to pay a studio deposit
by instant bank transfer, confirmed in seconds, at a fraction of card interchange — in a
country where cash still dominates and card penetration is low.

You cannot do this yourself: payment initiation is explicitly *carved back in* to the law by
Art. 4(g) ("with the exclusion of payment initiation services"). You would integrate a
licensed PISP as a provider behind your abstraction, not become one.

| | |
|---|---|
| Legal posture | Clean, provided the licensed PISP is the one initiating |
| Timing | Watch TIPS Clone go-live and the PISP licence register through 2026–27 |
| Why it matters | Card fees ~1.5–3.5%; instant-payment fees will be a fraction of that, and Albanian consumers trust bank transfer more than cards |

### A4 — Record-only deposit (ship this on day one regardless)

Cash remains dominant in Albania, especially outside Tirana. A booking flow that *requires*
a card payment will lose bookings. Model a deposit that the studio marks as received in
cash or by direct bank transfer to the studio's IBAN — Pena e Artë records the state
transition, sends the confirmation, and never touches money.

Zero legal exposure, zero integration, and it is very likely your highest-volume path in
year one.

---

## Flow B — studio pays you the subscription

You are selling B2B SaaS from Albania to Albanian businesses. Two clean routes, and they
are not mutually exclusive.

### B1 — Merchant of Record (recommended primary)

An MoR becomes the legal seller to the studio. It handles card acceptance, subscription
billing, dunning, VAT/sales tax, and invoicing to the customer. You issue **one** invoice
per month — to the MoR, a foreign company — which is an export of services.

That is the quiet win here: **it collapses your invoicing surface from N studios to one
counterparty**, which sits well alongside your outsourced fiscalization vendor rather than
competing with it.

Both major MoRs accept Albanian sellers, confirmed from their own documentation:

| Provider | Albania status | Fee | Payouts |
|---|---|---|---|
| **Lemon Squeezy** | **Albania explicitly listed** for bank payouts | ~5% + $0.50 | Bank (ALL/EUR account) or PayPal |
| **Paddle** | Supports sellers worldwide; **Albania is not on the unsupported list** (that list is sanctions-driven: Iran, Russia, Syria, etc.) | ~5% + $0.50 | Worldwide payout |

Paddle is the stronger SaaS product (multi-product subscriptions, B2B invoicing, retention
tooling, proper API and webhooks). Lemon Squeezy has the more explicit Albania commitment
in writing. Paddle also runs on Stripe underneath — but *as principal*, so the Albanian
merchant-onboarding block does not apply to you.

**Caveats to test before committing:**

- Both will run KYC on you as a Person Fizik. Have the QKB extract, NIPT, ID, proof of
  address and a live site with terms/refund/privacy pages ready — thin policy pages are the
  single most common rejection cause for Albanian applicants.
- Confirm the MoR will bill in **ALL**, or accept that Albanian studios pay in EUR and
  absorb FX. Small studios are price-sensitive; a EUR-denominated card charge with an FX
  markup reads as a price increase.
- MoR fees (~5%) are roughly double raw acquiring. On a €30/month plan that is €1.50 — cheap
  relative to building billing, tax and dunning yourself.

### B2 — Domestic invoice + bank transfer (recommended fallback, ship first)

Albanian SMEs pay B2B suppliers by bank transfer against an invoice. This is normal, expected,
and requires no payment integration at all — just your vendor's fiscalized invoice plus a
reconciliation job.

Since **Albania entered the SEPA geographical scope in November 2024 and began full SEPA
implementation on 7 October 2025**, EUR transfers to and from Albania now clear at
near-domestic cost and speed. Average B2B cross-border transfer costs in the acceding
countries fell roughly tenfold after launch. If you invoice in EUR, SEPA Credit Transfer is
now a genuinely good rail.

| | |
|---|---|
| Legal posture | Trivially clean — you are just a supplier being paid |
| Cost | Near zero |
| Downside | Manual reconciliation, involuntary churn, no card-on-file, dunning is your problem |
| Build | Bank statement import (MT940/CAMT or CSV) + reference-code matching |

**Verify:** whether Albanian banks yet adhere to **SEPA Direct Debit** (SDD Core/B2B), not
just SCT. If SDD is live, it is the best possible Flow B rail — pull-based, low-cost,
recurring by design. Geographical-scope entry does not automatically mean every scheme is
adhered to; ask your bank directly.

### B3 — Your own VPOS or EMI account for recurring cards

Open a VPOS in your own name (BKT/Raiffeisen/Credins) or a Paysera merchant account and
charge cards yourself. Legally clean — you are collecting your own revenue.

Only worth it if the acquirer supports genuine card-on-file MIT with tokens. Ask
explicitly; do not assume. Without MIT you are asking each studio owner to re-enter a card
every month, which is worse than B2 in every way. And you take on VAT, invoicing, dunning
and PCI scope that B1 hands to someone else.

---

## Recommended stack

**Ship now**

- Flow A: **A4 (record-only)** + **A1 (per-tenant VPOS)** starting with two banks — BKT and one
  of Raiffeisen/Credins — behind a provider abstraction.
- Flow B: **B2 (invoice + transfer)** as the default, so you have revenue with zero
  integration risk.

**Next**

- Flow B: **B1 (Paddle or Lemon Squeezy)** for self-serve card subscriptions. Apply early —
  KYC on an Albanian Person Fizik will take longer than the docs imply.
- Flow A: **A2 (Paysera Albania)** as the low-friction onboarding path for studios that don't
  want to negotiate their own VPOS.

**Watch**

- TIPS Clone instant payments (go-live planned July 2026) and the BoA PISP licence register.
  When a licensed PISP with a usable API exists, **A3** becomes the cheapest and most
  culturally-native way for an Albanian client to pay a deposit.

---

## What this means for the codebase

1. **`IPaymentProvider` abstraction in `Pena_e_Arte.Domain`**, implementations in
   `Pena_e_Arte.Infrastructure`. Bank VPOS, EMI aggregator, PISP and record-only are all
   just providers. Do not let BKT's redirect semantics leak into the Application layer.
2. **Per-tenant provider credentials, never in source, never in the DB in plaintext** —
   Vault or env-injected, keyed by tenant, resolved at request time. This is rule 4 in
   CLAUDE.md and it is also the thing that keeps you inside Art. 4(g).
3. **No platform balance. No `PlatformLedger`. No `PayoutQueue`.** If a schema ever implies
   Pena e Artë holds studio funds, the Art. 4(g) exclusion is gone. Add an architecture test
   that fails the build if such an entity appears.
4. **Payments are a separate bounded context** from the invoicing adapter. The vendor
   invoicing API is called *after* a settled-payment event, never inline in the payment path.
5. **Reconciliation is a first-class feature**, not a script. Studios will ask "did this
   client actually pay?" — and under A1 you cannot answer from your own database, only from
   the bank's webhook or settlement file. Design for eventual consistency and for the
   studio-marks-it-paid override.
6. **Idempotency keys and Hangfire retries on every provider call.** Albanian bank VPOS
   endpoints are not Stripe-grade; assume timeouts and duplicate callbacks.
7. **Help stays in sync** — `helpContent.ts`, the standalone manual, and the onboarding tour
   all need a "how your studio gets paid" section per provider, in the same change.

---

## Open questions for your lawyer / the banks

1. Does BoA agree that a booking platform that never holds funds sits in Art. 4(g)? Get this
   in writing if possible — it is cheap insurance and a sales asset with studios.
2. Does any Albanian acquirer support card-on-file MIT for e-commerce? This determines
   whether "deposit + balance" is one flow or two.
3. Will Paysera Albania provision sub-merchants via API under a platform agreement, with
   settlement direct to each studio?
4. Do Albanian banks adhere to SEPA Direct Debit yet, or only SCT/SCT Inst?
5. Under the outsourced fiscalization model, who is the legal issuer of the fiscal invoice
   for a Flow A deposit — the studio, or the vendor acting for the studio? Your adapter's
   contract depends on the answer.

---

## Sources

- [Law No. 55/2020 "On Payment Services" (English, Bank of Albania)](https://www.bankofalbania.org/rc/doc/Ligji_Per_sherbimet_e_pagesave_anglisht_18199.pdf) — Article 4 exclusions
- [Regulation 59/2021 on licensing of payment institutions and EMIs](https://www.bankofalbania.org/Supervision/Regulatory_Framework/Licensing_Regulations/Document_Title_30416_1.html)
- [BoA — licensed Electronic Money Institutions](https://www.bankofalbania.org/Supervision/Licensed_institutions/Electronic_Money_Institutions/)
- [BoA — licensed Payment Institutions](https://www.bankofalbania.org/Supervision/Licensed_institutions/Payment_Institutions/)
- [BoA — Open banking: licence granted to the first financial entity](https://www.bankofalbania.org/Press/Press_Releases/Open_banking_Bank_of_Albania_grants_the_licence_to_the_first_financial_entity.html)
- [BoA — Albania officially part of SEPA geographical scope](https://www.bankofalbania.org/Press/Press_Releases/Albania_officially_part_of_SEPA_geographical_scope.html)
- [European Payments Council — inclusion of Montenegro and Albania in SEPA](https://www.europeanpaymentscouncil.eu/news-insights/news/inclusion-montenegro-and-albania-sepa-payment-schemes-geographical-scope)
- [World Bank — Cheaper and Faster Payments: SEPA Opens New Horizons for the Western Balkans](https://www.worldbank.org/en/news/feature/2026/03/25/cheaper-and-faster-payments-sepa-opens-new-horizons-for-the-western-balkans)
- [BoA — TIPS Clone: A Pathway to Regional Instant Payment Integration](https://www.bankofalbania.org/rc/doc/2_TIPS_Clone_Bank_of_Albania_32368.pdf)
- [ECB — Roll-out of instant payment settlement service in Western Balkans](https://www.ecb.europa.eu/press/intro/news/html/ecb.mipnews250117_2.en.html)
- [BKT — Virtual POS](https://www.bkt.com.al/en/business/daily-operations/virtual-pos-services/virtual-pos)
- [Raiffeisen Bank Albania — POS and E-commerce service](https://www.raiffeisen.al/en/sme-businesses/products-and-services/se-business/pos-and-e-commerce-service.html)
- [OTP Bank Albania — Open Banking developer portal](https://otpbank.al/en/corporate/open-banking/)
- [Intesa Sanpaolo Bank Albania — Open Banking API docs](https://isbd.openbanking.intesasanpaolo.com/en/api_docs/isp-al)
- [Paysera — EMI licence issued by Bank of Albania](https://www.paysera.com/v2/en/blog/paysera-albania-emi)
- [Lemon Squeezy — Supported Countries (Albania listed for bank payouts)](https://docs.lemonsqueezy.com/help/getting-started/supported-countries)
- [Paddle — Which countries are supported by Paddle?](https://www.paddle.com/help/start/intro-to-paddle/which-countries-are-supported-by-paddle)
- [The Fintech Times — Albania's Fintech and Wider Digital Landscape in 2026](https://thefintechtimes.com/albanias-fintech-and-wider-digital-landscape-in-2026/)
- [PayAtlas — Accepting Payments in Albania: PSPs, Compliance & Fees](https://payatlas.com/countries/albania-al) — used only for fee/settlement/onboarding ranges; its claim that Stripe and Adyen serve Albanian merchants is not reliable
