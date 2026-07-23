# Overnight Prompt — Add NIPT (Business Tax ID) to Studio Registration

> Feed this file directly to Claude Code as the task prompt. It is self-contained:
> exact files, exact current code, exact target code, exact tests, exact docs to
> sync. Read the whole file before writing anything — later sections depend on
> decisions made in section 2 and 3.

**Date logged:** 2026-07-22
**Requested by:** Phi
**Origin:** Product decision — owners should supply their NIPT (Albanian business
tax ID, also called NUIS) at studio registration, for legal/invoicing compliance
and duplicate-business fraud prevention. **NIPT must never become a sign-in
credential** — login stays email + password (+ optional OAuth), full stop. That
conclusion was reached in a prior conversation and is not written down anywhere
else in this repo — this file is now the canonical record of it. If any part of
this implementation drifts toward using NIPT for authentication, stop and flag it.

---

## 1. Goal

Add a `Nipt` field to `Studio`, collected once at registration (`/register`,
Step 1) and editable afterward (`/studios/me`), validated for format and
uniqueness, never exposed on any anonymous/public-facing response, and fully
synced across Help Menu, standalone manual, and the two `docs/claude/*.md`
files that describe this area. This is a compliance/business-verification field
on the tenant record — not an auth factor, not a payment-provider KYC field
(Stripe Connect is explicitly dead in this codebase — see §4 "Do not touch").

Applicable non-negotiable rules from `CLAUDE.md` for this change: #2 (RBAC —
no new endpoints are unprotected), #3 (never log PII — see §6.3), #6 (match
industry standard — see §3.4), #7 (keep Help in sync — see §11), plus the
general "no unclear `var`", "no `any`", "test Application-layer logic" rules.

---

## 2. Decisions already made — implement as specified, do not re-litigate

1. **NIPT lives on `Studio`, not on the `IdentityUser`/JWT claims.** Confirmed
   by research: JWT today carries `sub`, `email`, `jti`, `email_verified`,
   `role`(s), `given_name`, `tenant_id` — nothing else, and there is no
   `ApplicationUser` subclass to add a column to even if we wanted to. Adding
   NIPT to claims or to the login request would be a deliberate violation of
   the prior decision. **Do not touch `LoginRequest`, `LoginCommand`,
   `LoginValidator`, `LoginPage.tsx`, `IdentityService.GenerateJwt`, or
   `TenantMiddleware`.**
2. **NIPT is required for new studio registrations, nullable at the DB level**
   (so existing rows backfill cleanly with `NULL` and are not blocked from
   logging in or operating post-migration).
3. **NIPT format:** Albanian NIPT/NUIS is 10 characters: 1 uppercase letter, 8
   digits, 1 uppercase letter (e.g. `L01234567A`). Implement the regex
   `^[A-Z]\d{8}[A-Z]$` with `.Length(10)`.
   **⚠ Confirm this against an authoritative source (QKB / tatime.gov.al)
   before merging.** This spec's author could not verify the exact checksum
   algorithm for the trailing check letter from the sandboxed research pass.
   If the checksum algorithm can be confirmed, add it as a second validation
   rule (`Must(HaveValidChecksum)`) in `RegisterStudioValidator` and
   `UpdateMyStudioValidator`. If it cannot be confirmed tonight, ship
   format-only validation (regex + length) and open a follow-up item — do not
   block the whole feature on the checksum, but do not skip flagging it either.
4. **Uniqueness policy:** a NIPT may not be registered by two different
   businesses. But the same owner may legitimately open a second location
   under the same legal business (same NIPT). Rule: reject registration if
   `Nipt` matches an existing **active** studio whose `OwnerEmail` (case-
   insensitive) differs from the incoming request's `OwnerEmail`. Allow it
   silently if the `OwnerEmail` matches (multi-location, same business). This
   mirrors how the platform already treats multi-studio ownership elsewhere
   (see `ClientAccountExtensions` multi-studio switch flow in
   `docs/claude/database.md` — same spirit, different entity).

---

## 3. Decisions you must make explicit note of / flag, not silently assume

3.1. Whether an *existing* studio without a NIPT should be soft-blocked from
     certain actions (e.g. can't be listed publicly, can't invite artists)
     until one is added. **Default for this task: no hard block.** Show a
     dismissible-but-recurring banner on `/studios/me` prompting the owner to
     add it, nothing more. Do not invent enforcement beyond that without
     flagging it as a separate decision.

3.2. Whether NIPT should ever be verified against a live registry (QKB open
     data, if it has an API) rather than just format-checked. **Out of scope
     tonight.** Log it as backlog item, same pattern as the existing `D18 —
     Tax handling` entry in `docs/claude/industry-feature-parity-report-2026-
     07-20.md` (do not edit that file — just be aware of the pattern; log the
     new backlog item in `docs/claude/architecture.md`'s Feature Module Map
     instead, per §11.3 below).

