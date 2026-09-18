# Overnight Prompt — Flow A Zero-Commission Refactor

> Feed this file directly to Claude Code (main **Pena e Artë - Engineering** project, full repo
> write access) as the task prompt. **Fully autonomous — no external dependency, no
> BLOCKING-MANUAL item anywhere in this prompt.** This is a domain-model and documentation
> correction, not a payment-provider integration — it does not touch `NullPaymentProvider`'s
> fail-closed behavior and does not wire up POK, easyPos, or Polar. Read this file in full before
> writing anything; it cites what a prior research session found in the repo, but the source may
> have moved since — re-verify every cited file/line yourself before editing.

**Date logged:** 2026-09-16
**Requested by:** Phi
**Origin:** Business-model clarification — Flow A carries no platform commission, ever. Client or
guest pays the studio/artist 100%. Pena e Artë's only revenue is Flow B (studio subscriptions).
Full reasoning: `docs/payments/ADR-0001-amendment-B-flow-a-zero-commission.md` (already written
and merged — **read it in full before doing anything else in this prompt**, it is the source of
truth for *why*, this prompt is only the *what*).

**Checkpoint before starting:**
```bash
git status                     # must be clean before starting
git checkout main && git pull
git checkout -b refactor/flow-a-zero-commission
git commit --allow-empty -m "checkpoint: before Flow A zero-commission refactor"
```

Per this project's rule 6 (industry-standard benchmark) and rule 7 (Help stays in sync), **every
user-facing change below must update `frontend/src/features/help/helpContent.ts`, the standalone
manual (`frontend/public/user-manual/index.html`), and, where the affected flow has an
onboarding-tour step, the matching file in `frontend/src/features/help/tours/`** — §5 lists what
a prior audit already knew to check; grep for anything it missed.

---

## 1. Why this prompt exists, and what "done" looks like

`Payment.PlatformFeeAmount` and `PaymentProviderCapabilities.SupportsSplit` were deliberately
built (EPIC-0001, PENA-106, 2026-07-31) to carry a platform commission that ADR-0001 described as
"deferred, not foreclosed" — default `0`, kept in the model so a future fee wouldn't require a
painful retrofit. That premise is gone: the business decision has been made, in the other
direction, permanently. Carrying dead optionality is no longer free — it's a footgun for the
next engineer who finds an unused-looking field and wires it up. **This prompt removes it rather
than leaving it at a permanent zero.**

"Done" means: no field, capability, or code path anywhere in the repo exists that could compute,
store, or transmit a nonzero platform commission on a Flow A payment; every place that referenced
`PlatformFeeAmount` or `SupportsSplit` has been either deleted or given a real, reviewed
replacement; and every doc/UI surface that implied or could imply a platform cut on a client's
deposit says clearly that the studio/artist receives 100%.

**Explicitly not in scope** (see §6): actually integrating POK, easyPos, or Polar; deciding which
processor non-Albanian studios will use; any change to `SessionSplit` (the studio's own internal
artist/owner split of the full deposit — untouched by this refactor, it was never platform
revenue).

---

## 2. Backend — remove the commission machinery

### 2.1 — `Payment` entity

In `Pena_e_Arte.Domain/Entities/Payment.cs`, remove the `PlatformFeeAmount` property entirely.
Before removing it, grep the whole solution for `PlatformFeeAmount` and classify every hit:
- **Query/report code that reads it** (e.g. `GetRevenueSummaryQuery`, any DTO/response mapping
  it) — confirm nothing downstream assumes a nonzero value is possible; remove the field from
  the read path, don't just let it fail to compile silently if it's mapped through
  `AutoMapper`/manual mapping.
- **Any write path that sets it** (payment creation/capture commands) — remove the assignment;
  do not replace it with a hardcoded `0` write to a field that no longer exists.
