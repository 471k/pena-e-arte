# Overnight Prompt — In-App Messaging (Client ↔ Artist ↔ Owner)

> Engineered by the "Pena e Artë - Engineering Consultation" project. Paste this whole file
> as the prompt for a Claude Code session with full read/write access to the
> `Pena_e_Arte.*` repo. Do not summarize or re-derive it — execute it as written, part by
> part, in order. Where a decision is already made below, do not re-litigate it; where a
> question is left open, resolve it yourself and record the choice in the architecture doc
> update (Part 11) the same way every prior overnight prompt in this repo has.

---

## Pre-flight

1. Read `CLAUDE.md`, then `docs/claude/architecture.md`, `docs/claude/backend.md`,
   `docs/claude/frontend.md`, `docs/claude/database.md`, `docs/claude/conventions.md` in
   full. This prompt assumes you have.
2. Confirm the repo builds and tests pass **before** you start:
   ```bash
   dotnet build
   dotnet test
   cd frontend && pnpm install && pnpm tsc --noEmit && pnpm lint && pnpm test && cd ..
   ```
   If any of these are already red on `main`, stop and report — do not build on top of a
   broken baseline.
3. Branch: `feat/in-app-messaging`.
4. This is a **meaningfully sized feature** (new domain area, new hub, new notification
   path, three new frontend surfaces) — the industry-feature-parity report
   (`docs/claude/industry-feature-parity-report-2026-07-20.md`, item B13) already flagged it
   as such and said so explicitly: "fully-specified, ready to implement, though it's a
   meaningfully sized feature — not a quick win." Budget the full night for it. If you run
   out of time, stop at a clean part boundary (see "Build checklist") and report exactly
   what's done vs. not, the same way `overnight-prompt-full-app-master-audit-2026-07-20.md`'s
   multi-session closure passes did — do not half-finish a Part.

---

## Context — current state (verified against live source, 2026-08-26)

- **B13 in the parity report** is the source of this feature: "In-app messaging/two-way
  chat — MISSING — P1 — Only one-way notifications + a platform bug-report thread
  (`FeedbackMessage`) exist." The report's own sketch: `Conversation`/`ChatMessage`
  entities, reusing existing SignalR hub infrastructure, new `Messaging` Application
  module, frontend thread view on `AppointmentDetailPage.tsx` + a client inbox. This
  prompt fully specifies that sketch — every open question the report left is resolved
  below in "Decisions."
- **Closest existing analog: Support Escalation** (`docs/claude/architecture.md`, "Support
  Escalation — 2026-07-21"). `FeedbackReport`/`FeedbackMessage` is a two-party threaded
  message system with a SignalR hub (`SupportHub`) and an `IRealtimeNotifier` extension
  (`NotifyTicketAsync`) — but it is deliberately **not tenant-scoped** (issuer reads across
  every studio) and its hub uses a group-per-resource model (`ticket:{id}`, joined on
  demand, ownership-checked in `JoinTicket`). Messaging is the opposite on both counts: it
  is tenant-scoped (a `TenantEntity`, like every other per-studio feature) and, per the
  hub design decided below, does **not** need a group-per-resource model at all. Study
  `SupportHub.cs`, `FeedbackAccessGuard.cs`, `PostFeedbackMessageCommand.cs`, and
  `FeedbackEndpoints.cs` for the shape to mirror — but do not copy `SupportHub`'s
  ticket-group pattern; see Decision 3 below for why.
- **`SupportHub.JoinTicket`'s history is a live lesson**: architecture.md documents that
  the original `JoinTicket` did not validate ticket ownership before adding a caller to
  the SignalR group — any authenticated user who learned a ticket GUID could join it and
  read all future replies, a real vulnerability caught in a pre-merge `/code-review high`
  pass, not before. Do not repeat that mistake here. Decision 3's hub design sidesteps the
  entire class of bug by never exposing a joinable-by-id group in the first place — but if
  you deviate from Decision 3 for any reason, you must independently re-derive and apply
  the same ownership check `SupportHub` now has.
- **Closest existing analog for a tenant-scoped child entity: `DesignRevision`.** Unlike
  `FeedbackMessage` (child of the non-tenant `FeedbackReport`, configured inline in
  `AppDbContext.OnModelCreating`, no query filter), `DesignRevision` is itself a full
  `TenantEntity` with its own `StudioId`, its own query filter, and its own
  `IEntityTypeConfiguration` via the shared `TenantEntityConfiguration<T>` base class
  (`Pena_e_Arte.Infrastructure/Persistence/Configurations/TenantEntityConfiguration.cs`).
  **`Conversation` and `ChatMessage` must follow the `DesignRevision` pattern, not the
  `FeedbackMessage` pattern** — both are real per-studio data, not a non-tenant issuer
  ticket system.
- **`Client.ArtistId`** (nullable `Guid`) already models a client's assigned artist
  (`Pena_e_Arte.Domain/Entities/Client.cs`) — this is the "client-artist assignment"
  concept referenced below. `Client.UserId` and `Artist.UserId` are both nullable `Guid?`
  — a `Client`/`Artist` row can exist with no linked Identity login (e.g. an
  owner-created record, or a client added via a raw contact reminder). **Messaging
  requires both parties to have a real login** — every eligibility/contact query below
  must filter out rows where `UserId is null`.
- **There is no `Owner` domain entity.** `Studio.OwnerEmail` is the only link to the
  owner's identity — resolving "the owner" for a studio means
  `IIdentityService.GetUserIdByEmailAsync(studio.OwnerEmail)` (and
  `GetUserDisplayNameAsync(studio.OwnerEmail)` for display), exactly the same indirection
  `RegisterOAuthUserHandler`'s existing owner-email-match check already relies on. A
  studio's owner user might not have completed signup yet (rare, but the `Studio` row can
  predate registration) — resolution can return null; every caller must handle that by
  simply omitting the owner from contacts, not throwing.
- **`IJobScheduler`/`JobScheduler`** (added 2026-08-21, `docs/claude/database.md`-adjacent
  — see `overnight-prompt-manual-client-reminders-2026-08-21.md`) is the required
  indirection for scheduling Hangfire work from the Application layer — Application must
  never reference Hangfire types directly (layering rule in `architecture.md`). Extend
  this interface; do not call `IBackgroundJobClient` from a handler.
- **`INotificationPreferenceService.IsEnabledAsync(studioId, NotificationType, NotificationChannel, ct)`**
  gates every Email/SMS notification already in the codebase (`NotificationType` currently
  has 9 values, none for messaging). `NotificationChannel.InApp` is documented as
  "bell/log only, never routed through the per-event email/SMS opt-in preferences" — for
  messaging specifically, there is no separate in-app "notification about a notification"
  needed at all (see Decision 5): the message itself, already durably stored in
  `ChatMessage`, **is** the in-app notification. Only the Email fallback needs a
  `NotificationType`.
- **Nav/layout precedent**: `NotificationBell` (`frontend/src/features/notifications/components/NotificationBell.tsx`)
  is imported individually into each role layout (`ClientLayout.tsx`, `ArtistLayout.tsx`,
  `OwnerLayout.tsx`, `IssuerLayout.tsx`) — there is no single shared header. Mirror this
  exactly for the new messages nav entry/badge; do not try to hoist it into one shared
  `AppLayout` location that doesn't currently exist.
- **`useSupportHub`'s two documented, already-fixed bugs are directly relevant** and must
  not be reintroduced: (1) it originally never rejoined its SignalR group after
  `withAutomaticReconnect()`'s automatic reconnect — fixed with an `onreconnected` handler;
  (2) sending a reply double-refetched because the sender is a member of their own
  group and received their own message back over the wire — fixed by skipping
  invalidation when the echoed message's author is the current user. Build
  `useChatHub` with both fixes in from the start.

---

## Decisions (already made — do not re-litigate)

1. **Scope is exactly client ↔ artist, client ↔ owner, artist ↔ owner — no client↔client,
   no artist↔artist, no issuer.** The user request and every benchmark comparator
   (Vagaro/Fresha/Boulevard/GlossGenius-tier "message your provider") are two-party,
   cross-role threads, not a group chat or social feature. Issuer already has a fully
   separate, working communication channel (`FeedbackReport`/`SupportHub`) for
   studio↔platform support — this feature does not touch it and issuer is not a valid
   participant. `ClientAndAbove` policy technically admits an issuer token to the
   endpoints below, but every handler filters by `ICurrentTenant.IsSet`
   (see Decision 2), which is normally false for an issuer, so an issuer gets empty
   results / a validation rejection on write, not a 500 or a data leak. This is
   deliberate, not an oversight — do not add issuer-specific messaging UI or a bypass.

