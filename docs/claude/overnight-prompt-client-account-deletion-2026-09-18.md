# Overnight Prompt — Client Account Deletion: Fix the Cross-Studio Erasure Bug, Add Studio-Level Archive, Self-Service Export, and a Support-Mediated Undo

> Feed this file directly to Claude Code as the task prompt. It is self-contained: exact
> files, exact current code, exact target code, exact tests, exact docs to sync. Read the
> whole file before writing anything — later phases depend on the helper introduced in
> Phase A, and the scope boundary in §4 is not optional.

**Date logged:** 2026-09-18
**Requested by:** Phi
**Origin:** Engineering-consultation review of "delete a client's account." That phrase turned
out to name three different actions with three different trust boundaries — a studio removing
someone from their active client list, a client's own GDPR/data-subject erasure request, and a
platform-level suspension for abuse or fraud — and reading the live source (not just
`architecture.md`'s summary of it) surfaced a real, shipped correctness bug in the second one,
plus a stale Decisions Log entry and three benchmark gaps worth closing in the same pass. Full
research trail: this session's own consultation transcript (not itself a repo file — the
findings below are restated here with their exact source citations so this prompt stands alone).
**Mode: fully autonomous, no user present.**

**Before writing any code, run:**

```bash
git status
git add -A && git commit -m "WIP: checkpoint before client-account-deletion-hardening" --allow-empty
git checkout -b feature/client-account-deletion-hardening
```

If `git status` shows no uncommitted changes, the `git commit --allow-empty` still runs (it's a
no-op checkpoint marker) — do not skip the branch creation.

---

## 1. Goal

Ship four bounded, implementation-ready changes to the existing client-erasure feature
(`Pena_e_Arte.Application/Clients/Commands/RequestDataErasureCommand.cs`, shipped 2026-07-31 as
EPIC-0001 PENA-104, extended since with a client-self-service command and frontend that
`architecture.md`'s Decisions Log has not caught up to — see §2.1):

- **Phase A (bug fix, do first — everything else is independent of this but should ship
  together):** "Delete my account" and owner-initiated "Erase client data" currently touch only
  the `Client` row for the studio that happens to be the caller's active tenant, while disabling
  the shared Identity login *globally*. A client who belongs to more than one studio ends up
  locked out everywhere while their PII survives, un-anonymized, forever, in every studio but
  the one they happened to be active in. Fix the fan-out so "erase" and its symmetric "cancel"
  (Phase D) act on every studio relationship at once, matching what the confirmation UI and Help
  copy already promise.
- **Phase B (new, benchmark gap):** Add a lightweight, non-destructive "remove client from list"
  action — the ordinary Fresha/Vagaro-style "delete client" that just declutters the active
  roster, keeps every appointment/payment/consent/review record fully intact and readable, and
  is reversible any time. This does not exist today; erasure is currently the *only* delete-like
  action available to an owner, which conflates "I don't want to see this person in my list
  anymore" with "destroy this person's data," and is a real gap against the benchmark set.
- **Phase C (new, benchmark gap):** A client-initiated "export my data" download, offered
  alongside "Delete my account," consistent with data-portability expectations and with the
  general "offer an export before deletion completes" practice.
- **Phase D (new, closes a real gap in the existing flow):** A support-mediated way to cancel a
  pending erasure request during the 30-day grace window. Today there is no way back once
  erasure is requested — the client's login is disabled immediately, so they cannot even
  self-service an undo — which is a harder edge than the benchmark set's own precedent (Mindbody
  reactivates a merely-inactive client automatically on login). Build the owner-facing cancel,
  not a self-service one — see §3.4 for why self-service is explicitly out of scope tonight.

Also correct the stale Decisions Log line and add the new dated entries (§13), and produce the
full **implementation-ready but NOT built** specification for the third fork — platform-level
suspension of a client for abuse/fraud — as a "do not build blind" backlog item (§10). Building
that tonight would mean guessing at product/legal decisions this prompt is not authorized to
make.

Applicable non-negotiable rules from `CLAUDE.md`: #1 (tenant isolation — the new cross-tenant
lookups in Phase A/D are the one place in this prompt where `IgnoreQueryFilters()` is
deliberately used, and must be justified exactly like every other approved usage), #2 (RBAC on
every new endpoint), #3 (never log PII — see §5.4 on the new audit metadata), #6 (industry
benchmark — see §14), #7 (Help sync — see §12).

---

## 2. Decisions already made — implement as specified, do not re-litigate

### 2.1 The Decisions Log entry you are about to read is stale — do not trust it, fix it

`docs/claude/architecture.md`'s Decisions Log, the "Two-stage retention purge + R2 delete +
audited erasure (EPIC-0001 PENA-104) — 2026-07-31" row, ends with: *"No client-facing
self-service erasure UI yet (open question §3.8)."* That is no longer true. Verified against the
live source tonight: `RequestMyDataErasureCommand`/`RequestMyDataErasureHandler` (same file as
`RequestDataErasureCommand`), the `POST /api/v1/clients/me/erase-data` endpoint
(`ClientEndpoints.cs`), the frontend `DeleteAccountSection.tsx` (mounted in `MyProfilePage.tsx`'s
Sharing tab), its test file, and the matching Help Menu article `client-delete-account` all
exist and work. This was built in a session after 2026-07-31 that never updated this Decisions
Log row. Fix the stale sentence as part of this prompt's own doc sync (§13.2) — do not leave it
implying the self-service UI is still missing.

### 2.2 Client identity model — confirmed, not assumed

`Client` is a `TenantEntity` (one row per studio) and one person (one Identity `UserId`) can
hold a `Client` row at more than one studio simultaneously — this is not a hypothetical, it's an
existing, working feature ("My Studios," `SwitchStudioCommand`, `AcceptStudioJoinInviteCommand`).
`ClientConfiguration.cs` documents this itself:

```csharp
// Supports cross-studio membership lookups by UserId (multi-studio client
// support — see ClientAccountExtensions.FindClientForUserAtStudioAsync).
builder.HasIndex(c => c.UserId)
       .HasDatabaseName("ix_clients_user_id");
```

and `docs/claude/architecture.md`'s `IgnoreQueryFilters()` Approved Usages table already has a
row for exactly this cross-tenant lookup pattern:

```
| 29 | `ClientAccountExtensions` (`FindClientForUserAtStudioAsync`, `FindAnyClientRecordForUserAsync`) | Cross-tenant `Client` lookup by `UserId` — supports linking a client's account across the multiple studios they belong to | Authenticated (any role, called from login/registration/multi-studio flows) |
```

Phase A and Phase D's cross-tenant lookups are the **same approved class of usage** as row #29 —
extend that row's description to mention the new method (§13.3), do not add a new numbered row.
This mirrors the precedent already set in this codebase for `ChatNotificationJob` extending row
#36 rather than creating a new one for "the same already-approved class of usage."

