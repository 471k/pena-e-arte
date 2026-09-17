# Overnight Master Prompt — Live + Offline Issuer Notification on New Studio Registration

**Audience:** a Claude Code session running in the main Engineering project, with full
repo write access, executing unattended overnight.

**Source project:** Pena e Artë — Engineering Consultation. This prompt was produced by
reading the live repo (not assumed from `docs/claude/architecture.md` alone, though that
file's Feature Module Map and IgnoreQueryFilters table were cross-checked and are updated
by this same prompt). Every file path, signature, and behavior claim below was verified
against the current source. Where the current behavior is a bug, it is called out
explicitly rather than silently folded into the fix.

---

## 1. What's being built

Right now, when a new studio registers (`RegisterStudioCommand` /
`RegisterStudioHandler` in `Pena_e_Arte.Application/Studios/Commands/RegisterStudioCommand.cs`),
**nothing tells the issuer (platform admin) it happened.** No email, no in-app log entry,
no real-time push. The only way an issuer discovers a new studio today is by opening
`/platform/studios` and looking.

This prompt adds both delivery paths the product already uses elsewhere for other events,
applied to this one:

- **Live**: if an issuer is currently logged into the platform admin console, their
  `NotificationBell` updates instantly (unread badge increments, bell panel shows the new
  entry) via the existing SignalR notification pipe — no page refresh needed.
- **Offline**: if no issuer is currently connected, the event still reaches them — as a
  real email to every issuer account, and as a durable `NotificationLog` row they'll see
  the next time they open the bell or `/notifications`.

This mirrors the existing `AppointmentCreated` pattern
(`SendAppointmentCreatedNotificationCommand` / `SendAppointmentCreatedNotificationHandler`,
`Pena_e_Arte.Application/Appointments/Commands/SendAppointmentCreatedNotificationCommand.cs`)
almost exactly: persisted email log + real-time SignalR push of that same log row. The
differences from that precedent, and why, are explained inline below.

---

## 2. Verified current state — read this before touching anything

This section is load-bearing. Two things are true today that are easy to get wrong by
assumption:

**2.1 — The issuer's `NotificationBell` is already wired into the UI but is not fully
functional for platform-level events.** `IssuerLayout.tsx`
(`frontend/src/layouts/IssuerLayout.tsx`) already renders `<NotificationBell />` in its
header, and the shared `/notifications` route
(`frontend/src/app/router.tsx`, the "Shared: notifications" block) already allows
`Role.Issuer`. But:

- `IssuerLayout.tsx` never calls `useSignalR` (unlike `OwnerLayout.tsx`,
  `ArtistLayout.tsx`, `ClientLayout.tsx`, which all call
  `useSignalR(tenantId)` to connect `/hubs/notification` and the other hubs). The issuer's
  bell today has **no live connection at all** — it only ever fetches on open, via
  `useGetNotificationsQuery`.
- `NotificationHub.JoinStudio` (`Pena_e_Arte.Infrastructure/Hubs/NotificationHub.cs`)
  only supports joining a *specific* `studio:{id}` group. There is no group an issuer can
  join to hear about a studio that didn't exist yet at connection time — which is exactly
  this event. `RealtimeNotifier.NotifyStudioAsync` only ever targets one studio's group.
  A brand-new platform-wide broadcast path is needed; it does not exist today.

**2.2 — `GetNotificationsQuery` has no issuer branch, and the issuer's tenant scoping
silently narrows results to one studio instead of erroring or returning everything.**
`NotificationLog` is a `TenantEntity`
(`Pena_e_Arte.Domain/Entities/NotificationLog.cs`) with a global query filter:

```csharp
builder.Entity<NotificationLog>().HasQueryFilter(n => n.StudioId == tenant.StudioId && n.DeletedAt == null);
```

