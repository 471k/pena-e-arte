# Implementation readiness — verified status of every prerequisite

**Date:** 31 July 2026 · **Requested by:** Phi
**Companion to:** `docs/payments/implementation-readiness.md`, `docs/payments/ADR-0001-payment-providers.md`
**Method:** every line below was verified against the live source tree at commit `7e4196c`
(`main`), not against `docs/claude/architecture.md`. File paths and line numbers are real.
**Not legal or tax advice.**

---

## How to read this

| Verdict | Meaning |
|---|---|
| **DONE** | Verified present and working in the repo. Nothing to build. |
| **PARTIAL** | Something real exists but does not satisfy the prerequisite as written. |
| **NOT STARTED** | Verified absent. Grep returns zero hits. |
| **EXTERNAL** | Not a repo artifact — paperwork, an account, or a human decision. Repo status noted where it touches code. |

---

## Executive summary

**Of the 41 checklist items in the readiness doc, 5 are DONE, 8 are PARTIAL, 14 are NOT
STARTED in-repo, and 14 are EXTERNAL (paperwork/accounts/decisions with no repo footprint).**

The readiness doc's headline holds and is if anything understated: **the code side is further
behind than "three to four weeks" suggests, because the existing payment code is Stripe-shaped
rather than provider-agnostic.** There is no `IPaymentProvider`, no `ISubscriptionBillingProvider`,
no `IFiscalizationProvider`, no `splitWith`, no `BillingMandate`, and no POK/easyPos/Polar code
anywhere. What exists is a working single-provider Stripe implementation whose provider identity
is baked into the `Payment` aggregate itself — which ADR-0001 §"Consequences" explicitly forbids.
Step 10 of the sequenced plan is therefore not greenfield; it is a **refactor of live code with a
migration**.

Three things the readiness doc does not mention that the repo makes visible, all of which
belong on the critical path:

1. **`/privacy` and `/terms` are already linked from the login page and are dead links today.**
   `LoginPage.tsx:251,255` point at routes that do not exist; `router.tsx:362`'s catch-all
   redirects them to `/discover`. A PSP reviewer clicking those during onboarding sees a
   broken site, which is the single most common Albanian-applicant rejection cause the doc
   itself names. This is a shipping bug, not just a missing page.
2. **The app collects Article 9 health data today with no explicit, separately-withdrawable
   consent, and the consent form stores no wording.** `ConsentForm.cs` persists a typed name
   and a timestamp but not the text agreed to — there is no way to prove what any client
   consented to. This is the largest compliance exposure in the codebase and it predates the
   payments work entirely.
3. **Brand name mismatch.** The product ships as "TattooOS" / `tattooos.co` throughout the
   frontend, while the entity, the project and ADR-0001 say "Pena e Artë". PSP and MoR KYC
   compares the trading name on the live site against the QKB extract. Resolve before applying,
   not after.

---

## §1 — The gate in front of everything (live site + four policy pages)

| Item | Verdict | Evidence |
|---|---|---|
| Real domain, HTTPS, real content | **NOT STARTED** | No K8s manifests exist. `docker/` contains only `observability/` (Prometheus, Loki, Tempo, Alloy, Grafana). `docs/claude/overnight-prompt-k3s-production-deploy-2026-07-26.md` is a **spec only** — its §0 lists Hetzner VPS + K3s + managed DB as human prerequisites that "must already be done before this session starts," and none of them exist. No server anywhere. |
| **Terms of Service** | **NOT STARTED** — *and actively broken* | No `/terms` route in `frontend/src/app/router.tsx`. `LoginPage.tsx:255` links to it. `router.tsx:362` `{ path: "*", element: <CatchAllRedirect /> }` sends it to `/discover`. |
| **Privacy Policy** | **NOT STARTED** — *and actively broken* | Same: no `/privacy` route; linked from `LoginPage.tsx:251`; silently redirected. |
| **Refund / cancellation policy** | **PARTIAL** | The *mechanics* are fully built and per-studio configurable: `DepositRule.CancellationWindowHours`, `DepositRule.RefundPercentOnLateCancel`, `Domain/Services/ClientCancellationPolicy.cs`, `Domain/Services/DepositCalculator.cs`, `AppointmentSelfServiceDefaults.CancellationWindowHours`, plus `Payment.RefundedAmount` for partial refunds. What is missing is the **published platform-level policy page** a PSP reads — who refunds what, deposits vs. no-shows, studio-as-merchant-of-record. The engine exists; the public document does not. |
| **Contact page** with real address | **NOT STARTED** | Only `mailto:` links exist — `LoginPage.tsx:259`, `BillingPage.tsx:260,486`, `SubscribePage.tsx:356`, defaulting to `support@tattooos.co` / `contact@tattooos.co` via `VITE_CONTACT_EMAIL`. No physical address string anywhere in the repo. |
| Pricing visible / commercial model described | **PARTIAL** | The pricing *model* is fully implemented — `Plan`, `PlanPrice`, `BillingInterval`, the issuer plan editor (`/platform/plans`), `/billing/subscribe`. But every one of those routes sits behind `RoleGuard`. There is **no public pricing route**. `plan-tiers.html` at the repo root is a standalone artifact, not a served page. |

