# Overnight Prompt — Owner-as-Artist (Dual-Role Studio Owner)

**Reported request:** "When an owner is also an artist, he should be able to use the platform
features as both an owner and an artist." Today there is no way for a studio owner to be
bookable, schedulable, or shown on the public portfolio as staff under their own login.

**Product decisions locked in for this run** (see `spec-plan-owner-artist-dual-role-2026-08-21.md`
for the full trade-off discussion — do not re-litigate these, just implement them):

1. The owner's own artist seat **counts** against the studio's plan Artist quota
   (`IQuotaCheckedCommand`, same as inviting any other artist).
2. The "Delete" action on the owner's own linked artist profile is relabeled "Stop working as an
   artist" with adjusted confirmation copy.
3. A new `ownerTour.ts` step is added for the new CTA.

---

## Diagnosis (read this fully before touching any file)

Verified against the live repo:

- `Role` (`Pena_e_Arte.Domain/Enums/Role.cs`) is `Client | Artist | Owner | Issuer`, one role per
  Identity user, assigned once at account creation. Nothing in the codebase ever grants a second
  role to an existing user, and this change does **not** introduce that — the owner keeps their
  single `"owner"` role throughout.
- RBAC already treats `owner` as a superset of `artist`:
  `"ArtistAndAbove" = RequireRole("artist", "owner", "issuer")`
  (`Pena_e_Arte.API/Extensions/AuthorizationExtensions.cs`). Frontend `usePermission`
  (`frontend/src/shared/hooks/usePermission.ts`) ranks `Owner(2) > Artist(1)` identically. The
  gap is not authorization — it's that no `Artist` row can ever get linked to an owner's own
  account.
- The only place an `Artist` row is created, `CreateArtistCommand`
  (`Pena_e_Arte.Application/Artists/Commands/CreateArtistCommand.cs`), always calls
  `identity.CreateUserAsync(email, tempPassword, "artist", ...)` to mint a **new** Identity
  account. As of today's `overnight-prompt-artist-invite-cross-tenant-fix-2026-08-21.md`, its one
  fallback path (reusing an existing account on "email already taken") now explicitly **rejects**
  any account that isn't an orphaned same-studio artist — so an owner's own email is now a hard
  `422` there, not a usable path. **This prompt adds a wholly separate command that never calls
  `identity.CreateUserAsync` at all**, so it cannot interact with or regress that fix.
- Every "does the caller own this artist" guard in the codebase branches on
  `if (currentUser.Role == "artist")` and leaves `owner` unrestricted
  (`ConfirmCashDepositCommand`, `UpsertArtistScheduleCommand`, `DeleteArtistTimeOffCommand`,
  `AddArtistTimeOffCommand`). Once the owner has a linked `Artist` row, these already grant full
  access with **zero changes**.
- `GetMyArtistHandler` (`GET /api/v1/artists/me`, already `ArtistAndAbove`) resolves purely by
  `a.UserId == currentUser.UserId` — no role check. Works for an owner the moment the row exists.
- `GetArtistsQuery`, the public portfolio, and the booking artist-picker all query `db.Artists`
  scoped to tenant + `IsActive`, with no role filter — a new row surfaces automatically.
- `DeleteArtistHandler` only ever sets `artist.DeletedAt` — it never calls into
  `IIdentityService`. Removing the owner's linked profile can never lock them out of their own
  account. No change needed to make this safe.
- `Artist.UserId` (`Pena_e_Arte.Domain/Entities/Artist.cs`) is already a nullable `Guid` with no
  FK/role constraint. **No migration is needed.**

### Why a new command instead of reusing `CreateArtistCommand`

`CreateArtistCommand`'s request carries an `Email` the caller supplies — that's what today's fix
had to guard, and reusing it here would mean re-deriving "is this actually the caller's own
email" from a user-editable field. The new command below never accepts an email at all — it
always uses `currentUser.Email` — which is what makes it structurally incapable of becoming
another "link an arbitrary account" backdoor.

---

## Files to Change

| # | File | Change |
|---|------|--------|
| 1 | `Pena_e_Arte.Contracts/Requests/CreateOwnArtistProfileRequest.cs` | New request DTO |
| 2 | `Pena_e_Arte.Application/Artists/Commands/CreateOwnArtistProfileCommand.cs` | New command + validator + handler |
| 3 | `Pena_e_Arte.API/Endpoints/ArtistEndpoints.cs` | Add `POST /api/v1/artists/me` (`OwnerOnly`) |
| 4 | `Pena_e_Arte.Application/Artists/Commands/ResendArtistInviteCommand.cs` | Guard against resending on the owner's own linked profile |
| 5 | `tests/Pena_e_Arte.UnitTests/Artists/CreateOwnArtistProfileHandlerTests.cs` | New tests |
| 6 | `tests/Pena_e_Arte.UnitTests/Artists/ResendArtistInviteHandlerTests.cs` | New tests for the guard |
| 7 | `frontend/src/features/artists/artistsApi.ts` | New `createOwnArtistProfile` mutation |
| 8 | `frontend/src/features/artists/index.ts` | Export the new hook |
| 9 | `frontend/src/features/artists/components/ArtistListPage.tsx` | "Become an artist" CTA + dialog for owners with no linked profile |
| 10 | `frontend/src/features/artists/components/ArtistDetailPage.tsx` | Hide "Resend invite" and relabel "Delete" for the owner's own linked profile |
| 11 | `frontend/src/layouts/OwnerLayout.tsx` | "My Portfolio" nav shortcut once a linked profile exists (mirrors `ArtistLayout`) |
| 12 | `frontend/src/features/help/helpContent.ts` | New `owner-become-artist` article; touch up `owner-artists-list` tips |
| 13 | `frontend/src/features/help/tours/ownerTour.ts` | New tour step |
| 14 | `frontend/public/user-manual/index.html` | New "Enable your own artist profile" section |
| 15 | `docs/claude/architecture.md` | Append one row to the Decisions Log |

