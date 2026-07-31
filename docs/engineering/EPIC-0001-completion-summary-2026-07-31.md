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

## Phases landed (5 of 7) — all verified to full DoD

- **Phase 1 (PENA-100/101)** — legal-entity disclosure + dead-link fix. ✅
- **Phase 2 (PENA-102)** — public policy pages + home shell + refund from live code. ✅
- **Phase 3 (PENA-103)** — consent versioning + immutable snapshot + audited cross-tenant
  sharing consent. ✅
- **Phase 4 (PENA-104)** — two-stage retention/purge job + `IR2Service.DeleteAsync` + audited
  erasure-request path. ✅
- **Phase 5 (PENA-105)** — `ISecretsProvider` + local Vault dev service + credential-pointer
  schema + pre-commit gitleaks hook + Twilio/Instagram docker-compose fix + ADR-0002 + runbook. ✅

## Phases NOT started (stopped at the Phase 5 boundary, last good `59f7926`)

- **Phase 6 (PENA-106)** — the `IStripePaymentService` → `IPaymentProvider` refactor,
  `Payment.PlatformFeeAmount`/`Currency`/`HoldExpiresAt`/`Provider`, migration, NetArchTest.
- **Phase 7 (PENA-107)** — architecture-test CI visibility, Help-sync CI check, `CONTRIBUTING.md`.

**Why the stop:** Phase 6 is the single largest, highest-risk phase (delete the aggregator
interface; a field rename cascading across 22 files / ~74 call sites; a data-preserving
migration; a new architecture test; provider-neutral naming) on payment code a reviewer will
scrutinise specifically. It could not be completed AND verified to a green build + test suite
within the remaining budget; the master prompt mandates stopping at a phase boundary rather than
pushing a half-done payments refactor. See the Execution status note in
`EPIC-0001-pre-implementation-hardening.md` for the full Phase 6 reference inventory (a clean
next pickup).

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

## Industry-standard citation per phase

- **P1:** EU e-Commerce Directive Art. 5 + Albanian trader-identification disclosure.
- **P2:** GDPR Art. 13/14 + Law 124/2024; EU/Albanian distance-selling refund rules; WCAG 2.1 AA.
- **P3:** GDPR Art. 9/Art. 7 + Law 124/2024; immutable-snapshot consent (DocuSign/HelloSign).
- **P4:** GDPR Art. 5(1)(e)/Art. 17, NIST SP 800-53 SI-12; S3 lifecycle expire-then-delete.
- **P5:** OWASP ASVS V6, CWE-798, twelve-factor config; PCI DSS Req 3/6.

---

## Final self-check (at the Phase 5 boundary)

- [x] `dotnet build "Pena e Arte.slnx"` — **clean** (0 errors).
- [x] `dotnet format "Pena e Arte.slnx" --verify-no-changes` — **clean**.
- [x] `dotnet test` — **green: 1446 unit + 330 integration** (0 failed). Integration run with
  `VAULT_ADDR`/`VAULT_TOKEN` set so the Vault-backed provider test exercises a real dev Vault.
- [x] `pnpm lint` — **0 errors** (15 pre-existing warnings in untouched files).
- [x] `pnpm build` (`tsc -b` + vite) — **clean**.
- [x] `pnpm test` — **1755 passed; 4 pre-existing failures unrelated to this work** (3 Stripe
  `PaymentMethodSelector` + 1 flaky `HelpMenu` tour — all confirmed failing/flaky on the clean
  baseline with these changes stashed; none touch any file this epic changed).
- [x] No `Console.WriteLine`/`console.log` introduced.
- [x] No secret/connection string/API key committed (the dev-mode Vault token is a labelled
  non-prod placeholder; new `appsettings`/compose entries are empty or env-substituted).
- [x] Every touched file inside the §4 scope boundary.
- [x] `IStripeBillingService.cs` / `StripeBillingService.cs` / `StripeDiscountService.cs`
  byte-for-byte unchanged (`git diff f3bf5d3 --` empty).
- [x] Every new/changed endpoint has `.RequireAuthorization()` (consent active-template →
  `ClientAndAbove`; erasure → `OwnerOnly`).
- [x] Every phase's Help-sync verdict stated explicitly (in each commit body and above).
- [x] `## Open questions for the founder` present with all 8 items + phase tags.
- [x] `## Execution status` note present and accurate, last good commit `59f7926`.
- [x] EF migrations verified: `AddConsentTemplateAndSnapshot` and `AddStudioCredentialRef` are
  additive (new tables/nullable columns), applied cleanly against a scratch MySQL on the full chain.
- [x] Pre-commit gitleaks hook proven end-to-end: staged private key BLOCKED, clean content ALLOWED.

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