**Also flagged (not in the readiness doc):** there is no public marketing home page at all.
`router.tsx:66-70` `IndexRedirect` sends an unauthenticated visitor straight to `/discover`
(the studio-discovery listing). A PSP reviewing "what is this business" lands on a directory,
not on an explanation. `/discover`, `/map`, `/s/:slug`, `/artist/:slug` and `/embed/:studioSlug`
are the only public routes.

---

## §2 — Entity, activity code, VAT

| Item | Verdict | Evidence |
|---|---|---|
| Stripe Connect Express business-type check (AL, individual) | **EXTERNAL** | No repo footprint. Gates Flow B per ADR-0001. |
| Accountant: activity code, Person Fizik vs SH.P.K. | **EXTERNAL** | — |
| Entity decided before any PSP application | **EXTERNAL** | — |
| VAT modelling / registration threshold | **NOT STARTED (repo)** | Grep for `vat`, `VatRate`, `TaxRate` across `Pena_e_Arte.Domain` and `.Application` returns **zero hits**. There is no tax field on `Plan`, `PlanPrice`, `Payment` or `Subscription`, no tax-inclusive/exclusive flag, and no invoice line-item model. The day VAT registration happens, prices displayed and `PaymentInvoiceService` both change. |
| Platform's own NIPT / entity details configured anywhere | **NOT STARTED** | `Studio.Nipt` stores the **tenant's** NIPT. Nothing stores Pena e Artë's own NIPT, legal name, or registered address — which every invoice and every policy page will need. |

**What *is* done here:** studio-side NIPT capture is complete and validated —
`Studio.Nipt` (`Studio.cs:14`), format regex `^[A-Z]\d{8}[A-Z]$` in
`RegisterStudioValidator.cs:9` and `UpdateMyStudioCommand.cs:66`, uniqueness enforced via
`DuplicateNiptException`, `HasMaxLength(10)` in `StudioConfiguration.cs:22`, and the
registration log deliberately records only `nipt_provided=true/false` rather than the value
(`RegisterStudioCommand.cs:94-95`) — correct PII posture. This is the
`overnight-prompt-nipt-studio-registration-2026-07-22.md` work, shipped.

---

## §3 — Documents to produce

| Item | Verdict | Evidence |
|---|---|---|
| Studio Services Agreement | **NOT STARTED** | No such file in `docs/` or the repo. |
| Data Processing Agreement per studio | **NOT STARTED** | No DPA artifact, no acceptance/versioning entity, no timestamp of studio agreement acceptance anywhere in the schema. |
| Records of processing (Art. 30 equivalent) | **NOT STARTED** | — |
| Client consent wording for health data + photography | **NOT STARTED — and worse than absent** | `ConsentForm.cs` has exactly five fields: `ClientId`, `AppointmentId`, `FileUrl`, `SignedAt`, `SignatureData`. **There is no consent text, no template entity, and no version reference.** `SignConsentFormPage.tsx` asks the client to type their name (`signatureData: z.string().min(2, "Please type your full name to sign")`) with no wording displayed in the component at all. Result: you cannot demonstrate what any client agreed to, which under a GDPR-aligned regime is indistinguishable from having no consent. |
| Sub-processor list | **NOT STARTED** | The actual sub-processors are identifiable from the code — Stripe, Cloudflare R2 (`IR2Service`), Resend, Twilio, Instagram Graph API, plus Hetzner once deployed — but no published list exists. |