2. **`Conversation`/`ChatMessage` are ordinary tenant-scoped entities (`DesignRevision`
   pattern), not a `FeedbackReport`-style non-tenant exception.** Every write endpoint's
   command validator rejects when `!currentTenant.IsSet`, mirroring
   `SubmitFeedbackValidator`'s existing `RuleFor(x => x).Must(_ => currentTenant.IsSet)`
   rule verbatim. No `IgnoreQueryFilters()` anywhere in this feature — do not add a 42nd
   approved usage to the `IgnoreQueryFilters()` table for this; there is no cross-tenant
   read requirement here at all.

3. **`ChatHub` auto-joins a personal `user:{userId}` SignalR group on connect — it does
   not use `SupportHub`/`ScheduleHub`'s join-a-resource-group-by-id model.** A 1:1
   conversation only ever has two participants, and both are already fully authenticated
   on their own connection — there is no resource id a client could leak, guess, or need
   to explicitly join/leave. One consequence, and the actual reason this is simpler and
   safer than the alternative: a user's single hub connection receives `MessageReceived`
   for **every** conversation they're part of, which is exactly what's needed to update
   an unread-badge while browsing anywhere in the app, not just while a specific thread is
   open — `SupportHub`'s per-ticket-group model would need a second mechanism for that
   (and doesn't have one; the support ticket badge, if it has one, is out of scope here to
   investigate). This sidesteps the entire bug class `SupportHub.JoinTicket` originally
   had — there is no `Join`-by-id call to forget an ownership check on.

4. **Eligibility (who can message whom) is relationship-based, not "anyone in the studio,"
   and is enforced server-side on every write** (never trust a client-supplied
   `recipientUserId` just because the UI only shows valid contacts): a client may message
   an artist iff that artist is `Client.ArtistId` **or** the client has ≥1 `Appointment`
   with that artist at this studio (covers "let the studio choose my artist" bookings
   where the assignment field may lag the actual working relationship); a client or artist
   may message the studio's owner unconditionally (any client/artist row scoped to this
   studio may reach the owner — this mirrors Support Escalation's own reasoning that a
   client should always be able to reach a human); the owner may message any active artist
   or any client in their studio unconditionally. `GetConversationContactsQuery` (the
   "who can I start a new thread with" list for the UI) and `CreateConversationCommand`'s
   handler (the actual write-path check) **must call the exact same eligibility logic** —
   extract it once (see `ConversationEligibility` in Part 5) so the two can't drift, the
   same reasoning that produced `FeedbackAccessGuard`.

5. **No `NotificationLog`/in-app-notification-row duplication for messages.** Every other
   notification type's `NotificationLog` row *is* the notification content (a reminder
   text, a status change). Here, `ChatMessage` already durably stores the content and its
   own read state (`ReadAt`) — writing a second copy into `NotificationLog` would be
   redundant data with no consumer. The only new notification-preference surface is a
   single new `NotificationType.MessageReceived`, **Email channel only** (no SMS): SMS
   costs real money per send (`docs/claude/architecture.md`'s Manual Client Reminders
   entry already flags SMS cost as a reason to gate it behind a quota) and a live back-
   and-forth conversation would trip that cost concern on every single message — Email is
   the correct, standard "you have a new message" channel here, matching how most
   vertical-SaaS competitors handle offline message notification.
6. **Debounce the email notification**: only send it when the new message is the
   *first* unread message in that conversation for the recipient (i.e., they had zero
   unread messages from the other participant immediately before this one was added).
   This prevents a burst of replies from generating a burst of emails — the recipient
   gets exactly one "you have a new message" email per unread streak, not one per
   message. Implement this as a count check inside `SendChatMessageHandler` before
   inserting the new message (see Part 6).
7. **Text-only messages in this pass — no attachments.** `FeedbackReport.AttachmentUrls`
   (R2 presign flow) is a real, working precedent this feature *could* reuse later, but
   adding it now doubles this prompt's scope for a capability the user's request didn't
   ask for. Flagged explicitly in "Out of Scope," not silently dropped.
8. **No message deletion/editing in this pass.** Neither `FeedbackMessage` nor any
   competitor in the benchmark set's messaging thread supports delete/edit as a baseline
   expectation — this is a reasonable v1 scope line, not a standard gap. Flagged in "Out
   of Scope."
9. **`POST /api/v1/conversations` is a get-or-create endpoint and returns `200 OK`
   whether it found an existing conversation or created a new one** — a deliberate,
   documented deviation from the "201 Created for POST that creates a resource"
   convention in `conventions.md`, because the caller (a "message this person" button)
   never knows in advance whether a thread already exists and shouldn't have to branch on
   it. Document this deviation inline in the endpoint's code comment, the same way other
   deliberate deviations in this codebase are commented at the point of deviation rather
   than left for someone to rediscover.
10. **Conversation participant ordering is normalized** (`ParticipantAUserId` is always
    the lexicographically/numerically smaller `Guid` of the two) so a unique index can
    prevent duplicate conversations for the same pair — see Part 1.

---

## Part 1 — Domain + EF Core

### 1a. New `Pena_e_Arte.Domain/Entities/Conversation.cs`

```csharp
namespace Pena_e_Arte.Domain.Entities;

public class Conversation : TenantEntity
{
    private Conversation() { }

    public Guid ParticipantAUserId { get; private set; }
    public string ParticipantARole { get; private set; } = string.Empty;
    public Guid ParticipantBUserId { get; private set; }
    public string ParticipantBRole { get; private set; } = string.Empty;

    public DateTime? LastMessageAt { get; private set; }
    public string? LastMessagePreview { get; private set; }
    public Guid? LastMessageSenderUserId { get; private set; }

    public ICollection<ChatMessage> Messages { get; private set; } = [];

    public static Conversation Create(
        Guid studioId, Guid userAId, string userARole, Guid userBId, string userBRole)
    {
        // Normalize so (studio, X, Y) and (studio, Y, X) always collide on the unique index
        // below — the caller does not (and should not) know or care which side of the pair
        // it's on.
        bool aFirst = userAId.CompareTo(userBId) <= 0;

        return new Conversation
        {
            StudioId = studioId,
            ParticipantAUserId = aFirst ? userAId : userBId,
            ParticipantARole = aFirst ? userARole : userBRole,
            ParticipantBUserId = aFirst ? userBId : userAId,
            ParticipantBRole = aFirst ? userBRole : userARole,
        };
    }

    public bool IsParticipant(Guid userId) =>
        userId == ParticipantAUserId || userId == ParticipantBUserId;

    public (Guid UserId, string Role) OtherParticipant(Guid userId) =>
        userId == ParticipantAUserId
            ? (ParticipantBUserId, ParticipantBRole)
            : (ParticipantAUserId, ParticipantARole);

    /// <summary>Denormalized preview fields for the inbox list — avoids a join/subquery per
    /// row just to show the last line and timestamp. Truncated to 140 chars; the full body
    /// lives only on the ChatMessage row.</summary>
    public void RecordLastMessage(Guid senderUserId, string body)
    {
        LastMessageAt = DateTime.UtcNow;
        LastMessageSenderUserId = senderUserId;
        LastMessagePreview = body.Length <= 140 ? body : body[..140];
    }
}
```

### 1b. New `Pena_e_Arte.Domain/Entities/ChatMessage.cs`

```csharp
namespace Pena_e_Arte.Domain.Entities;

public class ChatMessage : TenantEntity
{
    private ChatMessage() { }

    public Guid ConversationId { get; private set; }
    public Guid SenderUserId { get; private set; }
    public string SenderRole { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public DateTime? ReadAt { get; private set; }

    public static ChatMessage Create(
        Guid studioId, Guid conversationId, Guid senderUserId, string senderRole, string body) =>
        new()
        {
            StudioId = studioId,
            ConversationId = conversationId,
            SenderUserId = senderUserId,
            SenderRole = senderRole,
            Body = body.Trim(),
        };

    /// <summary>Idempotent — calling this on an already-read message is a no-op.</summary>
    public void MarkRead() => ReadAt ??= DateTime.UtcNow;
}
```

### 1c. `AppDbContext.cs` — add the two new DbSets + query filters

Add to the tenant-scoped DbSet block:
```csharp
public DbSet<Conversation> Conversations => Set<Conversation>();
public DbSet<ChatMessage>  ChatMessages  => Set<ChatMessage>();
```
Add to `OnModelCreating`, in the same block as the other soft-delete-aware filters
(`ManualReminder`, `HelpSearchLog`, etc. — **note**: neither new entity gets a `DeletedAt`
column; see Decision 8, there is no delete capability in this pass, so there is nothing to
filter on beyond `StudioId`):
```csharp
builder.Entity<Conversation>().HasQueryFilter(c => c.StudioId == tenant.StudioId);
builder.Entity<ChatMessage>() .HasQueryFilter(m => m.StudioId == tenant.StudioId);
```

