# Overnight Master Prompt — Client Conduct Reports (Report an Artist / Studio)

**Target:** the main "Pena e Artë - Engineering" Claude Code session, running with full repo
write access. Execute this prompt unattended, end to end, following the same discipline as
every other `overnight-prompt-*.md` in `docs/claude/` — verify every claim below against the
live source before writing code, do not re-litigate the decisions in the Decisions section,
and append a real Decisions Log entry to `architecture.md` documenting what you actually built
(not what this prompt predicted), including any deviations you were forced into and why.

**Feature, one sentence:** let a client file a report against an artist or a studio — picking a
category (scam, sexual misconduct, unsafe hygiene practices, harassment, discrimination, poor
service, other) and writing the reason in their own words — visible only to the issuer, the
studio's owner, and (in redacted form) the artist being reported.

---

## Pre-flight

1. Read `CLAUDE.md`, `docs/claude/backend.md`, `docs/claude/frontend.md`,
   `docs/claude/database.md`, `docs/claude/conventions.md` in full before touching anything.
2. Read the "Context" section below and re-verify every cited file/line against the live repo —
   this prompt was written against the repo state as of 2026-08-22 (latest migration
   `20260822111309_AddSocialAccountLinks`); if something has moved since, trust the live source
   and note the drift in your Decisions Log entry, per `architecture.md`'s own standing rule.
3. This prompt introduces a **new domain concept — trust & safety moderation** — that doesn't
   yet exist anywhere in the codebase. Nothing here is a variation on an existing entity; read
   `Review.cs`, `FeedbackReport.cs`, and `AuditLogEntry.cs` in full (all three are quoted below,
   but read the live files too) before writing `ConductReport.cs` — this feature deliberately
   borrows shape from all three and you need to understand why each borrowed piece looks the way
   it does, not just copy it.
4. Run the full backend + frontend test suites before starting, so you have a clean baseline to
   diff against. Do not fix unrelated pre-existing failures as part of this prompt — note them in
   your final summary instead, same as prior prompts have done.

---

## Context — current state (verified against live source, 2026-08-22)

**Nothing like this exists today.** There is no "report a studio/artist for misconduct" feature
anywhere in the product. What exists that this feature borrows from:

- **`Review`** (`Pena_e_Arte.Domain/Entities/Review.cs`) — client-authored content targeting
  exactly one of `StudioId`/`ArtistId`/`PortfolioImageId`, tied to a completed `AppointmentId`
  for studio/artist targets. Plain class, **not** a `TenantEntity`, no EF query filter — written
  via public-portfolio-page endpoints (`/api/v1/public/studios/{slug}/reviews`,
  `/api/v1/public/artists/{slug}/reviews`) that resolve the target by slug with
  `IgnoreQueryFilters()` (an approved cross-tenant lookup, same shape as entries 19–20 in the
  `IgnoreQueryFilters()` table), independent of the author's own `ICurrentTenant.StudioId`.
- **`FeedbackReport`** (`Pena_e_Arte.Domain/Entities/FeedbackReport.cs`) — non-tenant entity, no
  query filter registered at all, `IsAccessibleBy(userId, studioId, role)` method centralizing
  read authorization, loaded through a shared `FeedbackAccessGuard.LoadAccessibleReportAsync`
  helper (`Pena_e_Arte.Application/Feedback/FeedbackAccessGuard.cs`) so every handler enforces
  the same rule instead of three copies drifting apart. `Status` is a real C# enum
  (`FeedbackStatus`: Open/Reviewing/Resolved/Dismissed) persisted via `HasConversion<string>()`.
- **`AuditLogEntry`** (`Pena_e_Arte.Domain/Entities/AuditLogEntry.cs`) — append-only, non-tenant,
  nullable `StudioId`, written exclusively by `AuditLogBehavior<TRequest,TResponse>`
  (`Pena_e_Arte.Application/Common/Behaviors/AuditLogBehavior.cs`) for any MediatR command
  implementing `IAuditableCommand` (`Pena_e_Arte.Domain/Interfaces/IAuditableCommand.cs`),
  **only after** the handler's own `SaveChangesAsync` succeeds. Read by
  `GetAuditLogHandler` (issuer, cross-tenant, no filter to bypass) and
  `GetMyStudioAuditLogHandler` (owner, explicit `Where(a => a.StudioId == tenant.StudioId)`).
  Registered in `Program.cs` **after** `PlanLimitBehavior`.
- **RBAC policies** (`Pena_e_Arte.API/Extensions/AuthorizationExtensions.cs`): `ClientOnly`,
  `ClientAndAbove`, `ArtistAndAbove`, `OwnerOnly` (owner **and** issuer), `IssuerOnly`. There is
  no `ArtistOnly` policy — an artist-scoped endpoint uses `ArtistAndAbove` and resolves the
  caller's own `Artist` row inside the handler.
- **`ICurrentUser`** exposes only `UserId`, `Role`, `Email`, `IsAuthenticated` — no `ArtistId` or
  `ClientId`. Any handler needing "the caller's own Artist row" queries
  `db.Artists.FirstOrDefaultAsync(a => a.UserId == user.UserId, ct)`, which is naturally
  tenant-filtered to the caller's own studio since an artist only ever authenticates within
  their own studio's tenant context.
- **Studio.OwnerEmail** (`Pena_e_Arte.Domain/Entities/Studio.cs`) — every studio already carries
  its owner's contact email; no lookup needed to email an owner directly.
- **Platform support inbox**: `SubmitContactRequestHandler`
  (`Pena_e_Arte.Application/Contact/Commands/SubmitContactRequestCommand.cs`) hardcodes
  `private const string SupportEmail = "support@tattooos.co"` with the comment
  "Founder-confirmed public support inbox (2026-08-01). Not a secret." This is currently
  duplicated nowhere else — this prompt is the second consumer, so Part 2 below extracts it to a
  shared constant instead of copy-pasting the literal a second time.
- **Attachment uploads**: `FeedbackReport.AttachmentUrls` (`List<string>`, max 3, validated via
  `IR2Service.IsR2Url` in `SubmitFeedbackValidator`) uses the same presigned-upload flow as
  everywhere else — frontend `usePresignedUpload()` hook
  (`frontend/src/shared/hooks/usePresignedUpload.ts`) → `usePresignUploadMutation` → R2 presigned
  PUT → public URL stored server-side after validation.
- **`route family`**: reviews live under `app.MapGroup("/api/v1/public")` inside
  `Pena_e_Arte.API/Endpoints/PublicEndpoints.cs` — `POST /studios/{slug}/reviews`,
  `POST /artists/{slug}/reviews`, `GET /studios/{slug}/reviews/eligible-appointments`,
  `GET /artists/{slug}/reviews/eligible-appointments`, all `ClientAndAbove` +
  `RequireRateLimiting("public-write")` (writes) / `("public-read")` (reads) — **rate-limited
  even though authenticated**, unlike the "authenticated endpoints skip rate limiting" note in
  the Help Search Analytics Decisions Log entry, which applies specifically to that feature's
  tenant-scoped write path, not this public-group family. Follow the reviews precedent here, not
  the Help Search one.
- **Frontend**: reviews' RTK Query surface lives in `features/public/publicApi.ts` (not a
  separate `reviewsApi.ts` — that file only holds the owner's `respondToReview` mutation). The
  write-a-review UI lives in `features/public/components/ReviewSection.tsx`, rendered inside
  `ArtistPortfolioPage.tsx` / `StudioPortfolioPage.tsx` (public pages — `AllowAnonymous` at the
  page level, but the review/report actions themselves require an authenticated client).
- **Existing name collisions to avoid**: there is already an `Application/Reports/` folder and
  `Pena_e_Arte.API/Endpoints/ReportEndpoints.cs` — these are the **owner revenue & trend
  reporting** feature (`GetRevenueSummaryQuery`, feature #31 in the Feature Module Map), and a
  `frontend/src/features/reports/` folder for the same. There is also `FeedbackReport` (bug/
  feature/support tickets). None of these are what this prompt builds — name everything
  `ConductReport*` throughout (domain entity, folders, endpoints class, frontend feature folder)
  to keep all three "report" concepts unambiguous in the codebase.
- **Feature Module Map** in `architecture.md` currently ends at row **#36** (Social Media
  Verification). This feature becomes row **#37**.
- **`ReviewableAppointmentResponse`** lives in namespace `Pena_e_Arte.Contracts.Responses.Public`.
  `GetReviewableArtistAppointmentsHandler` /
  `GetReviewableStudioAppointmentsHandler` (`Pena_e_Arte.Application/Public/Queries/`) filter to
  `AppointmentStatus.Completed` and exclude appointments already reviewed by that author for
  that target — **this feature's equivalent query deliberately does neither** (see Decisions).

---

## Decisions (already made — do not re-litigate)

These were confirmed explicitly before this prompt was written. Build exactly this; if you find
a reason one of these is actually unworkable against the live code, stop and document why in
your Decisions Log entry rather than silently picking a different behavior.

1. **Reporter identity is anonymized to the reported artist, never to the owner or issuer.**
   The artist sees the category, the free-text reason, which of their appointments it relates to,
   and any attached evidence — but never the reporting client's name, email, or user id. The
   owner and the issuer always see full reporter identity, for investigation purposes. This is a
   real safety property, not a UI nicety — enforce it server-side in the response mapping (never
   send the field and redact in the UI), the same way `AuditLogEntry.Metadata` is described as
   "PII-scrubbed JSON — never names/emails/phone numbers" at the source, not at render time.

