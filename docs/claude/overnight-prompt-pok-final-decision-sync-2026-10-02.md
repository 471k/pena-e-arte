# Overnight Prompt — POK Final-Decision Sync (ADR-0001 Amendment D)

> Feed this file directly to Claude Code (main **Pena e Artë - Engineering** project, full repo
> write access) as the task prompt. **Fully autonomous — no BLOCKING-MANUAL item is attempted in
> this prompt; §7 names one explicitly and tells you not to touch it.** This is a small
> guardrail/doc-sync prompt, not a payment-provider integration — it does not wire POK into Flow
> B, does not touch `IStripeBillingService`/`StripeBillingService`'s actual billing logic, and
> does not call any POK or Stripe API. Read this file in full, then read
> `docs/payments/ADR-0001-amendment-D-pok-final-both-flows.md` in full (the source of truth for
> *why* — this prompt is only the *what*) before touching anything. Every file/line cited below
> was verified against live source on 2026-10-02; re-verify before editing in case it moved.

**Date logged:** 2026-10-02
**Requested by:** Phi
**Origin:** Final business decision — POK is the chosen provider for both Flow A and Flow B.
Flow A: confirms/finalizes what the code already does (low delta). Flow B: reverses the prior
Polar/Paddle plan, but is **contingent** on POK's recurring-billing feature (currently in
testing on POK's side, staging environment already obtained by Phi) reaching production. Full
reasoning: `docs/payments/ADR-0001-amendment-D-pok-final-both-flows.md` — already written and
merged, read it first.

**Checkpoint before starting:**
```bash
git status                     # must be clean before starting
git checkout main && git pull
git checkout -b chore/pok-final-decision-sync
git commit --allow-empty -m "checkpoint: before POK final-decision sync (Amendment D)"
```

Per this project's rule 7 (Help stays in sync), checked explicitly below in §9 — the answer is
"no Help change needed," stated rather than skipped, because nothing user-visible changes in
this prompt. Per rule 6 (industry-standard benchmark), see §10.

---

## 1. Why this prompt exists, and what "done" looks like

Amendment D made a final provider decision, but a decision recorded only in `docs/` is invisible
to anyone reading the code itself, and does nothing to stop a future session from accidentally
starting Flow B integration work against POK before POK's recurring feature is actually ready —
which is explicitly against Phi's own stated sequencing. This prompt's entire job is:

1. Add a failing-by-default **architecture test** that makes "don't wire POK into Flow B yet" a
   compile/test-time guarantee, not a comment someone can miss.
2. Add short **pointer comments** in the two or three places a future engineer is most likely to
   be reading code without having read the docs, so they land on Amendment D instead of
   reinventing the question.
3. **Confirm, explicitly, that there is nothing else to build tonight** — Flow A's code is
   already finalized-shape (POK is already the DI default, per
   `Pena_e_Arte.Infrastructure/Extensions/InfrastructureServiceExtensions.cs` lines 190–198), and
   Flow B has a real, working, tested Stripe implementation that must keep running exactly as-is
   until Amendment D's contingency lifts.

**Done looks like:** one new passing test that would fail if Flow B code starts referencing POK
types; a handful of one-paragraph comment additions; zero behavior change; zero new runtime
dependency; `dotnet build`/`dotnet test` green; nothing else touched.

---

## 2. Decisions already made — do not re-litigate

- POK is final for Flow A. POK is final for Flow B, **contingent** on POK's recurring-billing
  feature reaching production (in testing now, not GA). Source: Amendment D, §"The decision".
- Flow B's interim path (stay on manual/cash activation only, vs. a temporary MoR bridge) is
  **explicitly undecided** — Amendment D §"What this amendment leaves explicitly open" item 1.
  **This prompt does not decide it either.** Do not infer an answer from anything in this file.
