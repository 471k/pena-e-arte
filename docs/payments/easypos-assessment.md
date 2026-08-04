# easyPos Public API — full assessment

Prepared 31 July 2026. Fourth in the series with `legal-viable-payment-options.md`,
`paysera-wallet-api-assessment.md` and `pok-assessment.md`. Not legal or tax advice.

Source reviewed: the [easyPos Public API Postman
collection](https://documenter.getpostman.com/view/21155107/2sBXVkCA3S) — full collection
JSON pulled and every request, payload schema, example body and example response read. Plus
[easypos.al/api](https://easypos.al/api) and [easypos.al](https://easypos.al/).

---

## Verdict up front

**This is not a payments API. It moves no money and it cannot collect a subscription.**

It is a **fiscalization API** — it registers invoices with the Albanian tax administration
and returns the NIVF/NSLF codes and the verification link. Which means it does not compete
with POK, Paysera, or the MoRs at all.

**It is, however, exactly the thing your CLAUDE.md scope calls "a thin adapter to a vendor's
invoicing API."** You said fiscalized invoicing was outsourced to a certified third-party
vendor. easyPos is a credible candidate for that vendor, and this is the API you'd adapt to.

| | |
|---|---|
| **Flow A** (client → studio) | Moves no money — but it is the **missing second half**. Every payment a studio takes must be fiscalized. This automates it. **Strong yes, as a complement to POK.** |
| **Flow B** (studio → you) | **No — and structurally so.** The Public API cannot put a NIPT on an invoice, and B2B e-Fatura is a different product with no cloud API. |

---

## 1. Who ESDP is

easyPos is built by **ESDP**, Rruga Bernoca Nr. 2, Lundër, Tiranë. Founded 2013 doing
electronics; shipped a fiscal-cash-register modem in 2016; now among the first companies
**certified by DPT (Drejtoria e Përgjithshme e Tatimeve) and AKSHI** for fiscalization
software. Two products: **easyPos** (fiscal invoices, cash register) and **easyInvoice**
(e-Fatura, B2B/B2G).

Pricing, per business per year: easyPos **L 7,000**, easyInvoice **L 10,000**, both
**L 15,000** — roughly €70 / €100 / €150. Cheap, and paid by the studio, not by you.

The certification is the point. Fiscalization software must be DPT/AKSHI-certified to be
legal. ESDP is; you are not and should not try to become one.

---

## 2. The complete API surface

Base URLs:

```
Production   https://api.easypos.al/fiscalisation-service/v1
Development  https://api.dev.easypos.al/fiscalisation-service/v1
```

Auth is two headers on every request:

```
Authorization: Bearer <your-access-token>
integration-app: <your integration app identifier>   // "generic" in the collection
```

**There is no login or token endpoint in the collection.** Tokens are issued out of band —
the site says "Request API Access" via WhatsApp / `support@easypos.al`. Flag this; see §5.

### Endpoints

**Invoice**

| | |
|---|---|
| `POST /invoice/register` | The core call. Twelve worked example variants are published: Minimal, CARD Payment, Multiple Payments, Multiple Articles, With Discount, Invoice-Level Discount, NONCASH, NONCASH with Bank Transfer, With Buyer, With Currency, With Operator, Full Example. |
| `POST /invoice/cancel` | Cancel a previously fiscalized invoice. Takes its own `docId`. |
| `POST /invoice/status` | Check fiscalization status by `docId`. |
| `POST /invoice/pdf` | Generate the invoice PDF by `iic`. |
| `POST /invoice/pdf` | Generate the **receipt** by `iic` (same path, receipt variant). |

**Balance** — the daily cash-register (*arka*) declaration

| | |
|---|---|
| `POST /balance/initiate` | Opening balance for the register. `{ amount, notes }` |
| `POST /balance/deposit` | Record a cash deposit into the register. |
| `POST /balance/withdraw` | Record a cash withdrawal. |

**Utilities**

| | |
|---|---|
| `POST /utilities/get-taxpayers` | Search registered taxpayers by name or NIPT. |
| `POST /utilities/get-operators` | List registered operator codes. |

### Request shape

```json
{
  "docId": "<UUIDv4>",
  "operatorCode": "gh537ez280",
  "articles": [
    { "articleId": "PROD001", "vatCode": "B", "name": "Product",
      "price": 100, "units": 1, "soldIn": "XPP",
      "rebate": { "inPercentage": 10 } }
  ],
  "payment": [
    { "type": "CARD", "amount": 100 }
  ],
  "currency": { "code": "EUR", "exRate": 100 },
  "invoiceRebate": { "inPercentage": 10 },
  "buyer": { "buyerIDType": "ID", "buyerIDNum": "I12345678A",
             "buyerName": "…", "buyerAddress": "…",
             "buyerTown": "Tirane", "buyerCountry": "ALB" }
}
```

### Response shape

```json
{
  "fic": "7251edde-e10c-4e56-9f8b-a9e215ed2475",
  "iic": "E31BC7C6CF41F6E42515E765CCBC5F5F",
  "link": "https://efiskalizimi-app-test.tatime.gov.al/invoice-check/#/verify?iic=…&tin=…&crtd=…&ord=1&bu=…&cr=…&sw=…&prc=200.00"
}
```

`fic` = NIVF (the tax administration's unique invoice number). `iic` = NSLF (the issuer
security code). `link` is the public DPT verification URL — the thing behind the QR code on
a fiscal receipt. Note the example points at `efiskalizimi-app-**test**.tatime.gov.al`, so
the dev environment really does hit the DPT test system. That's a proper sandbox.

### Reference data

**VAT codes** A–L: `B` 20% standard, `D` 10% reduced, `E` 6% reduced, `A` 0% tax free,
`J` 0% export, `C`/`K` exemptions, `F`–`I` margin schemes.

**Payment types**: `CASH`/`BANKNOTE`, `CARD`, `ACCOUNT` (bank transfer — requires
`details` with IBAN, SWIFT, country, currency), `CHECK`, `SVOUCHER`, `COMPANY`,
`FACTORING`, `COMPENSATION`, `TRANSFER`, `WAIVER`, `KIND`, `OTHER`.

**Units** are UN/CEFACT: `XPP` piece, `KGM` kilogram.

**Buyer ID types**: `ID`, `PASS`, `VAT`, `TAX`, `SOC`.

---

## 3. Flow A — this is the missing half

easyPos moves no money. But every deposit and every session payment an Albanian studio takes
must be fiscalized within the legal window, and right now that means the owner re-typing it
into easyPos after taking the payment. Double entry. That is the single thing most likely to
make a booking SaaS feel like *more* admin instead of less.

If Pena e Artë fiscalizes automatically the moment a payment settles, that is a real
differentiator in this market — and it directly serves CLAUDE.md rule 6, because
Vagaro/Fresha/Boulevard all ship native fiscal/tax compliance for their markets and none of
them handle Albania.

### The pairing with POK

```
POK order captured ──webhook──▶ re-fetch GET /sdk-orders/{id}
                                        │
                                        ▼
                       POST /invoice/register  (easyPos)
                         payment: [{ type: "CARD", amount }]
                         articles: [{ name: "Tattoo session — 3h",
                                      vatCode: "B", soldIn: "XPP" }]
                         operatorCode: <artist>
                                        │
                                        ▼
                       store fic / iic / link on the appointment
                       → show QR, print thermal, send via WhatsApp
```

Cash deposits use the same call with `type: "CASH"`. The record-only path from the first memo
becomes fiscally real.

### Things that fit the tattoo domain unusually well

- **Multiple payment objects on one invoice.** `[{CASH, 50}, {CARD, 50}]` is a published
  example. "Deposit by card at booking, balance in cash on the day" is one invoice, natively.
  (Whether it *should* be one invoice or two is an accountant question — the API supports
  both.)
- **`operatorCode`** (format `xx000xx000`) is the fiscal operator — i.e. **the artist**.
  `get-operators` lets you populate a picker. Your artist entity gets a fiscal identity.
- **`invoice/cancel`** covers cancelled appointments and refunded deposits.
- **`currency: { code, exRate }`** for EUR-priced work with tourists — relevant for a
  destination market like Tirana.
- **`rebate`** at article and invoice level — loyalty discounts, touch-up freebies.
- **Balance endpoints** map to the studio's legally required daily *arka* declaration. You
  could open the till automatically at day start. Tempting — but see the warning in §5.

---

## 4. Flow B — structurally no

Two independent blockers.

**a) The Public API cannot issue an invoice with a NIPT.** From the collection's own
overview, verbatim:

> Note: NUIS (Albanian NIPT) is only for eInvoice - not available in Public API.

`buyerIDType` accepts `ID`, `PASS`, `VAT`, `TAX`, `SOC` — personal ID, passport, foreign VAT,
tax number, social security. Not the Albanian business NIPT. Your customers are Albanian
businesses; a B2B SaaS invoice needs their NIPT. This API structurally cannot produce it.

**b) e-Fatura lives in easyInvoice, and easyInvoice has no cloud API.** ESDP's API page
lists easyInvoice under **"Local API & File Integration"** — it talks to an *installed
desktop application* over the LAN, alongside JSON/TXT file drop. You cannot call that from an
ASP.NET Core service running on K3s.

