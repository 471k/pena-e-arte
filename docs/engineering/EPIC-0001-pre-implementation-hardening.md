# EPIC-0001 — Pre-implementation hardening (steps 0–6)

## Execution status — 31 July 2026 (COMPLETE: all 7 phases landed)

Autonomous run on branch `epic-0001/pre-implementation-hardening` (branched off
`f3bf5d3`; this worktree was one docs-only commit behind `main`'s `7e4196c` — the
missing commit is `docs: log K3s production-deployment spec`, immaterial to every
code phase here). Executed across three sessions: Phases 1–2, then 3–5, then 6–7.
**All seven phases (PENA-100 → PENA-107) are landed and verified.**

**Landed — each verified to the full Definition of Done for its layer** (backend:
`dotnet build` clean, `dotnet format --verify-no-changes` clean, `dotnet test`
green; frontend: `pnpm lint` 0 errors, `pnpm build` clean, `pnpm test` green):

- **Phase 1 — PENA-100/101** (`6f1491f`): legal-entity disclosure
  (`legalEntity.ts`, `SiteFooter.tsx`), index.html title/description/OG meta,
  `appsettings.json` LegalEntityName/Nipt, cookie-banner `/privacy` link, dead
  `/privacy` `/terms` link fix via real policy routes + public Home surface.
- **Phase 2 — PENA-102** (`1b26e7e`): the four policy pages + Home fleshed out
  (Privacy with `[LAWYER REVIEW REQUIRED]` banner, Terms, Refund from live deposit
  code, Contact, Home) + signup Terms/Privacy consent lines on both register pages.
- **Phase 3 — PENA-103** (`0b7bc45`): `ConsentTemplate` (versioned, nullable-studio,
  Kind discriminator) + immutable `ConsentTextSnapshot` resolved server-side at
  signing; migration `AddConsentTemplateAndSnapshot`; audited cross-tenant
  profile-sharing opt-in/opt-out (`UpdatePortableProfileOptInCommand` now
  `IAuditableCommand`); active-template query + endpoint; frontend renders template
  before signing and the snapshot on the detail page. **FINDING corrected:** the
  epic's premise that the portable profile shares Art. 9 medical notes/allergies is
  false — `PortableClientProfile` shares only tattoo history + body map; enum named
  `CrossTenantProfileSharing`, and Help/Privacy copy states this truthfully. Tests:
  resolver unit tests + snapshot-immutability + opt-in/opt-out audit integration.
- **Phase 4 — PENA-104** (`4f7a666`): `IR2Service.DeleteAsync` (new); two-stage
  `RetentionPurgeJob` (soft-delete → grace → hard-purge + R2 delete), registered in
  `Program.cs` via `AddOrUpdate` (NOT `IJobScheduler`); `App:RetentionDays` config
  (placeholders); `RequestDataErasureCommand` (owner/support endpoint, audited,
  distinct from automatic purge). No self-service erasure UI (open question §3.8).
- **Phase 5 — PENA-105** (`59f7926`): docker-compose Twilio/Instagram env fix +
  `.env.example`; `ISecretsProvider` (fail-closed) + `VaultSecretsProvider`
  (VaultSharp) + local Vault dev-mode compose service; `StudioCredentialRef`
  pointer schema + migration; `.githooks/pre-commit` gitleaks hook (proven to block
  a staged secret); `docs/infra/ADR-0002-secrets-management.md` + rotation runbook.

- **Phase 6 — PENA-106** (`0c71d36`): deleted `IStripePaymentService`/
  `StripePaymentService` outright; provider-neutral `IPaymentProvider` +
  `PaymentProviderCapabilities`; `NullPaymentProvider` (fail-closed DI default);
  `Payment.StripePaymentIntentId` → `ProviderReferenceId` (renamed across ~22 files/
  ~74 call sites) + new `Provider`/`Currency`/`HoldExpiresAt`/`PlatformFeeAmount`;
  migration `ReplaceStripePaymentIntentWithProviderReference` (EF auto-detected the
  rename as `RenameColumn` — no data loss; verified on a scratch DB);
  `PaymentReconciliationJob` gains a third hold-expiry auto-release pass;
  `NetArchTest.Rules` architecture fitness test (no platform-balance-ledger type);
  Flow-A Stripe wording neutralised in Help/manual (Flow-B billing kept). SessionSplit
  and Flow B untouched.
- **Phase 7 — PENA-107** (`d8b3c83`): architecture-test visibility step in the
  backend CI job; new `help-sync` CI job (did-you-update-the-docs, `[skip-help-sync]`
  override); `CONTRIBUTING.md` at repo root. No duplicate gitleaks step. Both checks
  proven locally (arch test fails on an injected `PlatformLedger`; help-sync fails on
  a gated change without Help).

**Outcome:** all seven phases landed, each committed separately with the exact
Deliverable message, and pushed. Final self-check green — see
`docs/engineering/EPIC-0001-completion-summary-2026-07-31.md`. Branch is for human
review; NOT merged, no PR (touches payments, secrets, consent/GDPR).

---

## Open questions for the founder

These require a decision from Phi (and, for the legal items, the Albanian
data-protection lawyer) — engineering deliberately did not guess them. Numbering
matches the master prompt's §3.

1. **(Phase 1)** Final tagline / meta-description copy for `<title>` /
   `og:description`. Currently wired to the placeholder `SITE_TAGLINE` =
   "TattooOS — booking & studio management for tattoo shops" in
   `frontend/src/shared/constants/legalEntity.ts` (source of truth) and mirrored
   literally in `frontend/index.html`.
2. **(Phase 1)** Is `tattooos.co` the domain that will go on PSP/MoR
   applications, and is `support@tattooos.co` a monitored inbox? (The Contact
   page currently points there.)
3. **(Phase 1)** Registered legal address for `LEGAL_ENTITY_ADDRESS` — left blank
   and wired in `legalEntity.ts`.
4. **(Phase 2)** Final Privacy Policy / Terms of Service legal text from the
   Albanian data-protection lawyer. Structural placeholders shipped with a
   visible `[LAWYER REVIEW REQUIRED]` banner gated on `HAS_FINAL_LEGAL_COPY`
   (flip one constant to remove the banner).
5. **(Phase 2)** `/contact`: monitored inbox vs. contact form? Shipped as a
   monitored-inbox placeholder. If a form is chosen, collect name/email/message
   only and add it to the Privacy sub-processor/retention inventory.
6. **(Phase 4 — not yet built)** Retention periods for consent forms / body maps /
   medical notes after last appointment or account closure. To ship as
   `App:RetentionDays:*` config defaulting to a clearly-commented placeholder
   (730 days / 2 years) — NOT final numbers.