- The non-Albanian/international-studio provider question is unrelated and untouched by any of
  this — Amendment D §"What this amendment leaves explicitly open" item 2. Nothing here concerns
  that question; do not conflate the two.

## 3. Decisions NOT made — flag, don't guess

If, in the course of this prompt, you notice anything that looks like it requires picking one of
these, **stop and surface it in your final report instead of deciding it**:

- What Flow B does between now and POK's recurring feature reaching GA (Amendment D, open item 1).
- Whether the "staging environment" Phi obtained from POK is the same `api-staging.pokpay.io`
  sandbox already configured as `PokOptions.BaseUrl`'s default
  (`Pena_e_Arte.Infrastructure/Services/PokOptions.cs`), or a distinct environment. This prompt
  does not change `PokOptions` and does not need to know the answer — just don't assume one.
- Anything about POK's recurring-billing feature's actual mechanics (SCA exemption, enrolment,
  webhook signing, pricing) — `docs/payments/pok-assessment.md`'s priority questions #1/#3/#4 are
  still unanswered in writing. Nothing in this prompt depends on the answers, and nothing here
  should imply they're known.

---

## 4. Scope boundary — do not touch

| File/area | Why it's off-limits for this prompt |
|---|---|
| `Pena_e_Arte.Infrastructure/Services/StripeBillingService.cs`, `StripeDiscountService.cs` — **method bodies** | Working, tested Flow B billing logic (MRR/ledger/refund machinery, `architecture.md` Decisions Log rows 2026-09-23→25). A comment addition above the class is in scope (§6.3); touching a method is not. |
| `Pena_e_Arte.Application/Billing/**`, `Pena_e_Arte.Application/Platform/Commands/{BackfillSubscriptionBilledAmountsCommand,CancelSubscriptionCommand}.cs` | Same reason — this is Flow B's actual subscription logic. Read-only for this prompt (the new test in §6.2 reads these files' source text; it does not edit them). |
| `Pena_e_Arte.Infrastructure/Services/PokPaymentProvider.cs`, `PokAuthClient.cs`, `PokCardTokenService.cs`, `PokOptions.cs` | Flow A's working POK integration. Already finalized-shape per Amendment D; no code change this prompt makes should touch these. |
| `frontend/src/features/help/**`, `frontend/public/user-manual/index.html` | No user-visible change in this prompt — see §9. Do not add a Help entry "just in case." |
| Any migration, any `appsettings*.json`, any Vault/secrets code | This prompt changes comments and adds one test. No schema, config, or secrets-handling change of any kind. |

---

## 5. Constraints (restated per this project's standard)

No new NuGet/npm package. TypeScript strict / no `any` (not applicable — this prompt touches no
frontend code). Explicit C# types, no `var` for non-obvious types. No business logic in
endpoints. Tenant isolation via EF Core global query filters (not applicable — no query touched).
Every endpoint keeps `.RequireAuthorization()` (not applicable — no endpoint touched). Never log
PII. Structured logs only (Serilog) — not applicable, no logging touched. **Tests ship with every
change** — the one change this prompt makes to behavior-adjacent surface (a new architecture
test) is itself the test; it needs no separate test of its own, but must be run and shown green.

---

## 6. Phase 1 — do this tonight (fully autonomous)

### 6.1 New architecture fitness test

Add the following `[Fact]` to the existing
`tests/Pena_e_Arte.UnitTests/Architecture/PaymentArchitectureTests.cs` (same file as the existing
`NoType_IsShapedLikeAPlatformBalanceLedgerOrPayoutQueue` /
`PokWebhookHandler_NeverReadsRequestBodyOrAssignsPaymentState` /
`NoMember_IsNamedPlatformFeeAmountOrSupportsSplit` tests — reuse the private `FindRepoFile`
helper already in that class, do not duplicate it):

