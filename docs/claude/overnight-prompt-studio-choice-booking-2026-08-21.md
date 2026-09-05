# Overnight Prompt — Studio-Choice Booking

> Date: 2026-08-21
> Target: `Pena_e_Arte.Domain`, `Pena_e_Arte.Contracts`, `Pena_e_Arte.Application` (Appointments +
> Reminders), `Pena_e_Arte.Infrastructure` (one EF migration + one email template), `Pena_e_Arte.API`,
> `frontend/src/features/appointments`, backend + frontend tests, Help Menu (`helpContent.ts`),
> standalone user manual (`index.html`), `clientTour.ts`.
> One new EF Core migration (loosens an existing required column to nullable — zero-downtime,
> no data backfill, no existing rows have a NULL to worry about). No new npm or NuGet packages.
> Supersedes `docs/claude/feature-spec-studio-choice-booking-2026-08-21.md` — that spec's open
> questions have all been resolved below; do not re-litigate them.

---

## Pre-flight

1. Read `CLAUDE.md`, `docs/claude/backend.md`, `docs/claude/frontend.md`, `docs/claude/database.md`,
   `docs/claude/conventions.md` before making any changes.
2. Baseline, before touching anything:
   - `dotnet build`
   - `dotnet test` — note the current pass count; pre-existing failures are not this prompt's
     problem, but do not introduce new ones.
   - `pnpm tsc --noEmit`
   - `pnpm test src/features/appointments src/features/reminders` — confirm both suites are
     green first.
3. Read these files in full before starting Part 3 — this prompt's code samples assume you have:
   - `Pena_e_Arte.Application/Appointments/Commands/CreateAppointmentCommand.cs`
   - `Pena_e_Arte.Application/Reminders/Commands/CreateManualReminderCommand.cs` (Part 5 — this
     file lives in a completely different feature and breaks if you skip it; see Context)
   - `Pena_e_Arte.Application/Common/ClientAccountExtensions.cs` — the exact precedent Part 3a's
     new shared extension method mirrors.
   - `frontend/src/features/appointments/components/BookAppointmentForm.tsx` in full — Part 7's
     changes touch several non-adjacent parts of this file.

---

## Context — current state (verified against live source, 2026-08-21)

- `Appointment.ArtistId` is a non-nullable `Guid`; `Artist` navigation is non-nullable. Every part
  of the booking pipeline — `CreateAppointmentCommand`, `CheckSlotAvailabilityQuery`,
  `RescheduleAppointmentCommand` — is keyed on one specific artist. There is no "studio picks the
  artist" path anywhere today.
- `CreateAppointmentCommand`'s conflict check acquires an `ISlotLocker` lock keyed
  `(studioId, artistId, date)` and computes `DepositAmount` via
  `DepositCalculator.Calculate(rule, artist.HourlyRate, duration)` — a percent-based rule with a
  `null` rate already returns `0m` with no crash (`Domain/Services/DepositCalculator.cs`); a
  fixed-amount rule is unaffected by artist rate either way. **No code change needed there.**
- `AppointmentResponse` has no `ArtistName` at all today (unlike `ClientName`), and
  `AppointmentDetailPage.tsx`/`AppointmentCard.tsx` render no Artist field whatsoever.
- `BookAppointmentForm.tsx`'s deposit step (`booked.depositAmount > 0`) already no-ops cleanly
  when the amount is `0` — a studio-choice booking under a percent rule sails to confirmation
  with no code change needed for that specifically. `MyBookingsSection.tsx`'s `DepositArea`
  reactively re-renders off `appt.depositAmount`/`depositStatus`, so a deposit that becomes
  payable later (Part 3g) surfaces with **no frontend change needed there either**.
- **`Pena_e_Arte.Application/Reminders/Commands/CreateManualReminderCommand.cs`, a file in a
  completely different feature folder, breaks the moment `Appointment.ArtistId` becomes
  nullable** — verified by reading it in full:
  - Line ~75: `if (isArtist && appointment.Artist.UserId != currentUser.UserId)` — null-refs if
    `appointment.Artist` is `null`.
  - Line ~91: `resolvedArtistId = appointment.ArtistId;` assigns a `Guid?` to a non-nullable
    `Guid resolvedArtistId` — **will not compile** once `Appointment.ArtistId` is `Guid?`.

  Part 5 below is not optional cleanup — without it the solution does not build.
- `ReminderDialog.tsx`'s `artistId` prop is **already optional** (`artistId?: string`) — the only
  change needed at that call site is `appt.artistId ?? undefined` for the type, once
  `AppointmentResponse.artistId` is nullable.
- `AppointmentConfiguration.cs`'s `HasOne(a => a.Artist).WithMany(...).HasForeignKey(a => a.ArtistId)`
  has **no explicit `.IsRequired(false)` call today, and needs none added** — EF Core infers the
  FK's required-ness from the nullable CLR type automatically, exactly as `ClientConfiguration.cs`
  already relies on for `Client.ArtistId` (added 2026-08-20, same pattern, no explicit call there
  either). Do not add one.