7. **(Phase 5 — not yet built)** Vault vs. Infisical/Doppler for the eventual
   production deployment. Default to build is Vault dev-mode locally; ADR-0002
   must present both with the tradeoff.
8. **(Phase 4 — not yet built)** Should a client-facing "delete my account" UI be
   built? Confirmed absent today. Scope is the backend retention/purge + audited
   erasure-request path; a self-service UI is a separate follow-up ticket unless
   the erasure path proves unusable without one.

### Additional finding flagged during Phase 2 (business decision, not a bug)

The `/refund-policy` copy is descriptive of the *current implemented* behavior:
a no-show forfeits the deposit in full, and the default late-cancellation refund
is 0% (deposit forfeited) unless a studio configures otherwise, with a 24-hour
default notice window. Whether that is the *intended* commercial policy is a
founder decision — it was documented truthfully, not silently rewritten.

---

**Status:** Ready for engineering · **Date:** 31 July 2026 · **Owner:** Phi
**Blocks:** ADR-0001 provider integrations (POK, easyPos, Polar)
**Source documents:** `docs/payments/ADR-0001-payment-providers.md`,
`docs/payments/ADR-0001-amendment-A-verified-repo-state.md`,
`docs/payments/implementation-readiness.md`, `CLAUDE.md`
**Verified against:** commit `7e4196c` on `main`, spot-checked directly against the source tree
while writing this epic (file/line references below are real, not inferred)

---

## How to use this document

Eight tickets, PENA-100 through PENA-107, sequenced exactly as ADR-0001 Amendment A ordered them
(step 0 → step 6), plus one addition (PENA-107, CI enforcement — not in the original list, added
because rules 6 and 7 of `CLAUDE.md` are unenforced today and every ticket below creates new ways
to violate them). Work top to bottom; PENA-100 through PENA-105 have no dependency on each other
and can run in parallel across two engineers, but PENA-106 depends on PENA-105 landing first
(secrets management must exist before any provider credential is issued) and PENA-107 should land
alongside PENA-100 so the gates are in place before the rest of the epic starts generating PRs.

**None of these tickets require an external account, credential, or approval.** That is the point
— this is the work that starts today, per `implementation-readiness.md` §7 Week 0–1.

### Definition of Ready (every ticket below already meets this)

- Problem statement traces to a specific file, line, or verified gap — not a hypothesis
- Acceptance criteria are testable, not aspirational
- No ticket is blocked on a PSP, MoR, or accountant decision

### Definition of Done (applies to every ticket — do not close without all seven)

1. Acceptance criteria met and demonstrated (screenshot, test run, or `curl` trace in the PR)
2. Unit and/or integration tests added per the **Testing** section — CLAUDE.md: *"Skip writing a
   test for business logic in the Application layer"* is a listed non-negotiable, not a suggestion
3. No `console.log`/`Console.WriteLine` introduced; structured Serilog logging only, and payment-
   or health-adjacent log lines carry zero PII (name/email/phone/card/health text) — grep the diff
   for the client's/studio's actual data before requesting review
4. If the change touches anything a client, artist, owner, or issuer can see or do: `helpContent.ts`,
   `frontend/public/user-manual/index.html`, and the relevant file(s) under
   `frontend/src/features/help/tours/` are updated **in the same PR** — CLAUDE.md rule 7. A ticket
   that changes user-facing behavior without a Help diff fails review on that basis alone, no
   exceptions.
5. No secret, connection string, or API key added to source, `appsettings.json`,
   `appsettings.Development.json`, or committed anywhere — env var or (post-PENA-105) Vault only
6. PR description states which CLAUDE.md rule(s) the change is in service of
7. Reviewer independently re-runs the acceptance-criteria demonstration, not just reads the diff

### Sequencing

| # | Ticket | Track | Depends on | Unblocks |
|---|---|---|---|---|
| 0 | PENA-100 — Brand & legal-entity disclosure | Frontend | — | Every PSP/MoR application (KYC compares live site to QKB extract) |
| 1 | PENA-101 — Fix dead `/privacy` and `/terms` links | Frontend | — | PENA-102 |
| 2 | PENA-102 — Public shell + policy routes | Frontend | PENA-101 | Every PSP/MoR application |
| 3 | PENA-103 — Health-data consent, versioning, cross-tenant review | Full-stack | — | Launch (Art. 9 exposure, independent of payments) |
| 4 | PENA-104 — Retention & deletion job | Backend | — | Launch (GDPR/Law 124-2024 storage-limitation duty) |
| 5 | PENA-105 — Secrets management | Backend/Infra | — | PENA-106, first studio credential of any kind |
| 6 | PENA-106 — `IPaymentProvider` refactor + architecture test | Backend | PENA-105 (for the credential-handling half) | POK integration (ADR-0001 step 8) |
| 7 | PENA-107 — CI gates for Help-sync and architecture tests | DevOps | — | Makes DoD items 2 and 4 machine-enforced instead of review-enforced |

---

## PENA-100 — Brand & legal-entity disclosure

**Priority:** P0, blocks every external application · **Track:** Frontend · **Est:** 1–2 days

### Decision this ticket implements

**TattooOS stays the user-facing brand.** No rename, no find-and-replace across the 109 files
that currently reference it (`frontend/src/features/**`, email templates, backend command names).
That work would be pure churn against no defect — TattooOS is fine as a product name and nothing
about Article 4(g), KYC, or Law 124/2024 requires it to change.

**What KYC actually checks is different from what this ticket assumed at first pass.** A payment
processor or MoR reviewing a merchant application does not require the trading name and the legal
name to be identical string-for-string — it requires the legal entity to be **discoverable and
consistent** on the live site, the same pattern Stripe-powered and Wise-powered merchants use
every day (product name "Notion", legal entity "Notion Labs, Inc." in the footer; product name
"Linear", legal entity "Linear Orbit, Inc."). Today Pena e Artë has **neither half** of that
pattern: `Studio.cs`, the email templates, and 100+ frontend files say "TattooOS"; nothing on the
public site says "Pena e Artë," the NIPT, or a registered address anywhere a KYC reviewer or a
studio's own compliance-conscious owner would look.

### Problem, verified

- `frontend/index.html:7` — `<title>frontend</title>`. The page title is the Vite scaffold
  default. It has shipped unbranded this whole time.
- No `<meta name="description">`, no Open Graph tags, in `frontend/index.html` at all.
- `frontend/src/features/public/components/PublicPageHeader.tsx:34` — `BrandMark` renders
  `TattooOS` — correct, this is the intended consumer brand, leave it.