```csharp
/// <summary>
/// ADR-0001 Amendment D (2026-10-02): POK is the final Flow B provider, but ONLY once POK's
/// recurring-billing feature reaches production and is confirmed in writing — that has not
/// happened yet. Until it does, Flow B integration code against POK must not exist anywhere in
/// Billing's commands or the Stripe billing services. This guards against it being added
/// prematurely by scanning Flow B's actual source for any reference to POK's types. A comment
/// saying "don't wire this up yet" erodes; a failing build doesn't (same philosophy as
/// NoMember_IsNamedPlatformFeeAmountOrSupportsSplit above). If this test is failing because the
/// contingency has genuinely been lifted, re-read ADR-0001-amendment-D-pok-final-both-flows.md,
/// update it to record that POK's recurring feature reached production, and only then update or
/// remove this test deliberately — do not delete it to make a build pass.
/// </summary>
[Fact]
public void FlowB_NeverReferencesPokTypes_UntilAmendmentDsContingencyIsLifted()
{
    string[] forbiddenTokens =
        ["PokPaymentProvider", "PokAuthClient", "PokCardTokenService", "IPokCardTokenService"];

    string billingDir = FindRepoFile("Pena_e_Arte.Application", "Billing");
    List<string> files = Directory.GetFiles(billingDir, "*.cs", SearchOption.AllDirectories).ToList();
    files.Add(FindRepoFile("Pena_e_Arte.Infrastructure", "Services", "StripeBillingService.cs"));
    files.Add(FindRepoFile("Pena_e_Arte.Infrastructure", "Services", "StripeDiscountService.cs"));

    List<string> offenders = [];
    foreach (string file in files)
    {
        string source = File.ReadAllText(file);
        foreach (string token in forbiddenTokens)
            if (source.Contains(token, StringComparison.Ordinal))
                offenders.Add($"{Path.GetFileName(file)} references {token}");
    }

    offenders.Should().BeEmpty(
        because: "ADR-0001 Amendment D: Flow B's move to POK is CONTINGENT on POK's recurring-"
               + "billing feature reaching production and being confirmed in writing — that has "
               + "not happened. Flow B code must keep calling Stripe until it does. If this test "
               + "is failing because that contingency has genuinely been lifted, re-read "
               + "docs/payments/ADR-0001-amendment-D-pok-final-both-flows.md, update it to "
               + "reflect the new state, and only then update or remove this test deliberately.");
}
```

Run it once added — it must pass today (Flow B currently has zero references to any POK type;
confirmed by research, see Amendment D). If it fails, that itself is a finding to report, not
something to silently fix by deleting the test.

### 6.2 Pointer comment — `StripeBillingService.cs`

`Pena_e_Arte.Infrastructure/Services/StripeBillingService.cs` currently has no class-level doc
comment at all (verified — the class declaration is the first non-`using` line). Add, directly
above `public class StripeBillingService(`:

```csharp
/// <summary>
/// IStripeBillingService for Flow B (studio → platform subscriptions). Stripe.net direct — this
/// cannot go live for this entity (Albania restriction, confirmed 2026-09-03) and exists today
/// only because nothing else works yet. <b>ADR-0001 Amendment D (2026-10-02) names POK as the
/// FINAL target for Flow B</b>, replacing the prior Polar/Paddle plan — but only once POK's
/// recurring-billing feature reaches production (currently in testing on POK's side; see
/// docs/payments/ADR-0001-amendment-D-pok-final-both-flows.md). Do not start wiring POK in here
/// until that amendment's contingency is lifted — see
/// tests/Pena_e_Arte.UnitTests/Architecture/PaymentArchitectureTests.cs's
/// FlowB_NeverReferencesPokTypes_UntilAmendmentDsContingencyIsLifted for the enforcement.
/// </summary>
```

### 6.3 Pointer comment — `InfrastructureServiceExtensions.cs`

In `Pena_e_Arte.Infrastructure/Extensions/InfrastructureServiceExtensions.cs`, immediately above
line 199 (`services.AddScoped<IStripeBillingService, StripeBillingService>();`), add:

```csharp
        // Flow B billing provider: Stripe.net direct today — commercially blocked for this
        // entity (2026-09-03 finding), kept only because nothing else works yet. ADR-0001
        // Amendment D (2026-10-02) names POK as the FINAL target, replacing the prior Polar
        // plan — but ONLY once POK's recurring-billing feature reaches production (currently in
        // testing). Do not register a POK-backed IStripeBillingService replacement here until
        // that happens — see docs/payments/ADR-0001-amendment-D-pok-final-both-flows.md.
```

Do not touch the existing comment above line 191 (the Flow A / POK one) — it is already accurate
and already final-shape; Amendment D changes nothing about it.

### 6.4 Runbook cross-reference

> **Superseded 2026-10-14 — skip this sub-step.** The staging gate ran; see the runbook's "§3.1 result" entry. Items 6.1–6.3 still apply.

`docs/payments/runbook-studio-currency-migration-2026-09-27.md` §3.1 is the existing, already-
written staging-verification checklist for Flow A's two unverified assumptions
(`PokPaymentProvider.ToPokAmount`'s unit assumption, and the authorized-but-not-captured status
mapping in `MapStatus`). Add one paragraph at the end of that §3.1 section — do not rewrite the
checklist itself:

```markdown
**Status update (2026-10-02):** per Phi, a POK staging environment has been obtained — but no
studio in this codebase has real POK-issued staging merchant credentials connected yet
(`ConnectPokAccountCommand` exists and is ready to use; it has not been exercised with real
values). This checklist cannot be executed until Phi connects at least one real staging studio
through it. That is a human/business step (obtaining and entering real POK-issued credentials),
not a code change — see `docs/payments/ADR-0001-amendment-D-pok-final-both-flows.md`'s "Phase 2"
equivalent in the overnight prompt that shipped this note
(`docs/claude/overnight-prompt-pok-final-decision-sync-2026-10-02.md` §7). Do not attempt to
fabricate or mock this verification — it specifically requires a real POK sandbox transaction.
```

---

## 7. Phase 2 — BLOCKING-MANUAL, do not attempt tonight

**Connecting a real studio to POK's staging environment with real, Phi-obtained merchant
credentials is a human step this prompt cannot and must not simulate.** `ConnectPokAccountCommand`
(`Pena_e_Arte.Application/Payments/Commands/ConnectPokAccountCommand.cs`) is the real, working
command for this — it needs a real `MerchantId` and the matching secret that only POK can issue
to Phi for a real merchant account. Nothing in this prompt should invent a fake one, seed one into
a dev database and call it "connected," or otherwise make §3.1's checklist appear to have run.

Once Phi has connected a real staging studio (through the owner-facing UI or directly via this
command), the actual next step — running `docs/payments/runbook-studio-currency-migration-
2026-09-27.md` §3.1's live-transaction checklist — is itself a good candidate for a short,
focused follow-up overnight prompt once that prerequisite is met. Flag this in your final report
as the natural next piece of work; do not start it speculatively now.

---

## 8. Test requirements

- **Unit:** the new `FlowB_NeverReferencesPokTypes_UntilAmendmentDsContingencyIsLifted` test
  (§6.1) must exist, compile, and pass.
- **No other test should change.** Run the full `Pena_e_Arte.UnitTests` suite (and the existing
  `PaymentArchitectureTests` fixture specifically) and confirm nothing else regresses — this
  prompt's changes are additive comments plus one new test, so a regression anywhere else means
  something in this prompt was misapplied.
- **No integration tests needed.** Nothing here touches a database, an HTTP endpoint, or an
  external API.

---

## 9. Help-sync obligation (CLAUDE.md rule #7) — checked, not skipped