So for invoicing your own subscriptions to studios, easyPos Public API is not an option. You
would use the easyInvoice web app manually, or find a cloud e-Fatura provider. **Ask ESDP
whether a cloud easyInvoice / e-Fatura API is on the roadmap** — if it is, one vendor covers
both your fiscalization needs and that is worth waiting for.

---

## 5. Concerns worth raising before you commit

**No token endpoint — and you are multi-tenant.** Tokens are issued by a human over
WhatsApp or email. For a SaaS where every studio needs its own credentials against its own
fiscal certificate, this is the question that decides whether onboarding is self-service or a
support ticket. Ask: *is there per-tenant token issuance, is it programmatic, and how are
tokens rotated or revoked?*

**`docId` is your only idempotency handle.** There is no `Idempotency-Key` header. Derive
`docId` deterministically from the payment ID so a Hangfire retry cannot double-fiscalize —
and **confirm with ESDP that re-posting the same `docId` is safe** rather than assuming it.
A duplicate fiscal invoice is a tax problem, not a bug.

**The 48-hour offline window belongs to the desktop app, not to you.** easyPos's marketing
offline module queues locally and syncs within the legal 48h. Calling the cloud API from your
backend, DPT downtime is *your* problem: queue, retry with backoff, and alert loudly as you
approach the legal deadline. This is a job with a hard SLA and an escalation path, not a
fire-and-forget HTTP call. Design it that way from day one.