2. **Resolution authority is split by severity.** Every `ReportCategory` value is classified
   High or Standard severity (mapping below). An owner may set the status
   (Open → Reviewing → Resolved/Dismissed) of a **Standard**-severity report about their own
   studio/artists. A **High**-severity report can only have its status changed by the **issuer**
   — the owner can still view it (full identity included) and it still appears in their inbox,
   but any attempt to `PATCH` its status as `owner` must be rejected with 403. This exists
   specifically because an owner resolving/dismissing a sexual-misconduct or scam report about
   their own artist is a direct conflict of interest (the owner has a financial incentive to
   bury it) — same reasoning class as why `IgnoreQueryFilters()` is restricted to `IssuerOnly`
   handlers by default.

3. **Filing requires a real appointment** between the reporting client and the target
   artist/studio — the client picks from their own appointment history with that target, the
   same "prove the relationship, don't take a bare claim" posture `Review` already uses.
   **Deviation from `Review`'s eligibility, made deliberately**: do **not** restrict to
   `AppointmentStatus.Completed`, and do **not** exclude appointments that already have a report
   attached. Reasoning: a review evaluates finished work, so requiring completion makes sense;
   a conduct report is about an incident that can occur *during* an appointment the studio
   controls the status of, or a client may reasonably need to file more than one report (a
   second incident, or more detail) against the same visit. Gating on `Completed` here would let
   a studio dodge every report by simply never marking the appointment complete — do not
   replicate that filter. Every real appointment the client has with that artist/studio is
   eligible, regardless of status, and the picker should show each appointment's status so
   context is visible while filing.