3.3. Whether to add issuer-side NIPT visibility/verification tooling
     (`IssuerStudioListPage.tsx`) in this same pass. **Recommended: yes,
     include it** — see §9. It's small, and CLAUDE.md rule #6 explicitly
     calls for issuer-role features to match general B2B SaaS platform-admin
     standards (org verification status is standard there — this is the same
     category of feature as the existing suspend/unsuspend toggle). If you
     run out of time, ship §§5–8 and §11–12 first (the compliance-critical
     path) and treat §9 as a fast-follow, but don't drop the Help/docs sync
     for whatever you do ship.

3.4. **Industry-standard benchmark check (CLAUDE.md rule #6):** none of
     Vagaro/Fresha/Boulevard/Mindbody/Zenoti/GlossGenius collect a national
     business tax ID at signup — this is *not* a pattern borrowed from the
     benchmark set, it's a local-compliance addition specific to operating in
     Albania. That's fine and expected (rule #6 is about UX/architecture
     quality, not about copying every field), but per the "explicitly flag
     gaps" convention, say so explicitly in the PR description /
     architecture.md entry rather than silently presenting it as
     benchmark-driven. The *pattern* to follow from the benchmark set is
     Stripe's/Fresha's own "add your business/tax details, non-blocking
     banner if missing" UX for backfill — which is what §3.1 specifies.

---

## 4. Scope boundary — do not touch

- `Pena_e_Arte.Application/Auth/**` (Login/RegisterUser commands, validators)
- `Pena_e_Arte.Infrastructure/Services/IdentityService.cs`
- `Pena_e_Arte.API/Middleware/TenantMiddleware.cs`
- `Pena_e_Arte.API/Extensions/AuthorizationExtensions.cs`
- `frontend/src/features/auth/**` (LoginPage, authApi, authSlice)
- Anything Stripe Connect–related. `StripeConnectService` is `[Obsolete]`.
  Do not resurrect it, do not add a `StripeAccountId`/KYC field anywhere —
  NIPT is unrelated to payment payouts in this codebase (confirmed: no Connect
  exists, card payments use the aggregator model, cash is manual).

---

## 5. Domain layer

**File:** `Pena_e_Arte.Domain/Entities/Studio.cs`

Add one property. Current fields (do not reorder or touch anything else):

```csharp
public class Studio
{
    public Guid     Id              { get; init; } = Guid.NewGuid();
    public string   Name            { get; set; }  = string.Empty;
    public string   Slug            { get; set; }  = string.Empty;
    public string   City            { get; set; }  = string.Empty;
    public string   OwnerEmail      { get; set; }  = string.Empty;
    public string?  Description     { get; set; }
    public string?  CoverImageUrl   { get; set; }
    public string?  PhoneNumber     { get; set; }
    public string?  InstagramHandle { get; set; }
    public string?  Nipt            { get; set; }          // ADD — business tax ID (NUIS), nullable for backfill
    public double   Latitude        { get; set; }
    public double   Longitude       { get; set; }
    public bool     IsActive              { get; set; } = true;
    public bool     ShowPlatformBranding  { get; private set; } = true;
    public DateTime? SlugLockedAt   { get; set; }
    public void UpdateBranding(bool show) => ShowPlatformBranding = show;
    public DateTime TrialExpiresAt        { get; set; }
    public string?  StripeCustomerId { get; set; }
    public DateTime CreatedAt       { get; init; } = DateTime.UtcNow;
    public long     StorageUsageBytes { get; set; }
    public Guid? PendingReferralCodeId { get; set; }
    public Subscription? Subscription { get; set; }
}
```

If you implement the §3.3 issuer-verification stretch, also add:

```csharp
    public DateTime? NiptVerifiedAt { get; set; }           // ADD (stretch, §9) — set by issuer, null = unverified
```

---

## 6. Infrastructure layer

### 6.1 EF configuration

**File:** `Pena_e_Arte.Infrastructure/Persistence/Configurations/StudioConfiguration.cs`

Add, alongside the existing `Property`/`HasIndex` calls:

```csharp
builder.Property(s => s.Nipt).HasMaxLength(10);
builder.HasIndex(s => s.Nipt)
       .IsUnique()
       .HasDatabaseName("ix_studios_nipt")
       .HasFilter("`nipt` IS NOT NULL AND `is_active` = 1");
```

Use a **filtered unique index**, not a plain unique index. MySQL 8.4 (Pomelo
provider) supports partial/filtered indexes via `HasFilter`. This does two
things at once: (a) allows unlimited `NULL` rows for backfilled studios that
haven't added a NIPT yet, and (b) means a *deactivated* studio's old NIPT
doesn't permanently block re-registration of that business under a new
studio row. The app-layer uniqueness check in §7.2 additionally applies the
same-owner-email exception from §2.4 — the DB index is the last-resort
integrity guarantee, not the primary UX (a `MySqlException` unique-violation
on this index should never normally fire because the handler checks first;
if it does fire, catch it and re-throw as `DuplicateNiptException` so the
client still gets a clean 409 instead of a raw 500).

