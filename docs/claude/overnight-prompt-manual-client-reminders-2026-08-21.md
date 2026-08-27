# Overnight Prompt — Manual Client Reminders (Artist-Triggered SMS)

> Date: 2026-08-21
> Target: `Pena_e_Arte.Domain`, `Pena_e_Arte.Contracts`, `Pena_e_Arte.Application` (new
> `Reminders/` folder), `Pena_e_Arte.Infrastructure` (one EF migration, one new Hangfire job,
> one new Redis-backed quota service), `Pena_e_Arte.API`, `frontend/src/features/reminders`
> (new), `frontend/src/features/appointments`, `frontend/src/features/clients`,
> `frontend/src/features/schedule` (verify exact file — see Part 9), backend + frontend tests,
> Help Menu (`helpContent.ts`), standalone user manual (`index.html`).
> One new EF Core migration — new table, one new nullable column on `clients`, one column
> widened from `NOT NULL` to nullable on `notification_logs` (safe — relaxing a constraint,
> not tightening one; no backfill needed). No new npm or NuGet packages — reuses the existing
> Twilio wrapper (`INotificationService`), Hangfire (`IJobScheduler`), and Redis wiring.
> Work unsupervised. Commit after every logical unit (see Part headers). All changes must pass
> `dotnet build`, `dotnet test`, `pnpm tsc --noEmit`, `pnpm lint`, and `pnpm test --run` before
> the session ends.

---

## Pre-flight

1. Read `CLAUDE.md`, `docs/claude/backend.md`, `docs/claude/database.md`,
   `docs/claude/frontend.md`, `docs/claude/conventions.md` before making any changes.
2. Baseline, before touching anything:
   - `dotnet build`
   - `dotnet test` — note the current pass count; pre-existing failures are not this prompt's
     problem, but do not introduce new ones.
   - `pnpm tsc --noEmit`
   - `pnpm test src/features/appointments src/features/clients` — confirm the current suite is
     green first.
3. Read each of these in full before starting the matching Part — they are the exact
   precedents this prompt's new code must mirror, not just similar-in-spirit examples:
   - `Pena_e_Arte.Infrastructure/Jobs/AppointmentReminderJob.cs` — Part 3's new job mirrors this
     one's structure almost line for line.
   - `Pena_e_Arte.Application/Appointments/Commands/CancelAppointmentCommand.cs` — the
     ownership-check (404-not-403), `IAuditableCommand` shape, and ConflictException-on-bad-state
     conventions Part 6 must follow exactly.
   - `Pena_e_Arte.Application/Artists/Commands/UpdateArtistCommand.cs` — the
     `currentUser.Role == "artist" && artist.UserId != currentUser.UserId` ownership-check
     pattern, reused verbatim in Part 6.
   - `Pena_e_Arte.Infrastructure/Services/PlanLimitService.cs` — the existing precedent for how
     this codebase wraps a Redis-backed counter/limit check in a scoped service class. Part 4's
     new quota service mirrors its shape (constructor injection of the Redis connection, method
     signature style, where it's registered in DI) — do not invent a new abstraction alongside
     it.
   - `Pena_e_Arte.Domain/Constants/AuditActions.cs` and `AuditTargetTypes.cs` — read the exact
     current contents before adding new constants (Part 2); do not guess existing values.
   - `Pena_e_Arte.API/Middleware/ExceptionMiddleware.cs` — read the full `switch` before adding
     the new case in Part 2.

---

## Context — current state (verified against live source, 2026-08-21)

- **Automatic reminders already exist and already work for unregistered clients.**
  `Client.UserId` is `Guid?` — a `Client` row can and does exist with no linked platform login
  (walk-in clients an artist creates via `CreateClientCommand` with just name/phone, `ArtistId`
  set, `UserId` null). `CreateAppointmentCommand` schedules a 48h and a 24h reminder for every
  appointment via `IJobScheduler.ScheduleAppointmentReminder`, and
  `AppointmentReminderJob.SendReminderAsync` sends SMS to `appointment.Client.Phone` whenever
  it's set, regardless of `UserId`. **This prompt does not touch that pipeline at all** — it is
  purely additive.
- **No manual, artist-triggered reminder capability exists anywhere in the codebase** — no
  command, no endpoint, no UI. This was verified by reading the actual source, not inferred from
  `docs/claude/architecture.md`'s Feature Module Map (there is no map entry for it because it was
  never built).
- **`NotificationLog.RecipientId` is currently `Guid` (not nullable)**, `RecipientType` is
  `NotificationRecipientType { Client, Studio, Artist }`, stored via
  `.HasConversion<string>().HasMaxLength(32)` — confirmed via
  `NotificationLogConfiguration.cs`, so there is comfortable headroom for a new enum value
  (`ExternalContact`, 15 characters) with zero migration risk on that column.
- **No SMS opt-out mechanism exists anywhere in the app.** The existing automatic reminder SMS
  body says "Reply STOP to opt out," but there is no Twilio inbound webhook and no `Client`
  field recording an opt-out — that text is aspirational only today.
- **No phone-number format validation exists anywhere in the app** — `CreateClientValidator`
  only does `MaximumLength(20)` on `Phone`. This prompt follows that same loose convention for
  its own new phone fields, for consistency, rather than inventing stricter validation
  unilaterally for just one feature.
- **No existing shared helper resolves "the artist record for the current user"** — every site
  that needs it (`UpdateArtistCommand`, `AddArtistTimeOffCommand`, `UpsertArtistScheduleCommand`,
  `UpdateArtistPortfolioCommand`) inlines
  `db.Artists.FirstOrDefaultAsync(a => a.UserId == currentUser.UserId, ct)` (or the equivalent
  ownership-check-only form) separately. This prompt adds a fifth/sixth near-duplicate rather
  than refactoring that now — extracting a shared helper is a real, separate cleanup, flagged in
  Out of Scope, not bundled into this feature change.