---

## §4 — Accounts to open

All six rows are **EXTERNAL**. Repo-relevant notes:

| # | What | Repo status |
|---|---|---|
| 1 | Stripe Connect Express eligibility check | No code. Note: Stripe Connect **was** integrated and then deliberately removed — see migration `20260611223749_RemoveStripeConnect.cs` and the "aggregator model … no connected account headers" comment on `IStripePaymentService.cs:3-6`. Do not assume prior Connect onboarding carries over. |
| 2 | Polar account + KYC | Zero Polar code. Flow B is currently **Stripe Checkout + Customer Portal** — `IStripeBillingService` (12 methods), `StripeBillingService.cs`, `StripeDiscountService.cs`, webhook handlers in `BillingEndpoints.cs`. **Migrating Flow B to Polar means replacing a substantial, working, tested subsystem, not writing a new one.** The readiness doc's estimate does not appear to price this in. |
| 3 | POK merchant account | Zero POK code. |
| 4 | easyPos account + API token | Zero easyPos code. Grep for `easypos`/`fiscaliz` across all five backend projects and `frontend/src` returns **zero hits**. |
| 5 | Business bank account (ALL + EUR) | No multi-currency support in code. `Payment` has **no `Currency` column** (`PaymentConfiguration.cs` — `Amount` is `decimal(18,2)`, no currency). Currency is passed per-request into `CreatePaymentIntentCommand.cs:42` and validated only as `^[a-zA-Z]+$` with length 3 (`CreatePaymentIntentValidator.cs:13-16`) — then discarded. Native ALL per ADR-0001 requires a schema change. |
| 6 | BKT re MSU Recurring/Split | **EXTERNAL** — would reverse ADR-0001 per its own table. |

---

## §5 — Technical prerequisites

| Item | Verdict | Evidence |
|---|---|---|
| Public HTTPS staging with stable webhook URL | **NOT STARTED** | See §1. This is the hardest blocker in this section — POK and Polar both call back, and nothing is deployed. |
| **Vault** (or equivalent) before holding any studio credential | **NOT STARTED** | Grep for `vault` across `Pena_e_Arte.API`, `.Infrastructure`, `docker/`, `docker-compose.yml` and `.github/` returns **zero hits**. All secrets are plain environment variables (`.env.example`: `STRIPE_SECRET_KEY`, `JWT_SECRET_KEY`, `R2_SECRET_ACCESS_KEY`, `RESEND_API_KEY`, …). **Partial credit:** an at-rest encryption pattern already exists — `ITokenEncryptor` / `AesTokenEncryptor.cs` encrypts Instagram OAuth tokens — but its key comes from `InstagramOptions` config, it is single-purpose, and it is not a secrets manager. It is a usable shape to generalise, not a substitute. |
| Hangfire with durable storage | **DONE** | `services.AddHangfire(...)` + `Hangfire.MySql` storage, `AddHangfireServer()` — `InfrastructureServiceExtensions.cs:60-68`. Dashboard gated by `HangfireDashboardAuthFilter` — `Program.cs:116-118`. Nine jobs exist including `PaymentReconciliationJob.cs`, which is the correct precedent for a fiscalization retry job. |
| Structured logging, `tenant_id`/`user_id`/`request_id`, no PII | **DONE** | Serilog (`Program.cs:16,26`), `UseSerilogRequestLogging` (`:102`), OpenTelemetry (`:43`), full Grafana/Prometheus/Loki/Tempo stack in `docker/observability/`. Independently confirmed in `docs/claude/security-audit-adversarial-2026-07-26.md`: Stripe webhook handlers log only `stripeEvent.Type`, and every Hangfire job signature passes IDs rather than names/emails. The one PII finding (a SignalR broadcast) was remediated in commit `f3bf5d3`. |
| Secrets rotation runbook | **NOT STARTED** | No runbook file in `docs/`. |
| **PCI: stay at SAQ-A** | **DONE for Stripe / UNDECIDED for POK** | Current design is SAQ-A by construction — `IStripePaymentService` returns a `ClientSecret` for browser-side confirmation and Flow B uses Stripe-hosted Checkout + Portal. Card data never reaches the backend. The POK choice (hosted `GuestCheckoutForm` vs. `encryptCard()`) is open per ADR-0001 and is a §8 founder decision. |