- No footer component exists anywhere under `frontend/src/features/public/components/` or
  `frontend/src/shared/components/` — there is nowhere on the site a legal entity line could even
  go today.
- `Studio.cs` has no analogue for the platform's own identity — this is about Pena e Artë's own
  entity disclosure, not a studio's.

### Acceptance criteria

- [ ] `frontend/index.html`: `<title>` set to `TattooOS — booking & studio management for tattoo
      shops` (or the founder's preferred tagline — copy is not engineering's call, wire it up as
      a constant so it's a one-line change), plus `<meta name="description">` and
      `og:title`/`og:description`/`og:image` tags.
- [ ] New `frontend/src/shared/components/SiteFooter.tsx`, rendered on every public route
      (`/discover`, `/s/:slug`, `/artist/:slug`, `/login`, `/register`, `/client-register`, and
      the new `/privacy`, `/terms`, `/refund-policy`, `/contact` routes from PENA-102). Contains,
      at minimum:
      - `© {currentYear} TattooOS`
      - `TattooOS is operated by Pena e Artë, NIPT M12219042B` (read from a config constant, see
        below — never hardcode the NIPT string in more than one place)
      - Links: Privacy Policy, Terms of Service, Refund Policy, Contact
- [ ] New single source of truth for the legal-entity string —
      `frontend/src/shared/constants/legalEntity.ts` exporting `LEGAL_ENTITY_NAME`,
      `LEGAL_ENTITY_NIPT`, `LEGAL_ENTITY_ADDRESS` (address may be blank until the founder supplies
      one — do not block the ticket on it, wire the field so filling it in later is a one-line
      change). `SiteFooter`, the Terms/Privacy page templates (PENA-102), and any future invoice
      or receipt template all read from this file — never re-type the NIPT.
- [ ] Backend equivalent: add `App:LegalEntityName`, `App:LegalEntityNipt` to `appsettings.json`
      (empty defaults, populated via env var like every other section in that file) for any
      backend-rendered surface that needs it later (e-Fatura template, PDF receipts via
      `ConsentFormPdfService`-style rendering). Not required to be consumed anywhere yet — just
      don't let the frontend be the only place this fact lives, or it drifts.
- [ ] Confirm with the founder (not an engineering decision) whether `tattooos.co` is the domain
      that will actually be submitted on PSP/MoR applications, and whether `support@tattooos.co`
      (`.env.example:33`, `VITE_CONTACT_EMAIL`) is a live, monitored inbox — an unmonitored
      contact address is a common rejection reason independent of branding.

### Explicitly out of scope

- Renaming any C# type, MediatR command, database table, or frontend component from
  `Stripe*`/`TattooOS`-adjacent names — those are internal identifiers, not the KYC surface.
- Final legal copy for the policy pages — that's PENA-102, and the text itself comes from the
  Albanian lawyer per `implementation-readiness.md` §3, not engineering.
- Picking a permanent registered address if the founder is still deciding — leave the field empty
  and wired.

### Industry standard

Trader-identification disclosure (legal name, registration number, contact) on a commercial site
is required under both the EU's e-Commerce Directive Art. 5 and Albania's own consumer-protection
and e-commerce framework, and it is exactly the pattern every PSP/MoR onboarding flow checks for —
this isn't a Pena e Artë-specific requirement, it's baseline. "Brand name in the header, legal
entity in the footer" is the universal solve (see Stripe, Notion, Linear, Vercel — all run this
exact split).

### Testing

- `SiteFooter` unit test asserting the legal-entity string renders and all four links point to
  real routes (will fail until PENA-102 lands the routes — fine, land PENA-101/102 first or stub).
- `useDocumentMeta`-style test (there's already a precedent test file:
  `frontend/src/shared/utils/__tests__/useDocumentMeta.test.ts`) extended or matched for the new
  `<title>`/meta tags.

---

## PENA-101 — Fix dead `/privacy` and `/terms` links

**Priority:** P0, trivial effort/high asymmetry · **Track:** Frontend · **Est:** < 1 hour

### Problem, verified

`frontend/src/app/router.tsx` has no `/privacy` or `/terms` route in its 41-route table (lines
105–363, read in full). The catch-all at line 362, `{ path: "*", element: <CatchAllRedirect /> }`,
combined with `CatchAllRedirect` (lines 72–76) — which sends unauthenticated visitors to
`/discover` and authenticated ones to their role home — means **any current link to `/privacy` or
`/terms` silently redirects away instead of 404ing**, which is worse than a 404: a 404 tells a
reviewer or a client something is broken; a silent redirect to the studio directory looks like the
policy pages just don't exist, or were deliberately hidden.

### Acceptance criteria

- [ ] Grep the frontend for every existing `to="/privacy"` / `to="/terms"` reference (known
      location: `LoginPage.tsx`; likely also `SiteFooter` once PENA-100 lands, and the
      `CookieConsentBanner.tsx` component, which should also link out to the Privacy Policy — check
      it) and confirm they all resolve once PENA-102's routes exist.
- [ ] Until PENA-102 ships the real pages, add **placeholder routes** (`element: <PolicyComingSoonPage />`
      or similar, clearly not the catch-all) so the link is never dead even for the few hours/days
      between merging PENA-101 and PENA-102 — or land PENA-101 and PENA-102 as a single PR. Given
      the size of PENA-102, landing them together is the cleaner choice; keep this as a separate
      ticket only if PENA-102 will take materially longer.
- [ ] Add a `frontend/src/app/__tests__/router.test.tsx` (or extend the existing router test if
      one exists under `frontend/src/app/__tests__/`) case asserting `/privacy` and `/terms` do
      **not** hit `CatchAllRedirect`.

### Testing

Route-level test as above. This is the cheapest regression test in the whole epic to write —
there is no excuse for skipping it.

---

## PENA-102 — Public shell + policy routes

**Priority:** P0 · **Track:** Frontend (content from lawyer, structure from engineering) · **Est:** 2–4 days

### Problem, verified

There is no public marketing surface. `IndexRedirect` (`router.tsx:66–70`) sends every
unauthenticated root visit straight to `/discover`, a studio directory — there is no page that
explains what TattooOS/Pena e Artë *is* before asking someone to either book a tattoo or register
a studio. Every PSP and MoR reviews the live site as part of underwriting; a directory with no
"what is this" page and no policies is the single most common rejection cause for Albanian
applicants per prior research (`implementation-readiness.md` §1).

### Acceptance criteria