- **Authenticated endpoints do not get Redis rate-limiting by policy in this app** (see the
  Redis rate-limiting rule referenced in `architecture.md`'s traffic-analytics section) — that
  policy is about the generic ASP.NET `RateLimiter` middleware for anonymous/auth endpoints. A
  free-text, artist-triggered SMS-sending endpoint reachable against any phone number is a
  materially different, real-money abuse surface (Twilio bills per SMS) — Part 4 adds a
  purpose-built per-artist daily quota, which is a business-rule check inside the handler, not a
  change to that generic HTTP rate-limiting policy.

---

## Decisions (already made — do not re-litigate)

| # | Decision | Rationale |
|---|---|---|
| 1 | Manual reminders **bypass** `INotificationPreferenceService` entirely — they are never gated by a studio's per-channel notification toggle. | `StudioNotificationPreference` gates *automatic lifecycle* notifications (`NotificationType`: `AppointmentCreated`, `Aftercare`, etc.). A manual reminder is a deliberate one-off action an artist takes right now — gating it behind a studio-wide preference toggle would let a stale/forgotten preference silently swallow an action the artist explicitly just took. Confirmed. |
| 2 | Owner and issuer callers may trigger or cancel a manual reminder **on behalf of any artist at the studio** — the ownership check (`currentUser.Role == "artist" && artist.UserId != currentUser.UserId`) only applies to artist-role callers, exactly like every existing site listed in Context. | Matches the established bypass pattern used everywhere else in this codebase for staff-role oversight actions. |
| 3 | The "raw contact" path (no `AppointmentId`, no `ClientId` — just typed name + phone) **never creates a `Client` row**. It stays fully ephemeral by design — that is the entire point of the "not registered, no record at all" requirement. | Confirmed. If this needs to become a real client relationship later, the artist creates one explicitly via the existing `CreateClientCommand` flow — this feature does not fold the two together. |
| 4 | The raw-contact UI entry point lives on `SchedulePage.tsx` as a "Quick Reminder" toolbar action. | The natural page for "I want to text someone about their appointment slot" even when there's no formal appointment record yet. An alternative (clients list page) was considered and rejected — the clients list implies an existing/intended `Client` record, which this path deliberately avoids creating. |
| 5 | `Message` is capped at 320 characters (~2 SMS segments). `ScheduledFor`, when provided, must be more than zero and at most 90 days in the future. | Sane, deliberately chosen ceilings to prevent a pathological far-future Hangfire schedule or a message no SMS gateway would sensibly deliver as one logical text — not free product-numbers left open for further debate. |
| 6 | A per-artist daily quota of **20 manual reminders per rolling 24 hours** is enforced server-side, Redis-backed, throwing a new `ManualReminderQuotaExceededException` mapped to **429**. | A free-text SMS-sending endpoint reachable against any phone number, with no existing per-endpoint rate limit in this app, is a real cost/abuse surface (Twilio bills per SMS) that must not ship unguarded. 20/day comfortably covers legitimate heavy use (a busy artist reminding most of a full day's bookings) while bounding worst-case cost from a compromised or malicious account. This is a starting number under real operating conditions, not a permanent product commitment — flagged in Out of Scope for revisiting once real usage data exists. |
| 7 | `Client.SmsOptOut` is added and checked before every manual-reminder send **and** wired into `AppointmentReminderJob`'s existing automatic sends too. The Twilio inbound webhook that would ever actually *set* `SmsOptOut` to `true` (processing a "STOP" reply) is **out of scope for this prompt.** | The field is cheap and consistent to add now and check everywhere SMS already goes out, even though nothing sets it yet — this at minimum makes a future opt-out feature a one-file webhook addition instead of a multi-handler retrofit. Building the actual inbound-webhook/STOP-handling pipeline is a materially larger, separately-scoped piece of work (Twilio signature validation, a new anonymous endpoint, updating every SMS-sending handler's contract) — flagged explicitly in Out of Scope, not silently dropped. |
| 8 | `NotificationLog.RecipientId` widens from `Guid` to `Guid?`, and `NotificationRecipientType` gains a new `ExternalContact` value. | This is a shared entity touched by every existing SMS/email-sending handler in the app — called out explicitly because of that, not because the change itself is risky. Every existing constructor call still supplies a real `Guid`, so this widening is additive/backward compatible; confirmed safe against `NotificationLogConfiguration.cs`'s `HasMaxLength(32)` on the string-converted enum column. |
| 9 | `CreateManualReminderCommand` and `CancelManualReminderCommand` both implement `IAuditableCommand`. | Matches the precedent of `CancelAppointmentCommand` and `UpdateClientArtistCommand` — both are staff-initiated mutations adjacent to a client record, and leaving a new one silently unaudited next to its logged siblings would be an inconsistency, not a simplification (same reasoning `docs/claude/CLAUDE.md` rule 6 already applied to `UpdateClientArtistCommand`). |
| 10 | Cancelling a manual reminder that has already fired (or was already cancelled) throws **`ConflictException`** (409), not `BusinessRuleViolationException` (422). | Matches the "this already happened / already completed by another request" semantics `ExceptionMiddleware` already uses `ConflictException` for elsewhere (e.g. the duplicate-payment-attempt race), rather than treating it as a validation failure of the *current* request. |

**Explicitly out of scope, flagged and not built here** (see the full "Out of Scope" section
near the end for detail): the Twilio inbound STOP webhook (Decision 7); extracting a shared
`FindArtistForUserAsync`-style helper (Context, last bullet); revisiting the 20/day quota number
once real usage data exists (Decision 6).

---

## Part 1 — Domain + EF Core

### 1a. `Pena_e_Arte.Domain/Entities/Client.cs`

Add, directly after `Phone`:
```csharp
/// <summary>
/// True once this client has opted out of SMS. Nothing in this codebase sets this to true
/// yet — there is no inbound-SMS/STOP-reply webhook (see architecture.md's Decisions Log,
/// 2026-08-21 entry) — but every outbound SMS path (automatic and manual reminders alike)
/// must check it now so a future opt-out feature is a one-file addition, not a retrofit.
/// </summary>
public bool SmsOptOut { get; set; }
```

### 1b. New enum `Pena_e_Arte.Domain/Enums/ManualReminderStatus.cs`

```csharp
namespace Pena_e_Arte.Domain.Enums;

public enum ManualReminderStatus
{
    Scheduled,
    Sent,
    Failed,
    Cancelled
}
```

### 1c. `Pena_e_Arte.Domain/Enums/NotificationRecipientType.cs`

Add one value, at the end (do not renumber/reorder the existing three — this enum is stored as
a string via `.HasConversion<string>()`, so ordinal position doesn't matter for existing data,
but keep new values appended rather than inserted, as a general habit):

```csharp
namespace Pena_e_Arte.Domain.Enums;

public enum NotificationRecipientType
{
    Client,
    Studio,
    Artist,
    // Recipient has no Client record at all — a manual reminder sent to a raw phone number
    // the artist typed in, with no platform record created. See ManualReminder.ClientId (null).
    ExternalContact
}
```

### 1d. `Pena_e_Arte.Domain/Entities/NotificationLog.cs`

Widen `RecipientId`:
```csharp
public Guid? RecipientId { get; set; }
```
(was `Guid RecipientId { get; set; }`). Every existing call site constructing a
`NotificationLog` still supplies a real `Guid` — grep `new NotificationLog` across
`Pena_e_Arte.Infrastructure/Jobs/` and `Pena_e_Arte.Application/**/Commands/` to confirm none of
them assume non-null in a way that breaks (they won't — a non-null `Guid` is a valid `Guid?`),
but do the grep and read each hit before moving on, don't just assume.

### 1e. New entity `Pena_e_Arte.Domain/Entities/ManualReminder.cs`

```csharp
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

public class ManualReminder : TenantEntity
{
    public Guid ArtistId { get; set; }              // who set the reminder
    public Guid? AppointmentId { get; set; }         // set when tied to an existing appointment
    public Guid? ClientId { get; set; }              // set when tied to an existing Client record
    public string RecipientName { get; set; } = string.Empty;   // always populated
    public string RecipientPhone { get; set; } = string.Empty;  // always populated
    public string? Message { get; set; }             // null = use the default template
    public DateTime ScheduledFor { get; set; }        // UTC; "now" for an immediate send
    public ManualReminderStatus Status { get; set; } = ManualReminderStatus.Scheduled;
    public string? JobId { get; set; }                // Hangfire job id, for cancellation
    public DateTime? SentAt { get; set; }

    public Artist Artist { get; set; } = null!;
    public Appointment? Appointment { get; set; }
    public Client? Client { get; set; }
}
```

Exactly one of `AppointmentId` / `ClientId` / a manually-typed `RecipientName`+`RecipientPhone`
drives how the two recipient columns get populated at creation time (Part 6 enforces this) —
after creation, every downstream read (the job, the history list) only ever needs
`RecipientName`/`RecipientPhone`, never branches on where they came from.

### 1f. `AppDbContext.cs` — add the new DbSet + query filter

In the tenant-scoped block, alongside `NotificationLogs`:
```csharp
public DbSet<ManualReminder> ManualReminders => Set<ManualReminder>();
```
And in `OnModelCreating`, alongside the other tenant filters:
```csharp
builder.Entity<ManualReminder>().HasQueryFilter(m => m.StudioId == tenant.StudioId);
```

### 1g. New `Pena_e_Arte.Infrastructure/Persistence/Configurations/ManualReminderConfiguration.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class ManualReminderConfiguration : TenantEntityConfiguration<ManualReminder>
{
    protected override string TableName => "manual_reminders";

    public override void Configure(EntityTypeBuilder<ManualReminder> builder)
    {
        base.Configure(builder);

        builder.Property(m => m.RecipientName).HasMaxLength(200).IsRequired();
        builder.Property(m => m.RecipientPhone).HasMaxLength(20).IsRequired();
        builder.Property(m => m.Message).HasMaxLength(320);
        builder.Property(m => m.JobId).HasMaxLength(100);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.HasIndex(m => new { m.StudioId, m.ScheduledFor })
               .HasDatabaseName("ix_manual_reminders_studio_scheduled_for");
        builder.HasIndex(m => m.AppointmentId)
               .HasDatabaseName("ix_manual_reminders_appointment_id");
        builder.HasIndex(m => m.ClientId)
               .HasDatabaseName("ix_manual_reminders_client_id");

        builder.HasOne(m => m.Artist)
               .WithMany()
               .HasForeignKey(m => m.ArtistId)
               .HasConstraintName("fk_manual_reminders_artists")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.Appointment)
               .WithMany()
               .HasForeignKey(m => m.AppointmentId)
               .HasConstraintName("fk_manual_reminders_appointments")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.Client)
               .WithMany()
               .HasForeignKey(m => m.ClientId)
               .HasConstraintName("fk_manual_reminders_clients")
               .OnDelete(DeleteBehavior.Restrict);
    }
}
```
Verify `TenantEntityConfiguration<T>`'s base `Configure` (read it first) covers `Id`/`StudioId`/
`CreatedAt`/`UpdatedAt` already, matching every other `*Configuration.cs` file in this project —
do not re-declare those here.

### 1h. `ClientConfiguration.cs`

Add, near the other simple property configurations:
```csharp
builder.Property(c => c.SmsOptOut).HasDefaultValue(false).IsRequired();
```

### 1i. Migration

```bash
dotnet ef migrations add AddManualReminders \
  --project Pena_e_Arte.Infrastructure \
  --startup-project Pena_e_Arte.API