- `frontend/src/shared/components/ui/toggle-switch.tsx` exists and is the right primitive for
  Part 7's new toggle — its API is `{ checked: boolean; onChange: () => void; disabled?: boolean;
  "aria-label": string }` (a no-argument toggle callback, not `onCheckedChange(value)` — verified
  by reading the component).
- Pre-existing, out of scope, flagged and not fixed here: `GetAppointmentQuery` (singular),
  `ConfirmAppointmentCommand`, `CompleteAppointmentCommand`, and `MarkNoShowCommand` have no check
  that an Artist-role caller is acting on their own appointment — any artist at the studio can
  already view or act on a colleague's appointment by navigating directly to its id, since
  `AppointmentEndpoints.cs` only gates by rank (`ArtistAndAbove`), not ownership. This predates
  this feature and isn't made worse by it. Separate hardening pass; do not fold in here.

---

## Decisions (already made with the product owner — do not re-litigate)

| # | Decision | Rationale |
|---|---|---|
| 1 | A client (or staff booking on a client's behalf) can either pick a specific artist (unchanged) or toggle "let the studio choose" — no artist selection, `ArtistId` sent as `null`. | Confirmed. The whole point of this prompt. |
| 2 | Studio-choice availability at booking/reschedule time is **any-artist**: at least one active artist must have open schedule, no time-off, and no conflicting appointment. Checked across all active artists, same rigor as the existing single-artist check. | Confirmed. |
| 3 | Artist assignment is **required before Confirm**. No new `AppointmentStatus` enum value — "needs artist" is computed as `Status == Pending && ArtistId == null`, never stored. | Confirmed. Minimal invasive change; avoids touching every status-based switch across frontend/backend for a state that's really "Pending, missing one field." |
| 4 | New endpoint `PATCH /api/v1/appointments/{id}/artist`, `OwnerOnly`, `AssignAppointmentArtistCommand`. Accepts a **required** `ArtistId` — assignment only moves forward, no unassign-back-to-null. Re-validates that specific artist's schedule/time-off/conflict at the appointment's exact date/time under a real `ISlotLocker` lock, independent of the softer any-artist check done at booking time. | Mirrors `UpdateClientArtistCommand`'s `OwnerOnly` precedent (roster/staff-assignment action). Re-validation is necessary because "someone was free" at booking time doesn't guarantee this specific artist is. |
| 5 | `AssignAppointmentArtistCommand` recomputes and persists `DepositAmount` when it's currently `0` and `DepositStatus == Pending` (a percent rule had no rate to work from at booking time). A fixed-amount-rule booking is already correct and untouched. | Confirmed. Makes the existing "Charge deposit" button and `MyBookingsSection`'s reactive deposit UI pick up the real amount automatically — no other frontend change needed. |
| 6 | `CreateAppointmentCommand` does **not** acquire an `ISlotLocker` lock for a studio-choice booking. | No specific artist resource is claimed yet — the real single-resource claim happens in `AssignAppointmentArtistCommand` (#4), which does lock. |
| 7 | `AppointmentResponse` gains `ArtistName` (nullable, trailing optional param), denormalized exactly like `ClientResponse.ArtistName`. | Needed to render "Unassigned" / the artist's name — currently absent entirely. |
| 8 | `AssignAppointmentArtistCommand` implements `IAuditableCommand`, new `AuditActions.AppointmentArtistAssigned` constant. | Follows the `ClientArtistReassigned` precedent — a staff-roster mutation logged next to its sibling. |
| 9 | The shared "is any active artist available" check lives as a **static extension method on `IAppDbContext`** in `Application/Common/`, not in `Domain/Services/`. | `Domain` must not depend on `Application.Persistence` (where `IAppDbContext` lives) — `DepositCalculator`'s home in `Domain/Services` only works because it's a pure calculator with zero DB access. `ClientAccountExtensions.cs` is the exact existing precedent for this shape. |
| 10 | `AssignAppointmentArtistCommand`'s specific-artist schedule/time-off/conflict validation is a **fresh, duplicated copy** of `CreateAppointmentCommand`'s existing inline logic — not refactored into a shared method. | Minimizes risk to the already-working, tested single-artist booking path. The codebase already tolerates a very similar duplication between `CreateAppointmentCommand` and `CheckSlotAvailabilityQuery` today. A dedup pass is optional future cleanup, not part of this prompt. |
| 11 | `CreateManualReminderCommand.cs` (Reminders feature): an artist-role caller referencing an appointment with no assigned artist gets `NotFoundException` (matches the existing "not yours" 404 convention). An owner/issuer-role caller gets `BusinessRuleViolationException("Assign an artist to this appointment before sending a reminder.")`. | The only two call sites that break on compile/null-ref (see Context) — this is the narrowest fix that keeps both existing behaviors' spirit intact. |
| 12 | The client is emailed when an artist is assigned (`SendAppointmentArtistAssignedNotificationCommand`, reuses the `AppointmentCreated` notification-preference toggle). The newly assigned artist is **not** separately emailed in this pass — they'll see the appointment appear in their own schedule via the existing real-time event the moment they're assigned. | Keeps this prompt's blast radius contained to the actual product problem (the client not knowing who their artist is). An artist-facing email is a small, separate follow-up if wanted later. |
| 13 | The "Needs artist" queue for the Owner is a badge on `SchedulePage.tsx`/`AppointmentCard.tsx` plus the assign control on `AppointmentDetailPage.tsx` — no dedicated dashboard list in this pass. | Confirmed scope for v1; a dashboard addition is a separate, larger follow-up. |

---

## Part 1 — Domain + EF Core

### 1a. `Pena_e_Arte.Domain/Entities/Appointment.cs`

```csharp
public Guid? ArtistId { get; set; }
...
public Artist? Artist { get; set; } = null!; // ← remove the `= null!`; Artist? Artist { get; set; }
```

Concretely: change `public Guid ArtistId { get; set; }` → `public Guid? ArtistId { get; set; }`, and
`public Artist Artist { get; set; } = null!;` → `public Artist? Artist { get; set; }`.

### 1b. `Pena_e_Arte.Infrastructure/Persistence/Configurations/AppointmentConfiguration.cs`

**No change.** EF Core infers the FK's nullability from the `Guid?` CLR type automatically — see
Context. Do not add an explicit `.IsRequired(false)` call; it isn't needed and doesn't match this
codebase's existing pattern for the identical situation on `ClientConfiguration.cs`.

### 1c. Migration

```bash
dotnet ef migrations add AllowNullArtistIdOnAppointment \
  --project Pena_e_Arte.Infrastructure \
  --startup-project Pena_e_Arte.API
```

Verify the generated migration only drops the `NOT NULL` constraint on `artist_id` — no FK
constraint drop/recreate, no index changes, nothing else. Apply it locally
(`dotnet ef database update ...`) and confirm the app still boots before moving on.

---

## Part 2 — Contracts

### 2a. `Pena_e_Arte.Contracts/Requests/CreateAppointmentRequest.cs`

```csharp
public record CreateAppointmentRequest(
    Guid? ArtistId,
    Guid ClientId,
    DateTime Date,
    int DurationMinutes,
    string? Notes,
    IReadOnlyList<string>? ImageUrls = null);