- [ ] `/privacy` route → Privacy Policy page. Structure (headings) built by engineering now with
      lorem-ipsum-free but clearly-marked placeholder copy (`[LAWYER REVIEW REQUIRED]` banner at
      top, removed when final text lands); final text supplied by the Albanian data-protection
      lawyer per `implementation-readiness.md` §9. Must cover, at minimum, at the structural level:
      what personal data is collected (incl. Art. 9 health data — cross-reference PENA-103),
      purposes, legal basis per category, retention (cross-reference PENA-104), sub-processor list
      (POK, easyPos, Polar once live, Cloudflare R2, Resend, Twilio, hosting provider), data
      subject rights under Law 124/2024, DPO/controller contact.
- [ ] `/terms` route → Terms of Service page, same placeholder-then-lawyer-fills pattern.
- [ ] `/refund-policy` route → explicit about deposits, no-shows, and who refunds what (this one
      engineering can draft accurately today — the deposit/cancellation logic already exists in
      `DepositRule`/`DepositCalculator`/`ClientCancellationPolicy`, so the policy page should
      **describe the actual implemented behavior**, not aspirational behavior; pull the real rules
      from those classes rather than writing new ones).
- [ ] `/contact` route → real contact channel (confirm with founder: monitored inbox vs. contact
      form; if a form, it must not accept and store data beyond what's needed to respond — keep it
      simple, name/email/message, and it becomes another entry in the sub-processor/retention
      inventory).
- [ ] New minimal public "About/Home" surface — does not have to be elaborate, but a first-touch
      visitor should land somewhere that says what the product is and links to Discover, Register
      studio, and the policy pages, rather than landing directly inside the studio directory.
      Reuse `PublicPageHeader` and the new `SiteFooter` from PENA-100.
- [ ] Router change: add all new routes to `router.tsx` alongside the existing top-level public
      routes (same pattern as `/discover`, `/map`, etc. — outside the authenticated `AppRoot` tree).
- [ ] `LoginPage.tsx:251,255` links now resolve correctly — verify by clicking through, not just
      by grep.