```

Verify the generated migration:
- Creates `manual_reminders` with the columns/indexes/FKs above.
- Adds `clients.sms_opt_out` as `bool NOT NULL DEFAULT false` (a genuinely new column — safe).
- Alters `notification_logs.recipient_id` from `NOT NULL` to nullable (no data
  loss/backfill — every existing row already has a non-null value that remains valid).

Apply it locally (`dotnet ef database update ...`) and confirm the app still boots before
moving on.

**Commit:** `migrate: add manual reminders, client sms opt-out, widen notification recipient id`

---

## Part 2 — Constants + new exception

### 2a. `Pena_e_Arte.Domain/Constants/AuditActions.cs`

Read the file's current contents first (Pre-flight step 3), then add, following its existing
`"Entity.PastTenseVerb"` naming convention exactly:
```csharp
public const string ManualReminderSent = "ManualReminder.Sent";
public const string ManualReminderCancelled = "ManualReminder.Cancelled";
```

### 2b. `AuditTargetTypes` (same file)

```csharp
public const string ManualReminder = "ManualReminder";
```

### 2c. New `Pena_e_Arte.Domain/Exceptions/ManualReminderQuotaExceededException.cs`

```csharp
namespace Pena_e_Arte.Domain.Exceptions;

public class ManualReminderQuotaExceededException()
    : DomainException("You've reached today's limit for manual reminders. Try again tomorrow, " +
                       "or contact support if you need a higher limit.");
```

### 2d. `Pena_e_Arte.API/Middleware/ExceptionMiddleware.cs`

This is shared middleware every request passes through — read the full existing `switch`
first. Add one new case, next to the other 4xx entries (position doesn't matter functionally,
but keep it near `PlanLimitExceededException` for readability — both are quota/limit
rejections):
```csharp
ManualReminderQuotaExceededException => (StatusCodes.Status429TooManyRequests, ex.Message, "MANUAL_REMINDER_QUOTA_EXCEEDED"),
```
This is the first `429` this middleware returns — confirm `StatusCodes.Status429TooManyRequests`
resolves correctly (it's a standard ASP.NET Core constant, should need no new `using`).

**Commit:** `feat(reminders): add audit constants and quota exception`

---

## Part 3 — `IJobScheduler` / `JobScheduler` + new Hangfire job

### 3a. `Pena_e_Arte.Domain/Interfaces/IJobScheduler.cs`

Add:
```csharp
string ScheduleManualReminder(Guid manualReminderId, DateTimeOffset sendAt);
void CancelJob(string jobId);
```

### 3b. `Pena_e_Arte.Infrastructure/Services/JobScheduler.cs`

```csharp
public string ScheduleManualReminder(Guid manualReminderId, DateTimeOffset sendAt) =>
    sendAt <= DateTimeOffset.UtcNow
        ? backgroundJobs.Enqueue<ManualReminderJob>(j => j.SendAsync(manualReminderId, default))
        : backgroundJobs.Schedule<ManualReminderJob>(j => j.SendAsync(manualReminderId, default), sendAt);

