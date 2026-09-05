# Overnight Prompt — Guest Checkout Booking + Intake Form

> Date: 2026-08-31
> Target: `Pena_e_Arte.Domain`, `Pena_e_Arte.Contracts`, `Pena_e_Arte.Application` (Auth, Appointments,
> Files, Public, Reminders no-op check), `Pena_e_Arte.Infrastructure` (one EF migration, one Hangfire
> job, rate-limiting extension, one email template), `Pena_e_Arte.API`, `frontend/src/features/{appointments,
> booking,public,clients}`, backend + frontend tests, `architecture.md`'s `AllowAnonymous Exceptions` and
> `IgnoreQueryFilters()` tables, Help Menu (`helpContent.ts`), standalone user manual (`index.html`),
> onboarding tours.
>
> **New capability, not a form tweak.** This is this codebase's first `AllowAnonymous` endpoint that
> writes a full tenant-scoped domain graph (Identity user + `Client` + `Appointment` + `AppointmentAttachment`
> + `IntakeForm`) in one call — every existing anonymous write (`/auth/register`, the contact form, review/
> conduct-report submission) either only touches `AspNetUsers`+one linked row, or requires an existing
> authenticated identity. Treat every new anonymous surface here (the booking endpoint, the presign
> endpoint, the two new public read endpoints) as security-sensitive by CLAUDE.md rule #1/#2 — this
> prompt specifies the guardrails for each one explicitly; do not loosen them "to make the form work."
>
> One new EF Core migration. No new npm or NuGet packages — `libphonenumber-js` (`PhoneInput`),
> `usePresignedUpload`, `BodyMap`, and the Redis rate limiter all already exist and are reused as-is.
>
> This prompt is the sole design record for this feature — there is no separate `feature-spec-*.md`
> paired with it. The "Decisions" table below is the equivalent of that spec's "resolved, do not
> re-litigate" section; treat it the same way `overnight-prompt-studio-choice-booking-2026-08-21.md`
> treats its own predecessor spec.

---

## Pre-flight

1. Read `CLAUDE.md`, `docs/claude/backend.md`, `docs/claude/frontend.md`, `docs/claude/database.md`,
   `docs/claude/conventions.md` before making any changes.
2. Baseline, before touching anything:
   - `dotnet build`
   - `dotnet test` — note the current pass count; pre-existing failures are not this prompt's problem,
     but do not introduce new ones.
   - `pnpm tsc --noEmit`
   - `pnpm test src/features/appointments src/features/booking src/features/clients src/shared/components/ui`
     — confirm all green first.
3. Read these files **in full** before writing any code — this prompt assumes you have them, and several
   Parts below extract shared logic out of them rather than duplicating it:
   - `Pena_e_Arte.Application/Appointments/Commands/CreateAppointmentCommand.cs`
   - `Pena_e_Arte.Application/Appointments/Queries/CheckSlotAvailabilityQuery.cs`
   - `Pena_e_Arte.Application/Auth/Commands/RegisterUserCommand.cs` — the anonymous account+`Client`
     creation/linking pattern this prompt's guest flow reuses almost verbatim.
   - `Pena_e_Arte.Application/Common/ClientAccountExtensions.cs`
   - `Pena_e_Arte.Application/Public/Queries/GetPublicStudioQuery.cs` — the slug-resolution +
     `IgnoreQueryFilters()` pattern every new public handler below follows.
   - `Pena_e_Arte.Application/Files/Queries/GetPresignedUploadUrlQuery.cs` + its validator
   - `Pena_e_Arte.Domain/Interfaces/IIdentityService.cs`
   - `Pena_e_Arte.Domain/Entities/ClientProfile.cs` + `Infrastructure/Persistence/Configurations/ClientProfileConfiguration.cs`
     — the `BodyMap` value-object JSON-column pattern `IntakeForm` reuses.
   - `frontend/src/features/appointments/components/BookAppointmentForm.tsx` in full — Part 6 extracts
     three sub-components out of it and adds new fields to what remains.
   - `frontend/src/shared/components/ui/phone-input.tsx` — reused as-is for the guest's phone field.
   - `frontend/src/features/clients/components/BodyMap.tsx` — reused as-is for "desired placement."
   - `Pena_e_Arte.API/Endpoints/PublicEndpoints.cs`, `Pena_e_Arte.API/Extensions/RateLimitingExtensions.cs`

---

## Addendum — 2026-08-31 (execution session, before any code written)

**The Context section below is stale on one material point, and Decision #4's entity name is
corrected as a result.** `IntakeForm` is NOT a documented-but-never-implemented gap — it shipped
2026-07-26 (`09ed943 feat(forms): implement Feature 02 — intake/consent forms + security fixes +
full test suite`), over a month before this prompt was written. The real `IntakeForm`
(`Pena_e_Arte.Domain/Entities/IntakeForm.cs`) is `ClientId` + nullable `AppointmentId` +
`FormData` (string blob) + `FileUrl` + `SubmittedAt` — a studio-sent intake/consent-style form a
client fills out, with its own Commands/Queries (`Pena_e_Arte.Application/IntakeForms/`),
`FormEndpoints.cs`, a frontend `features/forms` module, and its own test suite. It is a different
concept from what this prompt needs (booking-content captured at booking time: tattoo
description, desired placement, referral source, safety notes), and reusing its name for a 1:1-
with-`Appointment` entity of an incompatible shape would either fail to compile (duplicate class)
or silently corrupt the shipped feature.

