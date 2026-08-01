# Implementation readiness — what's required to execute ADR-0001

Prepared 31 July 2026. Companion to `ADR-0001-payment-providers.md`.
Not legal or tax advice — several items below need your accountant or a lawyer.

---

## The headline

**Almost none of the remaining lead time is code.** Integrating POK, Polar and easyPos is
maybe three to four weeks of engineering. Getting *approved* by them is six to ten weeks of
paperwork, and every one of them requires a live website with real policy pages before they
will even look at you.

So the sequencing is inverted from how it feels: **the website and the applications come
first, the payment code comes second.** If you build the integrations first you will sit on
finished code waiting for credentials.

---

## 1. The gate in front of everything

**A live, public, HTTPS site with real content and four policy pages.**

Every PSP and MoR reviews your site as part of onboarding, and the single most common cause of
rejection for Albanian applicants is a thin site or missing policies. You need:

- [ ] Real domain, HTTPS, real content describing what Pena e Artë is
- [ ] **Terms of Service**
- [ ] **Privacy Policy** (must survive Law 124/2024 scrutiny — see §6)
- [ ] **Refund / cancellation policy** — explicit about deposits, no-shows and who refunds what
- [ ] **Contact page** with a real address and a working channel
- [ ] Pricing visible, or at least a clear description of the commercial model

This is also a prerequisite for the DPT/AKSHI side and for studio trust. Do it first.

---

## 2. Entity — the decision you have to make before applying

You are registered as a **Person Fizik**, NIPT M12219042B, activity *"Shërbime zhvillimi
software-ike"*. Three questions, and the answers may point at converting to an **SH.P.K.**

**a) Does your registered activity cover this?** Writing software is one thing. Operating a
multi-tenant platform, holding studios' payment and fiscal credentials, and — if you enable
`splitWith` — **taking a percentage of payment volume** is arguably a different activity.
Ask your accountant whether the activity code needs extending, and specifically whether a
platform fee taken as a share of transaction value is characterised as a service fee (fine) or
as something payment-adjacent (a problem).

**b) Unlimited personal liability.** A Person Fizik has no liability shield. You would be
personally exposed for: a data breach involving studios' clients' **health data**, mishandled
fiscal credentials, a studio's chargeback dispute, and a Law 124/2024 fine (see §6 — the
maximum is enormous and enforcement is rising sharply). For a solo consultancy that risk is
theoretical. For a multi-tenant SaaS holding special-category data it is not.

**c) Counterparty acceptance.** POK, Polar and Stripe Connect Express all run KYC. Some accept
individuals; some only accept companies. Polar's own docs give exact steps to check whether
Stripe Connect Express supports the *individual* business type for Albania — **that check is
free and takes ten minutes, and it may decide the entity question for you.**