```

`ArtistId` moves from the 1st non-nullable positional param to nullable — since it's not a
*trailing* param, this is a source-breaking type change for any call site constructing this
record positionally with a bare `Guid` and expecting exact-type inference in generic contexts;
in practice, passing a `Guid` value where `Guid?` is expected compiles fine via implicit
widening, so existing call sites (mostly tests) keep compiling unchanged. Grep for
`new CreateAppointmentRequest(` to confirm nothing breaks; fix anything the compiler flags.

### 2b. `Pena_e_Arte.Contracts/Responses/AppointmentResponse.cs`

```csharp
public record AppointmentResponse(
    Guid Id,
    Guid StudioId,
    Guid? ArtistId,
    Guid ClientId,
    DateTime Date,
    DateTime EndDate,
    int DurationMinutes,
    string Status,
    string DepositStatus,
    decimal DepositAmount,
    string? Notes,
    DateTime CreatedAt,
    string? CancellationReason = null,
    DateTime? AftercareSentAt = null,
    string? ClientName = null,
    IReadOnlyList<string>? ImageUrls = null,
    string? ArtistName = null);
```

Only `ArtistId`'s type changes in place (same reasoning as 2a — existing callers passing a `Guid`
keep compiling); `ArtistName` is a new trailing optional param, so every other existing
positional `new AppointmentResponse(...)` call site keeps compiling unchanged.

### 2c. New file — `Pena_e_Arte.Contracts/Requests/AssignAppointmentArtistRequest.cs`

```csharp
namespace Pena_e_Arte.Contracts.Requests;

public record AssignAppointmentArtistRequest(Guid ArtistId);
```

---

## Part 3 — Application layer (Appointments)

### 3a. New file — `Pena_e_Arte.Application/Common/ArtistAvailabilityExtensions.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Common;

public static class ArtistAvailabilityExtensions
{
    /// <summary>
    /// True if at least one active artist at the (query-filter-scoped) studio has open
    /// schedule, no time-off, and no conflicting appointment covering
    /// [date, date + durationMinutes). Used only for the "book with the studio" path, where
    /// no specific artist has been chosen yet — this is a soft, advisory check.
    /// AssignAppointmentArtistCommand re-validates the specific artist actually chosen at
    /// assignment time, under a real per-artist lock, independent of this.
    /// No explicit studioId parameter — db.Artists/ArtistSchedules/ArtistTimeOffs/Appointments
    /// are already tenant-scoped by EF Core's global query filters, exactly as
    /// CreateAppointmentCommand's existing single-artist checks rely on today.
    /// </summary>
    public static async Task<bool> IsAnyArtistAvailableAsync(
        this IAppDbContext db, DateTime date, int durationMinutes, CancellationToken ct)
    {
        DateTime end = date.AddMinutes(durationMinutes);
        DayOfWeek day = date.DayOfWeek;
        TimeSpan startTime = date.TimeOfDay;
        TimeSpan endTime = end.TimeOfDay;

        bool studioClosed = await db.StudioClosures.AnyAsync(
            c => c.StartDate <= date.Date && c.EndDate >= date.Date, ct);
        if (studioClosed) return false;

        List<Guid> candidateArtistIds = await db.Artists
            .Where(a => a.IsActive)
            .Select(a => a.Id)
            .ToListAsync(ct);

        foreach (Guid artistId in candidateArtistIds)
        {
            bool hasSchedule = await db.ArtistSchedules.AnyAsync(
                s => s.ArtistId == artistId && s.DayOfWeek == day && s.IsAvailable
                     && startTime >= s.StartTime && endTime <= s.EndTime, ct);
            if (!hasSchedule) continue;

            bool onTimeOff = await db.ArtistTimeOffs.AnyAsync(
                t => t.ArtistId == artistId && t.StartDate <= date.Date && t.EndDate >= date.Date, ct);
            if (onTimeOff) continue;

            bool conflict = await db.Appointments.AnyAsync(
                a => a.ArtistId == artistId && a.Date < end && a.EndDate > date
                     && a.Status != AppointmentStatus.Cancelled, ct);
            if (conflict) continue;

            return true; // this artist clears all three checks
        }

        return false;
    }
}
```

Note: this runs up to 3 queries per active artist. Accepted tradeoff for v1 (booking isn't a
high-frequency path, and studios in this product are small) — not a target for premature
optimization.

### 3b. `Pena_e_Arte.Application/Appointments/Commands/CreateAppointmentCommand.cs` — handler replacement

```csharp
public async Task<AppointmentResponse> Handle(CreateAppointmentCommand command, CancellationToken ct)
{
    CreateAppointmentRequest req = command.Request;

    Guid clientId;
    if (currentUser.Role == "client")
    {
        Client client = await db.FindClientForUserAsync(currentUser, ct)
            ?? throw new NotFoundException(nameof(Client), currentUser.UserId);
        clientId = client.Id;
    }
    else
    {
        clientId = req.ClientId;
    }

    DateTime requestEnd = req.Date.AddMinutes(req.DurationMinutes);

    Artist? artist = null;
    if (req.ArtistId is Guid artistId)
    {
        // ── Specific-artist path — UNCHANGED from today ──
        artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == artistId, ct)
            ?? throw new NotFoundException(nameof(Artist), artistId);

        DayOfWeek requestDay = req.Date.DayOfWeek;
        TimeSpan requestStart = req.Date.TimeOfDay;
        TimeSpan requestEndTime = requestEnd.TimeOfDay;

        var scheduleEntry = await db.ArtistSchedules
            .Where(s => s.ArtistId == artistId && s.DayOfWeek == requestDay && s.IsAvailable)
            .FirstOrDefaultAsync(ct);

        if (scheduleEntry is null)
            throw new BusinessRuleViolationException($"The artist is not available on {requestDay}.");

        if (requestStart < scheduleEntry.StartTime || requestEndTime > scheduleEntry.EndTime)
            throw new BusinessRuleViolationException(
                $"Appointment time is outside the artist's working hours ({scheduleEntry.StartTime:hh\\:mm}–{scheduleEntry.EndTime:hh\\:mm}).");

        bool onTimeOff = await db.ArtistTimeOffs.AnyAsync(
            t => t.ArtistId == artistId &&
                 t.StartDate <= req.Date.Date &&
                 t.EndDate >= req.Date.Date, ct);

        if (onTimeOff)
            throw new BusinessRuleViolationException("The artist is on leave on the requested date.");
    }
    else
    {
        // ── Studio-choice path — new. Soft "someone can do this" check; no specific artist
        // resource is claimed here (Decision #6) — the real per-artist claim happens in
        // AssignAppointmentArtistCommand. ──
        bool anyoneAvailable = await db.IsAnyArtistAvailableAsync(req.Date, req.DurationMinutes, ct);

        if (!anyoneAvailable)
            throw new BusinessRuleViolationException(
                "No artist is available at that date and time. Please choose a different slot.");
    }

    bool locked = req.ArtistId is Guid lockArtistId
        && await slotLocker.TryAcquireLockAsync(tenant.StudioId, lockArtistId, req.Date, ct);

    if (req.ArtistId is not null && !locked) throw new SlotAlreadyBookedException();

    try
    {
        if (req.ArtistId is Guid checkArtistId)
        {
            bool conflict = await db.Appointments.AnyAsync(a =>
                a.ArtistId == checkArtistId &&
                a.Date < requestEnd &&
                a.EndDate > req.Date &&
                a.Status != AppointmentStatus.Cancelled, ct);

            if (conflict) throw new SlotAlreadyBookedException();
        }

        // Single-active is enforced by the deposit rule handlers; ordering by
        // UpdatedAt keeps selection deterministic even against legacy data.
        DepositRule? rule = await db.DepositRules
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        decimal depositAmount = DepositCalculator.Calculate(rule, artist?.HourlyRate, req.DurationMinutes);

        Appointment appointment = new()
        {
            StudioId = tenant.StudioId,
            ArtistId = artist?.Id,
            ClientId = clientId,
            Date = req.Date,
            EndDate = requestEnd,
            DurationMinutes = req.DurationMinutes,
            Status = AppointmentStatus.Pending,
            DepositStatus = DepositStatus.Pending,
            DepositAmount = depositAmount,
            Notes = req.Notes
        };

        foreach (string imageUrl in req.ImageUrls ?? [])
        {
            appointment.Attachments.Add(new AppointmentAttachment
            {
                StudioId = tenant.StudioId,
                ImageUrl = imageUrl,
                UploadedAt = DateTime.UtcNow
            });
        }

        db.Appointments.Add(appointment);
        await db.SaveChangesAsync(ct);

        await planLimits.InvalidateUsageCacheAsync(QuotaType.AppointmentsPerMonth, ct);

        appointment.ReminderJobId48h = jobs.ScheduleAppointmentReminder(
            appointment.Id, "48h", appointment.Date.AddHours(-48));
        appointment.ReminderJobId24h = jobs.ScheduleAppointmentReminder(
            appointment.Id, "24h", appointment.Date.AddHours(-24));
        await db.SaveChangesAsync(ct);

        AppointmentResponse response = Map(appointment);
        await realtime.NotifyStudioAsync(tenant.StudioId, "AppointmentCreated", response, ct);

        await sender.Send(new SendAppointmentCreatedNotificationCommand(appointment.Id), ct);

        return response;
    }
    finally
    {
        if (req.ArtistId is Guid unlockArtistId)
            await slotLocker.ReleaseLockAsync(tenant.StudioId, unlockArtistId, req.Date, ct);
    }
}

