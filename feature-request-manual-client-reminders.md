# Feature Request — Manual Client Reminders (Artist-Triggered SMS)

**From:** Product (via engineering consultation)
**Branch suggestion:** `feat/manual-client-reminders`
**Read first:** `docs/claude/architecture.md` (Feature Module Map row 06, IgnoreQueryFilters
table row #36), `docs/claude/backend.md`, `docs/claude/database.md` — plus the existing
reminder pipeline itself (see below). This was scoped by reading the actual
`AppointmentReminderJob`/`Client`/`NotificationLog` code, not written from a blank page.

---

## Business context (why)

The ask: let an artist manually send (or schedule) a 48-hours-style reminder to a client,
by SMS, from inside the platform — **including a person who has no account and no client
record at all**, just a phone number the artist types in.

This is **additive**, not a replacement. The platform already auto-schedules a 48h and a
24h reminder for every appointment (see below) — that stays exactly as-is. What's missing
is artist-initiated, ad-hoc control: send one right now, pick a custom time, write a custom
message, or reach someone who was never entered into the system as a `Client` at all (a
phone-only "heads up, don't forget Tuesday" text, not a booking).

---

## What already exists (read this before writing code)

- **Automatic reminders already work, including for unregistered clients.**
  `Client.UserId` is `Guid?` — a `Client` row can and does exist with no linked platform
  login (walk-in clients an artist creates via `CreateClientCommand` with just name/phone,
  `ArtistId` set, `UserId` null). `CreateAppointmentCommand` schedules both a 48h and a 24h
  reminder for *every* appointment via `IJobScheduler.ScheduleAppointmentReminder`, and
  `AppointmentReminderJob.SendReminderAsync` (`Pena_e_Arte.Infrastructure/Jobs/AppointmentReminderJob.cs`)
  sends SMS to `appointment.Client.Phone` whenever it's set — completely independent of
  whether that `Client` has a `UserId`. `CancelAppointmentCommand` calls
  `jobs.CancelAppointmentJobs(appointment.ReminderJobId48h, appointment.ReminderJobId24h)`
  to pull scheduled reminders when a booking is cancelled.
- **`IJobScheduler` / `JobScheduler`** (`Pena_e_Arte.Domain/Interfaces/IJobScheduler.cs`,
  `Pena_e_Arte.Infrastructure/Services/JobScheduler.cs`) is the thin Hangfire wrapper
  every scheduled job goes through — extend it, don't call `IBackgroundJobClient` directly
  from a new handler.
- **`NotificationLog`** (`Pena_e_Arte.Domain/Entities/NotificationLog.cs`) is the audit
  trail + in-app bell feed for every send. `RecipientId` is currently `Guid` (not
  nullable), `RecipientType` is `NotificationRecipientType { Client, Studio, Artist }`,
  stored via `.HasConversion<string>().HasMaxLength(32)`
  (`NotificationLogConfiguration.cs`) — there's headroom in that column, no migration risk
  from adding an enum value.
- **`INotificationService.SendSmsAsync`** (`Pena_e_Arte.Infrastructure/Services/NotificationService.cs`)
  wraps Twilio already — reuse it as-is, no new SMS provider code needed.
- **`INotificationPreferenceService`** (`StudioNotificationPreference`, per
  `NotificationType`/`NotificationChannel`) gates the *automatic* lifecycle notifications
  (`NotificationType` enum: `AppointmentCreated`, `AppointmentConfirmed`, ..., `Aftercare`).
  A manual, artist-initiated send is a deliberate one-off action, not a lifecycle event —
  this feature deliberately does **not** route through that preference gate (see Open
  Questions if that judgment call should be revisited).
- **Ownership-check convention for artist-scoped writes** — used identically in
  `UpdateArtistCommand`, `AddArtistTimeOffCommand`, `UpsertArtistScheduleCommand`,
  `UpdateArtistPortfolioCommand`:
  ```csharp
  if (currentUser.Role == "artist" && artist.UserId != currentUser.UserId)
      throw new ForbiddenException(...); // check exact exception type used at these sites
  ```
  Owner/issuer skip the check entirely. **Reuse this exact pattern** — there is no shared
  `FindArtistForUserAsync` helper today (unlike `Client`'s
  `ClientAccountExtensions.FindClientForUserAsync`); each site above resolves the artist
  inline via `db.Artists.FirstOrDefaultAsync(a => a.UserId == currentUser.UserId, ct)`. This
  feature is the third or fourth handler that needs it — worth extracting a shared
  extension now rather than adding a fifth near-duplicate copy (recommended, not required).
- **No SMS consent/opt-out mechanism exists anywhere in the app.** The existing reminder
  SMS body literally says "Reply STOP to opt out," but there is no Twilio inbound webhook
  and no `Client` field that records an opt-out — the text is aspirational only. This is a
  pre-existing gap this feature inherits; see "Compliance flags" below.
- **No phone-format validation exists anywhere in the app.** `CreateClientValidator`
  only does `MaximumLength(20)` on `Phone` — no E.164 or regex check. This feature follows
  that same loose convention for consistency rather than inventing stricter validation
  unilaterally (flagged as a pre-existing gap, not fixed here).

---

## What needs to change

### 1. Domain layer

**`Client.cs`** — add one field (small, forward-looking compliance hook; does not build
the STOP-handling webhook itself, see Compliance flags):
```csharp
public bool SmsOptOut { get; set; }
```

**New enum** `Pena_e_Arte.Domain/Enums/ManualReminderStatus.cs`:
```csharp
public enum ManualReminderStatus { Scheduled, Sent, Failed, Cancelled }
```

**Extend** `NotificationRecipientType`:
```csharp
public enum NotificationRecipientType
{
    Client,
    Studio,
    Artist,
    ExternalContact // recipient has no Client record — raw name/phone only
}
```

**Widen** `NotificationLog.RecipientId` from `Guid` to `Guid?`. Verify no existing handler
assumes non-null when constructing a `NotificationLog` (a quick grep of
`new NotificationLog` construction sites) — every current caller still sets a real `Guid`,
so this widening is additive/safe, but confirm before assuming.

**New entity** `Pena_e_Arte.Domain/Entities/ManualReminder.cs`:
```csharp
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

Exactly one of `AppointmentId` / `ClientId` / (`RecipientName` + `RecipientPhone` typed
manually) drives how `RecipientName`/`RecipientPhone` get populated at creation time — all
three still land in the same two columns, so the send/history code paths never need to
branch on which source it came from after creation. `ClientId` and `AppointmentId` can
both be set (an appointment-linked reminder also stores the resolved `ClientId` for easy
history lookups from the client's side).

### 2. Migration

One migration, `AddManualReminders`:
- New table `manual_reminders` (snake_case per `database.md`): FK to `artists` (required,
  `Restrict`), FK to `appointments` (nullable, `Restrict`), FK to `clients` (nullable,
  `Restrict`). Indexes: `ix_manual_reminders_studio_scheduled_for` (drives an "upcoming
  reminders" list), `ix_manual_reminders_appointment_id`, `ix_manual_reminders_client_id`.
  Apply the standard tenant query filter (`StudioId == tenant.StudioId`) plus soft-delete
  if this project's soft-delete convention applies to net-new entities (check whether
  `DeletedAt` is added to *every* new tenant entity by convention or only to entities that
  need it — `TenantEntity` base doesn't carry it per `database.md`, `Client` adds it
  itself).
- `clients.sms_opt_out` — new `bool NOT NULL DEFAULT false` column.
- `notification_logs.recipient_id` — widen from `NOT NULL` to nullable. No backfill
  needed (relaxing a constraint, not tightening one).

Follow the migration naming/generation convention already documented in `database.md`.

### 3. `IJobScheduler` / `JobScheduler` additions

```csharp
// IJobScheduler.cs
string ScheduleManualReminder(Guid manualReminderId, DateTimeOffset sendAt);
void CancelJob(string jobId);
```
```csharp
// JobScheduler.cs
public string ScheduleManualReminder(Guid manualReminderId, DateTimeOffset sendAt) =>
    sendAt <= DateTimeOffset.UtcNow
        ? backgroundJobs.Enqueue<ManualReminderJob>(j => j.SendAsync(manualReminderId, default))
        : backgroundJobs.Schedule<ManualReminderJob>(j => j.SendAsync(manualReminderId, default), sendAt);

public void CancelJob(string jobId) => backgroundJobs.Delete(jobId);
```
`CancelJob` is intentionally generic (not manual-reminder-specific) — usable later for any
single-job-id cancellation, unlike `CancelAppointmentJobs` which is a fixed two-job-id
shape.

### 4. New Hangfire job — `Pena_e_Arte.Infrastructure/Jobs/ManualReminderJob.cs`

Structure mirrors `AppointmentReminderJob` closely:
1. Load `ManualReminder` by id with `IgnoreQueryFilters()` (Hangfire jobs run with no
   tenant/request scope — same class as `AppointmentReminderJob`; **do not** create a new
   numbered `IgnoreQueryFilters()` approved-usage entry for this — add `ManualReminderJob`
   to the *existing* entry #36 in `architecture.md`'s table, which already lists
   `AppointmentReminderJob`, `DesignRevisionTimeoutJob`, `PaymentReconciliationJob`,
   `SendArtistInviteJob` as the same class of no-tenant-context system job).
2. If `Status == Cancelled`, return (artist cancelled after scheduling, before this fired).
3. If `AppointmentId` is set and that appointment's `Status == Cancelled`, mark this
   reminder `Cancelled` too and return — mirrors `AppointmentReminderJob`'s existing
   cancelled-appointment skip.
4. If a `ClientId` is set and `Client.SmsOptOut == true`, mark `Failed` (reason logged, not
   sent) and return. Raw/no-record contacts have no opt-out field to check — see
   Compliance flags.
5. Body: `reminder.Message` if provided, else a default template. Vary the default by
   whether an appointment is linked (`"Hi {Name}, reminder from {Studio} — your
   appointment is {Date:ddd dd MMM 'at' HH:mm}."`) vs. not (`"Hi {Name}, this is a reminder
   from {Studio}."` — no date/time to reference).
6. `await notifications.SendSmsAsync(reminder.RecipientPhone, body, ct)`, try/catch exactly
   like `AppointmentReminderJob`'s `TrySendSmsAsync`.
7. Write a `NotificationLog` row: `RecipientId = reminder.ClientId` (now legally nullable),
   `RecipientType = reminder.ClientId is not null ? Client : ExternalContact`,
   `Channel = Sms`, `Body = body`, `IsSuccess = <result>`.
8. Push `realtime.NotifyStudioAsync(reminder.StudioId, "NotificationReceived", ...)` — same
   SignalR event every other channel uses, so a manual reminder shows up in the existing
   bell feed identically to an automatic one, regardless of whether the recipient is a
   registered client.
9. Update `ManualReminder.Status = Sent` or `Failed`, `SentAt = UtcNow`, `SaveChangesAsync`.

### 5. Application layer — new `Pena_e_Arte.Application/Reminders/` folder

New top-level feature folder (sibling to `Appointments/`, `Clients/`, `Notifications/`,
etc. per `backend.md`'s layout) — `Commands/`, `Queries/`, `Validators/`.

**`CreateManualReminderCommand.cs`**
```csharp
public record CreateManualReminderCommand(CreateManualReminderRequest Request)
    : IRequest<ManualReminderResponse>;
```
Handler:
1. Resolve current artist (`db.Artists.FirstOrDefaultAsync(a => a.UserId == currentUser.UserId, ct)`)
   when `currentUser.Role == "artist"`; owner/issuer act on behalf of any artist at the
   studio via `Request.ArtistId` (mirrors the owner/issuer bypass already used at every
   site listed in "ownership-check convention" above).
2. Branch on which of `AppointmentId` / `ClientId` / raw contact fields the request carries
   (validator already enforces exactly one — see §6):
   - **Appointment-linked:** load the `Appointment` (tenant-scoped), 404 if not found or if
     `currentUser.Role == "artist"` and the appointment's `Artist.UserId != currentUser.UserId`
     (same 404-not-403 convention `CancelAppointmentHandler` uses, so a guessed id doesn't
     confirm existence). Pull `RecipientName`/`RecipientPhone` from `appointment.Client`.
     Reject (422) if `appointment.Client.Phone` is null — nothing to send to.
   - **Client-linked:** load the `Client` (tenant-scoped), same ownership check via
     `Client.ArtistId`, same null-`Phone` 422.
   - **Raw contact:** use `Request.RecipientName`/`Request.RecipientPhone` directly. No
     `Client` row is created — this path stays fully ephemeral, by design (that's the whole
     point of the "no record at all" case).
3. If a `Client` is resolved (either linked path) and `Client.SmsOptOut`, reject (422,
   clear message — don't silently swallow this at create time, catch it here in addition
   to the job's own defensive check in step 4 of §4).
4. Resolve `ScheduledFor`: `Request.ScheduledFor ?? DateTime.UtcNow` (null/omitted = send
   now).
5. Save the `ManualReminder` row (`Status = Scheduled`), call
   `jobs.ScheduleManualReminder(reminder.Id, scheduledFor)`, store the returned id back
   into `reminder.JobId`, `SaveChangesAsync` again (two saves, matching the
   create-then-store-job-id shape already used in `CreateAppointmentCommand` for
   `ReminderJobId48h`/`24h`).

**`CancelManualReminderCommand.cs`** — `IRequest`, takes `Guid Id`. Same ownership check.
404 if not found/not owned. No-op (or 409?) if `Status != Scheduled` — decide the exact
status code; recommend 409 (mirrors the deposit/slot "already happened" semantics used
elsewhere) since the reminder already fired or was already cancelled. Calls
`jobs.CancelJob(reminder.JobId)`, sets `Status = Cancelled`.

**`GetManualRemindersQuery.cs`** — `IRequest<List<ManualReminderResponse>>`, filters by
`AppointmentId` or `ClientId` (at least one required), tenant-scoped, ordered by
`ScheduledFor`. Backs the small history list under the "Send Reminder" button on both
frontend entry points (§7).

**`CreateManualReminderValidator.cs`** (FluentValidation):
- Custom rule: exactly one of (`AppointmentId` set, `ClientId` set, both `RecipientName`
  and `RecipientPhone` set) — reject if zero or more than one source is present.
- `RecipientName`: `NotEmpty().MaximumLength(200)` when the raw-contact path is used.
- `RecipientPhone`: `MaximumLength(20)` when the raw-contact path is used (matches
  `CreateClientValidator`'s existing loose convention — see the flagged gap above).
- `Message`: `MaximumLength(320)` when provided (roughly two SMS segments — a judgment
  call, not a hard product decision; flagged in Open Questions).
- `ScheduledFor`: when provided, `GreaterThan(DateTime.UtcNow)` (matches
  `CreateAppointmentValidator`'s convention) and reasonably capped (recommend 90 days out)
  to avoid an absurd far-future Hangfire schedule — exact cap is a judgment call, flag it.

### 6. Contracts

`Pena_e_Arte.Contracts/Requests/CreateManualReminderRequest.cs`:
```csharp
public record CreateManualReminderRequest(
    Guid? AppointmentId,
    Guid? ClientId,
    Guid? ArtistId,          // only honored for owner/issuer callers; ignored/overridden for "artist" role
    string? RecipientName,
    string? RecipientPhone,
    string? Message,
    DateTime? ScheduledFor);
```
`Pena_e_Arte.Contracts/Responses/ManualReminderResponse.cs`:
```csharp
public record ManualReminderResponse(
    Guid Id, Guid? AppointmentId, Guid? ClientId,
    string RecipientName, string RecipientPhone, string? Message,
    DateTime ScheduledFor, string Status, DateTime? SentAt, DateTime CreatedAt);
```

### 7. API endpoints — `Pena_e_Arte.API/Endpoints/ManualReminderEndpoints.cs`

```csharp
var group = app.MapGroup("/api/v1/reminders").RequireAuthorization();

group.MapPost("/", CreateManualReminder).RequireAuthorization("ArtistAndAbove");
group.MapGet("/", GetManualReminders).RequireAuthorization("ArtistAndAbove"); // ?appointmentId= or ?clientId=
group.MapDelete("/{id}", CancelManualReminder).RequireAuthorization("ArtistAndAbove");
```
201 Created on POST, 204 on successful DELETE, 409 on cancel-after-sent, 422 on
opt-out/missing-phone rejections — per the HTTP status conventions in `conventions.md`.

---

## Frontend changes

Two entry points, per this feature's own precedent that both an existing-record path and
a no-record path need first-class UI, not just an API:

1. **`AppointmentDetailPage.tsx`** (`frontend/src/features/appointments/components/`) —
   add a "Send Reminder" button next to the existing appointment actions. Opens a dialog:
   "Send now" vs. "Schedule for..." (date/time picker, reusing whatever picker
   `RescheduleDialog.tsx` already uses rather than adding a new dependency), optional
   custom message textarea (placeholder shows the default template text), submit → posts
   `CreateManualReminderCommand` with `AppointmentId` set — recipient name/phone are
   implicit from the appointment's client, no need to ask. Below the button, a small list
   from `GetManualRemindersQuery` shows past/scheduled manual reminders for this
   appointment (status badges: Scheduled/Sent/Failed/Cancelled, each cancellable while
   still `Scheduled`).
2. **`ClientDetailPage.tsx`** (`frontend/src/features/clients/components/` — verify exact
   path/filename) — same dialog, `ClientId` set instead of `AppointmentId`. Covers "text
   this client who has no upcoming appointment right now."
3. **The genuinely new case — no `Client` record at all.** This doesn't have a natural
   existing page to hang off of, since by definition there's no appointment or client
   detail page to open. Recommend a "Quick Reminder" toolbar action on `SchedulePage.tsx`
   (the artist's day/week schedule view) that opens the same dialog defaulting to
   manual-contact mode (type a name + phone directly). This placement is a judgment call —
   flagged as such, not a mandate; a case could also be made for putting it on the clients
   list page instead. Whoever implements should confirm with product/design before
   committing to the final placement, but should not block implementation on that — pick
   one, note the alternative was considered.

New RTK Query slice: `frontend/src/features/reminders/remindersApi.ts` (camelCase per
`conventions.md`'s file-naming table), typed request/response matching the Contracts DTOs
above, no `any`.

---

## Compliance / abuse flags — read before deciding scope, not resolved here

1. **No SMS opt-out enforcement mechanism exists app-wide.** This feature adds
   `Client.SmsOptOut` and checks it, but nothing ever *sets* it to `true` — there's no
   Twilio inbound-SMS webhook anywhere in the codebase to process a "STOP" reply, despite
   every outbound SMS already claiming one works. Building that properly (Twilio inbound
   webhook → signature validation → set `SmsOptOut` → suppress *all* future sends,
   automatic and manual alike) is bigger than this feature and touches every SMS-sending
   handler in the app, not just this one — matches the "too large for this pass" cutoff
   this codebase's own audits have used before (e.g. the deferred "P6.1 notification
   deep-linking" item in `architecture.md`). Recommend flagging it as its own follow-up
   rather than folding it in here silently.
2. **Raw/no-record contacts have no opt-out field at all**, by construction — there's no
   row to attach one to. If this matters for compliance, the alternative is forcing every
   raw-contact reminder through a lightweight `Client` creation first (defeats the "no
   record at all" requirement) or accepting the gap for that path specifically. Flagged as
   an explicit decision, not resolved here.
3. **Abuse/cost surface.** `docs/claude/architecture.md` currently documents that
   authenticated-only endpoints don't get Redis rate-limiting by policy (see the Redis
   rate-limiting rule referenced around the traffic-analytics section). A free-text,
   artist-triggered SMS endpoint reachable to any phone number is a materially different
   risk than the rest of that authenticated surface — a compromised or malicious artist
   account could run up a studio's Twilio bill or send unwanted texts to arbitrary numbers.
   Recommend a per-studio or per-artist daily send cap (Redis counter, same pattern
   already used for slot locks / rate limits per `database.md`'s Redis Patterns section),
   but the exact cap number is a product/cost call, not engineering's to invent — flagged
   in Open Questions.
4. **Phone number format is unvalidated app-wide** (§ "What already exists"). This feature
   inherits that gap rather than fixing it unilaterally for just this one path, which
   would create an inconsistency between "how strict is a phone number" depending on which
   feature entered it.

---

## Tests

Unit (`tests/Pena_e_Arte.UnitTests/Reminders/` — new folder):
- `Handle_AppointmentLinked_ResolvesRecipientFromClient`
- `Handle_ClientLinked_ResolvesRecipientFromClient`
- `Handle_RawContact_DoesNotCreateClientRow`
- `Handle_ClientHasNoPhone_ThrowsUnprocessable` (both linked paths)
- `Handle_ClientOptedOut_ThrowsUnprocessable`
- `Handle_ArtistNotOwnerOfAppointment_ThrowsNotFound` (404-not-403 convention)
- `Handle_OwnerRole_BypassesOwnershipCheck`
- `Handle_ScheduledForOmitted_SchedulesImmediateSend`
- `Handle_ScheduledForFuture_SchedulesViaHangfire`
- `Cancel_AlreadySent_ThrowsConflict`
- `Cancel_StillScheduled_DeletesHangfireJobAndSetsCancelled`
- `ManualReminderJob_AppointmentCancelledSinceScheduling_SkipsSend` (mirrors the existing
  `AppointmentReminderJob` cancelled-appointment test)
- `ManualReminderJob_SmsFails_WritesFailedLogAndStatus`
- `ManualReminderJob_NoClientLinked_WritesExternalContactRecipientType`

Integration (`tests/Pena_e_Arte.IntegrationTests/`):
- Full create → Hangfire schedule → job execution → `NotificationLog` + `ManualReminder`
  status flow, for both a `Client`-linked and a raw-contact reminder. Use whatever
  `INotificationService`/Twilio test double the integration base already substitutes — do
  **not** hit real Twilio in CI (same caution the notifications overnight prompt already
  documents).
- Tenant isolation: a manual reminder created in one studio's context must not be
  readable/cancellable from another tenant's `ICurrentTenant` scope.

Frontend (`__tests__/`, per the existing `AppointmentDetailPage.test.tsx` pattern):
- Dialog renders "Send now"/"Schedule for" correctly, disables submit with no recipient
  info in raw-contact mode, shows the reminder history list with correct status badges.

---

## Help-sync obligations (CLAUDE.md rule #7 — not optional)

- **`frontend/src/features/help/helpContent.ts`** — new Artist-role article, e.g.
  `artist-send-manual-reminder`, `route: "/schedule"` or `/clients/{id}` (match wherever
  the final entry points land), covering both the appointment-linked and raw-contact
  flows. Add `relatedArticleIds` links to/from the existing appointment-related articles.
  If owner/issuer can also trigger this on an artist's behalf, add or extend an
  Owner-role article too.
- **`frontend/public/user-manual/index.html`** — new section(s) alongside the existing
  `<section id="artist-appointment-detail" data-role="artist">` and
  `<section id="artist-client-detail" data-role="artist">` sections, following the same
  markup/structure already used there.
- **`frontend/src/features/help/tours/artistTour.ts`** — only touch this if the final UI
  placement changes a step target already covered (e.g. if "Quick Reminder" lands inside
  the existing `artist-schedule-nav` step's target area) or if a new persistent nav
  element is added that deserves its own tour step. Not mandatory to add a new step if the
  feature is discoverable from an existing page without a new nav entry — use judgment,
  but don't skip checking.

---

## Architecture doc updates required in the same change

- **Feature Module Map** (`architecture.md`) — add row **35**: `Manual Client Reminders`
  \| `ManualReminder` \| `Hangfire + Twilio (reused)` \| `Per-tenant`.
- **IgnoreQueryFilters table** — do **not** add a new numbered row; extend existing row
  **#36** to also list `ManualReminderJob` alongside `AppointmentReminderJob`,
  `DesignRevisionTimeoutJob`, `PaymentReconciliationJob`, `SendArtistInviteJob`.
- **Decisions Log** — add an entry recording: (a) manual reminders bypass
  `INotificationPreferenceService` deliberately (one-off action, not a lifecycle event),
  (b) the SMS-opt-out webhook gap was identified and deliberately deferred as a separate
  follow-up, (c) whatever abuse-rate-limit number gets picked in Open Question 3 below.

---

## Constraints (per project rules)

- Tenant isolation: every new query/command is tenant-scoped via the standard
  `ICurrentTenant`/query-filter mechanism — no `IgnoreQueryFilters()` needed anywhere in
  the Application-layer handlers (only the Hangfire job needs it, and only because it's a
  no-tenant-context system job, same as every existing job in table row #36).
- RBAC: `ArtistAndAbove` on all three endpoints, `.RequireAuthorization()` — no
  unprotected endpoint.
- Never log PII: `ManualReminderJob`'s Serilog calls use `tenant_id`/`user_id`/
  `manual_reminder_id`, never `RecipientName`/`RecipientPhone` in log properties (the
  `NotificationLog.Body` column is the audit record for message content, not the logs).
- Structured logs only, no `Console.WriteLine`.
- No new ORM/library — reuses existing `IJobScheduler`/Hangfire and
  `INotificationService`/Twilio wiring end to end.

---

## Open questions for whoever picks this up

1. **Final UI placement for the raw-contact ("no record at all") entry point** —
   `SchedulePage.tsx` toolbar (this doc's recommendation) vs. clients list page vs.
   somewhere else. Not a blocker, but confirm before or shortly after implementation.
2. **Should owner/issuer be allowed to trigger a manual reminder on any artist's behalf**,
   or should this stay strictly artist-to-their-own-client? This doc assumes owner/issuer
   can act for any artist at the studio (matching the existing bypass pattern used
   elsewhere), but that's a policy call worth confirming, not an engineering given.
3. **Abuse-prevention cap** — per-studio or per-artist daily manual-SMS send limit. This
   doc flags the need but does not pick a number; needs a product/cost decision before (or
   shortly after) shipping, given real Twilio per-SMS cost.
4. **SMS opt-out webhook** — tracked as a separate, larger follow-up (see Compliance
   flags #1), not in scope for this feature. Confirm that's acceptable before shipping a
   feature that expands SMS usage while that gap remains open.
5. **`Message` max length (320 chars assumed here)** and **`ScheduledFor` max
   horizon (90 days assumed here)** — both judgment calls in this spec, not confirmed
   product decisions.
