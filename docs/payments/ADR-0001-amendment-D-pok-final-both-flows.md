# ADR-0001 Amendment D — POK finalized for both flows; Flow B migration contingent on POK recurring-billing GA

**Date:** 2 October 2026 · **Status:** Accepted
**Amends:** `ADR-0001-payment-providers.md` §"Decision" (Flow B provider changed, Flow A provider
confirmed final), §"What would reverse this" (the POK-MIT-confirmation trigger row is now
partially satisfied — see below), `ADR-0001-amendment-B-flow-a-zero-commission.md` §"What this
amendment does not decide" (Flow A provider question closed; Flow B's "still designed to move to
Polar" is superseded), `docs/payments/pok-assessment.md` §7 "Recommendation" (the written-MIT-
confirmation precondition is downgraded from blocking to tracked-open, by business decision, not
by POK having answered it yet), `docs/payments/implementation-readiness-status-2026-07-31.md`'s
Help-sync table row "Flow B migration to Polar" (provider name only — the obligation itself is
unchanged)
**Decider:** Phi
**Triggered by:** explicit final-decision instruction, 2 October 2026 — "final decision. we're
going with POK for both flows A and B."

---

## The decision

**POK is the final, chosen provider for Flow A.** This closes what Amendment B and every
provider-assessment doc in this repo had treated as a settled default but — per Phi's own
correction on this exact point, prior to this amendment — was genuinely still open ("we are
still exploring our option... consider the issue unsettled yet"). As of this amendment, it is
no longer open. Paysera and Happy Pay were evaluated as alternatives (see `paysera-wallet-api-
assessment.md` and the Happy Pay correspondence referenced below) and are not being pursued
further for Flow A.

**POK is also the final, chosen provider for Flow B**, replacing Polar (and the Paddle fallback)
as the designated merchant-of-record successor to the current, commercially-blocked direct-
Stripe implementation. **This is a reversal, not a confirmation** — every prior document in this
repo (`ADR-0001-payment-providers.md`, Amendments A and C, `implementation-readiness-status-
2026-07-31.md`, `market-scan-both-flows.md`) names Polar as the Flow B target. That is no longer
the plan.

**Flow B's move to POK is contingent, not immediate.** Per Phi: *"flow B is not currently
supported by POK, but they are on testing of this feature and soon will be available... already
got their staging environment."* Concretely:

- POK does not today expose a production recurring/unattended-charge primitive. This confirms
  `pok-assessment.md` §4's finding (*"there is no recurring-billing primitive... nothing in the
  published docs describes an unattended merchant-initiated transaction"*) as still accurate.
- POK is actively testing this feature and it is expected to reach production.
- A POK staging environment has already been obtained (separate from the existing
  `api-staging.pokpay.io` sandbox referenced in `pok-assessment.md` §3 — confirm with POK whether
  this is the same sandbox with recurring endpoints newly exposed, or a distinct environment,
  before integration work starts; do not assume).
- **Flow B does not move to POK until POK's recurring-billing feature is confirmed production-
  ready.** Until then, Flow B has no committed target provider in active use — see "What this
  amendment leaves explicitly open" below.

This downgrades, by business decision rather than by POK answering in writing, the precondition
`pok-assessment.md` §7 set: *"Do not commit Flow B to POK until they answer the MIT question in
writing."* Phi is choosing to commit to POK as the direction now, ahead of that written
confirmation, accepting the risk that the feature's production shape (SCA exemption, enrolment
process, pricing) may differ from what's assumed here. **The specific written-confirmation
items `pok-assessment.md`'s priority question #1 asks for are not yet answered and still need to
be, before Flow B integration code is written against the feature** — this amendment finalizes
the *provider choice*, not the integration contract.

---

## Why POK over Paysera and Happy Pay (Flow A)

Recorded here because the comparison was done in this consultation and shouldn't be lost:

- **Paysera**: the published Checkout Classic card-payments contract restricts card acceptance
  to EU-registered persons (tied to its card acquirer, AS Magnetiq Bank) — confirmed in two
  independent contract documents, and still showing on the published fee page as of the most
  recent check. A phone call with Paysera support claimed Albanian merchants can use both flows,
  but described Flow A as routing client funds through "the platform's admin" first, then to the
  studio — a pooled/platform-custody model that conflicts directly with the Art. 4(g) zero-
  custody architecture this platform depends on. That conflict was never resolved in writing and
  is now moot for the provider decision, but should not be forgotten if Paysera is ever
  reconsidered for anything else (e.g., the still-open non-Albanian-studio question below).