---

## §6 — Compliance (Law 124/2024)

**This section has the widest gap between what the readiness doc asks for and what exists.**

| Item | Verdict | Evidence |
|---|---|---|
| DPIA | **NOT STARTED** | No artifact. |
| DPO appointment / threshold check | **EXTERNAL** | — |
| **Explicit, separately-withdrawable consent for health data** | **NOT STARTED — data is being collected today without it** | `SubmitIntakeFormPage.tsx:104-108` presents a single free-text "Medical history & notes" field, placeholder *"List any allergies, skin conditions, medications, or other relevant health information…"*, validated only as `z.string().min(10)`. **No consent checkbox, no purpose statement, no withdrawal mechanism.** Structured health flags exist on the read side (`IntakeFormDetailPage.tsx:32` `allergyDetails`). `ClientProfile.cs:9-10` persists `MedicalNotes` and `Allergies` as first-class columns, alongside `BodyMap` (`ValueObjects/BodyMap.cs`). This is Article 9 data in production shape with Article 6-level consent. |
| 72-hour breach notification process | **NOT STARTED** | No runbook. |
| Retention policy / deletion | **NOT STARTED** | Grep for `retention`, `RightToErasure`, `purge`, `DeleteMyAccount` across `.Domain` and `.Application` returns **zero hits**. No TTL, no scheduled deletion job, nothing that ever removes a consent form, intake form, body map or `MedicalNotes` row. **Partial credit:** `IPortableProfileService` + `Models/PortableClientProfile.cs` + `PortableTattooRecord.cs` give you Article 20 portability, which is the harder half. |

**Additional finding not in the readiness doc — cross-tenant health data sharing.**
`ClientProfile.AllowCrossTenantRead` (`ClientProfile.cs:12-13`, with `OptInToCrossTenant()` /
`OptOutOfCrossTenant()` and a `CrossTenantOptInAt` timestamp) lets a client expose their profile —
**including `MedicalNotes` and `Allergies`** — to other studios. The opt-in/opt-out plumbing and
timestamp are well built and are exactly the right *shape* for Article 9 consent. But the toggle
is generic profile-sharing consent, not explicit health-data consent, and no consent wording is
stored with it. Sharing special-category data across controllers on a boolean is the specific
pattern this regime penalises. Worth resolving in the same pass as the consent-form fix, since
the mechanism is already there.

**Audit trail for money-touching actions — PARTIAL.** `AuditLogEntry` + `AuditActions` exist
with 11 stable action constants and `IAuditableCommand` wiring, and `AuditTargetTypes.Payment`
is already declared — but the only payment-adjacent action logged is `SessionSplits.Updated`.
Refunds, cash confirmations and deposit forfeitures are not audited. Before real money moves,
they should be.

---

## §7 — Sequenced plan, item by item