**No webhooks.** Fiscalization here is synchronous request/response, which is fine, but
there's no callback channel if something changes state later. `invoice/status` is your only
reconciliation tool — poll it for anything that didn't return cleanly.

**Think hard before automating the Balance endpoints.** Declaring the cash register is the
studio's legal obligation. If your software opens the till each morning and the amount is
wrong, you have inserted yourself into someone else's tax liability. My inclination: expose
it as an explicit owner action in the UI, log who triggered it, and never do it on a timer.

**You'd be handling the studio's fiscal credentials.** That is a higher-trust relationship
than payment orchestration — with payments the money never touches you, but here you are
signing tax documents on their behalf. Vault, per-tenant, never logged, and put the
liability allocation in the studio contract explicitly.

**Every studio must be an easyPos customer.** L 7,000/yr is not a barrier, but it is a
dependency in your activation funnel. ESDP's site has a *"Bashkëpuno me ne si përfaqësues"*
(become a representative) programme — worth a call. A bundled or reseller arrangement would
turn a friction point into a margin line.

**Legal posture is unchanged.** Registering an invoice is reporting, not moving money.
Article 4(g) analysis from the first memo is untouched — the studio is the seller and its
certificate signs the invoice; you are a technical service provider.

---

## 6. How the four options actually relate

They are not alternatives. Only one column here moves client money, and only one produces a
fiscal receipt.

| | **POK** | **Paysera** | **Paddle / Lemon Squeezy** | **easyPos** |
|---|---|---|---|---|
| Category | Acquiring + marketplace split (EMI) | Acquiring + recurring (EMI) | Merchant of record | **Fiscalization** |
| Moves money | ✅ | ✅ | ✅ | ❌ |
| Flow A — client → studio | ✅ best fit | ⚠️ no split | ❌ | ➕ **completes it** |
| Flow B — studio → you | ❓ MIT unconfirmed | ✅ card token | ✅ best | ❌ no NIPT |
| Fiscal invoice to DPT | ❌ | ❌ | ❌ | ✅ |
| Regulator | Bank of Albania | Bank of Albania | — | **DPT + AKSHI** |
| Who pays | studio (MDR) | studio / you | you (~5%) | studio (L7,000/yr) |

**The stack that emerges:**

```
Flow A   POK (money)  +  easyPos (fiscal receipt)  +  record-only path for cash
Flow B   MoR or invoice+transfer (money)  +  ???  (B2B e-Fatura — still unsolved)
```