4. **Notification is severity-based.** Filing a Standard-severity report just lands in the
   relevant in-app inbox (owner's + issuer's), same as `FeedbackReport`. Filing a **High**-
   severity report additionally fires an immediate email — to `Studio.OwnerEmail` and to the
   platform support inbox (`support@tattooos.co`, see Part 2) — using the same direct
   `INotificationService.SendEmailAsync` call pattern as `SubmitContactRequestHandler`, **not**
   the `NotificationLog`/`NotificationType`/`StudioNotificationPreference` system (that system is
   for opt-in, per-event client-facing notifications like appointment reminders; an owner must
   not be able to configure their way out of "someone reported sexual misconduct at your
   studio").

5. **The reporting client gets no persistent list view of their own filed reports** — this is a
   deliberate reading of "visible only by the issuer, owner and the artist being reported," not
   an oversight. They get a submission confirmation (toast) at filing time and nothing else.
   This intentionally diverges from `FeedbackReport`'s "mine" pattern
   (`GetMyFeedbackReportsQuery`) — do not add a client-facing `GET .../mine` endpoint for this
   entity. If a future prompt wants to add client visibility into report status, that's a new,
   separate decision — flag it in Out of Scope, don't build it now.

6. **Entity naming**: `ConductReport` (not `Report` — collides conceptually with `Report`
   already meaning revenue reporting in `Application/Reports/`/`ReportEndpoints.cs`, and with
   `FeedbackReport`). Folders/classes: `Application/ConductReports/`,
   `Pena_e_Arte.API/Endpoints/ConductReportEndpoints.cs`,
   `frontend/src/features/conduct-reports/`.

7. **`ConductReport` is a plain, non-tenant entity** — same family as `Review`/`FeedbackReport`/
   `AuditLogEntry`, **not** a `TenantEntity`, no EF query filter registered. Reasoning: like
   `Review`, the entity's relevant "studio" is the *target's* studio, which is unrelated to the
   filing client's own current `ICurrentTenant.StudioId` (a client may have appointment history —
   and therefore standing to report — at a studio that isn't their currently-active tenant
   context, per the existing Multi-Studio Client View feature #23). Resolve the target by slug
   with `IgnoreQueryFilters()`, exactly like `CreateArtistReviewHandler` already does. Owner and
   artist reads filter explicitly in the handler (`Where(r => r.StudioId == tenant.StudioId)` /
   `Where(r => r.ArtistId == myArtistId)`), same pattern as `GetMyStudioAuditLogHandler`. Issuer
   reads need no `IgnoreQueryFilters()` at all — there is no filter to bypass, same as
   `GetFeedbackReportsHandler`/`GetAuditLogHandler`. **Do not add a row to the
   `IgnoreQueryFilters()` approved-usages table for this feature** — call this out explicitly in
   the architecture.md update (Part 11) so a future reader doesn't assume one is missing.

8. **Category → severity is a static classification, not a stored column.** Seven categories,
   five High, two Standard:

   | Category | Severity | Maps to the user's own examples |
   |---|---|---|
   | `SexualMisconduct` | High | "sexually abuse" |
   | `Scam` | High | "scamming" |
   | `UnsafeHygienePractices` | High | (health/safety — sterilization, infection risk; tattoo-specific) |
   | `Harassment` | High | (verbal/non-sexual abuse, threats) |
   | `Discrimination` | High | (refusal of service / mistreatment on a protected basis) |
   | `PoorServiceQuality` | Standard | "bad service" |
   | `Other` | Standard | catch-all |

   Classification lives in one place — `Domain/Constants/ReportCategoryClassifier.cs` — not a
   column on the entity, so there is exactly one source of truth to update if the taxonomy
   changes later.

9. **Status values mirror `FeedbackStatus`** exactly: `Open`, `Reviewing`, `Resolved`,
   `Dismissed`. No new status is needed to express "escalated to issuer" — that's a permission
   check at write time (Decision 2), not a state the report itself carries.

10. **Industry-benchmark note, flagged per CLAUDE.md rule #6**: `architecture.md`'s existing
    "Industry-Standard Benchmark Set" is vertical booking-SaaS competitors (Vagaro, Fresha,
    Boulevard, etc.) plus tattoo-specific ones — **none of these publicly document a formal
    client-initiated "report this provider" trust & safety flow** the way general two-sided
    marketplaces do (Uber, Airbnb, Etsy, Upwork all have one, with category taxonomies and
    severity-gated escalation broadly similar to what's specified here). This is a genuine gap
    in the benchmark set for this specific feature class, not a case of picking the wrong
    comparator — flag it exactly like that in your Decisions Log entry, and add a short "Trust &
    Safety Reference Set" line to `architecture.md`'s Industry-Standard Benchmark Set section
    (Part 11) so the next trust/safety feature doesn't have to rediscover this.

---

## Part 1 — Domain + EF Core

### 1a. New `Pena_e_Arte.Domain/Enums/ReportCategory.cs`

```csharp
namespace Pena_e_Arte.Domain.Enums;

public enum ReportCategory
{
    Scam,
    SexualMisconduct,
    UnsafeHygienePractices,
    Harassment,
    Discrimination,
    PoorServiceQuality,
    Other,
}
```

### 1b. New `Pena_e_Arte.Domain/Enums/ReportStatus.cs`

```csharp
namespace Pena_e_Arte.Domain.Enums;

public enum ReportStatus
{
    Open,
    Reviewing,
    Resolved,
    Dismissed,
}
```

### 1c. New `Pena_e_Arte.Domain/Constants/ReportCategoryClassifier.cs`

```csharp
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Constants;

/// <summary>
/// Single source of truth for which ReportCategory values are High severity. Backs two
/// things: whether filing the report fires the immediate owner+issuer email (Part 6), and
/// whether an owner (as opposed to only the issuer) is permitted to change a report's status
/// (UpdateConductReportStatusHandler). Deliberately a static classification, not a stored
/// column on ConductReport — one place to change if the taxonomy is revised later.
/// </summary>
public static class ReportCategoryClassifier
{
    private static readonly HashSet<ReportCategory> HighSeverity =
    [
        ReportCategory.Scam,
        ReportCategory.SexualMisconduct,
        ReportCategory.UnsafeHygienePractices,
        ReportCategory.Harassment,
        ReportCategory.Discrimination,
    ];

    public static bool IsHighSeverity(ReportCategory category) => HighSeverity.Contains(category);
}
```

### 1d. New `Pena_e_Arte.Domain/Entities/ConductReport.cs`

```csharp
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// A client-filed report of misconduct against an artist or a studio. Deliberately NOT a
/// TenantEntity — same non-tenant shape as Review/FeedbackReport/AuditLogEntry: the entity's
/// relevant studio is the *target's* studio, unrelated to the filing client's own current
/// ICurrentTenant.StudioId (see architecture.md Decisions Log, 2026-08-22 entry, for why).
/// No EF Core global query filter is registered for this entity — see database.md's
/// IgnoreQueryFilters() table note, which explicitly says no new row was needed here.
/// </summary>
public class ConductReport
{
    private ConductReport() { }

    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>The reported studio. Always set — for an artist-target report this is the
    /// artist's own studio; for a studio-target report it's the studio itself.</summary>
    public Guid StudioId { get; private set; }

    /// <summary>Null when the report targets the studio generally rather than one artist.</summary>
    public Guid? ArtistId { get; private set; }

    /// <summary>The real appointment that establishes the reporter's relationship to the
    /// target. Required for both target kinds — see Decision 3 for why this is NOT restricted
    /// to AppointmentStatus.Completed the way Review.AppointmentId effectively is.</summary>
    public Guid AppointmentId { get; private set; }

    /// <summary>Never exposed in a response the reported artist can read — see IsAccessibleBy
    /// and ConductReportResponse mapping. Owner/issuer always see it.</summary>
    public Guid ReporterUserId { get; private set; }
    public string ReporterName { get; private set; } = string.Empty;

    public ReportCategory Category { get; private set; }
    public string Reason { get; private set; } = string.Empty;

    /// <summary>Optional evidence — screenshots/short clips, same R2 presign flow and cap (3)
    /// as FeedbackReport.AttachmentUrls.</summary>
    public List<string> AttachmentUrls { get; private set; } = [];

    public ReportStatus Status { get; private set; } = ReportStatus.Open;
    public string? ResolutionNote { get; private set; }
    public DateTime? ResolvedAt { get; private set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public static ConductReport ForArtist(
        Guid studioId,
        Guid artistId,
        Guid appointmentId,
        Guid reporterUserId,
        string reporterName,
        ReportCategory category,
        string reason,
        IReadOnlyList<string>? attachmentUrls = null) =>
        new()
        {
            StudioId = studioId,
            ArtistId = artistId,
            AppointmentId = appointmentId,
            ReporterUserId = reporterUserId,
            ReporterName = reporterName.Trim(),
            Category = category,
            Reason = reason.Trim(),
            AttachmentUrls = attachmentUrls?.ToList() ?? [],
        };

    public static ConductReport ForStudio(
        Guid studioId,
        Guid appointmentId,
        Guid reporterUserId,
        string reporterName,
        ReportCategory category,
        string reason,
        IReadOnlyList<string>? attachmentUrls = null) =>
        new()
        {
            StudioId = studioId,
            ArtistId = null,
            AppointmentId = appointmentId,
            ReporterUserId = reporterUserId,
            ReporterName = reporterName.Trim(),
            Category = category,
            Reason = reason.Trim(),
            AttachmentUrls = attachmentUrls?.ToList() ?? [],
        };

    public void UpdateStatus(ReportStatus status, string? resolutionNote)
    {
        Status = status;
        ResolutionNote = resolutionNote?.Trim();
        ResolvedAt = status is ReportStatus.Resolved or ReportStatus.Dismissed
            ? DateTime.UtcNow
            : null;
    }

    /// <summary>Issuer: always. Owner: any report targeting their own studio (StudioId match),
    /// regardless of severity — owners can always VIEW a high-severity report about their own
    /// artist, they just can't change its status (enforced separately, see
    /// ConductReportAuthorizationGuard.EnsureCanChangeStatus). Artist: only reports where
    /// ArtistId matches their own — never studio-target reports with no ArtistId.</summary>
    public bool IsReadableBy(Guid userId, Guid? callerStudioId, Guid? callerArtistId, string role)
    {
        if (string.Equals(role, "issuer", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(role, "owner", StringComparison.OrdinalIgnoreCase))
            return callerStudioId is not null && StudioId == callerStudioId;
        if (string.Equals(role, "artist", StringComparison.OrdinalIgnoreCase))
            return callerArtistId is not null && ArtistId == callerArtistId;
        return false;
    }
}
```

### 1e. `AppDbContext.cs` — add the DbSet, no query filter

```csharp
// --- Non-tenant, same family as Review/FeedbackReport/AuditLogEntry ---
public DbSet<ConductReport> ConductReports => Set<ConductReport>();
```

Configure inline in `OnModelCreating`, same style as the existing `FeedbackReport`/`Review`
blocks (not a separate `IEntityTypeConfiguration<ConductReport>` file):

```csharp
builder.Entity<ConductReport>(entity =>
{
    entity.ToTable("ConductReports");
    entity.HasKey(r => r.Id);
    entity.Property(r => r.Category).HasConversion<string>().HasMaxLength(32);
    entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);
    entity.Property(r => r.Reason).HasMaxLength(2000);
    entity.Property(r => r.ReporterName).HasMaxLength(200);
    entity.Property(r => r.ResolutionNote).HasMaxLength(2000);
    // AttachmentUrls: same JSON column conversion FeedbackReport.AttachmentUrls already uses —
    // copy that exact ValueComparer/conversion, don't hand-roll a new one.
    entity.HasIndex(r => r.StudioId);
    entity.HasIndex(r => r.ArtistId);
    entity.HasIndex(r => r.Status);
    // No HasQueryFilter() — see Decision 7 and the IgnoreQueryFilters() table note (Part 11).
});
```

Check `FeedbackReport`'s exact `AttachmentUrls` column configuration in the live
`AppDbContext.cs` (`entity.Property(r => r.AttachmentUrls)...`) and copy it verbatim for
`ConductReport.AttachmentUrls` — do not reinvent the JSON conversion.

### 1f. Migration

```bash
dotnet ef migrations add AddConductReports \
  --project Pena_e_Arte.Infrastructure \
  --startup-project Pena_e_Arte.API
```

No backfill needed — this is a brand-new table with no prior data.

---

## Part 2 — Constants + audit wiring

### 2a. Extract the shared support-inbox constant

New `Pena_e_Arte.Domain/Constants/PlatformContacts.cs`:

```csharp
namespace Pena_e_Arte.Domain.Constants;

/// <summary>Founder-confirmed public support inbox (2026-08-01). Not a secret. Originally a
/// private const inside SubmitContactRequestHandler; extracted here because
/// ReportArtistHandler/ReportStudioHandler need the same address for the high-severity alert
/// email (Part 6) and a second private copy would drift.</summary>
public static class PlatformContacts
{
    public const string SupportEmail = "support@tattooos.co";
}
```

Update `SubmitContactRequestHandler` to reference `PlatformContacts.SupportEmail` instead of its
own private `SupportEmail` const, and delete the now-redundant private const. This is the only
change to that file — do not touch anything else in it.

### 2b. Extend `Pena_e_Arte.Domain/Constants/AuditActions.cs`

Add to the existing `AuditActions` class:

```csharp
public const string ConductReportStatusUpdated = "ConductReport.StatusUpdated";
```

Add to the existing `AuditTargetTypes` class:

```csharp
public const string ConductReport = "ConductReport";
```

Only `UpdateConductReportStatusCommand` implements `IAuditableCommand` (Part 4) — filing a
report is ordinary user content creation, not an admin/trust action, same reasoning that
`SubmitFeedbackCommand` isn't audited either. Changing a report's status (especially dismissing
one) is exactly the kind of trust-sensitive action `AuditLogEntry` exists for.

---

## Part 3 — Contracts

### 3a. New `Pena_e_Arte.Contracts/Requests/FileArtistConductReportRequest.cs`

```csharp
namespace Pena_e_Arte.Contracts.Requests;

public record FileArtistConductReportRequest(
    Guid AppointmentId,
    string Category,
    string Reason,
    IReadOnlyList<string>? AttachmentUrls = null);
```

### 3b. New `Pena_e_Arte.Contracts/Requests/FileStudioConductReportRequest.cs`

```csharp
namespace Pena_e_Arte.Contracts.Requests;

public record FileStudioConductReportRequest(
    Guid AppointmentId,
    string Category,
    string Reason,
    IReadOnlyList<string>? AttachmentUrls = null);
```

(Two near-identical records rather than one shared one — mirrors `CreateReviewRequest` being
reused for both review targets via a nullable `AppointmentId`; here `AppointmentId` is required
for both, so there's no field-shape reason to force a shared type, and keeping them distinct
matches the two distinct endpoints/commands they map to.)

### 3c. New `Pena_e_Arte.Contracts/Requests/UpdateConductReportStatusRequest.cs`

```csharp
namespace Pena_e_Arte.Contracts.Requests;

public record UpdateConductReportStatusRequest(string Status, string? ResolutionNote = null);
```

### 3d. New `Pena_e_Arte.Contracts/Responses/ConductReportResponse.cs`

```csharp
namespace Pena_e_Arte.Contracts.Responses;

// ReporterUserId/ReporterName are nullable and populated ONLY when the caller is authorized to
// see reporter identity (owner, issuer). For an artist-scoped read they are always null — see
// GetMyConductReportsAsArtistHandler's redacted mapping. This redaction happens server-side in
// the handler's projection, never client-side — do not ship a version of this response that
// includes the fields and relies on the frontend to hide them.
public record ConductReportResponse(
    Guid Id,
    Guid StudioId,
    string StudioName,
    Guid? ArtistId,
    string? ArtistName,
    Guid AppointmentId,
    DateTime AppointmentDate,
    string Category,
    bool IsHighSeverity,
    string Reason,
    IReadOnlyList<string> AttachmentUrls,
    string Status,
    string? ResolutionNote,
    DateTime? ResolvedAt,
    DateTime CreatedAt,
    Guid? ReporterUserId,
    string? ReporterName);
```

### 3e. New `Pena_e_Arte.Contracts/Responses/Public/ReportableAppointmentResponse.cs`

```csharp
namespace Pena_e_Arte.Contracts.Responses.Public;

// Deliberately carries Status (unlike ReviewableAppointmentResponse) — the report-filing picker
// shows it so the client has context while choosing which visit a report relates to, since
// eligibility here is NOT restricted to Completed (Decision 3).
public record ReportableAppointmentResponse(Guid Id, DateTime Date, int DurationMinutes, string Status);
```

---

## Part 4 — Application layer

New folder `Pena_e_Arte.Application/ConductReports/`.

### 4a. `Commands/FileArtistConductReportCommand.cs`

```csharp
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ConductReports.Commands;

public record FileArtistConductReportCommand(
    string Slug,
    Guid AppointmentId,
    Guid ReporterUserId,
    string ReporterName,
    ReportCategory Category,
    string Reason,
    IReadOnlyList<string>? AttachmentUrls) : IRequest;

public class FileArtistConductReportValidator : AbstractValidator<FileArtistConductReportCommand>
{
    private const int MaxAttachments = 3;

    public FileArtistConductReportValidator(IR2Service r2)
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(20).MaximumLength(2000);
        RuleFor(x => x.ReporterName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AttachmentUrls)
            .Must(urls => urls == null || urls.Count <= MaxAttachments)
            .WithMessage($"You can attach up to {MaxAttachments} files.");
        RuleForEach(x => x.AttachmentUrls)
            .NotEmpty().MaximumLength(2048).Must(r2.IsR2Url)
            .WithMessage("AttachmentUrls must reference a valid storage URL.")
            .When(x => x.AttachmentUrls is not null);
    }
}

public class FileArtistConductReportHandler(
    IAppDbContext db,
    ConductReportNotifier notifier)
    : IRequestHandler<FileArtistConductReportCommand>
{
    public async Task Handle(FileArtistConductReportCommand command, CancellationToken ct)
    {
        // Approved: public portfolio lookup, same as CreateArtistReviewHandler.
        Artist artist = await db.Artists
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Slug == command.Slug && a.DeletedAt == null, ct)
            ?? throw new NotFoundException(nameof(Artist), command.Slug);

        // Approved: cross-tenant ownership check — identical join shape to
        // CreateArtistReviewHandler, EXCEPT no AppointmentStatus.Completed filter and no
        // "already reported" exclusion (Decision 3).
        var appointment = await db.Appointments
            .IgnoreQueryFilters()
            .Where(a => a.Id == command.AppointmentId)
            .Join(db.Clients.IgnoreQueryFilters(),
                  a => a.ClientId,
                  c => c.Id,
                  (a, c) => new { Appointment = a, ClientUserId = c.UserId })
            .FirstOrDefaultAsync(ct);

        bool ownedByReporterWithThisArtist = appointment is not null
            && appointment.Appointment.ArtistId == artist.Id
            && appointment.ClientUserId == command.ReporterUserId;

        // 404, not a generic error — same "don't reveal another client's appointment exists"
        // convention as RescheduleAppointmentHandler / CreateArtistReviewHandler.
        if (!ownedByReporterWithThisArtist)
            throw new NotFoundException(nameof(Appointment), command.AppointmentId);

        ConductReport report = ConductReport.ForArtist(
            artist.StudioId,
            artist.Id,
            command.AppointmentId,
            command.ReporterUserId,
            command.ReporterName,
            command.Category,
            command.Reason,
            command.AttachmentUrls);

        db.ConductReports.Add(report);
        await db.SaveChangesAsync(ct);

        await notifier.NotifyIfHighSeverityAsync(report, artist.StudioId, ct);
    }
}
```

### 4b. `Commands/FileStudioConductReportCommand.cs`

Same shape as 4a, but resolves `Studio` by slug (`IgnoreQueryFilters()`, same pattern as
`CreateStudioReviewHandler`), joins `Appointments` on `StudioId == studio.Id` instead of
`ArtistId`, and calls `ConductReport.ForStudio(...)`. Write this handler by directly mirroring
`CreateStudioReviewHandler`'s existing ownership-check shape (read the live file — it is the
studio-target sibling of the artist handler shown above), with the same two deltas as 4a: no
`Completed` filter, no dedup exclusion.

### 4c. `ConductReportNotifier.cs` (Part 6 has the full notification logic — declared here so
the two Commands above can reference it)