Also add both to `Pena_e_Arte.Application/Persistence/IAppDbContext.cs`:
```csharp
DbSet<Conversation> Conversations { get; }
DbSet<ChatMessage>  ChatMessages  { get; }
```

### 1d. New `Pena_e_Arte.Infrastructure/Persistence/Configurations/ConversationConfiguration.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class ConversationConfiguration : TenantEntityConfiguration<Conversation>
{
    protected override string TableName => "conversations";

    public override void Configure(EntityTypeBuilder<Conversation> builder)
    {
        base.Configure(builder);

        builder.Property(c => c.ParticipantARole).HasMaxLength(20).IsRequired();
        builder.Property(c => c.ParticipantBRole).HasMaxLength(20).IsRequired();
        builder.Property(c => c.LastMessagePreview).HasMaxLength(140);

        builder.HasIndex(c => new { c.StudioId, c.ParticipantAUserId, c.ParticipantBUserId })
               .IsUnique()
               .HasDatabaseName("ix_conversations_studio_participants");

        // Inbox listing: "my conversations, most recent first" — this is the hot query.
        builder.HasIndex(c => new { c.StudioId, c.ParticipantAUserId, c.LastMessageAt })
               .HasDatabaseName("ix_conversations_studio_participant_a_last_message");
        builder.HasIndex(c => new { c.StudioId, c.ParticipantBUserId, c.LastMessageAt })
               .HasDatabaseName("ix_conversations_studio_participant_b_last_message");

        builder.HasMany(c => c.Messages)
               .WithOne()
               .HasForeignKey(m => m.ConversationId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
```

### 1e. New `Pena_e_Arte.Infrastructure/Persistence/Configurations/ChatMessageConfiguration.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class ChatMessageConfiguration : TenantEntityConfiguration<ChatMessage>
{
    protected override string TableName => "chat_messages";

    public override void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        base.Configure(builder);

        builder.Property(m => m.SenderRole).HasMaxLength(20).IsRequired();
        builder.Property(m => m.Body).HasMaxLength(2000).IsRequired();

        // Cursor pagination for one thread ("messages before X") + the unread-count query
        // ("unread messages in conversations I'm part of") both hit this.
        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt })
               .HasDatabaseName("ix_chat_messages_conversation_created");
        builder.HasIndex(m => new { m.ConversationId, m.SenderUserId, m.ReadAt })
               .HasDatabaseName("ix_chat_messages_conversation_sender_read");
    }
}
```

### 1f. Migration

```bash
dotnet ef migrations add AddMessagingConversationsAndChatMessages \
  --project Pena_e_Arte.Infrastructure \
  --startup-project Pena_e_Arte.API
```
Review the generated migration by hand before applying — confirm both tables get
`studio_id`/`created_at`/`updated_at` from `TenantEntityConfiguration<T>`, the unique index
on `(studio_id, participant_a_user_id, participant_b_user_id)`, and the cascade-delete FK
from `chat_messages.conversation_id` → `conversations.id`. Apply it:
```bash
dotnet ef database update --project Pena_e_Arte.Infrastructure --startup-project Pena_e_Arte.API
```

---

## Part 2 — Notification plumbing

### 2a. `Pena_e_Arte.Domain/Enums/NotificationType.cs`

Add one value:
```csharp
public enum NotificationType
{
    AppointmentCreated,
    AppointmentConfirmed,
    AppointmentCancelled,
    DepositCaptured,
    PaymentRefunded,
    IntakeFormSubmitted,
    ConsentFormSigned,
    DesignReviewed,
    Aftercare,
    MessageReceived, // new — Email channel only, see Decision 5
}
```
This widens `INotificationPreferenceService`/`StudioNotificationPreference`'s existing
per-event-per-channel matrix by one row — the same additive shape every prior new
`NotificationType` addition has taken. Default it to **enabled** for the `Email` channel
(matching the existing default posture for every other type) in whatever seed/default
logic `StudioNotificationPreference` currently uses for new studios — locate it (likely
alongside wherever the other 9 types' defaults are set) and extend it the same way, do not
invent a different default mechanism for just this one type.

### 2b. `Pena_e_Arte.Domain/Interfaces/IJobScheduler.cs`

Add:
```csharp
void EnqueueNewMessageEmail(Guid chatMessageId);
```

### 2c. `Pena_e_Arte.Infrastructure/Services/JobScheduler.cs`

Add the implementation, following the existing immediate-fire (`Enqueue`, not `Schedule`)
pattern used by `EnqueueArtistInvite`:
```csharp
public void EnqueueNewMessageEmail(Guid chatMessageId) =>
    backgroundJobs.Enqueue<ChatNotificationJob>(j => j.SendNewMessageEmailAsync(chatMessageId, default));
```

### 2d. New `Pena_e_Arte.Infrastructure/Jobs/ChatNotificationJob.cs`

```csharp
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Pena_e_Arte.Infrastructure.Jobs;

public class ChatNotificationJob(
    IAppDbContext db,
    INotificationPreferenceService prefs,
    INotificationService notifications,
    IIdentityService identity)
{
    public async Task SendNewMessageEmailAsync(Guid chatMessageId, CancellationToken ct)
    {
        // IgnoreQueryFilters is NOT used here — this job runs with no ICurrentTenant scope
        // (Hangfire jobs are not HTTP requests), so the tenant filter never applies to begin
        // with; querying by primary key/foreign key directly is correct and matches every
        // other existing job (AppointmentReminderJob, ManualReminderJob) in this codebase.
        ChatMessage? message = await db.ChatMessages
            .FirstOrDefaultAsync(m => m.Id == chatMessageId, ct);
        if (message is null) return;

        Conversation? conversation = await db.Conversations
            .FirstOrDefaultAsync(c => c.Id == message.ConversationId, ct);
        if (conversation is null) return;

        (Guid recipientUserId, _) = conversation.OtherParticipant(message.SenderUserId);

        bool enabled = await prefs.IsEnabledAsync(
            message.StudioId, NotificationType.MessageReceived, NotificationChannel.Email, ct);
        if (!enabled) return;

        string? email = await identity.GetUserEmailAsync(recipientUserId, ct);
        if (string.IsNullOrEmpty(email)) return;

        await notifications.SendEmailAsync(
            email,
            "You have a new message",
            $"You have a new message waiting for you. Log in to reply: " +
            $"{/* platform base URL config — reuse whatever existing email templates use */""}/messages");
    }
}
```
Wire the platform base URL the same way every other outbound-email job in this codebase
already does (grep an existing job like `ManualReminderJob`/`AppointmentReminderJob` for
its exact config key — do not invent a new one).

### 2e. `docs/claude/architecture.md` — SignalR Event Naming Convention table

Add two rows to the existing table (Part 12 below covers the rest of the doc updates —
this one line belongs in the SignalR section specifically, not the Feature Module Map):
```
MessageReceived         new chat message posted in a conversation (pushed to both participants' user:{userId} groups)
ConversationRead        the other participant marked the conversation read (read-receipt update)
```

---

## Part 3 — SignalR hub + `IRealtimeNotifier`

### 3a. New `Pena_e_Arte.Infrastructure/Hubs/ChatHub.cs`

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Pena_e_Arte.Infrastructure.Hubs;

// See Decision 3 in the messaging overnight prompt: unlike ScheduleHub/SupportHub's
// join-a-resource-group-by-id model, every connection here auto-joins a personal
// `user:{userId}` group on connect. A 1:1 conversation only ever has two participants,
// both already fully authenticated on their own connection, so there is no resource id a
// client could leak or guess — this sidesteps the ownership-check bug class SupportHub's
// JoinTicket originally had (see architecture.md's Support Escalation code-review entry)
// by construction, not by an extra check. It also means one connection receives
// MessageReceived for every conversation the user is part of, which the inbox unread
// badge needs regardless of which (if any) thread is currently open.
// /hubs paths are exempt from TenantMiddleware, so ICurrentUser/ICurrentTenant are never
// populated for hub invocations — claims are read directly from Context.User, matching
// every other hub in this codebase.
[Authorize]
public class ChatHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (Guid.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        }
        await base.OnConnectedAsync();
    }
}
```

### 3b. `Pena_e_Arte.Domain/Interfaces/IRealtimeNotifier.cs`

Add:
```csharp
Task NotifyUserAsync(Guid userId, string eventName, object payload, CancellationToken ct = default);
```

### 3c. `Pena_e_Arte.Infrastructure/Services/RealtimeNotifier.cs`

Add `IHubContext<ChatHub> chatHub` to the primary constructor and implement:
```csharp
public async Task NotifyUserAsync(Guid userId, string eventName, object payload, CancellationToken ct) =>
    await chatHub.Clients.Group($"user:{userId}").SendAsync(eventName, payload, ct);
```

### 3d. `Pena_e_Arte.API/Program.cs`

Add alongside the other four `MapHub` calls:
```csharp
app.MapHub<ChatHub>("/hubs/chat");
```
`AddSignalR()` is already called once globally (`InfrastructureServiceExtensions.cs:81`)
— it covers every hub, no per-hub registration needed there. `IRealtimeNotifier`'s DI
registration (`InfrastructureServiceExtensions.cs:135`) also needs no change beyond the
constructor already gaining a new `IHubContext<ChatHub>` parameter, which SignalR
auto-registers once `ChatHub` is mapped.

---

## Part 4 — Contracts

### 4a. `Pena_e_Arte.Contracts/Requests/CreateConversationRequest.cs`
```csharp
public record CreateConversationRequest(Guid RecipientUserId);
```

### 4b. `Pena_e_Arte.Contracts/Requests/SendChatMessageRequest.cs`
```csharp
public record SendChatMessageRequest(string Body);
```

### 4c. `Pena_e_Arte.Contracts/Responses/ConversationResponse.cs`
```csharp
public record ConversationResponse(
    Guid Id,
    Guid OtherUserId,
    string OtherRole,
    string OtherDisplayName,
    string? OtherAvatarUrl,
    DateTime? LastMessageAt,
    string? LastMessagePreview,
    bool LastMessageFromMe,
    int UnreadCount,
    DateTime CreatedAt);
```

### 4d. `Pena_e_Arte.Contracts/Responses/ChatMessageResponse.cs`
```csharp
public record ChatMessageResponse(
    Guid Id,
    Guid ConversationId,
    Guid SenderUserId,
    string SenderRole,
    string Body,
    DateTime CreatedAt,
    DateTime? ReadAt);
```

### 4e. `Pena_e_Arte.Contracts/Responses/ConversationContactResponse.cs`
```csharp
public record ConversationContactResponse(
    Guid UserId,
    string Role,
    string DisplayName,
    string? AvatarUrl,
    Guid? ExistingConversationId);
```

---

## Part 5 — Application layer — new `Pena_e_Arte.Application/Messaging/` folder

### 5a. `ConversationEligibility.cs` (shared — both the contacts query and the create
handler call this, so they can never drift; same reasoning as `FeedbackAccessGuard`)