If §3.3 stretch is implemented, no index needed on `NiptVerifiedAt` (low
cardinality, queried rarely, filtered client-side in the issuer list).

### 6.2 Migration

Generate with:

```bash
dotnet ef migrations add AddStudioNipt \
  --project Pena_e_Arte.Infrastructure \
  --startup-project Pena_e_Arte.API
```

Expected `Up`/`Down` (matching the `AddStudioContactInfo` precedent exactly —
verify the generated output matches this shape, MySQL charset annotation
included):

```csharp
public partial class AddStudioNipt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
                name: "Nipt",
                table: "studios",
                type: "varchar(10)",
                maxLength: 10,
                nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "ix_studios_nipt",
            table: "studios",
            column: "Nipt",
            unique: true,
            filter: "`nipt` IS NOT NULL AND `is_active` = 1");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "ix_studios_nipt", table: "studios");
        migrationBuilder.DropColumn(name: "Nipt", table: "studios");
    }
}
```

If the §3.3 stretch is in scope, add `NiptVerifiedAt` (`datetime(6)`,
nullable) to the same migration rather than a second one — one feature, one
migration, unless you're deliberately splitting core from stretch across two
PRs (also fine — then it's `AddStudioNiptVerifiedAt` as a follow-up).

Apply locally with:

```bash
dotnet ef database update --project Pena_e_Arte.Infrastructure --startup-project Pena_e_Arte.API
```

Zero-downtime note: this is an additive nullable column + filtered index, so
it's safe to deploy in one step per `database.md`'s migration-order guidance —
no need for the multi-step nullable→backfill→non-nullable dance used for
breaking changes, because this column is *staying* nullable at the DB level
by design (see §2.2).

---

## 7. Application layer

### 7.1 New domain exception

**File (new):** `Pena_e_Arte.Domain/Exceptions/DuplicateNiptException.cs`

```csharp
namespace Pena_e_Arte.Domain.Exceptions;

public class DuplicateNiptException()
    : DomainException("This business tax ID is already registered under a different account. " +
                       "If you're opening another location for the same business, register using " +
                       "the same owner email as your existing studio, or contact support.");
```

Confirm `ExceptionMiddleware` maps unrecognized `DomainException` subclasses
to a sensible default — if it needs an explicit case to hit 409 instead of a
generic 400, add one (`SlotAlreadyBookedException` is the existing precedent
for a domain rule → 409 mapping; match whatever it does).

### 7.2 Registration command/handler/validator

**File:** `Pena_e_Arte.Application/Studios/Commands/RegisterStudioCommand.cs`

Add the NIPT uniqueness check into `RegisterStudioHandler.Handle`, right
after the slug-collision loop and before constructing `Studio`:

```csharp
public async Task<StudioResponse> Handle(RegisterStudioCommand command, CancellationToken ct)
{
    RegisterStudioRequest req = command.Request;
    string slug   = req.Slug;
    int    suffix = 2;
    while (await db.Studios.AnyAsync(s => s.Slug == slug, ct))
        slug = $"{req.Slug}-{suffix++}";

    string normalizedNipt = req.Nipt.Trim().ToUpperInvariant();
    Studio? conflictingStudio = await db.Studios.IgnoreQueryFilters()
        .Where(s => s.Nipt == normalizedNipt && s.IsActive)
        .FirstOrDefaultAsync(ct);
    if (conflictingStudio is not null &&
        !string.Equals(conflictingStudio.OwnerEmail, req.OwnerEmail, StringComparison.OrdinalIgnoreCase))
    {
        throw new DuplicateNiptException();
    }

    // Referral code validation unchanged — see existing code

    DateTime now      = DateTime.UtcNow;
    DateTime trialEnd = now.AddDays(14);
    DateTime graceEnd = trialEnd.AddDays(7);

    Studio studio = new()
    {
        Name = req.Name, Slug = slug, City = req.City, OwnerEmail = req.OwnerEmail,
        Nipt = normalizedNipt,
        Latitude = req.Latitude, Longitude = req.Longitude, IsActive = true,
        TrialExpiresAt = trialEnd, PendingReferralCodeId = pendingReferralCodeId,
    };
    // ... unchanged Subscription creation, SaveChangesAsync, job scheduling, response
}
```

Note `IgnoreQueryFilters()` is used here even though this handler is not
`IssuerOnly` — `Studio` is an issuer-level entity with **no query filter
registered on it at all** (confirmed in `AppDbContext.OnModelCreating` — only
tenant-scoped entities get `HasQueryFilter`), so this is not a violation of
the `IgnoreQueryFilters()`-requires-`IssuerOnly` rule; that rule applies to
tenant-scoped entities only. Still, double-check this against
`database.md`'s "Tenant Isolation Rules" table before merging — if `Studio`
ever gains a filter in the future this call must be revisited.

**File:** `Pena_e_Arte.Application/Studios/Validators/RegisterStudioValidator.cs`

```csharp
public class RegisterStudioValidator : AbstractValidator<RegisterStudioCommand>
{
    private static readonly Regex NiptFormat = new(@"^[A-Z]\d{8}[A-Z]$", RegexOptions.Compiled);

    public RegisterStudioValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.Slug)
            .NotEmpty().MaximumLength(100)
            .Matches("^[a-z0-9-]+$")
            .WithMessage("Slug may only contain lowercase letters, numbers, and hyphens.");
        RuleFor(x => x.Request.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.OwnerEmail).NotEmpty().MaximumLength(256).EmailAddress();
        RuleFor(x => x.Request.Nipt)
            .NotEmpty()
            .Length(10)
            .Must(n => NiptFormat.IsMatch(n.Trim().ToUpperInvariant()))
            .WithMessage("NIPT must be 10 characters: a letter, 8 digits, then a letter (e.g. L01234567A).");
        RuleFor(x => x.Request.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Request.Longitude).InclusiveBetween(-180, 180);
    }
}
```

### 7.3 Update command/handler/validator (post-registration edit)

**File:** `Pena_e_Arte.Application/Studios/Commands/UpdateMyStudioCommand.cs`

```csharp
public async Task<StudioResponse> Handle(UpdateMyStudioCommand command, CancellationToken ct)
{
    Domain.Entities.Studio studio = await db.Studios
        .FirstOrDefaultAsync(s => s.Id == tenant.StudioId, ct)
        ?? throw new NotFoundException(nameof(Domain.Entities.Studio), tenant.StudioId);

    studio.Name  = command.Request.Name;
    studio.City  = command.Request.City;
    studio.Latitude  = command.Request.Latitude;
    studio.Longitude = command.Request.Longitude;
    studio.PhoneNumber = string.IsNullOrWhiteSpace(command.Request.PhoneNumber) ? null : command.Request.PhoneNumber.Trim();
    studio.InstagramHandle = string.IsNullOrWhiteSpace(command.Request.InstagramHandle) ? null : command.Request.InstagramHandle.Trim().TrimStart('@');

    if (!string.IsNullOrWhiteSpace(command.Request.Nipt))
    {
        string normalizedNipt = command.Request.Nipt.Trim().ToUpperInvariant();
        bool takenByAnother = await db.Studios.IgnoreQueryFilters()
            .AnyAsync(s => s.Id != studio.Id && s.Nipt == normalizedNipt && s.IsActive &&
                           !s.OwnerEmail.Equals(studio.OwnerEmail, StringComparison.OrdinalIgnoreCase), ct);
        if (takenByAnother) throw new DuplicateNiptException();
        studio.Nipt = normalizedNipt;
    }

    await db.SaveChangesAsync(ct);
    return new StudioResponse(/* ... include Nipt, see §8 */);
}
```

Note: `EF.Functions.Like`/string comparison on `IEnumerable`/`AnyAsync` with
`StringComparison.OrdinalIgnoreCase` does not translate to SQL directly in
some EF Core versions — if this throws a client-evaluation warning, normalize
`OwnerEmail` casing at write-time instead (store lowercase, compare lowercase)
rather than comparing with `StringComparison` in the LINQ predicate. Check
how the existing `RegisterUserCommand`'s owner-email cross-check (§ in
research: `string.Equals(studio.OwnerEmail, req.Email, StringComparison.OrdinalIgnoreCase)`)
handles this — that comparison happens after materializing `studio` from the
DB (not inside the LINQ predicate), so mirror that pattern here: fetch
candidates first, then compare in memory.