- [ ] Run the Stripe Connect Express business-type check for Albania (Polar's documented steps)
- [ ] Ask your accountant: activity code, and Person Fizik vs SH.P.K. for this business
- [ ] Decide entity **before** submitting any PSP application — re-KYC after a conversion is
      weeks of lost time

**VAT:** registration is mandatory once rolling 12-month turnover exceeds **ALL 10,000,000**
(~€96,200), measured on any rolling 12-month window, with 15 days to register after crossing.
Below that you may register voluntarily. Worth modelling now, because your invoicing and your
pricing display change the day it happens.

---

## 3. Documents to produce

Beyond the public policies:

- [ ] **Studio Services Agreement** — must cover: the studio is the merchant of record for
      client payments; who bears chargebacks and refunds (them); that you hold their POK and
      easyPos credentials as a technical service provider; data ownership and export on exit;
      liability allocation for fiscal filings.
- [ ] **Data Processing Agreement** with each studio — you are their **processor** for client
      data, and a **controller** for their own account data. This is mandatory under
      Law 124/2024, not optional.
- [ ] **Records of processing** (GDPR Art. 30 equivalent) — write it once, keep it current.
- [ ] **Client consent form wording** covering health data and any photography, reviewed by
      someone who knows Albanian data law.
- [ ] **Sub-processor list** — POK, easyPos, Polar, Cloudflare R2, Resend, Twilio, your host.
      Studios have a right to know.

---

## 4. Accounts to open, with realistic lead times

Start all of these **in parallel, now.** They are the critical path.

| # | What | Lead time | Blocks |
|---|---|---|---|
| 1 | **Stripe Connect Express eligibility check** (AL, individual) | minutes | The whole Flow B decision |
| 2 | **Polar** account + KYC | 1–3 weeks | Flow B build |
| 3 | **POK** merchant account, staging credentials, partner conversation | 2–6 weeks | Flow A build |
| 4 | **easyPos** account + API token (`integration-app` identifier) | 1–3 weeks | Fiscalization build |
| 5 | **Business bank account**, ALL + EUR | 1–3 weeks | Payouts, SEPA |
| 6 | *(optional)* BKT conversation re MSU Recurring/Split | 1–2 weeks | Nothing — but could reverse ADR-0001 |

**What POK will want from you:** QKB extract, NIPT, ID, proof of address, live site with
policies, a written description of the business model and expected volumes. Have it in one
folder before you email.

**Ask POK explicitly, in the first email:**
- Production MIT / unattended charging on stored tokens — available or not?
- Is there a partner/platform programme for provisioning studio merchants?
- Are webhooks signed? If so, what header and what rotation process?
- Pricing: MDR, the `splitWith` leg, refunds, chargebacks, settlement timing.

**Ask ESDP (easyPos) explicitly:**
- Per-tenant API tokens — issued programmatically, or one at a time by a human?
- Is re-posting the same `docId` idempotent?
- Is a **cloud** easyInvoice / e-Fatura API on the roadmap?
- Reseller/representative terms.

---

## 5. Technical prerequisites

**The non-obvious one: you need a deployed, publicly reachable HTTPS staging environment
before you can integration-test anything.** POK and Polar both call back to a webhook URL.
`localhost` will not work. This is a prerequisite, not a later milestone.

- [ ] Staging environment deployed, public HTTPS, stable URL for webhook endpoints
- [ ] **Vault** (or equivalent) live before you hold a single studio credential — CLAUDE.md
      rule 4, and it is what keeps you inside Article 4(g)
- [ ] Hangfire configured with durable storage for fiscalization retries and billing cycles
- [ ] Structured logging with `tenant_id` / `user_id` / `request_id` and **no PII** — you will
      be tempted to log payment payloads. Don't.
- [ ] Secrets rotation runbook — what happens when a studio's POK key leaks
- [ ] **PCI: stay at SAQ-A.** Card data must never touch your infrastructure or your forms.
      Use POK's hosted form or `encryptCard()`. Confirm your checkout choice keeps you there
      before you build it.

---

## 6. Compliance — and one thing you may not have priced in

**Albania replaced its data protection law.** Law **124/2024** was promulgated January 2025,
in force **1 February 2025**, and repealed Law 9887/2008. It is fully aligned with GDPR and
the Law Enforcement Directive. Maximum administrative fine is the GDPR-style upper tier —
**up to 4% of total annual worldwide turnover**, and enforcement is escalating: six fines were
issued in the first two months of 2026, three times the whole of the previous year.

**The part that matters for this product specifically: your digital consent forms process
special-category health data.** Allergies, medications, pregnancy, skin conditions,
blood-borne conditions — plus a body map and, presumably, photographs. Under a GDPR-aligned
regime that is Article 9 data, and it changes your obligations materially:

- [ ] **DPIA** — near-certainly required for large-scale processing of health data. Do it
      before launch, not after.
- [ ] **DPO** — required where there is large-scale processing of special-category data.
      Check the threshold against your projected scale; you may need to appoint one, and as a
      solo founder that means an external appointment.
- [ ] **Explicit consent** for health data, separately captured and separately withdrawable —
      not bundled into the booking terms.
- [ ] **72-hour breach notification** process, written down, before you need it.
- [ ] Retention policy — how long consent forms and body maps are kept, and what deletes them.

This is the single most underestimated item on this page. It is a bigger compliance surface
than the payments work, and it is *already* in your v2 feature scope.

**What you are not:** you are not an AML-obliged entity, because you are a technical service
provider that never possesses funds. Keep it that way and this stays true.

---

## 7. Sequenced plan

**Week 0–1 — unblock everything**
1. Stripe Connect Express eligibility check for Albania (free, 10 min) → decides Flow B path
2. Verify RPAY sh.p.k. on the BoA EMI register (5 min)
3. Accountant: entity decision, activity code, VAT modelling
4. Start writing the four policy pages

**Week 1–3 — applications out**
5. Site live with policies
6. Apply: Polar, POK, easyPos. Business bank account.
7. Send the explicit question lists in §4
8. Call BKT re MSU Recurring/Split; ask your bank re SEPA Direct Debit

**Week 2–4 — build the parts that need no credentials**
9. Deploy staging with public HTTPS + Vault
10. `IPaymentProvider`, `ISubscriptionBillingProvider`, `IFiscalizationProvider` with
    capability flags; `BillingMandate`; the architecture test that forbids a platform balance
11. Domain model for orders, deposits, holds, fiscal records — **including `splitWith` at 0%**
12. Cash / record-only path end to end. This ships without any provider.

**Week 4+ — integrate as credentials land**
13. POK staging → deposits, auth-then-capture, refunds, webhooks-as-triggers
14. easyPos dev (hits the DPT test system) → fiscalize on settled-payment event
15. Polar sandbox → subscriptions, dunning, webhook signature verification
16. Reconciliation, then Help content in the same changes

**Before launch**
17. DPIA, DPO decision, breach runbook, retention policy
18. Studio agreement + DPA reviewed by a lawyer
19. Live smoke test with a low-value real transaction on each provider

---

## 8. What only you can decide

- **Entity**: Person Fizik or SH.P.K. Everything else waits on this.
- **Monetization**: subscription-led (as per ADR-0001) or the Fresha-style payments-first
  model. Build `splitWith` at 0% either way so the option stays open.
- **Checkout UI**: POK's hosted form (fast, visual seam) or `encryptCard()` with your own form
  (on-brand, but you own 3DS orchestration on web with no documented helper).
- **First-customer strategy**: which two or three studios do you hand-sell? Their bank and
  their existing processor will tell you immediately whether the bank-VPOS off-ramp is
  theoretical or urgent.

## 9. What you need someone else for

| Need | Who |
|---|---|
| Entity, activity code, VAT, fiscal treatment of deposits | Albanian accountant |
| Studio agreement, DPA, consent wording, liability allocation | Albanian lawyer with data-protection experience |
| DPIA and DPO threshold under Law 124/2024 | Same lawyer, or a privacy consultant |
| Confirmation that Article 4(g) covers a non-custodial booking platform | Payments lawyer — get it in writing; it is cheap insurance and a sales asset with studios |

---

## Sources

- [Law No. 124/2024 on Personal Data Protection (IDP Albania, PDF)](https://idp.al/wp-content/uploads/2025/04/Law-no.124-2024-DP.pdf) · [IAPP analysis](https://iapp.org/news/a/albania-s-personal-data-protection-law-a-legal-framework-harmonized-with-the-gdpr) · [KPMG Albania briefing](https://kpmg.com/al/en/insights/2025/02/new-law-on--personal-data-protection-.html) · [EY — the Commissioner's evolving enforcement role](https://www.ey.com/en_al/insights/law/albania-data-protection-commissioner-under-the-new-data-protection-law) · [CMS Expert Guide — Albania](https://cms.law/en/int/expert-guides/cms-expert-guide-to-data-protection-and-cyber-security-laws/albania)
- [PwC — Albania, other taxes (VAT)](https://taxsummaries.pwc.com/albania/corporate/other-taxes) · [Albania VAT guide 2026](https://sherbimekontabiliteti.al/en/albania-vat-guide-2026/)
- [Law No. 55/2020 "On Payment Services"](https://www.bankofalbania.org/rc/doc/Ligji_Per_sherbimet_e_pagesave_anglisht_18199.pdf) — Article 4(g)
- [BoA — EMI register](https://www.bankofalbania.org/Supervision/Licensed_institutions/Electronic_Money_Institutions/)
- [Polar — supported countries and Stripe Connect Express business-type check](https://polar.sh/docs/merchant-of-record/supported-countries)
- [POK Payments docs](https://docs.pokpay.io/) · [easyPos API](https://easypos.al/api)