```csharp
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Messaging;

internal record EligibleContact(Guid UserId, string Role, string DisplayName, string? AvatarUrl);

internal static class ConversationEligibility
{
    /// <summary>Every user the given caller is allowed to message, computed per Decision 4:
    /// client → their appointment/assigned artists + owner; artist → their appointment/
    /// assigned clients + owner; owner → every active artist + every client. Rows with no
    /// linked login (UserId null) are excluded — you cannot message someone who can't log
    /// in to read it.</summary>
    public static async Task<List<EligibleContact>> GetContactsAsync(
        IAppDbContext db, IIdentityService identity, Guid studioId, Guid callerUserId, string callerRole,
        CancellationToken ct)
    {
        List<EligibleContact> contacts = [];

        if (string.Equals(callerRole, "client", StringComparison.OrdinalIgnoreCase))
        {
            Domain.Entities.Client? client = await db.Clients
                .FirstOrDefaultAsync(c => c.UserId == callerUserId, ct);
            if (client is null) return contacts;

            HashSet<Guid> artistIds = await db.Appointments
                .Where(a => a.ClientId == client.Id)
                .Select(a => a.ArtistId)
                .Distinct()
                .ToListAsync(ct) is var apptArtistIds
                ? [.. apptArtistIds]
                : [];
            if (client.ArtistId is { } assignedId) artistIds.Add(assignedId);

            List<Domain.Entities.Artist> artists = await db.Artists
                .Where(a => artistIds.Contains(a.Id) && a.UserId != null)
                .ToListAsync(ct);
            contacts.AddRange(artists.Select(a =>
                new EligibleContact(a.UserId!.Value, "artist", $"{a.FirstName} {a.LastName}", a.AvatarUrl)));
        }
        else if (string.Equals(callerRole, "artist", StringComparison.OrdinalIgnoreCase))
        {
            Domain.Entities.Artist? artist = await db.Artists
                .FirstOrDefaultAsync(a => a.UserId == callerUserId, ct);
            if (artist is null) return contacts;

            HashSet<Guid> clientIds = [.. await db.Appointments
                .Where(a => a.ArtistId == artist.Id)
                .Select(a => a.ClientId)
                .Distinct()
                .ToListAsync(ct)];
            List<Guid> assignedClientIds = await db.Clients
                .Where(c => c.ArtistId == artist.Id)
                .Select(c => c.Id)
                .ToListAsync(ct);
            foreach (Guid id in assignedClientIds) clientIds.Add(id);

            List<Domain.Entities.Client> clients = await db.Clients
                .Where(c => clientIds.Contains(c.Id) && c.UserId != null)
                .ToListAsync(ct);
            contacts.AddRange(clients.Select(c =>
                new EligibleContact(c.UserId!.Value, "client", $"{c.FirstName} {c.LastName}", null)));
        }
        else if (string.Equals(callerRole, "owner", StringComparison.OrdinalIgnoreCase))
        {
            List<Domain.Entities.Artist> artists = await db.Artists
                .Where(a => a.IsActive && a.UserId != null)
                .ToListAsync(ct);
            contacts.AddRange(artists.Select(a =>
                new EligibleContact(a.UserId!.Value, "artist", $"{a.FirstName} {a.LastName}", a.AvatarUrl)));

            List<Domain.Entities.Client> clients = await db.Clients
                .Where(c => c.UserId != null)
                .ToListAsync(ct);
            contacts.AddRange(clients.Select(c =>
                new EligibleContact(c.UserId!.Value, "client", $"{c.FirstName} {c.LastName}", null)));
        }

        // Owner is reachable by anyone (client or artist) in the studio, unconditionally.
        if (!string.Equals(callerRole, "owner", StringComparison.OrdinalIgnoreCase))
        {
            (Guid? ownerUserId, string ownerName) = await TryResolveOwnerAsync(db, identity, studioId, ct);
            if (ownerUserId is { } id && id != callerUserId)
                contacts.Add(new EligibleContact(id, "owner", ownerName, null));
        }

        return contacts;
    }

    public static async Task<bool> IsEligibleAsync(
        IAppDbContext db, IIdentityService identity, Guid studioId, Guid callerUserId, string callerRole,
        Guid recipientUserId, CancellationToken ct)
    {
        List<EligibleContact> contacts =
            await GetContactsAsync(db, identity, studioId, callerUserId, callerRole, ct);
        return contacts.Any(c => c.UserId == recipientUserId);
    }

    public static async Task<(Guid? UserId, string DisplayName)> TryResolveOwnerAsync(
        IAppDbContext db, IIdentityService identity, Guid studioId, CancellationToken ct)
    {
        Domain.Entities.Studio? studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == studioId, ct);
        if (studio is null) return (null, "Studio Owner");

        Guid? ownerUserId = await identity.GetUserIdByEmailAsync(studio.OwnerEmail, ct);
        if (ownerUserId is null) return (null, "Studio Owner");

        string? displayName = await identity.GetUserDisplayNameAsync(studio.OwnerEmail, ct);
        return (ownerUserId, displayName ?? "Studio Owner");
    }
}
```
**Note on `Appointment.ClientId`/`ArtistId`**: confirm the exact property names on
`Appointment` before writing this (this prompt assumes `ClientId`/`ArtistId` based on the
existing "Useful Queries" example in `database.md` — verify against the live
`Appointment.cs` rather than trusting this assumption blindly, per this project's own
"never invent a fact — verify against source" rule).

### 5b. `ConversationAccessGuard.cs` (mirrors `FeedbackAccessGuard` exactly)

```csharp
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Messaging;

internal static class ConversationAccessGuard
{
    public static async Task<Conversation> LoadParticipantConversationAsync(
        IAppDbContext db, Guid conversationId, Guid userId, CancellationToken ct)
    {
        Conversation conversation = await db.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct)
            ?? throw new NotFoundException(nameof(Conversation), conversationId);

        if (!conversation.IsParticipant(userId))
            throw new ForbiddenException("You do not have access to this conversation.");

        return conversation;
    }
}
```

### 5c. `Commands/CreateConversationCommand.cs`