- [ ] Verify `CookieConsentBanner.tsx` correctly links to the now-real `/privacy` route and that
      its consent categories match what the Privacy Policy will describe (placeholder note is fine
      if the lawyer hasn't delivered final copy yet — the *mechanism* must be correct now).

### Explicitly out of scope

- Final legal text — flagged inline in the page itself, not an engineering blocker to merging the
  structure.
- Any backend change. This is entirely frontend routes/components.

### Industry standard

WCAG 2.1 AA applies to these pages same as any other (the `design:accessibility-review` skill is
available if a dedicated a11y pass is wanted before this ships — worth running once real copy is
in, not on placeholder text). GDPR Art. 13/14 and the EU/Albanian consumer-protection distance-
selling rules set the substantive content bar for what a Privacy Policy and refund policy must
disclose; engineering's job is the structure and the discipline of keeping the refund policy
truthful to the actual deposit-rules code, not a marketing copy exercise.

### Testing

Route tests per page (renders, no console errors, footer/header present). No business logic here,
so no Application-layer test obligation — but still subject to DoD item 4 if any of this touches
what a client sees at signup (it does — add the relevant Help/onboarding-tour note if these pages
change first-run behavior).

---

## PENA-103 — Health-data consent, versioning, and `AllowCrossTenantRead` review

**Priority:** P0, largest compliance surface in the epic · **Track:** Full-stack · **Est:** 4–6 days

### Problem, verified

`ConsentForm.cs` (read in full):

```csharp
public class ConsentForm : TenantEntity
{
    public Guid ClientId { get; set; }
    public Guid AppointmentId { get; set; }
    public string? FileUrl { get; set; }
    public DateTime? SignedAt { get; set; }
    public string? SignatureData { get; set; }
    ...
}
```

This stores **that** a client signed and **when**, but not **what they signed**. There is no
consent text, no version, no snapshot. If a studio's consent wording changes six months from now
— which it will, because `implementation-readiness.md` §3 requires a lawyer-reviewed consent
wording that doesn't exist yet — every `ConsentForm` row ever created before that change becomes
unprovable: you cannot show a regulator, or a client in a dispute, what they actually agreed to.
For Article 9 special-category health data (allergies, medications, skin conditions, the body
map), this is the single largest compliance gap identified across every readiness document.

`ClientProfile.cs` (read in full):

```csharp
public class ClientProfile : TenantEntity
{
    ...
    public string? MedicalNotes { get; set; }
    public string? Allergies { get; set; }
    public BodyMap BodyMap { get; set; } = new();
    public bool AllowCrossTenantRead { get; private set; } = false;
    public DateTime? CrossTenantOptInAt { get; private set; }
    ...
    public void OptInToCrossTenant() { ... }
    public void OptOutOfCrossTenant() { ... }
}
```

`AllowCrossTenantRead` gates whether `MedicalNotes` and `Allergies` — Article 9 data — become
readable by a **second, unrelated studio** the client visits later ("portable profile"). The
toggle exists and is well-modeled as a domain method (good precedent — keep that pattern), but
there is nothing distinguishing this from a generic profile-sharing preference. GDPR Art. 9(2)(a)
requires **explicit** consent for processing special-category data, and Art. 7 requires that
consent be specific, informed, and as easy to withdraw as to give. A single boolean with generic
copy ("share my profile with other studios") does not meet the "explicit, specific to the
category of data" bar for health information — the client needs to understand, at the point of
opt-in, that they are specifically authorizing a second business to see their allergies and
medical notes, not just their name and appointment history.

### Acceptance criteria — consent versioning

- [ ] New `ConsentTemplate` entity (or equivalent naming the team prefers — the shape matters more
      than the name): `Id`, `StudioId` (nullable — templates may be platform-default or
      studio-customized, follow the same nullable-StudioId pattern already used in
      `AuditLogEntry` for platform-wide vs. studio-scoped rows), `Version` (string or int,
      monotonic), `BodyText` (the full consent language), `EffectiveFrom` (DateTime), `IsActive`.
- [ ] `ConsentForm` gains: `ConsentTemplateId` (FK), and — critically — a **snapshot** field,
      e.g. `ConsentTextSnapshot`, populated at signing time with the exact rendered text the client
      saw. Do not rely on the FK alone; templates must remain editable going forward without
      retroactively changing what a past signature legally represents. This mirrors how e-signature
      platforms (DocuSign, HelloSign) and every HIPAA-adjacent consent flow handle the same problem
      — snapshot, never live-reference, for anything already signed.
- [ ] EF Core migration adding these fields/entity. Follow the existing migration-naming and
      `#nullable disable` convention seen in `20260611223749_RemoveStripeConnect.cs`.
- [ ] `SignConsentFormPage.tsx` (frontend) renders the active template's `BodyText` at sign time
      and the backend command persists the snapshot — not a live re-render risk where the page
      could theoretically show one version and persist a different one due to a race with a
      template edit. Resolve the active template server-side at submission, not client-side.
- [ ] `ConsentFormDetailPage.tsx` displays the snapshot text, so anyone reviewing a past
      consent — client, studio, auditor — sees exactly what was agreed to, not the current template.

### Acceptance criteria — health-data-specific consent

- [ ] Split the current single `AllowCrossTenantRead` boolean into an explicit, separately-worded
      health-data consent step. Minimum bar: the opt-in UI copy must name the specific data
      categories being shared (allergies, medical notes — not "your profile") before the toggle can
      be set, and the persisted record should be auditable back to that specific wording (reuse the
      `ConsentTemplate`/snapshot pattern above rather than inventing a second mechanism — one
      versioned-consent system, used for both appointment consent forms and this toggle).
- [ ] `OptInToCrossTenant()` / `OptOutOfCrossTenant()` and their command handlers implement
      `IAuditableCommand` (existing interface, `Pena_e_Arte.Domain/Interfaces/IAuditableCommand.cs`
      — same pattern already used elsewhere via `AuditLogBehavior` in the MediatR pipeline). This
      is currently unaudited; per Amendment A Finding, payment-adjacent actions were flagged as
      not-yet-audited — this is the health-data analogue of that same gap and should close the
      same way.
- [ ] Confirm withdrawal is genuinely one step, matching Art. 7(3)'s "as easy to withdraw as to
      give" requirement — `OptOutOfCrossTenant()` already exists and looks compliant on inspection;
      verify the frontend surfaces it as prominently as the opt-in, not buried deeper in settings.

### Explicitly out of scope

- The DPIA and DPO-appointment decision itself — those are `implementation-readiness.md` §6 items
  for the founder and an external privacy consultant, not an engineering deliverable. This ticket
  makes the *system* auditable and versioned so a DPIA has something real to assess; it doesn't
  replace the DPIA.
- Retention/deletion of consent forms — that's PENA-104.

### Industry standard

GDPR Art. 9 (special-category data), Art. 7 (conditions for consent — specific, informed,
unambiguous, freely given, as easy to withdraw as to give), mirrored by Albania's Law 124/2024.
Consent-versioning-with-immutable-snapshot is standard practice anywhere legally significant
consent is captured (e-signature platforms, clinical/health intake systems, App Store/Play Store
privacy-consent flows post-2021).

### Testing

- Application-layer unit tests for the new consent-template resolution logic and the split
  health-data opt-in command (CLAUDE.md non-negotiable — do not skip).
- Integration test: sign a consent form, edit the active template, re-fetch the original
  `ConsentForm` and assert the snapshot text is unchanged.
- Integration test: `OptInToCrossTenant` produces an `AuditLogEntry` with the correct
  `AuditAction`/`AuditTargetType`.

### Help sync (DoD item 4 — do not skip)

`SignConsentFormPage` and the cross-tenant-sharing toggle already have Help articles per the 83
existing entries in `helpContent.ts` — find and update the relevant `client-*` and profile-sharing
articles to describe the new versioned-consent and explicit health-data-sharing copy, and check
whether the client onboarding tour (`frontend/src/features/help/tours/clientTour.ts`) walks
through consent signing — if so, update its step text to match.

---

## PENA-104 — Retention & deletion job

**Priority:** P0 · **Track:** Backend · **Est:** 2–3 days

### Problem, verified

There is no retention or deletion mechanism anywhere in the codebase for consent forms, body maps,
or client data generally. GDPR Art. 5(1)(e) (storage limitation) and Law 124/2024's equivalent
require personal data to be kept no longer than necessary for the purpose it was collected for,
and Art. 17 (right to erasure) requires a working deletion path on request. Neither exists today.

### Acceptance criteria

- [ ] Define the retention policy itself is **not** an engineering decision — get the actual
      retention periods (how long are consent forms/body maps/medical notes kept after a client's
      last appointment, or after account closure) from the founder and the data-protection lawyer
      per `implementation-readiness.md` §9 before hardcoding a number. Engineering's job is to make
      the period configurable (e.g. `App:RetentionDays:ConsentForms` in `appsettings.json`, same
      section-per-concern pattern as everything else in that file), not to pick it.
- [ ] New `RetentionPurgeJob` in `Pena_e_Arte.Infrastructure/Jobs/`, following the existing
      constructor-injection / `RunAsync(CancellationToken)` pattern used by
      `PaymentReconciliationJob.cs` (constructor takes `IAppDbContext` plus whatever else it needs;
      single public `RunAsync` entry point; uses `IgnoreQueryFilters()` where it must operate
      across tenant boundaries, exactly as `PaymentReconciliationJob` does for cross-tenant
      reconciliation).
- [ ] Register the job through the existing `IJobScheduler` abstraction
      (`Pena_e_Arte.Domain/Interfaces/IJobScheduler.cs`) rather than wiring Hangfire directly in
      `Program.cs` — stay consistent with how every other recurring job in this codebase is
      registered.
- [ ] Implement a **soft-delete-then-hard-purge** two-stage flow, not immediate hard delete:
      client-initiated or policy-triggered deletion marks records (reuse the existing `DeletedAt`
      field already present on `TenantEntity`-derived entities — confirm it's there before adding
      a new one) and a second pass, after a short grace window (e.g. 30 days, configurable),
      actually removes the row and any associated R2-stored files (consent PDFs, body-map images)
      via `IR2Service`. This protects against accidental deletion and matches how every mature SaaS
      handles "right to erasure" — never a single irreversible DELETE triggered directly from a
      user action.
- [ ] Explicit **right-to-erasure request path**: a client-initiated deletion request (self-service
      or via a support/owner action) that's distinguishable in the audit log from
      policy-driven/automatic purges — use `IAuditableCommand` again here.
- [ ] 72-hour breach-notification **process** is a separate, largely non-code deliverable
      (`implementation-readiness.md` §6) — out of scope for this ticket beyond ensuring the audit
      log (already built) gives whoever runs that process enough forensic trail to know what was
      accessed and when.

### Explicitly out of scope

- The actual number of days data is retained — lawyer/founder decision, engineering just makes it
  configurable.
- Building a client-facing "delete my account" UI if one doesn't already exist — check first
  (`MyProfilePage.tsx` is the likely home for it); if it's missing, that's arguably its own ticket,
  flag it rather than silently absorbing new scope into this one.

### Industry standard

GDPR Art. 5(1)(e), Art. 17; NIST SP 800-53 SI-12 (information management and retention). Two-stage
soft-delete/hard-purge with a grace window is the standard pattern (see AWS's own S3 lifecycle
"expire then permanently delete" two-phase model, or how Slack/Notion implement account deletion).

### Testing

- Unit tests for the retention-window calculation logic.
- Integration test: create a consent form, fast-forward the clock (or inject a fixed retention
  window in the test), run the job, assert soft-delete then hard-purge in two separate runs.
- Integration test: R2 file is actually removed on hard-purge, not just the DB row (check
  `IR2Service` is called and mock/verify the delete call).

---

## PENA-105 — Secrets management

**Priority:** P0, gates PENA-106's credential-handling half and every future studio integration ·
**Track:** Backend/Infra · **Est:** 3–5 days

### Problem, verified

`Pena_e_Arte.API/appsettings.json` (read in full) declares every secret — `Jwt:SecretKey`,
`Stripe:SecretKey`, `Twilio:AuthToken`, `Resend:ApiKey`, `CloudflareR2:SecretAccessKey`,
`Instagram:AppSecret`, `Instagram:TokenEncryptionKey` — as an empty string, populated at runtime
via environment variable substitution in `docker-compose.yml` (confirmed: lines like
`Stripe__SecretKey: ${STRIPE_SECRET_KEY:-}`). `.env` is correctly gitignored
(`.gitignore:40-43` — `.env`, `.env.*`, `*.env`, with `.env.example` explicitly un-ignored), so
nothing is currently leaking into source control. But this is still plain environment-variable
configuration with no encryption at rest, no access audit trail, no rotation mechanism, and no
per-tenant secret isolation — and CLAUDE.md rule 4 is explicit: *"All secrets via environment
variables or Vault. No hardcoded connection strings, API keys, or tokens anywhere."* Today's state
satisfies the letter (env vars) but not the spirit once studios start handing over their own POK
and easyPos credentials — those are **per-tenant** secrets, and there is currently no mechanism to
store one credential per studio at all, let alone securely.

This is also the mechanism ADR-0001's Article 4(g) posture depends on: *"No platform-level API
key — each studio issues its own credentials... Per-tenant secrets in Vault."* That line in the
ADR currently describes infrastructure that does not exist.

### Acceptance criteria

- [ ] Stand up a secrets backend. Two viable paths, pick one and document the choice as a short
      ADR (`docs/infra/ADR-000X-secrets-management.md`, following the existing ADR format in
      `docs/payments/`):
      - **HashiCorp Vault**, single-node Raft storage mode on the same K3s cluster (CLAUDE.md
        names Vault explicitly) — more ops burden for a solo founder, but zero ambiguity against
        the existing rule, and the KV v2 secrets engine plus per-tenant path namespacing
        (`secret/studios/{studioId}/pok`, `secret/studios/{studioId}/easypos`) maps cleanly onto
        the per-tenant-credential requirement.
      - **Infisical or Doppler** (managed secrets platforms, both have Kubernetes operators that
        sync secrets into K3s natively) — materially less ops burden, free/cheap tier fits a
        solo-founder budget, satisfies CLAUDE.md's rule in spirit (a dedicated secrets manager,
        not raw env vars) even though the rule names Vault specifically. **Flag this choice to the
        founder explicitly** rather than deciding unilaterally — CLAUDE.md rule 6 requires flagging
        gaps against the stated standard rather than silently substituting; this is exactly that
        situation, and the two options have real cost/ops tradeoffs the founder should see.
