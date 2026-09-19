# ADR-0001 Amendment B — Flow A carries no platform commission, permanently

**Date:** 16 September 2026 · **Status:** Accepted
**Amends:** `ADR-0001-payment-providers.md` §"Monetization — deliberately deferred, but not
foreclosed" (superseded in full), §"Flow A — POK" reason 1 (reasoning superseded, provider
choice unaffected for Albanian studios), `ADR-0001-amendment-A-verified-repo-state.md` Finding 4
and the "Revised ordering" row 6 (both narrowed, not reversed)
**Decider:** Phi
**Triggered by:** explicit business-model confirmation, 16 September 2026 — see "The decision"

---

## The decision

**Flow A takes no platform commission. Ever.** A client's deposit is paid to the studio or
artist at 100%. Pena e Artë never deducts a fee, a percentage, or any amount from a Flow A
transaction, at launch or at any point in the future. This is not "0% for now, revisit later" —
it is a closed decision. **Pena e Artë's only revenue is Flow B — the studio's own subscription.**

This reverses the explicit stance in ADR-0001's "Monetization" section, which read the option to
add a Flow A fee later as valuable enough to build for on day one. It wasn't reversed because
that reasoning was wrong at the time — it's reversed because the business decision it was
deferring has now actually been made, in the other direction.

---

## Why this changes more than a percentage

ADR-0001 picked POK **specifically because of `splitWith`** — its ability to take a platform fee
atomically at payment time. Reason 1 in "Flow A — POK · Why" says so directly: *"Without it,
Pena e Artë is stuck in the referral model — no pricing control, no merchant ownership,
structurally hard to escape. This is the deciding factor, not the API quality."* That framing
assumed a fee was coming eventually. It is not. So the specific thing ADR-0001 named as *the*
deciding factor for POK no longer applies as reasoning — though, see below, it doesn't change
the answer for Albanian studios, because of a second fact ADR-0001 already established.

**What doesn't change:** ADR-0001's "Accepted risks" table already called out — and accepted —
that POK has *"no platform-level API key — each studio issues its own credentials,"* noting
*"it is also the cleanest Article 4(g) posture."* In other words, the per-studio-merchant model
this amendment now requires everywhere was **already** how POK was going to work for Albanian
studios. Nothing about the POK integration itself needs to change on account of this amendment.
What changes is the domain model and the capability surface that existed to support a fee that
is now permanently ruled out.

**What does change:** every piece of machinery that existed to support taking a cut —
`Payment.PlatformFeeAmount`, the `SupportsSplit` capability flag, and `splitWith` called with a
nonzero recipient for Pena e Artë — was deliberately built and deliberately kept, per ADR-0001's
own words, because *"retrofitting a money split into an existing payment aggregate is painful;
carrying an unused field is free."* That tradeoff only makes sense if the field might one day
stop being unused. It won't. Carrying dead optionality forever is not free — it's a permanent
tax on every future engineer reading the `Payment` aggregate and wondering what `PlatformFeeAmount`
is for, and a permanent temptation to wire it up "since it's already there." **Recommendation:
remove it, don't just leave it at zero.**

---

## What supersedes what