| Step | Verdict | Note |
|---|---|---|
| 1. Stripe Connect Express eligibility check | **EXTERNAL** | Free, gates Flow B. Do first. |
| 2. Verify RPAY sh.p.k. on BoA EMI register | **EXTERNAL** | `docs/Law 55_2020 On Payment Services/` holds the law PDF; the register check itself is external. |
| 3. Accountant: entity, activity code, VAT | **EXTERNAL** | |
| 4. Write four policy pages | **NOT STARTED** | Two of the four are already linked from the login page as dead routes. |
| 5. Site live with policies | **NOT STARTED** | Blocked on §1 + the K3s Phase-0 prerequisites. |
| 6. Apply: Polar, POK, easyPos, bank | **EXTERNAL** | Blocked on 5. |
| 7. Send the §4 question lists | **EXTERNAL** | Can be sent in parallel with 5. |
| 8. BKT / SEPA conversations | **EXTERNAL** | |
| 9. Deploy staging with HTTPS + Vault | **NOT STARTED** | K3s manifests are spec-only; Vault has zero repo presence. This is two separate builds, not one. |
| 10. `IPaymentProvider` / `ISubscriptionBillingProvider` / `IFiscalizationProvider`, capability flags, `BillingMandate`, architecture test | **NOT STARTED — and is a refactor, not greenfield** | See the dedicated section below. |
| 11. Domain model for orders, deposits, holds, fiscal records, `splitWith` at 0% | **PARTIAL** | Deposits and holds partly exist (below). Orders, fiscal records and `splitWith` do not. |
| 12. Cash / record-only path end to end | **DONE** | `ClientPaymentMethod.Cash`, `PaymentStatus.CashPending`, `Payment.CashNote` + `CashConfirmedByUserId`, owner confirmation flow, and Help article `owner-cash-confirm`. This ships today with no provider — exactly as the plan assumes. |
| 13. POK staging integration | **NOT STARTED** | |
| 14. easyPos dev integration | **NOT STARTED** | |
| 15. Polar sandbox integration | **NOT STARTED** | Displaces existing Stripe billing. |
| 16. Reconciliation + Help in the same change | **PARTIAL** | `PaymentReconciliationJob.cs` already exists for Stripe and is the right pattern to generalise. Help infrastructure is fully in place (below). |
| 17. DPIA, DPO, breach runbook, retention | **NOT STARTED** | |
| 18. Studio agreement + DPA lawyer review | **NOT STARTED** | |
| 19. Live smoke test per provider | **EXTERNAL** | |

---

## The step-10 problem, stated precisely

ADR-0001 §"Consequences for the codebase" item 3 says *"Never build a provider-shaped domain
model."* The current model is provider-shaped:

```csharp
// Pena_e_Arte.Domain/Entities/Payment.cs:13-15
// Card (Stripe) fields — null for cash payments
public string? StripePaymentIntentId { get; set; }
public string? ClientSecret { get; set; }
```

with matching persistence in `PaymentConfiguration.cs`:

```csharp
builder.Property(p => p.StripePaymentIntentId).HasMaxLength(255);
builder.Property(p => p.ClientSecret).HasMaxLength(500);
```

and `Studio.StripeCustomerId` on the tenant root. So step 10 is:

1. Introduce `IPaymentProvider` with the four capability flags from ADR-0001
   (`SupportsSplit`, `SupportsAuthCapture`, `SupportsHoldExpiry`, `SupportedCurrencies`).
2. Move the existing Stripe implementation behind it — `IStripePaymentService` is already a
   clean seam and its five methods (create intent, capture, cancel, status, refund) map almost
   one-to-one onto what POK needs, which is the good news.
3. **Migrate `Payment`** to provider-neutral fields (`ProviderKey`, `ProviderReference`,
   `ProviderClientSecret` or equivalent) plus the missing `Currency` column, with a data
   migration for existing rows.
4. Add `splitWith` at 0% and `BillingMandate` as new concepts.
5. Add the architecture test that fails the build on a `PlatformLedger` / `PayoutQueue` /
   platform-balance entity.

**Naming caution — and a real invariant conflict.** `SessionSplit` already exists, but it is
**not** the ADR's `splitWith` platform fee. It is a labelled breakdown of a single payment into
sessions (`SessionSplit.cs`: `PaymentId`, `Label`, `Amount`, `PaidAt`), and
`UpdateSessionSplitsCommand.cs:32-35` enforces:

```csharp
decimal sum = command.Request.Splits.Sum(s => s.Amount);
if (sum != payment.Amount)
    throw new BusinessRuleViolationException(
        $"Session splits total ({sum:F2}) must equal payment amount ({payment.Amount:F2}).");
```

So the splits must sum **exactly** to the payment total. A platform fee modelled as another
`SessionSplit` row would either break that invariant or silently reduce what the studio is
recorded as receiving. The platform fee needs its own field on the payment aggregate, and
whatever it is called must not be "split" unqualified — the two will be conflated otherwise.

---

## What is genuinely done and should not be rebuilt

- **Cash / record-only payment path** — end to end, including owner confirmation and Help.
- **Deposit rules engine** — `DepositRule`, `DepositCalculator`, `ClientCancellationPolicy`,
  configurable notice window and late-cancel refund percentage, `DepositStatus`
  (`Pending`/`Paid`/`Forfeited`/`Refunded`), `Appointment.DepositStatus` + `DepositAmount`.
