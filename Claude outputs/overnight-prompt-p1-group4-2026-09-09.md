# Overnight Master Prompt — P1 Backlog, Group 4 ("Medium Builds")

**Date:** 2026-09-09
**Mode:** Autonomous overnight build. Work through the phases in order. Do not stop to ask
clarifying questions — every open design question below has been pre-resolved. If you hit a
genuine blocker not covered here, stop that phase, document it in the Decisions Log per the
Final Deliverable section, and move to the next phase rather than guessing silently.
**Run with:** a fresh branch off `main`, e.g. `feat/p1-group4-2026-09-09`.
**Before starting:** read `CLAUDE.md` in full (non-negotiable rules) and skim
`docs/claude/architecture.md`'s Decisions Log and Feature Module Map for context on
`ConsentTemplate`, `IntakeForm`, `DepositRule`/`DepositCalculator`, `Subscription`, and the
`AllowAnonymous Exceptions` table — you will extend two of these this session.

## Context

This is the fourth of several master prompts executing the 2026-09-09 P1 backlog audit
(`docs/claude/audit-p1-backlog-2026-09-09.md`) against `docs/claude/p1-backlog-master-build-spec-2026-09-09.md`.
Already shipped: Group 1 (`docs/claude/overnight-prompt-p1-group1-2026-09-09.md` — CSV export,
booking style field, installable PWA) and Group 2 (`docs/claude/overnight-prompt-p1-group2-2026-09-09.md`
— studio structured hours, timezone handling). Group 3 (waitlist, gift cards, packages,
client-to-client referral, marketing campaigns, support impersonation, booth-rent) remains
un-scheduled — every one of those items needs a product decision from the studio-ops side before
it can be built, and is explicitly out of scope for tonight.

This prompt covers Group 4 — four backlog items with no outstanding product decisions, but where
live-codebase verification (this session, read-only, via the linked repo — no code was written
from the consultation side) found the original backlog spec's premises wrong or incomplete in
several places. Each phase below states the correction inline; do not follow the original
backlog doc's wording for these four items where it conflicts with what's written here — this
document supersedes it for items #11, #14, #12, and #9.

**Build order:** Phase 1 (Promo Codes) → Phase 2 (Dunning) → Phase 3 (Custom Intake Fields) →
Phase 4 (Flash/Design Catalog). Phases 1 and 4 both touch `CreateAppointmentCommand.cs` —
building them in this order (Promo Codes first) means Phase 4 edits a file that already has the
Phase 1 changes in it; do not parallelize these two phases across separate agents/worktrees.

## Required reading before touching code

- `Pena_e_Arte.Application/Appointments/Commands/CreateAppointmentCommand.cs` — shared
  booking core (`CreateAppointmentCoreAsync`), used by both the authenticated and guest flows.
- `Pena_e_Arte.Domain/Entities/DepositRule.cs` and `Pena_e_Arte.Domain/Services/DepositCalculator.cs`
  — the fixed/percent shape Promo Codes will mirror.
- `Pena_e_Arte.Domain/Entities/Subscription.cs`, `Pena_e_Arte.Infrastructure/Jobs/TrafficRollupJob.cs`,
  `Pena_e_Arte.API/Program.cs` (the `recurringJobs.AddOrUpdate<T>` block, ~line 122–155) — the
  daily-recurring-job pattern Dunning will follow.
- `Pena_e_Arte.Domain/Entities/IntakeForm.cs`,
  `Pena_e_Arte.Application/IntakeForms/Commands/SubmitIntakeFormCommand.cs`,
  `frontend/src/features/forms/components/SubmitIntakeFormPage.tsx` — the actual freeform-textarea
  system Custom Intake Fields replaces (NOT `Appointment`/`BookingIntake` — see Phase 3 below).
- `Pena_e_Arte.Domain/Entities/Design.cs`, `DesignRevision.cs`, `DesignApproval.cs`,
  `Pena_e_Arte.Application/Designs/Commands/CreateDesignCommand.cs`,
  `frontend/src/features/artists/components/ArtistDetailPage.tsx` (portfolio-image management —
  NOT `frontend/src/features/public/components/ArtistPortfolioPage.tsx`, which is the public-facing
  view — see Phase 4 below).
- `docs/claude/architecture.md`'s `## AllowAnonymous Exceptions` table (~line 1125) — you will add
  one row to it in Phase 4.

## Constraints (apply to every phase)

- No new npm or NuGet packages.
- No `useEffect` for data fetching — RTK Query hooks only.
- TypeScript strict, no `any`.
- All business logic through MediatR commands/queries; no logic in endpoint lambdas beyond
  mapping.
- Every new endpoint: explicit `.RequireAuthorization("<Policy>")` or a documented
  `AllowAnonymous` exception added to the architecture.md table (Phase 4 needs one).
