# Overnight master prompt — EPIC-0001: Pre-implementation hardening (PENA-100 → PENA-107, corrected)

**Date:** 31 July 2026 · **Requester:** Phi · **Mode:** fully autonomous, no user present.
**Origin:** `docs/engineering/EPIC-0001-pre-implementation-hardening.md`, corrected per
`docs/engineering/EPIC-0001-review-2026-07-31.md` (both read in full before this prompt was
written — this document supersedes the epic wherever the two disagree; where they agree, the epic's
original wording is preserved and re-verified below, not re-derived from memory).
**Verified against:** commit `7e4196c` on `main`, re-spot-checked directly against the source tree a
second time while writing this prompt (new findings beyond the review doc are called out inline and
marked **NEW**).
**Blocks:** ADR-0001 provider integrations (POK, easyPos, Polar) — this epic is the prerequisite
work, not the integration itself.

---

## 0. Before you start

```bash
git checkout main
git pull
git status   # must be clean — if not, stop and report, do not stash over unknown local changes
git checkout -b epic-0001/pre-implementation-hardening
git commit --allow-empty -m "chore: checkpoint before EPIC-0001 hardening pass"
```

Work phase by phase, in the order below (not the epic's original parallel-track ordering — that
ordering assumed two engineers; you are one session running serially, so dependencies are resequenced
to unblock progressively rather than round-robin across independent tracks). Commit at the end of
each phase with the message given in that phase's "Deliverable" block — one commit per phase, not one
giant commit at the end, so a partial overnight run still leaves reviewable history.

If you get through some phases and run out of time/budget before finishing all seven, stop at a phase
boundary (never mid-phase), leave the branch pushed, and write a one-paragraph status note at the top
of `docs/engineering/EPIC-0001-pre-implementation-hardening.md` under a new `## Execution status —
<date>` heading listing which phases landed and which didn't, with the exact commit SHA of the last
completed phase. Do not silently leave an unfinished ticket looking done.

---

## 1. Corrections this prompt makes to the original epic — read this before Phase 5 or Phase 7

The review doc found two corrections material enough to change what those phases actually build.
**Do not re-implement what's already shipped:**