**File:** `Pena_e_Arte.Application/Studios/Validators/UpdateMyStudioValidator.cs`

```csharp
public class UpdateMyStudioValidator : AbstractValidator<UpdateMyStudioCommand>
{
    private static readonly Regex NiptFormat = new(@"^[A-Z]\d{8}[A-Z]$", RegexOptions.Compiled);

    public UpdateMyStudioValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.City).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Request.Longitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.Request.Nipt)
            .Length(10)
            .Must(n => NiptFormat.IsMatch(n!.Trim().ToUpperInvariant()))
            .WithMessage("NIPT must be 10 characters: a letter, 8 digits, then a letter (e.g. L01234567A).")
            .When(x => !string.IsNullOrWhiteSpace(x.Request.Nipt));
    }
}
```

`Nipt` is optional on update (an owner who skipped it at registration — not
possible after tonight's change, but relevant for every pre-existing studio —
can add it later; an owner who already has one should not be forced to
resubmit it every time they edit their profile, and once a valid NIPT is set
you may want to make the field read-only in the UI going forward rather than
silently allow changing a business's legal tax ID — see §9.2 for the UI
decision).

### 7.4 Logging

Per CLAUDE.md rule #3, never log the raw value even though NIPT is a business
identifier, not personal PII — treat it the same as any other tenant
identifier and log only `StudioId`:

```csharp
Log.Information("Studio registered {@StudioId} nipt_provided={@NiptProvided}",
    studio.Id, !string.IsNullOrEmpty(studio.Nipt));
```