- [ ] .NET side: introduce an `ISecretsProvider` abstraction (new interface,
      `Pena_e_Arte.Domain/Interfaces/`) with a single method resolving a secret by key, backed by
      whichever provider is chosen (`VaultSharp` NuGet package for Vault, or the equivalent SDK for
      Infisical/Doppler). Existing config sections in `appsettings.json` that are genuinely
      platform-wide and low-sensitivity (e.g. `Jwt:Issuer`, non-secret config) can stay as-is —
      this migration targets **actual secrets**, not all configuration.
- [ ] Add the schema for per-tenant credential storage: a new entity or extension of `Studio`
      referencing a secret path/key rather than storing any credential value directly in MySQL —
      the database should never contain plaintext or even encrypted-at-rest-in-app-code credential
      material, only a pointer into the secrets backend. This is the concrete infrastructure ADR-
      0001 assumes exists.
- [ ] **Rotate every credential currently in `.env`** as part of this ticket — they've been sitting
      in a plain environment file this whole development period; treat that as reason enough to
      rotate on principle once the new mechanism is live, not because of any known compromise.
- [ ] Add a secret-scanning pre-commit hook and CI step (`gitleaks` or `truffleHog`, both free/OSS)
      — belt-and-suspenders against the `.env` mistake ever reaching a commit despite `.gitignore`
      already covering it. Cheap to add, meaningfully reduces CWE-798 (hardcoded credentials) risk
      going forward, and pairs naturally with PENA-107's CI work.
- [ ] Document the rotation runbook — `implementation-readiness.md` §5 already calls for one
      ("what happens when a studio's POK key leaks") — write it once here rather than deferring
      again.

### Explicitly out of scope

- Actually issuing or storing any real POK/easyPos/Polar credential — none exist yet. This ticket
  builds the mechanism; PENA-106 and the later provider-integration tickets (ADR-0001 step 8+)
  populate it.

### Industry standard

OWASP ASVS V6 (Stored Cryptography / secrets management), CWE-798 (use of hardcoded credentials),
twelve-factor app config principles (env vars are step one, not the destination, for anything
handling real customer credentials at multi-tenant scale). PCI DSS Req. 3/6 apply once any card-
adjacent secret is in scope — staying disciplined here now avoids a harder retrofit once POK
credentials are live.

### Testing

- Unit tests for `ISecretsProvider` implementation(s) against a local/dev instance of the chosen
  backend (Vault has a well-documented `dev` mode for exactly this; Infisical/Doppler both have
  local CLI emulation).
- Integration test confirming a missing/unresolvable secret fails closed (throws, doesn't silently
  return null and let a downstream call proceed with no credential).
