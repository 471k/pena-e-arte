# EPIC-0001 — Completion summary (overnight run, 31 July 2026)

**Branch:** `epic-0001/pre-implementation-hardening` (pushed, NOT merged — human review
required; touches legal disclosure, consent/GDPR, retention, secrets, per-tenant credentials).
**Base:** `f3bf5d3` (this isolated worktree was one docs-only commit behind `main`'s `7e4196c`;
the missing commit is a K3s docs log, immaterial to every code phase).
**Mode:** fully autonomous, no user present. Executed across two sessions (Phases 1–2, then 3–5).

This is the direct-to-branch equivalent of a PR description (no PR flow configured).

---

## Commit history

| Commit | Phase | Description |
|---|---|---|
| `7a8d3c2` / `ab92a98` | setup | Import EPIC-0001 reference docs; empty checkpoint |
| `6f1491f` | **Phase 1 — PENA-100/101** | `feat(public): platform legal-entity disclosure + dead /privacy /terms link fix` |
| `1b26e7e` | **Phase 2 — PENA-102** | `feat(public): policy routes, public home shell, refund policy from live deposit rules` |
| `adc738d` | docs | Execution status + open questions + decisions log + this summary (after Phase 2) |
| `0b7bc45` | **Phase 3 — PENA-103** | `feat(consent): versioned consent templates with immutable snapshot; explicit audited health-data sharing consent` |
| `4f7a666` | **Phase 4 — PENA-104** | `feat(retention): two-stage soft-delete/hard-purge job, R2 delete capability, audited erasure-request path` |
| `59f7926` | **Phase 5 — PENA-105** | `feat(secrets): ISecretsProvider + local Vault dev service, per-tenant credential pointer schema, pre-commit gitleaks hook, Twilio/Instagram docker-compose fix` |
| `0c71d36` | **Phase 6 — PENA-106** | `refactor(payments): delete Stripe-aggregator IStripePaymentService, add provider-neutral IPaymentProvider + PlatformFeeAmount + architecture fitness test` |
| `d8b3c83` | **Phase 7 — PENA-107** | `ci: architecture-test visibility, Help-sync diff check, CONTRIBUTING.md` |

## Phases landed (all 7 of 7) — each verified to full DoD

- **Phase 1 (PENA-100/101)** — legal-entity disclosure + dead-link fix. ✅
- **Phase 2 (PENA-102)** — public policy pages + home shell + refund from live code. ✅
- **Phase 3 (PENA-103)** — consent versioning + immutable snapshot + audited cross-tenant
  sharing consent. ✅
- **Phase 4 (PENA-104)** — two-stage retention/purge job + `IR2Service.DeleteAsync` + audited
  erasure-request path. ✅
- **Phase 5 (PENA-105)** — `ISecretsProvider` + local Vault dev service + credential-pointer
  schema + pre-commit gitleaks hook + Twilio/Instagram docker-compose fix + ADR-0002 + runbook. ✅
- **Phase 6 (PENA-106)** — delete `IStripePaymentService`; provider-neutral `IPaymentProvider` +
  `PaymentProviderCapabilities` + `NullPaymentProvider`; `StripePaymentIntentId` →
  `ProviderReferenceId` + `Provider`/`Currency`/`HoldExpiresAt`/`PlatformFeeAmount`; RenameColumn
  migration (no data loss); reconciliation hold-expiry pass; `NetArchTest.Rules` fitness test. ✅
- **Phase 7 (PENA-107)** — architecture-test CI visibility + `help-sync` CI job + `CONTRIBUTING.md`
  (no duplicate gitleaks). ✅

**The epic is complete.** Every phase committed separately with its exact Deliverable message and
pushed. Nothing left outstanding except the founder Open Questions (below) and the intentionally
out-of-scope items (real POK provider implementation, production Vault cluster, final legal copy).

---

## Help-sync verdict per phase (DoD item 4)

- **Phase 1:** NO Help change needed — a footer/`<title>`/meta are not "how do I…" topics.
- **Phase 2:** NO `helpContent.ts` change (pre-login surfaces); the in-scope signup Terms/Privacy
  links WERE added; no tour covers pre-login pages.
- **Phase 3:** DONE — `helpContent.ts` `client-consent-sign` (full text shown before signing),
  `client-consent-list` + `artist-consent-view` (snapshot = what was agreed), `faq-portable-profile`
  corrected to state accurately what is/isn't shared; `user-manual/index.html` consent/sharing
  sections updated. Tours: NO change (clientTour never covered consent/sharing).