```csharp
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Pena_e_Arte.Application.ConductReports;

/// <summary>Fires the immediate owner+issuer alert email for a High-severity report (Decision
/// 4). Standard-severity reports are a no-op here — they surface purely via the in-app inboxes
/// (Part 5 queries). Deliberately bypasses NotificationLog/NotificationType/
/// StudioNotificationPreference — same direct INotificationService.SendEmailAsync call shape as
/// SubmitContactRequestHandler — an owner must not be able to opt out of this via notification
/// preferences (see Decision 4 for the full reasoning).</summary>
public class ConductReportNotifier(IAppDbContext db, INotificationService notifications)
{
    public async Task NotifyIfHighSeverityAsync(ConductReport report, Guid studioId, CancellationToken ct)
    {
        if (!ReportCategoryClassifier.IsHighSeverity(report.Category)) return;

        Studio? studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == studioId, ct);
        if (studio is null) return;

        string subject = $"[Urgent] {report.Category} report filed at {studio.Name}";
        string body =
            $"<p>A <strong>{report.Category}</strong> conduct report was just filed" +
            (report.ArtistId is not null ? " against an artist" : "") +
            $" at <strong>{studio.Name}</strong>.</p>" +
            "<p>Review it in the dashboard as soon as possible.</p>";

        await notifications.SendEmailAsync(studio.OwnerEmail, subject, body, ct: ct);
        await notifications.SendEmailAsync(PlatformContacts.SupportEmail, subject, body, ct: ct);
    }
}
```

Register `ConductReportNotifier` in `Program.cs`'s DI setup (transient or scoped — match
whatever lifetime the other Application-layer helper classes use, e.g. how `FeedbackAccessGuard`
is registered, if it's registered at all as a service vs. used as a static/internal helper —
check the live `Program.cs`/DI extension file for the exact convention before deciding).

### 4d. `ConductReportAuthorizationGuard.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ConductReports;

/// <summary>Shared by every handler that loads a single ConductReport by id — centralizes both
/// read authorization (ConductReport.IsReadableBy) and the severity-gated write rule (Decision
/// 2), the same reasoning FeedbackAccessGuard gives for FeedbackReport: one place, not N copies
/// that can drift.</summary>
internal static class ConductReportAuthorizationGuard
{
    public static async Task<ConductReport> LoadReadableReportAsync(
        IAppDbContext db, Guid reportId, ICurrentUser user, ICurrentTenant tenant, CancellationToken ct)
    {
        ConductReport report = await db.ConductReports.FirstOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw new NotFoundException(nameof(ConductReport), reportId);

        Guid? callerArtistId = null;
        if (string.Equals(user.Role, "artist", StringComparison.OrdinalIgnoreCase))
        {
            Artist? me = await db.Artists.FirstOrDefaultAsync(a => a.UserId == user.UserId, ct);
            callerArtistId = me?.Id;
        }

        Guid? callerStudioId = tenant.IsSet ? tenant.StudioId : null;

        if (!report.IsReadableBy(user.UserId, callerStudioId, callerArtistId, user.Role))
            throw new ForbiddenException("You do not have access to this report.");

        return report;
    }

    /// <summary>Decision 2: owner may change status only for Standard-severity reports about
    /// their own studio; issuer may always change status; artist may never change status.</summary>
    public static void EnsureCanChangeStatus(ConductReport report, ICurrentUser user)
    {
        if (string.Equals(user.Role, "issuer", StringComparison.OrdinalIgnoreCase)) return;

        if (string.Equals(user.Role, "owner", StringComparison.OrdinalIgnoreCase)
            && !ReportCategoryClassifier.IsHighSeverity(report.Category))
            return;

        throw new ForbiddenException(
            "High-severity reports can only be resolved by platform staff.");
    }
}
```

### 4e. `Commands/UpdateConductReportStatusCommand.cs`

```csharp
using FluentValidation;
using MediatR;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ConductReports.Commands;

public record UpdateConductReportStatusCommand(Guid ReportId, ReportStatus Status, string? ResolutionNote)
    : IRequest, IAuditableCommand
{
    public string AuditAction => AuditActions.ConductReportStatusUpdated;
    public string AuditTargetType => AuditTargetTypes.ConductReport;
    public Guid AuditTargetId => ReportId;
}

public class UpdateConductReportStatusValidator : AbstractValidator<UpdateConductReportStatusCommand>
{
    public UpdateConductReportStatusValidator()
    {
        RuleFor(x => x.ResolutionNote).MaximumLength(2000);
    }
}