- **Tests asserting `PlatformFeeAmount` stays `0`** (added in EPIC-0001 per that overnight
  prompt's §"Unit tests for `PlatformFeeAmount` calculation/persistence, explicitly asserting it
  never...") — delete these tests, they assert a premise (the field exists) that's no longer
  true, not the behavior (no commission) that still matters. Do not leave them red.

New EF Core migration dropping the column (follow this project's existing naming convention —
check the most recent migration under `Pena_e_Arte.Infrastructure/Migrations/` for the pattern):
```bash
dotnet ef migrations add RemovePlatformFeeAmount --project Pena_e_Arte.Infrastructure
```
Confirm the generated migration only drops the one column — no other schema drift should be
present; if `dotnet ef migrations add` wants to change anything else, stop and investigate why
before proceeding, don't just accept the generated migration.

### 2.2 — `PaymentProviderCapabilities` / `IPaymentProvider`

In `Pena_e_Arte.Domain/Interfaces/IPaymentProvider.cs` (or wherever `PaymentProviderCapabilities`
is actually declared — confirm the file), remove `SupportsSplit` from the capability set. Grep
every implementation (`NullPaymentProvider` today; any test doubles/fakes under
`tests/Pena_e_Arte.UnitTests/` or `IntegrationTests/`) and remove the corresponding property from
each. Grep the whole solution for `SupportsSplit` afterward and confirm zero hits remain.

Confirm `GET /api/v1/payments/capabilities` (`GetPaymentCapabilitiesQuery`/
`PaymentCapabilitiesResponse`, added in the 2026-09-03 compliance-payment-correctness work) never
exposed `SupportsSplit` in the first place — it currently only surfaces
`Capabilities.SupportsAuthCapture` as `CardPaymentsAvailable`. If that's confirmed, no change
needed there; note it explicitly in your final summary rather than silently assuming.

### 2.3 — Revenue reporting

`GetRevenueSummaryQuery` (`GET /api/v1/reports/revenue-summary`) currently sums
`Amount - (RefundedAmount ?? 0)`. Confirm it never referenced `PlatformFeeAmount` in that
calculation (per the payment-processing report, `PlatformFeeAmount` was "deliberately not
modeled as a `SessionSplit` row" and kept separate — but confirm this query itself doesn't
subtract it anywhere before assuming the removal is a no-op here). If it does reference it
anywhere, the query's job doesn't change — it's still reporting the studio's revenue, which was
always the full `Amount` minus refunds, never minus a platform cut — just remove the now-invalid
reference.

### 2.4 — Any owner-facing or admin-facing "fee"/"commission" surface

Grep the whole backend for `PlatformFee`, `platform fee`, `commission`, `Commission` (case-
insensitive) outside of `SessionSplit`-related code (which is a different, unaffected concept —
don't touch it) and outside historical/dated doc files under `docs/` (those are a record, not
live code — this prompt does not rewrite `docs/payments/ADR-0001-payment-providers.md`,
`docs/payments/pok-assessment.md`, or any other dated research memo; `ADR-0001-amendment-B-*.md`
is the doc that already supersedes them). For anything found in live source code or in
`docs/claude/architecture.md`, `helpContent.ts`, or the standalone manual, fix it to reflect zero
platform commission — never imply a fee exists, is planned, or is configurable.

---

## 3. Frontend — remove any "platform fee" assumption in the payments/billing UI

Grep `frontend/src/features/payments/` and `frontend/src/features/billing/` for
`platformFee`/`PlatformFee`/`SupportsSplit`/`commission` (case-insensitive). Likely candidates
based on the Feature Module Map (confirm each, don't assume the list is exhaustive):
- `PaymentDetailPage.tsx` / `PaymentListPage.tsx` — if either renders a fee/commission line item
  anywhere (even conditionally), remove it.
- `SessionSplitsEditor.tsx` — confirm it only edits `SessionSplit` rows (studio-internal) and has
  never surfaced `PlatformFeeAmount` alongside them; if it has, remove that part, keep the rest.
- Any RTK Query response type in `paymentsApi.ts` that includes `platformFeeAmount` — remove the
  field from the TypeScript type and from anywhere it's destructured/rendered.
- Any owner-facing pricing/marketing copy (public pricing page, studio onboarding tour, signup
  flow) that describes or could be read as describing a transaction fee on client deposits —
  Pena e Artë's pricing story is subscription-only; if any copy hedges ("no fees for now" or
  similar), make it unconditional ("we never take a cut of your deposits — that's between you and
  your client").

If none of the above actually exist in the frontend (the field may never have reached the UI —
confirm this rather than assuming), say so explicitly in your final summary.

---

## 4. Tests

- Run the full backend and frontend test suites after §2/§3's removals; fix every failure that's
  a direct consequence of the removed field/capability (a test referencing `PlatformFeeAmount` or
  `SupportsSplit` that no longer compiles). Do not broaden this into unrelated test fixes.
- Add one new backend test (wherever this project's `Payment`-aggregate unit tests live) asserting
  that a captured Flow A payment's full `Amount` is what's available for payout/reporting — i.e.
  a regression guard that nothing recomputes a lesser amount on the assumption a platform cut
  exists. Keep it small — this is a guard against regression, not new coverage of the whole
  payment lifecycle.
- Add a fast architecture-fitness assertion (alongside the existing "no platform balance/ledger"
  NetArchTest rule referenced in ADR-0001 Consequence 3 — find that test class and add to it
  rather than starting a new one) that fails the build if a `PlatformFeeAmount`-named property or
  a `SupportsSplit`-named member reappears anywhere in `Pena_e_Arte.Domain` or
  `Pena_e_Arte.Application`. This is the mechanism that keeps this decision from silently eroding
  the same way the aggregator model's absence was enforced by a test rather than a comment.

---

## 5. Help-sync obligations (CLAUDE.md rule 7 — not optional)

- `frontend/src/features/help/helpContent.ts`: check the `client-deposit-pay`, `owner-payments`,
  and any studio-onboarding/pricing-explainer articles (search for "fee," "commission," "deposit"
  — don't assume the ID list above is complete) for language that implies or could imply a
  platform cut. Correct or add a line making explicit that 100% of the deposit goes to the
  studio/artist.
- `frontend/public/user-manual/index.html`: same sections, same content, per this project's
  three-surface rule.
- `frontend/src/features/help/tours/*.ts`: check every onboarding tour (owner, client, artist,
  issuer) for a step that references payments/deposits/pricing; update any that mention or imply
  a fee. If none do, say so explicitly rather than silently skipping this file.

---

## 6. Explicitly out of scope

- Wiring a real Flow A payment provider (POK or otherwise) — separate, not-yet-started work per
  the production report's Outstanding Work §1/§3. This prompt changes the *shape* Flow A will
  have when that lands (no commission leg), not the integration itself.
- Deciding or researching which processor non-Albanian studios will use — open, tracked
  separately in `ADR-0001-amendment-B-flow-a-zero-commission.md`'s "What this amendment does not
  decide."
- Any change to `SessionSplit` or `UpdateSessionSplitsCommand` — that's the studio's own internal
  artist/owner split of the full deposit, unrelated to platform revenue, and this refactor must
  not touch its exact-sum-to-`Amount` invariant.
- Flow B / Polar / Stripe Billing — untouched by this prompt.
- Re-litigating or rewriting the dated research memos under `docs/payments/` (ADR-0001 itself,
  `pok-assessment.md`, `market-scan-both-flows.md`, `implementation-readiness*.md`,
  `industry-standard-payments-architecture.md`). They stay as-is, as a historical record;
  `ADR-0001-amendment-B-flow-a-zero-commission.md` is the document that supersedes their
  monetization framing. Do not edit them.

---

## 7. Final self-check

- [ ] `grep -ri "PlatformFeeAmount"` across the whole solution (backend + frontend) returns zero
      hits in live source and zero hits in `docs/claude/architecture.md` — any remaining hits are
      only inside the dated `docs/payments/` research memos listed in §6, which stay untouched.
- [ ] `grep -ri "SupportsSplit"` returns zero hits anywhere in the solution.
- [ ] The new migration applies cleanly (`dotnet ef database update`) and drops exactly the
      `PlatformFeeAmount` column, nothing else — confirm by inspecting the migration file, not
      just by running it.
- [ ] The new architecture-fitness test exists, is wired into the same suite as the existing
      no-platform-balance test, and — verified by temporarily reintroducing a
      `PlatformFeeAmount`-shaped property and confirming the build fails, then reverting — 
      actually catches a regression rather than trivially passing.
- [ ] `dotnet test` and `pnpm test` both pass.
- [ ] `dotnet build` / `pnpm build`+`tsc -b` both run clean — this project's own memory notes that
      build/typecheck has caught real bugs the unit-test run alone missed before.
- [ ] §5's three Help surfaces were each explicitly checked and updated (or explicitly noted as
      needing no change) — not silently skipped.
- [ ] `docs/claude/architecture.md`'s Feature Module Map row for Payments & Session Splits and its
      Decisions Log already reflect this change (edited 2026-09-16, alongside
      `ADR-0001-amendment-B-flow-a-zero-commission.md`) — confirm they still match what actually
      shipped; if the implementation ended up differing from what those docs describe, update
      them to match reality rather than leaving a mismatch.
- [ ] Final summary states explicitly, for each of §2.4, §3's frontend grep, and §5's tour-file
      check: what was found and fixed, or that nothing was found — never silence on a checked
      item.
