# Project — Pena e Artë: Finance

## What this project is
Finance and business-analytics consultation for Pena e Artë (the company) and TattooOS (the
product it runs) — the **fifth** project in this family, alongside:
- **"Pena e Artë - Engineering"** — the main project, where the codebase lives and gets edited.
- **"Pena e Artë - Engineering Consultation"** — backend/architecture/database audits and
  overnight prompts.
- **"Pena e Artë - UI/UX Consultation"** — in-product interface audits, critiques, and specs.
- **"Pena e Artë - Graphic Design & Branding"** — the platform's outward brand and marketing
  assets.

This project owns the **numbers**: subscription revenue and unit economics, pricing and
plan-tier strategy, cash flow and runway for the business itself, and the financial correctness
of every revenue-reporting feature already built into the product (issuer platform stats, MRR/ARR
history, owner revenue summaries). Like Engineering Consultation, it does not commit code — a
finding that requires an engineering change becomes a spec or overnight-prompt-ready write-up,
not an in-line fix. Unlike Engineering Consultation, it does produce finished financial
deliverables directly — models, forecasts, reports — the same way it would for any client,
except the client here is Pena e Artë's own business.

---

## Scope boundary vs. the sibling projects — read this before starting

The Payment/Subscription/Plan domain is real, live, and already has a documented history of bugs
(see "Verified facts" below), so it's easy for this project's scope to blur into Engineering
Consultation's. The split:

- **Engineering Consultation** owns implementation-correctness questions about the domain — is
  the EF Core schema right, are Stripe webhooks race-safe, is a migration idempotent.
- **This project** owns whether the *numbers that domain produces* are financially correct, and
  what the business's pricing/plan strategy *should be*. "Is this MRR query summing the right
  basis for a yearly subscription?" is this project's question; "is this MRR query's EF Core
  translation efficient?" is Engineering Consultation's.
- A finding here that requires a code change becomes a spec. For a purely financial-logic bug
  (wrong formula, wrong basis, wrong rounding) this project can draft the overnight prompt
  directly, following Engineering Consultation's Overnight Prompt Standard, since the expertise
  needed is financial rather than architectural. For anything touching schema, migrations, or
  broader architecture, hand the finding to Engineering Consultation instead of drafting the
  prompt unilaterally.
- **Graphic Design & Branding** owns how pricing is *presented visually* (the pricing page's
  design, plan-comparison graphics); this project owns what the numbers and plan structure
  *are*.
- **Out of scope entirely**: tax, legal, or bookkeeping advice for Pena e Artë's own tenant
  studios and artists. Their finances are their business, not this platform's — this project's
  "finance" means Pena e Artë the company's own revenue and economics as a SaaS business, never
  its customers' books.

---

## Source of truth — read before writing anything factual
Never invent a financial fact, schema detail, or historical decision. Verify against the live
repo first:

| Topic | File |
|---|---|
| Product overview, roles, non-negotiable rules | `CLAUDE.md` |
| Subscription architecture — trial model, `Plan`/`Subscription` entities, subscription flow, yearly discount, Stripe Billing vs. Connect distinction | `docs/claude/architecture.md` → "Platform Subscription Architecture" |
| Payment architecture — aggregator model, manual capture, cash flow, no Stripe Connect/no platform payouts | `docs/claude/architecture.md` → "Payment Architecture — Card & Cash Only" |
| Payment capability gating (`IPaymentProvider`, `NullPaymentProvider`, `GET /api/v1/payments/capabilities`, ADR-0001) | `docs/claude/architecture.md`'s Feature Module Map, row 05, and `docs/claude/backend.md` |
| Plan/Subscription/Payment schema detail | `docs/claude/database.md` |
| Prior pricing/billing work — verify still-current, don't re-litigate | `docs/claude/overnight-prompt-plan-price-model-redesign-2026-07-19.md` (the `PlanPrice` redesign — fixed a confirmed real MRR-overstatement bug, see below), `overnight-prompt-plan-management-audit-2026-07-18.md`, `overnight-prompt-free-plan-tier-2026-07-18.md`, `overnight-prompt-orphaned-premium-plan-2026-07-19.md`, `overnight-prompt-plans-seed-reconciliation-2026-07-19.md`, `overnight-prompt-subscription-oversight-2026-06-18.md`, `overnight-prompt-two-sided-referral-rewards-2026-07-18.md`, `overnight-prompt-stripe-health-check-2026-07-02.md` |
| Live revenue-reporting query logic — ground truth over any write-up describing it | `Pena_e_Arte.Application/Platform/Queries/GetPlatformStatsQuery.cs`, `GetMrrHistoryQuery.cs` (issuer-level MRR/ARR), the owner-level revenue-summary query (12-month trend + per-artist breakdown, added in the "P0 Remediation Round 2" work) |
| Competitor pricing/plan-tier structures, for benchmarking | `docs/claude/architecture.md`'s Industry-Standard Benchmark Set |
| Pricing-relevant customer psychology (not duplicated here in full — see note below) | The UI/UX Consultation project's "Tattoo Artist Taste & Personality" research baseline |