internal static AppointmentResponse Map(Appointment a, string? clientName = null, string? artistName = null) => new(
    a.Id, a.StudioId, a.ArtistId, a.ClientId,
    a.Date, a.EndDate, a.DurationMinutes,
    a.Status.ToString(), a.DepositStatus.ToString(),
    a.DepositAmount, a.Notes, a.CreatedAt,
    a.CancellationReason?.ToString(),
    a.AftercareSentAt,
    clientName,
    a.Attachments.OrderBy(x => x.UploadedAt).Select(x => x.ImageUrl).ToList(),
    artistName);
```

Only `Map`'s signature changed (`artistName` param added, defaulted `null`). Every other call
site (`GetAppointmentsQuery`, `GetAppointmentQuery`, and the new `AssignAppointmentArtistCommand`)
is updated below to pass its own resolved artist name through.

### 3c. `Pena_e_Arte.Application/Appointments/Validators/CreateAppointmentValidator.cs`

Remove `RuleFor(x => x.Request.ArtistId).NotEmpty();` entirely — `ArtistId` is now legitimately
nullable, and there's no replacement rule needed (a `Guid?` with no value is exactly the valid
studio-choice case).

### 3d. `Pena_e_Arte.Application/Appointments/Queries/CheckSlotAvailabilityQuery.cs`

`ArtistId` → `Guid?`. Handler — add the branch at the top, leave everything after it as-is
(just declare `Guid artistId = query.ArtistId.Value;` at the top of the unchanged section so the
rest of the method needs no further edits):

```csharp
public record CheckSlotAvailabilityQuery(
    Guid?    ArtistId,
    DateTime Date,
    int      DurationMinutes)
    : IRequest<SlotAvailabilityResult>;

public class CheckSlotAvailabilityHandler(IAppDbContext db)
    : IRequestHandler<CheckSlotAvailabilityQuery, SlotAvailabilityResult>
{
    public async Task<SlotAvailabilityResult> Handle(
        CheckSlotAvailabilityQuery query, CancellationToken ct)
    {
        if (query.ArtistId is null)
        {
            bool anyAvailable = await db.IsAnyArtistAvailableAsync(query.Date, query.DurationMinutes, ct);
            return anyAvailable
                ? new SlotAvailabilityResult(true, null)
                : new SlotAvailabilityResult(false, "No artist is available at that time.");
        }

        Guid artistId = query.ArtistId.Value;
        DateTime end = query.Date.AddMinutes(query.DurationMinutes);

        DayOfWeek day = query.Date.DayOfWeek;
        TimeSpan startTime = query.Date.TimeOfDay;
        TimeSpan endTime = end.TimeOfDay;

        bool studioClosed = await db.StudioClosures.AnyAsync(
            c => c.StartDate <= query.Date.Date &&
                 c.EndDate >= query.Date.Date, ct);

        if (studioClosed)
            return new SlotAvailabilityResult(false, "Studio is closed that day.");

        var schedule = await db.ArtistSchedules
            .Where(s => s.ArtistId == artistId &&
                        s.DayOfWeek == day &&
                        s.IsAvailable)
            .FirstOrDefaultAsync(ct);

        if (schedule is null)
            return new SlotAvailabilityResult(false, $"Artist is not available on {day}s.");

        if (startTime < schedule.StartTime || endTime > schedule.EndTime)
            return new SlotAvailabilityResult(false,
                $"Outside artist's hours ({schedule.StartTime:hh\\:mm}–{schedule.EndTime:hh\\:mm}).");

        bool onLeave = await db.ArtistTimeOffs.AnyAsync(
            t => t.ArtistId == artistId &&
                 t.StartDate <= query.Date.Date &&
                 t.EndDate >= query.Date.Date, ct);

        if (onLeave)
            return new SlotAvailabilityResult(false, "Artist is on leave that day.");

        bool conflict = await db.Appointments.AnyAsync(a =>
            a.ArtistId == artistId &&
            a.Date < end &&
            a.EndDate > query.Date &&
            a.Status != AppointmentStatus.Cancelled, ct);

        if (conflict)
            return new SlotAvailabilityResult(false, "That slot is already booked.");

        return new SlotAvailabilityResult(true, null);
    }
}
```

`AppointmentEndpoints.cs`'s `CheckSlotAvailability` handler: `artistId` route param becomes
`Guid? artistId` (minimal-API model binding already treats an absent query param as `null` for
a nullable `Guid?` — no `[FromQuery]` attribute changes needed).

### 3e. `Pena_e_Arte.Application/Appointments/Commands/RescheduleAppointmentCommand.cs`

Replace the conflict-check block:

```csharp
bool conflict = appointment.ArtistId is Guid artistId
    ? await db.Appointments.AnyAsync(a =>
        a.Id != command.AppointmentId &&
        a.ArtistId == artistId &&
        a.Date < newEnd &&
        a.EndDate > req.NewDate &&
        a.Status != AppointmentStatus.Cancelled, ct)
    : !await db.IsAnyArtistAvailableAsync(req.NewDate, req.NewDurationMinutes, ct);

if (conflict) throw new SlotAlreadyBookedException();
```

Flagged explicitly (per this prompt's source spec): without this branch,
`a.ArtistId == appointment.ArtistId` where `appointment.ArtistId` is `null` translates to
`a.artist_id IS NULL` — rescheduling one unassigned appointment would spuriously
conflict-check against every *other* unassigned appointment at an overlapping time, which is
wrong (nothing has actually claimed either slot yet).

### 3f. `Pena_e_Arte.Application/Appointments/Commands/ConfirmAppointmentCommand.cs`

Add, immediately after the existing `Status != Pending` check:

```csharp
if (appointment.Status != AppointmentStatus.Pending)
    throw new BusinessRuleViolationException(
        $"Only Pending appointments can be confirmed (current: {appointment.Status}).");

if (appointment.ArtistId is null)
    throw new BusinessRuleViolationException(
        "Assign an artist before confirming this appointment.");

appointment.Status = AppointmentStatus.Confirmed;
```

Server-side enforcement per Decision #3 and this codebase's established "never trust the
frontend gate alone" convention.

### 3g. New file — `Pena_e_Arte.Application/Appointments/Commands/AssignAppointmentArtistCommand.cs`

```csharp
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.Application.Appointments.Commands;

public record AssignAppointmentArtistCommand(Guid AppointmentId, AssignAppointmentArtistRequest Request)
    : IRequest<AppointmentResponse>, IAuditableCommand
{
    public string AuditAction => AuditActions.AppointmentArtistAssigned;
    public string AuditTargetType => AuditTargetTypes.Appointment;
    public Guid AuditTargetId => AppointmentId;
}