| ADR-0001 / Amendment A said | This amendment says |
|---|---|
| "Monetization — deliberately deferred, but not foreclosed." Build `splitWith` into the domain model and the POK integration from day one, even at a 0% fee. | Foreclosed. Do not build fee-taking machinery. Remove what already exists (see Consequences). |
| Flow A — POK reason 1: `splitWith`'s platform-fee field is "the deciding factor" for choosing POK | Superseded as *reasoning*. POK remains the right call for Albanian studios, but for a narrower reason that was already true: BoA-licensed EMI, native ALL, auth/capture/hold-expiry fit, real sandbox — and each studio holding its own merchant credentials directly, with no platform-level split of any kind. |
| Amendment A, Revised ordering, row 6: *"`IPaymentProvider` refactor + `Payment` migration + `PlatformFeeAmount` at 0% + architecture test"* | Narrowed: the refactor and the architecture test stand. `PlatformFeeAmount` is removed rather than defaulted, per this amendment's Consequences. |
| Amendment A, Finding 4: platform fee is a distinct `PlatformFeeAmount` field, never a `SessionSplit` row | Unaffected in spirit — `SessionSplit` still means what it meant (a studio's own internal artist/owner split of the deposit total, which is the studio's business, not the platform's). Only the *existence* of `PlatformFeeAmount` itself is now in question. |
| ADR-0001 Consequence 2: `IPaymentProvider` carries `SupportsSplit` among its capability flags | `SupportsSplit` is removed from the capability surface — there is nothing left for it to gate. |

Everything else in ADR-0001 and Amendment A stands: POK plus always-on cash for Flow A, Polar
(Paddle fallback) for Flow B, easyPos for fiscalization, three separate provider abstractions
never collapsed, no platform balance/ledger/payout queue, webhooks as triggers only.

---

## Consequences for the codebase

1. **Remove `Payment.PlatformFeeAmount`** (domain entity + migration) rather than leaving it at a
   permanent 0. A column that can only ever hold zero is not a data model, it's a comment with
   extra steps — and an invitation for someone to wire it up later without knowing this decision
   was deliberate and closed.
2. **Remove `SupportsSplit` from `PaymentProviderCapabilities`.** No capability should exist to
   gate a behavior (commission-taking) that will never ship.
3. **Any `IPaymentProvider` call shape that plans to pass a platform-side `splitWith` recipient
   must not do so.** When POK integration lands (separate, not-yet-started work per the
   production report's Outstanding Work §1), a Flow A payment goes to the studio's own merchant
   identity in full — never with a second `splitWith` leg naming Pena e Artë.
4. **`SessionSplit` is unaffected** — it remains the studio's own internal artist/owner split of
   the full deposit amount, unrelated to platform revenue.
5. **The revenue-reporting query (`GetRevenueSummaryQuery`) and anything that ever assumed a
   platform take-rate on Flow A must not do so.** Grep for `PlatformFeeAmount` before removing it
   — every reference needs a real decision (delete the read, or confirm it was already dead),
   not a silent compile-error fix.
6. **Provider selection for Flow A does not require one processor spanning every country.**
   Because there is no split to compute and no platform-level merchant relationship to maintain,
   each studio can hold its own merchant account with whatever processor actually serves its
   country — POK for Albania (per ADR-0001, unchanged), something else for studios elsewhere.
   That "something else" is explicitly **not decided by this amendment** — it's tracked as
   separate, still-open work (see "What this amendment does not decide").
7. **Docs and copy:** any user-facing or internal text that describes or implies a platform
   commission, service fee, or take rate on a client's deposit must be corrected. Help content
   (`helpContent.ts`, the standalone manual) and onboarding-tour copy are in scope per this
   project's rule 7 — check `client-deposit-pay`, `owner-payments`, and any studio-facing pricing
   explanation.

---

## What this amendment does not decide

- **Which processor non-Albanian studios use.** POK's residency requirement (registered head
  office in Albania — confirmed contractually, Art. 4.1 of the sample RPay merchant agreement)
  still means a non-Albanian studio cannot hold a POK account. Under this amendment's per-studio
  model, that studio needs its *own* merchant account with *some* other processor — a direct
  Stripe account is the leading candidate (Stripe's Albania block was specific to this entity and
  to Stripe Connect's sub-merchant model, not to an unrelated foreign studio opening its own
  ordinary account) but this has not yet been independently verified and is not blocking this
  amendment. Track separately.
- **The POK integration itself.** Still not started, per the production report's Outstanding
  Work §1/§3. This amendment changes what that integration will look like once it starts (no
  `splitWith` fee leg) but does not start it.
- **Flow B.** Untouched. Still blocked on Stripe's Albania restriction; still designed to move to
  Polar; still the platform's only revenue source, now formally so rather than provisionally so.