```csharp
public record CreateConversationCommand(CreateConversationRequest Request) : IRequest<ConversationResponse>;

public class CreateConversationHandler(
    IAppDbContext db, ICurrentUser user, ICurrentTenant tenant, IIdentityService identity)
    : IRequestHandler<CreateConversationCommand, ConversationResponse>
{
    public async Task<ConversationResponse> Handle(CreateConversationCommand command, CancellationToken ct)
    {
        Guid recipientId = command.Request.RecipientUserId;

        bool eligible = await ConversationEligibility.IsEligibleAsync(
            db, identity, tenant.StudioId, user.UserId, user.Role, recipientId, ct);
        if (!eligible) throw new ForbiddenException("You cannot start a conversation with this person.");

        Conversation? existing = await db.Conversations.FirstOrDefaultAsync(c =>
            (c.ParticipantAUserId == user.UserId && c.ParticipantBUserId == recipientId) ||
            (c.ParticipantAUserId == recipientId && c.ParticipantBUserId == user.UserId), ct);

        if (existing is not null) return await MapAsync(db, identity, existing, user.UserId, ct);

        // Resolve the recipient's role for the denormalized fields — reuse the same
        // eligibility lookup's contact list rather than a second round-trip guess.
        List<EligibleContact> contacts = await ConversationEligibility.GetContactsAsync(
            db, identity, tenant.StudioId, user.UserId, user.Role, ct);
        string recipientRole = contacts.First(c => c.UserId == recipientId).Role;

        Conversation conversation = Conversation.Create(
            tenant.StudioId, user.UserId, user.Role, recipientId, recipientRole);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(ct);

        return await MapAsync(db, identity, conversation, user.UserId, ct);
    }

    internal static async Task<ConversationResponse> MapAsync(
        IAppDbContext db, IIdentityService identity, Conversation c, Guid callerUserId, CancellationToken ct)
    {
        (Guid otherId, string otherRole) = c.OtherParticipant(callerUserId);
        (string displayName, string? avatarUrl) = await ResolveDisplayAsync(db, identity, otherId, otherRole, ct);
        int unread = await db.ChatMessages.CountAsync(m =>
            m.ConversationId == c.Id && m.SenderUserId != callerUserId && m.ReadAt == null, ct);

        return new ConversationResponse(
            c.Id, otherId, otherRole, displayName, avatarUrl,
            c.LastMessageAt, c.LastMessagePreview,
            c.LastMessageSenderUserId == callerUserId, unread, c.CreatedAt);
    }

    internal static async Task<(string DisplayName, string? AvatarUrl)> ResolveDisplayAsync(
        IAppDbContext db, IIdentityService identity, Guid userId, string role, CancellationToken ct)
    {
        if (string.Equals(role, "client", StringComparison.OrdinalIgnoreCase))
        {
            Domain.Entities.Client? c = await db.Clients.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            return c is null ? ("Client", null) : ($"{c.FirstName} {c.LastName}", null);
        }
        if (string.Equals(role, "artist", StringComparison.OrdinalIgnoreCase))
        {
            Domain.Entities.Artist? a = await db.Artists.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            return a is null ? ("Artist", null) : ($"{a.FirstName} {a.LastName}", a.AvatarUrl);
        }
        // owner
        string? name = await identity.GetUserDisplayNameAsync(await identity.GetUserEmailAsync(userId, ct) ?? "", ct);
        return (name ?? "Studio Owner", null);
    }
}
```

### 5d. `Validators/CreateConversationValidator.cs`

```csharp
public class CreateConversationValidator : AbstractValidator<CreateConversationCommand>
{
    public CreateConversationValidator(ICurrentTenant currentTenant)
    {
        RuleFor(x => x).Must(_ => currentTenant.IsSet)
            .WithName("Studio").WithMessage("You need to belong to a studio to send messages.");
        RuleFor(x => x.Request.RecipientUserId).NotEmpty();
    }
}
```

### 5e. `Commands/SendChatMessageCommand.cs`

```csharp
public record SendChatMessageCommand(Guid ConversationId, SendChatMessageRequest Request)
    : IRequest<ChatMessageResponse>;

public class SendChatMessageHandler(
    IAppDbContext db, ICurrentUser user, ICurrentTenant tenant,
    IRealtimeNotifier realtime, IJobScheduler jobs)
    : IRequestHandler<SendChatMessageCommand, ChatMessageResponse>
{
    public async Task<ChatMessageResponse> Handle(SendChatMessageCommand command, CancellationToken ct)
    {
        Conversation conversation = await ConversationAccessGuard.LoadParticipantConversationAsync(
            db, command.ConversationId, user.UserId, ct);

        // Decision 6: only the first unread message in a streak triggers the email —
        // count BEFORE inserting the new row.
        int priorUnread = await db.ChatMessages.CountAsync(m =>
            m.ConversationId == conversation.Id && m.SenderUserId != user.UserId && m.ReadAt == null, ct);

        ChatMessage message = ChatMessage.Create(
            tenant.StudioId, conversation.Id, user.UserId, user.Role, command.Request.Body);
        db.ChatMessages.Add(message);
        conversation.RecordLastMessage(user.UserId, message.Body);

        await db.SaveChangesAsync(ct);

        ChatMessageResponse response = Map(message);

        (Guid recipientId, _) = conversation.OtherParticipant(user.UserId);
        await realtime.NotifyUserAsync(recipientId, "MessageReceived", response, ct);
        await realtime.NotifyUserAsync(user.UserId, "MessageReceived", response, ct);

        if (priorUnread == 0) jobs.EnqueueNewMessageEmail(message.Id);

        return response;
    }

    internal static ChatMessageResponse Map(ChatMessage m) =>
        new(m.Id, m.ConversationId, m.SenderUserId, m.SenderRole, m.Body, m.CreatedAt, m.ReadAt);
}
```

### 5f. `Validators/SendChatMessageValidator.cs`

```csharp
public class SendChatMessageValidator : AbstractValidator<SendChatMessageCommand>
{
    public SendChatMessageValidator(ICurrentTenant currentTenant)
    {
        RuleFor(x => x).Must(_ => currentTenant.IsSet)
            .WithName("Studio").WithMessage("You need to belong to a studio to send messages.");
        RuleFor(x => x.Request.Body)
            .NotEmpty().WithMessage("Message cannot be empty.")
            .MaximumLength(2000).WithMessage("Message must be at most 2000 characters.");
    }
}
```

### 5g. `Commands/MarkConversationReadCommand.cs`

```csharp
public record MarkConversationReadCommand(Guid ConversationId) : IRequest;

public class MarkConversationReadHandler(IAppDbContext db, ICurrentUser user, IRealtimeNotifier realtime)
    : IRequestHandler<MarkConversationReadCommand>
{
    public async Task Handle(MarkConversationReadCommand command, CancellationToken ct)
    {
        Conversation conversation = await ConversationAccessGuard.LoadParticipantConversationAsync(
            db, command.ConversationId, user.UserId, ct);

        List<ChatMessage> unread = await db.ChatMessages.Where(m =>
            m.ConversationId == conversation.Id && m.SenderUserId != user.UserId && m.ReadAt == null)
            .ToListAsync(ct);
        if (unread.Count == 0) return;

        foreach (ChatMessage m in unread) m.MarkRead();
        await db.SaveChangesAsync(ct);

        (Guid otherId, _) = conversation.OtherParticipant(user.UserId);
        await realtime.NotifyUserAsync(otherId, "ConversationRead",
            new { conversation.Id, ReadByUserId = user.UserId }, ct);
    }
}
```

### 5h. `Queries/GetConversationsQuery.cs` — the inbox

```csharp
public record GetConversationsQuery : IRequest<List<ConversationResponse>>;

public class GetConversationsHandler(IAppDbContext db, ICurrentUser user, IIdentityService identity)
    : IRequestHandler<GetConversationsQuery, List<ConversationResponse>>
{
    public async Task<List<ConversationResponse>> Handle(GetConversationsQuery query, CancellationToken ct)
    {
        List<Conversation> conversations = await db.Conversations
            .Where(c => c.ParticipantAUserId == user.UserId || c.ParticipantBUserId == user.UserId)
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .ToListAsync(ct);

        List<ConversationResponse> results = [];
        foreach (Conversation c in conversations)
            results.Add(await CreateConversationHandler.MapAsync(db, identity, c, user.UserId, ct));
        return results;
    }
}
```

### 5i. `Queries/GetConversationMessagesQuery.cs` — cursor-paginated (never offset, per
`database.md`)

