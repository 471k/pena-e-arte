# Overnight Prompt — Client Artist Assignment

> Date: 2026-08-20
> Target: `Pena_e_Arte.Domain`, `Pena_e_Arte.Contracts`, `Pena_e_Arte.Application` (Clients),
> `Pena_e_Arte.Infrastructure` (one EF migration), `Pena_e_Arte.API`, `frontend/src/features/clients`,
> backend + frontend tests, Help Menu (`helpContent.ts`), standalone user manual (`index.html`).
> One new EF Core migration (nullable column — zero-downtime, no data backfill). No new npm or
> NuGet packages. No onboarding-tour changes needed (see Part 7 — verified, not assumed).

---

## Pre-flight

1. Read `CLAUDE.md`, `docs/claude/backend.md`, `docs/claude/frontend.md`, `docs/claude/database.md`,
   `docs/claude/conventions.md` before making any changes.
2. Baseline, before touching anything:
   - `dotnet build`
   - `dotnet test` — note the current pass count; pre-existing failures are not this prompt's problem,
     but do not introduce new ones.
   - `pnpm tsc --noEmit`
   - `pnpm test src/features/clients` — confirm the current suite is green first.
3. Read `Pena_e_Arte.Application/Designs/Commands/CreateDesignCommand.cs` in full before starting
   Part 3. It is the exact precedent Part 3's server-side artist-override logic must mirror — see
   Context below for why this isn't optional.

---

## Context — current state (verified against live source, 2026-08-20)

- `Client` has **no artist relationship of any kind** today. `Pena_e_Arte.Domain/Entities/Client.cs`
  has only `UserId, FirstName, LastName, Email, Phone, ErasureRequestedAt` plus the `Profile` /
  `Appointments` / `TattooRecords` collections. This was verified by reading the file directly, not
  inferred from `docs/claude/architecture.md`'s Feature Module Map — there is no map entry for it
  because it was never built, not because the map is stale.
- Both `Owner` and `Artist` can create clients today. `ClientEndpoints.cs` gates
  `POST /api/v1/clients` with `RequireAuthorization("ArtistAndAbove")`, and
  `ClientListPage.tsx`'s `canCreate = usePermission(Role.Artist)` matches. Any change to the create
  flow must handle both callers, not just Owner.
- There is **no way to edit a client's basic fields after creation at all** —
  `Pena_e_Arte.Application/Clients/Commands/` has no `UpdateClientCommand`. This prompt adds the
  narrowest possible slice of that (artist reassignment only). Do not build a general client-edit
  form — out of scope.
- This exact class of bug — trusting a client-supplied `artistId` instead of resolving it from the
  caller's own identity — was already found and fixed once in this codebase, in `CreateDesignCommand.cs`
  (see `docs/claude/architecture.md`'s 2026-07-01 artist QA pass entry: *"trusted the client-supplied
  `artistId`, letting an artist assign a new design to a colleague → now always overridden with the
  caller's own artist id"*). The current, correct code for that fix is:

  ```csharp
  Guid artistId = req.ArtistId;
  if (currentUser.Role == "artist")
  {
      Guid? myArtistId = await db.Artists
          .Where(a => a.UserId == currentUser.UserId)
          .Select(a => (Guid?)a.Id)
          .FirstOrDefaultAsync(ct);
      if (myArtistId is null)
          throw new ForbiddenException();
      artistId = myArtistId.Value;
  }
  ```

  Part 3's `CreateClientCommand` change follows this shape exactly. Do not skip it or "simplify" it —
  it is a previously-shipped defect class in this specific codebase, not a hypothetical concern.

---

## Decisions (already made with the product owner — do not re-litigate)

| # | Decision | Rationale |
|---|---|---|
| 1 | Every new client (Owner-created or Artist-created) requires an assigned artist. `CreateClientRequest.ArtistId` is a required `Guid`, validated `NotEmpty()`. | Confirmed. Mandatory going forward, not optional. |
| 2 | Existing clients are **not backfilled**. `Client.ArtistId` is added as a **nullable** database column. | Requirement #1 only binds new creations — there's no artist to assign to existing rows without guessing, and a wrong guess is worse than a visible "Unassigned" state an owner can fix later. Follows `docs/claude/database.md`'s zero-downtime "add nullable column" pattern. Do **not** attempt to make the column non-nullable in this pass. |
| 3 | Owner (and Issuer) see a required artist dropdown on the create-client form. Artist sees no such field — the client is silently assigned to them. | Confirmed. Enforced **server-side**, per the `CreateDesignCommand` precedent above — the request body's `artistId` is never trusted for an artist-role caller, regardless of what the frontend sends. |
| 4 | A minimal reassignment capability is added: `PATCH /api/v1/clients/{clientId}/artist`, gated `OwnerOnly`. | Confirmed. A wrong pick at creation must be correctable, but this is scoped to *only* the artist field — not a general `UpdateClientCommand`. `OwnerOnly` (not `ArtistAndAbove`) because reassigning a client between staff is a roster-management action, consistent with `RequestDataErasure` — the one other administrative `/clients` action — already being `OwnerOnly` while every other `/clients` endpoint is `ArtistAndAbove`. The request accepts a **nullable** `ArtistId` (unlike creation) so an owner can also explicitly unassign a client (e.g. the artist left the studio) — decision #1's "required" only binds the moment of creation. |
| 5 | The Clients list gets an artist filter, implemented as a **client-side filter** over the already-fetched list — no new backend query parameter. | `GetClientsQuery` already returns the studio's full, unpaginated client list to any `ArtistAndAbove` caller (`ClientListPage.tsx`'s `DataTable` renders the entire returned array — no cursor pagination on this endpoint). Filtering that array by `artistId` client-side is O(n) on data already in hand. Adding an `artistId` param to `useGetClientsQuery` instead would force a signature change on **three unrelated call sites** that also use this hook purely to populate a client picker (`BookAppointmentForm.tsx`, `CreateDesignPage.tsx`, `CreatePaymentIntentPage.tsx`) — client-side filtering avoids touching any of them. |
| 6 | `UpdateClientArtistCommand` implements `IAuditableCommand`. | Not explicitly requested, but flagged per `CLAUDE.md` rule 6: `AuditActions` already logs the sibling case `ClientProfileCrossTenantOptedIn/Out` — another owner/support-adjacent mutation on the `Client` record. Leaving this one `Client`-record mutation silently unaudited next to its logged sibling would be an inconsistency, not a simplification. |