That last gap is now the clearest open item in the whole project.

---

## 7. Recommendation

**Adopt easyPos as the Flow A fiscalization adapter, paired with POK.** It is the right
category of thing, from a DPT/AKSHI-certified vendor, at a price no studio will argue with,
with a real DPT-test sandbox and a payload model that fits tattoo work better than I expected
(multi-payment invoices, operator codes, article rebates).

**Do not expect it to solve Flow B.** The NIPT exclusion is explicit and the e-Fatura product
has no cloud API. Keep MoR or invoice+transfer for collection, and treat "how do I issue a
compliant B2B e-Fatura to each studio from a cloud backend" as a separate, still-open
question.

### Questions to send ESDP

1. **Per-tenant tokens** — can each studio get its own API token, issued programmatically?
   How are they rotated and revoked? What does onboarding a new studio actually look like?
2. **Is re-posting the same `docId` idempotent**, or does it create a duplicate fiscal
   invoice?
3. **Is a cloud easyInvoice / e-Fatura API on the roadmap?** Today it is Local API only. This
   is the difference between one vendor and two.
4. **Rate limits and SLA** on `/invoice/register`, and the documented behaviour when DPT
   itself is down. What's the recommended retry policy?
5. **Partner / reseller programme** — terms, and whether you can bundle easyPos into a
   Pena e Artë plan.
6. Is `integration-app` a value they issue per integrator, and does it affect rate limits or
   support routing?
7. For a **tattoo service**, is `vatCode` `B` (20%) correct, and is `soldIn: XPP` the right
   unit for a timed service? (Confirm with your accountant too — do not take a vendor's word
   on VAT treatment.)

---

## 8. Build implications

1. **`IFiscalizationProvider` is a separate abstraction from `IPaymentProvider`.** Different
   regulator, different vendor, different failure modes, different legal deadline. Do not
   collapse them.
2. **Fiscalization is triggered by a settled-payment domain event, never inline in the
   payment path.** A DPT outage must not fail a client's card payment. Hangfire job, durable
   queue, exponential backoff, hard alert at T-minus-N hours against the legal deadline.
3. **`docId` derived deterministically** from the payment/appointment ID. Persist it before
   the first call, never regenerate on retry.
4. **Persist `fic`, `iic` and `link` on the appointment** and treat them as immutable once
   set. They are the legal artefact — the client's proof of purchase and the studio's proof
   of declaration.
5. **Model the article catalogue as a first-class thing.** `articleId`, `vatCode`, `soldIn`
   and `name` (max 100 chars) per service. Studios will need to configure this once during
   onboarding — build the UI for it, don't hardcode.
6. **Map `operatorCode` onto the artist entity.** Seed it from `get-operators` during studio
   setup.
7. **Cancellation is a first-class flow**, not an admin escape hatch: appointment cancelled +
   deposit refunded via POK → `POST /invoice/cancel` → store the cancellation's own `docId`.
8. **Balance/*arka* stays a deliberate owner action** with an audit record of who triggered
   it. No timers.
9. **Per-tenant easyPos credentials in Vault**, same discipline as POK keys. These are more
   sensitive, not less.
10. **Help must cover it** — `helpContent.ts`, the standalone manual, and a studio-onboarding
    tour step for "connect your easyPos account and configure your services," in the same
    change.

---

## Sources

- [easyPos Public API — Postman collection](https://documenter.getpostman.com/view/21155107/2sBXVkCA3S) (full collection JSON reviewed)
- [easyPos API integration guide](https://easypos.al/api)
- [easyPos — product site, pricing, DPT/AKSHI certification](https://easypos.al/)
- [easyPos Local API — Postman](https://documenter.getpostman.com/view/15718037/UyrDCam4)
- [easyInvoice Local API — Postman](https://documenter.getpostman.com/view/21155107/2sBXqJLLm9)
- [ESDP](https://esdp.al/) · [easyPos help desk](https://help.easypos.al/pyetje-pergjigje)
- [DPT — fiscalization / electronic certificate](https://www.tatime.gov.al/d/8/45/0/1166/ertifikate-elektronike-test-per-fiskalizimin-ne-portalin-e-albania)
- [Law No. 55/2020 "On Payment Services" (English)](https://www.bankofalbania.org/rc/doc/Ligji_Per_sherbimet_e_pagesave_anglisht_18199.pdf) — Article 4(g), unchanged by any of this