Never do `Log.Information("... {@Nipt}", studio.Nipt)`.

---

## 8. Contracts — and the public-DTO leak audit (do this before anything else ships)

**File:** `Pena_e_Arte.Contracts/Requests/RegisterStudioRequest.cs`

```csharp
public record RegisterStudioRequest(
    string  Name, string Slug, string City, double Latitude, double Longitude,
    string  OwnerEmail, string Nipt, string? ReferralCode = null);
```

(`Nipt` inserted as a required positional parameter before the optional
`ReferralCode` — C# records require all optional/defaulted parameters to
trail required ones.)

**File:** `Pena_e_Arte.Contracts/Requests/UpdateStudioRequest.cs`

```csharp
public record UpdateStudioRequest(
    string Name, string City, double Latitude, double Longitude,
    string? PhoneNumber = null, string? InstagramHandle = null, string? Nipt = null);
```

**File:** `Pena_e_Arte.Contracts/Responses/StudioResponse.cs`

```csharp
public record StudioResponse(
    Guid Id, string Name, string Slug, string City, double Latitude, double Longitude,
    bool ShowPlatformBranding, bool AllowBrandingRemoval,
    DateTime TrialExpiresAt, DateTime CreatedAt, bool IsActive, DateTime? SlugLockedAt,
    string? PhoneNumber = null, string? InstagramHandle = null, string? Nipt = null);
```

**Before merging, audit every endpoint that returns `StudioResponse` (or any
projection of `Studio`) and confirm none of them is `AllowAnonymous`.**
Specifically check the public studio-profile/booking-widget endpoint
referenced in `architecture.md`'s Feature Module Map (#07 Studio Map, and
whatever endpoint backs the public `/book?studio={slug}` page and any embed-
code widget) — if any anonymous-facing endpoint currently reuses
`StudioResponse` rather than a slimmer public-only DTO, **do not add `Nipt`
to `StudioResponse` as written above.** Instead:

1. Create `PublicStudioResponse` (no `Nipt`, no `OwnerEmail`, no
   `StripeCustomerId` — audit whether those are already leaking too, since
   they'd have the same problem) for every `AllowAnonymous` route, and
2. Keep `Nipt` only on the authenticated `StudioResponse` returned by
   `POST /api/v1/studios` (immediately post-registration, same request) and
   `PUT /api/v1/studios/me` (owner-only).

This audit is not optional — a business tax ID leaking on a public booking
page is a real compliance/privacy problem, not a style nit. If you find the
DTO is already shared and already leaking `OwnerEmail`/`StripeCustomerId`
today, fix that in the same pass and note it as a pre-existing issue found
during this task, not a new one you introduced.

---

## 9. Frontend

### 9.1 Registration wizard

**File:** `frontend/src/features/studios/components/RegisterStudioPage.tsx`

Add `nipt` to the Step 1 schema (grouped with the studio's identity fields,
not with the owner's account fields in Step 2 — NIPT describes the business,
not the login):

```typescript
const schema = z.object({
  name: z.string().min(1, "Studio name is required").max(200),
  slug: z.string().min(1).max(100).regex(/^[a-z0-9-]+$/, "Slug may only contain lowercase letters, numbers, and hyphens."),
  city: z.string().min(1, "City is required").max(100),
  nipt: z.string()
    .trim()
    .length(10, "NIPT must be exactly 10 characters")
    .regex(/^[A-Za-z]\d{8}[A-Za-z]$/, "NIPT format looks wrong — expected a letter, 8 digits, then a letter (e.g. L01234567A)")
    .transform((v) => v.toUpperCase()),
  latitude:  z.number({ error: "Latitude is required" }).min(-90).max(90),
  longitude: z.number({ error: "Longitude is required" }).min(-180).max(180),
  email: z.string().min(1).max(256).email("Enter a valid email"),
  password: z.string(),
  confirmPassword: z.string(),
}).superRefine((data, ctx) => { /* unchanged password match/length logic */ });
```

Add the input next to the City/Location fields in Step 1's JSX, using
existing `shadcn/ui` `Input` + `Label` + form-error patterns already in this
component (match whatever the `city` field's markup looks like exactly —
don't introduce a new input pattern). Suggested copy:

- Label: `Business tax ID (NIPT)`
- Helper text below the input: `Your studio's NIPT, used for invoicing and
  business verification. Format: one letter, 8 digits, one letter.`
- Placeholder: `L01234567A`

Wire it into the `registerStudio` mutation call in `onSubmit` alongside the
other Step 1 fields (see §9.3 for the RTK Query type change).

### 9.2 Studio profile / edit page

**File:** `frontend/src/features/studios/components/StudioProfilePage.tsx`

Add `nipt` to its zod schema (optional, matching §7.3's server-side
"optional on update" decision):

```typescript
const schema = z.object({
  name: z.string().min(1).max(200),
  city: z.string().min(1).max(200),
  latitude: z.number().min(-90).max(90),
  longitude: z.number().min(-180).max(180),
  phoneNumber: z.string().optional(),
  instagramHandle: z.string().optional(),
  nipt: z.string()
    .trim()
    .length(10, "NIPT must be exactly 10 characters")
    .regex(/^[A-Za-z]\d{8}[A-Za-z]$/, "NIPT format looks wrong")
    .transform((v) => v.toUpperCase())
    .optional()
    .or(z.literal("")),
});
```

UX decision (per §7.3's note): once a studio has a non-null `Nipt`, render
the field **read-only with a "Contact support to change" note** instead of
a freely editable input — a business's legal tax ID being silently editable
by anyone with owner access is a bigger footgun than the convenience it buys.
If `nipt` is currently `null` (pre-existing studio that predates this
feature), render it as a normal editable input plus the backfill banner
below.

Add a **non-blocking banner** at the top of this page (dismiss-for-session,
reappear next visit — check if `shared/components` already has a Banner/Alert
primitive from `shadcn/ui`'s `Alert` component, used elsewhere per
`frontend.md`'s "use shadcn primitives before writing a custom component"
rule) shown only when `studio.nipt` is `null`:

> "Add your business tax ID (NIPT) to keep your invoices compliant. [Add now]"

Clicking "Add now" scrolls to / focuses the NIPT input.

### 9.3 RTK Query types

**File:** `frontend/src/features/studios/studiosApi.ts`

```typescript
export interface RegisterStudioRequest {
  name: string; slug: string; city: string; latitude: number; longitude: number;
  ownerEmail: string; nipt: string; referralCode?: string;
}
export interface StudioResponse {
  id: string; name: string; slug: string; city: string; latitude: number; longitude: number;
  showPlatformBranding: boolean; allowBrandingRemoval: boolean; trialExpiresAt: string;
  createdAt: string; isActive: boolean; slugLockedAt: string | null;
  phoneNumber: string | null; instagramHandle: string | null; nipt: string | null;
}
export interface UpdateStudioRequest {
  name: string; city: string; latitude: number; longitude: number;
  phoneNumber?: string | null; instagramHandle?: string | null; nipt?: string | null;
}
```

Add a 409-specific error message mapping in whichever shared RTK Query error
handler / toast utility this app uses for mutations (check
`shared/utils` for an existing `getErrorMessage`/`isApiError` helper — match
its pattern) so a duplicate-NIPT rejection shows the
`DuplicateNiptException` message from §7.1 verbatim to the owner, not a
generic "Something went wrong."

### 9.4 Do not touch

`frontend/src/features/auth/components/LoginPage.tsx`,
`frontend/src/features/auth/authApi.ts`, `authSlice.ts` — no field, no type,
no UI change. Login stays email + password + OAuth, exactly as documented in
the standalone manual's `#guest-login` section today.

---

## 10. Issuer platform-admin visibility (stretch — see §3.3 for priority call)

**File:** `frontend/src/features/platform/components/IssuerStudioListPage.tsx`

Add a `NIPT` column to the studio table (show `—` if null) and, if
`Studio.NiptVerifiedAt` was added per §5, a "Verified" badge/toggle the
issuer can flip via a new `VerifyStudioNiptCommand` (`IssuerOnly`, single
`PUT /api/v1/platform/studios/{id}/verify-nipt` endpoint, sets
`NiptVerifiedAt = DateTime.UtcNow`, no request body). This is the same
category of control as the existing suspend/unsuspend action on this page —
match its exact markup/mutation pattern (button + confirm dialog + optimistic
tag invalidation via `platformApi`'s `PlatformStudio`-equivalent tag — check
what tag type this page already invalidates on suspend and reuse it, don't
invent a new one). Keep this in `platformApi.ts`, not `studiosApi.ts`, per
`frontend.md`'s explicit "do NOT add issuer platform queries to
`studiosApi`" rule.

If you implement this, it needs its own backend command/validator/endpoint
(`Pena_e_Arte.Application/Studios/Commands/VerifyStudioNiptCommand.cs` or a
new `Platform/` subfolder if that's where other issuer-only studio commands
already live — check first) plus its own unit tests, mirroring §11's pattern.

---

## 11. Tests

### 11.1 Backend

**File:** `tests/Pena_e_Arte.UnitTests/Studios/RegisterStudioHandlerTests.cs`

Add, alongside the existing `[Fact]`s:

- `Handle_ValidNipt_PersistsNiptToStudio`
- `Handle_DuplicateNiptDifferentOwner_ThrowsDuplicateNiptException`
- `Handle_DuplicateNiptSameOwnerEmail_Succeeds` (the multi-location case,
  §2.4)
- `Handle_DuplicateNiptOnInactiveStudio_Succeeds` (confirms the filtered
  index / app-layer `s.IsActive` check lets a business re-register if their
  old studio was deactivated)

Update `ValidRequest()`:

```csharp
private static RegisterStudioRequest ValidRequest() =>
    new("Tinta & Alma", "tinta-alma", "Porto", 41.15, -8.61, "owner@tinta-alma.com", "L01234567A");
```

**File:** `tests/Pena_e_Arte.UnitTests/Studios/RegisterStudioValidatorTests.cs`

Add:

- `Validate_EmptyNipt_FailsOnNipt`
- `[Theory]` with `[InlineData("L0123456A")]` (9 chars), `[InlineData("L012345678A")]`
  (11 chars), `[InlineData("0101234567A")]` (starts with digit),
  `[InlineData("L01234567")]` (missing trailing letter) →
  `Validate_MalformedNipt_FailsOnNipt`
- `Validate_ValidNiptLowercase_IsValid` (confirms the `.ToUpperInvariant()`
  normalization path is exercised — validator should accept lowercase input
  since the handler normalizes it, but double check whether you want the
  validator itself to require uppercase-only input and force the frontend to
  transform before submit, vs. accepting either case server-side; pick one
  and be consistent between frontend `zod` `.transform()` and backend
  validator — currently spec'd as: frontend transforms to uppercase before
  submit, backend still normalizes defensively in case of direct API callers)

Update the `Command(...)` helper to take a `nipt` parameter (default to a
valid value like `"L01234567A"` for tests unrelated to NIPT itself, so 100+
lines of existing tests don't need touching individually — check whether the
handler-test file's `ValidRequest()` is reused by other command tests before
changing its signature; if so, prefer a new optional trailing parameter with
a default rather than breaking every call site).

Also add a new `UpdateMyStudioHandlerTests.cs`/`UpdateMyStudioValidatorTests.cs`
pair if one doesn't already exist — search for it first; if it exists, extend
it with the equivalent NIPT cases (add/change/duplicate/same-owner-exception).

### 11.2 Integration

Add or extend an integration test (check `tests/Pena_e_Arte.IntegrationTests`
for an existing `StudiosControllerTests`-equivalent hitting
`POST /api/v1/studios` end-to-end against the test MySQL instance) covering:
duplicate-NIPT registration returns `409`, and the response body contains the
`DuplicateNiptException` message.

### 11.3 Frontend

**File:** `frontend/src/features/studios/__tests__/RegisterStudioPage.test.tsx`

Add cases to the "step 1" `describe` block: NIPT required, NIPT format
rejected (a couple of malformed examples), NIPT included in the
`registerStudio` mutation payload on successful submit. Update the existing
`fillStep1` helper to also fill NIPT (with a valid default like
`"L01234567A"`) so every other Step-1 test that calls it doesn't break.

Add an equivalent small test file/cases for `StudioProfilePage.tsx` covering:
banner shows when `nipt` is `null`, banner absent when set, field is
read-only once set (per §9.2), duplicate-NIPT 409 shows the server error
message.

---

## 12. Help sync (CLAUDE.md rule #7 — mandatory in this same change)

### 12.1 In-app Help Menu

**File:** `frontend/src/features/help/helpContent.ts`

Extend the existing `owner-studio-profile` article (do not duplicate it into
a second article — NIPT is part of the same "studio settings" surface):

```typescript
{
  id: "owner-studio-profile",
  roles: [Owner],
  title: "Edit your studio profile",
  route: "/studios/me",
  keywords: ["studio settings", "studio name", "address", "description", "nipt", "tax id", "business id"],
  summary: "Edit your studio's public details — name, address, phone, Instagram, description — and your business tax ID (NIPT), which clients don't see but is used for invoicing and verification.",
  steps: [
    "Go to Studio Settings.",
    "Click \"Edit\" and update your studio name, address/city, phone number, Instagram handle, or description.",
    "If you haven't added your NIPT yet, enter it in the Business tax ID field — format is one letter, 8 digits, one letter (e.g. L01234567A). Once saved, this field becomes read-only; contact support to change it.",
    "Click \"Save\" to publish the changes.",
  ],
  tips: [
    "Your NIPT is never shown to clients or on your public booking page — it's for invoicing and business verification only.",
  ],
  relatedArticleIds: ["owner-branding", "owner-embed", "owner-qr-code", "owner-referral"],
},
```

The `/register` wizard itself is pre-login/anonymous, so per the existing
pattern (no in-app Help Menu article covers it today, since that panel is
post-login only) — **do not** add a `helpContent.ts` article for the
registration wizard's Step 1. Only the standalone manual documents that flow
(§12.2). This is a deliberate "no change here" per the existing convention,
not an oversight — say so in the PR description so a reviewer doesn't
wonder why it's missing.

### 12.2 Standalone user manual

**File:** `frontend/public/user-manual/index.html`

Update `#guest-register-studio`:

```html
<section id="guest-register-studio" data-role="guest">
<h2><span class="role-badge role-guest">Guest</span> Register a studio</h2>
<p>A two-step wizard at <code>/register</code> that creates a brand-new studio and its owner account together. Step 1 collects the studio's identity, location, and business tax ID (NIPT); step 2 creates the owner's login, either by password or by continuing with Google/Apple.</p>
...
<h3>Steps</h3>
<ol class="steps">
<li><span class="step-title">Step 1 — Studio details:</span> Studio name (auto-generates a URL slug you can still edit), pick the studio's location on the map picker, and enter your business tax ID (NIPT) — one letter, 8 digits, one letter (e.g. L01234567A). This is used for invoicing and business verification and is never shown publicly.</li>
<li>Click <span class="step-title">Next</span> to proceed to step 2.</li>
<li><span class="step-title">Step 2 — Owner account:</span> enter the owner's email, then either set a password + confirm, or click a Google/Apple button to continue with OAuth instead (this replaces the password fields).</li>
<li>Submit to create the studio, create the owner account, sign in, and land on the Dashboard.</li>
</ol>
...
</section>
```

Update `#owner-studio-profile`'s steps list to mention the NIPT field and its
read-only-after-set behavior, matching §12.1's `steps` array wording. Leave
`#guest-login` untouched — it correctly documents email/password + OAuth only,
and should keep doing so (this is your explicit confirmation that the
omission is intentional, not stale).

### 12.3 Onboarding tour

**File:** `frontend/src/features/help/tours/ownerTour.ts`

No new step required — the existing step targeting
`[data-tour="owner-studio-profile-nav"]` already says "Edit your studio's
public details, branding, booking widget, QR code, and referral code here,"
which is generic enough to cover the new field without editing. Confirm this
by reading the full file before deciding, and only add a step if you judge
the NIPT addition to be non-discoverable enough to need one — if you do add
one, keep it to the existing terse one-sentence `body` style, don't write a
paragraph.

---

## 13. `docs/claude/*.md` sync

### 13.1 `docs/claude/database.md`

Add `Nipt` (and `NiptVerifiedAt` if §3.3 is in scope) to the "Studio Entity
Fields" code block (currently lines 69–87), matching its existing style
exactly — this section is explicitly called out as "consolidated here" from
elsewhere, so it must stay accurate.

### 13.2 `docs/claude/architecture.md`

Add a new row to the Feature Module Map table (currently 32 numbered rows,
per this file's own research pass — confirm current max number before
picking `33`, since other overnight work may have added rows since 2026-07-20):

```
| 33 | NIPT Business Verification | `Studio.Nipt` (+ `NiptVerifiedAt` if stretch shipped) | None — format validation only, no external registry call | Per-tenant (owner write), Issuer-level (read/verify) |
```

Add a short dated note (matching the style of the existing "In-App Help
Menu — 2026-07-20" and "Payment Architecture" sections) explaining: what was
added, why NIPT is registration/compliance metadata and explicitly not an
auth factor (link back to this file by name so a future reader can find the
full reasoning), the filtered-unique-index + same-owner-email exception
design, and the open checksum-verification flag from §2.3. This is the
"Decisions Log"-style entry referenced elsewhere in this file (the Help Menu
section mentions "not logged as a separate Decisions Log entry" for a minor
dependency choice — this NIPT design *is* significant enough to warrant one).

---

## 14. Verification checklist — do not mark this done until all of these pass

1. `dotnet build` clean, `dotnet test` — all existing tests plus every new
   test listed in §11.1 pass.
2. `pnpm lint` and `pnpm test` clean, including the new/updated cases in
   §11.3.
3. Fresh registration end-to-end (`/register` → dashboard) with a
   well-formed NIPT succeeds; malformed NIPT is rejected client-side before
   any network call; a NIPT already used by a different owner returns the
   409 and the UI shows the exact server message.
4. Same NIPT, same owner email, second studio → registration **succeeds**
   (multi-location case) — verify this manually or via integration test, it's
   the easiest case to get backwards.
5. **DTO leak audit from §8 is complete and documented** — either confirmed
   no public endpoint returns `Nipt`, or `PublicStudioResponse` was
   introduced and every `AllowAnonymous` studio-related route now uses it.
6. `Studio.Nipt` never appears in any Serilog output — grep the diff for any
   `Log.*` call referencing `Nipt` and confirm none logs the raw value.
7. Existing (pre-migration) studios: confirm they still log in, still appear
   in owner/issuer dashboards, and see the backfill banner on `/studios/me`
   with no other broken behavior.
8. `helpContent.ts` article renders correctly in the in-app Help Menu (open
   it as an owner, search "NIPT" or "tax id", confirm the article surfaces).
9. `user-manual/index.html` renders correctly standalone (open the file
   directly, confirm both updated sections read correctly and the anchor
   links still work).
10. `docs/claude/database.md` and `docs/claude/architecture.md` diffs
    reviewed for accuracy against what was actually shipped (not what was
    planned — if the §3.3 stretch was dropped, don't leave references to
    `NiptVerifiedAt` in the docs).
11. If §9/§10 (issuer verification stretch) was shipped, its own tests
    (backend command + validator, frontend list-page interaction) pass too.
12. Confirm nothing in §4's "do not touch" list was touched — diff those
    exact files/folders against `main` and confirm zero changes.