- CI test confirming the gitleaks/truffleHog step actually fails the build on a planted fake
  secret (add and then immediately revert a dummy commit in a scratch branch to prove the gate
  works, don't just trust the config).

---

## PENA-106 — `IPaymentProvider` refactor: delete the Stripe aggregator design, add `PlatformFeeAmount`, add the architecture test

**Priority:** P0, this is the fix for a launch-blocking legal defect, not architecture hygiene ·
**Track:** Backend · **Est:** 5–8 days · **Depends on:** PENA-105 for the credential-handling half

### Problem, verified

`Pena_e_Arte.Domain/Interfaces/IStripePaymentService.cs`, lines 3–6, verbatim:

```csharp
/// <summary>
/// Aggregator model: all PaymentIntents go directly to the platform's Stripe account.
/// No connected account headers.
/// </summary>
```

Read together with `Pena_e_Arte.Infrastructure/Migrations/20260611223749_RemoveStripeConnect.cs`
(drops `studios.StripeAccountId`): **client money for a studio's services is designed to land in
Pena e Artë's own account, then be owed onward to the studio.** That is the exact fact pattern
Article 4(g) of Law 55/2020 excludes a technical service provider from licensing *only if absent*
— the exclusion requires never entering into possession of the funds. As currently designed, this
code enters into possession of them. **Nothing is deployed, so this is a launch blocker, not a
live exposure** — but it must be deleted, not migrated, per Amendment A Finding 1/2.

`Pena_e_Arte.Domain/Entities/Payment.cs` (read in full) — relevant gaps for the new abstraction:

```csharp
public class Payment : TenantEntity
{
    ...
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public ClientPaymentMethod Method { get; set; } = ClientPaymentMethod.Card;
    public string? StripePaymentIntentId { get; set; }
    public string? ClientSecret { get; set; }
    ...
    public decimal? RefundedAmount { get; set; }
    public ICollection<SessionSplit> SessionSplits { get; set; } = [];
}
```

No `Currency` column (confirmed absent) — every amount is implicitly a single currency today, and
POK requires explicit currency handling. No hold-expiry TTL field — `PaymentStatus.Captured` is
documented (`PaymentStatus.cs:11`, *"Card deposit authorised (held), not yet captured"*) as
matching POK's `autoCapture:false` semantics, but there's nowhere in the schema for POK's
`expiresAfterMinutes` server-enforced TTL to live. `StripePaymentIntentId` is a provider-specific
field name on a domain entity that will need to become provider-neutral once a second provider
(POK) exists behind the same abstraction.

`Pena_e_Arte.Domain/Entities/SessionSplit.cs` (read in full) — and its command,
`UpdateSessionSplitsCommand.cs:32-35`, enforces that splits sum **exactly** to `payment.Amount`.
This is a real invariant, and it is not the same concept as ADR-0001's platform fee: a
`SessionSplit` today represents how one payment is divided among the humans who did the work
(e.g. artist vs. studio), not a cut taken by the platform. Modeling the platform fee as another
`SessionSplit` row would either break the exact-sum invariant or silently understate what the
studio actually received. Per Amendment A Finding 4: **the platform fee is a distinct field,
`PlatformFeeAmount`, never a `SessionSplit` row.**

`Pena_e_Arte.Infrastructure/Jobs/PaymentReconciliationJob.cs` (read in full) is a correct,
reusable pattern — constructor DI of `IStripePaymentService` (soon `IPaymentProvider`), a
`RunAsync` entry point, `IgnoreQueryFilters()` for cross-tenant reconciliation. **Generalize this
job to the new interface rather than rewriting it** — the reconciliation logic itself (poll status,
promote `Captured`→`Paid`, cancel stale `Pending`) is provider-agnostic and doesn't need to change,
only the interface it depends on.

`Pena_e_Arte.Domain/Interfaces/IStripeBillingService.cs` (12 methods, read in full) is **explicitly
out of scope for this ticket** — Flow B, the platform's own subscription revenue, is legally fine
as Stripe-shaped today (collecting money owed to you is not a payment service) and remains a
valid, valuable porting template for the eventual Polar integration per Amendment A Finding 2.
**Do not touch `IStripeBillingService`, `StripeBillingService.cs`, or `StripeDiscountService.cs`
in this ticket.**

### Acceptance criteria

- [ ] New `Pena_e_Arte.Domain/Interfaces/IPaymentProvider.cs` replacing `IStripePaymentService` for
      Flow A only. Same five operations (create intent/hold, capture, cancel, get status, refund),
      renamed to be provider-neutral (e.g. `CreatePaymentHoldAsync` rather than
      `CreatePaymentIntentAsync` if "PaymentIntent" is judged too Stripe-specific — team's call,
      but be deliberate, not just a rename-for-its-own-sake).
- [ ] Capability flags on the interface or a companion `PaymentProviderCapabilities` type, per
      ADR-0001 Consequence 2: `SupportsSplit`, `SupportsAuthCapture`, `SupportsHoldExpiry`,
      `SupportedCurrencies`. Gate UI and business logic on capability, never silently degrade to a
      lowest common denominator — this is what lets bank-VPOS-as-escape-hatch (ADR-0001) plug into
      the same abstraction later without forcing POK's feature set down to VPOS's.
- [ ] **Delete** `IStripePaymentService.cs`, `StripePaymentService.cs`, and every reference to the
      aggregator model. Do not leave it "for reference" behind a flag — Amendment A is explicit
      this is a design defect, not an asset. Grep the full reference list from the earlier repo
      audit (30+ hits across tests, seeders, migrations) and resolve every one; do not leave a
      partial migration that half-compiles against two interfaces.
- [ ] `Payment.cs` changes:
      - Rename `StripePaymentIntentId` → a provider-neutral field (e.g. `ProviderReferenceId`),
        plus a `ProviderName`/`Provider` enum or string discriminator so reconciliation and
        webhooks know which `IPaymentProvider` implementation to call back into.
      - Add `Currency` (string, ISO 4217, default `"ALL"` — POK bills in lek).
      - Add a hold-expiry field (e.g. `HoldExpiresAt`, `DateTime?`) mapping onto POK's
        `expiresAfterMinutes`, enforced server-side (a scheduled job or the reconciliation job
        itself should auto-cancel/release holds past this timestamp — decide which and document
        it, don't leave it unenforced schema).
      - Add `PlatformFeeAmount` (`decimal`, default `0`) — built and wired through from day one at
        a 0% rate per ADR-0001's monetization section, never retrofitted later.
      - EF Core migration for all of the above, following the existing migration conventions.
- [ ] `SessionSplit` and `UpdateSessionSplitsCommand` are **unchanged** — their exact-sum invariant
      continues to apply only to `Payment.Amount`, and `PlatformFeeAmount` sits outside it,
      deducted from what's disbursed to the studio, not from what's split among session
      participants. Add an explicit code comment on `PlatformFeeAmount` cross-referencing
      `SessionSplit` so a future engineer doesn't try to unify them.
- [ ] **Architecture test** (ADR-0001 Consequence 3, this is the concrete deliverable that line was
      promising): a new test — either a new `Pena_e_Arte.ArchitectureTests` project or a class
      under the existing `tests/Pena_e_Arte.UnitTests/Domain/` folder (prefer the latter, less
      project sprawl for a solo founder's CI times) — using `NetArchTest.Rules` (NuGet package,
      the standard .NET equivalent of Java's ArchUnit) to assert **no type in the solution is named
      or shaped like a platform-balance ledger** (fails the build if a `PlatformLedger`,
      `PayoutQueue`, or similarly-named entity/table is ever introduced). Write this test **first**,
      confirm it currently fails against `main` in its pre-refactor state (there's no such entity
      today, so it should actually pass already — but write it to also assert against the
      *absence* of the aggregator pattern indirectly, e.g. assert no `IPaymentProvider`
      implementation exposes a method that could plausibly hold platform-owned funds; use your
      judgment on how strict a fitness function can meaningfully be here, perfect enforcement of "no
      commingling" via static analysis alone is hard — pair this with the reconciliation job
      logging/alerting if a real balance ever appears unexpectedly in a downstream financial report).
- [ ] `PaymentReconciliationJob.cs` updated to depend on `IPaymentProvider` instead of
      `IStripePaymentService` — same `RunAsync`/`IgnoreQueryFilters()` structure, no behavioral
      rewrite needed beyond the interface swap and the new `Currency`/`HoldExpiresAt` awareness.
- [ ] No concrete `IPaymentProvider` implementation is required in this ticket beyond what's needed
      to keep the existing test suite and demo seeder (`StripeDemoSeeder`) compiling — a thin
      `NullPaymentProvider`/test double is acceptable; the real POK implementation is ADR-0001 step
      8, a separate, later ticket once POK credentials exist (post-PENA-105, post-application).

### Explicitly out of scope

- `IStripeBillingService`/Flow B — untouched, per above.
- Actually integrating POK — this ticket produces the socket POK plugs into, not the plug itself.
- Bank-VPOS second implementation — identified in ADR-0001 as the eventual second
  `IPaymentProvider` implementation, not built now.

### Industry standard

This is a textbook architecture fitness function (Neal Ford/Rebecca Parsons's *Building
Evolutionary Architectures* — the "fitness function" pattern is exactly "write an automated test
that fails the build if an architectural invariant is violated," which is what ADR-0001
Consequence 3 already specified in prose). `NetArchTest.Rules` is the de facto standard tool for
this in the .NET ecosystem. PCI DSS SAQ-A scope discipline (card data never touches this
infrastructure) is preserved by this refactor, not weakened — worth calling out explicitly in the
PR since a payments refactor is exactly where SAQ-A scope creep tends to happen silently.

### Testing

- Unit tests for the new `IPaymentProvider` interface's capability-flag gating logic.
- Unit tests for `PlatformFeeAmount` calculation/persistence, explicitly asserting it never
  participates in the `SessionSplit` sum invariant (a regression test that would have caught the
  exact collision Amendment A Finding 4 identified).
- Integration test: hold-expiry TTL — create a payment hold with a past `HoldExpiresAt`, run the
  reconciliation job, assert it's auto-released/cancelled.
- The architecture test itself, run in CI (wire into PENA-107).
- Migration test/verification: run the new migration against a fresh DB and against a seeded dev
  DB, confirm no data loss on the rename from `StripePaymentIntentId`.

### Help sync (DoD item 4)

Payment-related Help articles and the owner onboarding tour almost certainly reference "connect
Stripe" language somewhere (`PaymentDetailPage`, `PaymentListPage`, `CreatePaymentIntentPage` all
appear in `helpContent.ts`'s coverage per the earlier grep). Update this copy to be provider-
neutral now even though POK isn't wired up yet — better than a second Help pass later, and cheap
to do while the interface rename is already touching these files' mental model.

---

## PENA-107 — CI gates for Help-sync and architecture-test enforcement

**Priority:** P1, not in the original six but required to make DoD items 2 and 4 real ·
**Track:** DevOps · **Est:** 1–2 days · **Land alongside PENA-100**

### Why this ticket exists

Every ticket above carries a Definition-of-Done obligation to update `helpContent.ts` and to keep
the (new, PENA-106) architecture test green. Today both are **review-enforced only** — a reviewer
has to remember to check, and `.github/` (confirmed present at repo root) has no workflow doing
either automatically. CLAUDE.md rule 7 (*"A feature is not done until Help describes it
correctly"*) and rule 6/Consequence 3 of ADR-0001 (the architecture test) are both currently
aspirational text, not enforced fact. This is the gap CLAUDE.md rule 6 asks to be flagged rather
than silently shipped around, and it's cheap to close.

### Acceptance criteria

- [ ] New GitHub Actions job (add to whatever workflow already runs on PR — check
      `.github/workflows/` for the existing CI pipeline and extend it, don't create a parallel one)
      that runs the full architecture-test suite (from PENA-106) on every PR and fails the build on
      any violation.
- [ ] "Help-sync" check: a lightweight script (bash or a small dotnet/node tool, whichever fits the
      existing CI tooling better) that inspects the PR diff and, if it touches specific
      user-facing paths (`frontend/src/features/{payments,forms,billing,studios}/**`,
      `Pena_e_Arte.Application/{Payments,ConsentForms,Billing}/**` — tune the path list to match
      what actually maps to user-facing behavior) **without** a corresponding diff in
      `frontend/src/features/help/helpContent.ts` or `frontend/public/user-manual/index.html`,
      posts a warning/failing check. Perfect precision isn't achievable with a path-based
      heuristic — bias toward false positives (an occasional "did you mean to update Help?" on a
      PR that genuinely didn't need it) over false negatives, and let a reviewer override with a
      clear justification rather than trying to make the check infallible.
- [ ] gitleaks/truffleHog secret-scanning step from PENA-105, wired in here if not already done
      as part of that ticket.
- [ ] Document the new required checks in whatever repo contributing-guide exists (or start one if
      none does — check for a `CONTRIBUTING.md` first).

### Explicitly out of scope

- Rewriting the existing CI pipeline beyond adding these jobs.
- Automated legal/compliance content checking (e.g. verifying the Privacy Policy text itself) —
  that's a human review step, not a CI concern.

### Industry standard

Fitness-function-in-CI is standard practice once an architecture test exists at all (see
ArchUnit's own docs, which assume CI enforcement as the point of writing the test — a fitness
function nobody runs automatically is just a unit test with extra steps). Path-based
"did-you-update-the-docs" CI checks are a common pattern in larger open-source projects (e.g.
Kubernetes' own PR bots flag missing doc updates on API changes) — same idea, scoped down for a
solo-founder repo.

### Testing

- Prove the Help-sync check actually fails on a scratch PR that touches a gated path without a
  Help update, and passes once the Help update is added — don't just trust the script logic, run
  it.
- Prove the architecture-test CI job fails if someone reintroduces a `PlatformLedger`-shaped type
  on a scratch branch, same discipline as the gitleaks proof in PENA-105.