(`Pena_e_Arte.Infrastructure/Persistence/AppDbContext.cs`). `ICurrentTenant.StudioId`
(`CurrentTenantService`, `Pena_e_Arte.Infrastructure/Services/CurrentTenantService.cs`) is
populated from the caller's `tenant_id` JWT claim — and per
`Pena_e_Arte.Infrastructure/Persistence/Seed/DataSeeder.cs`, the seeded issuer account
(`issuer@pena-arte.test`) carries `tenant_id = Studio1Id`, same as any other role. Nothing
in `GetNotificationsQuery.cs`
(`Pena_e_Arte.Application/Notifications/Queries/GetNotificationsQuery.cs`) calls
`IgnoreQueryFilters()` or special-cases `currentUser.Role == "issuer"` — it falls through
to the generic `else if (query.RecipientId.HasValue)` branch. **The practical effect: an
issuer hitting `GET /api/v1/notifications` today silently only ever sees Studio1's log,
never a true cross-tenant view, and never anything from a studio their JWT doesn't happen
to be tagged with.** This has apparently never mattered before because nothing issuer-facing
has ever been written to `NotificationLog`. It matters now, because this prompt is about
to write the first one. Fix this as part of this work — do not ship a new issuer
notification type into a query path that can't correctly return it.

**2.3 — There is no existing concept of "the issuer" as a `NotificationLog` recipient.**
`NotificationRecipientType` (`Pena_e_Arte.Domain/Enums/NotificationRecipientType.cs`) has
exactly `Client`, `Studio`, `Artist`. None fit. Add `Issuer`.

**2.4 — There is no existing way to look up issuer email addresses from the Application
layer.** `IAppDbContext` exposes no Identity `Users` DbSet (Identity is deliberately kept
out of the Application layer's persistence surface — see `IAppDbContext.cs`,
`Pena_e_Arte.Application/Persistence/IAppDbContext.cs`). The existing seam for
Identity-backed lookups is `IIdentityService`
(`Pena_e_Arte.Domain/Interfaces/IIdentityService.cs`, implemented by
`Pena_e_Arte.Infrastructure/Services/IdentityService.cs`), which already wraps
`UserManager<IdentityUser>` for every other Identity operation used by Application-layer
handlers. It has no "get emails for role" method today. Add one there — do **not**
invent a parallel interface; `IIdentityService` is the established seam and every other
handler that needs an Identity-backed fact already goes through it.