public class UpdateConductReportStatusHandler(
    IAppDbContext db, ICurrentUser user, ICurrentTenant tenant)
    : IRequestHandler<UpdateConductReportStatusCommand>
{
    public async Task Handle(UpdateConductReportStatusCommand command, CancellationToken ct)
    {
        ConductReport report = await ConductReportAuthorizationGuard.LoadReadableReportAsync(
            db, command.ReportId, user, tenant, ct);

        ConductReportAuthorizationGuard.EnsureCanChangeStatus(report, user);

        report.UpdateStatus(command.Status, command.ResolutionNote);
        await db.SaveChangesAsync(ct);
    }
}
```

Note `AuditLogBehavior` reads `AuditStudioId` if present on the command, else falls back to
`ICurrentTenant.StudioId` — since this command doesn't carry the target studio id directly and
the acting user's own tenant (owner) IS the right studio to attribute in the common case, the
default fallback is correct for the owner path; for the issuer path (no tenant set) it correctly
falls back to `null` (a platform-wide action with no single-studio attribution at the JWT level)
— that's an acceptable trade-off already accepted elsewhere (see `AuditLogBehavior`'s own doc
comment); if you want issuer status-changes attributed to the actual report's studio instead,
add an explicit `AuditStudioId => null` override is wrong here — instead have the handler look
up `report.StudioId` before dispatch is not possible from a command record; simplest correct fix
is to add `Guid? AuditStudioId => null;` is the default anyway, so leave it — but flag this in
your Decisions Log write-up as a known minor gap (issuer-authored audit rows for this action
carry `StudioId = null` instead of the report's actual studio) rather than silently "fixing" it
with an ad-hoc pattern not used elsewhere; a clean fix requires either a second constructor
parameter carrying the resolved studio id or a small refactor to `IAuditableCommand`, both out
of scope for this prompt.

### 4f. `Queries/GetMyStudioConductReportsQuery.cs` (owner)

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ConductReports.Queries;

public record GetMyStudioConductReportsQuery(string? Status = null) : IRequest<List<ConductReportResponse>>;

public class GetMyStudioConductReportsHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<GetMyStudioConductReportsQuery, List<ConductReportResponse>>
{
    public async Task<List<ConductReportResponse>> Handle(
        GetMyStudioConductReportsQuery query, CancellationToken ct)
    {
        IQueryable<ConductReport> q = db.ConductReports
            .Where(r => r.StudioId == tenant.StudioId)
            .OrderByDescending(r => r.CreatedAt);

        if (!string.IsNullOrEmpty(query.Status) && Enum.TryParse(query.Status, true, out ReportStatus status))
            q = q.Where(r => r.Status == status);

        // Join Studio/Artist/Appointment for display fields, full reporter identity included
        // (owner is always authorized to see it — Decision 1).
        return await ConductReportProjections.ToFullResponseAsync(q, db, ct);
    }
}
```

### 4g. `Queries/GetMyConductReportsAsArtistQuery.cs` (artist — redacted)

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ConductReports.Queries;

public record GetMyConductReportsAsArtistQuery : IRequest<List<ConductReportResponse>>;

public class GetMyConductReportsAsArtistHandler(IAppDbContext db, ICurrentUser user)
    : IRequestHandler<GetMyConductReportsAsArtistQuery, List<ConductReportResponse>>
{
    public async Task<List<ConductReportResponse>> Handle(
        GetMyConductReportsAsArtistQuery query, CancellationToken ct)
    {
        // Tenant-filtered by the caller's own JWT tenant — an artist only ever authenticates
        // within their own studio, so this naturally resolves their own Artist row.
        Artist? me = await db.Artists.FirstOrDefaultAsync(a => a.UserId == user.UserId, ct);
        if (me is null) return [];

        IQueryable<ConductReport> q = db.ConductReports
            .Where(r => r.ArtistId == me.Id)
            .OrderByDescending(r => r.CreatedAt);

        // Redacted — reporter identity fields always null (Decision 1). Do NOT reuse
        // ToFullResponseAsync here even though the shape matches; that helper is only for
        // owner/issuer callers.
        return await ConductReportProjections.ToRedactedResponseAsync(q, db, ct);
    }
}
```

### 4h. `Queries/GetConductReportsQuery.cs` (issuer, cross-tenant)

Same shape as `GetFeedbackReportsQuery` — lives in `Application/ConductReports/Queries/`
(alongside this entity's other queries, mirroring where `FeedbackReport`'s issuer query lives
relative to its entity, not `Application/Platform/Queries/`), filterable by `category`, `status`,
and `studioId`, no `IgnoreQueryFilters()` needed (Decision 7), full reporter identity included.

```csharp
public record GetConductReportsQuery(
    string? Category = null, string? Status = null, Guid? StudioId = null)
    : IRequest<List<ConductReportResponse>>;
```

Implement `GetConductReportsHandler` following `GetFeedbackReportsHandler`'s exact filter-chain
shape.

### 4i. `ConductReportProjections.cs` — shared response-mapping helper

Both the full (owner/issuer) and redacted (artist) projections need the same join against
`Studio`/`Artist`/`Appointment` for display fields (`StudioName`, `ArtistName`,
`AppointmentDate`, `IsHighSeverity`) — write one internal static class with two entry points
(`ToFullResponseAsync`, `ToRedactedResponseAsync`) that share the join logic and differ only in
whether `ReporterUserId`/`ReporterName` are populated or forced to `null`. This mirrors
`GetFeedbackReportsHandler.ToResponse`'s "shared `Expression<Func<...>>` so EF Core can
translate it into SQL" approach — keep the projection as an `Expression`, not a
`Select(...).ToList()` followed by manual materialization, so the query stays translatable and
doesn't pull full entities into memory.

### 4j. `Application/Public/Queries/GetReportableArtistAppointmentsQuery.cs`

Mirror `GetReviewableArtistAppointmentsQuery` exactly (same slug lookup, same cross-tenant join
against `Clients` filtered to the caller's own `UserId`), with the two Decision-3 deltas: drop
the `a.Status == AppointmentStatus.Completed` filter and the "not already reviewed" `Where`
clause, and project `Status.ToString()` into the new `ReportableAppointmentResponse` instead of
`ReviewableAppointmentResponse`.

### 4k. `Application/Public/Queries/GetReportableStudioAppointmentsQuery.cs`

Same relationship to `GetReviewableStudioAppointmentsQuery` as 4j has to the artist version —
read the live studio-target query and mirror it with the same two deltas.

---

## Part 5 — API endpoints

### 5a. Extend `Pena_e_Arte.API/Endpoints/PublicEndpoints.cs`

Inside the existing `group = app.MapGroup("/api/v1/public")` block, add four routes immediately
after the existing review routes (keep the file's existing route ordering/grouping style —
reviews, then reports, then view-tracking, etc.):

```csharp
group.MapPost("/artists/{slug}/reports", FileArtistConductReport)
     .RequireAuthorization("ClientOnly").RequireRateLimiting("public-write");
group.MapPost("/studios/{slug}/reports", FileStudioConductReport)
     .RequireAuthorization("ClientOnly").RequireRateLimiting("public-write");
group.MapGet("/artists/{slug}/reports/reportable-appointments", GetReportableArtistAppointments)
     .RequireAuthorization("ClientOnly").RequireRateLimiting("public-read");
group.MapGet("/studios/{slug}/reports/reportable-appointments", GetReportableStudioAppointments)
     .RequireAuthorization("ClientOnly").RequireRateLimiting("public-read");
```

Note the policy is `ClientOnly`, not `ClientAndAbove` like reviews — filing a conduct report is
scoped to actual clients per the feature's own definition (Decision-adjacent — this is a
straightforward reading of "allow the client to report," not a separate decision to relitigate).

Handler methods, following the exact `CreateArtistReview` bridging pattern already in this file
(`ClaimsPrincipal` → `Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!)` for the
reporter id, `FindFirstValue(ClaimTypes.Name) ?? FindFirstValue(ClaimTypes.GivenName) ??
"Anonymous"` for the reporter name):

```csharp
private static async Task<IResult> FileArtistConductReport(
    string slug,
    FileArtistConductReportRequest body,
    ClaimsPrincipal user,
    ISender mediator,
    CancellationToken ct)
{
    Guid reporterId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    string reporterName = user.FindFirstValue(ClaimTypes.Name)
                       ?? user.FindFirstValue(ClaimTypes.GivenName)
                       ?? "Anonymous";

    ReportCategory category = Enum.Parse<ReportCategory>(body.Category, ignoreCase: true);

    await mediator.Send(
        new FileArtistConductReportCommand(
            slug, body.AppointmentId, reporterId, reporterName, category, body.Reason,
            body.AttachmentUrls),
        ct);
    return Results.NoContent();
}
```

`FileStudioConductReport`, `GetReportableArtistAppointments`, `GetReportableStudioAppointments`
follow the same bridging shape as their review-endpoint siblings in this same file — mirror them
directly rather than inventing a new shape.

### 5b. New `Pena_e_Arte.API/Endpoints/ConductReportEndpoints.cs`

```csharp
using MediatR;
using Pena_e_Arte.Application.ConductReports.Commands;
using Pena_e_Arte.Application.ConductReports.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.API.Endpoints;

public static class ConductReportEndpoints
{
    public static void MapConductReportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/studios/me/conduct-reports", GetMyStudioConductReports)
           .RequireAuthorization("OwnerOnly");

        app.MapGet("/api/v1/artists/me/conduct-reports", GetMyConductReportsAsArtist)
           .RequireAuthorization("ArtistAndAbove");

        app.MapPatch("/api/v1/conduct-reports/{id:guid}/status", UpdateConductReportStatus)
           .RequireAuthorization("OwnerOnly");