---

## Step 1 — `Pena_e_Arte.Contracts/Requests/CreateOwnArtistProfileRequest.cs` (new file)

```csharp
namespace Pena_e_Arte.Contracts.Requests;

public record CreateOwnArtistProfileRequest(
    string FirstName,
    string LastName,
    string? Specializations,
    decimal? HourlyRate = null);
```

No `Email` field — the handler always uses the caller's own `ICurrentUser.Email`. Do not add one.

---

## Step 2 — `Pena_e_Arte.Application/Artists/Commands/CreateOwnArtistProfileCommand.cs` (new file)

Read `CreateArtistCommand.cs` first — this mirrors its slug-generation and mapping conventions
exactly.

```csharp
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Utilities;

namespace Pena_e_Arte.Application.Artists.Commands;

public record CreateOwnArtistProfileCommand(CreateOwnArtistProfileRequest Request)
    : IRequest<ArtistResponse>, IQuotaCheckedCommand
{
    public QuotaType QuotaType => QuotaType.Artists;
}

public class CreateOwnArtistProfileValidator : AbstractValidator<CreateOwnArtistProfileCommand>
{
    public CreateOwnArtistProfileValidator()
    {
        RuleFor(x => x.Request.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.HourlyRate).InclusiveBetween(0.01m, 10_000m)
            .When(x => x.Request.HourlyRate is not null);
        RuleFor(x => x.Request.Specializations).MaximumLength(1000)
            .When(x => x.Request.Specializations is not null);
    }
}

public class CreateOwnArtistProfileHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    ICurrentUser currentUser,
    IPlanLimitService planLimits)
    : IRequestHandler<CreateOwnArtistProfileCommand, ArtistResponse>
{
    public async Task<ArtistResponse> Handle(CreateOwnArtistProfileCommand command, CancellationToken ct)
    {
        CreateOwnArtistProfileRequest req = command.Request;

        bool alreadyHasProfile = await db.Artists.AnyAsync(a => a.UserId == currentUser.UserId, ct);
        if (alreadyHasProfile)
            throw new BusinessRuleViolationException("You already have an artist profile.");

        string baseSlug = SlugHelper.GenerateSlug($"{req.FirstName} {req.LastName}");
        string slug = baseSlug;
        int counter = 2;
        // IgnoreQueryFilters: slug must be globally unique for public portfolio URLs — same
        // rule CreateArtistHandler enforces for an invited artist.
        while (await db.Artists.IgnoreQueryFilters().AnyAsync(a => a.Slug == slug && a.DeletedAt == null, ct))
        {
            slug = $"{baseSlug}-{counter}";
            counter++;
        }

        // No identity.CreateUserAsync call, no AddToRoleAsync, no EnqueueArtistInvite — the
        // owner already has full credentials and a login. This links the existing account,
        // it never creates one.
        Artist artist = new()
        {
            StudioId = tenant.StudioId,
            UserId = currentUser.UserId,
            FirstName = req.FirstName,
            LastName = req.LastName,
            Email = currentUser.Email!,
            Specializations = req.Specializations,
            HourlyRate = req.HourlyRate
        };
        artist.SetSlug(slug);

        db.Artists.Add(artist);
        await db.SaveChangesAsync(ct);

        await planLimits.InvalidateUsageCacheAsync(QuotaType.Artists, ct);

        return CreateArtistHandler.Map(artist);
    }
}
```

`CreateArtistHandler.Map` is already `internal static` in the same project/namespace area — no
visibility change needed, confirm by reading `CreateArtistCommand.cs` before assuming.

---

## Step 3 — `Pena_e_Arte.API/Endpoints/ArtistEndpoints.cs`

Read the file first. Add the route directly after the existing `CreateArtist` mapping (note `GET
/me` already exists at the same path with a different verb — no conflict):

```csharp
group.MapPost("/", CreateArtist).RequireAuthorization("OwnerOnly");
group.MapPost("me", CreateOwnArtistProfile).RequireAuthorization("OwnerOnly");   // ← add this line
```