1. **Gitleaks CI scanning already exists.** `.github/workflows/ci.yml`'s `guardrails` job (lines
   214–235) already runs `gitleaks/gitleaks-action@v2` on every PR, and GitHub native secret scanning
   + push protection are already enabled at the platform level (documented in
   `docs/claude/architecture.md`'s Decisions Log, the entry beginning "Native secret scanning + push
   protection"). **Do not add a second gitleaks CI step.** The only genuinely missing secret-scanning
   layer is a **local pre-commit hook** — build that in Phase 5, not a CI step.
2. **`docker-compose.yml` doesn't wire every secret the epic's problem statement claims it does.**
   Only `Jwt__SecretKey`, `Stripe__*` (4 keys), `CloudflareR2__*` (5 keys), and `Resend__ApiKey` are
   substituted from env vars in the `api` service's `environment:` block (lines 146–169).
   `Twilio__AuthToken`, `Instagram__AppSecret`, and `Instagram__TokenEncryptionKey` have **no**
   corresponding lines — both `NotificationService.cs` (SMS via Twilio) and `InstagramService.cs` /
   `InstagramSyncJob.cs` are live, active integrations that would run with permanently empty
   credentials in any docker-composed deployment today. Fix this as its own AC item in Phase 5.

Two more corrections, found while re-verifying for this prompt (**NEW** — not in the review doc):

3. **PENA-104's job-registration instruction is wrong.** The epic says to register `RetentionPurgeJob`
   "through the existing `IJobScheduler` abstraction... rather than wiring Hangfire directly in
   `Program.cs`." Checked `Pena_e_Arte.Domain/Interfaces/IJobScheduler.cs`: that interface is for
   **ad-hoc/delayed** jobs (appointment reminders, trial-expiry warnings, artist invites) — it has no
   concept of a recurring cron schedule. Every actual recurring job (`IndustryReportJob`,
   `PaymentReconciliationJob`, `InstagramSyncJob`) is registered directly in `Program.cs` (lines
   74–91) via Hangfire's `IRecurringJobManager.AddOrUpdate<T>(jobId, expr, cronSchedule)`. **Register
   `RetentionPurgeJob` the same way, in `Program.cs`, next to the other three** — that is the actual
   existing convention, not `IJobScheduler`.
4. **`IR2Service` has no delete method.** PENA-104 requires purging R2-stored files (consent PDFs,
   body-map images) on hard-purge. `Pena_e_Arte.Domain/Interfaces/IR2Service.cs` currently exposes
   `GeneratePresignedUploadUrlAsync`, two `GeneratePresignedReadUrlAsync` overloads, `IsR2Url`,
   `UploadAsync`, `GetPublicUrl`, and `ListByPrefixAsync` — **no delete method exists anywhere in the
   codebase.** `Pena_e_Arte.Infrastructure/Services/R2Service.cs` wraps `IAmazonS3` but never calls
   `DeleteObjectAsync`. You must add `Task DeleteAsync(string objectKey, CancellationToken ct)` to
   `IR2Service` and implement it in `R2Service.cs` via `s3.DeleteObjectAsync(...)` as part of Phase 4
   — this is new surface area the epic didn't call out, not an existing capability to "reuse."

---

## 2. Decisions already made — do not re-litigate

- **Brand stays "TattooOS."** No rename of any C#/TS identifier, table, or the 109 files that
  reference it. Only add the legal-entity disclosure (Phase 1).
- **Deposit/refund policy content is descriptive, not aspirational.** Phase 2's `/refund-policy` page
  must be generated from the actual current behavior in `DepositRule.cs`, `DepositCalculator.cs`,
  and `ClientCancellationPolicy.cs` (all confirmed to exist in `Pena_e_Arte.Domain/Entities/` and
  `Pena_e_Arte.Domain/Services/` respectively) — read them in full before drafting copy, do not
  invent a policy that isn't what the code does.
- **Platform fee is a distinct `Payment.PlatformFeeAmount` field, never a `SessionSplit` row.**
  Non-negotiable per Amendment A Finding 4 — see Phase 6.
- **`IStripeBillingService`/Flow B is untouched.** Do not modify, rename, or delete
  `IStripeBillingService.cs`, `StripeBillingService.cs`, or `StripeDiscountService.cs` in any phase.
  It has 10 methods (not 12 — corrected count; irrelevant to the instruction either way: leave it
  alone).
- **Vault is the default secrets backend for this session**, not a founder-facing open choice to sit
  on. CLAUDE.md rule 4 names Vault explicitly; deviating from an explicitly-named non-negotiable rule
  is exactly the kind of unilateral call this project doesn't make without the founder present. Build
  `ISecretsProvider` backed by Vault running in **dev mode** as a new local `docker-compose.yml`
  service (same tier as the existing `mysql`/`redis` services — no cloud account, no K3s cluster
  needed, since neither exists yet per `docs/payments/implementation-readiness-status-2026-07-31.md`
  §1: "No K8s manifests exist... no server anywhere"). Write the ADR recording this as the default
  with Infisical/Doppler documented as the lower-ops-burden alternative for the founder to swap in
  before a real production deploy — that swap is exactly what the `ISecretsProvider` abstraction
  exists to make cheap. Do not stand up a real Raft-clustered production Vault; there is no cluster to
  put it on yet, and that's the separate K3s deploy prompt's job, not this one's.
- **No re-litigation risk**: `DECISIONS.md` has no prior entries on Vault, `ConsentTemplate`, or
  `IPaymentProvider` — confirmed via full-file grep before writing this prompt. Nothing here conflicts
  with a standing decision.

## 3. Decisions to flag, not make — leave these as named open questions, do not guess

Write all of these into a single new `## Open questions for the founder` section at the top of
`docs/engineering/EPIC-0001-pre-implementation-hardening.md` when you finish (or into your status note
if you stop early) — do not scatter them as TODO comments only, they need one place a non-engineer can
read:

1. Final tagline/meta-description copy for `<title>`/`og:description` (Phase 1) — wire the constant,
   leave the value as a clearly-marked placeholder.
2. Whether `tattooos.co` is the domain that will actually go on PSP/MoR applications, and whether
   `support@tattooos.co` is a monitored inbox (Phase 1).
3. Registered legal address for `LEGAL_ENTITY_ADDRESS` (Phase 1) — leave blank, wired.
4. Final Privacy Policy / Terms of Service legal text from the Albanian data-protection lawyer
   (Phase 2) — ship the structural placeholder with a visible `[LAWYER REVIEW REQUIRED]` banner.
5. `/contact` route: monitored inbox vs. contact form (Phase 2).
6. Retention periods for consent forms / body maps / medical notes after last appointment or account
   closure (Phase 4) — ship `App:RetentionDays:ConsentForms` etc. as configurable, default to a
   clearly-commented placeholder (`730` / 2 years) with a code comment: `// PLACEHOLDER — pending
   founder + data-protection-lawyer input per implementation-readiness.md §9, do not treat as final`.
7. Vault vs. Infisical/Doppler for the eventual production deployment (Phase 5) — default built
   tonight is Vault dev-mode locally, per §2 above; ADR must present both with the tradeoff, not just
   silently commit to Vault forever.
8. Whether a client-facing "delete my account" UI should be built (Phase 4) — confirmed absent from
   `frontend/src/features/clients/` today. This ticket's scope is the backend retention/purge
   mechanism and the audited request path; a self-service UI is out of scope unless you determine
   mid-phase that the erasure-request path is unusable without one, in which case flag it as a new
   ticket, don't silently absorb it.

---

## 4. Scope boundary — do not touch

- Any file outside `docs/`, `Pena_e_Arte.API/`, `Pena_e_Arte.Application/`, `Pena_e_Arte.Domain/`,
  `Pena_e_Arte.Infrastructure/`, `Pena_e_Arte.Contracts/`, `frontend/src/`, `frontend/public/`,
  `frontend/index.html`, `tests/`, `docker-compose.yml`, `.github/workflows/`, and (new)
  `.githooks/` or equivalent pre-commit tooling config, and `.gitleaksignore` if a false positive
  needs one.
- `IStripeBillingService.cs`, `StripeBillingService.cs`, `StripeDiscountService.cs` (Flow B — Phase 6
  explicitly excludes these).
- `SessionSplit.cs`, `UpdateSessionSplitsCommand.cs` — unchanged in Phase 6, exact-sum invariant
  applies only to `Payment.Amount`.
- No renaming of `Studio.cs`, any MediatR command, table, or frontend component away from
  `TattooOS`-adjacent naming (Phase 1 constraint, applies epic-wide).
- No real POK/easyPos/Polar credential is issued or stored anywhere in this session — Phase 5 builds
  the mechanism only, no live credential exists yet to migrate.
- No production Vault cluster, no K3s manifests, no cloud secrets-manager account creation — Phase 5
  is dev-mode-local only, per §2.
- `.github/workflows/ci.yml`'s existing `guardrails` job's gitleaks step — do not duplicate it, only
  extend the same workflow file with new jobs (architecture-test, Help-sync check) per Phase 7.

---

## 5. Constraints (restated, every phase)

No new npm/NuGet package without flagging it as a prerequisite decision — **except** `NetArchTest.Rules`
(Phase 6, confirmed absent from every `.csproj` in the solution today) and `VaultSharp` (Phase 5,
confirmed absent), both of which are pre-approved: `NetArchTest.Rules` is ADR-0001 Consequence 3's
explicit ask, `VaultSharp` is CLAUDE.md rule 4's named backend's official .NET client. No other new
package in any phase without a named justification in the PR description. No `useEffect` for data
fetching (RTK Query only — every existing page in this codebase already follows this, match it). No
`any` in TypeScript. Explicit C# types, no `var` for non-obvious types. No business logic in Minimal
API endpoints — MediatR + FluentValidation only. Tenant isolation via EF Core global query filters
everywhere except already-approved `IgnoreQueryFilters()` usages (`PaymentReconciliationJob.cs` is the
existing precedent — any new cross-tenant query needs the same explicit justification comment that
file has). Every new/changed endpoint has `.RequireAuthorization()` with the correct policy. Never log
PII — no name/email/phone/card/health text in any Serilog line, grep your own diff for the studio's or
client's actual seeded test data before considering a phase done. Structured Serilog only, no
`Console.WriteLine`/`console.log` (the `guardrails` CI job already enforces this — make sure you don't
trip it). Tests ship with every phase, per that phase's Testing section below.

---

## Phase 1 — PENA-100 + PENA-101: brand/legal disclosure, dead-link fix

**Files to touch:** `frontend/index.html`, new `frontend/src/shared/components/SiteFooter.tsx`, new
`frontend/src/shared/constants/legalEntity.ts`, `Pena_e_Arte.API/appsettings.json`,
`frontend/src/app/router.tsx`, new placeholder policy pages, new
`frontend/src/app/__tests__/router.test.tsx`.

### 1a — `frontend/index.html`

Current state (verbatim, confirmed):
```html
<title>frontend</title>
```
No `<meta name="description">`, no Open Graph tags anywhere in the file. Change to:
```html
<title>{{TAGLINE_PLACEHOLDER — see legalEntity.ts SITE_TAGLINE}}</title>
<meta name="description" content="{{SITE_TAGLINE}}" />
<meta property="og:title" content="TattooOS" />
<meta property="og:description" content="{{SITE_TAGLINE}}" />
<meta property="og:image" content="/og-image.png" />
```
Static HTML can't read a TS constant at build time without a Vite plugin you don't have — instead,
set a sensible literal default directly in `index.html` (e.g. `TattooOS — booking & studio management
for tattoo shops`) and add a code comment pointing at `legalEntity.ts` as the source of truth for
every other surface, with a note that if `index.html`'s copy and `legalEntity.ts`'s copy ever diverge,
`legalEntity.ts` wins. Do not block this on a Vite templating pipeline that doesn't exist — that's
over-engineering for a static tag.

### 1b — `frontend/src/shared/constants/legalEntity.ts` (new file)

```ts
// Single source of truth for the platform's own legal-entity disclosure.
// Read by SiteFooter, the Terms/Privacy page templates, and any future
// invoice/receipt template — never re-type the NIPT or entity name elsewhere.
export const LEGAL_ENTITY_NAME = "Pena e Artë";
export const LEGAL_ENTITY_NIPT = "M12219042B";
export const LEGAL_ENTITY_ADDRESS = ""; // PLACEHOLDER — pending founder input, wire only, do not guess
export const SITE_TAGLINE = "TattooOS — booking & studio management for tattoo shops";
```

### 1c — Backend equivalent

`Pena_e_Arte.API/appsettings.json` currently has (confirmed, lines 67–69):
```json
"App": {
  "BaseUrl": ""
},
```
Extend to:
```json
"App": {
  "BaseUrl": "",
  "LegalEntityName": "",
  "LegalEntityNipt": ""
},
```
Empty defaults, same pattern as every other section — populate via env var later, not required to be
consumed anywhere yet (no `App__LegalEntityName` line needs to go into `docker-compose.yml` this
phase — nothing reads it yet).

### 1d — `SiteFooter.tsx` (new file)

Render on every public route: `/discover`, `/s/:slug`, `/artist/:slug`, `/login`, `/register`,
`/client-register`, `/map`, and the new `/privacy`, `/terms`, `/refund-policy`, `/contact`, `/`
routes from Phase 2. Minimum content: `© {currentYear} TattooOS`, `TattooOS is operated by
{LEGAL_ENTITY_NAME}, NIPT {LEGAL_ENTITY_NIPT}` (both from `legalEntity.ts`, never hardcoded a second
time), and links to Privacy Policy / Terms of Service / Refund Policy / Contact.

**Correction to the epic's own problem statement:** the epic claims "no footer component exists
anywhere under `frontend/src/shared/components/`." False — `AuthShellFooter.tsx` already exists
there. It's a generic auth-card footer wrapper (renders "Already have an account? Sign in"-style
copy inside login/register cards), unrelated to this ticket's site-wide legal footer. Name the new
component `SiteFooter.tsx` (distinct from `AuthShellFooter.tsx`) and do not touch
`AuthShellFooter.tsx` — the two serve different UI locations and should stay separate, not merged.

### 1e — Route fixes (PENA-101, land together with Phase 2's routes since PENA-102 is landing in the
same session — no reason to ship a placeholder-then-real split when both land tonight)

`frontend/src/app/router.tsx` — confirmed no `/privacy` or `/terms` route in the 41-route table
(lines 105–363). `CatchAllRedirect` (lines 72–76) sends every unmatched path to `/discover` or the
user's role home, so a dead `/privacy`/`/terms` link silently redirects instead of 404ing. Add
`/privacy`, `/terms`, `/refund-policy`, `/contact`, and `/` (new About/Home surface, see Phase 2) as
top-level routes in the same array position as the existing `/discover`, `/map` entries (outside the
authenticated `AppRoot` tree) — do not add them under `IndexRedirect`.

The only two existing `/privacy`/`/terms` references in the whole frontend (confirmed via full grep —
these are the *only* two hits) are `frontend/src/features/auth/components/LoginPage.tsx:251` and
`:255`:
```tsx
<a href="/privacy" className="hover:text-foreground/70 transition-colors underline-offset-2 hover:underline">
  Privacy Policy
</a>
...
<a href="/terms" className="hover:text-foreground/70 transition-colors underline-offset-2 hover:underline">
  Terms of Service
</a>
```
These will resolve correctly once the routes exist — verify by actually navigating in a test, not
just by grep (see Testing below).