**No change to `helpContent.ts`, the standalone manual, or any onboarding tour.** This prompt
changes zero user-visible behavior: no client, owner, artist, or issuer sees, does, or is told
anything different after this ships. The two genuinely user-visible obligations this decision
will eventually carry — a "connect your payment provider" tour step for Flow A's POK sandbox
verification, and the `owner-billing`/`owner-subscribe` rewrite for Flow B's eventual move off
Stripe — are already tracked in `docs/payments/implementation-readiness-status-2026-07-31.md`'s
Help-sync table and in Amendment D itself; they trigger when that work ships, not by this prompt.

---

## 10. Industry-standard benchmark note (CLAUDE.md rule #6)

Not applicable in the usual sense — this prompt makes no product-facing change to benchmark
against the category (Vagaro/Fresha/Boulevard/etc.). The one relevant benchmark point is
process, not product: shipping an architecture fitness test to enforce a sequencing decision
(rather than relying on a comment or an engineer's memory) is itself the kind of engineering
discipline this repo already practices elsewhere (see the existing `NoMember_IsNamed-
PlatformFeeAmountOrSupportsSplit` test this prompt's new test is modeled on) — consistent with,
not a departure from, this codebase's own standard.

---

## 11. Final self-check

- [ ] Read `docs/payments/ADR-0001-amendment-D-pok-final-both-flows.md` in full before editing
      anything.
- [ ] New test added to `PaymentArchitectureTests.cs`, compiles, passes.
- [ ] Full `Pena_e_Arte.UnitTests` suite run; nothing else regressed.
- [ ] `StripeBillingService.cs` class doc comment added, exactly as specified in §6.2 — no
      method body touched.
- [ ] `InfrastructureServiceExtensions.cs` comment added above line 199 (verify the line number
      against current source before editing — this prompt cites 2026-10-02's line numbers;
      re-find the `AddScoped<IStripeBillingService` line if it moved), existing Flow A comment
      above line 191 left untouched.
- [ ] `runbook-studio-currency-migration-2026-09-27.md` §3.1 gets the one new paragraph from
      §6.4, nothing else in that file touched.
- [ ] No file outside this list touched: `PaymentArchitectureTests.cs`,
      `StripeBillingService.cs`, `InfrastructureServiceExtensions.cs`,
      `runbook-studio-currency-migration-2026-09-27.md`.
- [ ] No migration generated. No `appsettings*.json` touched. No secrets/Vault code touched.
- [ ] `dotnet build` clean. `dotnet format` clean (if this repo runs it in CI).
- [ ] Nothing in §2/§3's "do not re-litigate / do not guess" lists was decided by this prompt.
- [ ] Final report names Phase 2 (§7) as explicitly not attempted and explains why, and flags
      the "run §3.1 once a real studio is connected" follow-up as the natural next prompt.

---

## 12. Final deliverable spec

**Files changed:**
- `tests/Pena_e_Arte.UnitTests/Architecture/PaymentArchitectureTests.cs` — one new `[Fact]`.
- `Pena_e_Arte.Infrastructure/Services/StripeBillingService.cs` — one new class-level XML doc
  comment, no other change.
- `Pena_e_Arte.Infrastructure/Extensions/InfrastructureServiceExtensions.cs` — one new comment
  block, no other change.
- `docs/payments/runbook-studio-currency-migration-2026-09-27.md` — one new paragraph appended
  to §3.1.

**Commit message:**
```
chore(payments): guard Flow B against premature POK wiring, sync code comments to Amendment D

Adds an architecture fitness test (PaymentArchitectureTests) that fails the build if any Flow B
billing code references POK's types before ADR-0001 Amendment D's contingency — POK's
recurring-billing feature reaching production — is lifted. Adds pointer comments in
StripeBillingService and InfrastructureServiceExtensions so a future reader lands on Amendment D
instead of the superseded Polar plan. No behavior change; no new dependency.
```

Do not push or open a PR without Phi's review — per this repo's normal workflow, leave the branch
ready for review.