And add the handler method (near `CreateArtist`'s handler):

```csharp
private static async Task<IResult> CreateOwnArtistProfile(
    CreateOwnArtistProfileRequest request,
    ISender mediator,
    CancellationToken ct)
{
    ArtistResponse result = await mediator.Send(new CreateOwnArtistProfileCommand(request), ct);
    return Results.Created($"/api/v1/artists/{result.Id}", result);
}
```

Add `using Pena_e_Arte.Contracts.Requests;`/`Contracts.Responses` are already imported at the top
of this file — confirm before adding a duplicate.

---

## Step 4 — `Pena_e_Arte.Application/Artists/Commands/ResendArtistInviteCommand.cs`

Read the file first (it's short). Replace its handler in full:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Artists.Commands;

public record ResendArtistInviteCommand(Guid Id) : IRequest;

public class ResendArtistInviteHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    IJobScheduler scheduler,
    IIdentityService identity)
    : IRequestHandler<ResendArtistInviteCommand>
{
    public async Task Handle(ResendArtistInviteCommand command, CancellationToken ct)
    {
        Artist? artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == command.Id, ct);
        if (artist is null)
            throw new NotFoundException(nameof(Artist), command.Id);

        // The owner's own linked artist profile (see CreateOwnArtistProfileCommand) was never
        // created via an invite email — there's nothing to resend, and sending one would be a
        // confusing "set your password" email to someone who already has full credentials.
        // Reuses IIdentityService.GetUserRolesAsync, added by the 2026-08-21 cross-tenant fix.
        if (artist.UserId is not null)
        {
            IReadOnlyList<string> roles = await identity.GetUserRolesAsync(artist.UserId.Value, ct);
            if (roles.Contains("owner"))
                throw new BusinessRuleViolationException(
                    "This artist profile belongs to the studio owner's own account — there is no invite to resend.");
        }

        scheduler.EnqueueArtistInvite(artist.Email, artist.FirstName, tenant.StudioId);
    }
}
```

This adds a new constructor parameter (`IIdentityService identity`) — update every existing
caller/test constructor accordingly (see Step 6).

---

## Step 5 — Tests: `tests/Pena_e_Arte.UnitTests/Artists/CreateOwnArtistProfileHandlerTests.cs` (new file)

Read `CreateArtistHandlerTests.cs` and `tests/Pena_e_Arte.UnitTests/Helpers/FakeCurrentUser.cs`
first to confirm these conventions still match before using them.

```csharp
using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Artists;

public class CreateOwnArtistProfileHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IPlanLimitService _planLimits = Substitute.For<IPlanLimitService>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly FakeCurrentUser _owner;

    public CreateOwnArtistProfileHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _owner = new FakeCurrentUser(Guid.NewGuid(), "owner", "owner@studio.com");
    }

    private CreateOwnArtistProfileHandler CreateSut() => new(_db, _tenant, _owner, _planLimits);

    [Fact]
    public void CreateOwnArtistProfileCommand_IsQuotaCheckedForArtists()
    {
        IQuotaCheckedCommand command =
            new CreateOwnArtistProfileCommand(new CreateOwnArtistProfileRequest("A", "B", null));

        command.QuotaType.Should().Be(QuotaType.Artists);
    }

    [Fact]
    public async Task Handle_NoExistingProfile_CreatesArtistLinkedToCallersOwnUserId()
    {
        CreateOwnArtistProfileRequest req = new("Rui", "Tavares", "Neo-traditional", 90m);

        ArtistResponse result = await CreateSut().Handle(new CreateOwnArtistProfileCommand(req), default);

        result.UserId.Should().Be(_owner.UserId);
        result.Email.Should().Be("owner@studio.com");
        result.StudioId.Should().Be(_studioId);
        result.FirstName.Should().Be("Rui");
        result.Specializations.Should().Be("Neo-traditional");
        result.HourlyRate.Should().Be(90m);
    }

    [Fact]
    public async Task Handle_NoExistingProfile_PersistsArtistToDb()
    {
        await CreateSut().Handle(
            new CreateOwnArtistProfileCommand(new("Rui", "Tavares", null)), default);

        _db.Artists.Should().ContainSingle(a => a.UserId == _owner.UserId && a.Email == "owner@studio.com");
    }

    [Fact]
    public async Task Handle_AlreadyHasProfile_ThrowsBusinessRuleViolationException()
    {
        _db.Artists.Add(new Artist
        {
            StudioId = _studioId, UserId = _owner.UserId,
            FirstName = "Rui", LastName = "Tavares", Email = "owner@studio.com",
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut()
            .Handle(new CreateOwnArtistProfileCommand(new("Rui", "Tavares", null)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_AlreadyHasProfile_DoesNotPersistSecondArtist()
    {
        _db.Artists.Add(new Artist
        {
            StudioId = _studioId, UserId = _owner.UserId,
            FirstName = "Rui", LastName = "Tavares", Email = "owner@studio.com",
        });
        await _db.SaveChangesAsync();

        try
        {
            await CreateSut().Handle(new CreateOwnArtistProfileCommand(new("Rui", "Tavares", null)), default);
        }
        catch { }

        _db.Artists.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_NoExistingProfile_InvalidatesArtistsUsageCache()
    {
        await CreateSut().Handle(
            new CreateOwnArtistProfileCommand(new("Rui", "Tavares", null)), default);

        await _planLimits.Received(1)
            .InvalidateUsageCacheAsync(QuotaType.Artists, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateSlugSource_AppendsSuffixForUniqueness()
    {
        _db.Artists.Add(new Artist
        {
            StudioId = Guid.NewGuid(), UserId = Guid.NewGuid(),
            FirstName = "Rui", LastName = "Tavares", Email = "someone-else@other-studio.com",
        });
        // Force the same base slug an existing (different-studio) artist already holds, to
        // exercise the global-uniqueness loop the same way CreateArtistHandlerTests does.
        _db.Artists.First().GetType().GetMethod("SetSlug")!.Invoke(_db.Artists.First(), ["rui-tavares"]);
        await _db.SaveChangesAsync();

        ArtistResponse result = await CreateSut().Handle(
            new CreateOwnArtistProfileCommand(new("Rui", "Tavares", null)), default);

        result.Slug.Should().Be("rui-tavares-2");
    }
}
```

The last test uses reflection to call the internal `SetSlug` setter on a seeded artist purely to
exercise the collision path — if `CreateArtistHandlerTests.cs` already has a cleaner established
pattern for seeding a colliding slug (check for one before assuming reflection is necessary), use
that pattern instead for consistency.

---

## Step 6 — Tests: `tests/Pena_e_Arte.UnitTests/Artists/ResendArtistInviteHandlerTests.cs`

Read the file first (reproduced in the Diagnosis section above — it currently constructs
`ResendArtistInviteHandler` with 3 args). Update the constructor call and add the identity
substitute:

```csharp
private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
...
private ResendArtistInviteHandler CreateSut() => new(_db, _tenant, _scheduler, _identity);
```

Existing tests seed an artist with no `UserId` set (`SeedArtist` doesn't set one) — since the new
guard only runs `if (artist.UserId is not null)`, those tests are unaffected and need no other
change. Add:

```csharp
[Fact]
public async Task Handle_ArtistIsOwnersOwnLinkedProfile_ThrowsBusinessRuleViolationException()
{
    Guid ownerUserId = Guid.NewGuid();
    Artist artist = new()
    {
        StudioId = _studioId, UserId = ownerUserId,
        FirstName = "Rui", LastName = "Tavares", Email = "owner@studio.com",
    };
    _db.Artists.Add(artist);
    await _db.SaveChangesAsync();
    _identity.GetUserRolesAsync(ownerUserId, Arg.Any<CancellationToken>())
        .Returns(new List<string> { "owner" });

    Func<Task> act = () => CreateSut().Handle(new ResendArtistInviteCommand(artist.Id), default);

    await act.Should().ThrowAsync<BusinessRuleViolationException>();
}

[Fact]
public async Task Handle_ArtistIsOwnersOwnLinkedProfile_DoesNotEnqueueInvite()
{
    Guid ownerUserId = Guid.NewGuid();
    Artist artist = new()
    {
        StudioId = _studioId, UserId = ownerUserId,
        FirstName = "Rui", LastName = "Tavares", Email = "owner@studio.com",
    };
    _db.Artists.Add(artist);
    await _db.SaveChangesAsync();
    _identity.GetUserRolesAsync(ownerUserId, Arg.Any<CancellationToken>())
        .Returns(new List<string> { "owner" });

    try { await CreateSut().Handle(new ResendArtistInviteCommand(artist.Id), default); } catch { }

    _scheduler.DidNotReceiveWithAnyArgs().EnqueueArtistInvite(default!, default!, default);
}

[Fact]
public async Task Handle_ArtistIsRegularArtistAccount_StillEnqueuesInvite()
{
    Guid artistUserId = Guid.NewGuid();
    Artist artist = new()
    {
        StudioId = _studioId, UserId = artistUserId,
        FirstName = "Rui", LastName = "Tavares", Email = "artist@studio.com",
    };
    _db.Artists.Add(artist);
    await _db.SaveChangesAsync();
    _identity.GetUserRolesAsync(artistUserId, Arg.Any<CancellationToken>())
        .Returns(new List<string> { "artist" });

    await CreateSut().Handle(new ResendArtistInviteCommand(artist.Id), default);

    _scheduler.Received(1).EnqueueArtistInvite("artist@studio.com", "Rui", _studioId);
}
```

---

## Step 7 — `frontend/src/features/artists/artistsApi.ts`

Read the file first. Add alongside the existing `CreateArtistRequest` interface:

```ts
export interface CreateOwnArtistProfileRequest {
  firstName:       string;
  lastName:        string;
  specializations: string | null;
  hourlyRate:      number | null;
}
```

Add the mutation inside `endpoints: (builder) => ({ ... })`, directly after `createArtist`:

```ts
createOwnArtistProfile: builder.mutation<ArtistResponse, CreateOwnArtistProfileRequest>({
  query: (body) => ({ url: "artists/me", method: "POST", body }),
  invalidatesTags: ["Artist"],
}),
```

Add to the exports block at the bottom:

```ts
export const {
  useGetMyArtistQuery,
  useCreateArtistMutation,
  useCreateOwnArtistProfileMutation,   // ← add this line
  ...
} = artistsApi;
```

---

## Step 8 — `frontend/src/features/artists/index.ts`

Read the file first (12 lines). Add the new hook and type to the existing export blocks:

```ts
export {
  artistsApi,
  useCreateArtistMutation,
  useCreateOwnArtistProfileMutation,   // ← add this line
  useGetArtistsQuery,
  useGetArtistByIdQuery,
  useUpdateArtistMutation,
  useDeleteArtistMutation,
} from "./artistsApi";
export type { ArtistResponse, CreateArtistRequest, CreateOwnArtistProfileRequest, UpdateArtistRequest } from "./artistsApi";
```

---

## Step 9 — `frontend/src/features/artists/components/ArtistListPage.tsx`

Read the file in full first (reproduced in the Diagnosis research for this prompt — 367 lines,
plain `useState`-based, no `react-hook-form`). Add:

1. Imports: `useCreateOwnArtistProfileMutation` from `"../artistsApi"`, `useGetMyArtistQuery` from
   the same, `Dialog`, `DialogContent`, `DialogHeader`, `DialogTitle`, `DialogFooter`,
   `DialogDescription` from `"@/shared/components/ui/dialog"`, `Label` from
   `"@/shared/components/ui/label"` (already imports `Input`), and `useAppSelector` from
   `"@/app/hooks"`.

2. Inside `ArtistListPage()`, alongside the existing `canManage`/query hooks:

```tsx
const currentUserName = useAppSelector((s) => s.auth.user?.name);
const { data: myArtist, isLoading: myArtistLoading } = useGetMyArtistQuery(undefined, { skip: !canManage });
const [createOwnArtistProfile, { isLoading: isEnabling }] = useCreateOwnArtistProfileMutation();
const [becomeArtistOpen, setBecomeArtistOpen] = useState(false);
const [baFirstName, setBaFirstName]           = useState(currentUserName ?? "");
const [baLastName, setBaLastName]             = useState("");
const [baSpecializations, setBaSpecializations] = useState("");
const [baHourlyRate, setBaHourlyRate]         = useState("");

async function onEnableOwnArtistProfile() {
  try {
    const result = await createOwnArtistProfile({
      firstName:       baFirstName.trim(),
      lastName:        baLastName.trim(),
      specializations: baSpecializations.trim() || null,
      hourlyRate:      baHourlyRate.trim() ? Number(baHourlyRate) : null,
    }).unwrap();
    toast.success("Your artist profile is ready.");
    setBecomeArtistOpen(false);
    navigate(`/artists/${result.id}`);
  } catch (err: unknown) {
    const message =
      (err as { data?: { message?: string } } | undefined)?.data?.message
      ?? "Failed to enable your artist profile.";
    toast.error(message);
  }
}
```

3. In the JSX, directly below the search `<Input>` block and before the loading/empty/table
   states, add the CTA card — shown only for an owner with no linked profile, once that query has
   resolved:

```tsx
{canManage && !myArtistLoading && !myArtist && (
  <div className="flex items-center justify-between gap-3 rounded-lg border bg-muted/30 px-4 py-3">
    <div>
      <p className="text-sm font-medium">Also work as an artist?</p>
      <p className="text-xs text-muted-foreground">
        Enable your own artist profile — no second account needed.
      </p>
    </div>
    <Button
      size="sm"
      variant="outline"
      data-tour="owner-become-artist-cta"
      onClick={() => setBecomeArtistOpen(true)}
    >
      Enable my artist profile
    </Button>
  </div>
)}
```

4. Add the dialog near the bottom of the returned JSX, as a sibling of the root `<div>`'s closing
   (mirroring how `ArtistDetailPage.tsx` places its delete-confirmation `<Dialog>` just before its
   own closing `</div>`):

```tsx
<Dialog open={becomeArtistOpen} onOpenChange={setBecomeArtistOpen}>
  <DialogContent>
    <DialogHeader>
      <DialogTitle>Enable your artist profile</DialogTitle>
      <DialogDescription>
        This uses your existing owner login — no new email or password. You'll be selectable
        when booking or scheduling appointments, just like any other artist.
      </DialogDescription>
    </DialogHeader>
    <div className="space-y-3">
      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <Label htmlFor="ba-first-name">First name</Label>
          <Input id="ba-first-name" value={baFirstName} onChange={(e) => setBaFirstName(e.target.value)} />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="ba-last-name">Last name</Label>
          <Input id="ba-last-name" value={baLastName} onChange={(e) => setBaLastName(e.target.value)} />
        </div>
      </div>
      <div className="space-y-1.5">
        <Label htmlFor="ba-specializations">Specializations (optional)</Label>
        <Input
          id="ba-specializations"
          placeholder="e.g. Traditional, Realism"
          value={baSpecializations}
          onChange={(e) => setBaSpecializations(e.target.value)}
        />
      </div>
      <div className="space-y-1.5">
        <Label htmlFor="ba-hourly-rate">Hourly rate (€, optional)</Label>
        <Input
          id="ba-hourly-rate"
          type="number"
          step="0.01"
          min="0"
          value={baHourlyRate}
          onChange={(e) => setBaHourlyRate(e.target.value)}
        />
      </div>
    </div>
    <DialogFooter>
      <Button variant="outline" onClick={() => setBecomeArtistOpen(false)} disabled={isEnabling}>
        Cancel
      </Button>
      <Button
        onClick={() => void onEnableOwnArtistProfile()}
        disabled={isEnabling || !baFirstName.trim() || !baLastName.trim()}
      >
        {isEnabling ? "Enabling…" : "Enable my artist profile"}
      </Button>
    </DialogFooter>
  </DialogContent>
</Dialog>
```

Note: `toast` is already imported in this file (used by the existing delete-confirm flow) —
confirm before adding a duplicate import.

---

## Step 10 — `frontend/src/features/artists/components/ArtistDetailPage.tsx`

Read the file in full first (736 lines). Two targeted changes — `isOwnProfile` is **already
computed** at line ~144 (`isArtistRole && artist?.userId != null && artist.userId ===
currentUserId`) and already evaluates `true` for an owner viewing their own linked profile,
because `isArtistRole = usePermission(Role.Artist)` and `usePermission` ranks `Owner(2) >=
Artist(1)` — confirm this by reading `usePermission.ts` before assuming, but do not change that
computation; it already works correctly for this feature with zero edits.

**10a.** Around line 308–316 (the header action row), relabel the "Delete" button:

```tsx
{canManage && (
  <Button
    variant="outline"
    size="sm"
    onClick={() => setDeleteOpen(true)}
    className="gap-1.5 text-destructive hover:text-destructive"
  >
    <Trash2 className="h-3.5 w-3.5" />
    {isOwnProfile ? "Stop working as an artist" : "Delete"}
  </Button>
)}
```

**10b.** Around line 461–474 (the profile tab's "Resend invite" button), hide it entirely for the
owner's own linked profile:

```tsx
{canManage && !isOwnProfile && (
  <Button
    variant="ghost"
    size="sm"
    className="gap-1.5 shrink-0 text-xs h-7"
    disabled={isResending}
    onClick={() => void onResendInvite()}
  >
    {isResending ? (
      <Loader2 className="h-3.5 w-3.5 animate-spin" />
    ) : (
      <Send className="h-3.5 w-3.5" />
    )}
    Resend invite
  </Button>
)}
```

**10c.** Around line 703–730 (the delete confirmation `Dialog`), adjust the title, description,
and confirm-button label:

```tsx
<Dialog open={deleteOpen} onOpenChange={setDeleteOpen}>
  <DialogContent>
    <DialogHeader>
      <DialogTitle>
        {isOwnProfile ? "Stop working as an artist?" : `Delete ${artist.firstName} ${artist.lastName}?`}
      </DialogTitle>
      <DialogDescription>
        {isOwnProfile
          ? "This removes your artist profile — your owner login and studio access are unaffected. This action cannot be undone."
          : "This action cannot be undone."}
      </DialogDescription>
    </DialogHeader>
    <DialogFooter>
      <Button variant="outline" onClick={() => setDeleteOpen(false)} disabled={isDeleting}>
        Cancel
      </Button>
      <Button variant="destructive" onClick={onDelete} disabled={isDeleting}>
        {isDeleting ? (
          <>
            <Loader2 className="h-4 w-4 animate-spin" />
            {isOwnProfile ? "Removing…" : "Deleting…"}
          </>
        ) : (
          isOwnProfile ? "Stop working as an artist" : "Delete"
        )}
      </Button>
    </DialogFooter>
  </DialogContent>
</Dialog>
```

Do not change `onDelete()` itself — it already just calls `deleteArtist(id)` and navigates to
`/artists`, which is correct for this case unchanged (confirmed safe in the Diagnosis section —
`DeleteArtistHandler` never touches Identity).

---

## Step 11 — `frontend/src/layouts/OwnerLayout.tsx`

Read the file in full first (107 lines). Mirror `ArtistLayout.tsx`'s existing `myArtist` pattern
exactly (that file already does this for the artist role — reproduced in the Diagnosis research
for this prompt).

Add to the imports:

```tsx
import { ImagePlus } from "lucide-react";   // add to the existing lucide-react import line
import { useGetMyArtistQuery } from "@/features/artists/artistsApi";
```

Inside `OwnerLayout()`, alongside the existing `useGetSubscriptionQuery()`/`useGetMyStudioQuery()`
calls:

```tsx
const { data: myArtist } = useGetMyArtistQuery();
const navItems: NavItem[] = myArtist
  ? [...NAV_ITEMS, { label: "My Portfolio", href: `/artists/${myArtist.id}`, icon: <ImagePlus className="h-4 w-4" /> }]
  : NAV_ITEMS;
```

Replace both existing usages of `NAV_ITEMS` in the JSX (`{NAV_ITEMS.map(...)}` and `<NavDrawer
navItems={NAV_ITEMS} ...>`) with `navItems`. Leave the `const NAV_ITEMS: NavItem[] = [...]` array
declaration itself untouched — only the two render-time references change.

This fires `GET /api/v1/artists/me` unconditionally for every owner, most of whom won't have a
profile yet — a normal 404 each load, exactly like `ArtistLayout` already does for every artist
today. RTK Query dedupes this against the same call `ArtistListPage.tsx` (Step 9) makes via the
shared `"Artist"` cache tag. This is intentional — do not add a `skip` condition here.

---

## Step 12 — `frontend/src/features/help/helpContent.ts`

Read the file first to reconfirm the `HELP_ARTICLES` array and `HelpArticle` shape (id, roles,
title, route, keywords, summary, steps, tips, warnings — reproduced in the Diagnosis research for
this prompt via the `owner-artists-add` entry). Insert a new entry directly after the existing
`owner-artists-add` entry:

```ts
{
  id: "owner-become-artist",
  roles: [Owner],
  title: "Enable your own artist profile",
  route: "/artists",
  keywords: ["owner artist", "I also tattoo", "myself as artist", "owner-operator", "dual role", "become an artist"],
  summary: "If you also tattoo, add yourself as a bookable artist without creating a second account.",
  steps: [
    "Go to Artists.",
    "Click \"Enable my artist profile\" in the banner near the top of the list.",
    "Enter your first and last name, and optionally your specializations and hourly rate.",
    "Click \"Enable my artist profile\".",
  ],
  tips: [
    "This uses your existing owner login — no new email, password, or invite email.",
    "Once enabled, you'll get a \"My Portfolio\" shortcut in your menu, and you'll be selectable when booking or scheduling appointments, just like any other artist.",
    "This counts as one artist seat against your plan's usage, the same as inviting any other artist.",
  ],
},
```

Also touch up the existing `owner-artists-list` entry's `tips` array (append, don't replace):

```ts
tips: [
  // ...existing tip(s)...
  "If you also tattoo, look for \"Enable my artist profile\" near the top of this page.",
],
```

---

## Step 13 — `frontend/src/features/help/tours/ownerTour.ts`

Read the file first (39 lines, reproduced in full in the Diagnosis research for this prompt).
Add a new step targeting the CTA added in Step 9 — insert it after the `owner-add-artist-nav`
step and before the `owner-deposit-rules-nav` step:

```ts
{
  targetSelector: '[data-tour="owner-become-artist-cta"]',
  title: "Also work as an artist?",
  body: "If you tattoo yourself, enable your own artist profile here — no second account needed.",
},
```

This target only exists on the Artists list page (`/artists`) — confirm the tour framework
navigates there for this step (check how the preceding `owner-add-artist-nav` step, which targets
a nav link rather than a page element, is handled versus any existing step that targets an
element on a non-dashboard route) before assuming this works identically; adjust the step's route
metadata if the tour step format requires it elsewhere (e.g. `ownerTour.ts` steps may need a
route/path hint — read `OnboardingTour`'s `TourStep` type and how multi-page tours are already
handled, if any, before assuming a single shared shape).

---

## Step 14 — `frontend/public/user-manual/index.html`

Read the `owner-artists` and `owner-create-artist` `<section>` blocks first (~line 1758–1813,
reproduced in the Diagnosis research for this prompt) — match their exact structure: `<section
id="..." data-role="owner">`, `<h2>` with `role-badge`, an `access-table`, a `<p>` summary, a
`<figure class="wireframe">` with an inline SVG wireframe in the same visual style as its
siblings, a `<figcaption>`, an `<h3>Steps</h3>` with a `<ol class="steps">`, and a `<div
class="crosslinks">`. Insert a new section directly after `owner-create-artist` (before
`owner-artist-detail`), with `id="owner-become-artist"`:

- Access table: Guest ✗, Client ✗, Artist ✗, Owner ✓, Issuer ✓ (only the owner can enable their
  own profile).
- Summary paragraph: explain this links the owner's existing login as a bookable artist — no new
  email/password, no invite.
- Wireframe: a small form with **First name**, **Last name**, **Specializations (optional)**,
  **Hourly rate (optional)** fields and an "Enable my artist profile" button — no Email field
  (unlike `owner-create-artist`'s wireframe), since the email is the owner's own and isn't
  entered.
- Steps: mirror the dialog flow from Step 9 (open Artists → click the banner button → fill in
  name/specializations/rate → confirm).
- Crosslinks: back to `#owner-artists` and forward to `#owner-artist-detail`.

Also add a crosslink to this new section from the existing `owner-artists` section's
`<div class="crosslinks">` line.

---

## Step 15 — Decisions Log: `docs/claude/architecture.md`

Read the `## Decisions Log` section first (`| Decision | Choice | Reason |` table, starting
around line 1519/1524). Append one row:

```
| Owner-as-artist dual role — 2026-08-21 | Owner keeps a single "owner" Identity role (RBAC already treats owner as a superset of artist via the ArtistAndAbove policy — see AuthorizationExtensions.cs). New CreateOwnArtistProfileCommand (POST /api/v1/artists/me, OwnerOnly) creates an Artist row linked to the owner's own existing UserId/Email — no new Identity account, no role claim change, no invite email, no migration. Added a role-based guard to ResendArtistInviteCommand (reusing IIdentityService.GetUserRolesAsync from the same-day cross-tenant-invite fix) so it can never fire on the owner's own linked profile. Owner's own artist seat counts against the plan's Artist quota, same as any invited artist. Rejected a true multi-role-identity + role-switcher design as unnecessary given existing RBAC and much larger blast radius (JWT generation, every role-gated route guard, help/tour role-set assumptions) for no functional gain | Matches the "linked profile" pattern ArtistLayout already uses for myArtist-conditional nav; keeps this additive rather than touching the JWT/role model; composes cleanly with the same-day cross-tenant-invite-reuse fix since CreateOwnArtistProfileCommand never calls identity.CreateUserAsync and can't trigger its guarded fallback |
```

---

## Step 16 — Test-Fix Loop

After all changes:

```bash
cd "Pena e Arte"
dotnet build --verbosity minimal
dotnet test
```

For every failing backend test: read the test, read the implementation, fix the root cause
(never delete a test), re-run. Repeat until `dotnet test` exits 0.

Then:

```bash
cd frontend
pnpm lint
pnpm test
pnpm build
```

For every failing frontend test or type error: read the failure, read the component, fix the
root cause, re-run. Repeat until all three exit 0. Pay particular attention to:
- Any existing test that constructs `ResendArtistInviteHandler` with the old 3-argument
  constructor (Step 4 changed it to 4) — fix the call site, don't revert the constructor.
- Any existing `ArtistListPage.tsx` or `OwnerLayout.tsx` test/snapshot that assumes
  `useGetMyArtistQuery` is never called for an owner — update the test's mocked API layer to
  stub a 404/empty response rather than removing the assertion.

---

## Hard Rules

1. **No database migration.** `Artist.UserId` already supports this with zero schema change. If
   you find yourself reaching for `dotnet ef migrations add`, stop — you've misunderstood the
   task.
2. **Do not touch `CreateArtistCommand.cs`'s existing "email already taken" fallback** (the logic
   the 2026-08-21 cross-tenant fix just hardened). This prompt's new command is deliberately
   separate specifically so that code path is never touched.
3. **`CreateOwnArtistProfileCommand` must never call `identity.CreateUserAsync`,
   `AddToRoleAsync`, or `scheduler.EnqueueArtistInvite`.** There is no new account and nothing to
   invite — the whole point of this design is that it links, not creates.
4. **Do not modify `DeleteArtistHandler`.** It was confirmed safe by inspection (never touches
   `IIdentityService`) — no special-casing needed for the owner's own row.
5. **Do not implement a second Identity role or a frontend role switcher.** That alternative was
   considered and explicitly rejected (see the spec doc and the Decisions Log entry in Step 15) —
   RBAC already treats owner as a superset of artist everywhere that matters.
6. **Do not add role-based branching to `SchedulePage.tsx`.** It has none today and needs none —
   confirmed by inspection; it already shows the full studio schedule (including the new artist
   row) to anyone `ArtistAndAbove`.
7. **Do not relabel the "Delete" button in the `ArtistListPage.tsx` table/mobile-card rows.**
   Only `ArtistDetailPage.tsx`'s single-profile view gets the "Stop working as an artist"
   treatment (Step 10) — the list-row action stays generic. This is a deliberate scope boundary,
   not an oversight.
8. **No new logging.** Nothing in this change needs a `logger.Log*` call, and per CLAUDE.md
   Rule #3 none should be added speculatively — especially not one that logs an email address.
9. **Do not skip Step 15.** The Decisions Log entry is what keeps `architecture.md` from
   drifting from what the code does.
10. **Read every file listed above before editing it.** Several of this prompt's code blocks are
    insertions into files with more content around them than shown — match indentation, surrounding
    conventions, and existing imports exactly rather than pasting blindly.

---

## Expected Outcome

After this prompt:

- A studio owner can go to **Artists → "Enable my artist profile"**, fill in a small form, and
  become a bookable, schedulable artist under their existing login — no new email, password, or
  invite.
- They immediately get a **"My Portfolio"** shortcut in their own nav (`OwnerLayout`), show up in
  the booking artist-picker, the studio-wide schedule, the public studio portfolio, and pass every
  existing artist-ownership check with zero backend changes beyond the two touched in this
  prompt.
- **"Resend invite"** never appears (nor can be triggered) against their own linked profile.
- **"Delete"** on their own profile reads "Stop working as an artist" with copy clarifying their
  owner login is unaffected; deleting it behaves exactly like removing any other artist (blocked
  while upcoming appointments exist), and can never lock them out.
- The owner's own artist seat counts against the studio's plan Artist quota like any other.
- Help Menu, the standalone user manual, and the owner onboarding tour all describe the new
  capability, per CLAUDE.md Rule #7.
- **Zero interaction with today's cross-tenant-invite fix** — `CreateArtistCommand`'s guarded
  fallback path is untouched and still behaves exactly as that fix left it.