**Explicitly out of scope, flagged and not fixed here:** the standalone user manual's
`artist-clients` section access table currently reads `Owner ✓ (can also add clients)` with no
matching `✓` note for Artist, even though artists can and do create clients today (this predates
this prompt — verified via `ClientEndpoints.cs`'s `ArtistAndAbove` policy and `helpContent.ts`'s own
`artist-clients` article, which *does* say "Click 'New Client' to add a client"). Fixing that
pre-existing inconsistency is a separate, smaller Help-only cleanup — do not fold it into this pass.

---

## Part 1 — Domain + EF Core

### 1a. `Pena_e_Arte.Domain/Entities/Client.cs`

Add, directly after `Phone`:

```csharp
public Guid? ArtistId { get; set; }
public Artist? Artist { get; set; }
```

### 1b. `Pena_e_Arte.Domain/Entities/Artist.cs`

Add a back-reference collection, alongside the existing `Appointments` / `TattooRecords` / etc.:

```csharp
public ICollection<Client> Clients { get; set; } = [];
```

### 1c. `Pena_e_Arte.Infrastructure/Persistence/Configurations/ClientConfiguration.cs`

Add, after the existing indexes:

```csharp
builder.HasIndex(c => new { c.StudioId, c.ArtistId })
       .HasDatabaseName("ix_clients_studio_artist");

builder.HasOne(c => c.Artist)
       .WithMany(a => a.Clients)
       .HasForeignKey(c => c.ArtistId)
       .HasConstraintName("fk_clients_artists")
       .OnDelete(DeleteBehavior.Restrict);
```

`Restrict`, not `SetNull`, to match the identical `Client`/`Artist` FK shape already used on
`AppointmentConfiguration.cs`'s `Artist` relationship — a new, unexplained `SetNull` precedent here
would be an inconsistency. In practice this constraint is close to unreachable either way: per
`docs/claude/database.md`, tenant rows (including `Artist`) are soft-deleted, never hard-deleted.

### 1d. Migration

```bash
dotnet ef migrations add AddArtistIdToClient \
  --project Pena_e_Arte.Infrastructure \
  --startup-project Pena_e_Arte.API
```

Verify the generated migration adds a **nullable** `artist_id` column, the FK, and the composite
index — nothing else. Apply it locally (`dotnet ef database update ...`) and confirm the app still
boots before moving on.

---

## Part 2 — Contracts

### 2a. `Pena_e_Arte.Contracts/Requests/CreateClientRequest.cs`

```csharp
namespace Pena_e_Arte.Contracts.Requests;

public record CreateClientRequest(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    Guid ArtistId);
```