### 2.3 Why the new archive flag is not `DeletedAt`

`TenantEntity.DeletedAt` already means something specific and load-bearing: every entity's global
EF Core query filter is `... && DeletedAt == null`, and **EF Core applies a related entity's own
global query filter to it even when it's reached via a navigation `Include()` from an unfiltered
parent.** Reusing `DeletedAt` for "remove from active list" would silently make a removed
client's `Client` navigation resolve to `null` on every past `Appointment`/`Payment`/
`ConsentForm` that references them — exactly the outcome Fresha's own benchmark behavior
explicitly avoids ("past sales, appointments, and reviews remain in the studio's records"). Use
a new, separate field, `Client.ArchivedAt` (nullable `DateTime`), that **does not participate in
any EF Core query filter at all** and is applied only as an explicit, opt-out `Where` in
`GetClientsQuery` (§6.1). A client detail page, an appointment's client link, a payment record,
and consent-form lookups must all keep working normally for an archived client — only the
default *list* view hides them.

### 2.4 Naming conventions to follow

- Commands: `ArchiveClientCommand` / `RestoreClientCommand` — mirrors the existing
  `Deactivate`/`ReactivateReferralCodeCommand` pair exactly (same "reversible, audited,
  owner-initiated toggle" shape).
- Audit actions (new constants in `AuditActions.cs`, alongside the existing
  `ClientDataErasureRequested`): `ClientArchived = "Client.Archived"`,
  `ClientRestored = "Client.Restored"`, `ClientDataErasureCancelled =
  "Client.DataErasureCancelled"`. Do **not** invent a new action name for the Phase A fix itself —
  it's still the same `RequestDataErasureCommand`/`RequestMyDataErasureCommand` doing the same
  logical action, just correctly scoped now; keep `AuditActions.ClientDataErasureRequested`.

---

## 3. Decisions to flag, not decide — do not build blind

### 3.1 Fork #3 (platform-level abuse/fraud suspension) is spec-only tonight

See §10 for the full implementation-ready spec. It is **not built** in this pass. It requires
product decisions this prompt is not authorized to make: does a platform ban lock the shared
Identity login everywhere (affecting studios that never had a problem with this person), or only
flag the client record at the reported studio; is there an appeal path; how does it relate to the
existing `ConductReport` resolve-workflow (`ConductReport.Status`/`ResolvedAt`) which today ends
in a status change and nothing else — no account action is ever actually taken as a result of a
resolved report. Confirmed by reading `ConductReport.cs` in full: `UpdateStatus` only ever
changes `Status`/`ResolutionNote`/`ResolvedAt`. There is no `BanClient`/`SuspendClient`/
`AccountBanned` concept anywhere in `Pena_e_Arte.Application`, `Pena_e_Arte.Domain`, or
`Pena_e_Arte.API/Endpoints` (grepped case-insensitively tonight, zero hits) — this is a real,
confirmed gap, not a naming mismatch.

### 3.2 Jurisdiction-specific consent/health-record retention floor — not needed yet, don't build it

General research on this topic (US tattoo-consent-record laws by state, ranging roughly 1–7
years, longer for minors) does not apply to this codebase as it stands: `RegisterStudioValidator`
requires `Nipt` (`RuleFor(x => x.Request.Nipt).NotEmpty()...`), and NIPT is an Albanian business
tax ID — every studio that can currently register on this platform is Albania-based. There is no
`Studio.Country`/jurisdiction field anywhere, and none is needed today. The existing flat
`RetentionOptions` (`ConsentForms`/`BodyMaps` = 2555 days / 7 years, `GracePeriodBeforeHardPurge`
= 30 days) are founder-confirmed (2026-08-01) as the deliberate single-jurisdiction default — do
not build a per-state or per-country retention table tonight. **Flag, do not decide:** whether
Albania's own law sets a specific minimum retention period for tattoo/body-art consent or health
records distinct from the founder's assumed 7-year figure has not been verified against a
primary Albanian legal source by this consultation pass (only the founder's own confirmation is
on record) — that is a legal-verification task, not an engineering one, and is out of scope here.
If/when the platform ever supports non-Albanian studios, a per-jurisdiction retention-floor table
keyed off a new `Studio.Country`/jurisdiction field becomes necessary — log this as a Feature
Module Map backlog note (§13.4), do not build it speculatively now.

### 3.3 Should "export my data" and the erasure fix both fan out cross-tenant? Yes — decided, not flagged

For internal consistency, both are built as the same shape in this prompt: the whole reason
Phase A exists is that "my account" secretly meant "my account at whichever studio happens to be
active" — shipping Phase C's export with that identical, already-identified flaw would be
building the same bug twice in one night. Phase C's export therefore also uses the Phase A/D
cross-tenant helper (§5.1) and returns one bundle grouped by studio. This is a decision, not an
open question — implement it this way.

### 3.4 Self-service cancellation of a pending erasure request is explicitly out of scope

Once erasure is requested, login is disabled immediately (`IIdentityService.DisableLoginAsync`)
— a client literally cannot log back in to cancel their own request during the grace window, by
design (§ClientDataErasure.ExecuteAsync's own doc comment: "must not keep signing in during the
grace window"). Phase D therefore builds an **owner-facing** cancel only (a client calls/emails
support, the owner or admin actions it, mirroring how `EraseClientDataSection` already exists for
the symmetric "client asked by phone" case). A genuinely self-service undo would require a
different design entirely — e.g., a time-delayed lockout with an "are you sure" grace window
*before* the login is disabled, rather than after — which is a UX decision for a future prompt,
not a silent scope-add to this one. Log it as a Feature Module Map backlog note (§13.4); do not
build it tonight.

---

## 4. Scope boundary — do not touch

- `Pena_e_Arte.Infrastructure/Jobs/RetentionPurgeJob.cs` — its four passes are correct as-is and
  need no changes. Once Phase A correctly marks every one of a client's `Client` rows with
  `ErasureRequestedAt`, the job's existing cross-tenant `IgnoreQueryFilters()` sweep in
  `AnonymizeErasedClientsAsync` already finds and anonymizes all of them without modification.
- `Pena_e_Arte.Infrastructure/Services/RetentionOptions.cs` and the `App:RetentionDays` config —
  no new fields, no jurisdiction table (§3.2).
- `IIdentityService.DisableLoginAsync`/`DeleteUserAsync` — reused as-is. Phase D needs one new
  method, `EnableLoginAsync` (§8.2) — everything else on this interface is untouched.
- `ConductReport.cs` and its full workflow (`FileArtistConductReportCommand`,
  `FileStudioConductReportCommand`, `GetConductReportsHandler`, `UpdateConductReportStatusCommand`
  if it exists under a different name — confirm before assuming) — Phase 10's spec references it
  but this prompt does not modify it.
- Anything Flow A/Flow B payment-provider related (`IPaymentProvider`, `IStripeBillingService`,
  POK). Unrelated to this change.
- `TenantMiddleware.cs`, `AuthorizationExtensions.cs` — no new policies needed; every new endpoint
  in this prompt reuses `OwnerOnly`/`ClientAndAbove`/`ArtistAndAbove` exactly as already defined.

---

## 5. Phase A — Fix the cross-studio erasure fan-out (do this first)

### 5.1 New helper: `ClientAccountExtensions.FindAllClientRecordsForUserAsync`

**File:** `Pena_e_Arte.Application/Common/ClientAccountExtensions.cs`

Current file ends with `FindAnyClientRecordForUserAsync` (singular, oldest record only — used
for seeding a new membership's name/email/phone, per its own doc comment). Add a sibling that
returns **every** live `Client` row for a `UserId`, across every studio, same
`IgnoreQueryFilters()` justification as the existing two methods in this file (approved usage
#29 — see §13.3):

```csharp
/// <summary>
/// Approved exception #5 (see docs/claude/database.md "Tenant Isolation Rules") — same class
/// of usage as FindClientForUserAtStudioAsync/FindAnyClientRecordForUserAsync above. Finds
/// EVERY live Client record for a user across every studio they belong to. Used by right-to-
/// erasure (fan out the erasure/cancel/export action to every studio relationship, not just
/// the caller's active tenant) — never used to read or copy medical data between studios.
/// </summary>
public static Task<List<Client>> FindAllClientRecordsForUserAsync(
    this IAppDbContext db, Guid userId, CancellationToken ct) =>
    db.Clients.IgnoreQueryFilters()
        .Where(c => c.UserId == userId && c.DeletedAt == null)
        .ToListAsync(ct);
```

### 5.2 Current code (verified against live source tonight)

**File:** `Pena_e_Arte.Application/Clients/Commands/RequestDataErasureCommand.cs`

```csharp
internal static class ClientDataErasure
{
    public static async Task ExecuteAsync(
        IAppDbContext db, IIdentityService identity, Client client, CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;

        List<ConsentForm> forms = await db.ConsentForms
            .Where(f => f.ClientId == client.Id && f.DeletedAt == null)
            .ToListAsync(ct);
        foreach (ConsentForm form in forms)
            form.DeletedAt = now;

        ClientProfile? profile = await db.ClientProfiles
            .FirstOrDefaultAsync(p => p.ClientId == client.Id, ct);
        if (profile is not null)
            profile.DeletedAt = now;

        client.ErasureRequestedAt = now;

        await db.SaveChangesAsync(ct);

        if (client.UserId is Guid userId)
            await identity.DisableLoginAsync(userId, ct);
    }
}
```

This only ever touches the single `Client` instance passed in — the one resolved from the
caller's active tenant — while `DisableLoginAsync` acts on the shared, tenant-less Identity
login. That mismatch is the bug.

### 5.3 Target code

Replace `ClientDataErasure.ExecuteAsync` with a version that marks **every** studio's `Client`
row for the same `UserId`, then disables login once:

```csharp
internal static class ClientDataErasure
{
    /// <summary>
    /// Shared right-to-erasure logic (GDPR Art. 17). A person's "account" is not scoped to one
    /// studio — Client is per-tenant but Identity login is shared across every studio they
    /// belong to (see docs/claude/architecture.md's Decisions Log, "Client identity model",
    /// 2026-09-18) — so erasure must act on every Client row for this UserId, not just the one
    /// the caller happened to resolve from their active tenant. Immediately: soft-deletes the
    /// consent forms and profile for EVERY studio Client row sharing this UserId, marks each row
    /// for anonymization (ErasureRequestedAt), and disables the shared login once. The two-stage
    /// RetentionPurgeJob then, after the grace window, physically removes the consent forms +
    /// profiles and anonymizes every marked Client's PII + deletes the Identity user — its
    /// existing cross-tenant sweep needs no changes to pick up more than one row.
    /// </summary>
    public static async Task ExecuteAsync(
        IAppDbContext db, IIdentityService identity, Client client, CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;

        // Fan out across every studio this person is a client at — not just `client` itself.
        // `client` (the caller's active-tenant row, or the specific row an owner targeted) is
        // always included: a Client with no UserId (never linked to a login, e.g. a walk-in the
        // studio pre-created) has nothing to fan out to, and the list will just contain itself.
        List<Client> allClients = client.UserId is Guid uid
            ? await db.FindAllClientRecordsForUserAsync(uid, ct)
            : [client];
        if (allClients.All(c => c.Id != client.Id))
            allClients.Add(client); // defensive — should be unreachable, `client` is always live

        List<Guid> clientIds = allClients.Select(c => c.Id).ToList();

        List<ConsentForm> forms = await db.ConsentForms
            .IgnoreQueryFilters()
            .Where(f => clientIds.Contains(f.ClientId) && f.DeletedAt == null)
            .ToListAsync(ct);
        foreach (ConsentForm form in forms)
            form.DeletedAt = now;

        List<ClientProfile> profiles = await db.ClientProfiles
            .IgnoreQueryFilters()
            .Where(p => clientIds.Contains(p.ClientId))
            .ToListAsync(ct);
        foreach (ClientProfile profile in profiles)
            profile.DeletedAt = now;

        foreach (Client c in allClients)
            c.ErasureRequestedAt = now;

        await db.SaveChangesAsync(ct);

        if (client.UserId is Guid userId)
            await identity.DisableLoginAsync(userId, ct);
    }
}
```

`db.ConsentForms`/`db.ClientProfiles` need `IgnoreQueryFilters()` here for the same reason as the
new helper — they're being looked up across studios the caller's own JWT tenant doesn't cover.
This extends approved usage #29 too (it's the same fan-out operation, not a new cross-tenant
concern) — say so explicitly in the `database.md` update (§13.3).

`RequestDataErasureHandler` and `RequestMyDataErasureHandler` themselves need **no changes** —
they already resolve one `Client` and hand it to `ClientDataErasure.ExecuteAsync`; the fan-out now
happens inside that shared method.

### 5.4 Audit metadata — record how many studios were actually touched

`AuditLogBehavior`'s metadata is built by `AuditMetadataBuilder`, a per-command-type field
allowlist (never a wholesale serialize) — read its current shape
(`Pena_e_Arte.Application/**/AuditMetadataBuilder.cs` or wherever it currently lives; locate it
by usage, don't guess the path) before extending it. Add an allowlisted field for
`RequestDataErasureCommand`/`RequestMyDataErasureCommand`: `AffectedClientCount` (int — how many
`Client` rows across studios were marked, not the ids themselves, not PII). `AuditTargetId`
stays the single resolved/primary client id exactly as today (an accepted, documented limitation,
same shape as the existing "Referral-code commands carry only ReferralCodeId" precedent in this
codebase) — do not attempt to model a multi-target audit row, that's a larger schema change out
of scope here.

---

## 6. Phase B — Archive / restore a client (non-destructive "remove from list")

### 6.1 Domain + database

**File:** `Pena_e_Arte.Domain/Entities/Client.cs` — add one field, after `ErasureRequestedAt`:

```csharp
/// <summary>
/// Set when an owner/artist removes this client from the active client list without any data
/// destruction — the ordinary "delete client" action most vertical-booking SaaS offers
/// (Fresha/Vagaro-style). Deliberately NOT part of any EF Core query filter (unlike DeletedAt)
/// — an archived client's appointments, payments, consent forms, and reviews must keep
/// resolving their Client navigation normally everywhere except the default client-list view.
/// Fully reversible via RestoreClientCommand at any time; carries no retention/grace-period
/// semantics at all (contrast with ErasureRequestedAt).
/// </summary>
public DateTime? ArchivedAt { get; set; }
```

Migration: `dotnet ef migrations add AddClientArchivedAt --project Pena_e_Arte.Infrastructure`.
Nullable column, no default, no backfill needed (every existing client is un-archived).

### 6.2 Commands

**New file:** `Pena_e_Arte.Application/Clients/Commands/ArchiveClientCommand.cs`

```csharp
public record ArchiveClientCommand(Guid ClientId) : IRequest<Unit>, IAuditableCommand
{
    public string AuditAction => AuditActions.ClientArchived;
    public string AuditTargetType => AuditTargetTypes.Client;
    public Guid AuditTargetId => ClientId;
}

public class ArchiveClientHandler(IAppDbContext db)
    : IRequestHandler<ArchiveClientCommand, Unit>
{
    public async Task<Unit> Handle(ArchiveClientCommand command, CancellationToken ct)
    {
        Client client = await db.Clients.FirstOrDefaultAsync(c => c.Id == command.ClientId, ct)
            ?? throw new NotFoundException(nameof(Client), command.ClientId);

        if (client.ArchivedAt is not null) return Unit.Value; // idempotent

        client.ArchivedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public class ArchiveClientValidator : AbstractValidator<ArchiveClientCommand>
{
    public ArchiveClientValidator() => RuleFor(x => x.ClientId).NotEmpty();
}
```

**New file:** `Pena_e_Arte.Application/Clients/Commands/RestoreClientCommand.cs` — same shape,
`AuditActions.ClientRestored`, sets `client.ArchivedAt = null`. Both `ArtistAndAbove` at the
endpoint (matches the existing `GetClients`/`CreateClient` policy — archiving is a routine
list-management action, not an owner-only destructive one, unlike erasure).

### 6.3 Endpoints

**File:** `Pena_e_Arte.API/Endpoints/ClientEndpoints.cs` — add, near the existing
`UpdateClientArtist` route:

```csharp
group.MapPost("{clientId:guid}/archive", ArchiveClient).RequireAuthorization("ArtistAndAbove");
group.MapPost("{clientId:guid}/restore", RestoreClient).RequireAuthorization("ArtistAndAbove");
```

with the matching two handler methods following this file's existing pattern exactly (see
`UpdateClientArtist` immediately below it for the shape to copy).

### 6.4 Query + contract changes

**File:** `Pena_e_Arte.Application/Clients/Queries/GetClientsQuery.cs` — current code:

```csharp
public record GetClientsQuery(string? Search) : IRequest<List<ClientResponse>>;

public class GetClientsHandler(IAppDbContext db)
    : IRequestHandler<GetClientsQuery, List<ClientResponse>>
{
    public async Task<List<ClientResponse>> Handle(GetClientsQuery query, CancellationToken ct)
    {
        IQueryable<Domain.Entities.Client> q = db.Clients;
        ...
        return await q
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .Select(c => new ClientResponse(
                c.Id, c.StudioId, c.FirstName, c.LastName, c.Email, c.Phone, c.CreatedAt, c.UserId,
                c.ArtistId,
                c.Artist != null && c.Artist.DeletedAt == null
                    ? c.Artist.FirstName + " " + c.Artist.LastName
                    : null,
                c.ErasureRequestedAt))
            .ToListAsync(ct);
    }
}
```

Target: add an `IncludeArchived` parameter (default `false`), filter it explicitly (never via a
query filter — see §2.3), and project the new field:

```csharp
public record GetClientsQuery(string? Search, bool IncludeArchived = false) : IRequest<List<ClientResponse>>;

public class GetClientsHandler(IAppDbContext db)
    : IRequestHandler<GetClientsQuery, List<ClientResponse>>
{
    public async Task<List<ClientResponse>> Handle(GetClientsQuery query, CancellationToken ct)
    {
        IQueryable<Domain.Entities.Client> q = db.Clients;

        if (!query.IncludeArchived)
            q = q.Where(c => c.ArchivedAt == null);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string search = query.Search.ToLower();
            q = q.Where(c =>
                c.FirstName.ToLower().Contains(search) ||
                c.LastName.ToLower().Contains(search) ||
                c.Email.ToLower().Contains(search));
        }

        return await q
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .Select(c => new ClientResponse(
                c.Id, c.StudioId, c.FirstName, c.LastName, c.Email, c.Phone, c.CreatedAt, c.UserId,
                c.ArtistId,
                c.Artist != null && c.Artist.DeletedAt == null
                    ? c.Artist.FirstName + " " + c.Artist.LastName
                    : null,
                c.ErasureRequestedAt, c.ArchivedAt))
            .ToListAsync(ct);
    }
}
```

**File:** `Pena_e_Arte.Contracts/Responses/ClientResponse.cs` — append `ArchivedAt` after
`ErasureRequestedAt`, same optional-trailing-parameter style already used there:

```csharp
public record ClientResponse(
    Guid Id, Guid StudioId, string FirstName, string LastName, string Email, string? Phone,
    DateTime CreatedAt, Guid? UserId, Guid? ArtistId = null, string? ArtistName = null,
    DateTime? ErasureRequestedAt = null, DateTime? ArchivedAt = null);
```

Update the two other call sites that construct a `ClientResponse` positionally
(`CreateClientCommand.Map`, and any other `new ClientResponse(...)`/`Map(...)` call found by
grepping — `GetClientQuery`/`GetClientHandler` almost certainly has its own, check it) to pass
`client.ArchivedAt` as the new trailing argument.

### 6.5 Frontend

**File:** `frontend/src/features/clients/clientsApi.ts` — add two mutations mirroring
`requestDataErasure`'s existing shape (including its `Client` tag invalidation):

```typescript
archiveClient: builder.mutation<void, string>({
  query: (clientId) => ({ url: `clients/${clientId}/archive`, method: "POST" }),
  invalidatesTags: (_result, _error, clientId) => [{ type: "Client", id: clientId }, "Client"],
}),
restoreClient: builder.mutation<void, string>({
  query: (clientId) => ({ url: `clients/${clientId}/restore`, method: "POST" }),
  invalidatesTags: (_result, _error, clientId) => [{ type: "Client", id: clientId }, "Client"],
}),
```

Export the two hooks from the existing `export const { ... } = clientsApi;` block.

**File:** `frontend/src/features/clients/components/ClientListPage.tsx` — this page already has
an artist filter and a search box (`useGetClientsQuery(search)`). Add:
- A row-level "Archive" action (reuse whatever row-action affordance `DataTable` already
  supports elsewhere in this codebase — check `ClientDetailPage.tsx`'s existing dropdown/overflow
  pattern or `MyStudiosPage`'s kebab `DropdownMenu` for the established idiom before inventing a
  new one) — confirmed with the client's name in a lightweight confirm (not type-to-confirm; this
  is reversible, so it doesn't need erasure's stronger friction), then calls
  `useArchiveClientMutation`.
- A toggle/checkbox near the existing artist `Select` filter: "Show archived," which flips
  `useGetClientsQuery({ search, includeArchived: true })`. Archived rows render visually
  de-emphasized (muted, same treatment `MyStudiosPage`'s inactive-state rows already use) with a
  "Restore" action in place of "Archive."
- Update `useGetClientsQuery`'s RTK Query signature to accept the new `includeArchived` param
  (currently `useGetClientsQuery(search)` — check the query definition in `clientsApi.ts` and
  widen its argument type consistently, it is presently typed to accept only a `string |
  undefined`).

**File:** `frontend/src/features/clients/components/ClientDetailPage.tsx` — add an "Archive this
client" / "Restore this client" action near (not inside) the existing `EraseClientDataSection` at
the bottom of the page — visually distinct (a neutral/secondary button, not `destructive`
variant) so it reads as the lightweight, reversible sibling action it is, not a second flavor of
erasure.

---

## 7. Phase C — Self-service "export my data"

### 7.1 Query

**New file:** `Pena_e_Arte.Application/Clients/Queries/ExportMyDataQuery.cs`

```csharp
public record ExportMyDataQuery : IRequest<ClientDataExportResponse>;

public class ExportMyDataHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<ExportMyDataQuery, ClientDataExportResponse>
{
    public async Task<ClientDataExportResponse> Handle(ExportMyDataQuery query, CancellationToken ct)
    {
        // Same cross-tenant fan-out as Phase A/D (§3.3) — "my data" means every studio
        // relationship, not just the active one. Approved usage #29.
        List<Client> clients = await db.FindAllClientRecordsForUserAsync(currentUser.UserId, ct);
        if (clients.Count == 0)
            throw new NotFoundException(nameof(Client), currentUser.UserId);

        List<Guid> clientIds = clients.Select(c => c.Id).ToList();

        List<ClientProfile> profiles = await db.ClientProfiles
            .IgnoreQueryFilters().Where(p => clientIds.Contains(p.ClientId)).ToListAsync(ct);
        List<Appointment> appointments = await db.Appointments
            .IgnoreQueryFilters().Where(a => clientIds.Contains(a.ClientId)).ToListAsync(ct);
        List<ConsentForm> forms = await db.ConsentForms
            .IgnoreQueryFilters().Where(f => clientIds.Contains(f.ClientId)).ToListAsync(ct);
        List<TattooRecord> tattoos = await db.TattooRecords
            .IgnoreQueryFilters().Where(t => clientIds.Contains(t.ClientId)).ToListAsync(ct);
        List<Payment> payments = await db.Payments
            .IgnoreQueryFilters().Where(p => appointments.Select(a => a.Id).Contains(p.AppointmentId)).ToListAsync(ct);

        // Map into ClientDataExportResponse, grouped per studio (one section per Client row).
        // Exact DTO shape / field list is an implementation detail — define
        // ClientDataExportResponse in Pena_e_Arte.Contracts/Responses, one nested record per
        // studio (StudioName, client profile fields, appointment history, consent-form
        // metadata — NOT the signed PDF binary itself, link to it via the existing signed R2
        // URL flow instead — and tattoo records). Do not include other clients' data, other
        // studios' unrelated records, or any Identity/auth internals (password hash, tokens).
    }
}
```

Read `Payment.cs`/`Appointment.cs` before writing the final mapping to confirm exact field names
— they were not quoted verbatim in this prompt's own research pass and must not be guessed.

### 7.2 Endpoint

**File:** `Pena_e_Arte.API/Endpoints/ClientEndpoints.cs`:

```csharp
group.MapGet("me/export", ExportMyData).RequireAuthorization("ClientAndAbove");
```

Return as downloadable JSON (`Results.Ok`, frontend triggers a client-side `Blob` download —
matches the existing `downloadAuthenticatedFile` utility already used by
`ClientListPage.tsx`'s CSV export, reuse it rather than writing a second download helper) or a
generated PDF if you judge that materially better for a non-technical client reading their own
export — if PDF, follow this repo's existing PDF-generation pattern (check whether one already
exists, e.g. for invoices/receipts, before adding a new PDF library; `CLAUDE.md`'s "never add a
new ORM/library without flagging" spirit extends to any new dependency — flag it explicitly if a
new one is genuinely needed, do not silently add one).

### 7.3 Frontend

**File:** `frontend/src/features/clients/components/DeleteAccountSection.tsx` — add an "Export my
data" secondary button/link above the "Delete my account" button and inside the confirmation
dialog's description ("Consider exporting a copy of your data first" with a direct download
link), matching the "offer an export before deletion completes" practice. This is the one part of
this file that changes; the deletion flow itself (type-`DELETE`-to-confirm) is unchanged.

---

## 8. Phase D — Support-mediated cancel of a pending erasure request

### 8.1 Command

**New file:** `Pena_e_Arte.Application/Clients/Commands/CancelDataErasureCommand.cs`

```csharp
public record CancelDataErasureCommand(Guid ClientId) : IRequest<Unit>, IAuditableCommand
{
    public string AuditAction => AuditActions.ClientDataErasureCancelled;
    public string AuditTargetType => AuditTargetTypes.Client;
    public Guid AuditTargetId => ClientId;
}

public class CancelDataErasureHandler(IAppDbContext db, IIdentityService identity)
    : IRequestHandler<CancelDataErasureCommand, Unit>
{
    public async Task<Unit> Handle(CancelDataErasureCommand command, CancellationToken ct)
    {
        Client client = await db.Clients.FirstOrDefaultAsync(c => c.Id == command.ClientId, ct)
            ?? throw new NotFoundException(nameof(Client), command.ClientId);

        if (client.ErasureRequestedAt is not DateTime requestedAt)
            throw new BusinessRuleViolationException("No pending erasure request for this client.");

        // Mirror Phase A's fan-out symmetrically — this is the same underlying data-subject
        // request being undone, not a per-studio one.
        List<Client> allClients = client.UserId is Guid uid
            ? await db.FindAllClientRecordsForUserAsync(uid, ct)
            : [client];
        List<Guid> clientIds = allClients.Select(c => c.Id).ToList();

        // Restore only the rows THIS erasure request soft-deleted — matched by the exact
        // ErasureRequestedAt timestamp stamped on each Client row in the same operation, so a
        // form/profile independently expired by the routine 7-year retention pass (unrelated to
        // this request) is never resurrected by mistake.
        List<ConsentForm> forms = await db.ConsentForms
            .IgnoreQueryFilters()
            .Where(f => clientIds.Contains(f.ClientId) && f.DeletedAt == requestedAt)
            .ToListAsync(ct);
        foreach (ConsentForm form in forms) form.DeletedAt = null;

        List<ClientProfile> profiles = await db.ClientProfiles
            .IgnoreQueryFilters()
            .Where(p => clientIds.Contains(p.ClientId) && p.DeletedAt == requestedAt)
            .ToListAsync(ct);
        foreach (ClientProfile profile in profiles) profile.DeletedAt = null;

        foreach (Client c in allClients) c.ErasureRequestedAt = null;

        await db.SaveChangesAsync(ct);

        if (client.UserId is Guid userId)
            await identity.EnableLoginAsync(userId, ct);

        return Unit.Value;
    }
}

public class CancelDataErasureValidator : AbstractValidator<CancelDataErasureCommand>
{
    public CancelDataErasureValidator() => RuleFor(x => x.ClientId).NotEmpty();
}
```

`OwnerOnly` at the endpoint — same trust level as the owner-initiated erasure it undoes. This
must run before `RetentionPurgeJob`'s grace window elapses; once anonymization has actually run,
`ErasureRequestedAt` is already `null` again (see `AnonymizeErasedClientsAsync`'s last line) and
`CancelDataErasureCommand` correctly, harmlessly refuses with "no pending erasure request" — it
cannot un-anonymize already-scrubbed PII, and does not need special-case logic to detect that; the
guard above already covers it.

### 8.2 New `IIdentityService` method

**File:** `Pena_e_Arte.Domain/Interfaces/IIdentityService.cs` — add, next to `DisableLoginAsync`:

```csharp
/// <summary>
/// Reverses DisableLoginAsync: clears the account lockout so the user can sign in again.
/// Does not restore a refresh token — the user must sign in fresh with their password. No-op
/// if no such user exists or the account was never locked out.
/// </summary>
Task EnableLoginAsync(Guid userId, CancellationToken ct);
```

**File:** `Pena_e_Arte.Infrastructure/Services/IdentityService.cs` — implement next to
`DisableLoginAsync`, using `userManager.SetLockoutEndDateAsync(user, null)` (clears the far-future
lockout `DisableLoginAsync` set) — do not also re-enable via `SetLockoutEnabledAsync(user,
false)`, since lockout *capability* should stay enabled for future use, only the current lockout
*end date* needs clearing.

### 8.3 Endpoint + frontend

**File:** `Pena_e_Arte.API/Endpoints/ClientEndpoints.cs`:

```csharp
group.MapPost("{clientId:guid}/cancel-erasure", CancelDataErasure).RequireAuthorization("OwnerOnly");
```

**File:** `frontend/src/features/clients/components/EraseClientDataSection.tsx` — the existing
"pending" branch (`if (erasureRequestedAt) { ... }`) currently renders a read-only banner with no
action. Add a "Cancel erasure request" button inside that banner, confirmed with a plain
`AlertDialog` (not type-to-confirm — this is a recovery action, it should be easy, not hard),
calling a new `useCancelDataErasureMutation`.

---

## 9. Test requirements

**Unit (`tests/Pena_e_Arte.UnitTests/Clients/`):**
- `RequestMyDataErasureHandlerTests.cs` / `RequestDataErasureHandlerTests.cs` — add a case
  seeding the SAME `UserId` with `Client` rows at two different studios, asserting **both** get
  `ErasureRequestedAt` set, **both** get their consent forms/profile soft-deleted, and
  `DisableLoginAsync` is still called exactly once (not once per studio). Keep the existing
  `Handle_ErasesOnlyTheCallersOwnData_NeverAnotherClients` test passing unmodified — a genuinely
  different person's data must still be untouched; only the SAME person's other-studio rows
  should now be included.
- `ArchiveClientHandlerTests.cs` / `RestoreClientHandlerTests.cs` — new. Assert archiving does
  NOT touch `DeletedAt`, `ErasureRequestedAt`, consent forms, or the profile; assert idempotency
  (archiving an already-archived client is a no-op, not an error).
- `CancelDataErasureHandlerTests.cs` — new. Seed a two-studio erasure (both rows marked, forms/
  profile soft-deleted with the same timestamp), cancel from one studio's owner context, assert
  BOTH studios' rows are restored, `EnableLoginAsync` called once, and a form independently
  soft-deleted by the routine retention pass (different `DeletedAt` timestamp, not equal to
  `ErasureRequestedAt`) is correctly left alone. Also assert calling cancel with no pending
  request throws `BusinessRuleViolationException`.
- `ExportMyDataHandlerTests.cs` — new. Assert the export includes data from every studio the
  caller belongs to and nothing from any other client.
- `GetClientsHandlerTests.cs` — extend existing tests to cover `IncludeArchived` both ways.

**Integration (`tests/Pena_e_Arte.IntegrationTests/`):** exercise the four new/changed endpoints
through the real ASP.NET Core auth pipeline (`ClientEndpointsAuthorizationTests` or wherever the
existing per-endpoint policy tests live — follow that file's existing pattern) — confirm
`{clientId}/archive`/`restore` reject anything below `ArtistAndAbove`, `{clientId}/cancel-erasure`
rejects anything below `OwnerOnly`, `me/export` requires `ClientAndAbove` and returns only the
caller's own data end-to-end against a real MySQL instance (a fake in-memory `DbContext` doesn't
register query filters at all, so it cannot prove the multi-studio fan-out actually crosses the
tenant boundary correctly — this class of bug was only ever caught by a real-`AppDbContext`
integration test elsewhere in this codebase, per the guest-checkout precedent in
`architecture.md`'s IgnoreQueryFilters row #51 — do not settle for unit tests alone here).

**Frontend (`pnpm test`):** `ClientListPage` archive/restore interaction + "show archived" toggle
(loading/error/empty states); `ClientDetailPage` archive/restore action; `DeleteAccountSection`
export-my-data link; `EraseClientDataSection` cancel-erasure button (both the pending-banner
render and the confirm-and-call-mutation path). Update `DeleteAccountSection.test.tsx`'s existing
assertions if the export link changes its DOM structure.

---

## 10. Fork #3 — Platform-level client suspension (spec only — do NOT build tonight)

Full implementation-ready specification, for a future prompt once the open questions below are
answered by Phi:

**Trust boundary:** issuer/admin-initiated, cross-tenant, distinct from both owner-initiated
erasure (§Phase A, tenant-scoped, destroys data) and archive (§Phase B, tenant-scoped, keeps
data). A platform suspension is neither — it is a punitive access restriction that may or may not
touch data at all.

**Entities (proposed, not final):** `ClientSuspension { Id, UserId, Reason, Category
(FraudSuspected/AbusePattern/PlatformViolation), SuspendedByAdminUserId, SuspendedAt, LiftedAt,
LiftedByAdminUserId }` — non-tenant (no `StudioId`, no query filter), same shape as
`AuditLogEntry`/`ConductReport`, since a platform suspension is not one studio's data.

**Open questions — Phi must answer before this is buildable, not an engineering call:**
1. Does a suspension disable the shared Identity login everywhere (locking the person out of
   every studio, including ones with no complaint against them), or only block new bookings/new
   studio joins while leaving existing studio relationships and logins alone?
2. Is there an appeal/review path, and if so who reviews it — admin only, or does the reported
   studio's owner get visibility?
3. Relationship to `ConductReport`: does resolving a report as `Resolved` with a specific
   resolution type ever *trigger* a suspension automatically, or is suspension always a separate,
   manually-initiated admin action reviewed independently of any specific report?
4. Does a suspended client's existing appointments/data get touched at all (archived? erasure-
   pipelined?), or does suspension purely block *future* activity while past records and data
   stay exactly as they are?
5. Retention/audit: is a lifted suspension retained forever in `AuditLogEntry` (recommended —
   this is exactly the kind of trust-and-safety record that should never be silently deleted), and
   does the target ever get notified that they were suspended/unsuspended, or is this silent by
   design?

**Do not infer answers from the benchmark set** — Fresha/Vagaro/Boulevard/GlossGenius's own
trust-and-safety tooling is not publicly documented in enough detail to safely copy, and this is
exactly the kind of auth/compliance-adjacent surface `CLAUDE.md`'s own project conventions require
a real product decision for, not an engineering guess.

---

## 11. Industry-standard benchmark note per phase (CLAUDE.md rule #6)

- **Phase A** is not itself benchmark-driven — no competitor's public documentation describes
  their own multi-tenant erasure fan-out logic in enough detail to compare against. It is a
  correctness fix against this codebase's own stated promise ("permanently delete your account
  and personal data") and against GDPR Art. 17's own scope (erasure means erasure, not
  erasure-at-one-of-several-locations).
- **Phase B** directly closes a benchmark gap: Fresha's own help documentation is explicit that
  "delete client" removes someone from the active list while retaining their sales/appointments/
  reviews, and separately audit-logs who performed the deletion — this prompt's `ArchiveClient`/
  `RestoreClient` (audited via `IAuditableCommand`, same as every other toggle in this codebase)
  matches that shape. Mindbody's "an inactive client reactivates on login" is the precedent for
  "reversible, not a one-way action," which `RestoreClientCommand` satisfies more directly (an
  explicit owner action rather than an automatic reactivate-on-login, which doesn't fit this
  product's own client self-service model as cleanly).
- **Phase C** matches the general data-portability expectation (offer an export before/alongside
  deletion) that GDPR Art. 20 and CCPA both converge on; no single benchmark competitor's exact UX
  was verified for this one, since it's a baseline compliance expectation rather than a
  competitive UX differentiator.
- **Phase D** has no direct competitor precedent found; it exists to close this codebase's own
  gap (no undo path at all) rather than to match a specific competitor pattern.

---

## 12. Help sync (CLAUDE.md rule #7 — mandatory in this same change)

### 12.1 In-app Help Menu (`frontend/src/features/help/helpContent.ts`)

- **`client-delete-account`** (existing) — its current `warnings` array says: *"You're signed out
  immediately and can't log back in. Your data is then permanently deleted after a 30-day grace
  period... You only ever delete your own account — it can never affect anyone else's data."*
  That second sentence is true (no other *person's* data is touched) but was written before the
  multi-studio fan-out fix and reads, in context, like a stronger promise than the pre-fix code
  delivered. Now that Phase A is shipped, add one line making the actual (now-correct) scope
  explicit: *"If you're a client at more than one studio, this deletes your account and data
  everywhere you're a client — not just here."*
- **`owner-clients-erase-data`** (existing) — same addition to its `warnings` array.
- **New article `client-export-data`** (role: Client, route `/clients/me`): steps through the new
  "Export my data" link in the Sharing tab; summary along the lines of "Download a copy of your
  data — profile, appointments, and consent records, across every studio you're a client at —
  before or instead of deleting your account."
- **New article `owner-clients-archive`** (role: Owner, route `/clients`): documents
  Archive/Restore, explicitly contrasted against erasure — *"Archiving removes a client from your
  active list without deleting anything — their appointments, payments, and consent records stay
  exactly as they are, and you can restore them any time. Use Erase client data instead if you
  need to permanently delete their information."* Cross-link both directions via
  `relatedArticleIds`.
- **`owner-clients-erase-data`** gains a new step/`tips` entry documenting "Cancel erasure
  request" for the pending-request banner state.

### 12.2 Standalone manual (`frontend/public/user-manual/index.html`)

Mirror all four content changes above into the corresponding sections (`#client-delete-account`
if it exists under that anchor — confirm the exact anchor id by reading the file, do not assume
it matches the Help Menu article id 1:1) — add the multi-studio scope clarification, the new
export-data paragraph, and the new archive/restore paragraph under the owner client-detail
section.

### 12.3 Onboarding tours

**File:** `frontend/src/features/help/tours/clientTour.ts` — check whether an existing step
targets the Sharing tab / delete-account area; if the export link is added inside that same
existing UI region, no new step is needed (say so explicitly, per the existing convention of
stating "no new step needed" with a reason rather than silently skipping the question). **File:**
`frontend/src/features/help/tours/ownerTour.ts`/`artistTour.ts` — same check for the client-list/
client-detail steps; add a step only if you judge archive/restore to be non-discoverable without
one.

---

## 13. `docs/claude/*.md` sync

### 13.1 `docs/claude/database.md`

Add `Client.ArchivedAt` to wherever the `Client` entity's fields are documented, with a one-line
note distinguishing it from `DeletedAt`/`ErasureRequestedAt` (mirror §2.3's reasoning, condensed).

### 13.2 `docs/claude/architecture.md` — fix the stale Decisions Log line

In the "Two-stage retention purge + R2 delete + audited erasure (EPIC-0001 PENA-104) — 2026-07-31"
row, replace the trailing sentence *"No client-facing self-service erasure UI yet (open question
§3.8)."* with something like: *"Client-facing self-service erasure UI shipped in a later,
undated session (`RequestMyDataErasureCommand`, `DeleteAccountSection.tsx`) — this row was not
updated at the time; corrected 2026-09-18, see that date's Decisions Log entry for the
cross-studio fan-out bug found in it."*

### 13.3 `docs/claude/architecture.md` — `IgnoreQueryFilters()` Approved Usages, row #29

Extend the existing row's method list rather than adding a new numbered row (§2.2):

```
| 29 | `ClientAccountExtensions` (`FindClientForUserAtStudioAsync`, `FindAnyClientRecordForUserAsync`, `FindAllClientRecordsForUserAsync`) | Cross-tenant `Client` lookup by `UserId` — supports linking a client's account across the multiple studios they belong to, and fanning out right-to-erasure/export/cancel to every studio relationship (2026-09-18) | Authenticated (any role, called from login/registration/multi-studio flows, and the erasure/export/cancel commands) |
```

### 13.4 `docs/claude/architecture.md` — new Decisions Log entry + Feature Module Map row

Add a dated entry (2026-09-18) summarizing Phases A–D exactly the way the existing PENA-104 entry
is written (what shipped, why, verification), explicitly naming: the cross-studio bug found and
fixed, the new Archive/Restore/Export/Cancel features, and — as backlog notes, not shipped work —
Fork #3's full spec (§10) and the deferred self-service-cancel-before-lockout UX idea (§3.4).

Add a Feature Module Map row (confirm the current maximum row number before picking the next one
— other overnight work may have added rows since this prompt was written, exactly as the NIPT
prompt's own §13.2 instructs):

```
| <next> | Client Account Deletion Hardening | `Client.ArchivedAt`; fixed `ClientDataErasure` fan-out | `ClientAccountExtensions.FindAllClientRecordsForUserAsync` (approved usage #29) | Per-tenant (archive/restore), cross-tenant (erasure/export/cancel fan-out, same person) |
```

---

## 14. Verification checklist — do not mark this done until all of these pass

1. `dotnet build` clean, `dotnet test` — all existing tests plus every new test in §9 pass.
2. `pnpm lint`, `pnpm build`, `pnpm test` clean.
3. Manually (or via integration test) seed one Identity user as a `Client` at two different
   studios, request self-service erasure from one studio's context, and confirm: both `Client`
   rows get `ErasureRequestedAt`; both studios' consent forms/profile are soft-deleted; the login
   is disabled exactly once (not double-disabled, no error on the second internal call); the
   existing single-studio erasure path is unaffected for a client who only belongs to one studio.
4. Confirm `RetentionPurgeJob`, unmodified, correctly anonymizes both studios' rows in the same
   run once the grace window elapses (this exercises §4's claim that the job needed no changes —
   verify it, don't just assert it).
5. Archiving a client: confirm their past appointments, payments, and consent forms still display
   the client's real name when viewed directly (an `Include(a => a.Client)` on an `Appointment`
   for an archived client must NOT return `null` — this is the exact failure mode §2.3 explains
   `DeletedAt` would have caused; prove `ArchivedAt` doesn't cause it).
6. `GetClientsQuery` excludes archived clients by default and includes them with
   `includeArchived=true`; `ClientListPage`'s "Show archived" toggle round-trips correctly.
7. Export-my-data returns data from every studio the caller belongs to, nothing from any other
   client, and excludes any Identity/auth internals.
8. Cancel-erasure: restores exactly the rows the erasure request itself soft-deleted (verified via
   the `DeletedAt == requestedAt` matching logic), re-enables login, and correctly refuses (with a
   clear error, not a 500) when there is no pending request or the grace window has already
   elapsed and anonymization already ran.
9. `helpContent.ts` — all four changes (§12.1) render correctly in the in-app Help Menu; search
   "export my data" and "archive" both surface the new articles.
10. `user-manual/index.html` renders correctly standalone with all four updates.
11. `docs/claude/database.md`, `docs/claude/architecture.md` (stale-line fix, extended row #29,
    new Decisions Log entry, new Feature Module Map row) diffs reviewed for accuracy against what
    was actually shipped.
12. Confirm nothing in §4's "do not touch" list was touched — diff those exact files/folders
    against `main` and confirm zero changes.
13. No new `AllowAnonymous` endpoints were added (all four new/changed endpoints require
    authorization with an explicit policy) and no PII appears in any new Serilog output — grep the
    diff for any `Log.*` call touching client name/email/phone.
14. §10 (Fork #3) exists only as a doc addition to `architecture.md` — confirm no
    `ClientSuspension` entity, command, or endpoint was actually created.

---

## 15. Final deliverable spec

**Files created:**
- `Pena_e_Arte.Application/Clients/Commands/ArchiveClientCommand.cs`
- `Pena_e_Arte.Application/Clients/Commands/RestoreClientCommand.cs`
- `Pena_e_Arte.Application/Clients/Commands/CancelDataErasureCommand.cs`
- `Pena_e_Arte.Application/Clients/Queries/ExportMyDataQuery.cs`
- `Pena_e_Arte.Contracts/Responses/ClientDataExportResponse.cs` (shape per §7.1)
- One EF Core migration (`AddClientArchivedAt`)
- New test files per §9
- New frontend components/hooks per §6.5, §7.3, §8.3

**Files modified:** `RequestDataErasureCommand.cs` (Phase A fix), `ClientAccountExtensions.cs`,
`Client.cs`, `ClientResponse.cs`, `GetClientsQuery.cs`, every other `ClientResponse` construction
site, `ClientEndpoints.cs`, `IIdentityService.cs` + `IdentityService.cs`, `AuditActions.cs`,
`clientsApi.ts`, `ClientListPage.tsx`, `ClientDetailPage.tsx`, `DeleteAccountSection.tsx`,
`EraseClientDataSection.tsx`, `helpContent.ts`, `user-manual/index.html`, relevant tour file(s),
`docs/claude/database.md`, `docs/claude/architecture.md`.

**Commit message:**

```
Fix cross-studio erasure fan-out; add client archive, self-service export, and erasure cancel

- RequestDataErasureCommand/RequestMyDataErasureCommand now fan out across every studio a
  client belongs to (they share one Identity login but had per-studio Client rows) instead of
  silently leaving PII un-anonymized outside the caller's active tenant.
- New Client.ArchivedAt: a non-destructive, reversible "remove from list" action distinct from
  both soft-delete and GDPR erasure, closing a real gap against the Fresha/Vagaro benchmark.
- New self-service "export my data" (clients/me/export) and owner-facing "cancel pending
  erasure" (clients/{id}/cancel-erasure), the latter closing this feature's previous
  point-of-no-return gap.
- Corrects a stale architecture.md Decisions Log line that said self-service erasure UI didn't
  exist yet; it shipped in an undocumented later session.
- Fork #3 (platform-level client suspension for abuse/fraud) is speced but explicitly not
  built — see architecture.md's Decisions Log for the open product/legal questions.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
```