- **Phase 4:** NO Help change — retention is invisible background infra and the erasure endpoint
  ships with no user-visible UI trigger this phase (owner/support-only).
- **Phase 5:** NO Help change — infra/secrets plumbing has zero user-visible surface.
- **Phase 6:** DONE — Flow-A card wording neutralised in `helpContent.ts` (client-book tip, owner
  create-payment summary, faq-payment-methods) and `user-manual/index.html` (deposit checkout +
  refund warning). Flow-B billing copy deliberately KEPT as Stripe (it still uses Stripe Billing).
  No tour references Stripe. Frontend Stripe Elements card UI left in place (non-functional until
  POK's frontend ticket) — flagged as out of this backend refactor's scope.
- **Phase 7:** NO Help change — CI/DevOps tooling has zero user-visible surface.

## Industry-standard citation per phase

- **P1:** EU e-Commerce Directive Art. 5 + Albanian trader-identification disclosure.
- **P2:** GDPR Art. 13/14 + Law 124/2024; EU/Albanian distance-selling refund rules; WCAG 2.1 AA.
- **P3:** GDPR Art. 9/Art. 7 + Law 124/2024; immutable-snapshot consent (DocuSign/HelloSign).
- **P4:** GDPR Art. 5(1)(e)/Art. 17, NIST SP 800-53 SI-12; S3 lifecycle expire-then-delete.
- **P5:** OWASP ASVS V6, CWE-798, twelve-factor config; PCI DSS Req 3/6.
- **P6:** Architecture fitness function (Ford/Parsons, *Building Evolutionary Architectures*);
  NetArchTest = .NET ArchUnit; PCI DSS SAQ-A scope discipline (card data never touches this infra).
- **P7:** Fitness-function-in-CI (ArchUnit's own CI-enforcement assumption); path-based
  doc-sync CI checks (Kubernetes PR bots), scoped down for a solo-founder repo.

---

## Final self-check (at the end — all 7 phases landed)

- [x] `dotnet build "Pena e Arte.slnx" --configuration Release` — **clean** (0 errors).
- [x] `dotnet format "Pena e Arte.slnx" --verify-no-changes` — **clean**.
- [x] `dotnet test` — **green: 1446 unit + 330 integration** (0 failed), incl. the new
  architecture fitness test, hold-expiry, and PlatformFee-invariant tests.
- [x] `pnpm lint` — **0 errors** (15 pre-existing warnings in untouched files).
- [x] `pnpm build` (`tsc -b` + vite) — **clean**.
- [x] `pnpm test` — **1755+ passed; the only failures are the 3 pre-existing Stripe
  `PaymentMethodSelector` tests** (+ 1 flaky `HelpMenu` tour) — confirmed failing/flaky on the
  clean baseline with changes stashed; the refactor did not touch that component.
- [x] No `Console.WriteLine`/`console.log` introduced.
- [x] No secret/connection string/API key committed.
- [x] Every touched file inside the §4 scope boundary.
- [x] `IStripeBillingService.cs` / `StripeBillingService.cs` / `StripeDiscountService.cs` AND
  `SessionSplit.cs` / `UpdateSessionSplitsCommand.cs` byte-for-byte unchanged
  (`git diff f3bf5d3 --` empty for all five).
- [x] Every new/changed endpoint has `.RequireAuthorization()` (consent active-template →
  `ClientAndAbove`; erasure → `OwnerOnly`; Phase 6 added no endpoints).
- [x] Every phase's Help-sync verdict stated explicitly (in each commit body and above).
- [x] `## Open questions for the founder` present with all 8 items + phase tags.
- [x] `## Execution status` note present and accurate — updated to "COMPLETE: all 7 phases".
- [x] EF migrations verified additive/safe: `AddConsentTemplateAndSnapshot`,
  `AddStudioCredentialRef`, and `ReplaceStripePaymentIntentWithProviderReference` (EF used
  `RenameColumn`, no data loss) all applied cleanly against a scratch MySQL on the full chain.
- [x] Pre-commit gitleaks hook proven: staged private key BLOCKED, clean content ALLOWED.
- [x] Architecture test proven to FAIL on an injected `PlatformLedger`, PASS once removed.
- [x] Help-sync CI check proven: FAILS a gated change without Help; PASSES with Help / override /
  non-gated.

## Deviations / judgment calls beyond the spec

1. **Spec/reality mismatch (Phase 3), corrected:** the epic assumed the portable-profile opt-in
   shares Art. 9 medical notes/allergies. It does not — `PortableClientProfile` shares only
   display name, body-map locations, and tattoo history. Writing Help/Privacy copy claiming
   medical data is shared would be a false statement, so the enum is `CrossTenantProfileSharing`
   and every user-facing surface (toggle, Privacy Policy — Phase 2 copy corrected here too, user
   manual, `faq-portable-profile`) states the truth. This also fixed the pre-existing
   `faq-portable-profile` inaccuracy the epic flagged — truthfully, not as the epic assumed.
2. **`Assert.Skip` unavailable (Phase 5):** xUnit 2.9.3 has no dynamic skip, so the Vault
   happy-path test early-returns when `VAULT_ADDR`/`VAULT_TOKEN` are unset (runs where dev Vault
   exists); the always-run unreachable-Vault test covers the fail-closed contract in CI.
3. **Pre-commit hook via Docker (Phase 5):** gitleaks isn't installed locally, so the hook prefers
   a local binary but falls back to the official gitleaks Docker image (Docker is already required
   here). Made MSYS-safe + injects git `safe.directory` for the container; `.gitattributes` forces
   LF so the shebang survives on Linux/macOS.
4. **Worktree/branch mechanics (first session):** the spec files were untracked in the main
   checkout and absent from this isolated worktree; they were copied in and committed for a
   self-contained review branch. `§0 git checkout main && pull` could not run literally (worktree
   isolation); branched off `f3bf5d3` instead (one docs-only commit behind main).
5. **Flow A is intentionally non-functional after Phase 6 (by design, flagged):** deleting the
   Stripe aggregator with no POK provider yet means the DI default is `NullPaymentProvider`, which
   fails closed — card deposit endpoints throw at runtime until POK lands (the ADR-0001 sequel
   ticket). This is the intended post-refactor state (Amendment A required the aggregator deleted,
   not migrated), not a regression. The frontend Stripe Elements card UI was likewise left in
   place (it can't function without a backend provider) rather than ripped out — that belongs to
   POK's frontend ticket, out of this backend refactor's scope. Both are flagged, not silent.
6. **Bulk rename via scoped sed (Phase 6):** the `StripePaymentIntentId` → `ProviderReferenceId`
   field rename and the 5 method/type renames were applied with a scoped `sed` script (verified no
   Flow B collision first, excluded historical migrations). One over-broad match corrupted two
   doc-comments in my just-created files; both were caught and fixed before building. Every rename
   was validated by a clean compile + the full green test suite, not trusted blind.

---

## Founder resolutions — 1 Aug 2026 (follow-up pass, same branch)

The founder answered the 8 open questions + the refund finding; all wired into this branch in
three commits (config/docs; contact form; delete-account). Every item verified to the same DoD.

- **Q1 tagline/SEO** — kept `SITE_TAGLINE`; added distinct `SITE_META_DESCRIPTION` for
  description/og:description (legalEntity.ts + index.html).
- **Q2 domain/inbox** — `tattooos.co` / `support@tattooos.co` confirmed real; contact form routes
  there.
- **Q3 legal address** — `LEGAL_ENTITY_ADDRESS = "Rruga Pirro Goda, Tiranë, Albania"`, rendered on
  Privacy + Terms.
- **Q4 lawyer legal text** — STILL OPEN (not ready); `HAS_FINAL_LEGAL_COPY = false` + banner
  unchanged.
- **Q5 contact form** — built for real (anonymous, rate-limited, Resend send-only, ReplyTo,
  NOT persisted; Privacy sub-processor list updated). Help verdict stated in the commit.
- **Q6 retention** — 7 years / 2555 days for consent forms + body maps; 30-day grace; founder-
  confirmed comments replace the placeholders.
- **Q7 production secrets backend** — **HCP Vault** (managed); ADR-0002 + Decisions Log updated;
  no code change (same VaultSharp client).
- **Q8 delete-my-account UI** — client self-service flow on `MyProfilePage` (IDOR-proof, audited,
  confirmation flow); new Help article.
- **Refund finding** — no-show stays 100% forfeit; default notice window 24h → **48h**
  (`AppointmentSelfServiceDefaults`), refund/Help copy updated. Transferable-deposit idea deferred
  as new open question #10 (needs a credit-ledger feature).