- **Auth-then-capture semantics** — `PaymentStatus.Captured` is documented as *"Card deposit
  authorised (held), not yet captured"*, and `IStripePaymentService` already exposes
  `CapturePaymentAsync` / `CancelPaymentIntentAsync`. POK's `autoCapture:false` maps onto an
  existing state machine rather than a new one. **What is missing is the TTL** — there is no
  hold-expiry field, so POK's `expiresAfterMinutes` has nowhere to land.
- **Partial refunds** — `Payment.RefundedAmount` with the revenue-reporting semantics documented
  on the property itself.
- **One-payment-per-appointment integrity** — DB-level unique index `ux_payments_appointment_id`,
  with an explicit comment on why application-level checks alone are racy. Keep this.
- **Reconciliation job** — `PaymentReconciliationJob.cs`, exactly the pattern ADR-0001 item 5
  ("webhooks are triggers, never sources of truth") requires. Generalise, don't replace.
- **Hangfire durable jobs, Serilog + OTel structured logging, full observability stack.**
- **Studio NIPT capture** — validated, unique, PII-safe logging.
- **Audit log infrastructure** — `AuditLogEntry`, `AuditActions`, `IAuditableCommand`, issuer
  audit-log page.
- **Help infrastructure** — 83 articles in `frontend/src/features/help/helpContent.ts`, already
  including `client-deposit-pay`, `owner-payments`, `owner-payment-create`, `owner-cash-confirm`,
  `owner-billing`, `owner-subscribe`, `issuer-activate-cash-sub`; four onboarding tours
  (`tours/{client,artist,owner,issuer}Tour.ts`); help-search analytics (`HelpSearchLog`,
  `/platform/help-insights`).

---

## Help-sync obligation (CLAUDE.md rule #7)

**This status document itself needs no Help change** — it is an internal engineering artifact
with zero user-visible surface.

**The live manual is `frontend/public/user-manual/index.html`** (237 KB, modified 25 Jul 23:35),
served from Vite's `public/`. `docs/user-manual.html` (107 KB, 25 Jul 10:35) is a **stale copy
that is not served** — cite and edit the `frontend/public/` one. Worth deleting or clearly
marking the `docs/` copy before someone edits the wrong file.

Every downstream change this document implies carries a Help obligation, and each is
non-trivially user-visible:

| Change | `helpContent.ts` | Manual | Tour |
|---|---|---|---|
| Public ToS / Privacy / Refund / Contact pages | New article under the client group; refund page must agree with `owner-deposit-rules` and `client-cancel-booking` or clients get contradictory answers | New section | No — public pages sit outside the authenticated tours |
| Explicit health-data consent step | Rewrite `client-intake-submit` and `client-consent-sign`; both currently describe a flow with no consent gate | Both sections | **Yes** — `clientTour.ts`, the consent step's copy changes materially |
| POK connection in studio settings | New owner article; update `owner-studio-profile` | New section | **Yes** — `ownerTour.ts` needs a "connect your payment provider" step, which does not exist |
| easyPos / fiscalization | New owner article covering what fiscalization is and what a failure means | New section | **Yes** — `ownerTour.ts` |
| Flow B migration to Polar | Rewrite `owner-billing` and `owner-subscribe` end to end (they describe the Stripe Customer Portal by name) | Both sections | No — existing selectors survive a provider swap |
| Retention / deletion | New article; `client-profile` needs "how long we keep this" | New section | No |

---

## Industry-standard benchmark note (CLAUDE.md rule #6)

- **Health data is the sharp edge, and the benchmark set is weak here — which is an
  opportunity, not permission to match them.** Current market reporting has Fresha and Vagaro
  handling consent forms as PDF uploads or third-party add-ons rather than natively, with
  medical-history capture treated as generic form data; purpose-built tattoo platforms
  (Tattoo Studio Pro, TattooGenda, Shop Ledger) market integrated digital consent and health
  checks as a differentiator, and the category's own buying guides now tell studios to ask
  vendors about encryption standards, **retention policies** and regional hosting. Pena e Artë
  currently sits at the salon-generic end: structured storage, no consent wording, no retention.
  Closing that gap is both compliance work and a competitive claim.