```csharp
public record GetConversationMessagesQuery(Guid ConversationId, Guid? Before, int Take)
    : IRequest<List<ChatMessageResponse>>;

public class GetConversationMessagesHandler(IAppDbContext db, ICurrentUser user)
    : IRequestHandler<GetConversationMessagesQuery, List<ChatMessageResponse>>
{
    public async Task<List<ChatMessageResponse>> Handle(GetConversationMessagesQuery query, CancellationToken ct)
    {
        await ConversationAccessGuard.LoadParticipantConversationAsync(db, query.ConversationId, user.UserId, ct);

        int take = Math.Clamp(query.Take <= 0 ? 30 : query.Take, 1, 100);
        IQueryable<ChatMessage> q = db.ChatMessages.Where(m => m.ConversationId == query.ConversationId);

        if (query.Before is { } beforeId)
        {
            ChatMessage? cursor = await db.ChatMessages.FirstOrDefaultAsync(m => m.Id == beforeId, ct);
            if (cursor is not null) q = q.Where(m => m.CreatedAt < cursor.CreatedAt);
        }

        List<ChatMessage> page = await q.OrderByDescending(m => m.CreatedAt).Take(take).ToListAsync(ct);
        page.Reverse(); // return oldest-first within the page, newest page fetched first
        return page.Select(SendChatMessageHandler.Map).ToList();
    }
}
```

### 5j. `Queries/GetConversationContactsQuery.cs`

```csharp
public record GetConversationContactsQuery : IRequest<List<ConversationContactResponse>>;

public class GetConversationContactsHandler(IAppDbContext db, ICurrentUser user, ICurrentTenant tenant, IIdentityService identity)
    : IRequestHandler<GetConversationContactsQuery, List<ConversationContactResponse>>
{
    public async Task<List<ConversationContactResponse>> Handle(GetConversationContactsQuery query, CancellationToken ct)
    {
        if (!tenant.IsSet) return [];

        List<EligibleContact> contacts = await ConversationEligibility.GetContactsAsync(
            db, identity, tenant.StudioId, user.UserId, user.Role, ct);

        List<ConversationContactResponse> results = [];
        foreach (EligibleContact c in contacts)
        {
            Conversation? existing = await db.Conversations.FirstOrDefaultAsync(x =>
                (x.ParticipantAUserId == user.UserId && x.ParticipantBUserId == c.UserId) ||
                (x.ParticipantAUserId == c.UserId && x.ParticipantBUserId == user.UserId), ct);
            results.Add(new ConversationContactResponse(c.UserId, c.Role, c.DisplayName, c.AvatarUrl, existing?.Id));
        }
        return results;
    }
}
```

### 5k. `Queries/GetUnreadMessageCountQuery.cs` — nav badge

```csharp
public record GetUnreadMessageCountQuery : IRequest<int>;

public class GetUnreadMessageCountHandler(IAppDbContext db, ICurrentUser user)
    : IRequestHandler<GetUnreadMessageCountQuery, int>
{
    public async Task<int> Handle(GetUnreadMessageCountQuery query, CancellationToken ct)
    {
        List<Guid> myConversationIds = await db.Conversations
            .Where(c => c.ParticipantAUserId == user.UserId || c.ParticipantBUserId == user.UserId)
            .Select(c => c.Id).ToListAsync(ct);

        return await db.ChatMessages.CountAsync(m =>
            myConversationIds.Contains(m.ConversationId) && m.SenderUserId != user.UserId && m.ReadAt == null, ct);
    }
}
```

---

## Part 6 — API endpoints

### 6a. New `Pena_e_Arte.API/Endpoints/MessagingEndpoints.cs`

```csharp
public static class MessagingEndpoints
{
    public static void MapMessagingEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/conversations")
            .RequireAuthorization("ClientAndAbove");

        group.MapGet("", GetConversations);
        group.MapGet("contacts", GetContacts);
        group.MapGet("unread-count", GetUnreadCount);
        group.MapPost("", CreateConversation);
        group.MapGet("{id:guid}/messages", GetMessages);
        group.MapPost("{id:guid}/messages", SendMessage);
        group.MapPost("{id:guid}/read", MarkRead);
    }

    private static async Task<IResult> GetConversations(ISender mediator, CancellationToken ct) =>
        Results.Ok(await mediator.Send(new GetConversationsQuery(), ct));

    private static async Task<IResult> GetContacts(ISender mediator, CancellationToken ct) =>
        Results.Ok(await mediator.Send(new GetConversationContactsQuery(), ct));

    private static async Task<IResult> GetUnreadCount(ISender mediator, CancellationToken ct) =>
        Results.Ok(await mediator.Send(new GetUnreadMessageCountQuery(), ct));

    // Decision 9: 200, not 201 — get-or-create, the caller never knows in advance which case
    // it is.
    private static async Task<IResult> CreateConversation(
        CreateConversationRequest request, ISender mediator, CancellationToken ct) =>
        Results.Ok(await mediator.Send(new CreateConversationCommand(request), ct));

    private static async Task<IResult> GetMessages(
        Guid id, ISender mediator, CancellationToken ct, Guid? before = null, int take = 30) =>
        Results.Ok(await mediator.Send(new GetConversationMessagesQuery(id, before, take), ct));

    private static async Task<IResult> SendMessage(
        Guid id, SendChatMessageRequest request, ISender mediator, CancellationToken ct) =>
        Results.Created($"/api/v1/conversations/{id}/messages",
            await mediator.Send(new SendChatMessageCommand(id, request), ct));

    private static async Task<IResult> MarkRead(Guid id, ISender mediator, CancellationToken ct)
    {
        await mediator.Send(new MarkConversationReadCommand(id), ct);
        return Results.NoContent();
    }
}
```

### 6b. `Program.cs` — register the group

Add `app.MapMessagingEndpoints();` alongside the other `Map*Endpoints()` calls.

---

## Part 7 — Backend tests

Follow the `MethodName_Scenario_ExpectedResult` naming convention
(`docs/claude/conventions.md`) and the Arrange/Act/Assert blank-line separation. At
minimum:

- `ConversationTests` (unit): `Create_NormalizesParticipantOrder_RegardlessOfInputOrder`,
  `IsParticipant_ReturnsFalseForNonParticipant`, `RecordLastMessage_TruncatesLongBody`.
- `ChatMessageTests` (unit): `MarkRead_IsIdempotent_SecondCallDoesNotChangeReadAt`.
- `ConversationEligibilityTests` (unit, mocked `IAppDbContext`/`IIdentityService`): client
  can reach their assigned artist; client can reach an artist they've only ever had an
  appointment with (not the assigned one); client cannot reach an unrelated artist; client
  can always reach the resolved owner; owner can reach any artist/client; a
  `UserId == null` client/artist row never appears in results.
- `CreateConversationHandlerTests` (integration): creating a conversation between eligible
  parties succeeds and is idempotent (second call returns the same `Id`); creating one
  between ineligible parties throws `ForbiddenException`.
- `SendChatMessageHandlerTests` (integration): a message from a non-participant throws
  `ForbiddenException`; `Conversation.LastMessageAt`/`LastMessagePreview` update after
  send; `IJobScheduler.EnqueueNewMessageEmail` is called exactly once for the first unread
  message in a streak and **not called again** for a second message sent before the first
  is read (use NSubstitute to assert call count, matching this codebase's existing
  "every external service is NSubstitute-mocked at the handler level" convention from
  `ci.yml`).
- `MarkConversationReadHandlerTests` (integration): marks only the other participant's
  unread messages, not the caller's own sent messages.
- `GetConversationMessagesHandlerTests` (integration): cursor pagination returns pages in
  the right order and respects the `Take` clamp (1–100).
- Endpoint-level: confirm `ClientAndAbove` is enforced (an unauthenticated request 401s)
  and that a `ForbiddenException`/`NotFoundException` from a handler maps to 403/404 via
  `ExceptionMiddleware` — do not write a new exception-mapping test if
  `ExceptionMiddlewareTests` already covers these two exception types generically (check
  before adding a duplicate).

Run `dotnet test` and confirm the full suite is green, reporting before/after test counts
the same way every prior overnight prompt's "Verification" section does.

---

## Part 8 — Frontend

### 8a. New `frontend/src/features/messaging/` folder

```
features/messaging/
├── messaging.types.ts       ConversationResponse, ChatMessageResponse, ConversationContactResponse
├── messagingApi.ts          RTK Query slice (reducerPath: "messagingApi")
├── index.ts                 public exports only
└── components/
    ├── MessagesInboxPage.tsx      route: /messages — conversation list, click to open thread
    ├── ConversationThread.tsx     reusable: message bubbles + composer (mirrors SupportTicketThread's shape)
    ├── NewConversationDialog.tsx  contact picker (calls getContacts) → creates/opens a conversation
    ├── MessagesNavBadge.tsx       unread-count badge, mirrors NotificationBell's placement pattern
    └── useChatHub.ts              SignalR hook — see 8c
```

### 8b. `messagingApi.ts`