**2.5 — Precedent for a platform-wide (non-per-studio) SignalR group already exists, and
should be copied, not reinvented.** `TrafficHub`
(`Pena_e_Arte.Infrastructure/Hubs/TrafficHub.cs`) is `[Authorize(Policy = "IssuerOnly")]`
and auto-joins every connection to a single fixed group, `"platform:traffic"`, in
`OnConnectedAsync` — no per-client `JoinX` invocation needed, which the class's own doc
comment notes sidesteps a reconnect-group-loss issue documented elsewhere for hubs that
*do* require an explicit join call. `NotificationHub` cannot simply become
`IssuerOnly` like `TrafficHub` (every role connects to it for their own studio's group),
so the fix here is additive: give `NotificationHub` a *conditional* auto-join in
`OnConnectedAsync` — if the connecting user's role is `issuer`, add them to a new fixed
group, `"platform:issuer-notifications"`, alongside (not instead of) the existing
`JoinStudio`/`LeaveStudio` methods, which stay untouched for every other role.

**2.6 — `RegisterStudioHandler` already has the exact shape to extend.** It already calls
`jobs.ScheduleTrialExpiryWarning` / `ScheduleTrialExpiry` / `ScheduleGracePeriodEnd`
after `SaveChangesAsync`, and already logs `"Studio registered {@StudioId} ..."`. The
`CreateAppointmentCommand` precedent
(`Pena_e_Arte.Application/Appointments/Commands/CreateAppointmentCommand.cs`, ~line 144)
dispatches its notification as a direct in-request `await sender.Send(new
SendAppointmentCreatedNotificationCommand(appointment.Id), ct);` immediately after the
real-time push, not via a queued Hangfire job. Match that: `RegisterStudioHandler`
needs `ISender` added to its constructor and one new line after the existing job
scheduling calls.

**2.7 — `NotificationLog` has no "type" column** — only `Channel`, `Subject`, `Body`,
`RecipientId`, `RecipientType`, `SentAt`, `IsSuccess`. The preference-gated
`NotificationType` enum (`AppointmentCreated`, `DepositCaptured`, etc.) and
`StudioNotificationPreference`/`ClientNotificationPreference` exist purely to let a given
studio opt in/out of specific *client-facing* notification categories per channel. This
new issuer notice is not client-facing, not studio-specific, and not something any studio
should be able to opt out of on the issuer's behalf — it must **bypass
`INotificationPreferenceService` entirely** and always send, exactly like the
already-documented carve-out in `NotificationChannel.cs`'s comment on `InApp`
("e.g. an issuer generating a referral code on a studio's behalf ... never routed through
the per-event email/SMS opt-in preferences"). Do not add a new value to the
`NotificationType` enum for this — it isn't needed (no schema field consumes it for this
flow) and would incorrectly imply this is preference-gated.

**2.8 — `RecipientId`/`RecipientType` semantics already have a loose precedent worth
reusing, not fighting.** For `RecipientType.Studio`, `RecipientId` is the studio's own
`Id`, and it represents "this notification concerns/was sent to this studio" — not a
specific user id. Follow the same shape for the new `RecipientType.Issuer`: set
`NotificationLog.RecipientId = <the newly registered studio's Id>` and
`NotificationLog.StudioId = <the newly registered studio's Id>` (required — `StudioId` is
a non-nullable `Guid` on `TenantEntity`). Read as "this platform-level notice concerns
studio X," not "sent to studio X." `GetNotificationsHandler.ResolveRecipientNamesAsync`
needs a new branch so the issuer's notification list resolves and displays that studio's
name rather than falling back to a raw GUID — reuse the existing `Studios` lookup.

**2.9 — `NotificationLog.RecipientType` is stored as a string (`HasConversion<string>()`,
`HasMaxLength(32)`,** `Pena_e_Arte.Infrastructure/Persistence/Configurations/NotificationLogConfiguration.cs`).
Adding `Issuer` to the enum needs **no migration** for the column itself. A migration
**is** needed for a new index (see §6).

---

## 3. Non-goals — explicitly out of scope, do not build these

- SMS to the issuer. Twilio/SMS is a client-facing channel in this codebase; there is no
  precedent or requirement for it here. Email + persisted log + real-time push only.
- Per-issuer-user notification preferences. There is exactly one seeded issuer account
  today, but the fix must not hardcode that email — it must resolve every user in the
  `issuer` role dynamically (see §4.3). Preference toggles for this event are not needed;
  this is a mandatory platform-ops notice, not an opt-in.
- Any change to `StudioNotificationPreference` or `ClientNotificationPreference` schema.
- Any change to the client-facing `NotificationType` enum or the notification
  preferences UI (`NotificationPreferencesCard.tsx`) — this event never appears there.
- Touching `useSignalR.ts` (the Owner/Artist/Client hook) — build a small, separate,
  issuer-only hook instead (see §5.1). Do not add an issuer branch to the shared hook;
  it's parameterized by `studioId` and issuer connections have no single tenant studio to
  key off for this purpose.

---

## 4. Phase 1 — Domain layer

**4.1 — `Pena_e_Arte.Domain/Enums/NotificationRecipientType.cs`**

```csharp
public enum NotificationRecipientType
{
    Client,
    Studio,
    Artist,
    Issuer
}
```

**4.2 — `Pena_e_Arte.Domain/Interfaces/IRealtimeNotifier.cs`** — add a platform-wide push
method alongside the existing per-studio and per-ticket ones:

```csharp
public interface IRealtimeNotifier
{
    Task NotifyStudioAsync(Guid studioId, string eventName, object payload, CancellationToken ct = default);
    Task NotifyTicketAsync(Guid feedbackReportId, string eventName, object payload, CancellationToken ct = default);
    Task NotifyIssuersAsync(string eventName, object payload, CancellationToken ct = default);
}
```

**4.3 — `Pena_e_Arte.Domain/Interfaces/IIdentityService.cs`** — add one method to the
existing interface (do not create a new interface — see §2.4):

```csharp
/// <summary>
/// Returns the email address of every Identity user currently in the given role.
/// Used for platform-level notices that must reach every issuer account, not one
/// hardcoded address. Empty list if the role has no members or doesn't exist.
/// </summary>
Task<IReadOnlyList<string>> GetEmailsInRoleAsync(string role, CancellationToken ct);
```

**4.4 — `Pena_e_Arte.Domain/Interfaces/IEmailRenderer.cs`** — add:

```csharp
string RenderStudioRegisteredIssuer(
    string studioName,
    string city,
    string ownerEmail,
    string? nipt,
    DateTime trialExpiresAtUtc,
    bool hasReferral,
    string studioDetailUrl);
```

---

## 5. Phase 2 — Application layer

**5.1 — New command: `Pena_e_Arte.Application/Studios/Commands/SendStudioRegisteredNotificationCommand.cs`**
(same folder as `RegisterStudioCommand.cs`, same naming pattern as
`SendAppointmentCreatedNotificationCommand`):

```csharp
public record SendStudioRegisteredNotificationCommand(Guid StudioId) : IRequest<Unit>;

public class SendStudioRegisteredNotificationHandler(
    IAppDbContext db,
    IEmailRenderer emailRenderer,
    INotificationService notifications,
    IIdentityService identity,
    IRealtimeNotifier realtime,
    IAppSettings appSettings,
    ILogger<SendStudioRegisteredNotificationHandler> logger)
    : IRequestHandler<SendStudioRegisteredNotificationCommand, Unit>
{
    public async Task<Unit> Handle(SendStudioRegisteredNotificationCommand command, CancellationToken ct)
    {
        Studio? studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == command.StudioId, ct);
        if (studio is null)
        {
            logger.LogWarning("Studio {@StudioId} not found for studio-registered issuer notification", command.StudioId);
            return Unit.Value;
        }

        IReadOnlyList<string> issuerEmails = await identity.GetEmailsInRoleAsync("issuer", ct);
        if (issuerEmails.Count == 0)
        {
            logger.LogWarning("No issuer accounts found to notify of new studio {@StudioId}", studio.Id);
            return Unit.Value;
        }

        string detailUrl = $"{appSettings.BaseUrl}/platform/studios/{studio.Id}";
        string body = emailRenderer.RenderStudioRegisteredIssuer(
            studio.Name, studio.City, studio.OwnerEmail, studio.Nipt,
            studio.TrialExpiresAt ?? DateTime.UtcNow, studio.PendingReferralCodeId.HasValue, detailUrl);
        string subject = $"New studio registered — {studio.Name}";

        // Bypasses INotificationPreferenceService deliberately — see prompt §2.7.
        // Not gated per-recipient in the log: one row represents the event, IsSuccess
        // reflects whether at least one issuer email actually went out.
        bool anySucceeded = false;
        foreach (string email in issuerEmails)
        {
            try
            {
                await notifications.SendEmailAsync(email, subject, body, ct);
                anySucceeded = true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to send studio-registered notification to issuer {@IssuerEmail} for studio {@StudioId}",
                    email, studio.Id);
            }
        }

        NotificationLog log = new()
        {
            StudioId = studio.Id,
            RecipientId = studio.Id,
            RecipientType = NotificationRecipientType.Issuer,
            Channel = NotificationChannel.Email,
            Subject = subject,
            Body = body,
            SentAt = DateTime.UtcNow,
            IsSuccess = anySucceeded,
        };
        db.NotificationLogs.Add(log);
        await db.SaveChangesAsync(ct);

        await realtime.NotifyIssuersAsync("NotificationReceived", GetNotificationsHandler.Map(log, studio.Name), ct);

        return Unit.Value;
    }
}
```

Known, deliberate trade-off to state explicitly (per this project's habit of flagging
rather than silently accepting): if `identity.GetEmailsInRoleAsync` itself throws (not an
individual send failure — those are caught per-recipient above), the exception propagates
out of this command. Since `RegisterStudioHandler` (§5.2) calls this inline via
`sender.Send`, an unhandled exception here would fail the *studio's own registration
request* — matching exactly how `CreateAppointmentCommand` already lets
`SendAppointmentCreatedNotificationCommand` propagate uncaught (no try/catch at that call
site either). Do not add a try/catch around the `sender.Send` call in
`RegisterStudioHandler` to "fix" this — it would be inconsistent with the established
pattern and is out of scope; flag it in the PR description as a pre-existing class of risk
this prompt inherits rather than introduces.

**5.2 — `Pena_e_Arte.Application/Studios/Commands/RegisterStudioCommand.cs`** — add
`ISender sender` to `RegisterStudioHandler`'s constructor, and immediately after the three
existing `jobs.Schedule*` calls (before the `logger.LogInformation("Studio registered...")`
line), add:

```csharp
await sender.Send(new SendStudioRegisteredNotificationCommand(studio.Id), ct);
```

**5.3 — Fix `Pena_e_Arte.Application/Notifications/Queries/GetNotificationsQuery.cs`** —
add an issuer branch, ordered first in the if/else-if chain (`currentUser.Role ==
"issuer"` must be checked before the generic `else if (query.RecipientId.HasValue)`
fallthrough it currently hits):

```csharp
IQueryable<NotificationLog> q;

if (currentUser.Role == "issuer")
{
    // Issuer is not scoped to one studio for this read — see prompt §2.2 for why the
    // default tenant filter (StudioId == tenant.StudioId) would otherwise silently
    // narrow this to whichever studio happens to be on the issuer's own JWT.
    // IgnoreQueryFilters() drops both halves of the combined filter (StudioId AND
    // DeletedAt == null), so DeletedAt == null is reapplied explicitly below.
    q = db.NotificationLogs.IgnoreQueryFilters().AsNoTracking()
        .Where(n => n.DeletedAt == null && n.RecipientType == NotificationRecipientType.Issuer);
}
else
{
    q = db.NotificationLogs.AsNoTracking();

    if (currentUser.Role == "artist")
    {
        // ...existing artist branch, unchanged...
    }
    else if (currentUser.Role == "client")
    {
        // ...existing client branch, unchanged...
    }
    else if (query.RecipientId.HasValue)
    {
        q = q.Where(n => n.RecipientId == query.RecipientId.Value);
    }
}
```

Restructure carefully — the existing artist/client branches must keep their exact current
bodies (they already have their own regression-tested scoping bugs fixed on 2026-0x, per
the Decisions Log; do not touch their logic, only the control flow that wraps them).

Also extend `ResolveRecipientNamesAsync` in the same file with an `Issuer` branch,
resolving the same way the existing `Studio` branch does (same `RecipientId` shape, both
are a `Studio.Id`):

```csharp
List<Guid> issuerSubjectStudioIds = logs
    .Where(n => n.RecipientType == NotificationRecipientType.Issuer)
    .Select(n => n.RecipientId)
    .Distinct()
    .ToList();
```

— then merge this into the same `Studios` lookup already used for the `Studio` branch
(both resolve `Studio.Id → Studio.Name`; do not run a second near-identical query, extend
the existing `studioIds` list to include both sets before the single `db.Studios` query
runs).

**5.4 — `Pena_e_Arte.API/Endpoints/NotificationEndpoints.cs`** — no change needed. `GET
/api/v1/notifications` is already `RequireAuthorization("ClientAndAbove")`, and `issuer`
already satisfies that policy hierarchy (confirmed: `router.tsx`'s `/notifications` route
already lists `Role.Issuer` in its `RoleGuard`, and the issuer flows through today, just
scoped wrong per §2.2 — this prompt fixes the scoping, not the authorization).

---

## 6. Phase 3 — Infrastructure layer

**6.1 — `Pena_e_Arte.Infrastructure/Services/IdentityService.cs`** — implement
`GetEmailsInRoleAsync`:

```csharp
public async Task<IReadOnlyList<string>> GetEmailsInRoleAsync(string role, CancellationToken ct)
{
    IList<IdentityUser> users = await userManager.GetUsersInRoleAsync(role);
    return users
        .Select(u => u.Email)
        .Where(e => !string.IsNullOrWhiteSpace(e))
        .Select(e => e!)
        .ToList();
}
```

(Match whatever the existing constructor's injected `UserManager<IdentityUser>` field is
actually named in this file — do not introduce a second injection of it.)

**6.2 — `Pena_e_Arte.Infrastructure/Services/RealtimeNotifier.cs`** — implement
`NotifyIssuersAsync`:

```csharp
public async Task NotifyIssuersAsync(string eventName, object payload, CancellationToken ct) =>
    await notificationHub.Clients.Group("platform:issuer-notifications").SendAsync(eventName, payload, ct);
```

**6.3 — `Pena_e_Arte.Infrastructure/Hubs/NotificationHub.cs`** — add a conditional
auto-join, leaving `JoinStudio`/`LeaveStudio` untouched:

```csharp
public override async Task OnConnectedAsync()
{
    string role = Context.User?.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
    if (string.Equals(role, "issuer", StringComparison.OrdinalIgnoreCase))
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "platform:issuer-notifications");
    }

    await base.OnConnectedAsync();
}
```

No `OnDisconnectedAsync` override is needed — SignalR removes a disconnected connection
from all of its groups automatically (same as every other hub in this codebase relies on
implicitly).

**6.4 — `Pena_e_Arte.Infrastructure/Services/MailKit/EmailRenderer.cs`** — add
`RenderStudioRegisteredIssuer`, following the existing template style/branding
conventions already used by the other `RenderX` methods in this file (see
`RenderAppointmentCreatedStudio` for the closest existing "internal, not client-facing"
tone/layout — this new email has no `showBranding` parameter because it is never seen by
a studio or client, only the issuer). Content must include: studio name, city, owner
email, NIPT if provided (state "Not provided" if null), trial expiry date, whether a
referral code was applied, and a link built from the `studioDetailUrl` parameter to
`/platform/studios/{id}` (confirm this is in fact `IssuerStudioDetailPage`'s route in
`router.tsx` before wiring the link — adjust the path if it differs).

---

## 7. Phase 4 — Frontend

**7.1 — New hook: `frontend/src/shared/hooks/useIssuerNotificationHub.ts`** — deliberately
separate from `useSignalR.ts` (see §3). Connects only `/hubs/notification`, does **not**
call `.invoke("JoinStudio", ...)` (the server-side auto-join in §6.3 handles group
membership), and reuses the exact connection-building conventions already established in
`useSignalR.ts` (the dev-proxy WebSocket-upgrade workaround comment, the
automatic-reconnect config, and — critically — the block-body event-handler requirement:
copy the existing warning comment verbatim, since the same SignalR result-mismatch bug
applies here identically):

```ts
import { useEffect } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { notificationsApi } from "@/features/notifications/notificationsApi";
import { incrementUnread } from "@/features/notifications/notificationsSlice";

export function useIssuerNotificationHub() {
  const token    = useAppSelector((s) => s.auth.token);
  const dispatch = useAppDispatch();

  useEffect(() => {
    if (!token) return;

    const isLocalDevBrowser = import.meta.env.DEV && window.location.hostname === "localhost";
    const hubBase = isLocalDevBrowser ? "http://localhost:5078" : "";

    const connection = new HubConnectionBuilder()
      .withUrl(`${hubBase}/hubs/notification`, { accessTokenFactory: () => token! })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    // See useSignalR.ts: a single-expression arrow handler implicitly returns
    // dispatch's return value, which SignalR tries to send back as an invocation
    // result the server never asked for — block bodies only.
    connection.on("NotificationReceived", () => {
      dispatch(notificationsApi.util.invalidateTags(["NotificationLog"]));
      dispatch(incrementUnread());
    });

    const start = connection.start().catch(() => {});

    return () => {
      void start.finally(() => connection.stop());
    };
  }, [token, dispatch]);
}
```

**7.2 — `frontend/src/layouts/IssuerLayout.tsx`** — call the new hook:

```ts
import { useIssuerNotificationHub } from "@/shared/hooks/useIssuerNotificationHub";
// ...
export function IssuerLayout() {
  useIssuerNotificationHub();
  // ...existing body, unchanged...
```

No change needed to `NotificationBell.tsx`, `NotificationLogListPage.tsx`,
`ChannelBadge.tsx`, or `notification.types.ts` — they're channel/shape-agnostic and will
render an `Email`-channel entry with `recipientName` correctly once §5.3's fix ships.

---

## 8. Phase 5 — Database

**8.1 — New migration.** No column changes are needed (§2.9), but the issuer read in
§5.3 does a full-table `IgnoreQueryFilters()` scan filtered only by `RecipientType` — with
no `StudioId` in the `WHERE` clause, none of the three existing indexes on
`notification_logs` (`(StudioId, RecipientId)`, `SentAt`, `(StudioId, CreatedAt)` — see
`NotificationLogConfiguration.cs`) help this query as the table grows. Add:

```csharp
builder.HasIndex(n => n.RecipientType)
       .HasDatabaseName("ix_notification_logs_recipient_type");
```

Run `dotnet ef migrations add AddNotificationLogRecipientTypeIndex --project Pena_e_Arte.Infrastructure`.

---

## 9. Phase 6 — Help Menu / user manual / onboarding tour (CLAUDE.md rule #7)

This is user-facing for the issuer role (a new bell entry + email they'll start
receiving), so the sync obligation applies:

- `frontend/src/features/help/helpContent.ts` — add or extend the issuer-facing
  Notifications entry to mention that new-studio-registration events now appear in the
  platform bell/log and by email. Grep the file for the existing issuer notifications
  entry before adding a new one — extend, don't duplicate.
- `frontend/public/user-manual/index.html` — same addition in the issuer section.
- `frontend/src/features/help/tours/issuerTour.ts` — only touch this if an existing tour
  step specifically walks through the notification bell or `/platform/studios`; if no
  step currently references either, no onboarding-tour change is required (state this
  explicitly rather than silently skipping it, per CLAUDE.md rule #7's own convention).

---

## 10. Phase 7 — Tests

Follow the exact existing test-file placement convention (confirmed present in the repo):

- `tests/Pena_e_Arte.UnitTests/Studios/RegisterStudioHandlerTests.cs` — extend to assert
  `sender.Send(It.IsAny<SendStudioRegisteredNotificationCommand>(), ...)` is called with
  the newly created studio's id after a successful registration.
- `tests/Pena_e_Arte.UnitTests/Studios/SendStudioRegisteredNotificationHandlerTests.cs`
  (new) — cover: studio not found (logs warning, no throw); zero issuer accounts (logs
  warning, no throw, no email sent); one issuer email succeeds (log written with
  `IsSuccess = true`, `RecipientType.Issuer`, `RecipientId == studio.Id`,
  `realtime.NotifyIssuersAsync` invoked once); one issuer email throws (caught, logged,
  `IsSuccess` reflects whether *any* recipient succeeded — test both the
  all-fail-`false` and one-fail-one-succeed-`true` cases); multiple issuer accounts (every
  one gets `SendEmailAsync` called, single `NotificationLog` row still written once).
- `tests/Pena_e_Arte.UnitTests/Notifications/GetNotificationsHandlerTests.cs` — extend
  with an issuer-role case: seed `NotificationLog` rows across two different
  `StudioId`s, both `RecipientType.Issuer`, assert an issuer caller gets both back
  regardless of their own `tenant.StudioId`, and that a non-issuer, non-artist,
  non-client role (if any exists — `owner`) does *not* see `RecipientType.Issuer` rows
  even for their own studio (the `else` branch's `query.RecipientId.HasValue` path must
  not accidentally match these). Also assert `recipientName` resolves to the studio's
  name, not a raw GUID.
- `tests/Pena_e_Arte.IntegrationTests/Application/NotificationDispatchTests.cs` — extend
  with an end-to-end case: call `RegisterStudioCommand` against a real (test) DB, assert
  a `NotificationLog` row with `RecipientType.Issuer` exists afterward with the correct
  `StudioId`/`RecipientId`, without needing to mock `IIdentityService`/`INotificationService`
  if this test file already runs against real infra fakes — otherwise mock consistently
  with how this file already handles the `AppointmentCreated` case.
- New: a `NotificationHub`/`RealtimeNotifier` unit or integration test asserting an
  issuer connection lands in the `"platform:issuer-notifications"` group on
  `OnConnectedAsync`, and a non-issuer connection does not (mirrors the existing
  `JoinStudio` cross-tenant authorization tests added in the 2026-07-26 security
  remediation — match that file's style if it still exists in
  `tests/Pena_e_Arte.IntegrationTests/`).

Run the full backend suite (`dotnet test`) and full frontend suite (`pnpm test`, `pnpm
lint`, `pnpm build`) before considering this done — this repo's CI lint gate is blocking
(see Decisions Log, 2026-07-26 entry), not advisory.

---

## 11. Phase 8 — Docs to update in the same change

- **`docs/claude/architecture.md`**, IgnoreQueryFilters table — add a new row (currently
  ends at #41):

  ```
  | 42 | `GetNotificationsQuery` (issuer branch) | Cross-tenant `NotificationLog` read — the issuer's own `tenant_id` claim would otherwise silently scope this to one arbitrary studio instead of returning every `RecipientType.Issuer` row platform-wide | IssuerOnly |
  ```

- **`docs/claude/architecture.md`**, Decisions Log (or wherever the running dated log of
  shipped changes lives in this file — follow its existing entry format exactly, see the
  2026-07-26 security remediation entry for the level of detail expected) — add an entry
  for this feature: what shipped, why `NotificationHub` got a conditional auto-join
  instead of a new hub, why `IIdentityService` gained a method instead of a new
  interface, and the pre-existing `GetNotificationsQuery` issuer-scoping bug this closed
  as a side effect.
- **`DECISIONS.md`** (repo root) — add an entry if this file is where cross-cutting
  product/architecture decisions get recorded per this repo's existing convention (check
  its current entries for the right section before appending).

---

## 12. Definition of done

- [ ] New studio registration triggers exactly one `NotificationLog` row,
      `RecipientType.Issuer`, `Channel.Email`, `StudioId`/`RecipientId` both set to the
      new studio's id.
- [ ] Every account in the `issuer` role receives a real email (verify against the local
      Resend sandbox/test mode this repo already uses for other email tests).
- [ ] An issuer connected to the platform admin console at the moment of registration sees
      their `NotificationBell` unread count increment live, with no page refresh, within
      the SignalR round-trip.
- [ ] An issuer who was *not* connected at the time sees the entry appear in their bell
      and in `/notifications` the next time they log in or open the panel.
- [ ] `GetNotificationsQuery` now returns cross-tenant `RecipientType.Issuer` rows for an
      issuer caller regardless of which studio their own JWT happens to be tagged with —
      and this is covered by a test that fails without the fix (seed two different
      studios' worth of issuer-recipient rows, assert both come back).
- [ ] Artist and client branches of `GetNotificationsQuery` are byte-for-byte unchanged in
      behavior (existing tests for those still pass unmodified).
- [ ] `pnpm lint`, `pnpm build`, `pnpm test`, `dotnet build`, `dotnet test` all green.
- [ ] Help Menu + user manual updated (or onboarding tour explicitly stated as
      not-applicable, per §9).
- [ ] `architecture.md` IgnoreQueryFilters table row #42 added; Decisions Log entry added;
      `DECISIONS.md` updated if applicable.
- [ ] Migration applied cleanly against a fresh DB (`dotnet ef database update`) with no
      data loss on the existing `notification_logs` table.
