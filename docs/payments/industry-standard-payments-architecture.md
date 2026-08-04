# Industry standard: payments architecture for vertical booking SaaS

Prepared 31 July 2026. Written against CLAUDE.md rule 6 — the benchmark set is
Vagaro, Fresha, Boulevard, Mindbody, Zenoti, GlossGenius, plus Booksy as the closest
structural analogue (Poland → global, appointment-led, SMB, initially non-US market).

---

## The short version

**The industry standard is one embedded payment provider, platform-branded, with the
platform taking a spread — not a menu of processors.** Multi-processor support is the
*legacy* pattern that this industry has spent the last five years actively consolidating
away from.

Which means my earlier "POK primary, bank VPOS as an escape hatch" recommendation is
defensible on Albanian market grounds but is **not** what the benchmark set does. Worth
correcting the framing before it hardens into architecture.

---

## 1. What the benchmark set actually does — Flow A

| Platform | Model |
|---|---|
| **Fresha** | **Fresha Payments only.** Closed ecosystem, no third-party processor. 2.19% + $0.20. Software is free; payments *are* the business model. |
| **GlossGenius** | Own payments only. Flat 2.6%, no per-transaction cents fee. |
| **Vagaro** | Vagaro payments, 2.45–2.75% card-present. |
| **Boulevard** | Boulevard Payments, proprietary front end. |
| **Mindbody** | Built **Mindbody Payments on Stripe Connect**, and from 2020 **consolidated its payment systems onto Stripe**. Retains *some* region-specific processors for Europe/APAC/UAE. |
| **Booksy** | **Switched to Stripe** — Stripe Payments + Stripe Connect, one API across all regions. |
| **Zenoti** | Zenoti Payments, but **a separate Zenoti Payments account per country**, plus regional integrations (e.g. Payment Express in AU/NZ). |

Three things jump out.

**a) Every one of them has a first-party, platform-branded payments product.** Not "connect
your Stripe account." Not "choose your gateway." *Their* payments, inside *their* product,
under *their* brand.

**b) Consolidation is the direction of travel, and it's recent.** Mindbody explicitly
consolidated onto a single stack in 2020. Booksy explicitly switched to Stripe. Neither added
processors — both removed them.

**c) The only reason anyone keeps multiple processors is geography.** Zenoti runs a separate
account per country. Mindbody keeps regional processors where its primary can't reach. Nobody
in this set offers a *choice* of processor within one market — they run whatever single
provider works in each market, and the merchant doesn't pick.

**That last point is the one that matters for you.** Multi-provider in this industry is a
coverage mechanism, not a customer-choice feature. If you end up supporting POK and bank VPOS,
that should be because they reach different studios — not because you're offering a menu.

---

## 2. The business model the standard is built on

This is the part that reframes the whole POK decision.

Embedded payments in vertical SaaS is not a convenience feature — it is the **primary revenue
line** for a growing share of the benchmark set. The published economics:

- **a16z**: vertical SaaS platforms embedding financial services grow **revenue per customer
  by 2–5×** without adding users or products.
- **Rainforest's 2026 Vertical SaaS Embedded Payments Benchmark**: nearly half of vertical SaaS
  platforms run take rates **above 90 basis points**. A platform at a "modest 30 bps" is
  described as leaving money on the table.
- **BCG + Adyen (2025)**: embedded payments retain customers at **2.5×** the rate of
  traditional payment providers.
- **Visa** (five European markets): verticalised acquirers see **+19 percentage points** payment
  volume growth and **5% less merchant attrition**.

Three monetization models are standard:

1. **Referral / revenue share** — you refer merchants to a partner, take 10–30% of their net
   margin. Lowest lift, lowest margin, and you don't own the merchant relationship or data.
2. **Markup / take rate** — you set the sell price, keep the spread over your buy rate.
   *Described as the optimal model for most vertical SaaS platforms.*
3. **Bundled** — merchants pay a higher processing rate that covers the software fee. Works
   where payments are deeply integrated and merchants value simplicity over rate shopping.

And the guardrail: **do not become a registered PayFac.** The consensus is that PayFac
operating costs can be ~10× working with an embedded-payments provider and consume most of the
payments revenue. For you this is doubly true — a BoA payment-institution licence is out of
reach anyway.

### Why this makes POK's `splitWith` strategically important, not just convenient

`splitWith` is the mechanism that lets you run model 2 or 3 at all. Without an atomic
platform fee at payment time, you're stuck in model 1 (or invoicing studios separately and
chasing it), which the industry considers the weakest position — no pricing control, no
merchant ownership, structurally hard to escape later.

So the honest reframe: **POK isn't just the nicest Albanian API. It's the only Albanian
provider found that lets Pena e Artë run the standard vertical-SaaS payments business model.**
That's a much stronger argument for it than "good sandbox."

---

## 3. Where my earlier recommendation diverges from the standard

I said: *"Keep bank VPOS behind the same abstraction for large studios that already have a
merchant agreement and won't move."*

The industry's own framing of that exact situation:

> **"We already have a processor" is the most common objection when rolling out embedded
> payments. And competing on rate alone almost always fails.**

The standard playbook is *not* to accommodate it. It's to compete on **operational friction** —
separate logins, manual reconciliation, disconnected reporting, slower support — and to time
the conversation for the **60–90 day window before their existing processing agreement
renews**, when they're already reviewing terms.

There's also a metric that argues against accommodating it. **Attach rate** — the share of new
customers who take payments at signup — is treated as the metric to maximise from day one,
because *"every customer who doesn't sign up for payments when they onboard becomes a backbook
adoption challenge later."* Offering a "keep your own processor" option at onboarding is a
direct, permanent hit to attach rate.