**`CookieConsentBanner.tsx` — correction to the epic's AC.** The epic frames this as "verify
`CookieConsentBanner.tsx` correctly links to the now-real `/privacy` route and that its consent
categories match." That's not what needs to happen. Read `frontend/src/shared/components/
CookieConsentBanner.tsx` in full: it is currently a single "Got it" accept-all button with blanket
copy ("We use essential cookies to keep you signed in... By continuing to use TattooOS, you agree to
this.") — **there is no link to any policy page today, and no category distinction at all** (no
essential-vs-analytics split). Add a `/privacy` link into the banner copy (e.g. "...See our{" "}
<Link to="/privacy">Privacy Policy</Link> for details."). Do not invent consent categories that don't
correspond to anything real — this app currently sets no non-essential cookies, so an accept-all
banner is currently *accurate*, just undocumented. Add one code comment flagging that if any
analytics/marketing cookie is ever added later, this banner needs a real category-level opt-in at
that point, not before.

### Testing

- `SiteFooter` unit test: legal-entity string renders, all four links point to real routes (now
  real, not stubbed, since Phase 2 lands in the same session).
- Extend `useDocumentMeta.test.ts` (precedent file confirmed at
  `frontend/src/shared/utils/__tests__/useDocumentMeta.test.ts`) or add a matching test for the new
  `<title>`/meta tags in `index.html` if there's a reasonable way to assert on static HTML in this
  test setup — if not, note why and rely on the E2E suite's page-load assertions instead.
- New `frontend/src/app/__tests__/router.test.tsx` (check first whether one already exists under
  `frontend/src/app/__tests__/` and extend it): assert `/privacy` and `/terms` render their real
  pages and do **not** hit `CatchAllRedirect`.
- `CookieConsentBanner.test.tsx` (exists at `frontend/src/shared/components/__tests__/
  CookieConsentBanner.test.tsx`) — extend with a case asserting the new Privacy Policy link renders
  and points to `/privacy`.

### Help sync (DoD item 4)

No existing `helpContent.ts` article covers a footer or page-title/meta change — confirmed via grep,
nothing to update there. **Explicit verdict: no Help-content change needed** — a footer and page
`<title>` are not something a user asks "how do I..." about, and no onboarding-tour step references
either. State this in the PR description rather than leaving it silent, per this project's own rule
that "no Help change needed" must be said explicitly, not omitted.

### Industry standard

Trader-identification disclosure (legal name, registration number, contact) on a commercial site is
required under the EU e-Commerce Directive Art. 5 and Albania's own consumer-protection/e-commerce
framework — this is the same "brand in the header, legal entity in the footer" split Stripe, Notion,
Linear, and Vercel all run, and it's exactly what a PSP/MoR KYC reviewer checks for.

**Deliverable / commit:**
```
git add -A
git commit -m "feat(public): platform legal-entity disclosure + dead /privacy /terms link fix (PENA-100, PENA-101)"
```

---

## Phase 2 — PENA-102: public shell + policy routes

**Files to touch:** `frontend/src/app/router.tsx` (routes added in Phase 1, pages built here), new
`frontend/src/features/public/components/PrivacyPolicyPage.tsx`,
`TermsOfServicePage.tsx`, `RefundPolicyPage.tsx`, `ContactPage.tsx`, `HomePage.tsx` (naming: match
whatever convention the rest of `frontend/src/features/public/components/` uses — check
`StudioPortfolioPage.tsx`/`DiscoverPage.tsx` for the exact pattern before naming new files).

### Policy page structure

- `/privacy`: structural headings only, `[LAWYER REVIEW REQUIRED]` banner at top (removed when real
  text lands — make the banner conditional on a constant, not something a future PR has to remember
  to delete by hand: e.g. `const HAS_FINAL_LEGAL_COPY = false;` gating the banner). Cover at minimum:
  what personal data is collected (including Art. 9 health data — cross-reference Phase 3), purposes,
  legal basis per category, retention (cross-reference Phase 4), sub-processor list (POK, easyPos,
  Polar — not live yet, list as "planned" — Cloudflare R2, Resend, Twilio, hosting provider), data
  subject rights under Law 124/2024, DPO/controller contact.
- `/terms`: same placeholder-then-lawyer-fills pattern.
- `/refund-policy`: **write real copy now** — read `Pena_e_Arte.Domain/Entities/DepositRule.cs`,
  `Pena_e_Arte.Domain/Services/DepositCalculator.cs`, and `Pena_e_Arte.Domain/Services/
  ClientCancellationPolicy.cs` in full (all three confirmed to exist) and describe the actual
  implemented deposit/no-show/cancellation behavior — do not draft new policy language that isn't
  what the code does. If the code's behavior seems like bad policy, that's a separate finding to flag
  in the PR description, not something to silently "fix" by writing aspirational copy.
- `/contact`: confirm with the founder (open question §3.5) whether this is a monitored inbox or a
  form. If a form: name/email/message only, nothing beyond what's needed to respond, and add it to
  the sub-processor/retention inventory the Privacy Policy structure already lists.
- `/` (new minimal public Home/About surface): reuse `PublicPageHeader` and the new `SiteFooter`;
  first-touch visitor sees what the product is and links to Discover, Register studio, and the four
  policy pages, instead of `IndexRedirect` sending every unauthenticated root visit straight into
  `/discover` (confirmed current behavior, `router.tsx:66–70`).

`LoginPage.tsx:251,255` links now resolve — verify by rendering the route and clicking through in a
test, not just by grep (grep already confirms the `href`s are correct; what needs proving is that the
target route actually renders the intended page, not `CatchAllRedirect`).

### Testing

Route-level tests per new page: renders, no console errors, `SiteFooter`/`PublicPageHeader` present.
No Application-layer test obligation (no business logic here) — but this is still subject to Help-sync
DoD item 4 below since it changes first-run/signup-adjacent behavior.

### Help sync (DoD item 4)

These are new first-touch surfaces for unauthenticated visitors — no existing `helpContent.ts`
article covers pre-login pages (Help content is scoped to authenticated in-app usage per its
existing structure). **Explicit verdict:** no `helpContent.ts` change needed (nothing in-app changed
for an already-signed-in user), but check whether `RegisterStudioPage.tsx` or `ClientRegisterPage.tsx`
link out to Terms/Privacy at signup (common pattern — "By registering you agree to our Terms") and if
either currently lacks that link, add it as part of this phase (new user-visible surface at signup =
directly in scope for this ticket, not a separate one).

### Industry standard

WCAG 2.1 AA applies to these pages same as any other — run the `design:accessibility-review` skill's
checklist mentally while building (contrast, keyboard nav, landmark structure) even though a
dedicated pass is better deferred until real copy lands. GDPR Art. 13/14 and EU/Albanian
consumer-protection distance-selling rules set the substantive content bar for Privacy/refund
disclosure; your job is structure and truthfulness to the actual deposit-rules code, not marketing
copy.

**Deliverable / commit:**
```
git add -A
git commit -m "feat(public): policy routes, public home shell, refund policy from live deposit rules (PENA-102)"
```

---

## Phase 3 — PENA-103: consent versioning + explicit health-data consent

**Files to touch:** new `Pena_e_Arte.Domain/Entities/ConsentTemplate.cs`,
`Pena_e_Arte.Domain/Entities/ConsentForm.cs` (extend), new EF Core migration,
`Pena_e_Arte.Application/ConsentForms/Commands/SignConsentFormCommand.cs`,
`Pena_e_Arte.Application/Clients/Commands/UpdatePortableProfileOptInCommand.cs`, new
`Pena_e_Arte.Domain/Constants/AuditActions.cs` entries,
`frontend/src/features/forms/components/SignConsentFormPage.tsx`,
`ConsentFormDetailPage.tsx`, `frontend/src/features/help/helpContent.ts`.

### 3a — `ConsentTemplate` entity (new)

Current `ConsentForm.cs` (verbatim, confirmed):
```csharp
public class ConsentForm : TenantEntity
{
    public Guid ClientId { get; set; }
    public Guid AppointmentId { get; set; }
    public string? FileUrl { get; set; }
    public DateTime? SignedAt { get; set; }
    public string? SignatureData { get; set; }

