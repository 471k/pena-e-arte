# Payment provider outreach — draft emails (POK & Polar)

Two ready-to-send drafts. Each is grounded in the technical/legal research already in
`docs/payments/` (ADR-0001, `pok-assessment.md`, `market-scan-both-flows.md`) so the questions
match exactly what's still open before either integration can be built. Nothing about internal
architecture (class/interface names, prior implementation issues, revenue, or the fact that a
legal review flagged the old design) is disclosed — only what a vendor needs to answer a
prospective merchant's integration questions.

Fill in the bracketed placeholders (recipient address, your name/title, phone) before sending.
Neither vendor's docs list a verified sales-specific email address — send via each vendor's
official contact channel (POK: contact form at pokpay.io / docs.pokpay.io; Polar: polar.sh
contact form or their sales inbox as listed on their site) rather than a guessed address.

The POK draft leads with a go/no-go question of its own, parallel to Polar's: a meaningful share
of a studio's own clients (the people paying the deposit) will be tourists or otherwise paying
with a foreign-issued card, so whether POK accepts non-Albanian cards at all is asked upfront,
before the rest of the questions.

**2026-09-08 update — checked POK's public docs against every open question.** Reviewed
`docs.pokpay.io` (index, `/react`, `/rest-api`), the consolidated `llms-full.txt` bundle (which
aggregates every SDK guide: React, Vanilla JS, CDN, React Native, Flutter, PHP SDK, WooCommerce,
PrestaShop, plus the REST API staging/auth page), and `docs/react.md`. These are developer SDK
integration guides, not a commercial/business reference — none of the pricing, merchant-country-
eligibility, cardholder-country-eligibility, webhook-signing, MOTO/production-recurring, exact
`splitWith` shape, dispute/chargeback, settlement-timing, or OpenAPI-spec questions below are
answered there, so every one of them is still open and worth asking directly. Two things the
docs *did* confirm, both already reflected in the draft: supported card brands are Visa, Visa
Electron, Mastercard, and Maestro (Q13); and the web SDKs (React/Vanilla JS/CDN) have no
documented low-level 3-D Secure primitive — only React Native and Flutter document one
(`createChallenge`) — which sharpened Q10 rather than resolving it. (The PHP SDK's docs list an
`SdkOrderSplitWith` model class by name with no field-level schema on the pages reviewed, so
`splitWith`'s exact shape — Q2/Q11 — is still unconfirmed from public docs.)

**2026-09-11 update — the POK email below was actually sent, and answered, plus real material
POK/RPay sent by email surfaced.** The email in Section 1 wasn't just a draft — it was sent
(2026-09-07, from Ali Kreku / Phi Software Solutions) and POK's support team (`support@rpay.ai`)
replied in full on 2026-09-10. Separately, real attachments POK/RPay had sent by email at different
points in an ongoing correspondence were reviewed line by line: a dated sample merchant agreement
(`Marrëveshje Bashkëpunimi`, 24.08.2026 — a blank-signatory RPay↔"Biznesi" contract) and RPay's
official business-registry extract, plus (see update #2 below) a commercial sales deck from an
earlier June 2026 exchange. All of this is direct vendor correspondence, not third-party or public
material, and a check for embedded hyperlinks turned up none in any of the files (see the
question-status log below). Between the email reply and these attachments, most of what was open
after the 2026-09-08 docs review is now answered from primary sources rather than inferred from
developer docs. The full text of POK's reply and a per-question status log are appended after the
email below; the Polar email (Section 2) has not been sent and nothing there has changed.

---

## 1. POK Payments — Flow A (client deposit payments), with a Flow B feasibility question

**To:** [POK Payments — Merchant Sales / Partnerships team]
**Subject:** Integration inquiry — split payments for a multi-tenant booking platform (Albania)

Hello,

My name is [Phi], and I'm building **[Pena e Artë / TattooOS]**, a booking and studio-management
platform for tattoo studios, primarily but not exclusively based in Albania. Each studio on the
platform manages its own appointments,
client records, and takes a card deposit from clients at booking time. We're evaluating POK as
our payment provider for these client deposit payments and have a set of questions before we
scope the integration.

**How we'd want to use POK:** each studio would hold its own POK merchant account, and its
clients' deposit payments would settle directly to that studio's account — with our platform
deducting a small platform fee automatically at the time of payment, which we understand
`splitWith` on order creation is designed for. We want our platform to never take custody of a
client's payment at any point, which is an important compliance requirement for us under
Albanian payment-services regulation, so a model where funds move directly from client to studio
(with our fee split off atomically) is exactly the shape we're looking for.

A few things about our use case that are relevant to your team:

- We're a multi-tenant platform — potentially many independent studios, each needing its own POK
  merchant account and credentials, provisioned over time as studios join. Most of these studios
  are based in Albania, but some may be based outside Albania as well.
- A meaningful share of these studios' own clients — the people actually paying the booking
  deposit — are based outside Albania: tourists booking a session while traveling, or clients
  otherwise paying with a foreign-issued card rather than an Albanian one.
- Deposits need an authorize-now/capture-later flow: a client authorizes a hold at booking time,
  and the studio (or an automatic process) captures it later, or releases it if the appointment
  is cancelled within a certain window.
- We'll need refunds (full and partial) as a first-class, API-driven flow, since cancellation
  and rescheduling policies are core to the product.
- Some studios have multiple physical locations under one account.

**The two questions that matter most to us before anything else:**

- **Merchant eligibility:** can POK onboard a merchant (studio) that is itself based outside
  Albania — not just studios registered in Albania? Most of our studios will be Albania-based,
  but some will not be, and we'd like to know upfront whether a non-Albanian studio can hold a
  POK merchant account at all, or whether POK is limited to Albania-registered merchants only.
- **Cardholder eligibility:** separately, can POK process a deposit payment from a client whose
  card was issued outside Albania, paying into an (Albanian or non-Albanian) studio's POK
  merchant account — i.e. is international/foreign-card acceptance supported today? A large
  share of the deposits our studios take will come from clients paying with a non-Albanian card.

Between the two, this determines whether POK can serve as our provider at all, both for our
studio base and for their clients. If either isn't supported today, is it on your roadmap, and
is there any workaround in the meantime (a different card network route, a separate onboarding
tier, a partner arrangement for merchants outside Albania, etc.)?

**Questions — pricing:**

1. What is your merchant discount rate (transaction fee) for card payments and POK-app payments
   settled in ALL? Is pricing tiered by volume, and does the rate differ for a domestic
   (Albanian-issued) card versus a foreign-issued/international card?
2. Is there a separate rate or fixed cost associated with the `splitWith` platform-fee leg of a
   transaction, on top of the base merchant discount rate?
3. What are your fees for refunds and for chargebacks/disputes?
4. What is your standard settlement timing — how quickly do funds land in a merchant's account
   after a captured payment?
5. Is there a setup fee, minimum monthly volume, or minimum monthly fee per merchant?

**Questions — onboarding and platform model:**

6. Can our platform provision or refer a studio's merchant account and credentials
   programmatically (a partner/platform program), or is onboarding always a direct, manual
   process between POK and each individual studio? In practice, how long does it typically take
   a new merchant to go from signup to being able to accept its first live payment?
7. Is there a formal partner/reseller or platform agreement available for a company like ours
   that's onboarding many merchants over time?

**Questions — technical integration:**

8. Are webhook payloads signed in production (a signature header and secret we can verify), or
   should we treat webhooks purely as an unauthenticated notification and always re-fetch order
   status via the API before acting on it?
9. Is there an OpenAPI/Swagger specification for the REST API? Your docs point to
   `payments.doc.pokpay.io` as the full endpoint reference, but we couldn't tell from the public
   pages whether a machine-readable spec is published there or elsewhere. We're building on
   ASP.NET Core (.NET), and since we understand there's no official .NET/C# SDK, a generated
   client from a spec would be very helpful.
10. For a web-based (not native app) checkout: your SDK docs document a low-level 3-D Secure
    primitive (`createChallenge`) for React Native and Flutter, but we couldn't find an
    equivalent for the web SDKs (React, Vanilla JS, CDN) — there, 3DS appears to run only inside
    your pre-built checkout form components (`GuestCheckoutForm` etc.). Can you confirm that's
    accurate today, and if so, is a custom web checkout built with `encryptCard()` and our own UI
    expected to hand off to your hosted form for the 3DS step, or is there an unlisted way to
    handle the challenge ourselves on web?
11. Is `splitWith.userPhoneNumber` intended only for one-off, ad hoc splits to an individual, or
    is it also suitable for a recurring, repeatable commission-split arrangement (e.g. a studio
    splitting a portion of each deposit with a specific staff member every time)?
12. Is there a dispute/chargeback API we can integrate with, or is that handled only through your
    merchant dashboard?
13. Can you confirm current support for the Visa/Mastercard family of card brands, and whether
    ALL settlement to the studio is fully native end-to-end with no forced currency conversion —
    including when the paying client's card was issued in a different country/currency? If a
    foreign-issued card triggers a currency-conversion step, whose exchange rate applies and is
    there an additional fee on top of the merchant discount rate for that transaction?

**One additional question, since it would significantly change our roadmap:**

14. Can a merchant charge a previously-saved card on file in production **without the
    cardholder present** (a merchant-initiated or MOTO-style transaction), for a possible future
    recurring-billing use case on our side? If this is possible, what's the enrollment process,
    and which strong-customer-authentication exemption applies under the Bank of Albania's
    authentication regulation? We've seen a MOTO endpoint listed as staging-only in your public
    documentation and want to understand whether that reflects the current production
    availability.

We'd appreciate a call to walk through this, along with current pricing documentation, a sample
merchant agreement, and sandbox/API credentials so we can begin a technical evaluation.

Thank you for your time — looking forward to hearing from you.

Best regards,
[Phi]
[Title] · Pena e Artë (TattooOS)
[Phone] · [Website]

---

### POK — question-by-question status as of 2026-09-11

Sources: POK support's reply (`support@rpay.ai`, 2026-09-10, to the sent email above); the sample
`Marrëveshje Bashkëpunimi` merchant agreement (24.08.2026); RPay's QKB business-registry extract
(current as of 03/09/2026, confirmed as two identical copies). No hyperlinks were found inside
either PDF or the `.docx` — the PDFs are static government-issued extracts, and the `.docx`'s only
link-shaped relationship is its footer/logo, not a real hyperlink — so there was nothing further
to follow in any of the four files.

**The two questions that matter most — both answered, and the answer changes the plan:**

- **Merchant eligibility — answered, and it's a hard "no" for non-Albania studios.** POK support:
  "POK currently onboards only businesses registered in Albania." The sample agreement makes this a
  contractual signing condition, not just a policy: the Business must "have its registered head
  office in Albania" (Art. 4.1) to access the Services at all. This directly contradicts this
  email's premise that "some [studios] may be based outside Albania" — under what POK actually
  offers today, a non-Albania-registered studio cannot hold a POK merchant account, full stop. This
  needs a product decision (Albania-only studio onboarding for v1 with POK, versus sourcing a
  second provider for non-Albania studios), not just another round of wording.
- **Cardholder eligibility — answered, unrestricted.** No restriction based on the card's country
  of issuance; foreign-issued cards are accepted. POK settles in both ALL and EUR (ALL is the
  default account currency — a studio needs to add a EUR sub-account to avoid conversion), and any
  currency conversion on a foreign card happens on the cardholder's side with no extra POK fee to
  the merchant.

**Pricing (Q1, Q2, Q3, Q5) — mostly answered, from a real sample contract, not POK's final word.**
The sample agreement's rate schedule: 2.5% + 20 Lekë/€0.20 for an online card payment from a payer
not registered on the POK app, 1.7% for a payer who is registered, 2.5% + 20 Lekë/€0.20 for a
physical/POS card payment, 0% to move funds between businesses inside the POK Merchant app, and 0%
to withdraw to the studio's own bank account. No fee is shown for a refund itself; a chargeback that
requires the merchant to refund costs €75 per case (and if the studio's balance can't cover it, RPay
fronts the money as a credit — interest-free for 3 months, then 1yr Euribor+4% in EUR or 1yr
T-bill+3.5% in ALL). Installation is free and no minimum monthly volume/fee appears in the template.
Not shown: whether pricing is tiered by volume (no tiering in this template), and whether this
specific rate card is TattooOS-specific or POK's generic published rate — POK's own reply says
"detailed pricing... require[s] separate confirmation," so treat this as a strong reference point,
not a final quote.

**Settlement timing (Q4) — clarified, not fully answered.** It's a pull/on-demand model: the studio
can withdraw its balance to its bank account "at any moment," and the transfer follows "normal
banking timeframes" (RPay disclaims bank-side delays). No SLA in days is stated anywhere in these
documents — POK's reply also lists this among the items needing separate confirmation.

**Platform/reseller model (Q6, Q7) — effectively answered, in the negative.** The sample agreement
is strictly bilateral (RPay ↔ one "Biznesi"); nothing in it accommodates a platform provisioning
credentials for many merchants. This matches POK support's "onboarding is a manual process that each
business must complete individually" — there does not appear to be a partner/platform program today;
each studio would sign this same agreement directly with RPay.

**Technical integration (Q8–Q13) — answered by POK support's reply, not by these files (the
agreement is a commercial/legal contract, not a technical spec):** webhooks aren't guaranteed
signed — always re-fetch order status via the API before acting on a notification (Q8); no
OpenAPI/Swagger spec is published — POK points to `https://docs.pokpay.io/rest-api`, "including the
instructions at the bottom of the page" (Q9; re-checked directly per the follow-up below); web
checkouts must use POK's own UI for the 3-D Secure step — there's no way to run 3DS in a fully
custom web UI today (Q10); `splitWith.userPhoneNumber` applies per-order only — a recurring
commission split must be re-specified on every order, there's no persistent/repeatable
configuration (Q11); there is no dispute/chargeback API, only the merchant dashboard (Q12); card
brand support wasn't re-confirmed in this round but was already confirmed from public docs
(Visa/Visa Electron/Mastercard/Maestro) — the currency side of Q13 is answered above under
cardholder eligibility.

**MOTO / merchant-initiated payments (Q14) — still open.** POK's reply explicitly lists "production
availability of merchant-initiated/MOTO payments" among the items requiring separate confirmation.
Genuinely open, not just undocumented.

**Regulatory status of RPay — a related open item, now closed.** RPay's own merchant agreement
states it is registered by the Bank of Albania under **license No. 50, dated 9 August 2021, as an
Electronic Money Institution** — this confirms the "RPay is a BoA-licensed EMI" assumption in
ADR-0001 with a specific license number, straight from RPay's own contract (not yet cross-checked
against the BoA's public register directly).

**Net effect:** of the original 14 numbered questions plus the two eligibility questions, only Q14
(MOTO) and the fine print of Q4 (exact settlement SLA) and Q1/Q5 (whether this rate card is final
and whether it's tiered) remain genuinely open — and POK has said as much itself. The one item that
isn't just "still open" but actively changes the plan is merchant eligibility: this draft, and the
underlying multi-tenant design, currently assume a studio outside Albania could get its own POK
merchant account, and that's now confirmed false.

**2026-09-11 update #2 — a real POK sales deck (`POK_Prezantim.pptx`, "CONFIDENTIAL · June 2026"),
sent by POK's commercial team by email during an earlier June 2026 exchange, replaces the
accidental duplicate-PDF upload and adds a few things.** Four slides, no embedded hyperlinks in
any of them. New findings:

- **Q6 (onboarding timeline) — now answered with a concrete number.** POK's own standard rollout
  plan is **go-live within 7 days**: days 1–3 onboarding/KYB + signing the commercial contract +
  dashboard access, days 3–5 technical installation (POS terminals, website/system integration, QR
  activation, staff cards), days 5–7 training and go-live with on-site support. That's for a single
  direct merchant, consistent with everything else confirming there's no platform-provisioning path
  — each studio would go through this same 7-day process individually.
- **Pricing — the "standard" rate card matches the sample contract exactly, which raises a
  question rather than closing one.** Slide 3 is an annex of POK's "standard" rates: 2.5% +
  €0.20/20 Lekë for POS/QR/web-gateway/non-POK pay-by-link, 1.7% for a POK-app-registered payer —
  identical to the figures in the sample merchant agreement reviewed in update #1. But the deck's
  own text says "our offer for your business on the previous page is more favorable than this in
  every line" — and no such offer page is actually present in this file (slide 2, the page before
  the annex, is a "why POK" services overview with no numbers on it). So either the sample contract
  we have already reflects a discount that happens to equal the standard rate, or the actual
  discounted offer was never sent to us and needs to be asked for directly. Worth a direct follow-up
  question rather than assuming the numbers in hand are final. Two new data points not seen before:
  IBAN/SEPA Instant transfers at €1.5/transfer, and a business staff card at €16/year (a different
  figure from the Lekë-denominated card fees in the sample contract — currency/product mismatch, not
  yet reconciled).
- **Q14 (MOTO) — a new signal, not a confirmation.** The deck lists "MOTO Payments — pagesa me
  numër karte nga distanca" (remote card-number payments) as one of POK's core commercial services
  on the same slide as QR, Pay-by-Link, and SEPA Instant, with no staging-only caveat. That cuts
  against the "MOTO is staging-only" impression from the public docs, but POK support's own reply
  still lists MOTO's production availability as something requiring separate confirmation — treat
  this as supportive, not decisive.
- **A real commercial contact, with a caveat on timing.** "Ekipi Komercial i POK" (POK's Commercial
  Team): `Amalushi@rpay.ai`, +355 68 402 9649. The deck states it's "valid for 60 days from June
  2026" — that window has already passed as of this update, so any pricing or terms in it should be
  reconfirmed as current before being relied on, not assumed to still be on offer.
- Context, not directly question-answering: POK states it's been BoA-licensed since 2021, one of
  the first EMI-licensed entities in Albania, aligned with EU PSD-style regulation, with 50,000+
  monthly transactions, 1,700+ registered merchants, and 6,000+ active issued cards platform-wide.

---

## 2. Polar — Flow B (platform subscription billing, merchant of record)

**To:** [Polar — Sales]
**Subject:** Merchant-of-record inquiry — SaaS subscription billing for an Albania-based platform

Hello,

My name is [Phi], and I run **[Pena e Artë / TattooOS]**, a subscription-based SaaS platform for
tattoo studios. Studio owners pay us a recurring monthly or yearly subscription for platform
access, across a small number of pricing tiers. We're based in and operate from Albania, and
we're evaluating Polar as merchant of record for this subscription billing, so that VAT/sales-tax
determination, remittance, and buyer-facing invoicing are handled by you rather than by us
directly.

**The one question that matters most to us before anything else:** we understand Polar's payouts
run through Stripe Connect Express. Could you confirm whether Stripe Connect Express currently
accepts an Albania-registered sole proprietor ("Person Fizik" / individual business type,
equivalent to a sole trader) for payout purposes? We understand there's a documented
eligibility-check process on your side for exactly this — could you either point us to it or
confirm the outcome directly for an Albanian individual entrepreneur? This determines whether we
can proceed with Polar at all, so we'd like to resolve it before investing further time in
integration planning.

**Questions — pricing:**

1. What is your current transaction fee structure (percentage + fixed fee) for subscription
   payments? We've seen your pricing has moved toward tiered plans recently — could you send us
   your current rate card?
2. Are there separate fees for payouts, currency conversion, or handling disputes/chargebacks?
3. Is there a monthly platform or account fee in addition to the per-transaction rate?
4. What currencies can we bill our subscribers in, and what currency (or currencies) can we
   receive payouts in?

**Questions — subscription lifecycle (please confirm which of the following your API
supports natively):**

5. A hosted checkout page for starting a new subscription, where we can attach our own reference
   ID to the session so we can match a completed checkout back to the correct customer record on
   our side.
6. Collecting a payment method up front but deferring the first actual charge to a specific
   future date we specify (used when a customer has already paid for an initial period through
   another channel and we want their card on file for renewal only).
7. Changing a subscription to a higher-priced tier immediately, with the price difference
   prorated and charged right away.
8. Scheduling a subscription's tier change to take effect only at the end of the current billing
   period (for downgrades), without an early or partial charge — and the ability to cancel a
   pending scheduled change before it takes effect.
9. A hosted customer self-service portal where a subscriber can update their payment method,
   view or download past invoices, and cancel their own subscription.
10. Applying a one-time discount or coupon to a subscription that's already active and being
    billed (we run a referral program where an existing customer gets a free month when someone
    they refer becomes a new paying customer).
11. Webhook events (ideally signed) covering: checkout completed, invoice paid, subscription
    updated (status, price, and current-period-end changes, including a "will not renew" flag),
    and subscription canceled/deleted.

**Questions — tax, onboarding, and support:**

12. As merchant of record, do you determine and remit VAT/sales tax on our behalf globally? What
    tax documentation does a subscriber receive, and what do we receive for our own bookkeeping?
13. What information and documents will you need from us as an Albania-registered business to
    complete onboarding, and what's a realistic end-to-end timeline?
14. Is there a full sandbox/test-mode environment available before going live?
15. Is there a dedicated contact or support channel during integration, separate from general
    customer support?

We'd welcome a call to go through this, along with your current pricing documentation and
sandbox/API access so we can begin technical evaluation once the Stripe Connect Express
eligibility question above is resolved.

Thank you — looking forward to your response.

Best regards,
[Phi]
[Title] · Pena e Artë (TattooOS)
[Phone] · [Website]