public class AssignAppointmentArtistHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    ISlotLocker slotLocker,
    IRealtimeNotifier realtime,
    ISender sender)
    : IRequestHandler<AssignAppointmentArtistCommand, AppointmentResponse>
{
    public async Task<AppointmentResponse> Handle(AssignAppointmentArtistCommand command, CancellationToken ct)
    {
        Appointment appointment = await db.Appointments
            .Include(a => a.Client)
            .Include(a => a.Attachments)
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct)
            ?? throw new NotFoundException(nameof(Appointment), command.AppointmentId);

        if (appointment.Status is AppointmentStatus.Cancelled
                                or AppointmentStatus.Completed
                                or AppointmentStatus.NoShow)
            throw new BusinessRuleViolationException(
                $"Cannot assign an artist to a {appointment.Status} appointment.");

        Guid artistId = command.Request.ArtistId;

        Artist artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == artistId, ct)
            ?? throw new NotFoundException(nameof(Artist), artistId);
        if (!artist.IsActive)
            throw new BusinessRuleViolationException("Cannot assign an inactive artist.");

        // Mirrors CreateAppointmentCommand's specific-artist validation exactly — a fresh
        // copy, not a shared extraction (Decision #10), to avoid touching that already-
        // working, tested path.
        DayOfWeek day = appointment.Date.DayOfWeek;
        TimeSpan startTime = appointment.Date.TimeOfDay;
        TimeSpan endTime = appointment.EndDate.TimeOfDay;

        var scheduleEntry = await db.ArtistSchedules
            .Where(s => s.ArtistId == artistId && s.DayOfWeek == day && s.IsAvailable)
            .FirstOrDefaultAsync(ct);

        if (scheduleEntry is null)
            throw new BusinessRuleViolationException($"This artist is not available on {day}.");

        if (startTime < scheduleEntry.StartTime || endTime > scheduleEntry.EndTime)
            throw new BusinessRuleViolationException(
                $"Appointment time is outside this artist's working hours ({scheduleEntry.StartTime:hh\\:mm}–{scheduleEntry.EndTime:hh\\:mm}).");

        bool onTimeOff = await db.ArtistTimeOffs.AnyAsync(
            t => t.ArtistId == artistId &&
                 t.StartDate <= appointment.Date.Date &&
                 t.EndDate >= appointment.Date.Date, ct);

        if (onTimeOff)
            throw new BusinessRuleViolationException("This artist is on leave on the appointment's date.");

        bool locked = await slotLocker.TryAcquireLockAsync(tenant.StudioId, artistId, appointment.Date, ct);
        if (!locked) throw new SlotAlreadyBookedException();

        try
        {
            bool conflict = await db.Appointments.AnyAsync(a =>
                a.Id != appointment.Id &&
                a.ArtistId == artistId &&
                a.Date < appointment.EndDate &&
                a.EndDate > appointment.Date &&
                a.Status != AppointmentStatus.Cancelled, ct);

            if (conflict) throw new SlotAlreadyBookedException();

            appointment.ArtistId = artist.Id;

            // Decision #5: recompute a deferred deposit (a percent rule had no artist rate
            // to work from at booking time) now that a real rate is known. A fixed-amount
            // rule was already correct at booking and is untouched by this condition.
            if (appointment.DepositAmount == 0m && appointment.DepositStatus == DepositStatus.Pending)
            {
                DepositRule? rule = await db.DepositRules
                    .Where(r => r.IsActive)
                    .OrderByDescending(r => r.UpdatedAt)
                    .FirstOrDefaultAsync(ct);

                appointment.DepositAmount =
                    DepositCalculator.Calculate(rule, artist.HourlyRate, appointment.DurationMinutes);
            }

            appointment.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            AppointmentResponse response = CreateAppointmentHandler.Map(
                appointment,
                clientName: $"{appointment.Client.FirstName} {appointment.Client.LastName}",
                artistName: $"{artist.FirstName} {artist.LastName}");

            await realtime.NotifyStudioAsync(tenant.StudioId, "AppointmentArtistAssigned", response, ct);
            await sender.Send(new SendAppointmentArtistAssignedNotificationCommand(appointment.Id), ct);

            return response;
        }
        finally
        {
            await slotLocker.ReleaseLockAsync(tenant.StudioId, artistId, appointment.Date, ct);
        }
    }
}

public class AssignAppointmentArtistValidator : AbstractValidator<AssignAppointmentArtistCommand>
{
    public AssignAppointmentArtistValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
        RuleFor(x => x.Request.ArtistId).NotEmpty();
    }
}
```

### 3h. `Pena_e_Arte.Application/Appointments/Queries/GetAppointmentsQuery.cs`

Add `.Include(a => a.Artist)` alongside the existing `.Include(a => a.Attachments)`, and extend
the projection:

```csharp
.Select(a => CreateAppointmentHandler.Map(
    a,
    a.Client.FirstName + " " + a.Client.LastName,
    a.Artist != null ? a.Artist.FirstName + " " + a.Artist.LastName : null))
```

If this doesn't translate/compile as an in-query projection (verify — the existing single-name
version already does this today, so it likely continues to work, but confirm), fall back to
materialize-then-map: `ToListAsync()` first, then `.Select(a => CreateAppointmentHandler.Map(...))`
on the in-memory list — the exact fallback `GetClientsQuery`'s handler already uses for the
identical situation (see `docs/claude/overnight-prompt-client-artist-assignment-2026-08-20.md`
Part 3c).

### 3i. `Pena_e_Arte.Application/Appointments/Queries/GetAppointmentQuery.cs`

```csharp
public async Task<AppointmentResponse> Handle(GetAppointmentQuery query, CancellationToken ct)
{
    Domain.Entities.Appointment appointment = await db.Appointments
        .Include(a => a.Client)
        .Include(a => a.Artist)
        .Include(a => a.Attachments)
        .FirstOrDefaultAsync(a => a.Id == query.AppointmentId, ct)
        ?? throw new NotFoundException(nameof(Domain.Entities.Appointment), query.AppointmentId);

    return CreateAppointmentHandler.Map(
        appointment,
        $"{appointment.Client.FirstName} {appointment.Client.LastName}",
        appointment.Artist is not null
            ? $"{appointment.Artist.FirstName} {appointment.Artist.LastName}"
            : null);
}
```

### 3j. `Pena_e_Arte.API/Endpoints/AppointmentEndpoints.cs`

Add, alongside the other `{id:guid}`-scoped routes:

```csharp
group.MapPatch("{id:guid}/artist", AssignAppointmentArtist).RequireAuthorization("OwnerOnly");
```

```csharp
private static async Task<IResult> AssignAppointmentArtist(
    Guid id,
    AssignAppointmentArtistRequest request,
    ISender mediator,
    CancellationToken ct)
{
    AppointmentResponse result = await mediator.Send(new AssignAppointmentArtistCommand(id, request), ct);
    return Results.Ok(result);
}
```

Also update `CheckSlotAvailability`'s handler signature (Part 3d): `Guid? artistId` instead of
`Guid artistId`, and pass it straight through to `new CheckSlotAvailabilityQuery(artistId, ...)`.

### 3k. `Pena_e_Arte.Domain/Constants/AuditActions.cs`

Add one constant:

```csharp
public const string AppointmentArtistAssigned = "Appointment.ArtistAssigned";
```

`AuditTargetTypes.Appointment` already exists — no change needed there.

---

## Part 4 — Notifications

### 4a. `Pena_e_Arte.Domain/Interfaces/IEmailRenderer.cs`

Add one method to the interface, placed near `RenderAppointmentConfirmation`:

```csharp
string RenderAppointmentArtistAssigned(
    string clientFirstName,
    string artistFullName,
    DateTime date,
    string studioName,
    bool showBranding);