- Every new entity needs an EF Core migration; every `TenantEntity` subclass gets the standard
  global query filter — do not add `IgnoreQueryFilters()` anywhere without a one-line comment
  citing the approved-usage precedent it matches (several phases below cite the exact one to
  reuse).
- Tests: unit tests for every new validator/handler; integration tests for every new endpoint,
  covering both the happy path and the RBAC/tenant-isolation boundary.
- Help sync (CLAUDE.md rule #7): every user-facing addition below updates `helpContent.ts`, the
  standalone manual (`frontend/public/user-manual/index.html`), and the relevant onboarding tour
  file in the same change.

---

## PHASE 1 — Promo Codes / Discounts at Booking (#11)

### Design decisions (pre-resolved — do not re-litigate)

**Correction to the original spec:** the backlog doc specified `PromoCode.RewardType`/`RewardValue`
"reuse the enum from item 4" (Client-to-Client Referral Program). Verified: item 4 has not been
built — no `ClientReferralCode` entity, no `RewardType` enum exists anywhere in the codebase today
(`Pena_e_Arte.Domain` has no `RewardType.cs`), and item 4 is in the decision-gated Group 3 backlog
with no scheduled build date. There is nothing to reuse. Do not invent a `RewardType` enum either —
`DepositRule` already establishes the exact idiom for "fixed amount or percent, pick one" with two
nullable decimal fields (`AmountFixed`/`AmountPercent`), not a discriminator enum. Mirror that
shape exactly for consistency with the existing codebase convention (CLAUDE.md rule #6):

```csharp
public class PromoCode : TenantEntity
{
    public string Code { get; set; } = string.Empty;
    public decimal? AmountFixed { get; set; }
    public decimal? AmountPercent { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int? MaxRedemptions { get; set; }
    public int RedemptionCount { get; set; }
    public bool IsActive { get; set; }
}
```

Because `RewardType` doesn't exist, drop the original spec's "resolve the stacking-with-referral-
rewards question" requirement entirely — there is no referral reward to stack against tonight.
Leave a one-line code comment at the discount-application site (see Backend below) noting that a
future item-4 build must decide the stacking rule when it lands; do not attempt to design that
now.

The discount reduces `depositAmount` (not a separate "total price" — there is no such field on
`Appointment`; deposits are the only monetary amount this booking flow calculates, per
`DepositCalculator`). Percent discounts apply to the calculated deposit, floored at 0.

### Backend

- `Pena_e_Arte.Domain/Entities/PromoCode.cs` — as above.
- Migration `AddPromoCodes`.
- `EF Core` configuration mirroring `DepositRuleConfiguration` (find it in
  `Pena_e_Arte.Infrastructure/Persistence/Configurations/` before writing from scratch).
- `CreatePromoCodeCommand`/`UpdatePromoCodeCommand`/`DeletePromoCodeCommand` (soft-delete, matching
  `DepositRule`'s pattern) — `OwnerOnly`.
- `GetPromoCodesQuery` — `OwnerOnly`, studio-scoped list for the management page.
- Add `string? PromoCode` as a new **trailing** optional parameter on
  `Pena_e_Arte.Contracts.Requests.CreateAppointmentRequest` (it is a positional record — append
  after `Images`, do not insert in the middle, to avoid breaking any existing positional
  construction call sites; grep `new CreateAppointmentRequest(` first to confirm none exist that
  would be silently misaligned).
- In `CreateAppointmentCommand.cs`'s `CreateAppointmentCoreAsync`, immediately after the existing
  line `decimal depositAmount = DepositCalculator.Calculate(rule, artist?.HourlyRate,
  req.DurationMinutes);`, add promo lookup + application:
  - Look up an active, non-expired `PromoCode` by `req.PromoCode` (case-insensitive), studio-scoped,
    `IgnoreQueryFilters()` is NOT needed here — this runs inside the normal tenant-scoped path
    (unlike the artist-availability checks above it in the same method, which need it because
    they also serve the anonymous guest path with no ambient tenant; `PromoCode` lookup can use
    `tenant`-filtered access since `studioId` is already an explicit parameter — actually check:
    this method runs for BOTH guest and authenticated callers and has no ambient `ICurrentTenant`
    (it's a `static` method taking `studioId` explicitly) — so it DOES need
    `db.PromoCodes.IgnoreQueryFilters().Where(p => p.StudioId == studioId && ...)`, same reasoning
    as the `DepositRules` query three lines above it. Follow that exact precedent, not the
    tenant-filtered assumption above — copy the `DepositRules` query's shape.
  - If found and `MaxRedemptions` not yet reached: compute discount from `AmountFixed`/
    `AmountPercent` (percent applies to `depositAmount`), subtract, floor at 0, increment
    `RedemptionCount`, save.
  - If the code doesn't resolve (missing, expired, exhausted, wrong studio): do not throw — silently
    ignore the code (deposit unaffected). A guest fat-fingering a promo code should not block their
    booking. Return the applied/not-applied fact in the response so the frontend can show
    "Promo code not found" without it being a hard validation error (see Frontend).
  - Leave the future-stacking comment here per the design decision above.
- `AppointmentResponse` gains a `bool PromoCodeApplied` field (positional record — append at the
  end, matching the append-only convention already used for prior additions to this response; grep
  every construction site of `AppointmentResponse` first, same care as Group 1's item-17 work).

### Frontend

- Owner: `/promo-codes` management page (new route + nav entry), same shape/table as the deposit
  rules management page (find and mirror its component before writing from scratch).
- Client/guest: promo code entry field in `BookAppointmentForm.tsx` (and the guest checkout
  equivalent that shares `TattooIntakeFields.tsx`/the same request shape) — plain text input,
  optional, submitted as `promoCode` on the booking request. On response, if
  `promoCodeApplied === false` and a code was entered, show a small inline "Promo code not
  recognized or expired" note rather than blocking submission (the booking already succeeded).

### Tests

- Unit: `PromoCode` discount math (fixed, percent, floor-at-zero, expired, exhausted, wrong studio
  — each a separate case).
- Integration: booking with a valid code reduces `DepositAmount`; booking with an invalid/expired
  code still succeeds with `PromoCodeApplied = false`; `RedemptionCount` increments exactly once
  per successful booking; `MaxRedemptions` enforced.

### Help sync

- `owner-promo-codes` help entry + manual section (creation/management).
- Update the existing `client-book-appointment` help article to mention the optional promo code
  field.

---

## PHASE 2 — Dunning / Failed-Payment Recovery Flow (#14)

### Design decisions (pre-resolved — do not re-litigate)

**Correction to the original spec:** it instructed grepping for `"TrialWarning"` to find an
existing job pattern to mirror — that literal name does not exist. The actual jobs are
`TrialExpiryWarningJob.cs` and `GracePeriodEndJob.cs`, but **do not mirror their scheduling
pattern** — both are one-shot, per-studio jobs scheduled via `IJobScheduler.ScheduleX(studioId,
enqueueAt)` at a single computed future instant (e.g. at trial start, schedule a warning for
trial-start + 12 days). Dunning needs the opposite shape: a single job that scans **every**
past-due subscription once a day and decides per-subscription whether an escalation email is due.
The correct pattern to mirror is the existing **daily recurring scan jobs** —
`TrafficRollupJob.cs`, `RetentionPurgeJob.cs`, `PaymentReconciliationJob.cs` — registered via
`IRecurringJobManager.AddOrUpdate<T>("job-name", j => j.RunAsync(...), Cron.Daily(hour: N))` in
`Program.cs`'s one-time-setup block. Existing daily jobs are staggered at 02:00
(payment-reconciliation), 02:30 (traffic-rollup), 03:00 (instagram-sync), 04:00 (retention-purge),
05:00 (guest-pending-upload-cleanup), 06:00 (r2-export). Register the new job at **07:00 UTC**,
continuing the stagger.

**Also verified, no conflict:** platform SaaS billing (the studio's own subscription to
TattooOS) is still processed through Stripe directly
(`Pena_e_Arte.Application/Billing/Commands/HandleSubscriptionUpdatedCommand.cs`, driven by Stripe
webhooks, setting `SubscriptionStatus.PastDue` on a `"past_due"` event). This is a **separate**
Stripe integration from `IPaymentProvider`/`NullPaymentProvider` (client-facing tattoo-deposit
card processing, removed 2026-07-31 for the Albania-entity compliance reason documented in
`IPaymentProvider.cs`). Dunning is unaffected by that removal — do not treat this as blocked.

**Critical correction — the dashboard banner as originally specified cannot work as written.**
`Pena_e_Arte.API/Middleware/TenantMiddleware.cs` already hard-blocks **every** request (not just
writes) once `Subscription.Status == SubscriptionStatus.PastDue`, for any path other than the
exempt prefixes (`/api/v1/auth`, `/api/v1/billing`, `/api/v1/webhooks`, `/health`, `/metrics`,
`/hangfire`, `/hubs`) and one special-cased route, `GET /api/v1/studios/me` (kept reachable so the
frontend can read `isActive: false` and render a banner at all). This means a past-due studio
cannot load its normal dashboard to see a banner placed there — the banner has to live somewhere
that survives the block. That place already exists:
`frontend/src/shared/components/SuspensionBanner.tsx`, rendered in `OwnerLayout.tsx` (and
Artist/Client layouts), driven off exactly the `GET /api/v1/studios/me` response the middleware
keeps reachable. Today it only checks a flat `isActive === false` boolean and shows one generic
message for every non-active status. **Extend `SuspensionBanner`, don't add a new banner
component**: thread the actual `subscriptionStatus` (and a new `pastDueSince` timestamp, see
below) through `StudioResponse`, and branch the message specifically for `PastDue` with a "days
overdue" count — everything else (Cancelled, GracePeriod, generic suspension) keeps today's
copy.

`Subscription` has no field recording when it entered `PastDue` — add one. It must be set on
transition into `PastDue` and cleared on transition out (to `Active` or `Cancelled`), inside
`HandleSubscriptionUpdatedCommand`, the single place `Status` is currently mutated by the Stripe
webhook.

Owner exclusion from automated dunning is a per-studio admin toggle, structurally identical to
the existing `ExtendTrialCommand` (`Pena_e_Arte.Application/Platform/Commands/ExtendTrialCommand.cs`)
— cross-tenant `IgnoreQueryFilters()` (cite the same "usage #5, AdminOnly" approved-usage comment
that file already uses), `IAuditableCommand`, registered under the `AdminOnly`-gated
`/api/v1/platform` group in `PlatformEndpoints.cs`. Mirror that file's shape exactly.

### Backend

- `Subscription.PastDueSince DateTime?` (default null) — migration `AddSubscriptionPastDueSince`.
- `Subscription.DunningExcludedManually bool` (default false) — same migration.
- In `HandleSubscriptionUpdatedCommand.Handle`, immediately after the existing
  `subscription.Status = command.StripeStatus switch { ... }` assignment: set
  `subscription.PastDueSince = DateTime.UtcNow` when the new status is `PastDue` and the old
  status wasn't already `PastDue` (read the old status into a local before the switch
  overwrites it); clear `PastDueSince = null` whenever the new status is anything else.
- `PastDueReminderJob` (new, `Pena_e_Arte.Infrastructure/Jobs/`) — constructor shape mirrors
  `TrafficRollupJob`/`RetentionPurgeJob` (`IAppDbContext db, ILogger<PastDueReminderJob> logger`,
  plus `INotificationService notifications`). `RunAsync(CancellationToken ct)`: query all
  `Subscription`s with `Status == PastDue && !DunningExcludedManually`, compute
  `daysPastDue = (int)(DateTime.UtcNow - PastDueSince.Value).TotalDays` for each, and send an
  email via `INotificationService.SendEmailAsync` only when `daysPastDue` is exactly 1, 3, or 7
  (not "at least" — a studio checked daily should get exactly three escalating emails, not one
  every day past the first threshold). Log a `NotificationLog` row per send, matching
  `TrialExpiryWarningJob`'s logging pattern. Idempotent-safe: since it only fires on exact-day
  matches and the job runs once daily, a normal run never double-sends; document that a missed
  run (job failure) skips that day's message rather than catching up, and that this is
  acceptable (it's a reminder, not a legal notice).
- Register in `Program.cs`'s recurring-job block: `recurringJobs.AddOrUpdate<PastDueReminderJob>(
  "past-due-reminder", j => j.RunAsync(CancellationToken.None), Cron.Daily(hour: 7));` — comment
  noting the stagger, matching the existing comments on the jobs above it.
- `SetDunningExclusionCommand(Guid StudioId, bool Excluded)` — mirror `ExtendTrialCommand.cs`
  exactly: `IAuditableCommand`, new `AuditActions` constant (add
  `SubscriptionDunningExclusionChanged` to `Pena_e_Arte.Domain/Constants/AuditActions.cs`),
  `AuditTargetTypes.Subscription` (already exists). Endpoint:
  `POST /api/v1/platform/studios/{studioId:guid}/subscription/dunning-exclusion`, added to the
  existing `AdminOnly` group in `PlatformEndpoints.cs` alongside
  `studios/{studioId}/subscription/activate`.
- `StudioResponse` (or wherever `GET /api/v1/studios/me` sources its DTO — check
  `GetMyStudioQuery`/equivalent) gains `SubscriptionStatus` (already may be present — verify
  before adding a duplicate field) and `PastDueSince` for the banner to consume.
- `SubscriptionOversightPage.tsx`'s backing query/response gains a `daysPastDue`-derived value —
  the page already has "Overdue since: {fmt(sub.currentPeriodEnd)}" copy at ~line 152; extend that
  to also show the days-past-due count and make the column sortable (it currently is not).

### Frontend

- `SuspensionBanner.tsx`: accept `subscriptionStatus`/`pastDueSince` (from the studio response),
  branch a `PastDue`-specific message: `"Your subscription payment is N days overdue. Update your
  billing details to avoid service interruption."` with the existing "Contact support" /
  "reactivate your subscription" links kept as-is for the owner role. Leave the artist/client
  branch messages untouched (they already handle "suspended" generically and PastDue-vs-other
  distinction is only actionable by the owner anyway).
- Admin: `SubscriptionOversightPage.tsx` — days-past-due sortable column; per-subscription
  dunning-exclusion toggle (calls the new command), placed in whatever per-row action menu the
  page already uses for other admin actions (find it before adding a new UI pattern).

### Tests

- Unit: `PastDueReminderJob` — sends on exactly day 1/3/7, not on day 2/4/5/6, not when
  `DunningExcludedManually`, not when already back to `Active`. `HandleSubscriptionUpdatedCommand`
  — `PastDueSince` set on entry, cleared on exit, unaffected by other status transitions.
- Integration: `SetDunningExclusionCommand` — `AdminOnly` enforced, `AuditLogEntry` written.
  `GET /api/v1/studios/me` still reachable and returns the new fields even when
  `Status == PastDue` (regression-guard for the existing exemption).

### Help sync

- `owner-past-due` help entry (what happens when payment fails, what the banner means, the
  three-email schedule).
- Admin manual section update for the dunning-exclusion toggle.

---

## PHASE 3 — Custom Intake-Form Fields (#12)

### Design decisions (pre-resolved — do not re-litigate)

**Correction to the original spec — this is the most significant one in this document.** The
backlog doc describes today's intake form as "a single freeform textarea
(`TattooDescription`/`SafetyNotes` on `Appointment`)" and proposes replacing it. That target is
wrong on every count: `TattooDescription`/`SafetyNotes` live on `BookingIntake` (one-to-one with
`Appointment`), not on `Appointment` directly, and — more importantly — **that is not the
freeform-textarea intake system this item means to replace.** `BookingIntake` captures what the
client wants done at booking time (tattoo description, desired placement, referral source) and
was already extended once this session (Group 1, item #17). It is not a form the studio
configures.

The actual freeform-textarea intake system is a **separate, already-built, already-shipped**
feature: the `IntakeForm` entity (`Pena_e_Arte.Domain/Entities/IntakeForm.cs`), submitted via
`SubmitIntakeFormCommand`/`SubmitIntakeFormPage.tsx`, with a single `FormData string` field that
today is rendered as one plain textarea labeled "Medical history & notes"
(`SubmitIntakeFormPage.tsx`, confirmed by direct read of the component — a static Zod schema with
one `formData` string field, no per-field structure). It already has its own consent-tracking
plumbing (`ConsentTemplateId`/`ConsentTextSnapshot`/`ConsentedAt`, resolved against
`ConsentTemplate` kind `IntakeFormConsent`) which this item does not touch. `ConsentTemplate` (the
original spec correctly flagged this as distinct) is legal-liability consent text with versioning
— still correctly out of scope, don't merge it with the new template system below.

Build the owner-configurable structured form against **`IntakeForm`**, not `Appointment`. Do not
add a new `Appointment.IntakeFormResponsesJson` field (the original spec's proposal) — `IntakeForm`
already has a `FormData string` column that exists for exactly this purpose; once fields are
structured, `FormData` holds the JSON-serialized responses instead of a plain string, with no
schema change to `IntakeForm` itself needed for the response side. Only the new template entity is
new schema.

```csharp
public class IntakeFormTemplate : TenantEntity
{
    public bool IsActive { get; set; }
    public string FieldSchemaJson { get; set; } = string.Empty; // JSON array of {label, type, required, options?}
}
```

Field types: `Text`, `Textarea`, `Select`, `Checkbox`, `Date` — keep the schema minimal, this is a
form builder, not a general-purpose CMS. When no active template exists for a studio, fall back to
today's single-textarea behavior exactly as-is (labeled "Medical history & notes") — do not force
every studio to configure a template before clients can submit anything.

### Backend

- `Pena_e_Arte.Domain/Entities/IntakeFormTemplate.cs` as above. Migration `AddIntakeFormTemplate`.
- `UpsertIntakeFormTemplateCommand` — `OwnerOnly`. Validate `FieldSchemaJson` deserializes to a
  non-empty array of the five allowed types (FluentValidation custom rule); cap at, say, 20
  fields — this is a form builder, not unbounded.
- `GetActiveIntakeFormTemplateQuery` — mirror `GetActiveConsentTemplateQuery` exactly (already
  used by `SubmitIntakeFormPage.tsx` via `useGetActiveConsentTemplateQuery` — find and copy its
  resolution-by-studio shape, including the null-means-no-template-configured case). Needs to be
  reachable by `ClientAndAbove` (authenticated submission) — check whether guest/anonymous intake
  submission exists at all before deciding if `AllowAnonymous` is also needed here (verify against
  `IntakeFormsEndpoints.cs` or wherever `SubmitIntakeFormCommand` is currently exposed; if intake
  forms are only ever submitted by an authenticated client post-booking, no anonymous access is
  needed and none should be added).
- No change to `IntakeForm` entity or `SubmitIntakeFormCommand`'s persistence — `FormData` already
  accepts up to 65535 chars (`SubmitIntakeFormValidator`), which comfortably holds serialized JSON
  for a 20-field structured response; the validator's `NotEmpty().MaximumLength(65535)` rule
  already covers both the plain-string and JSON-string cases with no change needed.

### Frontend

- Owner: intake-form builder page (`/intake-form-builder` or similar route) — add/remove/reorder
  fields, set required/type/options. New page, no existing component to mirror closely; keep the
  UI simple (a list of field rows, each with type dropdown + label input + required checkbox +
  options textarea shown only for `Select`).
- `SubmitIntakeFormPage.tsx` — **highest-risk change in this phase**, since it replaces a
  hardcoded, working, frequently-exercised form. Fetch the active template via
  `useGetActiveIntakeFormTemplateQuery`; when null, render exactly today's single textarea
  (zero behavior change for studios that never configure a template — this is the fallback path,
  not a deprecated one). When a template exists, render its fields dynamically (`Textarea`,
  `Input`, `Select`, `Checkbox`, a date picker matching whatever date-input pattern the codebase
  already uses elsewhere — check `ArtistScheduleEditor.tsx` or similar for the existing date-input
  component before adding a new one) and serialize responses to a JSON object keyed by field
  label before submitting as `formData`. Budget a full manual QA pass across: guest with no
  template, client with a configured template, owner-configured-empty-template edge case (0
  fields — should probably block save on the builder side rather than let a submitter see a blank
  form).

### Tests

- Unit: `UpsertIntakeFormTemplateCommand` validator (field-type whitelist, field-count cap,
  malformed JSON rejected). `GetActiveIntakeFormTemplateQuery` resolution (studio-specific
  overrides platform-default-null correctly — actually there is no platform-default template
  concept here unlike `ConsentTemplate`; confirm the query is purely "does this studio have one
  active" with no null-studio fallback, since `IntakeFormTemplate` is a plain `TenantEntity` not
  shaped like `ConsentTemplate`).
- Frontend: `SubmitIntakeFormPage` renders the plain-textarea fallback with no template, and
  renders all five field types correctly with a template configured; submitted payload is valid
  JSON matching the schema.

### Help sync

- `owner-intake-form-builder` help entry + manual section.
- Update the existing intake-form-submission help article's steps/screenshots if the
  freeform-textarea description no longer matches reality for studios with a template configured
  (word it as "your studio's intake form may look different if your studio has customized it").

---

## PHASE 4 — Flash/Design Catalog (#9)

### Design decisions (pre-resolved — do not re-litigate)

**Confirmed correct in the original spec:** `Design.ClientId` is today non-nullable
(`Pena_e_Arte.Domain/Entities/Design.cs`), and making it nullable (for a catalog item with no
client yet) is a real schema change against production data. Every current access site was
enumerated by this session's research — six files:
`Pena_e_Arte.Application/Designs/Commands/CreateDesignCommand.cs`,
`Pena_e_Arte.Application/Designs/Commands/ReviewDesignCommand.cs`,
`Pena_e_Arte.Application/Designs/Queries/GetDesignsQuery.cs`,
`Pena_e_Arte.Application/Designs/Validators/CreateDesignValidator.cs`,
`Pena_e_Arte.Infrastructure/Persistence/Configurations/DesignConfiguration.cs`, plus the FK
constraint itself. Check each of the six for a `.ClientId`/`Client` null-forgiving or
non-null-assuming access before making the column nullable, and fix any that would break.

**Correction to the original spec — wrong frontend file named for artist-facing management.**
The backlog doc says to add the "Flash" tab (mark items as catalog, set price) to
`ArtistPortfolioPage.tsx`. That component lives in `frontend/src/features/public/components/` and
is the **public, guest-facing** portfolio display — an artist does not manage their own catalog
items there. Portfolio-image management (the existing per-image Style/Category dropdowns this
item's UI should sit beside) is in
`frontend/src/features/artists/components/ArtistDetailPage.tsx` — confirmed by direct read
(`updateImageStyle`/`updateImageCategory` functions, ~lines 270–290). Put the artist-facing
"mark as flash catalog item, set price" controls there, matching the existing per-image
Style/Category dropdown pattern exactly. The **guest/client-facing** "Flash" browsing tab and
"Book this design" CTA correctly belong on the public `ArtistPortfolioPage.tsx` as the original
spec said — that part was right, only the management-side file was wrong.

**Correction/clarification — the clone-on-book linkage the original spec left unspecified.** The
backlog doc says the new appointment must be "linked to the clone" of the catalog `Design` but
never specifies the mechanism, and today `Design` has **no `AppointmentId` field at all** — it is
already a standalone client/artist "design thread" not tied to any specific `Appointment` by FK,
even for organically-created (non-catalog) designs (confirmed: no `AppointmentId` anywhere in
`Design.cs`, `DesignRevision.cs`, or `DesignConfiguration.cs`). A catalog-booked design should not
be a special case here — do not add a new FK just for this feature. Instead:
1. Clone the catalog `Design` row (new `Id`, `ClientId` = the booking client, `ArtistId`
   unchanged, `IsCatalogItem = false`, `Title` = original title, e.g. `"{OriginalTitle} (flash
   booking)"`) and clone its latest `DesignRevision` (new `Id`, same `FileUrl`, `VersionNumber =
   1`, `Notes` noting it originated from a catalog booking). This seeds an independent
   design-approval thread for the artist exactly as if they'd created it manually for this client
   — consistent with how every other `Design` already works, not a new pattern.
2. Separately, and this is the actual booking-visible link: attach the flash design's image to
   the **new appointment** as a `Reference`-category `AppointmentAttachment` — this mechanism
   already exists and needs no schema change (`AppointmentAttachmentCategory.Reference`, already
   used for client-submitted reference images at booking time). This is what the client and artist
   actually see on the appointment itself; the cloned `Design` row is a separate artist-side
   record for managing that specific commissioned/re-used piece going forward, exactly like any
   other design thread.

This avoids adding new schema entirely and avoids the "which appointment does this design belong
to" ambiguity by not needing an answer — the attachment carries the booking-visible reference, the
cloned Design carries the artist's ongoing thread, and neither needs to point at the other any
more than they do for a non-catalog booking today.

**New `AllowAnonymous` endpoint required** — the public catalog browse endpoint needs a new row in
`docs/claude/architecture.md`'s `## AllowAnonymous Exceptions` table (~line 1125). Follow the exact
row format already there; the closest precedent is the `GET /api/v1/public/portfolio/feed` row
("Public discovery portfolio feed" / "None — read-only public images, no PII").

### Backend

- Add `Design.IsCatalogItem bool` (default false) and `Design.Price decimal?`. Migration
  `AddDesignCatalogFields`. Apply the nullability fix to `Design.ClientId` (→ `Guid?`) in the same
  migration, after fixing the six call sites identified above.
- `GetDesignCatalogQuery` — public, studio-scoped, `Design.IsCatalogItem == true && ClientId ==
  null`. `GET /api/v1/public/studios/{slug}/design-catalog`, `AllowAnonymous` — add the new
  architecture.md table row in the same commit that adds this endpoint (CLAUDE.md hard rule: never
  add an `AllowAnonymous` endpoint without a table row).
- `MarkDesignAsCatalogItemCommand` (or extend an existing `UpdateDesignCommand` if one exists —
  check before adding a new command for what might be a one-field toggle) — `ArtistAndAbove`,
  sets `IsCatalogItem`/`Price`, and must null out `ClientId` when marking as a catalog item (a
  design can't simultaneously belong to a specific client and be a reusable catalog template)
  — reject with a clear validation message if the design already has approved revisions tied to
  an active client relationship, rather than silently orphaning client-facing history.
- `RequestCatalogDesignCommand(Guid CatalogDesignId, CreateAppointmentRequest BookingRequest)` —
  clones the `Design`+latest `DesignRevision` as specified above, then calls the existing
  `internal static CreateAppointmentHandler.CreateAppointmentCoreAsync(...)` (already `internal
  static`, callable in-assembly — same pattern `CreateGuestAppointmentCommand` already uses),
  passing the catalog design's `FileUrl` as an additional `Reference`-category image in
  `BookingRequest.Images` if not already present (avoid a duplicate attachment if the client's
  request already includes it). `ClientAndAbove` for the authenticated path; if guest booking of
  catalog items is in scope (check with the guest-checkout flow's existing capabilities before
  assuming yes or no — the original spec's frontend section implies guests can browse and book, so
  most likely yes), add the equivalent guest-facing command mirroring
  `CreateGuestAppointmentCommand`'s shape.

### Frontend

- Artist/Owner: in `ArtistDetailPage.tsx`, beside the existing per-image Style/Category
  dropdowns, add a "Mark as flash catalog item" toggle + price input (only for designs, not raw
  `PortfolioImage` entries — confirm the page's data model distinguishes the two lists before
  wiring this in the wrong place).
- Guest/client: "Flash" tab on the public `ArtistPortfolioPage.tsx` (catalog items grid, price
  shown), "Book this design" CTA that pre-fills `BookAppointmentForm.tsx` (or the guest-checkout
  equivalent) with the catalog design's reference image and title.

### Tests

- Unit: catalog clone-on-book produces a new `Design`+`DesignRevision` with a fresh `Id`,
  original catalog item's `Design` row completely unmodified by the booking (this is the
  data-corruption regression this phase exists to prevent — write a test that books the same
  catalog item twice as two different clients and asserts both end up with independent Design
  rows and the catalog original is untouched).
- Integration: `GetDesignCatalogQuery` — `AllowAnonymous`, only returns `IsCatalogItem &&
  ClientId == null` rows, tenant-scoped by slug. `RequestCatalogDesignCommand` — booking succeeds,
  attachment present on the new appointment, cloned Design has correct `ClientId`/`ArtistId`.

### Help sync

- `artist-flash-catalog` help entry + manual section.
- Tour step on `artistTour.ts` if flash cataloging becomes a documented primary artist workflow
  (recommended — differentiator for the tattoo vertical per CLAUDE.md rule #6's industry-parity
  guidance).

---

## Out of Scope — flagged explicitly, not silently dropped

- **Item 4 (Client-to-Client Referral Program) stacking with Promo Codes**: whichever future
  session builds item 4 must decide the stacking rule (additive vs. exclusive) against the
  `PromoCode` discount added in Phase 1. Not resolved here because item 4 doesn't exist yet.
- **Dunning escalation beyond email**: no SMS/in-app-notification escalation tier was added; the
  original spec didn't ask for one and this document doesn't add scope.
- **`PastDueReminderJob` missed-run catch-up**: documented in Phase 2 as an accepted gap (a missed
  daily run skips that day's threshold rather than catching up later) — revisit only if it becomes
  an actual support complaint.
- **Guest booking of catalog designs**: Phase 4 assumes guests can book catalog items (matching
  the original spec's frontend section) but flags this as needing a one-line confirmation against
  the actual guest-checkout capability list before building the guest-facing command — if guest
  catalog booking turns out to be explicitly unsupported by existing product decisions, build the
  authenticated-client path only and document the guest gap here instead of guessing.
- **`IntakeFormTemplate` versioning**: unlike `ConsentTemplate`, the new template has no version
  history — editing it in place changes what every future submitter sees, with no snapshot of what
  a past submitter saw (this mirrors `IntakeForm.FormData`'s existing lack of schema versioning,
  not a new gap introduced here, but worth flagging since `ConsentTemplate` right next to it does
  version).

---

## PHASE 5 — Backlog Carry-Forward

Do not attempt Group 3 items (waitlist #1, gift cards #2, packages #3, client-to-client referral
#4, marketing campaigns #10, support impersonation #15, booth-rent #8) — every one needs a product
decision first. If time remains after Phases 1–4 and their tests are green, do not pull forward
any Group 3 item on your own judgment; stop and let the next session's scope be set explicitly.

## Final self-check

- [ ] All four phases: `dotnet build` clean, `dotnet test` green (unit + integration).
- [ ] `pnpm tsc`/`pnpm lint`/`pnpm test` green.
- [ ] `pnpm build` clean.
- [ ] Every new `TenantEntity` has a migration and the standard global query filter.
- [ ] Every `IgnoreQueryFilters()` call has a comment citing its approved-usage precedent.
- [ ] The new `AllowAnonymous` endpoint (Phase 4) has its architecture.md table row.
- [ ] Help sync completed for every user-facing addition (helpContent.ts, manual, tours).
- [ ] `Design.ClientId` nullability change: all six identified call sites checked and fixed if
      needed.
- [ ] `SuspensionBanner.tsx` change doesn't regress the existing generic-suspension /
      artist / client message paths — only `PastDue` gets new copy.
- [ ] `SubmitIntakeFormPage.tsx`'s no-template fallback renders identically to today's behavior
      (manual QA, not just unit tests, per the risk note in Phase 3).

## Final Deliverable

Append one Decisions Log entry per phase (four entries) to `docs/claude/architecture.md`,
following the existing entry format (bold title + date, then the same prose-paragraph style used
by every prior entry — no bullet lists inside an entry). Each entry must note: what was built, the
concrete correction made versus the original backlog spec (cite the specific wrong claim), and the
verified test status.

Commit message:

```
feat: P1 backlog Group 4 — promo codes, dunning, custom intake fields, flash catalog (#11, #14, #12, #9)

Builds the four Group 4 items from the 2026-09-09 P1 backlog audit. Corrects several
stale/incorrect premises in the original spec found during implementation:
- Promo codes built standalone (RewardType enum from item 4 doesn't exist; item 4 unbuilt)
- Dunning job follows the daily-recurring-scan pattern, not the one-shot per-studio pattern
  the original "grep TrialWarning" pointer would have led to (that job doesn't exist)
- Custom intake fields target IntakeForm.FormData, not Appointment/BookingIntake as originally
  specified — those are a different, unrelated feature
- Flash catalog management UI corrected to the artist-facing ArtistDetailPage.tsx, not the
  public-facing ArtistPortfolioPage.tsx the original spec named

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01S2ze276hAyTgmpbWntd3Kf
```