```typescript
export const messagingApi = createApi({
  reducerPath: "messagingApi",
  baseQuery: fetchBaseQuery({
    baseUrl: "/api/v1/",
    prepareHeaders: (headers, { getState }) => {
      const { token, tenantId } = (getState() as RootState).auth;
      if (token)    headers.set("Authorization", `Bearer ${token}`);
      if (tenantId) headers.set("X-Tenant-Id", tenantId);
      return headers;
    },
  }),
  tagTypes: ["Conversation", "Messages", "UnreadCount"],
  endpoints: (builder) => ({
    getConversations: builder.query<ConversationResponse[], void>({
      query: () => "conversations",
      providesTags: ["Conversation"],
    }),
    getContacts: builder.query<ConversationContactResponse[], void>({
      query: () => "conversations/contacts",
    }),
    getUnreadCount: builder.query<number, void>({
      query: () => "conversations/unread-count",
      providesTags: ["UnreadCount"],
    }),
    createConversation: builder.mutation<ConversationResponse, { recipientUserId: string }>({
      query: (body) => ({ url: "conversations", method: "POST", body }),
      invalidatesTags: ["Conversation"],
    }),
    getMessages: builder.query<ChatMessageResponse[], { conversationId: string; before?: string }>({
      query: ({ conversationId, before }) =>
        `conversations/${conversationId}/messages${before ? `?before=${before}` : ""}`,
      providesTags: (_r, _e, arg) => [{ type: "Messages", id: arg.conversationId }],
    }),
    sendMessage: builder.mutation<ChatMessageResponse, { conversationId: string; body: string }>({
      query: ({ conversationId, body }) => ({
        url: `conversations/${conversationId}/messages`, method: "POST", body: { body },
      }),
      invalidatesTags: (_r, _e, arg) => [{ type: "Messages", id: arg.conversationId }, "Conversation", "UnreadCount"],
    }),
    markConversationRead: builder.mutation<void, string>({
      query: (conversationId) => ({ url: `conversations/${conversationId}/read`, method: "POST" }),
      invalidatesTags: ["Conversation", "UnreadCount"],
    }),
  }),
});
```

### 8c. `useChatHub.ts` — built with `useSupportHub`'s two documented bugs fixed from the
start (see Context above), not discovered later in a review pass

```typescript
export function useChatHub() {
  const token = useAppSelector((s) => s.auth.token);
  const currentUserId = useAppSelector((s) => s.auth.user?.id);
  const dispatch = useAppDispatch();

  useEffect(() => {
    if (!token) return;
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/chat", { accessTokenFactory: () => token })
      .withAutomaticReconnect()
      .build();

    const handleMessage = (message: ChatMessageResponse) => {
      // Fix mirrored from useSupportHub's documented bug: skip invalidation when this is
      // the echo of the current user's own just-sent message — the mutation's own
      // invalidatesTags already refetched it once.
      if (message.senderUserId === currentUserId) return;
      dispatch(messagingApi.util.invalidateTags([
        { type: "Messages", id: message.conversationId }, "Conversation", "UnreadCount",
      ]));
    };
    const handleRead = () => dispatch(messagingApi.util.invalidateTags(["Conversation"]));

    connection.on("MessageReceived", handleMessage);
    connection.on("ConversationRead", handleRead);
    // Fix mirrored from useSupportHub's documented bug: ChatHub's group membership is
    // per-connection (auto-joined in OnConnectedAsync), so a fresh connection after a
    // reconnect needs no explicit re-join call — unlike SupportHub, there is no
    // JoinTicket to re-invoke. The subscription itself, however, must be re-armed if the
    // connection object were ever recreated; using a single long-lived connection with
    // withAutomaticReconnect() avoids that entirely, which is why this hook does not
    // need an onreconnected handler the way useSupportHub does — confirm this reasoning
    // holds before removing it: if a full new HubConnection is ever constructed (not just
    // reconnected), auto-join happens again automatically in OnConnectedAsync regardless.

    connection.start().catch(() => {});
    return () => { connection.stop(); };
  }, [token, currentUserId, dispatch]);
}
```
Mount this hook once, high in the tree, for every authenticated client/artist/owner
session (the same place `NotificationBell`'s own SignalR hook is mounted — locate it and
follow the identical mounting pattern, do not invent a second convention).

### 8d. `MessagesInboxPage.tsx`, `ConversationThread.tsx`, `NewConversationDialog.tsx`

Standard `shadcn/ui` composition — reuse existing primitives (`Dialog`, `Avatar`, `Input`,
`Button`, `ScrollArea` if already present, `Badge` for unread counts) rather than building
new ones. `ConversationThread` shows message bubbles right-aligned for the current user's
own messages, left-aligned for the other participant's, with the other participant's
display name/avatar in the header and a read-receipt tick (rendered when
`lastMessageFromMe && conversation` has been marked read by the other side — thread the
`ReadAt` through the same way `SupportTicketThread` already threads message state).
`MessagesInboxPage` calls `markConversationRead` when a thread is opened (on mount of the
selected thread), not on every keystroke or poll.

### 8e. `frontend/src/features/appointments/components/AppointmentDetailPage.tsx`

Add a "Message [Artist name]" button (client-side view) / "Message [Client name]" button
(artist/owner-side view) that calls `createConversation` with the other party's resolved
`userId` (available from the appointment's existing artist/client projection — check
`AppointmentResponse`'s shape for whatever field already carries the artist's/client's
`UserId`; if it doesn't currently project one, that's a backend gap this Part must also
close — add it to `AppointmentResponse`/the query projection rather than making a second
round-trip) and navigates to `/messages?conversation={id}`. This is the "thread view on
AppointmentDetailPage.tsx" the parity report called for — it deep-links into the same
inbox/thread UI, it does not duplicate `ConversationThread` inline on this page.

### 8f. Layouts, nav, router

- `frontend/src/layouts/ClientLayout.tsx`, `ArtistLayout.tsx`, `OwnerLayout.tsx`: add a
  "Messages" `NavItem` (mirroring the existing `NAV_ITEMS` array shape shown in
  `ClientLayout.tsx`) with a `tourId`, and render `MessagesNavBadge` next to
  `NotificationBell` in each layout's header, following the exact same
  import-and-render-per-layout pattern `NotificationBell` already uses — **not**
  `IssuerLayout.tsx`, per Decision 1.
- `frontend/src/app/router.tsx`: add
  ```tsx
  {
    path: "messages",
    element: <RoleGuard allowedRoles={[Role.Client, Role.Artist, Role.Owner]} />,
    children: [{ index: true, element: <ErrorBoundary><MessagesInboxPage /></ErrorBoundary> }],
  },
  ```
  as a sibling of the existing `appointments`/`clients`/`designs` route groups.
- `frontend/src/app/store.ts`: add `[messagingApi.reducerPath]: messagingApi.reducer` to
  the reducer map and `.concat(messagingApi.middleware)` to the middleware chain, matching
  every other `*Api` slice already registered there.

### 8g. `data-tour` attributes

Add `data-tour="client-messages-nav"` / `"artist-messages-nav"` / `"owner-messages-nav"` to
the new nav items — required by Part 10's onboarding-tour update below (the "Adding a New
Feature" checklist in `architecture.md` explicitly calls out: update the tour "if the
feature touches a nav item... or any existing `data-tour=` target," and this feature adds
three new nav items).

---

## Part 9 — Frontend tests

Mirror the existing `describe`/`it` convention. Minimum coverage:
- `messagingApi` — mock server responses, confirm tag invalidation on
  send/create/markRead.
- `useChatHub` — both fixed-from-the-start behaviors from 8c must have their own test:
  a `MessageReceived` event authored by the current user does not trigger
  `invalidateTags`; one authored by someone else does.
- `ConversationThread` — renders bubbles on the correct side by sender; composer disables
  submit on empty/whitespace-only body; enforces the 2000-char limit client-side (mirrors
  the server-side `SendChatMessageValidator` limit — do not let the UI silently accept
  what the API will reject).
- `NewConversationDialog` — shows contacts from `getContacts`; selecting an existing
  contact with `existingConversationId` set navigates directly to that thread instead of
  calling `createConversation` again (avoid the network round-trip when the answer is
  already known client-side).
- `MessagesInboxPage` — empty state when there are no conversations yet (every role).
- Layout tests (`ClientLayout.test.tsx`, `ArtistLayout.test.tsx`, `OwnerLayout.test.tsx`)
  — extend the existing suites to assert the new nav item renders; do not create parallel
  duplicate test files.

---

## Part 10 — Help sync (CLAUDE.md rule 7 — not optional)

This feature adds a user-visible surface for **three** roles — Help sync is not a single
edit, it's three, and it is not "done" until all three exist:

### 10a. `frontend/src/features/help/helpContent.ts`

Add one `HelpArticle` per role (Client, Artist, Owner — matching the existing per-role
sectioning already visible in the file), each following the exact shape already used
(`id`, `roles`, `title`, `route: "/messages"`, `keywords`, `summary`, `steps`, `tips`,
`relatedArticleIds`). Suggested content, adjust to match the actual shipped UI copy once
built:
- **Client**: "Message your artist or the studio" — steps: open Messages from the nav,
  either continue an existing thread or start a new one (only your assigned/booked artist
  and the studio are available to message), type and send.
- **Artist**: "Message a client or the studio owner" — steps: open Messages, pick a
  client you have an appointment with (or one assigned to you) or the owner, reply in
  real time.
- **Owner**: "Message any artist or client" — steps: open Messages, start a new
  conversation with any artist or client at your studio.

Add `relatedArticleIds` cross-links from the existing appointment-related articles (e.g.
`client-book-appointment`) to the new messaging article(s), and vice versa, so search
surfaces them together — check what `relatedArticleIds` values already exist nearby before
inventing new ones.

### 10b. `frontend/public/user-manual/index.html`

Add a matching section (same content, same three-role split) in whatever place the
standalone manual currently organizes per-role feature sections — follow its existing
heading/anchor structure exactly (do not introduce a new heading level or section pattern
just for this feature).

### 10c. Onboarding tours

`frontend/src/features/help/tours/clientTour.ts`, `artistTour.ts`, `ownerTour.ts`: add a
step targeting each new `data-tour="..."-messages-nav"` attribute from Part 8g, following
the existing step shape/ordering convention in each file (insert it near the other
primary-nav steps, not appended at the end out of context).

**Do not skip this Part or defer it to "a follow-up."** `CLAUDE.md` rule 7 and the
"Adding a New Feature" checklist in `architecture.md` both call this out as a
same-change requirement, and `conventions.md`'s "What to Never Do" repeats it a third
time. All three are unambiguous: this feature is not done without 10a–10c.

---

## Part 11 — Architecture doc updates (`docs/claude/architecture.md`)

### 11a. Feature Module Map — new row \#38

```
| 38 | In-App Messaging | `Conversation`, `ChatMessage` (both TenantEntity, DesignRevision-shaped) | `ChatHub` SignalR hub (per-user `user:{id}` groups, no join-by-id), `IRealtimeNotifier.NotifyUserAsync`, `IJobScheduler.EnqueueNewMessageEmail` (Hangfire, debounced) | Per-tenant (client↔artist, client↔owner, artist↔owner only — no issuer) |
```

### 11b. SignalR Event Naming Convention table

Add the two rows from Part 2e.

### 11c. Decisions Log

Add a dated entry ("In-App Messaging — 2026-08-26") in the same narrative style as the
"Manual Client Reminders (2026-08-21)" entry — what shipped, the key non-obvious
decisions (Decisions 3, 4, 5, 6, 9 above are the ones worth restating here since they
diverge from the closest precedent, `SupportHub`/`FeedbackReport`), what's flagged as
explicitly out of scope, and verification performed (test counts before/after, same
format as every prior entry).

### 11d. `IgnoreQueryFilters() Approved Usages` table

**No entry needed** — per Decision 2, this feature adds zero `IgnoreQueryFilters()` calls.
Do not add a 42nd row; if you find yourself wanting to, that means you've deviated from
Decision 2 and should stop and reconsider rather than proceeding.

---

## Out of Scope — flagged explicitly, not silently dropped

- **Attachments/image sharing in messages** (Decision 7). `FeedbackReport`'s R2 presign
  flow is the proven pattern to reuse when this is picked up.
- **Message editing or deletion** (Decision 8).
- **Client↔client or artist↔artist messaging.** Not asked for; would be a materially
  different (social, not provider-relationship) feature.
- **Issuer participation/visibility into conversations.** Issuer has `FeedbackReport`/
  `SupportHub` for platform support already; this feature does not extend issuer's
  cross-tenant read surface. If a future moderation/trust-and-safety need arises (e.g. an
  owner reporting client harassment via chat, echoing the existing `ConductReport`
  feature's shape), that is a new, separate feature to spec later, not a silent addition
  here.
- **Typing indicators / online presence.** `TrafficHub` is the only existing precedent for
  live presence tracking in this codebase and it's issuer-analytics-scoped, not a general
  presence system — building one for chat is a real feature in its own right, not a small
  addition, and the user's request (basic messaging) doesn't call for it.
- **Push notifications (mobile).** B19 (mobile app/PWA) is itself still MISSING per the
  parity report — there is no push channel to route a "new message" notification through
  yet. Email is the correct fallback today (Decision 5); revisit once B19 ships.
- **SMS notification for new messages.** Deliberately excluded per Decision 5 (cost).

---

## Build checklist

Run after **every** Part, not just at the end — catching a break immediately after the
Part that caused it is far cheaper than debugging it after Part 11:

```bash
# 1. Backend build (new entities/migration/hub/handlers/endpoints)
dotnet build

# 2. Backend tests
dotnet test

# 3. Frontend type check
cd frontend && pnpm tsc --noEmit

# 4. Lint
pnpm lint

# 5. All frontend tests must pass (including every new messaging/useChatHub/layout test)
pnpm test

# 6. Frontend build
pnpm build
cd ..
```
Report exact before/after test counts for both suites (backend `dotnet test`, frontend
`pnpm test`) in your final summary, the same way every prior overnight prompt's
verification section does — a bare "tests pass" is not sufficient evidence.

---

## Summary of Changes

### New features:
- In-app messaging between client↔artist, client↔owner, and artist↔owner within a studio.
- Real-time delivery via a new `ChatHub` (per-user group model — see Decision 3).
- Debounced email fallback notification when the recipient is offline (Decisions 5–6).
- New `/messages` inbox route + contextual "Message [person]" entry point on
  `AppointmentDetailPage.tsx`.

### New backend surface:
- `Conversation`, `ChatMessage` entities + migration.
- `ChatHub`; `IRealtimeNotifier.NotifyUserAsync`; `IJobScheduler.EnqueueNewMessageEmail`;
  `ChatNotificationJob`; `NotificationType.MessageReceived`.
- `Pena_e_Arte.Application/Messaging/` (commands, queries, validators,
  `ConversationEligibility`, `ConversationAccessGuard`).
- `GET/POST /api/v1/conversations`, `GET /api/v1/conversations/contacts`,
  `GET /api/v1/conversations/unread-count`, `GET/POST /api/v1/conversations/{id}/messages`,
  `POST /api/v1/conversations/{id}/read`.

### New frontend surface:
- `frontend/src/features/messaging/` (full feature slice).
- Nav entries + unread badges in `ClientLayout`, `ArtistLayout`, `OwnerLayout` (not
  `IssuerLayout`).
- `AppointmentDetailPage.tsx` "Message" entry point.

### Explicitly out of scope (see "Out of Scope" section above):
Attachments, edit/delete, same-role messaging, issuer participation, typing/presence, push
notifications, SMS notifications.

### Help sync:
`helpContent.ts` (3 new articles), standalone manual (3 new sections), `clientTour.ts` /
`artistTour.ts` / `ownerTour.ts` (1 new step each) — all in Part 10, all mandatory.

---

## Hard Rules Reminder

- Tenant isolation: both new entities carry `StudioId` and a query filter. No
  `IgnoreQueryFilters()` anywhere in this feature (Decision 2).
- RBAC: every endpoint is `ClientAndAbove`, with the studio-membership check pushed into
  each write validator (`ICurrentTenant.IsSet`) — issuer is admitted by the policy but
  excluded in practice (Decision 1).
- No PII in logs — do not log message bodies, display names, or emails in any
  `Log.Information`/`Log.Warning` call this feature adds; log only ids
  (`conversationId`, `chatMessageId`, `recipientUserId`), matching the existing
  `tenant_id`/`user_id`/`request_id` convention.
- Every write command has a FluentValidation validator (`CreateConversationValidator`,
  `SendChatMessageValidator`).
- Every business-logic path has a test (Part 7/9).
- No `var` for non-obvious C# types; no TypeScript `any`.
- Help Menu, standalone manual, and onboarding tours updated in this same change
  (Part 10) — not optional, not a follow-up.
- Benchmark this feature's UX against the Industry-Standard Benchmark Set
  (`architecture.md`) before calling it done — Vagaro/Fresha/Boulevard/GlossGenius-tier
  provider-client messaging is the bar; if anything shipped here falls short of it, flag
  the gap explicitly in your final summary rather than shipping it silently (CLAUDE.md
  rule 6).