- **Happy Pay**: correct business channel identified (`happy.al/become-a-partner`, not the
  consumer-loyalty `loyalty@happy.al` channel this consultation was initially misrouted to), but
  a full ~45-question diligence email was never answered before this decision was made.
- **POK**: already has a documented, Article-4(g)-clean per-studio-merchant-credential model
  (`pok-assessment.md`: *"no platform-level API key — each studio issues its own credentials...
  it is also the cleanest Article 4(g) posture"*), a real card sandbox with published test cards,
  full CRUD on orders (create/capture/refund/cancel), native ALL, and — as of the 2026-09-12
  integration landing per `architecture.md`'s Payment Architecture section — is already the real
  `IPaymentProvider` DI default in this codebase. Choosing POK for Flow A is largely a
  finalization of a direction the code already reflects, not a new build.

---

## What changes in the codebase (Flow A — low delta, mostly finalization)

1. **No architectural change.** `IPaymentProvider`, `PokPaymentProvider`, and
   `PaymentProviderCapabilities` already exist and are already the DI default per
   `architecture.md`. This amendment removes the "still exploring" framing from any doc that
   carried it — it does not request new code.
2. **Outstanding before this can be called production-ready** (per `architecture.md`'s own
   banner on the Payment Architecture section): real per-studio POK sandbox verification against
   a live POK account has not happened yet. That remains outstanding after this amendment — it
   is a verification task, not a decision this amendment can close.
3. **Still unanswered from `pok-assessment.md`'s priority question list** (§"Questions to send
   POK, in priority order") and not superseded by anything in this amendment: webhook signing
   (#3), the merchant/partner onboarding programme and realistic studio-signup-to-first-deposit
   time (#2), pricing/MDR/chargeback terms (#4), a chargeback/dispute API (#5), and whether
   `splitWith.userPhoneNumber` fits recurring **artist-commission** `SessionSplit` use (#6, and
   note this is unrelated to the removed platform-fee `splitWith` usage per Amendment B — this is
   the studio's own internal split, which remains in scope). These should still be asked and
   answered in writing; finalizing the provider does not retire this list.

## What changes in the codebase (Flow B — large delta, do not underestimate)

This is the part of the decision most likely to be underestimated by whoever picks it up, so it
is stated plainly:

1. **The existing Flow B implementation is not a thin stub being swapped out — it is a mature,
   independently verified subscription-billing system built directly against Stripe's
   primitives.** Per `architecture.md`'s Decisions Log (rows dated 2026-09-23 through 2026-09-25):
   a shared MRR rules engine (`MrrRules`/`MrrInputLoader`) with month-end semantics matching
   ChartMogul/Baremetrics/Stripe Billing analytics convention; billed-amount snapshotting
   (`Subscription.BilledUnitAmount` et al.) so catalogue price changes never retroactively alter
   what an existing subscriber is charged; a full yearly-cancellation prorated-refund engine
   (`YearlyRefundCalculator`, `SubscriptionRefund` entity, admin-override path); suspension-
   pauses-billing (`PauseCollectionAsync`/`ResumeCollectionAsync`); an append-only revenue ledger
   with movement-deduplication; and all of it verified against **real Stripe test-mode traffic**
   (Stripe CLI `listen`, real test clocks, real declines) — which surfaced and fixed four real
   bugs that unit tests alone had not caught.
2. **None of that logic is reusable as-is against POK, and POK gives you less than Stripe did,
   not more.** `pok-assessment.md` §4 says this explicitly: *"POK is an acquirer, not a billing
   engine. Plans, proration, upgrades, grace periods, dunning and invoicing are all yours to
   build."* The MoR path (Polar/Paddle) this repo was pointed at instead handed you plans,
   proration, dunning, invoicing, and tax/VAT handling as part of the integration. Moving to POK
   means the platform keeps owning all of that — which it already does today, against Stripe's
   primitives — and must re-platform it onto whatever POK's recurring feature's actual primitive
   turns out to be (token-charge-on-schedule? a native subscription object? push-to-app-for-
   confirmation per `pok-assessment.md`'s "if no" fallback?) once that's known. **Budget this as
   a re-platforming effort comparable in size to the original Stripe Billing build, not as a
   provider swap.**
3. **Do not start Flow B integration code against POK's recurring feature until it is confirmed
   production-ready and its exact mechanics are known in writing** (SCA exemption basis,
   enrolment process, webhook signing, pricing — `pok-assessment.md` priority questions #1, #3,
   #4). Writing code against a testing-stage feature's assumed shape risks a second rebuild when
   the real production contract differs.
4. **`IStripeBillingService`, `StripeBillingService`, `StripeDiscountService`, and every
   Stripe-billing-specific entity/migration stay in the codebase as-is for now.** They are not
   being deleted or frozen by this amendment — they remain the only even-partially-working Flow B
   mechanism (manual/cash activation via `ActivateSubscriptionManuallyCommand` is the only
   production-viable path today, per the open interim question below) until a POK-based
   replacement is designed and built.

---

## What this amendment leaves explicitly open — do not guess at these

1. **The interim Flow B path, between now and POK recurring reaching production, is not
   decided by this amendment.** Options on the table, none chosen: (a) keep
   `ActivateSubscriptionManuallyCommand` (cash/manual activation, out-of-band) as the only
   production path and simply don't sell self-serve card subscriptions until POK ships; (b) stand
   up Polar or Paddle as a genuinely temporary bridge, knowing it gets torn out once POK ships;
   (c) something else. **This needs a decision from Phi before any Flow B work beyond what
   already exists is built.** Flagging it rather than picking one, per this project's own "do
   not build blind" rule.
2. **The non-Albanian/international-studio provider question is unrelated and still fully open.**
   POK cannot serve a studio outside Albania (residency requirement, confirmed contractually per
   Amendment B). This amendment does not touch that question — Stripe-direct for non-Albanian
   studios remains an unverified candidate, unchanged from Amendment B's own "what this amendment
   does not decide" section. Do not read "POK for both flows" as "POK for every studio
   everywhere" — it is not.
3. **The exact production mechanics of POK's recurring feature are unknown.** Whether it is
   MIT/MOTO-based silent charging, a native subscription object, or a push-to-app confirmation
   flow (`pok-assessment.md`'s "if no" fallback) determines materially different UX and dunning
   design. Get this in writing from POK before design work starts on the Flow B re-platform.
4. **Webhook signing, onboarding programme, pricing, and dispute-API status for POK remain
   unanswered** (carried over from `pok-assessment.md`, unchanged by this amendment — see "What
   changes in the codebase (Flow A)" item 3 above).

---

## Industry-standard benchmark note

Per this project's standing rule, every provider decision is checked against the category.
**This one is a deliberate divergence from the benchmark set, not a benchmark-driven choice, and
should be presented as such.** Every booking-SaaS competitor (Vagaro, Fresha, Boulevard, Mindbody,
GlossGenius, Booksy, Mangomint) and every MoR (Paddle, Lemon Squeezy, Polar) in this category
treats subscription billing as a solved, outsourced problem — that is precisely what an MoR is
for. Choosing a plain acquirer (POK) for Flow B, and therefore continuing to own the billing
engine in-house, is the opposite of where the category's tooling has converged, and is being
done for reasons outside the benchmark (a single local vendor relationship, ALL-denominated
settlement, one integration instead of two) rather than because it is the better-engineered path.
That tradeoff is Phi's to make and has been made — but it should not be silently presented later
as "what everyone does," because it is not.

---

## Help-sync obligations

Per this project's rule 7, checked explicitly rather than skipped:

1. **`frontend/src/features/help/helpContent.ts`** — no change required **yet**. Nothing about
   what a client or owner sees or can do has changed today: Flow A's card option is already
   provider-neutral in its copy (per Amendment A's "Flow-A card wording... went provider-neutral"
   finding), and Flow B's user-facing billing flow still runs on Stripe Customer Portal/Stripe
   Billing unchanged. `implementation-readiness-status-2026-07-31.md`'s existing Help-sync
   obligation table row for Flow B (originally "migration to Polar: rewrite `owner-billing` and
   `owner-subscribe` end to end") is updated by this amendment to name POK instead of Polar (see
   "Other documents updated" below) — but that rewrite is still gated on the Flow B migration
   actually happening, which per this amendment has not started.
2. **`frontend/public/user-manual/index.html`** (confirmed live/served copy per
   `implementation-readiness-status-2026-07-31.md` — `docs/user-manual.html` is the stale,
   unserved copy) — same as above: no change required yet, update is bundled with the eventual
   Flow B migration work, not this provider-choice amendment.
3. **Onboarding tours** (`frontend/src/features/help/tours/{client,artist,owner,issuer}Tour.ts`)
   — no change required yet, same reasoning. **Flagging in advance:** when POK integration
   verification lands for Flow A (the outstanding sandbox-verification item above), `owner-
   studio-profile`/`ownerTour.ts` will need a "connect your payment provider" step per
   `implementation-readiness-status-2026-07-31.md`'s own existing table row — that obligation
   predates this amendment and is restated here so it isn't lost.

**Why "no change yet" is the right call, not a skipped obligation:** this amendment is a provider
decision and a sequencing commitment, not a shipped user-visible change. The Help-sync
obligations for the actual Flow A sandbox verification and the actual Flow B re-platform are real
and already tracked (in `implementation-readiness-status-2026-07-31.md` and above) — they trigger
when that work ships, not when the provider is chosen.

---

## Other documents updated by this amendment

- **`docs/claude/architecture.md`** — "Payment Architecture — Two Flows..." section and the
  Decisions Log updated to state POK as final for both flows and link here (this is the
  authoritative live reference; done in the same change as this file).
- **`docs/payments/ADR-0001-payment-providers.md`** — header pointer added noting this amendment
  supersedes the original Flow B decision; original text left intact as historical record.
- **`docs/payments/pok-assessment.md`** — header pointer added noting the Flow A recommendation is
  now final and the Flow B written-confirmation precondition has been superseded by business
  decision, pending the actual written answer; original assessment left intact, it is still the
  live reference for POK's open questions.
- **`docs/payments/market-scan-both-flows.md`** — header pointer added noting Polar is no longer
  the Flow B target; original market scan left intact as historical record of the comparison.
- **`docs/payments/implementation-readiness-status-2026-07-31.md`** — the Help-sync table's
  "Flow B migration to Polar" row updated to name POK; everything else in that document
  (Law 124/2024 analysis, recommended order of attack, etc.) is unrelated and untouched.
- **`docs/payments/ADR-0001-amendment-A-verified-repo-state.md`** and **`docs/payments/ADR-0001-
  amendment-C-studio-currency.md`** — each contains a one-line restatement of the full provider
  stack ("POK for Flow A, Polar (Paddle fallback) for Flow B..."); a footnote pointer to this
  amendment is added at that line in each. Neither document's own subject matter (repo-state
  verification; studio currency) is otherwise touched.

**Deliberately left untouched — historical execution records, not living references:**
`docs/claude/overnight-prompt-epic-0001-pre-implementation-hardening-2026-07-31.md`,
`docs/claude/overnight-prompt-flow-a-zero-commission-refactor-2026-09-16.md`,
`docs/claude/production-readiness-gap-audit-2026-09-03.md`,
`docs/engineering/EPIC-0001-pre-implementation-hardening.md`, and
`docs/infra/ADR-0002-secrets-management.md`. These are point-in-time records of work already
executed and shipped; they mention Polar only in describing what was true when they were written.
Editing them risks corrupting the historical record they exist to preserve. If a future reader
needs the current provider decision, `architecture.md` and this amendment are the live references
— these five files are not, and should not be treated as such.

---

## Addendum (2026-10-02, same day): executable follow-up prompt

`docs/claude/overnight-prompt-pok-final-decision-sync-2026-10-02.md` is the fully-specified,
fully-autonomous overnight prompt for the main Engineering project's Claude Code session to
run against this decision. It does NOT build Flow B on POK (the contingency above hasn't
lifted) — it adds an architecture fitness test that fails the build if Flow B code ever
references a POK type before this amendment's contingency is lifted, plus pointer comments
in `StripeBillingService.cs` and `InfrastructureServiceExtensions.cs` so a future reader of
the code itself (not just the docs) lands on this amendment. It also names, explicitly, the
one real blocking-manual prerequisite for Flow A's own outstanding sandbox verification —
Phi connecting a real staging studio to POK with real, POK-issued credentials via the
already-working `ConnectPokAccountCommand` — and instructs Claude Code NOT to simulate or
fake that step. Read that prompt for the exact diffs.

---

## Final self-check

- [x] POK confirmed as final for Flow A — stated, reasoned against Paysera/Happy Pay alternatives.
- [x] POK confirmed as final for Flow B — stated, reversal from Polar made explicit, not buried.
- [x] Flow B's POK contingency stated precisely (testing now, not GA, staging obtained) — not
      overstated as "done" or understated as "undecided."
- [x] Interim Flow B path named as an open decision, not silently resolved.
- [x] International/non-Albanian-studio question re-affirmed as separate and untouched.
- [x] Engineering cost of the Flow B re-platform (losing MoR-provided billing-engine functionality)
      stated explicitly, not glossed over.
- [x] Industry-benchmark divergence named and explained, not silently presented as standard.
- [x] Help-sync obligations checked for all three surfaces; "no change yet" justified, not assumed.
- [x] Every other document mentioning Polar identified; each either updated with a pointer or
      explicitly named as deliberately untouched, with reasoning.
- [x] No source code, migration, or config file touched — this is a docs-only change, per this
      project's file-access boundary.