This is a **breaking** positional-record change. Grep the whole repo for
`new CreateClientRequest(` and update every call site (tests, primarily) to supply the new
argument — the compiler will find most of these for you, but grep first so nothing is missed.

### 2b. `Pena_e_Arte.Contracts/Responses/ClientResponse.cs`

```csharp
namespace Pena_e_Arte.Contracts.Responses;

public record ClientResponse(
    Guid Id,
    Guid StudioId,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    DateTime CreatedAt,
    Guid? UserId,
    Guid? ArtistId = null,
    string? ArtistName = null);
```

Trailing optional params with defaults — mirrors `AppointmentResponse.ClientName`'s existing
denormalized-field pattern exactly, so every existing positional `new ClientResponse(...)` call site
(mostly tests) keeps compiling unchanged. `ArtistName` is always present in the actual JSON response
(possibly `null`) — the C# default only affects constructor call-site convenience, not serialization.

### 2c. New file — `Pena_e_Arte.Contracts/Requests/UpdateClientArtistRequest.cs`

```csharp
namespace Pena_e_Arte.Contracts.Requests;

public record UpdateClientArtistRequest(Guid? ArtistId);
```

---

## Part 3 — Application layer

### 3a. `Pena_e_Arte.Application/Clients/Commands/CreateClientCommand.cs` — full replacement

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Clients.Commands;

public record CreateClientCommand(CreateClientRequest Request) : IRequest<ClientResponse>;

public class CreateClientHandler(IAppDbContext db, ICurrentTenant tenant, ICurrentUser currentUser)
    : IRequestHandler<CreateClientCommand, ClientResponse>
{
    public async Task<ClientResponse> Handle(CreateClientCommand command, CancellationToken ct)
    {
        CreateClientRequest req = command.Request;

        bool exists = await db.Clients.AnyAsync(c => c.Email == req.Email, ct);
        if (exists)
            throw new BusinessRuleViolationException($"A client with email '{req.Email}' already exists in this studio.");

        // An artist can only ever create clients assigned to themselves — any artistId supplied
        // in the request is ignored rather than trusted. Mirrors CreateDesignCommand's fix for the
        // identical defect class (see docs/claude/architecture.md, 2026-07-01 artist QA pass).
        Guid artistId = req.ArtistId;
        if (currentUser.Role == "artist")
        {
            Guid? myArtistId = await db.Artists
                .Where(a => a.UserId == currentUser.UserId)
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(ct);
            if (myArtistId is null)
                throw new ForbiddenException();
            artistId = myArtistId.Value;
        }

        // Validate up front for a clean 404/business-rule error instead of an FK violation, and
        // load the entity needed to denormalize ArtistName into the response.
        Artist artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == artistId, ct)
            ?? throw new NotFoundException(nameof(Artist), artistId);
        if (!artist.IsActive)
            throw new BusinessRuleViolationException("Cannot assign a client to an inactive artist.");

        Client client = new()
        {
            StudioId = tenant.StudioId,
            FirstName = req.FirstName,
            LastName = req.LastName,
            Email = req.Email,
            Phone = req.Phone,
            ArtistId = artist.Id
        };

        db.Clients.Add(client);
        await db.SaveChangesAsync(ct);

        return Map(client, artist);
    }

    internal static ClientResponse Map(Client c, Artist? artist = null) =>
        new(c.Id, c.StudioId, c.FirstName, c.LastName, c.Email, c.Phone, c.CreatedAt, c.UserId,
            artist?.Id, artist is null ? null : $"{artist.FirstName} {artist.LastName}");
}
```

Note the two-step artist resolution is intentional, not redundant: the first query (when
`role == "artist"`) only ever needs to check *whose* artist record the caller has, mirroring
`CreateDesignCommand`'s minimal projection exactly; the second, full-entity query is needed
regardless of caller role, both to validate existence/`IsActive` and to populate `ArtistName` in
the response — something `CreateDesignCommand` never needed to do.

`Map`'s signature changed (`artist` param added, defaulted to `null`) — every other call site
(`GetClientQuery`, `GetClientsQuery`, and the new `UpdateClientArtistCommand`) is updated below to
pass its own loaded `Artist?` through.

### 3b. `Pena_e_Arte.Application/Clients/Validators/CreateClientValidator.cs`

Add one rule:

```csharp
RuleFor(x => x.Request.ArtistId).NotEmpty();
```

### 3c. `Pena_e_Arte.Application/Clients/Queries/GetClientsQuery.cs` — handler replacement

```csharp
public async Task<List<ClientResponse>> Handle(GetClientsQuery query, CancellationToken ct)
{
    IQueryable<Domain.Entities.Client> q = db.Clients.Include(c => c.Artist);

    if (!string.IsNullOrWhiteSpace(query.Search))
    {
        string search = query.Search.ToLower();
        q = q.Where(c =>
            c.FirstName.ToLower().Contains(search) ||
            c.LastName.ToLower().Contains(search) ||
            c.Email.ToLower().Contains(search));
    }

    List<Domain.Entities.Client> clients = await q
        .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
        .ToListAsync(ct);

    return clients.Select(c => CreateClientHandler.Map(c, c.Artist)).ToList();
}
```

Materialize-then-map, not the previous in-query `.Select(c => CreateClientHandler.Map(c))` — this
mirrors `GetArtistsHandler`'s exact existing pattern (`ToListAsync` first, then
`.Select(CreateArtistHandler.Map)` on the in-memory list) rather than relying on EF Core to
translate a static-method call inside the SQL projection.

### 3d. `Pena_e_Arte.Application/Clients/Queries/GetClientQuery.cs`

Add `.Include(c => c.Artist)` to the existing `FirstOrDefaultAsync` query, and change the return
line to `return CreateClientHandler.Map(client, client.Artist);`.

### 3e. New file — `Pena_e_Arte.Application/Clients/Commands/UpdateClientArtistCommand.cs`

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Clients.Commands;

public record UpdateClientArtistCommand(Guid ClientId, UpdateClientArtistRequest Request)
    : IRequest<ClientResponse>, IAuditableCommand
{
    public string AuditAction => AuditActions.ClientArtistReassigned;
    public string AuditTargetType => AuditTargetTypes.Client;
    public Guid AuditTargetId => ClientId;
}

public class UpdateClientArtistHandler(IAppDbContext db)
    : IRequestHandler<UpdateClientArtistCommand, ClientResponse>
{
    public async Task<ClientResponse> Handle(UpdateClientArtistCommand command, CancellationToken ct)
    {
        Client client = await db.Clients.FirstOrDefaultAsync(c => c.Id == command.ClientId, ct)
            ?? throw new NotFoundException(nameof(Client), command.ClientId);

        Artist? artist = null;
        if (command.Request.ArtistId is Guid artistId)
        {
            artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == artistId, ct)
                ?? throw new NotFoundException(nameof(Artist), artistId);
            if (!artist.IsActive)
                throw new BusinessRuleViolationException("Cannot assign a client to an inactive artist.");
        }

        client.ArtistId = artist?.Id;
        client.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return CreateClientHandler.Map(client, artist);
    }
}
```