    public Client Client { get; set; } = null!;
    public Appointment Appointment { get; set; } = null!;
}
```
This stores that a client signed and when, never what they signed. Add:
```csharp
public class ConsentTemplate : TenantEntity   // StudioId nullable-by-convention on TenantEntity
                                                // itself is not nullable — see note below
{
    public Guid? StudioId2 { get; set; }  // placeholder name — see note
    public string Version { get; set; } = string.Empty;
    public string BodyText { get; set; } = string.Empty;
    public DateTime EffectiveFrom { get; set; }
    public bool IsActive { get; set; }
}
```
**Note — resolve before writing this for real:** `TenantEntity.StudioId` (confirmed,
`Pena_e_Arte.Domain/Entities/TenantEntity.cs:6`) is a non-nullable `Guid`, but this ticket needs
platform-default templates with **no** studio (nullable `StudioId`), matching the pattern the epic
says `AuditLogEntry` already uses for platform-wide vs. studio-scoped rows. Read `AuditLogEntry.cs`
in full first to see exactly how it models a nullable studio scope on top of (or instead of)
`TenantEntity` — it likely does **not** inherit `TenantEntity` for this exact reason. Follow whatever
pattern `AuditLogEntry` actually uses (probably: don't inherit `TenantEntity`, declare
`Guid? StudioId` directly) rather than fighting `TenantEntity`'s non-nullable field. Do not add
`ShowPlatformBranding`-style bypass hacks — mirror `AuditLogEntry`'s actual shape exactly.

### 3b — `ConsentForm` changes

Add `ConsentTemplateId` (FK, `Guid`) and `ConsentTextSnapshot` (`string`, the exact rendered text at
signing time — never re-derive from the FK for anything already signed, this is the whole point).

EF Core migration: follow the `#nullable disable` + lowercase-table-name convention confirmed in
`20260611223749_RemoveStripeConnect.cs`. Run:
```bash
dotnet ef migrations add AddConsentTemplateAndSnapshot --project Pena_e_Arte.Infrastructure --startup-project Pena_e_Arte.API
```
(check `Pena_e_Arte.Infrastructure.csproj`/existing migration invocations for the exact
`--startup-project` flag needed if migrations have historically failed without it in this repo — if
unsure, check how a recent migration was actually generated by reading its `.Designer.cs` header
comment for tooling version, then match.)

### 3c — Server-side resolution, not client-side

Current `SignConsentFormCommand.cs` handler (`Pena_e_Arte.Application/ConsentForms/Commands/
SignConsentFormCommand.cs`, read in full, confirmed) builds the `ConsentForm` at lines 46–53:
```csharp
ConsentForm form = new()
{
    StudioId = tenant.StudioId,
    ClientId = appointment.ClientId,
    AppointmentId = appointment.Id,
    SignatureData = req.SignatureData,
    SignedAt = DateTime.UtcNow
};
```
Insert, immediately before this: resolve the active `ConsentTemplate` for `tenant.StudioId`
server-side (`IsActive == true`, most recent `EffectiveFrom <= DateTime.UtcNow`, falling back to the
platform-default template — `StudioId == null` — if the studio has no custom one), and set
`ConsentTemplateId` and `ConsentTextSnapshot = template.BodyText` on the new `ConsentForm`. This must
happen inside the same handler, in the same transaction as the row insert — never trust a template id
or body text passed up from the client, exactly per the epic's own instruction ("Resolve the active
template server-side at submission, not client-side").

### 3d — Frontend: there is currently nothing to "hook a template into"

Read `frontend/src/features/forms/components/SignConsentFormPage.tsx` in full (confirmed). The
epic's AC says this page should render "the active template's `BodyText` at sign time" — but the
current page (lines 111–116) shows only a **static, generic paragraph**:
```tsx
<p className="text-sm text-muted-foreground mb-6">
  By signing this consent form you acknowledge the risks and procedures associated with your
  tattoo session. Type your full legal name below to provide your digital signature. A PDF
  document will be generated and attached to your appointment record automatically.
</p>
```
There is no template-fetching, no rendered legal body text, nothing to modify — this is new UI
surface, not a hookup. Replace this paragraph with: a fetch of the active template for the client's
current studio (new query, e.g. `useGetActiveConsentTemplateQuery`, added to
`frontend/src/features/forms/consentFormsApi.ts` — read that file first to match its existing RTK
Query endpoint style exactly), rendered in a scrollable prose block above the signature field, with
the existing generic paragraph kept below it as the procedural instructions (not deleted, it's still
accurate UI copy, just not the *legal* text). **Recommended, not required:** disable the "Sign Consent
Form" submit button until the user has scrolled the template text into view at least once — this
measurably strengthens the GDPR Art. 7 "informed consent" argument over text that's merely present on
the page. Flag this as a UX recommendation in the PR description if you decide not to implement it,
don't silently skip it.

`ConsentFormDetailPage.tsx` (confirmed to exist under `frontend/src/features/forms/`) — update to
display `ConsentTextSnapshot`, not a live template re-render, so anyone reviewing a past consent
(client, studio, auditor) sees exactly what was agreed to.

### 3e — Health-data-specific consent split