public void CancelJob(string jobId) => backgroundJobs.Delete(jobId);
```
`CancelJob` is deliberately generic (not manual-reminder-specific), unlike
`CancelAppointmentJobs`'s fixed two-job-id shape — it's usable for any single-job-id
cancellation.

### 3c. New `Pena_e_Arte.Infrastructure/Jobs/ManualReminderJob.cs`

Mirror `AppointmentReminderJob`'s structure closely (read it again if needed — this is not a
loose paraphrase, follow its shape: constructor injection, `IgnoreQueryFilters()` because
Hangfire jobs run with no tenant/request scope, try/catch around the SMS send, `NotificationLog`
write, `SaveChangesAsync`, then the SignalR push):

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.Infrastructure.Jobs;

public class ManualReminderJob(
    INotificationService notifications,
    AppDbContext db,
    IRealtimeNotifier realtime,
    ILogger<ManualReminderJob> logger)
{
    public async Task SendAsync(Guid manualReminderId, CancellationToken ct = default)
    {
        ManualReminder? reminder = await db.ManualReminders
            .IgnoreQueryFilters()
            .Include(m => m.Client)
            .Include(m => m.Appointment)
            .FirstOrDefaultAsync(m => m.Id == manualReminderId, ct);

        if (reminder is null)
        {
            logger.LogWarning("ManualReminder {@ManualReminderId} not found", manualReminderId);
            return;
        }

        if (reminder.Status == ManualReminderStatus.Cancelled)
        {
            logger.LogInformation("Skipping cancelled ManualReminder {@ManualReminderId}", manualReminderId);
            return;
        }

        if (reminder.Appointment is not null && reminder.Appointment.Status == AppointmentStatus.Cancelled)
        {
            reminder.Status = ManualReminderStatus.Cancelled;
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Skipping ManualReminder {@ManualReminderId} — linked appointment was cancelled", manualReminderId);
            return;
        }

        if (reminder.Client is not null && reminder.Client.SmsOptOut)
        {
            reminder.Status = ManualReminderStatus.Failed;
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Skipping ManualReminder {@ManualReminderId} — client has opted out of SMS", manualReminderId);
            return;
        }

        Studio? studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == reminder.StudioId, ct);
        string studioName = studio?.Name ?? "your studio";

        string body = BuildBody(reminder, studioName);
        bool success = await TrySendSmsAsync(reminder, body, ct);

        NotificationLog log = new()
        {
            StudioId = reminder.StudioId,
            RecipientId = reminder.ClientId,
            RecipientType = reminder.ClientId is not null
                ? NotificationRecipientType.Client
                : NotificationRecipientType.ExternalContact,
            Channel = NotificationChannel.Sms,
            Body = body,
            SentAt = DateTime.UtcNow,
            IsSuccess = success
        };
        db.NotificationLogs.Add(log);

        reminder.Status = success ? ManualReminderStatus.Sent : ManualReminderStatus.Failed;
        reminder.SentAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        await realtime.NotifyStudioAsync(
            reminder.StudioId, "NotificationReceived",
            ToResponse(log, reminder.RecipientName), ct);
    }

    private static NotificationLogResponse ToResponse(NotificationLog log, string recipientName) => new(
        log.Id, log.RecipientId, recipientName, log.Channel.ToString(),
        log.Subject, log.Body, log.SentAt, log.IsSuccess, log.CreatedAt);

    private static string BuildBody(ManualReminder reminder, string studioName)
    {
        if (!string.IsNullOrWhiteSpace(reminder.Message))
            return reminder.Message;

        return reminder.Appointment is not null
            ? $"Hi {reminder.RecipientName}, reminder from {studioName} — your appointment is " +
              $"{reminder.Appointment.Date:ddd dd MMM 'at' HH:mm}."
            : $"Hi {reminder.RecipientName}, this is a reminder from {studioName}.";
    }

    private async Task<bool> TrySendSmsAsync(ManualReminder reminder, string body, CancellationToken ct)
    {
        try
        {
            await notifications.SendSmsAsync(reminder.RecipientPhone, body, ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send manual reminder SMS {@ManualReminderId}", reminder.Id);
            return false;
        }
    }
}
```

Check the exact constructor signature/namespace of `NotificationLogResponse` (used above via
`ToResponse`) against `AppointmentReminderJob`'s own `ToResponse` — copy its exact record shape
rather than retyping from memory in case the fields differ slightly from what's shown here.

### 3d. `docs/claude/architecture.md` — extend the existing IgnoreQueryFilters row, do not add a new one

Find table row **#36** in the "AllowAnonymous/IgnoreQueryFilters approved usages" table
(currently reads `AppointmentReminderJob`, `DesignRevisionTimeoutJob`,
`PaymentReconciliationJob`, `SendArtistInviteJob`) and add `ManualReminderJob` to that same
list:
```
| 36 | `AppointmentReminderJob`, `DesignRevisionTimeoutJob`, `PaymentReconciliationJob`, `SendArtistInviteJob`, `ManualReminderJob` | Hangfire background jobs run with no request/tenant scope at all — same class as `IndustryReportJob` (#3) | Hangfire job (system) |
```
Do **not** create a new numbered row — `ManualReminderJob` is the same class of no-tenant-context
system job as the four already listed there.

**Commit:** `feat(reminders): add ManualReminderJob and job scheduler support`

---

## Part 4 — Redis-backed per-artist daily quota

### 4a. New `Pena_e_Arte.Domain/Interfaces/IManualReminderQuotaService.cs`

```csharp
namespace Pena_e_Arte.Domain.Interfaces;

public interface IManualReminderQuotaService
{
    /// <summary>Throws ManualReminderQuotaExceededException if the artist has already hit
    /// today's cap; otherwise increments the counter and returns normally.</summary>
    Task CheckAndIncrementAsync(Guid studioId, Guid artistId, CancellationToken ct);
}
```

### 4b. New `Pena_e_Arte.Infrastructure/Services/ManualReminderQuotaService.cs`