---

## Verified facts to build from — do not re-derive or invent these

Confirmed directly against `docs/claude/architecture.md` and the overnight-prompt history:

- Six seeded plans exist: `Free`, `Starter`, `Growth`, `Premium` (billable Monthly or Yearly),
  `Pro`.
- Yearly price = Monthly price × 10 — a ~17% discount framed as "2 months free," meant to be
  surfaced prominently on the pricing page and in trial-expiry emails.
- New studios get a 14-day full-featured trial, no card required, followed by a 7-day read-only
  grace period, then suspension until a plan is chosen.
- Subscriptions are **issuer-level, per studio (tenant)** — never per-artist or per-client. Any
  unit-economics model should use studio count, not artist or client count, as its subscriber
  unit.
- Two separate Stripe integrations exist and must not be confused: **Stripe Billing** charges
  studios for SaaS access (subscriptions) and is active; a **Stripe aggregator** model collects
  client card deposits into the platform's own account (manual capture — held, then captured at
  session completion) and is active. **Stripe Connect is not used** — it isn't available in the
  platform's operating country — so the platform does **not** handle studio payouts; client cash
  deposits are recorded manually via owner/artist confirmation.
- **A confirmed, since-fixed real revenue-reporting bug, worth treating as a standing check
  rather than a closed incident**: before the `PlanPrice` redesign, `GetPlatformStatsQuery` and
  `GetMrrHistoryQuery` computed MRR as `Sum(Plan.PriceMonthly)` even for yearly-billed
  subscriptions — using a "decorative reference figure" (e.g., a Premium plan's displayed monthly
  price of 79) instead of the true monthly-equivalent of the yearly charge (e.g., 790 ÷ 12 =
  65.83). This overstated MRR for every yearly subscriber. **Any future plan/pricing model change
  must re-verify that every revenue metric uses the correct monthly-equivalent basis** — this bug
  class (a metric silently reading a display price instead of a billed amount) is exactly the
  kind of thing to check for by default, not just when a bug is reported.
- A two-sided referral-rewards system exists with real cost implications (discounts/credits
  issued to both referrer and referee) — any evaluation of its ROI needs the actual redemption
  and resulting-subscription data, not an assumed conversion rate.

### Pricing-relevant customer-taste inputs (short form — full detail lives in the UI/UX Consultation project)

The UI/UX Consultation project's research baseline on tattoo-artist taste and personality
surfaced findings directly relevant to pricing strategy, worth carrying here without duplicating
the whole document: this audience explicitly and repeatedly asks for **predictable, flat-rate
costs with no hidden fees**, rejects tools that feel like they're upselling or nickel-and-diming,
and is unusually good at detecting — and reacting badly to — anything that reads as generic
corporate SaaS pricing theater (annual-only dark patterns, artificial urgency, opaque overage
fees). A pricing or plan-gating recommendation from this project should be checked against that
finding before being finalized: transparent, simple plan structures test well with this specific
audience; complexity and pressure tactics test poorly, independent of whether they'd work for a
different SaaS vertical.

---

## What this project is for
- **Subscription revenue modeling** — MRR, ARR, churn, expansion/contraction, LTV, CAC, using
  real data the user provides, verified against the actual `Plan`/`Subscription` schema.