**Resolution (best-practice call, not a re-litigation of the product decision in #4 — only the
entity's name/identity changes, not its purpose):** the new entity is named **`BookingIntake`**,
not `IntakeForm`, everywhere in this prompt. Read every `IntakeForm`/`IntakeForms` reference in
Parts 1, 2, 3, 6, 7, 8 below as `BookingIntake`/`BookingIntakes` — table `booking_intakes`, DbSet
`BookingIntakes`, folder `Pena_e_Arte.Application/BookingIntakes/` (not `Public/` — see Part 3c's
actual location), configuration `BookingIntakeConfiguration`. The existing `IntakeForm`
feature is untouched by this prompt. Feature Module Map row #02 is NOT updated to point at this
work (it already correctly lists `IntakeForm` as implemented) — instead a new row is added for
booking-content intake if warranted (see Part 4c/DoD).

---

## Context — current state (verified against live source, 2026-08-31)

- **`/book` is a fully protected route today.** `router.tsx` gives it to the `client` role only;
  `BookAppointmentForm.tsx` renders inside it and has no unauthenticated path at all. There is **no
  guest/anonymous booking capability anywhere in this codebase** — "Guest/Visitor QA Pass" in
  `architecture.md` is about an unauthenticated visitor *browsing* public pages, not booking.
- **Client sign-up collects almost nothing today.** `RegisterUserRequest(Email, Password, Role, StudioId?,
  FirstName?)` — no `LastName`, no `Phone`, no marketing consent. `Client.cs` already **has** `FirstName`,
  `LastName`, `Phone` columns (`Phone` nullable) — they're just never populated at signup
  (`RegisterUserHandler` sets `LastName = string.Empty`, leaves `Phone` null). **No entity change needed
  for name/phone** — only for `MarketingOptIn`, which doesn't exist yet (Part 1c).
- **`RegisterUserHandler` is the exact template this feature's account-creation logic reuses**: it's
  already `AllowAnonymous`, already does `IgnoreQueryFilters()` `Client` lookup-by-`(StudioId, Email,
  UserId == null)` to link a studio-pre-created `Client` row or create a fresh one (approved exception
  #28), and already treats email-send failure as non-fatal (`try/catch` + `LogWarning`, registration still
  succeeds). Reuse this shape; do not invent a second one.
- **`IIdentityService` already has every primitive this feature needs — no interface change required**:
  `CreateUserAsync` (email+password+role+studioId+firstName), `GeneratePasswordResetTokenAsync`,
  `GenerateEmailConfirmationTokenAsync`, and — critically — `GetUserIdByEmailAsync`, which is exactly
  what's needed to detect "this email already has an account" before creating a duplicate one.
- **`database.md` documents an `IntakeForm` entity in its canonical `DbContext` example
  (`DbSet<IntakeForm> IntakeForms`, with a tenant query filter) that does not exist anywhere in the
  codebase** — confirmed via `find`/`grep` across `Pena_e_Arte.Domain`, `.Application`, `.Infrastructure`:
  no `IntakeForm.cs`, no migration, no usage. `architecture.md`'s Feature Module Map row #02
  ("Consultation & Consent Forms") lists the same two entities, `IntakeForm` + `ConsentForm` — only
  `ConsentForm` was ever built. **This is a documented-but-never-implemented gap, not new scope this
  prompt is inventing** — flag it as closed once Part 1 ships, and note in the module-map row that
  `IntakeForm` is now real. Per this project's own sourcing rule: docs can lag reality; source is ground
  truth; the discrepancy is noted here rather than silently "fixed" without a trace.
- **Reference images today are one undifferentiated collection.** `AppointmentAttachment` has no
  category — every image in `BookAppointmentForm.tsx`'s `ReferenceImagesField` becomes an
  `AppointmentAttachment` row indistinguishable from any other. There is no "photo of the area" concept
  at all. Part 1b adds a `Category` discriminator; existing rows backfill to `Reference` (Part 1's
  migration), matching what they actually are today.
- **The existing presign endpoint (`POST /api/v1/files/presign`) cannot serve an anonymous guest.**
  `GetPresignedUploadUrlHandler` scopes the R2 key with `tenant.StudioId` from `ICurrentTenant`, which is
  populated from the JWT `tenant_id` claim — a guest has no JWT. `.RequireAuthorization("ClientAndAbove")`
  on the endpoint would reject them outright even if it didn't. **A parallel anonymous presign path is
  required** (Part 3f) — it must NOT simply drop the auth requirement on the existing one, because that
  endpoint accepts an arbitrary `ObjectKey` prefix and `application/pdf` (used by consent-form/design
  flows) — an unauthenticated caller must never get that latitude. The new endpoint is intentionally
  narrower: images only, one fixed key-prefix shape, one studio slug it's scoped to.
- **Existing rate-limit policies, verified in `RateLimitingExtensions.cs`**: `auth` (10/min/IP),
  `public-write` (30/min/IP), `public-read` (120/min/IP), `billing` (20/min/user). None of them is sized
  for an endpoint that creates an Identity user + DB rows + sends email, which is a materially heavier
  and more valuable-to-abuse action than a review post or a Redis view-counter increment. Part 4d adds a
  new `public-booking` policy rather than reusing `public-write` for the write endpoints.
- **Email verification is a non-blocking UI nudge today, not a server-side gate.** Confirmed by reading
  `CreateAppointmentCommand` end-to-end — there is no `IsEmailConfirmedAsync` check anywhere in the
  booking path. `BookPage.tsx`'s amber banner ("verify your email... before your booking is finalized")
  is UI copy only; nothing enforces it. This feature does not change that posture — the guest's account
  is fully capable of booking and being contacted before any verification happens, same as every existing
  authenticated client today.
- **`ArtistSchedule`/`ArtistTimeOff`/`StudioClosure`/slot-conflict logic already lives in
  `CheckSlotAvailabilityQuery` and inline in `CreateAppointmentCommand`, duplicated between the two.**
  Both need a public, slug-scoped equivalent for the guest flow. Rather than triplicating this logic,
  Part 3a extracts it into a shared `IAppDbContext` extension both the existing authenticated handlers
  and the two new public handlers call.
- **Known, pre-existing, out-of-scope bug, flagged not fixed**: `BookAppointmentForm.tsx` sends
  `depositRuleId` on every submit; `CreateAppointmentRequest` (backend contract) has no such field, and
  `CreateAppointmentHandler` always auto-selects "the single active `DepositRule`, if any" regardless of
  what the client picked. The frontend's deposit-rule `<Select>` is therefore decorative. This prompt's
  new guest deposit-preview (Part 3e) deliberately mirrors the **backend's actual behavior** ("the one
  active rule, if any" — no rule picker at all) rather than perpetuating the existing broken picker into
  a second surface. Do not fix the existing authenticated-form bug in this prompt — it's a separate,
  narrower change; a one-line note is left in Part 6d pointing at it for a future prompt.

---

## Decisions (already made with the product owner — do not re-litigate)

| # | Decision | Rationale |
|---|---|---|
| 1 | Ship true **guest checkout**: an unauthenticated visitor on a studio's public page books directly — no prior sign-up. An Identity user + `Client` are auto-provisioned server-side as part of the booking call. | Explicit product decision. Matches Fresha/Vagaro/Boulevard/GlossGenius (CLAUDE.md rule #6) — none of them force account creation before the first booking. |
| 2 | The guest is **not asked to set a password during booking.** The handler generates a random, policy-compliant password server-side, creates the Identity user with it, immediately discards it, then calls the existing `GeneratePasswordResetTokenAsync` (same primitive `ForgotPasswordHandler` already uses) and emails a "set your password to manage this booking" link built from the exact same `{baseUrl}/reset-password?email=...&token=...` shape `ForgotPasswordHandler` already builds. | Zero new auth infrastructure. A password field on a booking form is friction the industry benchmark set doesn't have; a magic-link/passwordless first session would be new infra this codebase has none of. Reusing the reset-password primitive is the smallest correct change. |
| 3 | **Duplicate-email handling**: before creating anything, call `identity.GetUserIdByEmailAsync(email)` (pre-existing, unchanged). If an account already exists **anywhere on the platform** (any studio — Identity users are platform-global), reject with a new `AccountAlreadyExistsException` → `409 Conflict`, message instructing the guest to log in first. Do **not** silently attach the booking to the existing account without authentication — that would let anyone book (and attach medical intake data) against a stranger's account by guessing their email. | Same threat model `RegisterUserHandler`'s owner-email check and the guest-QA-pass "owner takeover" fix (2026-07-02, cited in Decisions Log) already established for this codebase: never let an anonymous caller act on an existing account without proving control of it. |
| 4 | New `IntakeForm` entity (`TenantEntity`, 1:1 with `Appointment`) holds the booking-content fields: `TattooDescription` (required), `SafetyNotes` (optional), `DesiredPlacement` (`BodyMap` value object, reused byte-for-byte from `ClientProfile`), `ReferralSource` (enum) + `ReferralSourceOther`. Matches the shape `database.md` already documents but was never built (see Context). | Finishes a documented, never-implemented module rather than inventing parallel storage. Keeps per-visit intake separate from the client's permanent `ClientProfile.MedicalNotes`/`Allergies`/`BodyMap` (tattoo history) — conflating "what I want done at this booking" with "my medical history / where my existing tattoos are" would corrupt the latter's semantics. |
| 5 | `Client.MarketingOptIn` (new `bool`, default `false`) — **not** part of `IntakeForm`. | It's an account-level communication preference ("sign up for news and updates"), not booking content — same category as `Client.SmsOptOut`, which it's modeled directly after (see Part 1c). |
| 6 | **Two separate, both-required attachment groups** on every new booking (guest or authenticated): "Area photo" (photo of the body area) and "Reference images" (inspiration/style photos) — via a new `AppointmentAttachmentCategory` enum (`AreaPhoto`, `Reference`) on the existing `AppointmentAttachment` entity, not two new tables. | User's spec explicitly separates these; they serve different purposes for the artist (placement/skin condition vs. desired style) and conflating them into one undifferentiated grid (today's behavior) loses that. One entity + a discriminator column is the minimal shape — matches this codebase's existing `PortfolioImageCategory` precedent on `PortfolioImage`. |
| 7 | **"Desired placement" reuses the existing `BodyMap.tsx` component and `BodyMap` value object as-is** — a second `IntakeForm.DesiredPlacement` field of the identical value-object type, not a new picker. | `BodyMap.tsx` is already a controlled, generic zone-picker (`locations: string[]`, `onChange`) with no built-in assumption about *why* zones are selected — it's used today for tattoo history, but nothing about its API is history-specific. Reusing it here is the "use the existing primitive" convention this codebase follows everywhere else (`ToggleSwitch`, `PhoneInput`, `DataTable`, etc.). |
| 8 | **The new intake fields (tattoo description, dual images, desired placement, referral source, additional/safety notes) are added to BOTH the new guest flow AND the existing authenticated `BookAppointmentForm.tsx`.** Only the *identity* fields (name/email/phone/marketing opt-in) are guest-only — an already-authenticated client's name/email are already on file, and staff booking on behalf of an existing selected `Client` already have it too. | CLAUDE.md rule #6: a feature must hold for every path it touches, not just the one it was built for. The intake-quality fields are booking content, not identity — there's no reason a logged-in client's booking should carry less structured intake data than a guest's. |
| 9 | **Staff-side "new client" quick-add is explicitly OUT of scope for this prompt.** Staff booking on behalf of a first-time walk-in still must create the `Client` record via the existing Clients page first, then select them in `BookAppointmentForm.tsx`'s existing `clientId` dropdown — unchanged. | Keeps this prompt's blast radius to the guest path + shared intake fields. An inline "add new client while booking" affordance for staff is a real, valid gap (noted for a future prompt) but doubles the surface area of an already-large change for a workflow (staff picking up the phone for a walk-in) that has an existing, if less convenient, path today. Do not build it here. |
| 10 | **Anonymous presign is a brand-new, narrowly-scoped endpoint** (`POST /api/v1/public/studios/{slug}/booking/presign`), not a loosened version of the existing `/api/v1/files/presign`. Accepts only `image/jpeg`\|`image/png`\|`image/webp` (no `application/pdf`), and the object key is server-constructed as `appointments/guest-pending/{studioId}/{category}/{Guid}.{ext}` — the caller supplies only `Category` (`area`\|`reference`), never a free-text key/prefix. | The existing endpoint's free(-ish)-prefix + PDF support exists for authenticated design-revision/consent-form flows; giving an anonymous caller that same latitude turns a presign endpoint into an open write primitive to R2. Narrow-by-construction is cheaper to reason about than narrow-by-validation. |
| 11 | New `public-booking` rate-limit policy: **8 requests / 5 minutes, per IP**, applied to the booking-submit and presign endpoints only (the three new public *read* endpoints use the existing `public-read`, 120/min). | Booking-submit creates an Identity user, a `Client`, an `Appointment`, an `IntakeForm`, up to 12 R2-referencing attachment rows, and sends an email — an order of magnitude more expensive and more valuable to abuse (spam accounts, fake appointments consuming `AppointmentsPerMonth` plan quota, R2 storage cost via presign) than anything `public-write`'s 30/min was sized for (review text, a Redis counter). Presign shares the policy since it's a precondition for the same abuse. |
| 12 | **Orphaned guest-pending R2 objects get a new daily cleanup Hangfire job** (`GuestPendingUploadCleanupJob`) deleting anything under `appointments/guest-pending/**` older than 48h with no matching `AppointmentAttachment.ImageUrl`. The pre-existing authenticated `appointments/pending/**` prefix has the identical latent gap (no cleanup job exists for it today either) — **not fixed here**, flagged as a pre-existing, separate issue; only the new anonymous prefix gets a job in this prompt, because anonymous presign is new attack surface this prompt is introducing and must not leave unmonitored. | Anonymous presign (Decision #10) means anyone can generate write URLs and abandon uploads with zero account cost. The authenticated prefix's gap is real but predates this feature and isn't made worse by it — same "flag, don't silently fold in" posture `CLAUDE.md` rule #6 and this codebase's own Decisions Log entries repeatedly take with adjacent-but-out-of-scope findings. |
| 13 | **`StudioPortfolioPage.tsx`'s "Book" CTA changes from `/login?redirect=/book?studio={slug}` to `/book?studio={slug}` directly** for unauthenticated visitors. `/book` itself branches on auth state (Part 6). | The entire point of guest checkout is skipping the forced-login hop. Leaving the old CTA in place would ship a guest-booking backend nobody can reach from the actual entry point real visitors use. |
| 14 | **Email verification stays exactly as-is: a non-blocking nudge, unchanged for both guest and existing authenticated accounts.** No new verification gate is added anywhere in this prompt. | Out of scope; changing an existing, working (if soft) posture is a separate decision with its own blast radius. Noted in Context so it isn't mistaken for an oversight. |
| 15 | **`ReferralSource` is a fixed enum** (`Instagram`, `TikTok`, `YouTube`, `FriendsAndFamily`, `Other`) with a conditionally-required `ReferralSourceOther` free-text field, not a free-text field alone. | Matches the user's exact spec (fixed options + "somewhere else" free text) and gives the owner a queryable/aggregable field rather than unstructured text — consistent with this codebase's preference for enums over freeform strings wherever the option set is fixed (`AppointmentStatus`, `ReportCategory`, `ConsentTemplateKind`, etc.). |
| 16 | **The existing `depositRuleId` frontend/backend mismatch (Context, last bullet) is not fixed in this prompt.** The new guest deposit preview mirrors the backend's real behavior (single active rule, no picker) rather than the existing broken picker. | Keeps this prompt's scope bounded to guest checkout + intake. Flagged explicitly (Part 6d) so it isn't lost, but fixing a pre-existing, unrelated bug belongs in its own prompt per this project's own separation-of-concerns convention (see almost every Decisions Log entry's "flagged, not fixed here" pattern). |

---

## Part 1 — Domain + EF Core

### 1a. New file — `Pena_e_Arte.Domain/Entities/IntakeForm.cs`

```csharp
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.ValueObjects;

namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// Booking-content intake captured when an appointment is requested — what the client wants
/// done, where, reference material, and how they found the studio. One-to-one with
/// Appointment. Deliberately separate from ClientProfile (MedicalNotes/Allergies/BodyMap),
/// which is the client's permanent record across all bookings, not this visit's request.
/// Documented in database.md's DbContext example since before this entity existed; this is
/// the first real implementation — see architecture.md Context note, 2026-08-31.
/// </summary>
public class IntakeForm : TenantEntity
{
    public Guid AppointmentId { get; set; }

    /// <summary>"What are you looking to get done?" — required on every new booking.</summary>
    public string TattooDescription { get; set; } = string.Empty;

    /// <summary>"Anything else I should know?" — medical issues, allergies, antibiotics, skin
    /// conditions. Optional. Free text; NOT synced into ClientProfile.MedicalNotes/Allergies —
    /// see Decision #4.</summary>
    public string? SafetyNotes { get; set; }

    /// <summary>Reuses ClientProfile's exact value object/JSON-column pattern (Part 1d) —
    /// zone ids from the same BodyMap.tsx picker, but scoped to "where do you want THIS
    /// tattoo," not the client's tattoo history.</summary>
    public BodyMap DesiredPlacement { get; set; } = new();

    public ReferralSource? ReferralSource { get; set; }

    /// <summary>Required (validator-enforced) when ReferralSource == Other; ignored otherwise.</summary>
    public string? ReferralSourceOther { get; set; }

    public Appointment Appointment { get; set; } = null!;
}
```

### 1b. New file — `Pena_e_Arte.Domain/Enums/ReferralSource.cs`

```csharp
namespace Pena_e_Arte.Domain.Enums;

public enum ReferralSource
{
    Instagram,
    TikTok,
    YouTube,
    FriendsAndFamily,
    Other,
}
```

### 1c. New file — `Pena_e_Arte.Domain/Enums/AppointmentAttachmentCategory.cs`

```csharp
namespace Pena_e_Arte.Domain.Enums;

public enum AppointmentAttachmentCategory
{
    /// <summary>A straight-on photo of the body area to be tattooed.</summary>
    AreaPhoto,

    /// <summary>Style/placement/prior-work inspiration images. The only category that
    /// existed before this change — pre-existing rows backfill to this value (migration).</summary>
    Reference,
}
```

### 1d. `Pena_e_Arte.Domain/Entities/AppointmentAttachment.cs` — add one property

Add `public AppointmentAttachmentCategory Category { get; set; } = AppointmentAttachmentCategory.Reference;`
(default preserves current behavior for any code path that doesn't set it explicitly — there shouldn't be
any after Part 2/3, but the default is the correct backfill value regardless).

### 1e. `Pena_e_Arte.Domain/Entities/Client.cs` — add one property

```csharp
/// <summary>"Sign up for news and updates" — account-level marketing consent, captured at
/// guest checkout or in the manual Add Client form. Default false (opt-in, never opt-out-by-
/// default) — same posture as GDPR-conscious consent everywhere else in this codebase
/// (SmsOptOut exists for the opposite direction; this is the marketing-opt-IN analog).</summary>
public bool MarketingOptIn { get; set; }
```

Place it directly under `SmsOptOut` with a doc comment cross-referencing it, matching the existing
comment style on that property.

### 1f. `Pena_e_Arte.Infrastructure/Persistence/Configurations/IntakeFormConfiguration.cs` (new)

Mirror `ClientProfileConfiguration`'s exact `BodyMap` JSON-column pattern (`HasConversion` +
`SetValueComparer`) verbatim for `DesiredPlacement` — same value object, same serialization, no reason to
diverge. Additional configuration:

```csharp
builder.ToTable("intake_forms");
builder.Property(i => i.TattooDescription).IsRequired().HasMaxLength(4000);
builder.Property(i => i.SafetyNotes).HasMaxLength(4000);
builder.Property(i => i.ReferralSource).HasConversion<string>().HasMaxLength(32);
builder.Property(i => i.ReferralSourceOther).HasMaxLength(200);
builder.HasOne(i => i.Appointment)
       .WithOne()
       .HasForeignKey<IntakeForm>(i => i.AppointmentId)
       .HasConstraintName("fk_intake_forms_appointments")
       .OnDelete(DeleteBehavior.Cascade);
builder.HasIndex(i => i.AppointmentId).IsUnique().HasDatabaseName("ix_intake_forms_appointment_id");
```

`Appointment.cs` gains `public IntakeForm? Intake { get; set; }` (nullable — appointments created before
this migration have none; every new one from this prompt forward always has one, guest or authenticated).

### 1g. `AppointmentAttachmentConfiguration.cs` — add `Category` column

```csharp
builder.Property(a => a.Category).HasConversion<string>().HasMaxLength(16).HasDefaultValue(AppointmentAttachmentCategory.Reference);
```

### 1h. `ClientConfiguration.cs` — add `MarketingOptIn`

```csharp
builder.Property(c => c.MarketingOptIn).HasDefaultValue(false).IsRequired();
```

### 1i. `AppDbContext.cs` — add `DbSet<IntakeForm> IntakeForms => Set<IntakeForm>();` and its tenant query
filter (`builder.Entity<IntakeForm>().HasQueryFilter(i => i.StudioId == tenant.StudioId);`) — exactly the
shape `database.md` already documents.

### 1j. Migration

```
dotnet ef migrations add AddIntakeFormGuestBookingAndAttachmentCategory \
  --project Pena_e_Arte.Infrastructure --startup-project Pena_e_Arte.API
```

Must contain: new `intake_forms` table; new `category` column on `appointment_attachments`
(`DEFAULT 'Reference'`, then backfill any existing NULL/empty to `'Reference'` explicitly in the
migration's `Up()` even though the default covers new rows — existing rows written before the column
existed need the same explicit backfill statement, same "add nullable/defaulted → backfill existing"
shape `database.md`'s zero-downtime migration order describes); new `marketing_opt_in` column on `clients`
(`DEFAULT false`). Single migration is fine — all three changes ship together tonight, same precedent as
`AddPlanPriceAndSubscriptionBillingInterval` bundling multiple related changes. Review the generated
migration before applying; MySQL 8.4 + Pomelo sometimes needs an explicit `oldClrType`/column type on the
`category` addition — verify by actually running `dotnet ef database update` locally, not by reading the
generated file alone.

---

## Part 2 — Contracts

### 2a. `Pena_e_Arte.Contracts/Requests/CreateAppointmentRequest.cs` — extend

```csharp
public record CreateAppointmentRequest(
    Guid? ArtistId,
    Guid ClientId,
    DateTime Date,
    int DurationMinutes,
    string? Notes,
    string TattooDescription,
    string? SafetyNotes,
    IReadOnlyList<string>? DesiredPlacementLocations,
    string? ReferralSource,          // enum name as string, nullable — "Other" requires ReferralSourceOther
    string? ReferralSourceOther,
    IReadOnlyList<AppointmentImageRequest>? Images = null);   // replaces ImageUrls

public record AppointmentImageRequest(string Url, string Category);   // Category: "AreaPhoto" | "Reference"
```

`Notes` is kept (existing field, still used for anything that doesn't fit the structured fields — the
placeholder copy on the frontend's `Notes` textarea changes from "Style, size, placement, skin
concerns…" to something narrower like "Anything else for the studio?" since style/placement/skin
concerns now have their own dedicated fields). `ImageUrls` (old, flat `string[]`) is removed —
`BookAppointmentForm.tsx`'s existing single-collection upload becomes two collections (Part 6d); there is
exactly one caller of the old shape and it's rewritten in this same prompt, so no back-compat shim is
needed.

### 2b. New file — `Pena_e_Arte.Contracts/Requests/CreateGuestAppointmentRequest.cs`

```csharp
namespace Pena_e_Arte.Contracts.Requests;

public record CreateGuestAppointmentRequest(
    string FirstName,
    string LastName,
    string Email,
    string Phone,               // E.164, from PhoneInput — same shape CreateClientRequest already requires
    bool MarketingOptIn,
    CreateAppointmentRequest Booking);   // reuses every booking-content field from 2a; ClientId on
                                          // the nested record is ignored/unused for this path
```

Nesting the existing `CreateAppointmentRequest` avoids duplicating every booking field a second time;
the handler (Part 3b) ignores `Booking.ClientId` entirely and resolves the real one itself.

### 2c. New file — `Pena_e_Arte.Contracts/Requests/PresignGuestUploadRequest.cs`

```csharp
namespace Pena_e_Arte.Contracts.Requests;

public record PresignGuestUploadRequest(string ContentType, string Category); // Category: "area" | "reference"
```

Reuses the existing `Pena_e_Arte.Contracts.Responses.PresignUploadResponse(string UploadUrl, string PublicUrl)`
unchanged.

### 2d. New file — `Pena_e_Arte.Contracts/Responses/Public/PublicBookingArtistResponse.cs`

```csharp
namespace Pena_e_Arte.Contracts.Responses.Public;

public record PublicBookingArtistResponse(
    Guid ArtistId,
    string Name,
    string? AvatarUrl,
    string? Specializations,
    decimal? HourlyRate);   // needed client-side for the deposit-percent preview; see Decision re: rule below
```

Deliberately a new, purpose-built response rather than reusing/extending `PublicArtistSummary` — that
type is already returned on `StudioPortfolioPage`'s public feed and is covered by existing tests/consumers;
exposing `HourlyRate` there would be an unrelated, unreviewed change to a different surface. Confirm
`HourlyRate` isn't itself sensitive data the product owner wants withheld from anonymous callers before
shipping this row — it's already computed into `AppointmentResponse.depositAmount` today for
authenticated bookings, so the underlying number reaches the client either way; this just makes the
*estimate* visible pre-submit, matching what `DepositPreview` already shows authenticated users.

### 2e. New file — `Pena_e_Arte.Contracts/Responses/Public/PublicDepositRuleResponse.cs`

```csharp
namespace Pena_e_Arte.Contracts.Responses.Public;

public record PublicDepositRuleResponse(string Name, decimal? AmountFixed, decimal? AmountPercent);
```

Nullable return (the query returns `null` when the studio has no active rule) — mirrors backend's own
"single active rule, if any" behavior (Context, last bullet), not a list.

### 2f. `AppointmentResponse.cs` — extend with the intake projection

```csharp
public record AppointmentResponse(
    /* ...existing fields, unchanged... */
    string? TattooDescription,
    string? SafetyNotes,
    IReadOnlyList<string>? DesiredPlacementLocations,
    string? ReferralSource,
    string? ReferralSourceOther,
    IReadOnlyList<AppointmentAttachmentResponse>? Attachments);  // replaces the old flat imageUrls: string[]

public record AppointmentAttachmentResponse(string Url, string Category);
```

`imageUrls` consumers (`AppointmentDetailPage.tsx`, `AppointmentCard.tsx`) are updated in Part 6 to render
the two categories separately (area photo shown near the intake description; reference images in their
own gallery — matches how an artist actually wants to read this: "here's the spot, here's the vibe").

---

## Part 3 — Application layer

### 3a. New file — `Pena_e_Arte.Application/Common/SlotAvailabilityExtensions.cs`

Extract the shared logic `CheckSlotAvailabilityHandler` and the inline block in
`CreateAppointmentHandler` both currently duplicate (studio-closure check → per-artist schedule check →
time-off check → conflict check; plus the any-artist variant via the existing `IsAnyArtistAvailableAsync`,
which stays where it is — only the specific-artist chain needs extracting, since that's the one
duplicated). Signature:

```csharp
public static class SlotAvailabilityExtensions
{
    public static async Task<(bool Available, string? Reason)> CheckArtistSlotAvailabilityAsync(
        this IAppDbContext db, Guid studioId, Guid artistId, DateTime date, int durationMinutes,
        CancellationToken ct)
    // body = CheckSlotAvailabilityHandler's existing specific-artist chain, unchanged logic,
    // just parameterized by studioId explicitly instead of relying on ICurrentTenant implicitly
    // via the query filter (so it also works IgnoreQueryFilters()'d from a public/anonymous caller —
    // add an explicit `.Where(x => x.StudioId == studioId)` predicate to every query in this method,
    // since a public caller's IAppDbContext has no ambient tenant scope to rely on).
}
```

Update `CheckSlotAvailabilityHandler` and `CreateAppointmentHandler` to call this instead of their
inline/duplicated versions. **Behavior must not change for either existing caller** — this is a pure
extraction; write/run the existing `CheckSlotAvailability`/`CreateAppointment` test suites before and
after to confirm identical results.

### 3b. `CreateAppointmentCommand.cs` — extract `CreateAppointmentCore`

Refactor `CreateAppointmentHandler.Handle` into two parts: keep the existing method for the authenticated
`clientId`-resolution branch (JWT-role check, `FindClientForUserAsync`, or `req.ClientId` from a staff
caller), then delegate everything from `DateTime requestEnd = ...` onward (artist validation, slot
lock, conflict check, deposit calc, `Appointment` construction, `IntakeForm` construction (new — see
below), attachment mapping by category, save, reminder scheduling, realtime notify, created-notification
send) into a new internal static-ish method:

```csharp
internal static async Task<AppointmentResponse> CreateAppointmentCoreAsync(
    IAppDbContext db, ICurrentTenant tenant, ISlotLocker slotLocker, IJobScheduler jobs,
    IRealtimeNotifier realtime, ISender sender, Guid studioId, Guid clientId,
    CreateAppointmentRequest req, CancellationToken ct)
```

placed in the same file (or a new `Pena_e_Arte.Application/Appointments/Commands/CreateAppointmentCore.cs`
if that reads cleaner — implementer's call, keep it private/internal either way, not part of the public
Application API surface). Both `CreateAppointmentHandler` (existing, authenticated) and the new
`CreateGuestAppointmentHandler` (3c) call this core after resolving their own `clientId`. Add the new
`IntakeForm` construction here (once, shared):

```csharp
appointment.Intake = new IntakeForm
{
    StudioId = tenant.StudioId, // or the passed-in studioId param for the guest path — see note below
    TattooDescription = req.TattooDescription,
    SafetyNotes = req.SafetyNotes,
    DesiredPlacement = new BodyMap { Locations = req.DesiredPlacementLocations?.ToList() ?? [] },
    ReferralSource = req.ReferralSource is null ? null : Enum.Parse<ReferralSource>(req.ReferralSource),
    ReferralSourceOther = req.ReferralSourceOther,
};
```

and split `req.Images` by `Category` into `AppointmentAttachment` rows (replacing the old flat
`req.ImageUrls` loop) tagged with the matching `AppointmentAttachmentCategory`.

**Important**: `CreateAppointmentCoreAsync` must take `studioId` as an explicit parameter rather than
resolving it from `ICurrentTenant` internally — the guest path has no ambient tenant scope (no JWT), so
`ICurrentTenant` is unpopulated for it. The existing authenticated caller passes `tenant.StudioId` as
before; the new guest handler passes the slug-resolved `Studio.Id`. This is the same "explicit studioId
parameter instead of `ICurrentTenant`" shape `GetPublicStudioQuery`/`GetPublicArtistQuery` already use for
every other public handler — do not special-case this one.

### 3c. New file — `Pena_e_Arte.Application/Public/Commands/CreateGuestAppointmentCommand.cs`

```csharp
public record CreateGuestAppointmentCommand(string StudioSlug, CreateGuestAppointmentRequest Request)
    : IRequest<AppointmentResponse>, IQuotaCheckedCommand
{
    public QuotaType QuotaType => QuotaType.AppointmentsPerMonth;   // guest bookings count toward the
                                                                     // same plan quota as any other —
                                                                     // do not give guests an unmetered path
}
```

Handler responsibilities, in order:

1. Resolve `Studio` by slug, `IgnoreQueryFilters()`, `IsActive && IsPublished` — identical predicate to
   `GetPublicStudioHandler`. 404 (via `NotFoundException`) if absent.
2. `Guid? existingUserId = await identity.GetUserIdByEmailAsync(req.Email);` — if non-null, throw the new
   `AccountAlreadyExistsException` (Decision #3). Map it to `409 Conflict` in the global exception
   middleware/filter (check `Pena_e_Arte.API`'s existing exception-to-status mapping — mirror how
   `SlotAlreadyBookedException` is already mapped to 409, same file).
3. Client-side lookup/link, same shape as `RegisterUserHandler` (approved exception #28 — add this
   handler to that same table row rather than a new numbered entry, since it's the identical pattern
   applied at a second call site — or a new row if the reviewer prefers one row per handler; match
   whichever the table's existing convention leans toward on inspection. **Verify against the live table
   before deciding** — don't guess).
4. Generate a random password (≥24 chars, mixed case/digit/symbol — check `Program.cs`/Identity options
   for the configured `PasswordOptions` and satisfy them with margin; do not hardcode assumptions).
   `identity.CreateUserAsync(req.Email, randomPassword, "client", studio.Id, req.FirstName)`. Discard the
   password immediately — never log it (CLAUDE.md rule #3), never return it in any response.
5. Create the `Client` row (`FirstName`, `LastName`, `Email`, `Phone`, `MarketingOptIn` all populated from
   the request — unlike `RegisterUserHandler`'s existing flow, which leaves `LastName` empty and `Phone`
   null; this is the one behavioral difference from the reused pattern, intentional per the user's spec).
6. Call `CreateAppointmentCoreAsync` (3b) with `studio.Id` and the new client's id.
7. Generate both a password-reset token (`GeneratePasswordResetTokenAsync`) and an email-confirmation
   token (`GenerateEmailConfirmationTokenAsync`) — send ONE combined email (new `IEmailRenderer` method,
   Part 3g) rather than two separate ones. Wrap in `try/catch` + `LogWarning` on failure, matching
   `RegisterUserHandler` exactly — booking success must never depend on email delivery succeeding.
8. Return the same `AppointmentResponse` `CreateAppointmentCoreAsync` produces.

**Transactional integrity**: wrap steps 4–6 in an explicit `IDbContextTransaction` (check
`IAppDbContext`/`AppDbContext` for the existing transaction-wrapping convention used elsewhere — e.g. any
multi-`SaveChangesAsync` handler already in this codebase — and match it) so a failure partway through
(e.g. Identity user created but `SaveChangesAsync` for the `Client`/`Appointment` throws) doesn't leave an
orphaned Identity user with no linked `Client`. If `IIdentityService.CreateUserAsync` isn't itself
transactional with `IAppDbContext` (likely true — Identity has its own store), accept that a true
all-or-nothing guarantee across both stores isn't achievable without deeper Identity-store changes (out
of scope); at minimum wrap the DB-only steps (`Client`+`Appointment`+`IntakeForm`+attachments) in one
transaction, and if that fails after the Identity user was already created, log an error with enough
detail (studio id, email — **not** raw PII beyond what's already permitted per CLAUDE.md rule #3, i.e.
log identifiers, not full name/phone) for manual cleanup. Note this residual risk in the PR description;
don't silently claim full atomicity if it isn't achievable.

### 3d. New file — `Pena_e_Arte.Domain/Exceptions/AccountAlreadyExistsException.cs`

Mirror the existing exception classes' shape (`SlotAlreadyBookedException`,
`ConsentFormAlreadySignedException`) — simple, message-carrying, mapped to `409` in the same central
place those are.

### 3e. New file — `Pena_e_Arte.Application/Public/Queries/GetPublicBookingArtistsQuery.cs`

`record GetPublicBookingArtistsQuery(string StudioSlug) : IRequest<IReadOnlyList<PublicBookingArtistResponse>>`.
Resolve studio by slug (same predicate as 3c step 1), then `db.Artists.IgnoreQueryFilters().Where(a =>
a.StudioId == studio.Id && a.DeletedAt == null)` → project into `PublicBookingArtistResponse`. Anonymous,
`public-read`.

### 3f. New file — `Pena_e_Arte.Application/Public/Queries/CheckPublicSlotAvailabilityQuery.cs`

`record CheckPublicSlotAvailabilityQuery(string StudioSlug, Guid? ArtistId, DateTime Date, int
DurationMinutes) : IRequest<SlotAvailabilityResult>` (reuse the existing `SlotAvailabilityResult` record
from `CheckSlotAvailabilityQuery.cs` rather than duplicating it). Resolve studio by slug, then call
`db.IsAnyArtistAvailableAsync(...)` (existing, already public-safe — no query-filter dependency, verify)
for the any-artist case, or `db.CheckArtistSlotAvailabilityAsync(studio.Id, artistId, ...)` (3a) for the
specific-artist case — same branching `CheckSlotAvailabilityHandler` already does, just slug-scoped.
Anonymous, `public-read`.

### 3g. New file — `Pena_e_Arte.Application/Public/Queries/GetPublicDepositRuleQuery.cs`

`record GetPublicDepositRuleQuery(string StudioSlug) : IRequest<PublicDepositRuleResponse?>`. Resolve
studio by slug, then the exact same "single active rule, ordered by `UpdatedAt` desc" query
`CreateAppointmentHandler` already runs, `IgnoreQueryFilters()`'d and scoped by `studio.Id` explicitly.
Anonymous, `public-read`.

### 3h. New file — `Pena_e_Arte.Application/Public/Queries/GetPresignedGuestUploadUrlQuery.cs` +
`GetPresignedGuestUploadUrlValidator.cs`

```csharp
public record GetPresignedGuestUploadUrlQuery(string StudioSlug, PresignGuestUploadRequest Request)
    : IRequest<PresignUploadResponse>;
```

Handler: resolve studio by slug (404 if not found — do not leak which slugs exist vs. don't beyond what
`GetPublicStudioQuery` already leaks, i.e. no new information disclosure). Validate `Category` is
`"area"` or `"reference"` (validator, `RuleFor(x => x.Request.Category).Must(c => c is "area" or
"reference")`). Content type restricted to the three image types only (no `application/pdf` — Decision
#10) — same `ExtensionsByContentType` dictionary shape as the existing handler, image-only subset.
Construct the key as `appointments/guest-pending/{studio.Id}/{category}/{Guid.NewGuid():N}.{ext}` —
**never** accept a client-supplied prefix/objectKey at all (unlike the existing endpoint's "trust the
folder prefix, generate the filename" split — here the *entire* key is server-constructed; the request
carries no key material whatsoever). Call `IR2Service.GeneratePresignedUploadUrlAsync` unchanged.

### 3i. `CreateAppointmentValidator.cs` — extend for the new shared fields

Add: `RuleFor(x => x.Request.TattooDescription).NotEmpty().MaximumLength(2000);`,
`RuleFor(x => x.Request.SafetyNotes).MaximumLength(2000).When(...)`,
`RuleFor(x => x.Request.DesiredPlacementLocations)` — each id must be a known `BodyMap.tsx` zone id
(reuse/duplicate the same id list the frontend's `ALL_BODY_ZONES` exports, or accept any string ≤64 chars
and rely on the frontend being the only writer — **prefer validating against the known zone-id set**,
mirroring how this codebase generally validates enums/closed sets server-side rather than trusting the
client; check whether `ALL_BODY_ZONES`' ids are already mirrored anywhere backend-side for
`ClientProfile.BodyMap` — if `UpsertClientProfileValidator` doesn't validate zone ids either, match that
existing (lenient) precedent instead of introducing an inconsistency, and note the gap either way).
`RuleFor(x => x.Request.ReferralSourceOther).NotEmpty().When(x => x.Request.ReferralSource ==
"Other").WithMessage(...)`. Extend `Images`' validation (was `ImageUrls`) to also constrain `Category` to
the two known values and cap **each category independently** at `MaxImageUrls` (6) — a guest shouldn't be
able to put 12 images all in one category if the UI is meant to show two capped-at-6 groups; confirm this
matches Part 6's frontend cap before finalizing the number.

### 3j. New file — `Pena_e_Arte.Application/Public/Validators/CreateGuestAppointmentValidator.cs`

`FirstName`/`LastName` `NotEmpty().MaximumLength(100)`; `Email` `NotEmpty().EmailAddress()`; `Phone`
`Matches(E164Format)` — reuse the exact `^\+[1-9]\d{1,14}$` regex `CreateClientValidator` already has
(Decisions Log, phone-input entry) rather than re-deriving it; `Booking` validated via a nested
`SetValidator(new CreateAppointmentValidator(r2))` (FluentValidation's standard nested-object pattern —
confirm this codebase already uses `SetValidator` elsewhere for nested request objects, or whether
validators are composed differently; match existing convention).

---

## Part 4 — API Endpoints + governance tables

### 4a. `Pena_e_Arte.API/Endpoints/PublicEndpoints.cs` — add to `MapPublicEndpoints`

```csharp
group.MapPost("/studios/{slug}/book", CreateGuestAppointment)
     .AllowAnonymous().RequireRateLimiting("public-booking");
group.MapGet("/studios/{slug}/booking/artists", GetPublicBookingArtists)
     .AllowAnonymous().RequireRateLimiting("public-read");
group.MapGet("/studios/{slug}/booking/availability", CheckPublicSlotAvailability)
     .AllowAnonymous().RequireRateLimiting("public-read");
group.MapGet("/studios/{slug}/booking/deposit-rule", GetPublicDepositRule)
     .AllowAnonymous().RequireRateLimiting("public-read");
group.MapPost("/studios/{slug}/booking/presign", GetPresignedGuestUploadUrl)
     .AllowAnonymous().RequireRateLimiting("public-booking");
```

Follow the file's existing handler-method shape exactly (thin `mediator.Send` wrappers, `Results.Ok`/
`Results.NotFound`, query-string/route-param binding as plain method parameters — no new pattern).
`CheckPublicSlotAvailability`'s `date`/`durationMinutes`/`artistId` bind as query-string params, same
shape the authenticated `GET .../check-slot` endpoint already uses (find and mirror it exactly —
`AppointmentEndpoints.cs`).

### 4b. `docs/claude/architecture.md` — `AllowAnonymous Exceptions` table: add 5 rows

| Endpoint | Reason | Security mechanism |
|---|---|---|
| `POST /api/v1/public/studios/{slug}/book` | Guest checkout — books without a prior account | Rate-limited (`public-booking`, 8/5min/IP); duplicate-email rejected via `GetUserIdByEmailAsync` (409); random server-generated password, never returned/logged; plan quota (`AppointmentsPerMonth`) still enforced |
| `GET /api/v1/public/studios/{slug}/booking/artists` | Public artist list for the guest booking picker | None — read-only, non-sensitive (name/avatar/specializations/hourly rate) |
| `GET /api/v1/public/studios/{slug}/booking/availability` | Public slot-availability check for guest booking | None — read-only boolean+reason, no PII |
| `GET /api/v1/public/studios/{slug}/booking/deposit-rule` | Public deposit estimate for guest booking | None — read-only, single active rule's name/amounts only |
| `POST /api/v1/public/studios/{slug}/booking/presign` | Anonymous image upload before a guest's account exists | Rate-limited (`public-booking`); image content-types only, no PDF; entire R2 key server-constructed, no client-supplied path component; orphan-cleanup job (Part 5a) |

(Exact wording is a starting point — match the table's existing terse style, don't pad it.)

### 4c. `docs/claude/architecture.md` — `IgnoreQueryFilters() Approved Usages` table: add rows,
continuing the numbering from the current last entry (#45 as of this prompt's Context read — **re-check
the live table's actual last number before writing new rows**, it may have moved) for:
`CreateGuestAppointmentHandler` (Studio + duplicate `Client` lookup, same shape as #28),
`GetPublicBookingArtistsHandler`, `CheckPublicSlotAvailabilityHandler`, `GetPublicDepositRuleHandler`,
`GetPresignedGuestUploadUrlHandler` — each row: location, purpose, "Anonymous" caller column, matching
every other public-handler row's exact phrasing style.

### 4d. `Pena_e_Arte.API/Extensions/RateLimitingExtensions.cs` — add the new policy

```csharp
AddRedisPolicy(opt, db, logger, "public-booking", permitLimit: 8, window: TimeSpan.FromMinutes(5));
```

Add it to the same `PostConfigure` block, directly after `public-write`, and add a line to the existing
comment table at the top of the method documenting it (`public-booking | 8 | 5 min ← guest booking
submit + presign`), matching the file's existing self-documenting-comment convention exactly.

---

## Part 5 — Cross-feature work

### 5a. New file — `Pena_e_Arte.Infrastructure/Jobs/GuestPendingUploadCleanupJob.cs`

Daily Hangfire job (register alongside the existing recurring jobs — find where `AppointmentReminderJob`/
`TrafficRollupJob` etc. are scheduled in `Program.cs`/startup and match that exact registration shape).
Lists R2 objects under `appointments/guest-pending/` (check `IR2Service` for a list/enumerate capability —
if none exists, this is new surface on that interface; if R2/S3-compatible listing isn't already wrapped,
add the minimal `ListObjectsAsync(prefix)` method needed, don't over-build it), deletes any object older
than 48h whose key is not referenced by any `AppointmentAttachment.ImageUrl` (`IgnoreQueryFilters()` —
Hangfire job, no request scope, same class as `AppointmentReminderJob` etc., approved exception #36 — add
a row for this one too, or fold it into #36's existing multi-job row if the table's convention groups
same-shape jobs together; check which). Log a summary count (objects scanned/deleted), no PII.

### 5b. `frontend/src/features/public/components/StudioPortfolioPage.tsx` — CTA change

Find the "Book" CTA's `href`/`to` logic (currently `/login?redirect=/book?studio={slug}` for an
unauthenticated visitor, confirmed in architecture.md's own documented behavior for this component).
Change to `/book?studio={slug}` directly for every visitor, authenticated or not — `/book` itself now
branches (Part 6a).

### 5c. `frontend/src/features/appointments/components/BookPage.tsx` — auth branch

- Authenticated (`user != null`): unchanged — existing `BookAppointmentForm` + `MyBookingsSection`,
  verification banner untouched.
- Unauthenticated + `?studio={slug}` present: render the new `GuestBookAppointmentForm` (Part 6b). No
  verification banner (nothing to verify yet), no `MyBookingsSection` (nothing to show pre-account).
- Unauthenticated + no `studio` param: reuse `BookAppointmentForm.tsx`'s existing "you haven't joined a
  studio yet — browse studios" empty state verbatim (it's already the correct message for this case; just
  render it from `BookPage` directly instead of only reachable via the authenticated component).

---

## Part 6 — Frontend

### 6a. Extract 3 shared sub-components out of `BookAppointmentForm.tsx`

Both the existing authenticated form and the new guest form need identical intake UI (Decision #8) — do
not duplicate the JSX.

- **`components/TattooIntakeFields.tsx`** — tattoo-description textarea (required), referral-source
  `Select` (Instagram/TikTok/YouTube/FriendsAndFamily/Other, with a conditional text input when "Other"
  is selected — same conditional-field pattern `AreaAndReferenceImagesField`'s category grouping and
  existing conditional fields elsewhere in this file already use), additional-notes textarea (optional,
  replaces the narrowed `Notes` field's placeholder copy per Context). Controlled via `react-hook-form`
  `Controller`/`register`, same as every other field in the source file — no new form-state pattern.
- **`components/CategorizedImagesField.tsx`** — generalize the existing `ReferenceImagesField` (currently
  hardcoded to one collection) into a reusable component taking `category: "AreaPhoto" | "Reference"`,
  `label`, `helperText`, `required: boolean`, `max: number`, plus the same `images`/`error`/`onPick`/
  `onRemove`/`disabled` props the original already has. `BookAppointmentForm.tsx` renders two instances
  (Area, Reference); so does the new guest form. Preserve every existing behavior (upload-in-progress
  spinner, error tile, remove button, `MAX_REFERENCE_IMAGES` cap — now per-category) byte-for-byte; this
  is a rename+parameterize, not a rewrite.
- **`components/DesiredPlacementField.tsx`** — thin wrapper around the existing, unmodified
  `features/clients/components/BodyMap.tsx` (`locations`/`onChange`), with the field label/required-marker
  chrome this file's other fields use (`FieldLabel`).

### 6b. New file — `frontend/src/features/booking/components/GuestBookAppointmentForm.tsx`

New top-to-bottom form for the unauthenticated path. Fields, in the order the user's spec lists them:
First/Last Name, Email, "Sign up for news and updates" (optional checkbox), Phone (`PhoneInput`,
reused as-is), `TattooIntakeFields`'s description field, Preferred Dates (artist picker sourced from
`GET .../booking/artists` + "let the studio choose" toggle — same UX as the existing form's artist
selector, reimplemented against the new public query hooks rather than the authenticated ones; datetime +
duration `Select` + debounced `GET .../booking/availability` check — same debounce/UX pattern as the
existing form's `SlotAvailabilityIndicator`, reused component as-is against the new public hook), two
`CategorizedImagesField` instances (Area required, Reference required — per Decision #6, **both**
required for this new flow; confirm with the product owner whether the existing authenticated form's
images should also flip from optional to required now that they're split into a required "area photo" —
default to **not** changing the existing form's existing-users expectations without an explicit go-ahead;
leave Area/Reference both optional there unless told otherwise, flag the inconsistency in the PR
description rather than silently deciding either way), `DesiredPlacementField`, referral source + "how
did you hear about us," `TattooIntakeFields`'s notes field, deposit preview (via
`GET .../booking/deposit-rule` + the selected artist's `HourlyRate` from the public artists list — same
calculation `DepositPreview` already does, reused verbatim against the new data source). Submit → new
`useCreateGuestAppointmentMutation`. Success screen: booking-requested confirmation + "Check your email to
set up your account and manage this booking — you can also use 'Forgot password' any time with this email
address" (Decision #2's built-in recovery path — no bespoke "resend" feature needed, say so explicitly in
the UI copy since it's the guest's actual safety net if the confirmation email is delayed/lost).

### 6c. New file — `frontend/src/features/public/publicBookingApi.ts` (or extend the existing `publicApi.ts`
if that's a better fit on inspection — match whichever this codebase's existing RTK Query slice
boundaries suggest) — 5 endpoints against the new public routes, unauthenticated `baseQuery` (same
pattern `useGetPublicStudioQuery` already uses — no JWT attach logic needed/wanted here).

### 6d. `BookAppointmentForm.tsx` — add the shared intake fields

Render `TattooIntakeFields`, two `CategorizedImagesField`s (replacing the single `ReferenceImagesField`),
and `DesiredPlacementField` inside the existing authenticated form, in the same relative position the
current `Notes`/`ReferenceImagesField` occupy. Update the submit payload to the new
`CreateAppointmentRequest` shape (2a). **Leave the existing, pre-existing `depositRuleId` `<Select>` and
its payload field exactly as they are** — do not attempt to fix the frontend/backend mismatch documented
in Context/Decision #16 as part of this change; a one-line `// TODO` comment pointing at this prompt's
date is enough of a marker, not a fix.

### 6e. `AppointmentCard.tsx` / `AppointmentDetailPage.tsx` — render the new fields

Tattoo description near the top (it's the headline "what is this appointment for" info an artist/owner
actually wants first); Area photo shown adjacent to it; Reference images in their own gallery section
(reuse whatever lightbox/gallery pattern `AppointmentDetailPage.tsx` already has for the old flat image
list, just fed from the `Reference`-category subset); referral source + safety notes shown lower, in a
clearly-labeled "Intake" section — safety notes in particular should be visually distinct (not buried in
generic notes) since it's the field artists most need to not miss.

### 6f. `frontend/src/features/clients/components/CreateClientPage.tsx` — add `MarketingOptIn` checkbox

Minor addition to the existing manual Add Client form, for parity — an owner/staff manually adding a
client should be able to record the same consent a guest sets themselves. Not required by the user's
spec directly, but leaving the field write-only-via-guest-checkout while every other `Client` field is
staff-editable would be an inconsistent, easily-missed gap; low cost to include here.

### 6g. Explicitly NOT changed: `ClientRegisterPage.tsx`

Per Decision #1 (guest checkout, not "enrich sign-up"), the existing sign-up screen is untouched. Do not
add `LastName`/`Phone`/marketing fields there as part of this prompt — that was the alternative option
that was not chosen; adding it anyway would be scope creep past what was decided.

---

## Part 7 — Tests

### Backend (mirror `conventions.md`'s `MethodName_Scenario_ExpectedResult` naming throughout)

- `CreateGuestAppointmentHandlerTests`: happy path (new account+client+appointment+intake created,
  correct email sent); duplicate email → `AccountAlreadyExistsException`; studio not found/unpublished →
  `NotFoundException`; quota exceeded → existing `PlanLimitBehavior` still fires (guest path goes through
  the same `IQuotaCheckedCommand` pipeline — verify, don't assume, that MediatR pipeline behaviors apply
  identically to a command dispatched from a `Public` handler as from an authenticated one); slot conflict
  → `SlotAlreadyBookedException` (via the shared core); random password never appears in any log output
  (assert on a captured `ILogger`, not just "we didn't call `LogInformation` with it" — check every log
  call added in this handler).
- `SlotAvailabilityExtensionsTests` (or fold into existing `CheckSlotAvailabilityHandlerTests` +
  `CreateAppointmentHandlerTests` if the extraction is behavior-preserving and doesn't need new coverage
  beyond what already exists — confirm via the "before/after" comparison Part 3a calls for).
- `GetPublicBookingArtistsHandlerTests`, `CheckPublicSlotAvailabilityHandlerTests`,
  `GetPublicDepositRuleHandlerTests`, `GetPresignedGuestUploadUrlHandlerTests`: each — happy path,
  studio-not-found path, and (for the presign one) rejected content-type + rejected category.
- `CreateAppointmentValidatorTests`, `CreateGuestAppointmentValidatorTests`: every new rule from Parts
  3i/3j, including the `ReferralSourceOther`-required-when-`Other` conditional and the per-category image
  cap.
- Integration tests (per `overnight-prompt`/`architecture.md`'s established convention — real MySQL via
  the test project's existing fixture, external services NSubstitute-mocked): full guest-booking flow
  end-to-end through the API layer, confirming the created `Client.MarketingOptIn`/`Phone`/`LastName`
  persist correctly and the `IntakeForm` row is linked 1:1.
- `GuestPendingUploadCleanupJobTests`: deletes only unreferenced, >48h-old objects; leaves referenced and
  fresh ones alone.

### Frontend

- `GuestBookAppointmentForm.test.tsx`: renders all fields in spec order; required-field validation;
  submit happy path; duplicate-email 409 shows the "log in instead" messaging; both image categories
  independently required and independently capped.
- `TattooIntakeFields.test.tsx`, `CategorizedImagesField.test.tsx`, `DesiredPlacementField.test.tsx`
  (extracted components — new, focused unit tests replacing whatever coverage of the old inline JSX
  existed in `BookAppointmentForm.test.tsx`, moved rather than duplicated).
- `BookAppointmentForm.test.tsx`: update for the new fields/payload shape; confirm existing tests
  (artist selection, deposit preview, slot availability) still pass unmodified in behavior.
- `BookPage.test.tsx`: new cases for the unauthenticated+slug and unauthenticated+no-slug branches (5c).
- `publicBookingApi` tests matching this codebase's existing RTK-Query-slice test convention (check
  whether other `*Api.ts` files have dedicated tests or rely on component-level coverage — match
  whichever is the norm).

---

## Part 8 — Help Menu, user manual, onboarding tour (CLAUDE.md rule #7 — not optional)

### 8a. `frontend/src/features/help/helpContent.ts`

- Update the existing `client-book-appointment` article: add a step/note that booking no longer requires
  an account first — a visitor can book directly from a studio's page, and an account is created for them
  automatically (mention the password-setup email). Add `"guest"`, `"no account"`, `"sign up"` to its
  `keywords`.
- New article, e.g. `id: "guest-booking-account-setup"`: explains what happens after a guest books — check
  email, set a password via the link (or "Forgot password" any time), how to see the booking afterward
  (log in). `route`: `/book` (same route the guest lands on — Help content is route-scoped per this file's
  existing convention, confirm and match it). Link it via `relatedArticleIds` both directions with
  `client-book-appointment`.

### 8b. `frontend/public/user-manual/index.html`

Matching update to whatever section documents booking today — add the guest-checkout path, the dual
image categories, referral source, and the desired-placement body map, mirroring `helpContent.ts`'s new
copy (the two surfaces should say the same thing in the same place per this project's own "keep Help in
sync" rule — don't let them drift).

### 8c. `frontend/src/features/help/tours/clientTour.ts`

Check for any existing `data-tour="..."` target inside `BookAppointmentForm.tsx`/`BookPage.tsx` — if the
tour references the old single reference-image field or the old `Notes` field by a tour-step selector
that no longer exists after Part 6, update the step to the new element. If no tour step touches this
specific UI (plausible — the tour runs for already-authenticated users, and guest checkout by definition
happens before the tour would ever run), state that explicitly and explain why no change is needed
(matches this codebase's own "confirmed no change needed, here's why" convention rather than silently
skipping the check).

### 8d. `ownerTour.ts` / `artistTour.ts`

Check whether either references the `AppointmentDetailPage.tsx` image/notes layout Part 6e changes. If
so, update the selector; if not, state so explicitly, same as 8c.

---

## Definition of done

- [ ] `dotnet build` clean; `dotnet test` green, count increased (not just unchanged) by the new backend
      test files in Part 7, zero new failures/skips relative to the Pre-flight baseline.
- [ ] `pnpm tsc --noEmit` clean; `pnpm lint` clean on every touched/new file individually if a full-repo
      lint run isn't feasible in this sandbox (matches the precedent noted in the phone-country-codes
      Decisions Log entry — confirm whether that sandbox constraint still applies before assuming it does).
- [ ] `pnpm build` clean.
- [ ] `pnpm test` green across `appointments`, `booking`, `clients`, `public`, `shared/components/ui`,
      count increased by the new frontend test files.
- [ ] Migration applied against a real local MySQL 8.4 instance, not just generated and read.
- [ ] `architecture.md`: `AllowAnonymous Exceptions` table (+5 rows), `IgnoreQueryFilters()` Approved
      Usages table (+rows, correctly numbered against the table's actual current last entry), Feature
      Module Map row #02 updated to reflect `IntakeForm` now existing, and a new dated Decisions Log entry
      for this feature following every existing entry's format (What/Why/Verified).
- [ ] Manually verified end-to-end, not just unit-tested: an actual guest booking submitted through the
      running frontend against the running backend results in — a real Identity user with a real random
      password nowhere visible in logs/responses; a `Client` row with all fields populated; an
      `Appointment` with `Status = Pending`; an `IntakeForm` row linked 1:1; the correct count of
      `AppointmentAttachment` rows split correctly by `Category`; one email sent containing both a working
      password-reset link and a working email-confirmation link; the plan's `AppointmentsPerMonth` usage
      counter incremented; a second submission with the same email correctly rejected with 409 and a
      login-instead message, without creating a second Identity user or a duplicate `Client`.
- [ ] Rate limits verified empirically (not assumed): 9th request within 5 minutes from the same IP to
      the booking-submit endpoint returns 429 with a `Retry-After` header.
- [ ] Presign endpoint verified to reject a `application/pdf` content type and a `Category` value other
      than `area`/`reference`.
- [ ] `GuestPendingUploadCleanupJob` verified against real R2 (or a local-equivalent test double) —
      deletes what it should, leaves what it shouldn't.
- [ ] Help Menu, user manual, and both onboarding-tour files addressed — either updated, or explicitly
      confirmed-no-change-needed with the reasoning stated in the PR description, per Part 8.
- [ ] `StudioPortfolioPage.tsx`'s CTA change (Decision #13) manually clicked through as a logged-out
      visitor, confirming it lands on a working guest booking form, not a login wall.