        RouteGroupBuilder platform = app.MapGroup("/api/v1/platform/conduct-reports")
            .RequireAuthorization("IssuerOnly");
        platform.MapGet("", GetConductReports);
    }

    private static async Task<IResult> GetMyStudioConductReports(
        ISender mediator, CancellationToken ct, string? status = null)
    {
        List<ConductReportResponse> result =
            await mediator.Send(new GetMyStudioConductReportsQuery(status), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetMyConductReportsAsArtist(ISender mediator, CancellationToken ct)
    {
        List<ConductReportResponse> result =
            await mediator.Send(new GetMyConductReportsAsArtistQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateConductReportStatus(
        Guid id, UpdateConductReportStatusRequest request, ISender mediator, CancellationToken ct)
    {
        ReportStatus status = Enum.Parse<ReportStatus>(request.Status, ignoreCase: true);
        await mediator.Send(
            new UpdateConductReportStatusCommand(id, status, request.ResolutionNote), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetConductReports(
        ISender mediator, CancellationToken ct,
        string? category = null, string? status = null, Guid? studioId = null)
    {
        List<ConductReportResponse> result =
            await mediator.Send(new GetConductReportsQuery(category, status, studioId), ct);
        return Results.Ok(result);
    }
}
```

Note `UpdateConductReportStatus` is gated `OwnerOnly` at the policy level (which includes
issuer), with the severity split enforced inside the handler via
`ConductReportAuthorizationGuard.EnsureCanChangeStatus` — this matches how `RespondToReview` is
`OwnerOnly` at the route while finer-grained rules live in the handler layer throughout this
codebase.

### 5c. `Program.cs`

Add `app.MapConductReportEndpoints();` next to the existing `app.MapFeedbackEndpoints();` /
`app.MapReviewEndpoints();` calls. `PublicEndpoints.cs`'s new routes need no separate
registration — they're inside the existing `MapPublicEndpoints()` call already wired.

---

## Part 6 — Notifications (severity-based alerting)

Covered functionally in Part 4c (`ConductReportNotifier`). Checklist for this part:

- [ ] `ConductReportNotifier` registered in DI (`Program.cs` or the relevant
      `ServiceCollectionExtensions` file — match the existing convention for Application-layer
      helper classes; if none of the comparable helpers (`FeedbackAccessGuard`,
      `ConductReportAuthorizationGuard`) are DI-registered because they're static, consider
      making `ConductReportNotifier` static too for consistency, taking `IAppDbContext` and
      `INotificationService` as method parameters instead of constructor-injected fields — pick
      whichever matches the codebase's actual convention once you've checked, and note which you
      picked and why in your summary).
- [ ] `PlatformContacts.SupportEmail` extracted per Part 2a, `SubmitContactRequestHandler`
      updated to use it, no behavior change to that handler otherwise.
- [ ] Verify `INotificationService.SendEmailAsync(to, subject, body, ct)` (the 4-arg overload,
      no `replyTo`) is the correct overload to call here — there is no submitter to reply to in
      this context (unlike the contact form), so the shorter overload is correct; confirm it
      exists as shown in `Pena_e_Arte.Domain/Interfaces/INotificationService.cs`.
- [ ] Standard-severity reports must NOT trigger any email — verify with a unit test that stubs
      `INotificationService` and asserts zero calls for `PoorServiceQuality`/`Other`.
- [ ] Never log the reporter's name, email, or the report's free-text reason at any log level —
      same PII discipline as `SubmitContactRequestHandler`'s own comment ("Never log the
      name/email/message — PII — rule 3; log only that one was relayed").

---

## Part 7 — Backend tests

Unit tests (mirror the existing `Feedback`/`Reviews` test project structure and naming
convention `MethodName_Scenario_ExpectedResult`):

- `ReportCategoryClassifierTests` — every High category returns true, every Standard category
  returns false, exhaustive over all seven enum values (use `Enum.GetValues<ReportCategory>()`
  in a `[Theory]` rather than hand-listing seven `[Fact]`s, so a future added category is forced
  to get a classification or the test itself won't compile/will silently miss it — actually
  since it's data-driven, add an explicit "every category has been considered" assertion, e.g.
  count High + Standard == total enum count, so a newly added category with no classification
  decision is caught by this test rather than silently defaulting to Standard).
- `FileArtistConductReportHandlerTests` — valid same-client-same-artist appointment succeeds;
  another client's appointment throws `NotFoundException`; appointment with a **different**
  artist throws `NotFoundException`; an appointment that is **not** `Completed` still succeeds
  (this is the one that would catch an accidental copy-paste of Review's `Completed` filter —
  write it explicitly, don't skip it); filing a second report against the same appointment
  succeeds (no dedup guard, unlike reviews — assert this explicitly too, for the same
  copy-paste-guard reason).
- `FileStudioConductReportHandlerTests` — same shape as above, studio-target.
- `UpdateConductReportStatusHandlerTests` — owner changing a Standard-severity report they own
  succeeds; owner attempting to change a High-severity report throws `ForbiddenException`;
  issuer changing any severity succeeds; owner attempting to change another studio's report
  throws (via the guard's `NotFoundException`/`ForbiddenException` from
  `LoadReadableReportAsync`); artist attempting to change status throws.
- `ConductReportAuthorizationGuardTests` — `IsReadableBy` matrix: issuer/any → true; owner/own
  studio → true; owner/other studio → false; artist/own reports → true; artist/other artist's
  reports → false; artist/studio-target report (ArtistId null) → false.
- `ConductReportNotifierTests` — High severity sends two emails (owner + support inbox);
  Standard severity sends zero.
- `GetMyConductReportsAsArtistHandlerTests` — response never contains `ReporterUserId`/
  `ReporterName` (assert both are null on every item), even when the underlying entity has them
  set — this is the single most important test in this whole feature; do not skip it.

Integration tests (`[Collection("Database")]`, per the shared-DB isolation rule in
`architecture.md`'s Decisions Log — scope every assertion to ids the test itself created, never
an absolute count):

- End-to-end file → owner reads it → owner resolves it (Standard) → status persists.
- End-to-end file (High severity) → owner attempts resolve → 403 → issuer resolves → status
  persists → `AuditLogEntry` row exists with `Action == AuditActions.ConductReportStatusUpdated`.
- Artist reads their own reports via the real endpoint (not just the handler in isolation) and
  the HTTP response JSON has no `reporterUserId`/`reporterName` keys with non-null values.

---

## Part 8 — Frontend

### 8a. New `frontend/src/features/conduct-reports/conductReports.types.ts`

```typescript
export const REPORT_CATEGORY = {
  Scam:                   "Scam",
  SexualMisconduct:       "SexualMisconduct",
  UnsafeHygienePractices: "UnsafeHygienePractices",
  Harassment:             "Harassment",
  Discrimination:         "Discrimination",
  PoorServiceQuality:     "PoorServiceQuality",
  Other:                  "Other",
} as const;
export type ReportCategory = typeof REPORT_CATEGORY[keyof typeof REPORT_CATEGORY];

export const REPORT_STATUS = {
  Open:      "Open",
  Reviewing: "Reviewing",
  Resolved:  "Resolved",
  Dismissed: "Dismissed",
} as const;
export type ReportStatus = typeof REPORT_STATUS[keyof typeof REPORT_STATUS];

// Mirror ReportCategoryClassifier.cs exactly — keep in sync if the backend taxonomy changes.
export const HIGH_SEVERITY_CATEGORIES: ReadonlySet<ReportCategory> = new Set([
  "Scam", "SexualMisconduct", "UnsafeHygienePractices", "Harassment", "Discrimination",
]);

export interface ConductReportResponse {
  id: string;
  studioId: string;
  studioName: string;
  artistId: string | null;
  artistName: string | null;
  appointmentId: string;
  appointmentDate: string;
  category: ReportCategory;
  isHighSeverity: boolean;
  reason: string;
  attachmentUrls: string[];
  status: ReportStatus;
  resolutionNote: string | null;
  resolvedAt: string | null;
  createdAt: string;
  reporterUserId: string | null;
  reporterName: string | null;
}

export interface ReportableAppointment {
  id: string;
  date: string;
  durationMinutes: number;
  status: string;
}
```

### 8b. Extend `features/public/publicApi.ts`

Add, alongside the existing review endpoints:

```typescript
getReportableArtistAppointments: builder.query<ReportableAppointment[], string>({
  query: (slug) => `artists/${slug}/reports/reportable-appointments`,
}),
getReportableStudioAppointments: builder.query<ReportableAppointment[], string>({
  query: (slug) => `studios/${slug}/reports/reportable-appointments`,
}),
fileArtistConductReport: builder.mutation<void, { slug: string; body: FileConductReportArgs }>({
  query: ({ slug, body }) => ({ url: `artists/${slug}/reports`, method: "POST", body }),
}),
fileStudioConductReport: builder.mutation<void, { slug: string; body: FileConductReportArgs }>({
  query: ({ slug, body }) => ({ url: `studios/${slug}/reports`, method: "POST", body }),
}),
```

Export the four generated hooks from this file's existing export block. `FileConductReportArgs`
is `{ appointmentId: string; category: ReportCategory; reason: string; attachmentUrls?: string[] }`
— define it in `conductReports.types.ts` and import it here.

### 8c. New `frontend/src/features/conduct-reports/conductReportsApi.ts`

RTK Query slice for the authenticated owner/artist/issuer surfaces (separate from `publicApi`,
mirroring `feedbackApi.ts`'s split from the public review endpoints):

```typescript
export const conductReportsApi = createApi({
  reducerPath: "conductReportsApi",
  baseQuery,
  tagTypes: ["ConductReport"],
  endpoints: (builder) => ({
    getMyStudioConductReports: builder.query<ConductReportResponse[], { status?: string } | void>({
      query: (args) => ({ url: "studios/me/conduct-reports", params: args ?? undefined }),
      providesTags: ["ConductReport"],
    }),
    getMyConductReportsAsArtist: builder.query<ConductReportResponse[], void>({
      query: () => "artists/me/conduct-reports",
      providesTags: ["ConductReport"],
    }),
    getPlatformConductReports: builder.query<ConductReportResponse[], Record<string, string> | void>({
      query: (args) => ({ url: "platform/conduct-reports", params: args ?? undefined }),
      providesTags: ["ConductReport"],
    }),
    updateConductReportStatus: builder.mutation<void, { id: string; status: string; resolutionNote?: string }>({
      query: ({ id, ...body }) => ({ url: `conduct-reports/${id}/status`, method: "PATCH", body }),
      invalidatesTags: ["ConductReport"],
    }),
  }),
});
```

Register `conductReportsApi.reducer`/`.middleware` in `app/store.ts`, following the exact
pattern every other feature api slice already uses there.

### 8d. New `features/conduct-reports/components/ConductReportDialog.tsx`

Controlled dialog (`open`/`onOpenChange` props, exactly like `FeedbackDialog`), taking a
`target: { kind: "artist" | "studio"; slug: string; name: string }` prop so one component serves
both entry points. Build it as a close structural cousin of `FeedbackDialog.tsx`:

- `react-hook-form` + `zod` schema: `category` (enum of the 7 values), `appointmentId` (populated
  from the reportable-appointments query, required), `reason` (min 20 / max 2000, matching the
  backend validator).
- Category `Select` — group or visually flag the five High-severity options (e.g. a small
  warning-color dot) so a client understands these get escalated immediately; label each
  category in plain language ("Scam or fraud", "Sexual misconduct or abuse", "Unsafe or unsanitary
  practices", "Harassment", "Discrimination", "Poor service quality", "Other").
- Appointment picker — a `Select` populated from
  `useGetReportableArtistAppointmentsQuery(slug)` / `useGetReportableStudioAppointmentsQuery(slug)`
  depending on `target.kind`, each option showing date + status (e.g. "Aug 20, 2026 — Confirmed").
  Empty state: "You don't have any appointments with {target.name} yet" with the submit button
  disabled — reports require the real-appointment relationship (Decision 3), this is not
  optional client-side validation, it mirrors what the server will reject anyway.
- Reason `Textarea`, same 2000 char counter treatment `FeedbackDialog` already has.
- Attachments — reuse `usePresignedUpload()` + the same `Attachment` state/upload UI
  `FeedbackDialog.tsx` already implements (image/video accept types, `MAX_ATTACHMENTS = 3`,
  preview thumbnails, per-file upload status) rather than re-deriving it; extract the shared
  attachment-picker UI into `shared/components/AttachmentPicker.tsx` if that's a clean lift, or
  duplicate the block with a comment pointing at `FeedbackDialog.tsx` as the source of truth if
  extraction turns out to be too invasive for this prompt's scope — pick whichever keeps this
  prompt's diff focused; note which you chose in your summary.
- Submit → `useFileArtistConductReportMutation` / `useFileStudioConductReportMutation`
  (`publicApi`) with `{ appointmentId, category, reason, attachmentUrls }`.
- On success: toast confirmation ("Report submitted. Our platform team will review it.") and
  close the dialog — **do not** show the submitted report back to the user in any list (Decision
  5 — there is nothing to show them; don't build a local optimistic "your report" card either).

### 8e. Trigger placement

In `features/public/components/ArtistPortfolioPage.tsx` and `StudioPortfolioPage.tsx`, add a
small, low-emphasis "Report this artist" / "Report this studio" trigger near `ReviewSection`
(a text-button style, not a prominent CTA — this should be discoverable, not front-and-center,
matching how trust & safety report actions are typically styled on marketplace platforms: present
but quiet). Gate visibility with the **exact same auth-gating `ReviewSection.tsx` already uses**
for its own "write a review" trigger — read that component's full source (only the first ~120
lines were sketched during this prompt's own research) and match its pattern precisely rather
than re-deriving a new one; do not show the trigger to anonymous visitors or non-client roles.

### 8f. Owner-facing page — `features/conduct-reports/components/ConductReportsPage.tsx`

One page component, mounted at a new route `/conduct-reports`
(`RoleGuard allowedRoles={[Role.Artist, Role.Owner]}` — **not** `Role.Issuer`, which gets its
own dedicated platform page, 8g below), branching internally on the caller's role:

- **Owner view**: `useGetMyStudioConductReportsQuery`, full table/card list (follow
  `FeedbackInboxPage.tsx`'s `DataTable`-with-`mobileCard` convention per `conventions.md`'s
  mobile rules — this list has more than 3 meaningful columns: category, target, status, date,
  severity), status filter chips, full reporter identity shown. For a High-severity row, replace
  the status-change controls with a locked/disabled state and copy explaining escalation
  ("Escalated to platform review — only Pena e Artë staff can close this report"), never a
  silently-disabled control with no explanation.
- **Artist view**: `useGetMyConductReportsAsArtistQuery`, same list styling, no status-change
  controls at all (artists never change status — Decision 2), reporter identity fields render as
  "Anonymous" or similar neutral copy, never blank/undefined.

### 8g. Issuer-facing page — `features/conduct-reports/components/ConductReportInboxPage.tsx`

Mirrors `FeedbackInboxPage.tsx` structurally: `useGetPlatformConductReportsQuery` with
category/status/studio filters, full reporter identity, status-change controls always enabled
(issuer can always resolve). Mounted at `/platform/conduct-reports`
(`RoleGuard allowedRoles={[Role.Issuer]}`, added to the `platform` children array in
`router.tsx` alongside the existing `feedback` entry).

### 8h. Navigation

- `OwnerLayout.tsx`: new nav item, label **"Conduct Reports"** (not "Reports" — that label is
  already the revenue-reporting nav item; picking a distinct label here is a correctness
  requirement, not a style preference), `href: "/conduct-reports"`, open-count badge sourced from
  `useGetMyStudioConductReportsQuery({ status: "Open" })`, mirroring the `Feedback` nav item's
  exact badge-computation pattern in `IssuerLayout.tsx`.
- `ArtistLayout.tsx`: new nav item, label **"Reports About Me"**, `href: "/conduct-reports"`,
  same open-count badge pattern sourced from `useGetMyConductReportsAsArtistQuery`.
- `IssuerLayout.tsx`: new nav item, label **"Conduct Reports"**, `href: "/platform/conduct-reports"`,
  badge sourced from `useGetPlatformConductReportsQuery({ status: "Open" })`.
- Add `data-tour="owner-conduct-reports-nav"` / `data-tour="artist-conduct-reports-nav"` /
  `data-tour="issuer-conduct-reports-nav"` to each new nav link — required by Part 10 below.

### 8i. `router.tsx`

Add the `/conduct-reports` route (Owner + Artist) at the same nesting level as `dashboard`/
`schedule`, and the `/platform/conduct-reports` route inside the existing `platform` children
array, both wrapped in `<ErrorBoundary>` exactly like every sibling route already is.

---

## Part 9 — Frontend tests

Mirror `FeedbackDialog.test.tsx` / `FeedbackInboxPage.test.tsx` structurally:

- `ConductReportDialog.test.tsx` — category select renders all 7 options; submit disabled until
  an appointment is picked; empty-appointments state disables submit with the right copy;
  attachment cap enforced at 3; successful submit calls the right mutation with the right body
  and shows the confirmation toast, dialog closes, **no** report content is rendered back to the
  user afterward (assert the DOM has no leftover report card — this directly tests Decision 5).
- `ConductReportsPage.test.tsx` — owner view: High-severity row shows locked/escalated copy, no
  status buttons; Standard-severity row has working status buttons. Artist view: reporter name
  never rendered as anything but the neutral placeholder, even when the mocked API response (by
  test-author mistake or a future backend regression) includes a non-null `reporterName` — this
  test should assert the frontend **also** never displays it even if the payload leaked it,
  belt-and-suspenders on top of the backend-side redaction test in Part 7.
- `ConductReportInboxPage.test.tsx` — issuer view: status controls always enabled regardless of
  severity; category/status filters narrow the list.

---

## Part 10 — Help sync (CLAUDE.md rule #7 — not optional)

- `frontend/src/features/help/helpContent.ts`:
  - New Client article: "Report an artist or studio" — `route: "/artist/:slug"` or a generic
    entry pointing at both portfolio page types, `keywords` including "report", "complaint",
    "abuse", "scam", "unsafe", "harassment", `steps` walking through opening the dialog from the
    artist/studio page, picking the appointment + category, writing the reason, and what happens
    next (platform review, no reply channel shown in-app). `tips`: mention that high-severity
    categories get escalated immediately.
  - New Owner article: "Review conduct reports" — covers the `/conduct-reports` page, the
    severity split (why some reports are locked for platform-only resolution), and the open-count
    badge.
  - New Artist article: "See reports filed about you" — covers `/conduct-reports` artist view,
    explicitly reassures that the reporting client's identity is not shown to them.
  - (Issuer doesn't need a new dedicated article if the existing platform-admin guidance already
    covers "moderation inbox" generically — check the live file; add one only if issuer guides
    are itemized per-feature the way client/owner/artist ones are.)
- `frontend/public/user-manual/index.html`: add matching sections for the three new articles
  above, keeping this file and `helpContent.ts` in sync per the standing rule in the "In-App Help
  Menu" architecture.md entry.
- Onboarding tours — append (don't insert mid-sequence) one new step to each of
  `ownerTour.ts`, `artistTour.ts` targeting the new `data-tour` nav attributes from Part 8h.
  `issuerTour.ts` likewise if issuer's tour itemizes individual platform nav items the way
  `artistTourSteps` does (check the live file's existing granularity before deciding whether a
  new step is warranted vs. redundant with an existing generic "platform nav" step).

---

## Part 11 — Architecture doc updates

### 11a. Feature Module Map (`docs/claude/architecture.md`)

Add row **#37**:

```
| 37 | Client Conduct Reports | `ConductReport` (non-tenant, no query filter — same shape as Review/FeedbackReport/AuditLogEntry) | None — direct email alert for High severity, same INotificationService.SendEmailAsync path as the contact form | Per-tenant (owner read), Per-user (artist read, redacted), Issuer-level (cross-tenant read + High-severity resolution) |
```

### 11b. IgnoreQueryFilters() table

**Do not add a row.** Add one sentence immediately after the existing table (or wherever the
table's surrounding prose already clarifies non-tenant entities that don't need an entry) making
this explicit: `ConductReport` needs no `IgnoreQueryFilters()` anywhere — it has no query filter
registered at all, same as `Review`/`FeedbackReport`/`AuditLogEntry`. This sentence exists purely
to preempt a future reader assuming an entry was missed.

### 11c. Industry-Standard Benchmark Set

Append a short new subsection under the existing benchmark set, per Decision 10:

```
Trust & Safety Reference Set (added 2026-08-22, for client-initiated report/moderation
features specifically — the vertical booking-SaaS comparators above don't publicly document
this pattern):
  Uber, Airbnb, Etsy, Upwork — category-taxonomy report flows with severity-gated escalation
```

### 11d. Decisions Log

Append a dated entry titled `### Client Conduct Reports — 2026-08-22` following the exact prose
style of the existing entries (e.g. "Feedback / Bug Report Feature — 2026-07-02"). Write this
**after** you've actually built and verified the feature, documenting:
- What was built (bullet list of entities/endpoints/pages, same density as the Feedback entry).
- The architecture decisions from this prompt's Decisions section, restated as committed fact
  (not "the prompt said to" — write it as your own reasoning, the way every other entry in this
  log does).
- Any deviation you were forced into against what this prompt specified, and why — if none,
  say so explicitly rather than omitting the subsection (some prior entries do this, e.g. "no
  deviations found" isn't universal but is good practice here given how many judgment calls this
  prompt left open, like 8d's attachment-picker extraction choice and 4c/6's DI-registration
  choice).
- Verification performed (which tests you ran, whether you exercised the real HTTP path for the
  redaction guarantee, not just the handler in isolation).

---

## Out of Scope — flagged explicitly, not silently dropped

- **Reported-party response/appeal flow.** `Review` has `OwnerResponse`; this feature has no
  equivalent for the reported artist to respond to a conduct report. A future prompt could add
  one, gated so it doesn't leak back to the (anonymized-to-the-artist) reporter's identity in
  reverse — that's a genuinely separate design problem, not a trivial extension.
- **Per-user filing rate limit / abuse-of-the-report-system quota.** The standard IP-based
  `public-write` rate limiter is the only defense in this prompt. A malicious client filing
  repeated false high-severity reports (e.g. to harass a studio) isn't specifically guarded
  against beyond that. A proper fix would mirror `ManualReminderQuotaService`'s Redis-backed
  per-user daily quota pattern — deliberately not built here since it's its own scoped piece of
  work with its own edge cases (what's a fair per-user limit for a safety-reporting feature,
  balancing against not discouraging genuine repeat victims).
- **Client-facing status visibility.** Decision 5 — the reporting client never sees their own
  filed reports after submission. If product direction changes on this later, it's a new
  `GET .../mine`-style endpoint plus a UI surface, not a small tweak to what's built here.
- **SMS/push alerting for High-severity reports.** Email only, per Decision 4. Twilio SMS is
  already wired for other flows in this codebase but was not extended here — a deliberate scope
  cut, not an oversight.
- **Automatic enforcement action** (e.g. auto-suspending an artist/studio after N high-severity
  reports). This feature is reporting + triage only; any suspension remains the existing manual
  `SuspendStudioCommand`/`AuditActions.StudioSuspended` issuer action, invoked separately by a
  human reviewing the report — no automatic linkage was built.

---

## Build checklist

```
Domain
  [ ] ReportCategory, ReportStatus enums
  [ ] ReportCategoryClassifier (High/Standard mapping, exhaustive-coverage test)
  [ ] ConductReport entity (non-tenant, ForArtist/ForStudio factories, UpdateStatus, IsReadableBy)
  [ ] AuditActions.ConductReportStatusUpdated, AuditTargetTypes.ConductReport
  [ ] PlatformContacts.SupportEmail (extracted from SubmitContactRequestHandler)

Application
  [ ] FileArtistConductReportCommand + Handler + Validator
  [ ] FileStudioConductReportCommand + Handler + Validator
  [ ] UpdateConductReportStatusCommand + Handler + Validator (IAuditableCommand)
  [ ] GetMyStudioConductReportsQuery + Handler (owner)
  [ ] GetMyConductReportsAsArtistQuery + Handler (artist, redacted)
  [ ] GetConductReportsQuery + Handler (issuer)
  [ ] GetReportableArtistAppointmentsQuery + Handler
  [ ] GetReportableStudioAppointmentsQuery + Handler
  [ ] ConductReportAuthorizationGuard (read + write-permission checks)
  [ ] ConductReportNotifier (High-severity email alert)
  [ ] ConductReportProjections (shared full/redacted response mapping)

Infrastructure
  [ ] AppDbContext: DbSet<ConductReport>, inline OnModelCreating config, NO query filter
  [ ] Migration AddConductReports generated, reviewed, applies cleanly to a fresh DB

Contracts
  [ ] FileArtistConductReportRequest, FileStudioConductReportRequest
  [ ] UpdateConductReportStatusRequest
  [ ] ConductReportResponse, ReportableAppointmentResponse

API
  [ ] PublicEndpoints.cs extended: file (artist/studio), reportable-appointments (artist/studio)
  [ ] New ConductReportEndpoints.cs: owner/artist/issuer reads, status PATCH
  [ ] Program.cs: MapConductReportEndpoints() wired
  [ ] Every endpoint's authorization policy matches this prompt exactly (ClientOnly for filing,
      OwnerOnly for owner+issuer reads/writes, ArtistAndAbove for artist reads, IssuerOnly for
      the platform list)

Frontend
  [ ] conductReports.types.ts
  [ ] publicApi.ts extended (file + reportable-appointments, both targets)
  [ ] New conductReportsApi.ts, registered in store.ts
  [ ] ConductReportDialog.tsx, wired into ArtistPortfolioPage.tsx + StudioPortfolioPage.tsx
  [ ] ConductReportsPage.tsx (owner/artist, role-branched), route /conduct-reports
  [ ] ConductReportInboxPage.tsx (issuer), route /platform/conduct-reports
  [ ] Nav items + open-count badges in OwnerLayout, ArtistLayout, IssuerLayout
  [ ] data-tour attributes on all three new nav items

Tests
  [ ] Backend unit tests per Part 7 (classifier exhaustiveness, eligibility deltas from Review,
      authorization guard matrix, notifier severity gate, redaction test)
  [ ] Backend integration tests per Part 7 (end-to-end file→resolve, severity-gated 403, audit
      row written, real-HTTP redaction check)
  [ ] Frontend tests per Part 9

Cross-cutting (CLAUDE.md rules #6/#7 — not optional)
  [ ] Trust & Safety Reference Set added to architecture.md's benchmark section (Decision 10)
  [ ] helpContent.ts: 3 new articles (client, owner, artist)
  [ ] Standalone manual updated to match
  [ ] Onboarding tours updated (owner, artist, issuer if warranted)
  [ ] Feature Module Map row #37 added
  [ ] IgnoreQueryFilters() table: explicit "no entry needed" note added, not silently skipped
  [ ] Decisions Log entry appended, written from actual build outcome, not this prompt's predictions
```

---

## Summary of Changes

Fill this in at the end, following the exact density and honesty of every prior
`overnight-prompt-*.md`'s own closing summary — new features shipped, explicitly out-of-scope
items (cross-reference the "Out of Scope" section above rather than re-deriving), Help sync
confirmation, and — given how many explicit judgment calls this prompt left open (DI lifetime
for `ConductReportNotifier`, attachment-picker extraction vs. duplication, the `AuditStudioId`
gap for issuer-authored status changes, whether issuer's tour needs a new step) — a short
"judgment calls made" list so a reviewer can see exactly where you exercised discretion and why,
rather than having to diff every file to find out.