`AuditStudioId` is left at its default (`null`) — `AuditLogBehavior` falls back to
`ICurrentTenant.StudioId`, which is correct here since this always runs inside a tenant-scoped
`OwnerOnly` request. No explicit override needed (matches `ClientProfileCrossTenantOptedIn`'s
commands, not `SuspendStudioCommand`'s issuer-cross-tenant case).

### 3f. New file — `Pena_e_Arte.Application/Clients/Validators/UpdateClientArtistValidator.cs`

```csharp
using FluentValidation;
using Pena_e_Arte.Application.Clients.Commands;

namespace Pena_e_Arte.Application.Clients.Validators;

public class UpdateClientArtistValidator : AbstractValidator<UpdateClientArtistCommand>
{
    public UpdateClientArtistValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.Request.ArtistId)
            .NotEqual(Guid.Empty)
            .When(x => x.Request.ArtistId.HasValue)
            .WithMessage("ArtistId cannot be empty.");
    }
}
```

### 3g. `Pena_e_Arte.Domain/Constants/AuditActions.cs`

Add one constant to `AuditActions` (`AuditTargetTypes.Client` already exists — no change needed
there):

```csharp
public const string ClientArtistReassigned = "Client.ArtistReassigned";
```

---

## Part 4 — API endpoint

### `Pena_e_Arte.API/Endpoints/ClientEndpoints.cs`

Add the route, placed with the other `{clientId:guid}`-scoped routes:

```csharp
group.MapPatch("{clientId:guid}/artist", UpdateClientArtist).RequireAuthorization("OwnerOnly");
```

And the handler method:

```csharp
private static async Task<IResult> UpdateClientArtist(
    Guid clientId,
    UpdateClientArtistRequest request,
    ISender mediator,
    CancellationToken ct)
{
    ClientResponse result = await mediator.Send(new UpdateClientArtistCommand(clientId, request), ct);
    return Results.Ok(result);
}
```

---

## Part 5 — Frontend (`frontend/src/features/clients`)

### 5a. `clientsApi.ts`

Update `ClientResponse`:

```typescript
export interface ClientResponse {
  id:         string;
  studioId:   string;
  firstName:  string;
  lastName:   string;
  email:      string;
  phone:      string | null;
  createdAt:  string;
  userId:     string | null;
  artistId:   string | null;
  artistName: string | null;
}
```

Update `CreateClientRequest`:

```typescript
export interface CreateClientRequest {
  firstName: string;
  lastName:  string;
  email:     string;
  phone:     string | null;
  artistId:  string;
}
```

Add:

```typescript
export interface UpdateClientArtistRequest {
  artistId: string | null;
}
```

Add a mutation (place alongside the other client mutations):

```typescript
updateClientArtist: builder.mutation<
  ClientResponse,
  { clientId: string; body: UpdateClientArtistRequest }
>({
  query: ({ clientId, body }) => ({
    url: `clients/${clientId}/artist`,
    method: "PATCH",
    body,
  }),
  invalidatesTags: (_result, _error, { clientId }) => [
    { type: "Client", id: clientId },
    "Client",
  ],
}),
```

Export `useUpdateClientArtistMutation` from the destructured hooks block at the bottom.

### 5b. `components/CreateClientPage.tsx`

Field order, top to bottom: First/Last name → Email → **Artist** (Owner/Issuer only) → Phone
(optional) → Submit.

- Import `useAppSelector` from `@/app/hooks`, `usePermission` (already there) and `Role` (already
  there), `Controller` from `react-hook-form` (add to the existing `useForm` import),
  `Select, SelectContent, SelectItem, SelectTrigger, SelectValue` from
  `@/shared/components/ui/select`, `useGetArtistsQuery` from `@/features/artists/artistsApi`, and
  `useGetMyArtistQuery` from `@/features/artists/artistsApi`.
- `const role = useAppSelector((s) => s.auth.role);`
- `const isOwnerPlus = usePermission(Role.Owner);`
- `const { data: artists, isLoading: loadingArtists } = useGetArtistsQuery(undefined, { skip: !isOwnerPlus });`
- `const { data: myArtist } = useGetMyArtistQuery(undefined, { skip: isOwnerPlus });`
- Extend the zod schema: `artistId: z.string().min(1, "Select an artist"),` — placed between `email`
  and `phone`.
- Destructure `control` and `setValue` from `useForm` (alongside the existing `register`,
  `handleSubmit`, `errors`).
- Add an effect to silently populate the field for the artist-role case:

  ```tsx
  useEffect(() => {
    if (!isOwnerPlus && myArtist) setValue("artistId", myArtist.id);
  }, [isOwnerPlus, myArtist, setValue]);
  ```

- Render, between the Email field and the Phone field:

  ```tsx
  {isOwnerPlus && (
    <div className="space-y-1.5">
      <Label htmlFor="artistId">Artist</Label>
      <Controller
        control={control}
        name="artistId"
        render={({ field }) => (
          <Select value={field.value} onValueChange={field.onChange}>
            <SelectTrigger
              id="artistId"
              aria-label="Select artist"
              className={cn(errors.artistId && "border-destructive")}
            >
              <SelectValue placeholder={loadingArtists ? "Loading artists…" : "Choose an artist"} />
            </SelectTrigger>
            <SelectContent>
              {artists?.map((a) => (
                <SelectItem key={a.id} value={a.id}>
                  {a.firstName} {a.lastName}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        )}
      />
      {errors.artistId && (
        <p className="text-xs text-destructive">{errors.artistId.message}</p>
      )}
    </div>
  )}
  {!isOwnerPlus && <input type="hidden" {...register("artistId")} />}
  ```

- Disable the submit button while the artist-role caller's own id hasn't resolved yet:
  `disabled={isLoading || (!isOwnerPlus && !myArtist)}`.
- `onSubmit`: include `artistId: values.artistId` in the `createClient(...)` call.

This deliberately uses a plain `SelectItem` list (name only, no avatar, no inline search box) rather
than `BookAppointmentForm.tsx`'s richer `ArtistSelectItem`/search-box pattern — this form is, and
should stay, a small single-column dialog-style page; a studio with enough artists to need in-select
search is better served by keeping this simple and revisiting only if that actually becomes a
problem.

### 5c. `components/ClientDetailPage.tsx`

- Add `UserRound` to the existing `lucide-react` import line.
- Import `useGetArtistsQuery` from `@/features/artists/artistsApi` and
  `useUpdateClientArtistMutation` from `../clientsApi`.
- `const isOwner = usePermission(Role.Owner);` (new — `canEdit` already exists at `Role.Artist` and
  must stay as-is, this is a separate, stricter gate).
- `const { data: artists } = useGetArtistsQuery(undefined, { skip: !isOwner });`
- `const [updateClientArtist, { isLoading: isReassigning }] = useUpdateClientArtistMutation();`
- Add a handler:

  ```tsx
  async function handleArtistChange(value: string) {
    if (!id) return;
    const result = await updateClientArtist({
      clientId: id,
      body: { artistId: value === "unassigned" ? null : value },
    });
    if ("error" in result) {
      toast.error("Failed to update assigned artist.");
      return;
    }
    toast.success("Assigned artist updated.");
  }
  ```

- In the identity `Card`, add a new row after the "Client since" row (still inside the same
  `border-t pt-1` block, or its own following row — match the existing spacing pattern):

  ```tsx
  <div className="flex items-center gap-2 text-sm">
    <UserRound className="h-4 w-4 shrink-0 text-muted-foreground" />
    {isOwner ? (
      <Select
        value={client.artistId ?? "unassigned"}
        onValueChange={handleArtistChange}
        disabled={isReassigning}
      >
        <SelectTrigger
          aria-label="Assigned artist"
          className="h-7 w-auto gap-1.5 border-none px-0 shadow-none text-sm"
        >
          <SelectValue placeholder="Unassigned" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="unassigned">Unassigned</SelectItem>
          {artists?.map((a) => (
            <SelectItem key={a.id} value={a.id}>
              {a.firstName} {a.lastName}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    ) : (
      <span>{client.artistName ?? "Unassigned"}</span>
    )}
  </div>
  ```

  Non-owners (artists) see plain read-only text — they can see who a client is assigned to, but
  cannot change it, consistent with decision #4's `OwnerOnly` gate.

### 5d. `components/ClientListPage.tsx`

- Add `useGetArtistsQuery` import from `@/features/artists/artistsApi`.
- New local state: `const [artistFilter, setArtistFilter] = useState<string>("all");`
- `const { data: artists } = useGetArtistsQuery(undefined);`
- Derive the filtered list (used everywhere `clients` currently drives rendering — `hasClients`,
  the `DataTable`'s `data`, and the count badge):

  ```tsx
  const filteredClients = (clients ?? []).filter((c) => {
    if (artistFilter === "all") return true;
    if (artistFilter === "unassigned") return c.artistId === null;
    return c.artistId === artistFilter;
  });
  ```

- Add a `Select` next to the search `Input` (same flex row, e.g. wrap both in a
  `<div className="flex gap-2">`):

  ```tsx
  <Select value={artistFilter} onValueChange={setArtistFilter}>
    <SelectTrigger className="w-[180px]" aria-label="Filter by artist">
      <SelectValue placeholder="All artists" />
    </SelectTrigger>
    <SelectContent>
      <SelectItem value="all">All artists</SelectItem>
      <SelectItem value="unassigned">Unassigned</SelectItem>
      {artists?.map((a) => (
        <SelectItem key={a.id} value={a.id}>
          {a.firstName} {a.lastName}
        </SelectItem>
      ))}
    </SelectContent>
  </Select>
  ```

  Add the `Select`/`SelectContent`/`SelectItem`/`SelectTrigger`/`SelectValue` import from
  `@/shared/components/ui/select`.

- Add an "Artist" column to the `DataTable`, between "Phone" and the actions column:

  ```tsx
  {
    header: "Artist",
    cell: (c) =>
      c.artistName ?? (
        <span aria-label="Unassigned" className="text-muted-foreground/50">
          —
        </span>
      ),
  },
  ```

  Add the artist name to the `mobileCard` renderer too (append to the existing
  `{c.firstName} {c.lastName}` / email+phone line, e.g. `· {c.artistName ?? "Unassigned"}`).

- Every place the component currently reads `clients` for rendering purposes (not for the count
  badge in the header if you want that to reflect total-in-studio rather than post-filter — use
  your judgement, but the count badge is a small detail; using `filteredClients.length` there is
  fine and arguably more useful) should switch to `filteredClients`. `hasClients`, `emptyMessage`,
  and the empty-state block's visibility conditions all need to account for the filter being active
  in addition to `search` (a filter set to a specific artist with zero matches is a form of "no
  results", same as an unmatched search).

---

## Part 6 — Tests

### Backend

- `tests/Pena_e_Arte.UnitTests/Clients/CreateClientHandlerTests.cs` — update every existing test to
  supply `ArtistId` in `CreateClientRequest` and seed an `Artist` fixture. Add new cases:
  - Owner-role caller: the artist supplied in the request is used as-is.
  - Artist-role caller: a request `artistId` for a *different* artist is silently overridden with
    the caller's own artist id (assert the persisted `Client.ArtistId`, not just that no exception
    was thrown).
  - Artist-role caller with no matching `Artist` record for their `UserId` → `ForbiddenException`.
  - Non-existent `ArtistId` → `NotFoundException`.
  - `IsActive == false` artist → `BusinessRuleViolationException`.
  - Response includes `ArtistName` as `"{FirstName} {LastName}"`.
- `tests/Pena_e_Arte.UnitTests/Clients/CreateClientValidatorTests.cs` — add a case asserting
  `Guid.Empty` (or omitted) `ArtistId` fails validation.
- `tests/Pena_e_Arte.UnitTests/Clients/GetClientsHandlerTests.cs` — update fixtures/assertions to
  cover `ArtistId`/`ArtistName` being populated when a client has an assigned artist, and both
  `null` when not.
- New — `tests/Pena_e_Arte.UnitTests/Clients/UpdateClientArtistHandlerTests.cs`: reassign to a
  different artist; unassign (`ArtistId: null`); 404 on missing client; 404 on missing artist;
  business-rule violation on inactive artist; asserts `client.UpdatedAt` changed.
- New — `tests/Pena_e_Arte.UnitTests/Clients/UpdateClientArtistValidatorTests.cs`.
- `tests/Pena_e_Arte.IntegrationTests/Application/ClientHandlerIntegrationTests.cs` — update any
  `CreateClientCommand`/`CreateClientRequest` construction to include `ArtistId`. Add an
  integration test hitting `PATCH /api/v1/clients/{clientId}/artist` end-to-end, asserting a
  200 for an owner-authenticated request and a 403 for an artist-authenticated one (the endpoint's
  `OwnerOnly` gate).
- Grep the whole `tests/` tree for `new CreateClientRequest(` and `new ClientResponse(` and fix any
  remaining call sites the compiler flags — do this by running `dotnet build` on the test projects
  and working through the errors, not by trying to enumerate every occurrence by hand up front.

### Frontend

- `__tests__/CreateClientPage.test.tsx`:
  - Add an MSW handler for `GET http://localhost/api/v1/artists` returning a small fixture list
    (at least one artist) — the page now calls this for the owner-role case.
  - Add an MSW handler for `GET http://localhost/api/v1/artists/me` returning a single artist
    fixture — needed for the artist-role case.
  - Update the existing `POST /api/v1/clients` handler to read/echo `artistId` from the request
    body and include `artistId`/`artistName` in the response fixture.
  - `renders the form fields` (owner preloaded state, already the default in `makeStore()`): also
    assert the Artist select is present.
  - `shows validation errors when submitting empty form`: also assert an artist-selection error
    appears.
  - The two "submitting a valid form" tests and the success/error-toast tests: select an artist
    (via the select trigger + option click, or `userEvent.selectOptions`-equivalent for this UI
    kit — match however other tests in this repo already drive this `Select` component, e.g.
    `BookPage.test.tsx`) before submitting.
  - Add a new test with `role: "artist"` in `preloadedState`: assert the Artist select is **not**
    rendered, and that submitting the form (without ever touching an artist field) still succeeds,
    confirming the hidden auto-fill worked.
- `__tests__/ClientListPage.test.tsx` — update `ClientResponse` fixtures to include
  `artistId`/`artistName`; add a test selecting a specific artist from the new filter and asserting
  the table only shows that artist's clients, plus a test for the "Unassigned" filter option.
- `__tests__/ClientDetailPage.test.tsx` — update fixtures; add a test asserting an owner sees an
  editable `Select` for the assigned artist and changing it calls the mutation and shows a success
  toast; add a test asserting a non-owner (artist) sees plain read-only text instead.
- `__tests__/clients.test.tsx` — update any `ClientResponse`-shaped fixtures for the two new fields;
  this file's actual scope should be checked before assuming what else needs touching.
- Run `pnpm tsc --noEmit` and the full `pnpm test` after all of the above. `ClientResponse` gained
  two required (non-optional) TypeScript fields, so any other test file across the frontend that
  constructs a `ClientResponse`-shaped fixture (check `BookAppointmentForm`/`BookPage`,
  `CreateDesignPage`, and `CreatePaymentIntentPage` test suites, since all three call
  `useGetClientsQuery`) may need the same two fields added to its fixtures. Fix whatever the type
  checker and test run surface — do not try to enumerate every file by hand up front.

---

## Part 7 — Help Menu, user manual, onboarding tour

Per `CLAUDE.md` rule 7, this feature is not done until all three surfaces reflect it.

### 7a. `frontend/src/features/help/helpContent.ts`

- `owner-clients-add` (id, existing): update `steps` to insert, after the email step and before the
  phone step:
  `"Select the Artist this client belongs to. (If you're an artist adding your own client, this
  step doesn't apply — it's assigned to you automatically.)"`. Add `"assign artist"`,
  `"preferred artist"` to `keywords`.
- `owner-clients-list` (id, existing): add a step: `"Use the Artist filter to narrow the list to
  one artist's clients, or \"Unassigned\" for clients with no artist yet."`
- New article, placed near the other `owner-clients-*` entries:

  ```typescript
  {
    id: "owner-clients-reassign-artist",
    roles: [Owner],
    title: "Reassign a client to a different artist",
    route: "/clients",
    keywords: ["reassign artist", "change artist", "unassign client"],
    summary: "Change which artist a client belongs to, or unassign them, from the client's profile.",
    steps: [
      "Open the client's profile from the Clients list.",
      "Click the artist name shown near the top of the profile to open the dropdown.",
      "Choose a different artist, or choose \"Unassigned\".",
      "The change saves immediately — no separate save button.",
    ],
  },
  ```

- `artist-client-detail` (id, existing, Artist role): add one line to `summary` or a short new
  step noting the client's assigned artist is now shown (read-only for this role) near the top of
  the profile.

### 7b. `frontend/public/user-manual/index.html`

- `#owner-create-client` section: update the wireframe SVG to add an Artist field between Email and
  Phone (a labeled `<rect>` matching the existing field styling), update the figcaption's field list
  (`"first/last name, email, artist, phone"`), and add a step: `"Choose the Artist this client
  belongs to."` between the email step and the phone step. Note in the steps or a short paragraph
  that an artist-role caller creating their own client does not see this field.
- `#owner-clients` section: mention the new artist filter in its `<p>` description and steps.
- `#owner-client-detail` section: add one sentence noting the one owner-only difference from the
  artist view — the assigned-artist control is editable, not just displayed.
- `#artist-client-detail` (if a matching manual section exists under the Artist role — verify by
  searching for its `id`; the earlier grep for this pass found `artist-clients` and
  `artist-client-detail` sections both exist) — add one sentence noting the assigned artist is
  shown (read-only) near the top of the client's profile.
- Do **not** touch the `artist-clients` section's access table — see the explicitly-out-of-scope
  note in the Decisions table above.

### 7c. Onboarding tours

Checked `frontend/src/features/help/tours/ownerTour.ts` and `artistTour.ts` — neither currently has
a step that walks through client creation or the client detail page at all (the only clients-adjacent
tour content found was a deposit-rules step, unrelated). **No tour changes are needed for this
prompt** — stated explicitly here rather than left ambiguous, per this project's own convention of
recording genuine no-op findings rather than silently skipping them.

---

## Definition of done

- [ ] Migration applied cleanly; `dotnet ef database update` succeeds; app boots.
- [ ] `dotnet build` — zero errors.
- [ ] `dotnet test` — all green (pre-existing failures noted at pre-flight excluded), including the
      new `UpdateClientArtistHandlerTests`/`UpdateClientArtistValidatorTests` and the updated
      `CreateClientHandlerTests`/`CreateClientValidatorTests`/`GetClientsHandlerTests`/integration test.
- [ ] `pnpm tsc --noEmit` — zero errors.
- [ ] `pnpm test` — all green, including the updated Clients suite and any other suite touched by
      `ClientResponse`'s new fields.
- [ ] Manual smoke check (or an added integration/component test covering it): an owner creating a
      client is required to pick an artist and cannot submit without one; an artist creating a
      client never sees the field and the resulting client is assigned to them; an owner can
      reassign or unassign a client's artist from the detail page; an artist attempting the PATCH
      endpoint directly gets 403; the Clients list filter and column both reflect assignment
      correctly, including "Unassigned".
- [ ] `helpContent.ts`, `user-manual/index.html` updated per Part 7; onboarding tours confirmed
      (not just assumed) to need no change.