Current `ClientProfile.cs` (verbatim, confirmed):
```csharp
public class ClientProfile : TenantEntity
{
    public Guid ClientId { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? MedicalNotes { get; set; }
    public string? Allergies { get; set; }
    public BodyMap BodyMap { get; set; } = new();
    public bool AllowCrossTenantRead { get; private set; } = false;
    public DateTime? CrossTenantOptInAt { get; private set; }

    public Client Client { get; set; } = null!;

    public void OptInToCrossTenant() { AllowCrossTenantRead = true; CrossTenantOptInAt = DateTime.UtcNow; UpdatedAt = DateTime.UtcNow; }
    public void OptOutOfCrossTenant() { AllowCrossTenantRead = false; CrossTenantOptInAt = null; UpdatedAt = DateTime.UtcNow; }
}
```
A single boolean gates whether `MedicalNotes`/`Allergies` (Art. 9 data) become readable by a second,
unrelated studio. Do not add a second consent mechanism — reuse the `ConsentTemplate`/snapshot
pattern from 3a–3c for this too, with its own `Version`/category (e.g. a `TemplateKind` enum or
string discriminator on `ConsentTemplate` distinguishing "appointment consent" from "cross-tenant
health-data sharing consent" — pick whichever shape keeps one table, not two). The opt-in UI copy at
the point of toggling must name the specific data categories being shared ("your allergies and
medical notes" — not "your profile") before the toggle can be set.

### 3f — Audit the currently-unaudited opt-in/opt-out

`Pena_e_Arte.Application/Clients/Commands/UpdatePortableProfileOptInCommand.cs` (read in full,
confirmed) is the actual handler behind both `OptInToCrossTenant()`/`OptOutOfCrossTenant()` calls —
it does **not** implement `IAuditableCommand` today (confirmed: no `IAuditableCommand` in its type
list, no `AuditAction`/`AuditTargetType`/`AuditTargetId` members). Add it, following the exact
pattern in `Pena_e_Arte.Application/Studios/Commands/SuspendStudioCommand.cs` (read in full,
confirmed — the record implements `IAuditableCommand` directly with three expression-bodied
properties):
```csharp
public record UpdatePortableProfileOptInCommand(UpdatePortableProfileOptInRequest Request)
    : IRequest<Unit>, IAuditableCommand
{
    public string AuditAction => Request.OptIn
        ? AuditActions.ClientProfileCrossTenantOptedIn
        : AuditActions.ClientProfileCrossTenantOptedOut;
    public string AuditTargetType => AuditTargetTypes.ClientProfile;
    public Guid AuditTargetId => /* resolve — command doesn't carry the profile id directly today,
                                     resolve it the same way the handler does via currentUser, or
                                     restructure the command to carry ClientId if that's cleaner */;
}
```
Add the two new constants to `Pena_e_Arte.Domain/Constants/AuditActions.cs` (confirmed current
shape — flat `public const string` list, `"Entity.Action"` naming, e.g. `"Studio.Suspended"`) and a
new `ClientProfile` entry to the `AuditTargetTypes` class in the same file. Note the command as
currently written resolves the client/profile via `currentUser.UserId` inside the handler, not from
the request DTO — you'll need to either thread a resolvable id onto the command record or resolve it
via a small lookup inside the `AuditTargetId` getter; check how `AuditLogBehavior.cs` actually
consumes `AuditTargetId` (sync property vs. something that can await) before deciding which approach
compiles cleanly.

Verify `OptOutOfCrossTenant()` is exposed in the UI as prominently as opt-in (Art. 7(3) "as easy to
withdraw as to give") — check whatever settings/profile-sharing page currently renders this toggle.

### Testing

- Application-layer unit tests: consent-template resolution logic (active template lookup,
  studio-custom vs. platform-default fallback), the split health-data opt-in command.
- Integration test: sign a consent form, edit the active template's `BodyText`, re-fetch the original
  `ConsentForm`, assert `ConsentTextSnapshot` is unchanged.
- Integration test: `UpdatePortableProfileOptInCommand` produces an `AuditLogEntry` with the correct
  `AuditAction`/`AuditTargetType` for both opt-in and opt-out.

### Help sync (DoD item 4) — three concrete updates, not a "check whether"

1. `frontend/src/features/help/helpContent.ts` — update article `client-consent-sign` (confirmed at
   line 157) to mention the consent text is now shown in full before signing, and add a line to
   `client-consent-list`/`artist-consent-view` (confirmed at lines 172 and 400) noting past consents
   show exactly what was agreed to at the time (the snapshot), even if the studio's wording has since
   changed.
2. **Fix an existing inaccuracy found while researching this ticket** (not new from this change, but
   directly relevant): `helpContent.ts`'s `faq-portable-profile` article (confirmed at lines
   1054–1060) currently reads: *"studios can only ever see non-sensitive details — never your payment
   history or consent form data."* This is misleading — `AllowCrossTenantRead` specifically gates
   `MedicalNotes` and `Allergies`, which **are** Art. 9 sensitive data, and this FAQ answer implies
   the opposite. Rewrite it to accurately state that opting in specifically shares allergies and
   medical notes with the new studio (not just non-sensitive profile fields), and reference the new
   explicit consent step from 3e.
3. `frontend/src/features/help/tours/clientTour.ts` (read in full, confirmed 4 steps: book-nav,
   my-studios-nav conditional, designs-nav, help-button) — **confirmed: no step currently covers
   consent signing or profile-sharing at all.** Explicit verdict: no tour-step change required,
   because the tour never walked through this flow to begin with — state this in the PR rather than
   silently skipping the question. (Optional: if you judge a step worth adding given health-data
   consent is now a bigger deal, that's a legitimate scope addition — flag it as a recommendation,
   don't fold it in silently.)

### Industry standard

GDPR Art. 9 (special-category data), Art. 7 (specific, informed, unambiguous, freely given, as easy
to withdraw as to give), mirrored by Law 124/2024. Consent-versioning-with-immutable-snapshot is
standard practice anywhere legally significant consent is captured (DocuSign, HelloSign,
HIPAA-adjacent intake systems, App Store/Play Store privacy-consent flows post-2021).

**Deliverable / commit:**
```
git add -A
git commit -m "feat(consent): versioned consent templates with immutable snapshot; explicit audited health-data sharing consent (PENA-103)"
```

---

## Phase 4 — PENA-104: retention & deletion job

**Files to touch:** new `Pena_e_Arte.Infrastructure/Jobs/RetentionPurgeJob.cs`,
`Pena_e_Arte.API/Program.cs`, `Pena_e_Arte.API/appsettings.json`,
`Pena_e_Arte.Domain/Interfaces/IR2Service.cs`, `Pena_e_Arte.Infrastructure/Services/R2Service.cs`,
new EF Core migration only if `DeletedAt` needs any schema companion (it doesn't — see below).

### 4a — Retention config

Extend `appsettings.json`'s pattern (same section-per-concern style as `App`, `Twilio`, etc.):
```json
"App": {
  "BaseUrl": "",
  "LegalEntityName": "",
  "LegalEntityNipt": "",
  "RetentionDays": {
    "ConsentForms": 730,
    "BodyMaps": 730,
    "GracePeriodBeforeHardPurge": 30
  }
},
```
Every numeric default here is a **PLACEHOLDER** per open question §3.6 — comment each one inline as
such. Engineering's job is making these configurable, not picking the final numbers.

### 4b — Soft-delete: already wired, confirmed — reuse, don't rebuild

`TenantEntity.DeletedAt` (confirmed, `Pena_e_Arte.Domain/Entities/TenantEntity.cs:9`) already exists
and is **already** excluded via global query filter for `ConsentForm`, `ClientProfile`, and 19 other
entities in `Pena_e_Arte.Infrastructure/Persistence/AppDbContext.cs` (confirmed, e.g. line 89:
`builder.Entity<ClientProfile>().HasQueryFilter(c => c.StudioId == tenant.StudioId && c.DeletedAt ==
null);`, line 103 for `ConsentForm`). Soft-delete-then-hard-purge is also already an established
pattern elsewhere (`DeleteDepositRuleCommand.cs`, `DeleteArtistCommand.cs`,
`DeleteTattooRecordCommand.cs`, `DeleteArtistTimeOffCommand.cs`, `DeleteStudioClosureCommand.cs` all
set `.DeletedAt = DateTime.UtcNow` today, confirmed via grep). This ticket is building the **second
pass** (hard purge after the grace window) on top of an already-correct first pass, not inventing
soft-delete from scratch.

### 4c — Add the missing R2 delete capability

`Pena_e_Arte.Domain/Interfaces/IR2Service.cs` (confirmed current full shape — 6 methods, no delete).
Add:
```csharp
Task DeleteAsync(string objectKey, CancellationToken ct);
```
Implement in `Pena_e_Arte.Infrastructure/Services/R2Service.cs` (confirmed uses `IAmazonS3 s3` via
constructor injection already):
```csharp
public async Task DeleteAsync(string objectKey, CancellationToken ct)
{
    await s3.DeleteObjectAsync(new DeleteObjectRequest
    {
        BucketName = _opts.BucketName,
        Key = objectKey,
    }, ct);
}
```
(`DeleteObjectRequest` is in `Amazon.S3.Model`, already `using`'d in this file.)

### 4d — `RetentionPurgeJob`

New file, `Pena_e_Arte.Infrastructure/Jobs/RetentionPurgeJob.cs`, matching the exact pattern
confirmed in `PaymentReconciliationJob.cs` (constructor DI, single public `RunAsync`,
`IgnoreQueryFilters()` for cross-tenant sweep):
```csharp
public class RetentionPurgeJob(IAppDbContext db, IR2Service r2, IOptions<RetentionOptions> options)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        await SoftPurgeExpiredAsync(ct);
        await HardPurgeGraceExpiredAsync(ct);
    }
    // SoftPurgeExpiredAsync: find ConsentForms (and whatever else is in scope) past
    // RetentionDays.ConsentForms since SignedAt/last-related-appointment, set DeletedAt if not
    // already set. HardPurgeGraceExpiredAsync: find rows with DeletedAt older than
    // GracePeriodBeforeHardPurge, call r2.DeleteAsync for any FileUrl, then actually
    // db.ConsentForms.Remove(...) and SaveChangesAsync. Both queries need IgnoreQueryFilters()
    // since the soft-delete filter would otherwise hide exactly the rows this job needs to find.
}
```
Implement the two private methods with the exact same `IgnoreQueryFilters()` + `.Where(... &&
DeletedAt == null)` / `.Where(... && DeletedAt != null && DeletedAt < cutoff)` structure
`PaymentReconciliationJob.cs` uses for its two-pass split.

**Register it correctly — correction to the epic's AC (see §1.3 above).** Do **not** use
`IJobScheduler` (that interface is for delayed/ad-hoc jobs, not recurring cron jobs — confirmed via
its actual method list, none of which take a cron expression). Register in `Program.cs`, in the same
`using (IServiceScope jobScope = ...)` block as the other three recurring jobs (confirmed at lines
74–91):
```csharp
recurringJobs.AddOrUpdate<RetentionPurgeJob>(
    "retention-purge",
    j => j.RunAsync(CancellationToken.None),
    Cron.Daily(4));   // stagger away from payment-reconciliation (2am) and instagram-sync (3am)
```

### 4e — Right-to-erasure request path

A client-initiated deletion request, distinguishable in the audit log from policy-driven automatic
purges (`IAuditableCommand` again, following the same pattern as Phase 3f). Check
`frontend/src/features/clients/components/MyProfilePage.tsx` first for whether any "delete my data"
entry point exists — confirmed absent today. Per open question §3.8, building a full self-service UI
is out of this ticket's scope; build the backend command (e.g. `RequestDataErasureCommand`,
`IAuditableCommand`, soft-deletes the client's `ConsentForm`s/`ClientProfile` immediately rather than
waiting for the retention window) that a support/owner action or a future self-service button can
call — and note explicitly in the PR that no such button exists yet, flagged as its own follow-up
per §3.8, not silently built as an afterthought.

### Testing

- Unit tests for the retention-window calculation logic (inject a fixed "now" via a testable clock
  abstraction if one exists in this codebase — check for `TimeProvider`/`IClock` usage elsewhere
  before adding a new one).
- Integration test: create a consent form, run the job with a retention window forced to already be
  expired, assert soft-delete on the first run and hard-purge (row gone, R2 delete called — mock/
  verify `IR2Service.DeleteAsync`) only after the grace window on a second run.
- Integration test: `RequestDataErasureCommand` produces a distinguishable `AuditLogEntry` from an
  automatic purge (different `AuditAction` values).

### Help sync (DoD item 4)

If the erasure-request path ships with no UI trigger (support/owner-only, per 4e), the affected
surface is internal/admin-only. Check whether an owner-facing admin action already exists anywhere
(e.g. `ClientDetailPage.tsx` action menu) that this should slot into, and if you add an entry point
there, add the corresponding `helpContent.ts` article. **Explicit verdict required either way** — do
not leave this unaddressed even if the answer is "no new UI, no Help update needed this phase."

### Industry standard

GDPR Art. 5(1)(e) (storage limitation), Art. 17 (right to erasure); NIST SP 800-53 SI-12. Two-stage
soft-delete/hard-purge with a grace window mirrors AWS S3 lifecycle "expire then permanently delete"
and how Slack/Notion implement account deletion.

**Deliverable / commit:**
```
git add -A
git commit -m "feat(retention): two-stage soft-delete/hard-purge job, R2 delete capability, audited erasure-request path (PENA-104)"
```

---

## Phase 5 — PENA-105: secrets management (corrected scope)

**Files to touch:** `docker-compose.yml`, new `Pena_e_Arte.Domain/Interfaces/ISecretsProvider.cs`,
new `Pena_e_Arte.Infrastructure/Services/VaultSecretsProvider.cs`, new
`Pena_e_Arte.Infrastructure/Extensions` registration, schema addition for per-tenant credential
pointers, new `docs/infra/ADR-0002-secrets-management.md`, new `.githooks/pre-commit` (or
`lefthook.yml`/`husky` config — pick whichever has the least new-tooling footprint for a solo-founder
repo), rotation runbook doc.

### 5a — Fix the docker-compose gap first (fastest, highest-value item in this phase)

Confirmed: `docker-compose.yml`'s `api` service `environment:` block (lines 146–169) wires `Jwt`,
`Stripe` (×4), `CloudflareR2` (×5), `Resend` — but **not** `Twilio__AuthToken`,
`Instagram__AppSecret`, or `Instagram__TokenEncryptionKey`, even though `appsettings.json` declares
all three as empty-string placeholders (confirmed at lines 29, 46, 48) and both integrations are
live (`NotificationService.cs` uses Twilio; `InstagramService.cs`/`InstagramSyncJob.cs` run a
scheduled sync). Add, in the same block, next to the existing `Resend__*` lines:
```yaml
Twilio__AccountSid: ${TWILIO_ACCOUNT_SID:-}
Twilio__AuthToken: ${TWILIO_AUTH_TOKEN:-}
Twilio__FromNumber: ${TWILIO_FROM_NUMBER:-}
Instagram__AppId: ${INSTAGRAM_APP_ID:-}
Instagram__AppSecret: ${INSTAGRAM_APP_SECRET:-}
Instagram__TokenEncryptionKey: ${INSTAGRAM_TOKEN_ENCRYPTION_KEY:-}
```
(Check `appsettings.json`'s `Twilio`/`Instagram` sections in full first for the exact full key list —
quoted above is the confirmed minimum; there may be one or two more keys in those sections worth
wiring at the same time, e.g. a Twilio `ServiceSid` if one exists — grep before finalizing.) Add the
matching entries to `.env.example` if they're not already there (confirmed `.env.example` currently
has no `TWILIO_*`/`INSTAGRAM_*` lines either — same gap, same fix).

### 5b — `ISecretsProvider` abstraction

```csharp
namespace Pena_e_Arte.Domain.Interfaces;

public interface ISecretsProvider
{
    /// <summary>Resolves a secret by key. Throws if unresolvable — never returns null and lets a
    /// downstream call proceed with no credential (fail closed, per this ticket's own test
    /// requirement).</summary>
    Task<string> GetSecretAsync(string key, CancellationToken ct);
}
```
Backed by `VaultSharp` (NuGet, pre-approved per §5 constraints) against a **local dev-mode Vault**
added as a new `docker-compose.yml` service:
```yaml
vault:
  image: hashicorp/vault:1.18
  container_name: pena_e_arte_vault
  cap_add: [IPC_LOCK]
  environment:
    VAULT_DEV_ROOT_TOKEN_ID: ${VAULT_DEV_ROOT_TOKEN:-dev-only-not-for-prod}
  ports: ["8200:8200"]
  # Dev mode only — in-memory, unsealed automatically, data lost on restart. This is deliberately
  # NOT the production posture; there is no K3s cluster to run a real Raft-backed Vault on yet
  # (confirmed absent — see docs/payments/implementation-readiness-status-2026-07-31.md §1). Standing
  # up a production Vault cluster is out of scope for this session; that's the K3s deploy prompt's
  # job once real infrastructure exists.
```
Existing config sections that are genuinely platform-wide and low-sensitivity (`Jwt:Issuer`, etc.)
stay in `appsettings.json` as-is — this migration targets actual secrets only, matching the epic's
own scoping.

### 5c — Per-tenant credential schema

New entity or `Studio` extension referencing a secret **path/key**, never a credential value, in
MySQL — e.g. a `StudioCredentialRef` join-style entity: `StudioId`, `Provider` (enum: `Pok`,
`EasyPos`), `SecretPath` (string, e.g. `secret/studios/{studioId}/pok`), no value column at all. This
is scaffolding for ADR-0001's Article 4(g) posture ("No platform-level API key... Per-tenant secrets
in Vault") — no real credential is issued yet, this ticket just makes the pointer mechanism exist.

### 5d — Rotate credentials currently in `.env`

Cannot literally be done by this session (no access to the founder's actual `.env` file's real
values or any live external accounts) — instead, produce the rotation **runbook**
(`docs/infra/secrets-rotation-runbook.md`) documenting the exact steps to rotate each of the seven
secrets (Jwt, Stripe ×4, CloudflareR2 ×5, Resend, and now Twilio/Instagram per 5a), and flag in the
PR description that actually executing the rotation against live values is a founder action this
session cannot perform, per `implementation-readiness.md` §5's own ask for this runbook.

### 5e — Pre-commit hook (the one genuinely-missing scanning layer, per §1.1)

Add a lightweight pre-commit hook running `gitleaks protect --staged` (or `gitleaks detect
--source . --no-git -v` scoped to staged files) before every local commit — this is the layer that
catches a secret *before* it's committed, which neither the existing CI gitleaks step nor GitHub push
protection can do (both only see a commit after it's already made locally). Use whichever hook
manager has the smallest footprint for this repo (check if `husky`/`lefthook` is already a
`package.json`/`frontend/package.json` dependency before adding a new one; if neither exists, a plain
`.githooks/pre-commit` shell script + `git config core.hooksPath .githooks` documented in the
contributing guide is a zero-dependency option and probably the right call for a solo-founder repo).
Document the install step (`git config core.hooksPath .githooks` or equivalent) in Phase 7's
contributing guide.

### 5f — ADR

`docs/infra/ADR-0002-secrets-management.md`, following the existing ADR format in `docs/payments/`
(read `docs/payments/ADR-0001-payment-providers.md`'s structure before writing — context, decision,
consequences, alternatives-considered sections). Record: Vault (dev-mode, local) as tonight's default
per CLAUDE.md rule 4's explicit naming; Infisical/Doppler documented as the lower-ops-burden
production alternative with the cost/ops tradeoff spelled out for the founder to decide before any
real production deploy; note that `ISecretsProvider` makes this swap a single new implementation
class, not a rewrite.

### Testing

- Unit tests for `ISecretsProvider`'s Vault-backed implementation against the local dev-mode Vault
  service (Vault's dev mode is well-documented for exactly this — confirmed usable without any
  external account).
- Integration test: missing/unresolvable secret throws, never silently returns null.
- Prove the pre-commit hook actually blocks a commit containing a planted fake secret on a scratch
  branch, then confirm it doesn't false-positive on real (non-secret) config — don't just trust the
  hook script, run it.
- **Do not** write a test proving CI-level gitleaks blocks anything — that's already proven and
  already running; duplicating it here is exactly the redundant work §1.1 corrects.

### Help sync (DoD item 4)

**Explicit verdict: no `helpContent.ts` change needed.** Infra/secrets plumbing has zero user-visible
surface — no client, artist, owner, or issuer sees or does anything differently. State this
explicitly in the PR per this project's rule that even a legitimate "no Help change" verdict must be
said out loud, not left silent.

### Industry standard

OWASP ASVS V6, CWE-798, twelve-factor app config principles (env vars are step one, not the
destination, once real per-tenant customer credentials are in scope). PCI DSS Req. 3/6 apply once any
card-adjacent secret is live — staying disciplined now avoids a harder retrofit later.

**Deliverable / commit:**
```
git add -A
git commit -m "feat(secrets): ISecretsProvider + local Vault dev service, per-tenant credential pointer schema, pre-commit gitleaks hook, Twilio/Instagram docker-compose fix (PENA-105)"
```

---

## Phase 6 — PENA-106: `IPaymentProvider` refactor, `PlatformFeeAmount`, architecture test

**Files to touch:** delete `Pena_e_Arte.Domain/Interfaces/IStripePaymentService.cs`,
`Pena_e_Arte.Infrastructure/Services/StripePaymentService.cs`; new
`Pena_e_Arte.Domain/Interfaces/IPaymentProvider.cs`; `Pena_e_Arte.Domain/Entities/Payment.cs`; new EF
Core migration; `Pena_e_Arte.Infrastructure/Jobs/PaymentReconciliationJob.cs`; new architecture-test
file under `tests/Pena_e_Arte.UnitTests/`; every one of the ~22 confirmed reference sites (tests,
commands, seeders); `frontend/src/features/help/helpContent.ts`.

### 6a — Delete the aggregator interface

Current `IStripePaymentService.cs` (verbatim, confirmed):
```csharp
/// <summary>
/// Aggregator model: all PaymentIntents go directly to the platform's Stripe account.
/// No connected account headers.
/// </summary>
public interface IStripePaymentService
{
    Task<(string PaymentIntentId, string ClientSecret)> CreatePaymentIntentAsync(
        long amountInCents, string currency, Guid paymentId, CancellationToken ct);
    Task CapturePaymentAsync(string paymentIntentId, CancellationToken ct);
    Task CancelPaymentIntentAsync(string paymentIntentId, CancellationToken ct);
    Task<string?> GetPaymentIntentStatusAsync(string paymentIntentId, CancellationToken ct);
    Task<string> RefundPaymentIntentAsync(string paymentIntentId, long? amountInCents, CancellationToken ct);
}
```
Read together with `20260611223749_RemoveStripeConnect.cs` (drops `studios.StripeAccountId`), this is
the Article 4(g) exposure Amendment A Finding 1/2 requires **deleted, not migrated**. Create
`IPaymentProvider.cs` with the same 5 operations renamed provider-neutral (e.g.
`CreatePaymentHoldAsync`, `CaptureAsync`, `CancelAsync`, `GetStatusAsync`, `RefundAsync` — be
deliberate about naming, not a mechanical find-replace). Add a `PaymentProviderCapabilities`
companion type (or capability flags on the interface itself): `SupportsSplit`,
`SupportsAuthCapture`, `SupportsHoldExpiry`, `SupportedCurrencies` — gate UI/business logic on
capability, never silently degrade to a lowest common denominator.

Delete `IStripePaymentService.cs` and `StripePaymentService.cs` outright — do not leave either "for
reference" behind a flag. Grep for every reference (confirmed ~22 non-doc hits across
`tests/Pena_e_Arte.UnitTests/Payments/*`, `tests/Pena_e_Arte.UnitTests/Appointments/
CancelAppointmentHandlerTests.cs`, `tests/Pena_e_Arte.UnitTests/Jobs/
PaymentReconciliationJobTests.cs`, `tests/Pena_e_Arte.IntegrationTests/Application/*` (3 files),
`Pena_e_Arte.Application/Payments/Commands/*` (5 files), `Pena_e_Arte.Application/Appointments/
Commands/{MarkNoShowCommand,CompleteAppointmentCommand,CancelAppointmentCommand}.cs`,
`Pena_e_Arte.Infrastructure/Jobs/PaymentReconciliationJob.cs`, `Pena_e_Arte.Infrastructure/
Extensions/InfrastructureServiceExtensions.cs` — re-grep at execution time since this list may have
grown since verification) and resolve every one to `IPaymentProvider`. Do not leave a partial
migration that half-compiles against two interfaces.

Provide a thin `NullPaymentProvider` (or similarly named test double) sufficient to keep the existing
test suite and `StripeDemoSeeder.cs` (confirmed exists at
`Pena_e_Arte.Infrastructure/Persistence/Seed/StripeDemoSeeder.cs`) compiling — the real POK
implementation is a separate, later ticket, out of scope here.

### 6b — `Payment.cs` changes

Current (verbatim, confirmed):
```csharp
public class Payment : TenantEntity
{
    public Guid AppointmentId { get; set; }
    public Guid ClientId { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public ClientPaymentMethod Method { get; set; } = ClientPaymentMethod.Card;
    public string? StripePaymentIntentId { get; set; }
    public string? ClientSecret { get; set; }
    public string? CashNote { get; set; }
    public Guid? CashConfirmedByUserId { get; set; }
    public DateTime? PaidAt { get; set; }
    public decimal? RefundedAmount { get; set; }
    public Appointment Appointment { get; set; } = null!;
    public Client Client { get; set; } = null!;
    public ICollection<SessionSplit> SessionSplits { get; set; } = [];
}
```
Confirmed: no `Currency` column exists. Changes:
- `StripePaymentIntentId` → `ProviderReferenceId` (provider-neutral), plus a `Provider` discriminator
  (enum or string) so reconciliation/webhooks know which `IPaymentProvider` implementation to call.
- Add `Currency` (`string`, ISO 4217, default `"ALL"`).
- Add `HoldExpiresAt` (`DateTime?`), mapping onto POK's `expiresAfterMinutes`, enforced server-side —
  decide whether `PaymentReconciliationJob` auto-cancels/releases holds past this timestamp as a
  third pass alongside its existing two, and implement whichever you decide (the epic leaves this as
  "decide which and document it" — pick "extend `PaymentReconciliationJob`," it already owns
  time-based payment-state transitions, don't create a fourth job for one more check).
- Add `PlatformFeeAmount` (`decimal`, default `0`) — wired through at a 0% rate from day one per
  ADR-0001's monetization section. Add an explicit code comment cross-referencing `SessionSplit`
  (`Pena_e_Arte.Domain/Entities/SessionSplit.cs`) so a future engineer doesn't try to unify the two —
  `PlatformFeeAmount` is deducted from what's disbursed to the studio and sits **outside**
  `SessionSplit`'s exact-sum-to-`Payment.Amount` invariant (confirmed at
  `UpdateSessionSplitsCommand.cs:32-35`: `decimal sum = command.Request.Splits.Sum(s =>
  s.Amount); if (sum != payment.Amount) throw...`). Do not touch `SessionSplit.cs` or
  `UpdateSessionSplitsCommand.cs` — they're correct and unchanged.

EF Core migration for all of the above, following the confirmed `#nullable disable` convention. Run:
```bash
dotnet ef migrations add ReplaceStripePaymentIntentWithProviderReference --project Pena_e_Arte.Infrastructure --startup-project Pena_e_Arte.API
```
Verify the rename migration preserves existing `StripePaymentIntentId` data by renaming the column
(`RenameColumn`), not dropping-and-adding — check against a seeded dev DB that no data is lost.

### 6c — Architecture test (ADR-0001 Consequence 3 — the concrete deliverable that line promised)

New file under `tests/Pena_e_Arte.UnitTests/Domain/` (preferred over a separate
`Pena_e_Arte.ArchitectureTests` project — less project sprawl for a solo founder's CI times, per the
epic's own reasoning). Add the `NetArchTest.Rules` NuGet package to
`tests/Pena_e_Arte.UnitTests/Pena_e_Arte.UnitTests.csproj` (confirmed absent from every `.csproj` in
the solution today — pre-approved new package per §5). Assert no type in the solution is named or
shaped like a platform-balance ledger (fails the build if a `PlatformLedger`, `PayoutQueue`, or
similarly-named entity/table is ever introduced). Confirm it currently passes against the
post-refactor state (there's no such entity today). Pair it with a code comment noting perfect static
enforcement of "no commingling" isn't achievable — the reconciliation job's logging is the
complementary runtime signal if a real balance ever appears unexpectedly in a downstream report.

### 6d — `PaymentReconciliationJob.cs` update

Current (verbatim, confirmed, full file already quoted in the review doc) — swap the constructor's
`IStripePaymentService stripe` parameter for `IPaymentProvider paymentProvider`, update the two method
bodies' calls (`stripe.GetPaymentIntentStatusAsync` → the renamed equivalent,
`stripe.CancelPaymentIntentAsync` → the renamed equivalent), and extend with the `HoldExpiresAt`
auto-release pass decided in 6b. Same `RunAsync`/`IgnoreQueryFilters()` structure, no other
behavioral rewrite.

### Testing

- Unit tests for `IPaymentProvider`'s capability-flag gating logic.
- Unit tests for `PlatformFeeAmount` calculation/persistence, explicitly asserting it never
  participates in the `SessionSplit` sum invariant — this is the regression test that would have
  caught Amendment A Finding 4's exact collision.
- Integration test: hold-expiry TTL — create a payment hold with a past `HoldExpiresAt`, run
  reconciliation, assert auto-release/cancel.
- The architecture test itself, run locally now, wired into CI in Phase 7.
- Migration verification: run the new migration against a fresh DB and a seeded dev DB, confirm no
  data loss on the `StripePaymentIntentId` → `ProviderReferenceId` rename.

### Help sync (DoD item 4)

Payment-related Help articles and the owner onboarding tour reference "connect Stripe"-style language
— confirmed `PaymentDetailPage`, `PaymentListPage`, `CreatePaymentIntentPage` all appear in
`helpContent.ts`'s coverage. Update this copy to be provider-neutral now even though POK isn't wired
up yet. Grep `helpContent.ts` and every `frontend/src/features/help/tours/*.ts` file for "Stripe" —
resolve every hit found, not just the three pages named above (that list is the epic's best guess,
verify it's complete before considering this phase done).

### Industry standard

Textbook architecture fitness function (Neal Ford/Rebecca Parsons, *Building Evolutionary
Architectures*) — ADR-0001 Consequence 3 already specified this in prose, this is the automated test
that makes it real. `NetArchTest.Rules` is the de facto .NET equivalent of ArchUnit. PCI DSS SAQ-A
scope discipline (card data never touches this infrastructure) is preserved, not weakened — call this
out explicitly in the PR since a payments refactor is exactly where SAQ-A scope creep tends to happen
silently.

**Deliverable / commit:**
```
git add -A
git commit -m "refactor(payments): delete Stripe-aggregator IStripePaymentService, add provider-neutral IPaymentProvider + PlatformFeeAmount + architecture fitness test (PENA-106)"
```

---

## Phase 7 — PENA-107: CI gates (corrected scope — smaller than the epic specified)

**Files to touch:** `.github/workflows/ci.yml` (extend, don't duplicate), new
`.github/scripts/check-help-sync.{sh,py}` (or equivalent), new `CONTRIBUTING.md` (confirmed absent at
repo root today).

### 7a — What NOT to do (per §1.1's correction)

Do not add a new gitleaks/truffleHog step. `.github/workflows/ci.yml`'s `guardrails` job already runs
`gitleaks/gitleaks-action@v2` on every PR (confirmed present at commit `7e4196c` itself — this isn't
something that shipped after the epic was written), and GitHub native secret scanning + push
protection are already enabled platform-side (documented in `docs/claude/architecture.md`'s Decisions
Log). The pre-commit hook from Phase 5e is the only new scanning surface this epic actually needed;
it's already done.

### 7b — Architecture-test CI job

Extend the existing `backend` job in `.github/workflows/ci.yml` (don't create a parallel job) — the
architecture test from Phase 6c lives under `tests/Pena_e_Arte.UnitTests/`, so it already runs as
part of the existing `dotnet test tests/Pena_e_Arte.UnitTests/...` step (line 64–69 of the current
file) with no new CI job needed at all, **unless** you want it isolated with its own visible check
name for faster PR-review scanning — if so, add a `dotnet test ... --filter
FullyQualifiedName~ArchitectureTests` step as a distinct step within the existing `backend` job
(not a new top-level job), so it fails fast and visibly without duplicating the full test run.

### 7c — Help-sync CI check (genuinely new, genuinely needed — confirmed absent)

New script (bash or a small Python/dotnet tool — match whatever's lightest given the existing
`guardrails` job already runs a Python heuristic inline for the endpoint-authorization check, so a
Python script inline in the workflow is the path of least resistance) that inspects the PR diff and,
if it touches `frontend/src/features/{payments,forms,billing,studios,clients}/**`,
`Pena_e_Arte.Application/{Payments,ConsentForms,Billing,Clients}/**` (tune this list — it should now
also cover `Pena_e_Arte.Domain/Entities/{ConsentForm,ConsentTemplate,ClientProfile,Payment}.cs` given
Phases 3 and 6 land tonight) without a corresponding diff in `frontend/src/features/help/
helpContent.ts` or `frontend/public/user-manual/index.html` (confirm which is the actually-served
copy before citing it — check for a `docs/user-manual.html` too and verify via
`frontend/src/features/help` or a router reference which one is live), posts a warning/failing check.
Bias toward false positives over false negatives — let a reviewer override with justification rather
than chasing perfect precision. Add this as a new job in `.github/workflows/ci.yml`, alongside
`guardrails`, not merged into it (different failure semantics — this one is a heuristic warning-class
check, `guardrails` is a hard-fail security gate).

### 7d — `CONTRIBUTING.md`

Confirmed absent at repo root. Create it, documenting: the required CI checks (backend, frontend,
docker-build, guardrails, and the new Help-sync check), the pre-commit hook install step from Phase
5e (`git config core.hooksPath .githooks` or equivalent), and a short note on the Definition of Done
this whole epic has been operating under (acceptance criteria demonstrated, tests, no PII in logs,
Help-sync, no secrets, PR states which CLAUDE.md rule it serves, reviewer re-runs the demonstration).

### Testing

- Prove the Help-sync check fails on a scratch PR touching a gated path without a Help update, and
  passes once the Help update is added — run it, don't just trust the script logic.
- Prove the architecture-test step fails if a `PlatformLedger`-shaped type is reintroduced on a
  scratch branch — same discipline as the pre-commit-hook proof in Phase 5.

### Help sync (DoD item 4)

**Explicit verdict: no `helpContent.ts` change needed.** CI/DevOps tooling has zero user-visible
surface for any of the four roles.

### Industry standard

Fitness-function-in-CI is standard once an architecture test exists at all (ArchUnit's own docs
assume CI enforcement as the point of the test). Path-based "did-you-update-the-docs" CI checks are a
common pattern in larger open-source projects (Kubernetes' own PR bots), scoped down here for a
solo-founder repo.

**Deliverable / commit:**
```
git add -A
git commit -m "ci: architecture-test visibility, Help-sync diff check, CONTRIBUTING.md (PENA-107, corrected scope — gitleaks already existed)"
```

---

## Final self-check — run before declaring any phase done, and again at the very end

- [ ] `dotnet build "Pena e Arte.slnx" --configuration Release` clean.
- [ ] `dotnet format "Pena e Arte.slnx" --verify-no-changes` clean (CI blocks on this — don't find out
      after pushing).
- [ ] `dotnet test` (both unit and integration projects) green, including the new architecture test.
- [ ] `pnpm lint && pnpm build && pnpm test --coverage` clean in `frontend/`.
- [ ] No `Console.WriteLine`/`console.log` introduced (the `guardrails` CI job will catch this, but
      check locally first).
- [ ] No secret, connection string, or API key added to source, `appsettings.json`,
      `appsettings.Development.json`, or committed anywhere — the new Vault dev-mode service and
      `docker-compose.yml` additions use env-var substitution only, same as every existing line.
- [ ] Every touched file is inside the scope boundary in §4 — nothing outside `docs/`,
      `Pena_e_Arte.*`, `frontend/src`, `tests/`, `docker-compose.yml`, `.github/workflows/` was
      modified.
- [ ] `IStripeBillingService.cs`, `StripeBillingService.cs`, `StripeDiscountService.cs` are byte-for-
      byte unchanged (`git diff main -- Pena_e_Arte.Domain/Interfaces/IStripeBillingService.cs
      Pena_e_Arte.Infrastructure/Services/StripeBillingService.cs
      Pena_e_Arte.Infrastructure/Services/StripeDiscountService.cs` must be empty).
- [ ] Every new/changed endpoint has `.RequireAuthorization()` with the correct policy — the
      `guardrails` job's Python heuristic will flag a miss, but check before pushing.
- [ ] Every phase's Help-sync verdict is stated explicitly somewhere in that phase's commit or PR
      description — no silent "forgot to check" gaps.
- [ ] The `## Open questions for the founder` section (§3) is present in
      `docs/engineering/EPIC-0001-pre-implementation-hardening.md`, listing all 8 items with which
      phase they belong to.
- [ ] If any phase was skipped or left incomplete, the `## Execution status` note (§0) is present and
      accurate, with the last-good commit SHA.

---

## Final deliverable spec

1. Branch `epic-0001/pre-implementation-hardening`, one commit per completed phase, pushed.
2. `docs/engineering/EPIC-0001-pre-implementation-hardening.md` updated with the `## Open questions
   for the founder` section and, if applicable, `## Execution status`.
3. New `docs/infra/ADR-0002-secrets-management.md` (Phase 5).
4. New `docs/infra/secrets-rotation-runbook.md` (Phase 5).
5. New `CONTRIBUTING.md` at repo root (Phase 7).
6. `docs/claude/architecture.md` Decisions Log: add one entry per phase actually completed, following
   the file's existing table format — at minimum, entries for "IPaymentProvider replaces
   IStripePaymentService (PENA-106)" and "Per-tenant secrets: ISecretsProvider + local Vault dev mode
   (PENA-105)," since both are exactly the kind of cross-cutting decision that table exists to record
   and both are currently described only in prose in the (now-superseded-in-part) ADR-0001 documents.
7. Final PR description (or, if this is a direct-to-branch overnight run with no PR flow configured,
   a `docs/engineering/EPIC-0001-completion-summary-<date>.md`) listing: which phases landed, every
   Help-sync verdict per phase, the industry-standard citation per phase, and a link to the final
   self-check results.