- **Law 124/2024 is confirmed current.** Approved 19 December 2024, in force February 2025,
  GDPR/LED-aligned. Health data is Article 9 special-category — prohibited in principle,
  permitted only in enumerated cases. DPO required for large-scale processing of sensitive data;
  DPIA plus prior consultation where indicated. The Commissioner is actively enforcing on health
  data specifically — the IDP published a decision in April 2026 treating publication of health
  data as a Law 124/2024 violation. The readiness doc's characterisation holds.
- **Deliberate divergences to state out loud rather than hide:** (a) the always-on cash path is
  a market-reality addition none of the benchmark set needs, per ADR-0001; (b) easyPos
  fiscalization is an Albanian compliance requirement with no benchmark analogue; (c) NIPT
  capture likewise. None of these should be presented as benchmark-driven.

---

## Recommended order of attack (repo-side only)

Ordered by "unblocks the most, costs the least." Everything here is a doc/spec deliverable
from this project; none of it is implemented here.

1. **Fix the dead `/privacy` and `/terms` links.** Either ship the pages or remove the links.
   Shipping them is the answer, since §1 requires them anyway — but a broken link on the login
   page during PSP review is a self-inflicted rejection. Smallest possible fix, highest
   asymmetry.
2. **Public shell: home / ToS / Privacy / Refund / Contact / Pricing routes.** Pure frontend,
   no backend, no provider dependency. Unblocks §1, §7 step 5, and every application in §4.
   The legal *text* needs a lawyer; the *routes and layout* do not and can be built against
   placeholder copy.
3. **Health-data consent + consent-form versioning.** Add a versioned consent-template entity,
   store the wording (or its hash + version) with each signature, add a separate explicit
   health-data consent with its own withdrawal path, and revisit `AllowCrossTenantRead` in the
   same pass. Independent of payments, larger legal exposure than payments, and already in v2
   scope.
4. **Retention + deletion.** A Hangfire job and a policy. The infrastructure is already there.
5. **Secrets management.** Generalise `ITokenEncryptor` or adopt a real secrets manager —
   required *before* the first studio POK credential is held, per CLAUDE.md rule 4.
6. **`IPaymentProvider` refactor + `Payment` migration (adds `Currency`, hold-expiry, provider-
   neutral references, `splitWith` at 0%) + the no-platform-balance architecture test.**
   Needs no credentials, and every provider integration lands on top of it.
7. **K3s deploy** — the existing spec, once its Phase-0 human prerequisites are done.

Steps 1–6 are all buildable today with zero external dependencies. Only step 7 needs a credit
card, and only steps 13–15 of the original plan need credentials.

---

## Sources

- [Law No. 124/2024 On Personal Data Protection (IDP Albania, PDF)](https://idp.al/wp-content/uploads/2025/04/Law-no.124-2024-DP.pdf)
- [IDP — publication of health data constitutes a violation of Law 124/2024 (April 2026)](https://idp.al/en/2026/04/21/the-publication-of-health-data-in-the-media-constitutes-a-violation-of-law-no-124-2024-on-the-protection-of-personal-data/)
- [IAPP — Albania's personal data protection law: harmonized with the GDPR](https://iapp.org/news/a/albania-s-personal-data-protection-law-a-legal-framework-harmonized-with-the-gdpr)
- [CMS Expert Guide — data protection and cybersecurity laws in Albania](https://cms.law/en/int/expert-guides/cms-expert-guide-to-data-protection-and-cyber-security-laws/albania)
- [DLA Piper — Data protection laws in Albania](https://www.dlapiperdataprotection.com/index.html?t=about&c=AL)
- [Zenoti — Tattoo Shop Management Software: 2026 Guide](https://www.zenoti.com/thecheckin/tattoo-shop-management-software-guide)
- [Tattoo Studio Pro — Digital Studio Forms: Consent, Waivers & Health Checks](https://tattoostudiopro.com/tattoo-forms/)
- [TattooGenda — digital consent forms: a tattoo studio's paperless playbook](https://tattoogenda.com/tattoo-insights/digital-consent-forms/)