**Corrected position:** bank VPOS is a **conversion off-ramp**, not a supported tier. Build it
if and only if a named studio you actually want is blocked on it, treat it as temporary, and
work to migrate them. Do not put it in the onboarding flow as a choice, do not put it on the
pricing page, and do not commit to feature parity with the POK path.

---

## 4. Flow B — the standard is unambiguous

Nobody in the benchmark set runs two subscription billing stacks. It isn't a debated question.
One billing provider, plus a manual/offline path for customers who won't pay by card. That is
exactly what I described in the previous answer, and it matches.

The one genuinely standard nuance: mature vertical SaaS increasingly **de-emphasises the
software subscription entirely** in favour of payments revenue.

---

## 5. The strategic option this raises for Albania

Fresha is the extreme version: **free software, monetise payments.** Given the earlier finding
that Albania has the lowest minimum wage in Europe, and that Albanian SMBs are sharply
price-sensitive, this deserves serious thought:

- A €30/month software fee is a hard sell to a two-artist studio in Tirana.
- A take rate on deposits the studio is already collecting is close to invisible — it comes out
  of money moving anyway, and it scales with the studio's success rather than taxing them
  before they've earned anything.

If most revenue came from a Flow A take rate, then **Flow B shrinks from "the thing that pays
the bills" to "a small fixed fee, or nothing at all"** — and with it, most of the problems in
the earlier memos: the MoR fee comparison, the recurring-billing question, the SEPA Direct
Debit unknown, and the unsolved B2B e-Fatura problem, which only exists because you invoice
studios for software.

That is not a decision to make from a research memo. But the payments-first model is the
industry direction, it fits this market's price sensitivity unusually well, and it makes the
POK `splitWith` capability the load-bearing element of the whole product — which is worth
knowing *before* you finalise the payment abstraction, not after.

---

## 6. Standard metrics to instrument from day one

The benchmark set tracks four. Build them into the issuer/admin dashboard now, not later:

| Metric | Definition |
|---|---|
| **Attach rate** | % of *new* studios that enable payments at signup |
| **Adoption rate** | % of *all eligible* studios actively using payments |
| **Net payments revenue** | after interchange, scheme fees, and your buy rate |
| **Take rate** | net payments revenue ÷ total payment volume, in bps |

Two more that the standard treats as product levers rather than support costs: **dispute
handling** (real-time visibility, fast evidence submission, clear status — merchants judge you
on how this *feels*) and **deposit/payout speed** (something merchants actively shop for).

---

## 7. Corrected recommendation

| | Standard | Pena e Artë |
|---|---|---|
| Flow A provider count | **One**, platform-branded | **POK**, branded as Pena e Artë payments |
| Multi-provider | Only for **geographic coverage**, never as customer choice | Bank VPOS = conversion off-ramp, not a tier |
| Monetization | **Markup / take rate**, ~90+ bps | `splitWith` — build it in from day one, even at 0% |
| PayFac | **Don't** | Not an option anyway; POK is the licensed party |
| Cash | n/a in benchmark markets | **Always on** — market reality, not a deviation |
| Flow B billing stacks | **One** + manual path | One MoR + invoice/transfer |
| Attach rate | Maximise from day one | Payments in the onboarding flow, not a settings page |

The abstraction advice from the previous answer stands — `IPaymentProvider`, per-tenant
credentials, capability flags. What changes is the **intent**: it exists so you can survive
losing a provider (one Albanian EMI lost its licence five months ago) and so you can reach
studios POK can't, **not** so studios can shop for a processor inside your product.

---

## Sources

- [Rainforest — How to monetize payments for SaaS platforms](https://www.rainforestpay.com/blog/monetize-payments-for-saas-platforms-a-revenue-guide) (take-rate benchmarks, monetization models, PayFac cost, attach/adoption metrics, "we already have a processor" playbook)
- [Rainforest — 2026 Vertical SaaS Embedded Payments Benchmarking Study](https://www.rainforestpay.com/blog/2026-vertical-saas-embedded-payments-benchmarking-study)
- [a16z — Fintech scales vertical SaaS](https://a16z.com/fintech-scales-vertical-saas/) (2–5× revenue per customer)
- [Apideck — Embedded finance for vertical SaaS](https://www.apideck.com/blog/embedded-finance-vertical-saas) · [Vertical SaaS payouts: why Stripe Connect isn't always the answer](https://www.apideck.com/blog/vertical-saas-payouts-stripe-connect)
- [Payabli — Integrated vs embedded payments for vertical SaaS](https://payabli.com/integrated-vs-embedded-payments-whats-best-for-your-vertical-saas/)
- [Stripe — Behind the scenes: how Mindbody built its integrated payments platform](https://stripe.com/customers/mindbody) (2020 consolidation onto Stripe Connect)
- [Stripe — Booksy switches to Stripe](https://stripe.com/customers/booksy)
- [Zenoti — set up Zenoti Payments](https://help.zenoti.com/en/configuration/zenoti-payments-configurations/set-up-zenoti-payments/set-up-zenoti-payments-for-your-business.html) · [supported gateways and terminals](https://help.zenoti.com/en/integrations/payment-gateways,-services,-and-terminals-supported-by-zenoti.html) (separate account per country)
- [Mindbody — payment processing](https://www.mindbodyonline.com/business/payments)
- [The Salon Business — best salon software guide 2026](https://thesalonbusiness.com/best-salon-software/) · [Merchant Insiders — best payment processor for salons 2026](https://merchantinsiders.com/blogs/best-payment-processor-for-salons/) (Fresha/Vagaro/GlossGenius rates)