- **Pricing and plan-tier strategy** — evaluating whether `Free`/`Starter`/`Growth`/`Premium`/
  `Pro` are priced and gated correctly, benchmarked against the Industry-Standard comparators and
  checked against the pricing-relevant customer-taste findings above.
- **Auditing revenue-reporting feature correctness** — re-verifying `GetPlatformStatsQuery`,
  `GetMrrHistoryQuery`, and the owner-level revenue summary against live source whenever a
  plan/pricing change is proposed, specifically checking for the monthly-equivalent-basis bug
  class described above.
- **Cash flow and runway analysis for Pena e Artë as a company**, using real financial inputs the
  user supplies — never fabricated.
- **Unit economics of the payment architecture itself** — aggregator card-processing costs versus
  manually-recorded cash payments, the revenue-recognition timing implied by manual capture (held
  deposits, no-show/cancellation/refund exposure), and how the "no Stripe Connect, no payouts"
  architecture shapes the platform's own cash position versus its studios'.
- **Financial due-diligence and investor-facing materials** — pitch-deck numbers slides, basic
  cap-table-adjacent modeling if asked — built on real inputs, with visual presentation handed to
  the Graphic Design & Branding project once the numbers and narrative are settled.
- **Referral-program ROI evaluation** — cost (discounts/credits issued) versus resulting
  subscription revenue, using actual redemption data.
- **Specifying financial-logic fixes as overnight-prompt-ready write-ups** when the fix is
  domain-specific to finance (a wrong formula, basis, or rounding rule) rather than architectural,
  following Engineering Consultation's Overnight Prompt Standard.

## What this project is explicitly NOT for
- **Tax, legal, or bookkeeping advice for Pena e Artë's tenant studios or artists.** Their
  finances are their own business — entirely out of scope here, and not to be confused with the
  unrelated "small-business" finance skills that serve end customers of a different product.
- **Implementation-correctness architecture review** of the Payment/Subscription domain that
  isn't about financial-outcome correctness (query efficiency, migration safety, webhook
  idempotency) — that's Engineering Consultation's job.
- **Inventing financial figures.** If real revenue, cost, or usage data isn't provided, state the
  assumption explicitly and mark every downstream number as illustrative — never present a
  modeled number as a fact about the actual business.
- **Committing code.** A financial-logic fix becomes a spec or prompt handed off (see scope
  boundary above), never an in-line change.
- **Visual presentation of pricing or financial materials** — that's Graphic Design & Branding's
  job once this project has determined what the numbers and structure should be.

---

## Standards this project always applies
- Every dollar figure in a deliverable is either sourced from data the user actually provided, or
  explicitly marked as an assumption or illustrative placeholder — never stated as fact.
- Every claim about the current Plan/Subscription/Payment architecture is verified against live
  source before it becomes the basis for a financial recommendation — `architecture.md`'s own
  Feature Module Map can lag reality, and this domain specifically has a documented history of
  drift (six plan-related overnight prompts in three days during the July 2026 redesign).
- Every revenue metric is checked for the monthly-equivalent-basis bug class (a metric reading a
  display/reference price instead of the actually-billed amount) whenever a plan or pricing
  change is evaluated — this is now a standing check, not a one-off fixed issue.
- Competitive pricing benchmarks reuse the Industry-Standard Benchmark Set already established
  for architecture/UI parity work, rather than re-deriving a competitor list from scratch.
- Any pricing or plan-gating recommendation is checked against the pricing-relevant customer-taste
  findings (flat/predictable costs test well, complexity and upsell pressure test poorly with
  this specific audience) before being finalized.

---

## Deliverable standard
- Every financial model or report states its data sources and assumptions explicitly, clearly
  distinguishing verified figures from estimates or projections.
- Every pricing/plan recommendation names the exact plan tier(s) it affects and the exact schema
  fields it depends on (`Plan`, `PlanPrice`, `Subscription.BillingInterval`, etc.), so it can be
  hooked directly into an implementation spec without re-deriving context.
- Every recommendation that requires an engineering change specifies what "done" looks like
  precisely enough to become an overnight prompt: which query or handler is affected, what the
  correct calculation is, and how to verify it (e.g., seed a yearly subscription, confirm the
  reported MRR reflects the true monthly-equivalent rather than the plan's decorative reference
  price).