```

Implement it in the concrete `EmailRenderer` class (search `Pena_e_Arte.Infrastructure` for
`class EmailRenderer` or `: IEmailRenderer`) — mirror `RenderAppointmentCreatedClient`'s existing
template structure and styling exactly; do not invent a different visual pattern for this one
email.

### 4b. New file — `Pena_e_Arte.Application/Appointments/Commands/SendAppointmentArtistAssignedNotificationCommand.cs`

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Notifications.Queries;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Appointments.Commands;

public record SendAppointmentArtistAssignedNotificationCommand(Guid AppointmentId) : IRequest<Unit>;

public class SendAppointmentArtistAssignedNotificationHandler(
    IAppDbContext db,
    IEmailRenderer emailRenderer,
    INotificationService notifications,
    INotificationPreferenceService prefs,
    ILogger<SendAppointmentArtistAssignedNotificationHandler> logger)
    : IRequestHandler<SendAppointmentArtistAssignedNotificationCommand, Unit>
{
    public async Task<Unit> Handle(SendAppointmentArtistAssignedNotificationCommand command, CancellationToken ct)
    {
        Appointment? appointment = await db.Appointments
            .Include(a => a.Client)
            .Include(a => a.Artist)
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct);

        if (appointment is null || appointment.Artist is null)
        {
            logger.LogWarning(
                "Appointment {@AppointmentId} not found or has no artist for artist-assigned notification",
                command.AppointmentId);
            return Unit.Value;
        }

        Studio? studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == appointment.StudioId, ct);
        if (studio is null) return Unit.Value;

        // Reuses the AppointmentCreated preference toggle — this is a follow-up to the same
        // "your booking" thread the client already opted into at booking time, not a
        // distinct notification category. Verify NotificationType.cs still reads correctly
        // for this before assuming; add a dedicated enum value instead if it doesn't fit.
        bool emailEnabled = await prefs.IsEnabledAsync(
            studio.Id, NotificationType.AppointmentCreated, NotificationChannel.Email, ct);

        if (!emailEnabled) return Unit.Value;

        string body = emailRenderer.RenderAppointmentArtistAssigned(
            appointment.Client.FirstName,
            $"{appointment.Artist.FirstName} {appointment.Artist.LastName}",
            appointment.Date,
            studio.Name,
            studio.ShowPlatformBranding);

        string subject = $"Your artist has been assigned — {studio.Name}";
        bool success = true;
        try
        {
            await notifications.SendEmailAsync(appointment.Client.Email, subject, body, ct);
        }
        catch (Exception ex)
        {
            success = false;
            logger.LogWarning(ex,
                "Failed to send artist-assigned email for appointment {@AppointmentId}", appointment.Id);
        }

        db.NotificationLogs.Add(new NotificationLog
        {
            StudioId = studio.Id,
            RecipientId = appointment.ClientId,
            RecipientType = NotificationRecipientType.Client,
            Channel = NotificationChannel.Email,
            Subject = subject,
            Body = body,
            SentAt = DateTime.UtcNow,
            IsSuccess = success,
        });
        await db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
```

`SendAppointmentCreatedNotificationCommand.cs` itself needs **no change** — verified by reading
it in full: it emails the client and the studio owner, never the artist, so there's nothing to
guard for the studio-choice (no-artist-yet) case there.

---

## Part 5 — Cross-feature fix (required for the build to compile): `CreateManualReminderCommand.cs`

`Pena_e_Arte.Application/Reminders/Commands/CreateManualReminderCommand.cs` — inside the
`req.AppointmentId is not null` branch:

Replace:

```csharp
if (isArtist && appointment.Artist.UserId != currentUser.UserId)
    throw new NotFoundException(nameof(Appointment), req.AppointmentId);
```

with:

```csharp
if (isArtist && (appointment.Artist is null || appointment.Artist.UserId != currentUser.UserId))
    throw new NotFoundException(nameof(Appointment), req.AppointmentId);
```

And replace:

```csharp
resolvedArtistId = appointment.ArtistId;
```

with:

```csharp
resolvedArtistId = appointment.ArtistId
    ?? throw new BusinessRuleViolationException(
        "Assign an artist to this appointment before sending a reminder.");
```

Per Decision #11: an artist-role caller referencing an unassigned appointment gets the same
404 they'd get for any appointment that isn't theirs; an owner/issuer caller gets a clear
business-rule error instead of a compile error or null-ref. Nothing else in this file changes.

---

## Part 6 — Frontend (`frontend/src/features/appointments`)

### 6a. `appointment.types.ts`

```typescript
export interface AppointmentResponse {
  id:                 string;
  studioId:           string;
  artistId:           string | null;
  clientId:           string;
  date:               string;
  endDate:            string;
  durationMinutes:    number;
  status:             AppointmentStatus;
  depositStatus:      DepositStatus;
  depositAmount:      number;
  notes:              string | null;
  createdAt:          string;
  cancellationReason?: string | null;
  aftercareSentAt?:    string | null;
  clientName?:         string | null;
  imageUrls?:          string[];
  artistName?:         string | null;
}

export interface CreateAppointmentRequest {
  artistId:        string | null;
  clientId:        string;
  date:            string;
  durationMinutes: number;
  depositRuleId:   string | null;
  notes:           string | null;
  imageUrls?:      string[];
}

export interface CheckSlotAvailabilityParams {
  artistId?:       string;
  date:            string;
  durationMinutes: number;
}

export interface AssignAppointmentArtistRequest {
  artistId: string;
}
```

### 6b. `appointmentsApi.ts`

`checkSlotAvailability`'s query builder — only include `artistId` when present:

```typescript
checkSlotAvailability: builder.query<SlotAvailabilityResponse, CheckSlotAvailabilityParams>({
  query: ({ artistId, date, durationMinutes }) => ({
    url:    "appointments/check-slot",
    params: { ...(artistId ? { artistId } : {}), date, durationMinutes },
  }),
  keepUnusedDataFor: 0,
}),
```

Add a new mutation, placed alongside the other appointment mutations:

```typescript
assignAppointmentArtist: builder.mutation<
  AppointmentResponse,
  { id: string; body: AssignAppointmentArtistRequest }
>({
  query: ({ id, body }) => ({
    url:    `appointments/${id}/artist`,
    method: "PATCH",
    body,
  }),
  invalidatesTags: ["Appointment"],
}),
```

Export `useAssignAppointmentArtistMutation` from the destructured hooks block, and import
`AssignAppointmentArtistRequest` in the type-imports block at the top of the file.

### 6c. `components/BookAppointmentForm.tsx`

- Add `bookAnyArtist: z.boolean().default(false)` to the zod schema, and change `artistId` to
  `z.string().nullable()`. Add a top-level `.refine`:

  ```ts
  const schema = z.object({
    artistId:        z.string().nullable(),
    bookAnyArtist:   z.boolean().default(false),
    clientId:        z.string().min(1, "Select a client"),
    scheduledAt:     z.string().min(1, "Select date and time").refine(
      (v) => new Date(v) > new Date(),
      "Appointment must be in the future"
    ),
    durationMinutes: z.number().refine(
      (v) => (VALID_DURATIONS as readonly number[]).includes(v),
      "Select a valid appointment duration"
    ),
    depositRuleId:   z.string().nullable().optional(),
    notes:           z.string().optional(),
  }).refine(
    (data) => data.bookAnyArtist || (!!data.artistId && data.artistId.length > 0),
    { message: "Select an artist", path: ["artistId"] },
  );
  ```