Mirror `PlanLimitService.cs`'s constructor-injection and Redis-access style exactly (read it
first — match how it resolves `IConnectionMultiplexer`/`IDatabase`, whatever field/property name
it uses, rather than inventing a different shape here). Key convention, per
`docs/claude/database.md`'s Redis Patterns section (`ratelimit:{tenantId}:{endpoint}`-style
keys): `manualreminders:{studioId}:{artistId}:{yyyyMMdd}` (UTC date), simple `INCR` + `EXPIRE`
(24h+buffer TTL so it never lingers past the day it's for), cap **20**:

```csharp
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using StackExchange.Redis;

namespace Pena_e_Arte.Infrastructure.Services;

public class ManualReminderQuotaService(IConnectionMultiplexer redis) : IManualReminderQuotaService
{
    private const int DailyLimit = 20;

    public async Task CheckAndIncrementAsync(Guid studioId, Guid artistId, CancellationToken ct)
    {
        IDatabase db = redis.GetDatabase();
        string key = $"manualreminders:{studioId}:{artistId}:{DateTime.UtcNow:yyyyMMdd}";

        long count = await db.StringIncrementAsync(key);
        if (count == 1)
            await db.KeyExpireAsync(key, TimeSpan.FromHours(25));

        if (count > DailyLimit)
            throw new ManualReminderQuotaExceededException();
    }
}
```
Verify the exact `IConnectionMultiplexer` DI registration already used for `PlanLimitService`
(or wherever Redis is wired up in `Program.cs`/a `ServiceCollectionExtensions` file) and register
`IManualReminderQuotaService` the same way, alongside it — do not introduce a second, differently
configured Redis connection.

**Fail-open note:** unlike `RedisFixedWindowRateLimiter` (which explicitly fails open on a Redis
outage, per `architecture.md`), this quota check **fails closed** — if Redis is unreachable, let
the `StackExchange.Redis` exception propagate (it will surface as a generic 500, not silently
allow unlimited sends). This is a deliberate difference: the HTTP rate limiter protects
uptime/abuse at the infrastructure layer where fail-open is the safer default, but this quota's
entire purpose is bounding real SMS cost — silently allowing unlimited sends during a Redis blip
defeats that purpose. State this reasoning in the commit message so it isn't mistaken for an
oversight later.

**Commit:** `feat(reminders): add per-artist daily manual reminder quota`

---

## Part 5 — Contracts

### 5a. New `Pena_e_Arte.Contracts/Requests/CreateManualReminderRequest.cs`

```csharp
namespace Pena_e_Arte.Contracts.Requests;

public record CreateManualReminderRequest(
    Guid? AppointmentId,
    Guid? ClientId,
    Guid? ArtistId,          // only honored for owner/issuer callers acting on another artist's behalf
    string? RecipientName,
    string? RecipientPhone,
    string? Message,
    DateTime? ScheduledFor);
```

### 5b. New `Pena_e_Arte.Contracts/Responses/ManualReminderResponse.cs`

```csharp
namespace Pena_e_Arte.Contracts.Responses;

public record ManualReminderResponse(
    Guid Id,
    Guid? AppointmentId,
    Guid? ClientId,
    string RecipientName,
    string RecipientPhone,
    string? Message,
    DateTime ScheduledFor,
    string Status,
    DateTime? SentAt,
    DateTime CreatedAt);
```

**Commit:** `feat(reminders): add manual reminder request/response contracts`

---

## Part 6 — Application layer — new `Pena_e_Arte.Application/Reminders/` folder

New top-level feature folder (sibling to `Appointments/`, `Clients/`, `Notifications/`, etc.,
per `backend.md`'s documented layout) — `Commands/`, `Queries/`, `Validators/`.

### 6a. `Commands/CreateManualReminderCommand.cs`

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Reminders.Commands;

public record CreateManualReminderCommand(CreateManualReminderRequest Request)
    : IRequest<ManualReminderResponse>, IAuditableCommand
{
    public string AuditAction => AuditActions.ManualReminderSent;
    public string AuditTargetType => AuditTargetTypes.ManualReminder;
    public Guid AuditTargetId => Guid.Empty; // set below once the reminder id exists — verify
                                              // against IAuditableCommand's actual contract
                                              // (read it first): if AuditTargetId must be known
                                              // before Handle() runs, this may need to become a
                                              // post-creation audit write inside the handler
                                              // instead of a command-level property. Confirm the
                                              // exact mechanism CancelAppointmentCommand uses
                                              // (its AuditTargetId is the pre-existing
                                              // AppointmentId, known up front) — this command's
                                              // target id does not exist until after Handle()
                                              // creates the row, which is a real difference worth
                                              // resolving deliberately, not copying blindly.
}

public class CreateManualReminderHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    ICurrentUser currentUser,
    IJobScheduler jobs,
    IManualReminderQuotaService quota)
    : IRequestHandler<CreateManualReminderCommand, ManualReminderResponse>
{
    public async Task<ManualReminderResponse> Handle(CreateManualReminderCommand command, CancellationToken ct)
    {
        CreateManualReminderRequest req = command.Request;

        Artist artist = await ResolveArtistAsync(req.ArtistId, ct);

        string recipientName;
        string recipientPhone;
        Guid? clientId = null;
        Guid? appointmentId = null;

        if (req.AppointmentId is not null)
        {
            Appointment appointment = await db.Appointments
                .Include(a => a.Client)
                .Include(a => a.Artist)
                .FirstOrDefaultAsync(a => a.Id == req.AppointmentId, ct)
                ?? throw new NotFoundException(nameof(Appointment), req.AppointmentId);

            if (currentUser.Role == "artist" && appointment.Artist.UserId != currentUser.UserId)
                throw new NotFoundException(nameof(Appointment), req.AppointmentId);

            if (appointment.Client.Phone is null)
                throw new BusinessRuleViolationException(
                    "This client has no phone number on file — nothing to send a reminder to.");
            if (appointment.Client.SmsOptOut)
                throw new BusinessRuleViolationException("This client has opted out of SMS.");

            recipientName = $"{appointment.Client.FirstName} {appointment.Client.LastName}";
            recipientPhone = appointment.Client.Phone;
            clientId = appointment.ClientId;
            appointmentId = appointment.Id;
        }
        else if (req.ClientId is not null)
        {
            Client client = await db.Clients
                .FirstOrDefaultAsync(c => c.Id == req.ClientId, ct)
                ?? throw new NotFoundException(nameof(Client), req.ClientId);

            if (currentUser.Role == "artist" && client.ArtistId != artist.Id)
                throw new NotFoundException(nameof(Client), req.ClientId);

            if (client.Phone is null)
                throw new BusinessRuleViolationException(
                    "This client has no phone number on file — nothing to send a reminder to.");
            if (client.SmsOptOut)
                throw new BusinessRuleViolationException("This client has opted out of SMS.");

            recipientName = $"{client.FirstName} {client.LastName}";
            recipientPhone = client.Phone;
            clientId = client.Id;
        }
        else
        {
            // Raw-contact path — validator has already enforced both fields are present.
            recipientName = req.RecipientName!;
            recipientPhone = req.RecipientPhone!;
        }

        await quota.CheckAndIncrementAsync(tenant.StudioId, artist.Id, ct);

        DateTime scheduledFor = req.ScheduledFor ?? DateTime.UtcNow;

        ManualReminder reminder = new()
        {
            StudioId = tenant.StudioId,
            ArtistId = artist.Id,
            AppointmentId = appointmentId,
            ClientId = clientId,
            RecipientName = recipientName,
            RecipientPhone = recipientPhone,
            Message = req.Message,
            ScheduledFor = scheduledFor,
            Status = ManualReminderStatus.Scheduled
        };

        db.ManualReminders.Add(reminder);
        await db.SaveChangesAsync(ct);

        reminder.JobId = jobs.ScheduleManualReminder(reminder.Id, scheduledFor);
        await db.SaveChangesAsync(ct);

        return ToResponse(reminder);
    }

    private async Task<Artist> ResolveArtistAsync(Guid? requestedArtistId, CancellationToken ct)
    {
        if (currentUser.Role == "artist")
        {
            return await db.Artists.FirstOrDefaultAsync(a => a.UserId == currentUser.UserId, ct)
                ?? throw new ForbiddenException();
        }

        // Owner/issuer: act on behalf of the requested artist (Decision 2). Require it explicitly
        // rather than silently picking "any" artist at the studio.
        if (requestedArtistId is null)
            throw new BusinessRuleViolationException("ArtistId is required.");

        return await db.Artists.FirstOrDefaultAsync(a => a.Id == requestedArtistId, ct)
            ?? throw new NotFoundException(nameof(Artist), requestedArtistId);
    }

    private static ManualReminderResponse ToResponse(ManualReminder r) => new(
        r.Id, r.AppointmentId, r.ClientId, r.RecipientName, r.RecipientPhone, r.Message,
        r.ScheduledFor, r.Status.ToString(), r.SentAt, r.CreatedAt);
}
```

**Before finalizing this handler:** read `Pena_e_Arte.Application/Common/IAuditableCommand.cs`
and `AuditLogBehavior.cs` in full. This command's audit target (the new `ManualReminder`'s id)
does not exist until *after* `Handle()` runs and saves — unlike every existing
`IAuditableCommand` implementer, whose target is a pre-existing entity id known from the
incoming request. Resolve this mismatch properly: either (a) confirm `AuditLogBehavior` reads
`AuditTargetId` from the command *after* the handler completes (in which case a mutable property
set inside `Handle()` works, and the placeholder `Guid.Empty` above must be replaced with a real
settable pattern), or (b) if the behavior captures `AuditTargetId` before dispatch, this needs a
different approach — e.g. writing the audit entry directly inside the handler instead of via the
`IAuditableCommand` pipeline. **Do not ship the `Guid.Empty` placeholder as-is** — it is marked
here deliberately as an unresolved detail this prompt could not verify without reading
`AuditLogBehavior.cs` directly, not as a finished implementation.

### 6b. `Commands/CancelManualReminderCommand.cs`

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Reminders.Commands;

public record CancelManualReminderCommand(Guid Id) : IRequest, IAuditableCommand
{
    public string AuditAction => AuditActions.ManualReminderCancelled;
    public string AuditTargetType => AuditTargetTypes.ManualReminder;
    public Guid AuditTargetId => Id;
}

public class CancelManualReminderHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IJobScheduler jobs)
    : IRequestHandler<CancelManualReminderCommand>
{
    public async Task Handle(CancelManualReminderCommand command, CancellationToken ct)
    {
        ManualReminder reminder = await db.ManualReminders
            .Include(m => m.Artist)
            .FirstOrDefaultAsync(m => m.Id == command.Id, ct)
            ?? throw new NotFoundException(nameof(ManualReminder), command.Id);

        if (currentUser.Role == "artist" && reminder.Artist.UserId != currentUser.UserId)
            throw new NotFoundException(nameof(ManualReminder), command.Id);

        if (reminder.Status != ManualReminderStatus.Scheduled)
            throw new ConflictException(
                $"This reminder is already {reminder.Status.ToString().ToLowerInvariant()} and can no longer be cancelled.");

        if (!string.IsNullOrEmpty(reminder.JobId))
            jobs.CancelJob(reminder.JobId);

        reminder.Status = ManualReminderStatus.Cancelled;
        reminder.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
```

### 6c. `Queries/GetManualRemindersQuery.cs`

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Reminders.Queries;

public record GetManualRemindersQuery(Guid? AppointmentId, Guid? ClientId)
    : IRequest<List<ManualReminderResponse>>;

public class GetManualRemindersHandler(IAppDbContext db)
    : IRequestHandler<GetManualRemindersQuery, List<ManualReminderResponse>>
{
    public async Task<List<ManualReminderResponse>> Handle(GetManualRemindersQuery query, CancellationToken ct)
    {
        if (query.AppointmentId is null && query.ClientId is null)
            throw new BusinessRuleViolationException("Either appointmentId or clientId is required.");

        IQueryable<ManualReminder> q = db.ManualReminders.AsQueryable();
        if (query.AppointmentId is not null)
            q = q.Where(m => m.AppointmentId == query.AppointmentId);
        if (query.ClientId is not null)
            q = q.Where(m => m.ClientId == query.ClientId);

        return await q
            .OrderByDescending(m => m.ScheduledFor)
            .Select(m => new ManualReminderResponse(
                m.Id, m.AppointmentId, m.ClientId, m.RecipientName, m.RecipientPhone, m.Message,
                m.ScheduledFor, m.Status.ToString(), m.SentAt, m.CreatedAt))
            .ToListAsync(ct);
    }
}
```

### 6d. `Validators/CreateManualReminderValidator.cs`

```csharp
using FluentValidation;
using Pena_e_Arte.Application.Reminders.Commands;

namespace Pena_e_Arte.Application.Reminders.Validators;

public class CreateManualReminderValidator : AbstractValidator<CreateManualReminderCommand>
{
    public CreateManualReminderValidator()
    {
        RuleFor(x => x.Request).Must(HaveExactlyOneRecipientSource)
            .WithMessage("Provide exactly one of: appointmentId, clientId, or a name and phone.");

        RuleFor(x => x.Request.RecipientName)
            .NotEmpty().MaximumLength(200)
            .When(x => x.Request.AppointmentId is null && x.Request.ClientId is null);

        RuleFor(x => x.Request.RecipientPhone)
            .NotEmpty().MaximumLength(20)
            .When(x => x.Request.AppointmentId is null && x.Request.ClientId is null);

        RuleFor(x => x.Request.Message).MaximumLength(320);

        RuleFor(x => x.Request.ScheduledFor)
            .GreaterThan(DateTime.UtcNow)
            .LessThanOrEqualTo(DateTime.UtcNow.AddDays(90))
            .When(x => x.Request.ScheduledFor.HasValue);
    }

    private static bool HaveExactlyOneRecipientSource(Pena_e_Arte.Contracts.Requests.CreateManualReminderRequest r)
    {
        int sources = 0;
        if (r.AppointmentId is not null) sources++;
        if (r.ClientId is not null) sources++;
        if (!string.IsNullOrWhiteSpace(r.RecipientName) && !string.IsNullOrWhiteSpace(r.RecipientPhone)) sources++;
        return sources == 1;
    }
}
```

**Commit:** `feat(reminders): add CreateManualReminder/CancelManualReminder/GetManualReminders application layer`

---

## Part 7 — API endpoints

### 7a. New `Pena_e_Arte.API/Endpoints/ManualReminderEndpoints.cs`

```csharp
using MediatR;
using Pena_e_Arte.Application.Reminders.Commands;
using Pena_e_Arte.Application.Reminders.Queries;
using Pena_e_Arte.Contracts.Requests;

namespace Pena_e_Arte.API.Endpoints;

public static class ManualReminderEndpoints
{
    public static void MapManualReminderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reminders").RequireAuthorization();

        group.MapPost("/", CreateManualReminder).RequireAuthorization("ArtistAndAbove");
        group.MapGet("/", GetManualReminders).RequireAuthorization("ArtistAndAbove");
        group.MapDelete("/{id:guid}", CancelManualReminder).RequireAuthorization("ArtistAndAbove");
    }

    private static async Task<IResult> CreateManualReminder(
        CreateManualReminderRequest request, ISender mediator, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateManualReminderCommand(request), ct);
        return Results.Created($"/api/v1/reminders/{result.Id}", result);
    }

    private static async Task<IResult> GetManualReminders(
        Guid? appointmentId, Guid? clientId, ISender mediator, CancellationToken ct)
    {
        var result = await mediator.Send(new GetManualRemindersQuery(appointmentId, clientId), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CancelManualReminder(
        Guid id, ISender mediator, CancellationToken ct)
    {
        await mediator.Send(new CancelManualReminderCommand(id), ct);
        return Results.NoContent();
    }
}
```

### 7b. `Program.cs`

Add `app.MapManualReminderEndpoints();` alongside the other `Map*Endpoints()` calls — check the
exact existing call order/grouping first and slot it in consistently (likely alphabetically or
by feature grouping — match whatever convention is already there).

**Commit:** `feat(reminders): add manual reminder API endpoints`

---

## Part 8 — Backend tests

New folder `tests/Pena_e_Arte.UnitTests/Reminders/`:

- `CreateManualReminderHandlerTests.cs`:
  - `Handle_AppointmentLinked_ResolvesRecipientFromClient_ReturnsScheduledReminder`
  - `Handle_ClientLinked_ResolvesRecipientFromClient_ReturnsScheduledReminder`
  - `Handle_RawContact_DoesNotCreateClientRow_UsesTypedNameAndPhone`
  - `Handle_AppointmentClientHasNoPhone_ThrowsBusinessRuleViolationException`
  - `Handle_ClientOptedOut_ThrowsBusinessRuleViolationException`
  - `Handle_ArtistNotOwnerOfAppointment_ThrowsNotFoundException` (confirms 404-not-403)
  - `Handle_ArtistNotOwnerOfClient_ThrowsNotFoundException`
  - `Handle_OwnerRole_RequiresArtistIdOnRequest_ThrowsWhenMissing`
  - `Handle_OwnerRole_ActsOnBehalfOfSpecifiedArtist`
  - `Handle_ScheduledForOmitted_SchedulesImmediateSend` (assert `IJobScheduler.ScheduleManualReminder`
    called with a `sendAt` at or before `DateTimeOffset.UtcNow`)
  - `Handle_ScheduledForFuture_SchedulesViaHangfireAtGivenTime`
  - `Handle_QuotaExceeded_PropagatesManualReminderQuotaExceededException` (mock
    `IManualReminderQuotaService` to throw, assert it propagates unchanged and no `ManualReminder`
    row is left in a bad state — decide/confirm whether the row should already be persisted before
    the quota check or not; this handler as drafted in 6a checks quota **before** constructing the
    row, so nothing is persisted on rejection — verify the final implementation preserves that
    order)

- `CancelManualReminderHandlerTests.cs`:
  - `Cancel_AlreadySent_ThrowsConflictException`
  - `Cancel_AlreadyCancelled_ThrowsConflictException`
  - `Cancel_StillScheduled_DeletesHangfireJobAndSetsCancelled`
  - `Cancel_ArtistNotOwner_ThrowsNotFoundException`
  - `Cancel_OwnerRole_BypassesOwnershipCheck`

- `GetManualRemindersHandlerTests.cs`:
  - `Handle_NoFilterProvided_ThrowsBusinessRuleViolationException`
  - `Handle_FilterByAppointmentId_ReturnsMatchingReminders`
  - `Handle_FilterByClientId_ReturnsMatchingReminders`

- `ManualReminderJobTests.cs` (mirror `AppointmentReminderJobTests.cs`'s existing test shapes if
  that file exists — check first):
  - `SendAsync_ReminderNotFound_LogsWarningAndReturns`
  - `SendAsync_AlreadyCancelled_SkipsSend`
  - `SendAsync_LinkedAppointmentCancelledSinceScheduling_SkipsSendAndMarksCancelled`
  - `SendAsync_ClientOptedOut_MarksFailedWithoutSending`
  - `SendAsync_NoClientLinked_WritesExternalContactRecipientType`
  - `SendAsync_ClientLinked_WritesClientRecipientType`
  - `SendAsync_SmsSucceeds_MarksSentAndWritesSuccessLog`
  - `SendAsync_SmsThrows_MarksFailedAndWritesFailureLog`

- `ManualReminderQuotaServiceTests.cs`:
  - `CheckAndIncrementAsync_UnderLimit_DoesNotThrow`
  - `CheckAndIncrementAsync_AtLimit_ThrowsManualReminderQuotaExceededException`
  - `CheckAndIncrementAsync_FirstCallOfDay_SetsExpiry`
  Use whatever Redis test double/fixture the existing Redis-touching tests already use (check
  `PlanLimitServiceTests.cs` or `RedisFixedWindowRateLimiterTests.cs` for the pattern) — do not
  hit a real Redis instance in CI.

`tests/Pena_e_Arte.IntegrationTests/`:
- `ManualReminderFlowIntegrationTests.cs`:
  - Full create → Hangfire schedule → job execution → `NotificationLog` + `ManualReminder`
    status flow, for both a `Client`-linked and a raw-contact reminder. Use whatever
    `INotificationService`/Twilio test double the integration base already substitutes — do
    **not** hit real Twilio in CI (same caution the existing notifications overnight prompt
    already documents).
  - Tenant isolation: a `ManualReminder` created in one studio's context must not be
    readable/cancellable from another tenant's `ICurrentTenant` scope.
  - Quota: the 21st `CreateManualReminderCommand` in one day for the same artist returns 429.

**Commit:** `test(reminders): add unit and integration test coverage`

---

## Part 9 — Frontend

### 9a. New `frontend/src/features/reminders/remindersApi.ts`

RTK Query slice (camelCase per `conventions.md`'s file-naming table), typed against the
Contracts DTOs from Part 5 — no `any`. Endpoints: `createManualReminder` (mutation),
`getManualReminders` (query, args `{ appointmentId?: string; clientId?: string }`),
`cancelManualReminder` (mutation). Match whichever existing RTK Query slice
(`appointmentsApi.ts` is a good model) for base-URL/tag-invalidation conventions — invalidate
the relevant reminders list tag on create/cancel.

### 9b. New `frontend/src/features/reminders/components/ReminderDialog.tsx`

A single shared dialog component, parameterized by how it's opened (`appointmentId`, `clientId`,
or neither — raw-contact mode). Contents:
- "Send now" / "Schedule for..." toggle (date/time picker — reuse whatever picker
  `RescheduleDialog.tsx` already uses; read that file first, do not add a new date-picker
  dependency).
- Optional custom message textarea, placeholder showing the default template text from Part
  3c's `BuildBody` logic (a static preview string is fine — it doesn't need to call the backend
  to preview).
- In raw-contact mode only: name + phone text inputs, required.
- Submit → `useCreateManualReminderMutation`, passing whichever of
  `appointmentId`/`clientId`/`recipientName`+`recipientPhone` applies.
- Below the form (when `appointmentId` or `clientId` is known): a small history list from
  `useGetManualRemindersQuery`, status badges (Scheduled/Sent/Failed/Cancelled), each
  `Scheduled` entry has its own small "Cancel" action wired to `useCancelManualReminderMutation`.

### 9c. `frontend/src/features/appointments/components/AppointmentDetailPage.tsx`

Add a "Send Reminder" button next to the existing appointment actions, opening `ReminderDialog`
with `appointmentId` set (recipient name/phone are implicit from the appointment's client — the
dialog does not ask for them in this mode).

### 9d. Client detail page

Verify the exact file name/path first (`frontend/src/features/clients/components/` — likely
`ClientDetailPage.tsx`, confirm by listing that directory). Add the same "Send Reminder" button,
opening `ReminderDialog` with `clientId` set.

### 9e. `SchedulePage.tsx` — "Quick Reminder" toolbar action (Decision 4)

Verify the exact file path first — it's referenced in `architecture.md`/`helpContent.ts` as the
artist's day/week schedule view; confirm via `frontend/src/features/appointments/components/` or
a dedicated `schedule` feature folder before assuming the path. Add a toolbar button that opens
`ReminderDialog` in raw-contact mode (no `appointmentId`, no `clientId` — the dialog shows the
name/phone inputs).

### 9f. `data-tour` attributes

If Section 9e's toolbar button sits inside the schedule page's existing nav/toolbar area, check
whether it falls within `artistTour.ts`'s existing `[data-tour="artist-schedule-nav"]` step's
target region. If it's a genuinely new, separately-clickable element outside that target's
bounding area, do not silently add a new tour step for it without reading `artistTour.ts` and
deciding deliberately — a small, secondary toolbar action does not necessarily need its own tour
step (this codebase's own `artistTour.ts` doesn't have a step for every single button, e.g. no
step for "Cancel appointment"). Default to **not** adding a new tour step unless the button is a
primary, easily-missed piece of new functionality — state which way you went and why in the
commit message.

**Commit:** `feat(reminders): add frontend UI for manual client reminders`

---

## Part 10 — Frontend tests

- `frontend/src/features/reminders/__tests__/ReminderDialog.test.tsx` (new):
  - Renders "Send now"/"Schedule for" toggle; date picker only shown in scheduled mode.
  - Raw-contact mode shows name/phone inputs; appointment/client mode does not.
  - Submit disabled until required fields are filled (name+phone in raw-contact mode).
  - History list renders status badges and a working Cancel action for `Scheduled` rows only.
- `AppointmentDetailPage.test.tsx`: new test asserting the "Send Reminder" button opens the
  dialog with `appointmentId` passed through.
- Client detail page test file (Part 9d): same pattern with `clientId`.
- `SchedulePage.test.tsx` (or wherever 9e landed): new test asserting the "Quick Reminder"
  button opens the dialog in raw-contact mode.

**Commit:** `test(reminders): add frontend test coverage`

---

## Part 11 — Help sync (CLAUDE.md rule 7 — not optional)

### 11a. `frontend/src/features/help/helpContent.ts`

New Artist-role article, e.g. `artist-send-manual-reminder`, covering both flows (appointment/
client-linked send, and the raw-contact "Quick Reminder" on the schedule page). Add
`relatedArticleIds` links to/from whatever existing appointment-detail article already exists.
If Decision 2 means owner/issuer can also trigger this, add or extend an Owner-role article too
— check whether an Owner-role appointment-detail article already exists to extend rather than
creating a duplicate.

### 11b. `frontend/public/user-manual/index.html`

New content inside the existing `<section id="artist-appointment-detail" data-role="artist">`
and (if a separate section exists) the client-detail equivalent, plus a new small addition to
whichever section documents the artist's schedule page, following this file's existing
markup/structure (read the surrounding section fully before editing, matching the
`get-directions` overnight prompt's precedent for how to extend this file's wireframe SVGs and
step lists precisely rather than freehand).

### 11c. `artistTour.ts`

See Part 9f — decided inline, not deferred.

**Commit:** `docs(reminders): update help content and user manual`

---

## Part 12 — Architecture doc updates

### 12a. Feature Module Map (`docs/claude/architecture.md`)

Add row **35** (the table currently ends at row 34):
```
| 35 | Manual Client Reminders | `ManualReminder` | Hangfire + Twilio (reused) + Redis (quota) | Per-tenant |
```

### 12b. IgnoreQueryFilters table

Already covered in Part 3d — extend row #36, do not add a new row.

### 12c. Decisions Log

Add a new entry at the end of the `## Decisions Log` table, summarizing: the additive
relationship to the existing automatic reminder pipeline; the three-way recipient resolution
(appointment/client/raw-contact); the deliberate bypass of `INotificationPreferenceService`; the
`SmsOptOut` field added without its enforcement webhook (flagged as a follow-up); the
`NotificationLog.RecipientId`/`ExternalContact` widening; the 20/day Redis-backed quota and its
fail-closed behavior (contrasted explicitly with the rate-limiter's fail-open default); and the
`IAuditableCommand` additions. Follow the existing table's row format and level of detail (see
the 2026-08-20 "Get Directions" entry for the right length/density to match).

**Commit:** `docs(reminders): update architecture.md feature map and decisions log`

---

## Out of Scope — flagged explicitly, not silently dropped

1. **Twilio inbound SMS webhook / STOP-reply handling.** `Client.SmsOptOut` is added and checked
   everywhere SMS goes out (Decision 7), but nothing in this prompt ever sets it to `true`. Real
   STOP-compliance requires a new anonymous Twilio-signature-validated inbound endpoint,
   correlating the replying phone number back to a `Client` (non-trivial across multiple
   tenants/studios if the same number texts more than one studio), and touching every existing
   SMS-sending handler's suppression check consistently. This is materially bigger than "add a
   reminder feature" and deserves its own dedicated prompt.
2. **Extracting a shared `FindArtistForUserAsync`-style helper.** This prompt's handlers inline
   the same ownership-check pattern already duplicated four times elsewhere in this codebase
   (Context, last bullet) rather than refactoring now — a real cleanup opportunity, but
   unrelated to shipping this feature and risks touching working code unnecessarily in the same
   change.
3. **Revisiting the 20/day quota number** once real production usage data exists. Chosen as a
   reasonable starting cap (Decision 6), not a permanent, data-validated number.
4. **A configurable per-studio quota override** (e.g. a higher limit for a studio that pays for
   it, or an owner-configurable cap) — the current cap is a single hardcoded constant. Worth
   revisiting if the 20/day default proves too restrictive for a real studio's workflow, but not
   built here.

---

## Build checklist

Run all of these before ending the session; every one must be clean:

```bash
# 1. Backend build (new entity/migration/job/handlers/endpoints)
dotnet build

# 2. Backend tests
dotnet test

# 3. Frontend type check
cd frontend && pnpm tsc --noEmit

# 4. Lint
pnpm lint

# 5. All frontend tests must pass (including every new Reminders/ReminderDialog/schedule test)
pnpm test --run

# 6. Frontend build
pnpm build
```

---

## Summary of Changes

### New features:
- Artist-triggered manual reminders — send now or schedule for a custom time, with an optional
  custom message, additive to the existing automatic 48h/24h reminder pipeline.
- Works for an existing (possibly unregistered — `UserId` null) `Client` record, an
  appointment's linked client, **or** a raw phone number typed on the spot with no `Client`
  record created at all.
- New "Send Reminder" action on the appointment detail page and the client detail page; new
  "Quick Reminder" toolbar action on the schedule page for the no-record case.
- Manual reminders appear in the existing studio notification bell feed identically to
  automatic ones (`NotificationLog` + `NotificationReceived` SignalR event, reused as-is).
- Per-artist daily quota (20/day, Redis-backed, fails closed) protects against runaway SMS cost.
- `Client.SmsOptOut` added and checked everywhere SMS is sent (automatic and manual) — the
  enforcement mechanism's trigger (an inbound STOP webhook) is flagged as a separate follow-up.

### Explicitly out of scope (see "Out of Scope" section above):
- Twilio inbound STOP webhook.
- Shared artist-resolution helper refactor.
- Quota-number validation against real usage / per-studio configurable quota.

### Help sync:
- `helpContent.ts`: new Artist-role article (and Owner-role if applicable).
- `user-manual/index.html`: extended appointment-detail, client-detail, and schedule sections.
- `artistTour.ts`: touched only if Part 9f's judgment call concludes the new schedule-page
  button needs its own step — state which way you went in the commit message either way.

---

## Hard Rules Reminder

- **Tenant isolation:** every new entity/query/command is tenant-scoped via the standard
  `ICurrentTenant`/query-filter mechanism. `ManualReminderJob` uses `IgnoreQueryFilters()`
  exactly like every other no-tenant-context Hangfire job (table row #36, extended not
  duplicated) — no new bypass pattern introduced.
- **RBAC:** `ArtistAndAbove` on all three new endpoints, `.RequireAuthorization()` — no
  unprotected endpoint.
- **Never log PII:** `ManualReminderJob`'s Serilog calls use `tenant_id`/`manual_reminder_id`,
  never `RecipientName`/`RecipientPhone` as log properties — `NotificationLog.Body` is the
  audit record for message content, not the structured logs.
- **Secrets never in source:** no new secrets — reuses existing Twilio/Redis configuration.
- **Structured logs only:** no `Console.WriteLine`/`console.log` introduced anywhere in this
  change.
- **Match current industry standards (rule 6):** artist-initiated reminder/messaging tools are
  standard in this category (Vagaro/Fresha/Boulevard all offer some form of manual client
  messaging alongside automated reminders) — this closes that gap. The still-open SMS-consent
  webhook gap is flagged explicitly (Out of Scope #1), not silently shipped as if solved.
  Redis-backed quota protection on a cost-bearing, freely-addressable SMS endpoint matches this
  same "don't ship a substandard pattern silently" rule — flagged and built, not deferred.
- **Every feature ships with Help-sync obligations in the same change** (Part 11) — done, with
  the tour-step judgment call stated explicitly either way, not silently skipped.