- Import `ToggleSwitch` from `@/shared/components/ui/toggle-switch`.
- `defaultValues`: add `bookAnyArtist: false`, keep `artistId: ""` (empty string, not `null` —
  the refine already treats an empty string as "no artist chosen" identically to `null`; don't
  change the default's type just for this).
- Add `const watchedBookAnyArtist = useWatch({ control, name: "bookAnyArtist" });` alongside the
  other `useWatch` calls.
- Slot-check effect (`useEffect` building `debouncedCheck`): change the readiness condition and
  the constructed params to account for `bookAnyArtist`:

  ```tsx
  useEffect(() => {
    const ready = watchedDate && watchedDuration && (watchedBookAnyArtist || watchedArtistId);
    const delay = ready ? 600 : 0;
    const timer = setTimeout(() => {
      if (!watchedDate || !watchedDuration || (!watchedBookAnyArtist && !watchedArtistId)) {
        setDebouncedCheck(null);
        return;
      }
      setDebouncedCheck({
        artistId:        watchedBookAnyArtist ? undefined : watchedArtistId,
        date:            watchedDate,
        durationMinutes: watchedDuration,
      });
    }, delay);
    return () => clearTimeout(timer);
  }, [watchedArtistId, watchedBookAnyArtist, watchedDate, watchedDuration]);
  ```

- Artist selector block: wrap the existing `Select` block (the `<div className="space-y-1.5">…
  <FieldLabel htmlFor="artistId" required>Artist</FieldLabel> …` block) in
  `{!watchedBookAnyArtist && (...)}`, unchanged inside. Immediately before it, add the toggle:

  ```tsx
  <div className="flex items-center justify-between rounded-md border border-border/40
                  bg-muted/20 px-3 py-2">
    <div>
      <p className="text-xs font-medium">Let the studio choose my artist</p>
      <p className="text-[11px] text-muted-foreground">
        We&apos;ll confirm someone&apos;s available — the studio assigns your artist before
        confirming.
      </p>
    </div>
    <Controller
      control={control}
      name="bookAnyArtist"
      render={({ field }) => (
        <ToggleSwitch
          checked={field.value}
          onChange={() => field.onChange(!field.value)}
          aria-label="Let the studio choose my artist"
        />
      )}
    />
  </div>
  ```

- `onSubmit`: send `artistId: values.bookAnyArtist ? null : values.artistId`.
- `resetForm(...)` after a successful submit: add `bookAnyArtist: false` to the reset values.
- Confirmation step (the `if (booked) { ... }` block, the "the artist will confirm soon" copy):
  make each of the three occurrences conditional on `booked.artistId`:

  ```tsx
  {depositDone === "paid"
    ? booked.artistId
      ? "Your deposit is authorised — the artist will confirm soon."
      : "Your deposit is authorised — the studio will assign an artist and confirm soon."
    : depositDone === "cash"
    ? booked.artistId
      ? "Bring the deposit in cash to the studio. The artist will confirm soon."
      : "Bring the deposit in cash to the studio. The studio will assign an artist and confirm soon."
    : depositDone === "skipped"
    ? booked.artistId
      ? "The studio will contact you about the deposit. The artist will confirm soon."
      : "The studio will contact you about the deposit and assign an artist soon."
    : booked.artistId
    ? "The artist will confirm soon."
    : "The studio will assign an artist and confirm soon."}
  ```

### 6d. `components/AppointmentCard.tsx`

- In the header row (`<div className="flex items-center gap-2 flex-wrap">`, next to
  `AppointmentStatusBadge`), add a small badge when unassigned:

  ```tsx
  {appointment.status === AppointmentStatus.Pending && appointment.artistId === null && (
    <span className="text-[10px] font-medium uppercase tracking-wide rounded-full
                     bg-amber-500/15 text-amber-600 px-1.5 py-0.5">
      Needs artist
    </span>
  )}
  ```

- Guard the "Confirm" button: change `{isPending && (` to
  `{isPending && appointment.artistId !== null && (`.

### 6e. `components/AppointmentDetailPage.tsx`

- Import `useGetArtistsQuery` from `@/features/artists/artistsApi`,
  `useAssignAppointmentArtistMutation` from `../appointmentsApi`, `UserRound` added to the
  existing `lucide-react` import line, and
  `Select, SelectContent, SelectItem, SelectTrigger, SelectValue` from
  `@/shared/components/ui/select`.
- `const { data: artists } = useGetArtistsQuery(undefined, { skip: !canOwner });`
- `const [assignArtist, { isLoading: assigning }] = useAssignAppointmentArtistMutation();`
- Handler:

  ```tsx
  async function handleAssignArtist(artistId: string) {
    const result = await assignArtist({ id: appt!.id, body: { artistId } });
    if ("data" in result) toast.success("Artist assigned.");
    else                  toast.error("Failed to assign artist.");
  }
  ```

- Add an Artist `Row`, placed right after the Client row (before the Date & time `Separator`):

  ```tsx
  <Separator />
  <Row
    label="Artist"
    value={
      canOwner ? (
        <Select value={appt.artistId ?? undefined} onValueChange={handleAssignArtist} disabled={assigning}>
          <SelectTrigger
            aria-label="Assigned artist"
            className="h-7 w-auto gap-1.5 border-none px-0 shadow-none text-sm ml-auto"
          >
            <SelectValue placeholder="Unassigned — pick an artist" />
          </SelectTrigger>
          <SelectContent>
            {artists?.map((a) => (
              <SelectItem key={a.id} value={a.id}>
                {a.firstName} {a.lastName}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      ) : appt.artistName ? (
        appt.artistName
      ) : (
        <span className="text-amber-600">Unassigned</span>
      )
    }
  />
  ```

  Unlike `ClientDetailPage.tsx`'s artist reassignment `Select` from 2026-08-20, there is **no
  "Unassigned" option in the dropdown** — assignment is one-directional per Decision #4.

- Guard the "Confirm appointment" button:

  ```tsx
  {isPending && appt.artistId !== null && (
    <Button className="w-full gap-2" disabled={anyLoading} onClick={handleConfirm}>
      ...
    </Button>
  )}
  {isPending && appt.artistId === null && canOwner && (
    <p className="text-xs text-muted-foreground text-center">
      Assign an artist above before this can be confirmed.
    </p>
  )}
  ```

- Guard "Send Reminder": change `<Button ... onClick={() => setReminderDialogOpen(true)}>` to
  only render when `appt.artistId !== null` (wrap it, mirroring the Confirm button's guard) —
  per Part 5, sending a reminder for an unassigned appointment now fails server-side with a
  clear error, so don't offer an action guaranteed to fail.
- `ReminderDialog` prop: `artistId={appt.artistId ?? undefined}` (was `appt.artistId` directly —
  needed purely for the type change, `ReminderDialog`'s `artistId` prop is already optional).

### 6f. `components/SchedulePage.tsx`, `components/MyBookingsSection.tsx`

No changes expected beyond what `AppointmentCard.tsx` (6d) already covers for `SchedulePage.tsx`
(it renders `AppointmentCard` per appointment) and what Context already established for
`MyBookingsSection.tsx`'s reactive `DepositArea`. Verify both hold once the backend lands; flag
if not.

---

## Part 7 — Tests

### Backend

- `CreateAppointmentHandlerTests` — new cases: studio-choice booking succeeds when at least one
  active artist is free (assert `ArtistId` persists as `null`, `DepositAmount` is `0` for a
  percent rule / correct for a fixed rule); fails with `BusinessRuleViolationException` when no
  active artist is free; no `ISlotLocker.TryAcquireLockAsync` call happens on this path (mock
  verification).
- New `AssignAppointmentArtistHandlerTests`: success path including deposit recompute when it
  was `0` and no-op when it was already nonzero (fixed-rule case); 404 on missing
  appointment/artist; business-rule violation on inactive artist, terminal-status appointment,
  and a schedule/time-off/conflict clash for the specific artist chosen; asserts
  `appointment.UpdatedAt` changed and the realtime/notification sends fired.
- New `AssignAppointmentArtistValidatorTests`.
- `ConfirmAppointmentHandlerTests` — new case: `BusinessRuleViolationException` when
  `ArtistId is null`.
- `RescheduleAppointmentHandlerTests` — new case: rescheduling an unassigned appointment
  succeeds/fails based on any-artist availability at the new time (not a spurious
  null-vs-null conflict against another unassigned appointment).
- `CheckSlotAvailabilityHandlerTests` — new cases for `ArtistId: null` (available / unavailable).
- New — `tests/Pena_e_Arte.UnitTests/Reminders/CreateManualReminderHandlerTests.cs` (or add to
  the existing suite): new case asserting an artist-role caller referencing an appointment with
  no assigned artist gets `NotFoundException`; new case asserting an owner-role caller gets
  `BusinessRuleViolationException` with the "Assign an artist…" message.
- Integration tests: `PATCH /api/v1/appointments/{id}/artist` — 200 for Owner, 403 for Artist.
  `POST /api/v1/appointments` with no `artistId` — 201, response has `artistId: null`.
- Grep the whole `tests/` tree for `new CreateAppointmentRequest(` and
  `new AppointmentResponse(` and fix whatever the compiler flags after Part 2's changes — don't
  try to enumerate every occurrence by hand.

### Frontend

- `__tests__/BookPage.test.tsx`:
  - New test: toggling "Let the studio choose my artist" hides the artist `Select`, and
    submitting without ever picking an artist succeeds with `artistId: null` sent in the
    request body.
  - New test: the slot-check call omits `artistId` when the toggle is on.
  - Existing "submitting a valid form" tests: confirm they still pass with the toggle off by
    default (no artist-selection regression).
  - Add an MSW handler variant returning `artistId: null, artistName: null` for the "book with
    studio" success response, and assert the confirmation copy reflects that
    (`"The studio will assign an artist and confirm soon."` — not the artist-specific copy).
- `__tests__/AppointmentDetailPage.test.tsx`:
  - Owner sees an editable Artist `Select` and assigning one calls the mutation and shows a
    success toast.
  - Non-owner sees plain text: the artist's name, or amber "Unassigned" when null.
  - Confirm button is absent/replaced with the hint text when `artistId === null`; present when
    assigned.
  - Send Reminder button absent when `artistId === null`.
- New/updated `SchedulePage.test.tsx` or `AppointmentCard`-covering test: "Needs artist" badge
  renders only for `Pending` + `artistId === null`; Confirm button absent in that state.
- `remindersApi`/`ReminderDialog` tests: confirm `artistId` prop being `undefined` still behaves
  correctly (should already, since the prop was already optional).
- Run `pnpm tsc --noEmit` and the full `pnpm test` after all of the above. `AppointmentResponse`
  gained a new optional field and widened `artistId`'s type — check any other test file across
  the frontend that constructs an `AppointmentResponse`-shaped fixture (payments, reminders,
  and dashboard suites are the likely candidates, since several call `useGetAppointmentsQuery`
  or `useGetAppointmentQuery`) and fix whatever the type checker and test run surface.

---

## Part 8 — Help Menu, user manual, onboarding tour

Per `CLAUDE.md` rule 7, this feature is not done until all three surfaces reflect it.

### 8a. `frontend/src/features/help/helpContent.ts`

- `client-book-appointment` (existing, id confirmed live): update `steps` — after the existing
  "choose an artist, a date, and an appointment duration" step, add: "Or toggle \"Let the studio
  choose my artist\" if you don't have a preference — the studio will assign one before
  confirming." Add a `tips` entry: "If you let the studio choose, you won't know your artist
  until the studio assigns one — you'll get an email when they do." Add keywords: `"let studio
  choose"`, `"any artist"`, `"studio picks artist"`.
- `owner-schedule` (existing, id confirmed live): add a step: "Appointments waiting on an artist
  show a \"Needs artist\" badge — open one and assign an artist from its detail page before it
  can be confirmed."
- New article, placed near `owner-schedule`:

  ```typescript
  {
    id: "owner-appointments-assign-artist",
    roles: [Owner],
    title: "Assign an artist to a studio-choice booking",
    route: "/schedule",
    keywords: ["assign artist", "needs artist", "unassigned booking"],
    summary: "Pick which artist does the work for a client who let the studio choose.",
    steps: [
      "Open the appointment from Schedule — look for the \"Needs artist\" badge.",
      "Use the Artist dropdown near the top of the appointment to pick an artist.",
      "The client is emailed once you've assigned one, and the appointment can now be confirmed.",
    ],
  },
  ```

### 8b. `frontend/public/user-manual/index.html`

- Locate the booking section (search for the existing client-booking wireframe/section id — not
  identified in this prompt's research pass) and add a step/note about the "let the studio
  choose" toggle, mirroring the existing field-by-field description style.
- Locate the owner-facing appointment/schedule section and add a note about the "Needs artist"
  badge and the assign-artist control.

### 8c. `frontend/src/features/help/tours/clientTour.ts`

Update the existing `client-book-nav` step's `body` (currently: "Request a tattoo appointment
here — pick an artist, a date, and how long the session should be.") to:

```
"Request a tattoo appointment here — pick an artist, a date, and how long the session should
be, or let the studio choose your artist for you."
```

### 8d. `frontend/src/features/help/tours/ownerTour.ts`

Checked in this prompt's research pass — no existing step covers Schedule or appointment
detail at all. **No change required for this pass** (stated explicitly, per this project's
convention of recording genuine no-op findings rather than silently skipping them). A new step
introducing the "Needs artist" workflow is optional future polish, not required here.

---

## Definition of done

- [ ] Migration applied cleanly; `dotnet ef database update` succeeds; app boots.
- [ ] `dotnet build` — zero errors, including `CreateManualReminderCommand.cs` (Part 5).
- [ ] `dotnet test` — all green (pre-existing failures noted at pre-flight excluded), including
      every new test in Part 7.
- [ ] `pnpm tsc --noEmit` — zero errors.
- [ ] `pnpm test` — all green, including the updated Appointments and Reminders suites and any
      other suite touched by `AppointmentResponse`'s widened/new fields.
- [ ] Manual smoke check: a client can book a specific artist (unchanged) or toggle "let the
      studio choose"; a studio-choice booking with no artist available anywhere is rejected at
      submit time with a clear message; an Owner sees the "Needs artist" badge on Schedule,
      assigns an artist from the appointment detail page, and the client gets an email; Confirm
      is blocked (both UI and server-side) until an artist is assigned; a deferred percent-rule
      deposit becomes payable (via the existing Charge/`MyBookingsSection` UI) the moment an
      artist is assigned; sending a manual reminder for an unassigned appointment fails with a
      clear message instead of a 500.
- [ ] `helpContent.ts`, `user-manual/index.html`, `clientTour.ts` updated per Part 8;
      `ownerTour.ts` confirmed (not just assumed) to need no change.
