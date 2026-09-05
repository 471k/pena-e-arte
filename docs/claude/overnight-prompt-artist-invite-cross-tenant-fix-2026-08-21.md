# Overnight Prompt — Artist Invite Cross-Tenant Account Reuse Fix

**Reported rule (currently unenforced):** If an email already belongs to an existing account
as an **owner** (or any role other than a same-studio orphaned artist), that account must not be
silently linked as an **artist** when a different studio's owner invites that email address.

---

## Diagnosis (read this fully before touching any file)

### The bug

`CreateArtistHandler.Handle` (`Pena_e_Arte.Application/Artists/Commands/CreateArtistCommand.cs`,
lines 29–90) invites a new artist in three steps:

1. Checks for an existing **Artist** row with this email — but `db.Artists` is tenant-scoped by
   the global query filter, so this only rules out a duplicate **within the current studio**.
2. Calls `identity.CreateUserAsync(req.Email, tempPassword, "artist", tenant.StudioId, ...)`.
   `AspNetUsers` (ASP.NET Core Identity) is a **single global table, not tenant-partitioned** —
   confirmed in `Pena_e_Arte.Infrastructure/Services/IdentityService.cs`. So if the email already
   belongs to an account in **any** studio, `CreateAsync` fails with an "already taken" error.
3. On that failure, the handler (lines 58–64) assumes the only possible cause is "an orphaned
   artist account from a previous failed attempt," looks up the existing `UserId` via
   `GetUserIdByEmailAsync`, and reuses it — **with no check on what role that account holds or
   which studio it already belongs to**:

   ```csharp
   // An Identity user exists with no linked artist (orphaned from a previous failed attempt).
   // Recover by reusing the existing user's ID.
   Guid? existingId = await identity.GetUserIdByEmailAsync(req.Email, ct);
   if (existingId is null)
       throw new BusinessRuleViolationException($"The email '{req.Email}' is already registered to another account. Each artist must have a unique email address.");

   userId = existingId.Value;
   ```

4. It then creates a new `Artist` row for the **current** studio pointing at that reused
   `UserId`, and enqueues `SendArtistInviteJob`, which generates a real password-reset link
   for that email and sends it framed as an artist invite
   (`Pena_e_Arte.Infrastructure/Jobs/SendArtistInviteJob.cs`, lines 18–39).

**Net effect:** an owner of Studio A can be silently given an `Artist` row in Studio B — created
by Studio B's owner, without Studio A owner's consent or any role check — the moment Studio B's
owner invites Studio A owner's email address. The same silent-reuse path fires for any existing
**client** or **issuer** account too, and for an **artist who already belongs to a different
studio**. This is a direct violation of CLAUDE.md Non-Negotiable Rule #1 ("Tenant isolation is
mandatory").

### Why the existing fallback exists (and when it's actually safe)

`CreateUserAsync` (`IdentityService.cs`, lines 16–35) creates the Identity user, assigns the
role, and attaches the `tenant_id` claim for the studio **before** `CreateArtistHandler` ever
saves the `Artist` row (`db.Artists.Add` happens afterward, line 79). So the *only* legitimate
"orphaned" scenario is: a previous `CreateArtistCommand` call for **this same studio** got as far
as creating the Identity user (role `"artist"`, `tenant_id` claim = this studio) but crashed or
errored before `SaveChangesAsync` persisted the `Artist` row. That case is safe to recover by
reusing the existing `UserId` — nothing else is.

### Why role + tenant are both checkable, and why this generalizes correctly

- `IIdentityService.GetTenantIdsAsync(Guid userId, CancellationToken ct)` **already exists** and
  returns every `tenant_id` claim a user holds.
- No method currently exposes a user's Identity **roles** by id — this must be added.
- Per `IdentityService.GenerateJwt`'s own comment (lines 367–371) and
  `docs/claude/architecture.md` entry #23 ("Multi-Studio Client View... Per-user, cross-tenant"),
  **only the `client` role supports belonging to more than one studio**. `artist`, `owner`, and
  `issuer` accounts are single-studio by design. So the correct guard is not just "reject
  owners" — it's "reject anything that isn't an artist role already scoped to *this* studio."
  That single check correctly covers the reported owner case *and* the client/issuer case *and*
  the cross-studio-artist case, with no special-casing.

### Confirmed: no frontend change needed

`CreateArtistPage.tsx` (`frontend/src/features/artists/components/CreateArtistPage.tsx`, lines
34–51) already surfaces any non-2xx `createArtist` response via
`result.error.data.message` in a toast — it does not special-case today's duplicate-email 422.
The new rejection reuses the same `BusinessRuleViolationException` → 422 path
(`Pena_e_Arte.API`'s `ExceptionMiddleware` already maps `BusinessRuleViolationException` to 422),
so the new error message will surface automatically with zero frontend changes.

### Help Menu / user manual / onboarding tour (CLAUDE.md Rule #7)

**Not required.** This is a backend authorization/validation fix closing a tenant-isolation gap
— it does not add a feature, change a workflow, or add a UI surface. The only user-visible
change is a more specific error toast on an invite attempt that was always going to fail one way
or another (it previously fell through as a `500`-adjacent silent misassignment, not a
successful invite). No Help content describes today's broken behavior, so none needs to change.

---

## Files to Change

| # | File | Change |
|---|------|--------|
| 1 | `Pena_e_Arte.Domain/Interfaces/IIdentityService.cs` | Add `GetUserRolesAsync(Guid userId, CancellationToken ct)` |
| 2 | `Pena_e_Arte.Infrastructure/Services/IdentityService.cs` | Implement `GetUserRolesAsync` |
| 3 | `Pena_e_Arte.Application/Artists/Commands/CreateArtistCommand.cs` | Guard the "email already taken" fallback with a role + tenant check |
| 4 | `tests/Pena_e_Arte.IntegrationTests/Infrastructure/IdentityServiceTests.cs` | New tests for `GetUserRolesAsync` |
| 5 | `tests/Pena_e_Arte.UnitTests/Artists/CreateArtistHandlerTests.cs` | New tests for the guard (rejection + genuine-recovery cases) |
| 6 | `docs/claude/architecture.md` | Append one row to the "Decisions Log" |

---

## Step 1 — `Pena_e_Arte.Domain/Interfaces/IIdentityService.cs`

Read the file first. Add this method to the interface, near `GetUserIdByEmailAsync` /
`GetTenantIdsAsync`:

```csharp
/// <summary>
/// Returns the Identity roles held by the given user (e.g. "owner", "artist", "client",
/// "issuer"). Empty if no such user exists. This app's own flows only ever assign a user
/// one role, but Identity itself does not prevent more than one, so callers should not
/// assume a single-element result.
/// </summary>
Task<IReadOnlyList<string>> GetUserRolesAsync(Guid userId, CancellationToken ct);
```

---

## Step 2 — `Pena_e_Arte.Infrastructure/Services/IdentityService.cs`

Read the file first. Add the implementation near `GetUserIdByEmailAsync` (around line 179):

```csharp
public async Task<IReadOnlyList<string>> GetUserRolesAsync(Guid userId, CancellationToken ct)
{
    IdentityUser? user = await userManager.FindByIdAsync(userId.ToString());
    if (user is null) return [];
    return await userManager.GetRolesAsync(user);
}
```

`GetRolesAsync` returns `IList<string>`, which satisfies `IReadOnlyList<string>` directly — no
extra `.ToList()` needed, matching the style already used elsewhere in this file (e.g.
`GetTenantIdsAsync`).

---

## Step 3 — `Pena_e_Arte.Application/Artists/Commands/CreateArtistCommand.cs`

Read the file first (`CreateArtistHandler.Handle`, lines 29–90). Add `ICurrentTenant` usage that
already exists (`tenant.StudioId` is already in scope). Replace the `if (!created)` block
(lines 52–65) with:

```csharp
if (!created)
{
    bool emailTaken = errors.Any(e => e.Contains("already taken", StringComparison.OrdinalIgnoreCase));
    if (!emailTaken)
        throw new BusinessRuleViolationException($"Failed to create artist account: {string.Join(", ", errors)}");

    Guid? existingId = await identity.GetUserIdByEmailAsync(req.Email, ct);
    if (existingId is null)
        throw new BusinessRuleViolationException($"The email '{req.Email}' is already registered to another account. Each artist must have a unique email address.");

    // The only safe reason for this email to already exist in Identity is a genuinely
    // orphaned artist account: a previous CreateArtistCommand call for THIS studio got as
    // far as creating the Identity user (already holding the "artist" role and this
    // studio's tenant_id claim — see CreateUserAsync) but never made it to persisting the
    // Artist row below, e.g. a crash between identity.CreateUserAsync and SaveChangesAsync.
    // That case is safe to recover by reusing the existing user's ID.
    //
    // Any other case — the email belongs to an owner, client, or issuer account, or to an
    // artist who already belongs to a DIFFERENT studio — must be rejected outright. Silently
    // reusing that account's ID here would grant it artist access to this tenant's data
    // without the account holder's consent or knowledge, violating tenant isolation
    // (CLAUDE.md Non-Negotiable Rule #1). Only the "client" role supports belonging to more
    // than one studio (see GenerateJwt's tenant-claim comment in IdentityService and
    // architecture.md's "Multi-Studio Client View" entry) — artist and owner accounts are
    // single-studio by design, so any cross-studio match here is always wrong.
    IReadOnlyList<string> existingRoles = await identity.GetUserRolesAsync(existingId.Value, ct);
    IReadOnlyList<Guid> existingTenantIds = await identity.GetTenantIdsAsync(existingId.Value, ct);
    bool isOrphanedArtistForThisStudio =
        existingRoles.Contains("artist") && existingTenantIds.Contains(tenant.StudioId);

    if (!isOrphanedArtistForThisStudio)
        throw new BusinessRuleViolationException(
            $"The email '{req.Email}' already belongs to an existing account and cannot be invited as an artist here.");

    userId = existingId.Value;
}
```

Add `identity` to the constructor injection list — it is already injected (`IIdentityService
identity` is already a constructor parameter). No constructor signature change needed.

---

## Step 4 — Tests: `tests/Pena_e_Arte.IntegrationTests/Infrastructure/IdentityServiceTests.cs`

Read the file first. It already builds a real `UserManager<IdentityUser>` against the test
database (`BuildUserManagerAsync`) and seeds the four roles (`client`, `artist`, `owner`,
`issuer`). Add these tests near the other `CreateUserAsync`/role-related tests:

```csharp
[Fact]
public async Task GetUserRolesAsync_ExistingUser_ReturnsAssignedRoles()
{
    UserManager<IdentityUser> um = await BuildUserManagerAsync();
    IdentityService sut = CreateSut(um);
    string email = UniqueEmail();

    (bool _, Guid userId, string[] _) = await sut.CreateUserAsync(email, "Password1!", "owner", Guid.NewGuid());

    IReadOnlyList<string> roles = await sut.GetUserRolesAsync(userId, default);

    roles.Should().Contain("owner");
}

[Fact]
public async Task GetUserRolesAsync_NoSuchUser_ReturnsEmpty()
{
    UserManager<IdentityUser> um = await BuildUserManagerAsync();
    IdentityService sut = CreateSut(um);

    IReadOnlyList<string> roles = await sut.GetUserRolesAsync(Guid.NewGuid(), default);

    roles.Should().BeEmpty();
}
```

Confirm `UniqueEmail()` is the exact helper name already used elsewhere in this file before
using it — read the file to verify rather than assuming.

---

## Step 5 — Tests: `tests/Pena_e_Arte.UnitTests/Artists/CreateArtistHandlerTests.cs`

Read the file first. The constructor already stubs `_identity.CreateUserAsync(...)` to succeed
for any args via `ReturnsForAnyArgs`. Add these tests at the bottom of the class — each
overrides that default with a specific-args stub for its own email, which NSubstitute resolves
correctly alongside the general default:

```csharp
[Fact]
public async Task Handle_EmailBelongsToExistingOwnerAccount_ThrowsBusinessRuleViolationException()
{
    const string email = "owner-of-another-studio@example.com";
    Guid existingOwnerUserId = Guid.NewGuid();

    _identity.CreateUserAsync(email, Arg.Any<string>(), "artist", _studioId, Arg.Any<string>())
        .Returns((false, Guid.Empty, new[] { $"Username '{email}' is already taken." }));
    _identity.GetUserIdByEmailAsync(email, Arg.Any<CancellationToken>()).Returns(existingOwnerUserId);
    _identity.GetUserRolesAsync(existingOwnerUserId, Arg.Any<CancellationToken>())
        .Returns(new List<string> { "owner" });
    _identity.GetTenantIdsAsync(existingOwnerUserId, Arg.Any<CancellationToken>())
        .Returns(new List<Guid> { Guid.NewGuid() }); // owner's OWN studio — never this one

    Func<Task> act = () => CreateSut()
        .Handle(new CreateArtistCommand(new("New", "Artist", email, null)), default);

    await act.Should().ThrowAsync<BusinessRuleViolationException>()
        .WithMessage($"*{email}*");
}

[Fact]
public async Task Handle_EmailBelongsToExistingOwnerAccount_DoesNotPersistArtist()
{
    const string email = "owner-of-another-studio@example.com";
    Guid existingOwnerUserId = Guid.NewGuid();

    _identity.CreateUserAsync(email, Arg.Any<string>(), "artist", _studioId, Arg.Any<string>())
        .Returns((false, Guid.Empty, new[] { $"Username '{email}' is already taken." }));
    _identity.GetUserIdByEmailAsync(email, Arg.Any<CancellationToken>()).Returns(existingOwnerUserId);
    _identity.GetUserRolesAsync(existingOwnerUserId, Arg.Any<CancellationToken>())
        .Returns(new List<string> { "owner" });
    _identity.GetTenantIdsAsync(existingOwnerUserId, Arg.Any<CancellationToken>())
        .Returns(new List<Guid> { Guid.NewGuid() });

    try { await CreateSut().Handle(new CreateArtistCommand(new("New", "Artist", email, null)), default); } catch { }

    _db.Artists.Should().NotContain(a => a.Email == email);
}

[Fact]
public async Task Handle_EmailBelongsToExistingOwnerAccount_DoesNotEnqueueInviteEmail()
{
    const string email = "owner-of-another-studio@example.com";
    Guid existingOwnerUserId = Guid.NewGuid();

    _identity.CreateUserAsync(email, Arg.Any<string>(), "artist", _studioId, Arg.Any<string>())
        .Returns((false, Guid.Empty, new[] { $"Username '{email}' is already taken." }));
    _identity.GetUserIdByEmailAsync(email, Arg.Any<CancellationToken>()).Returns(existingOwnerUserId);
    _identity.GetUserRolesAsync(existingOwnerUserId, Arg.Any<CancellationToken>())
        .Returns(new List<string> { "owner" });
    _identity.GetTenantIdsAsync(existingOwnerUserId, Arg.Any<CancellationToken>())
        .Returns(new List<Guid> { Guid.NewGuid() });

    try { await CreateSut().Handle(new CreateArtistCommand(new("New", "Artist", email, null)), default); } catch { }

    _scheduler.DidNotReceiveWithAnyArgs().EnqueueArtistInvite(default!, default!, default);
}

[Fact]
public async Task Handle_EmailBelongsToArtistAtDifferentStudio_ThrowsBusinessRuleViolationException()
{
    const string email = "artist-at-another-studio@example.com";
    Guid existingArtistUserId = Guid.NewGuid();
    Guid otherStudioId = Guid.NewGuid();

    _identity.CreateUserAsync(email, Arg.Any<string>(), "artist", _studioId, Arg.Any<string>())
        .Returns((false, Guid.Empty, new[] { $"Username '{email}' is already taken." }));
    _identity.GetUserIdByEmailAsync(email, Arg.Any<CancellationToken>()).Returns(existingArtistUserId);
    _identity.GetUserRolesAsync(existingArtistUserId, Arg.Any<CancellationToken>())
        .Returns(new List<string> { "artist" });
    _identity.GetTenantIdsAsync(existingArtistUserId, Arg.Any<CancellationToken>())
        .Returns(new List<Guid> { otherStudioId }); // "artist" role, but for a DIFFERENT studio

    Func<Task> act = () => CreateSut()
        .Handle(new CreateArtistCommand(new("New", "Artist", email, null)), default);

    await act.Should().ThrowAsync<BusinessRuleViolationException>()
        .WithMessage($"*{email}*");
}

[Fact]
public async Task Handle_EmailBelongsToClientAccount_ThrowsBusinessRuleViolationException()
{
    const string email = "client@example.com";
    Guid existingClientUserId = Guid.NewGuid();

    _identity.CreateUserAsync(email, Arg.Any<string>(), "artist", _studioId, Arg.Any<string>())
        .Returns((false, Guid.Empty, new[] { $"Username '{email}' is already taken." }));
    _identity.GetUserIdByEmailAsync(email, Arg.Any<CancellationToken>()).Returns(existingClientUserId);
    _identity.GetUserRolesAsync(existingClientUserId, Arg.Any<CancellationToken>())
        .Returns(new List<string> { "client" });
    _identity.GetTenantIdsAsync(existingClientUserId, Arg.Any<CancellationToken>())
        .Returns(new List<Guid> { _studioId }); // even a client of THIS studio must not become its artist

    Func<Task> act = () => CreateSut()
        .Handle(new CreateArtistCommand(new("New", "Artist", email, null)), default);

    await act.Should().ThrowAsync<BusinessRuleViolationException>()
        .WithMessage($"*{email}*");
}

[Fact]
public async Task Handle_EmailBelongsToOrphanedArtistForThisStudio_ReusesExistingUserIdAndSucceeds()
{
    const string email = "orphaned@studio.com";
    Guid orphanedUserId = Guid.NewGuid();

    _identity.CreateUserAsync(email, Arg.Any<string>(), "artist", _studioId, Arg.Any<string>())
        .Returns((false, Guid.Empty, new[] { $"Username '{email}' is already taken." }));
    _identity.GetUserIdByEmailAsync(email, Arg.Any<CancellationToken>()).Returns(orphanedUserId);
    _identity.GetUserRolesAsync(orphanedUserId, Arg.Any<CancellationToken>())
        .Returns(new List<string> { "artist" });
    _identity.GetTenantIdsAsync(orphanedUserId, Arg.Any<CancellationToken>())
        .Returns(new List<Guid> { _studioId }); // artist role, SAME studio — genuine recovery case

    ArtistResponse result = await CreateSut()
        .Handle(new CreateArtistCommand(new("Recovered", "Artist", email, null)), default);

    result.UserId.Should().Be(orphanedUserId);
    _db.Artists.Should().ContainSingle(a => a.Email == email && a.UserId == orphanedUserId);
}
```

**Note on the constructor's default stub:** `_identity.CreateUserAsync(default!, default!,
default!, default, default).ReturnsForAnyArgs(...)` remains as the happy-path default for all
existing tests. NSubstitute lets a later specific-argument `Returns(...)` (configured inside a
test method, as above) take precedence for calls matching those exact arguments, while every
other test's calls keep matching the constructor's `ReturnsForAnyArgs` default. Read
NSubstitute's actual matching behavior if unsure — do not assume, verify by running the new
tests.

---

## Step 6 — Decisions Log: `docs/claude/architecture.md`

Read the "## Decisions Log" section (`| Decision | Choice | Reason |` table, currently starting
around line 1519). Append one row, matching the existing table's style and level of detail:

```
| Owner-as-artist cross-tenant invite fix — 2026-08-21 | `CreateArtistHandler` now checks the existing account's role and tenant membership (new `IIdentityService.GetUserRolesAsync`, plus the already-existing `GetTenantIdsAsync`) before reusing an Identity user's ID on "email already taken." Only a genuinely orphaned artist account for the SAME studio is recovered; an owner, client, issuer, or an artist already belonging to a DIFFERENT studio now throws `BusinessRuleViolationException` instead | Previously any existing account's Identity `UserId` was silently reused and linked as a brand-new `Artist` row in the inviting studio, with no role or tenant check — letting one studio's owner invite (by email) an owner, artist, or client account from a completely unrelated studio and silently gain an `Artist` record pointing at that account, without its holder's consent. Direct violation of CLAUDE.md Rule #1 (tenant isolation). Artist and owner accounts are single-studio by design — only `client` supports multi-studio membership (`GenerateJwt`'s tenant-claim comment; architecture.md's "Multi-Studio Client View" entry #23) — so this guard generalizes cleanly to every non-artist-same-studio case, not just the reported owner scenario |
```

---

## Step 7 — Test-Fix Loop

After all changes, run the full backend test suite and loop until clean:

```bash
cd "Pena e Arte"
dotnet build --verbosity minimal
dotnet test
```

For every failing test:
1. Read the test file.
2. Read the implementation.
3. Fix the root cause (never delete a test).
4. Re-run tests.

Repeat until `dotnet test` exits 0. No frontend changes are part of this fix, so `pnpm` steps are
not required — but if any frontend test happens to assert the exact wording of the old duplicate-
email 422 message for `CreateArtist`, read it and confirm it still passes (the duplicate-email-
within-this-studio path in Step 3 is untouched; only the cross-account fallback path changed).

---

## Hard Rules

1. **Do not touch the existing tenant-scoped duplicate-email check** (lines 33–35 of
   `CreateArtistCommand.cs`, `db.Artists.AnyAsync(a => a.Email == req.Email, ct)`) — that check
   is correct and unrelated to this bug.
2. **Do not change `CreateUserAsync`'s signature or behavior** — the fix is entirely in how
   `CreateArtistHandler` reacts to its failure, plus one new read-only `IIdentityService` method.
3. **Do not add a new exception type.** Reuse `BusinessRuleViolationException` — it is already
   mapped to HTTP 422 in `ExceptionMiddleware`, matching the existing duplicate-email error on
   the line directly above this fix.
4. **Do not modify `SendArtistInviteJob.cs` or `ResendArtistInviteCommand.cs`** — with this fix,
   an `Artist` row can never be created against a non-orphaned foreign account, so the invite
   email is never sent to one. No changes needed there.
5. **No frontend changes** — verified above that the existing error-toast path already surfaces
   the new message with no code change required. Do not add one speculatively.
6. **Do not log the email address or any other PII** in the new guard (CLAUDE.md Rule #3) — the
   exception message returned to the caller (studio owner) is fine since it's already
   user-facing input echoed back, exactly like the existing duplicate-email message; just don't
   add a new `logger.Log*` call here.
7. **Do not skip Step 6** — the Decisions Log entry is what keeps `architecture.md` from
   silently drifting from what the code actually does, per this project's own stated convention
   for every fix of this shape (see the 2026-07-26 security-remediation entry as the closest
   prior example).

---

## Expected Outcome

After this fix:

- **Owner `owner-a@studio-a.test`** (owner of Studio A) can never end up with a silent `Artist`
  row in Studio B just because Studio B's owner types their email into the "Invite Artist" form.
  Studio B's owner sees: *"The email 'owner-a@studio-a.test' already belongs to an existing
  account and cannot be invited as an artist here."*
- The same rejection applies if the email belongs to any **client** or **issuer** account, or to
  an **artist who already works at a different studio**.
- **Genuine recovery still works**: if a previous invite attempt for *this* studio created the
  Identity user (role `artist`, this studio's `tenant_id` claim) but crashed before the `Artist`
  row was saved, retrying the invite with the same email still succeeds and reuses that user.
- **No behavior change** for the common case — a brand-new email with no existing account at all
  — which still creates the Identity user and the `Artist` row exactly as before.
- **No frontend, Help Menu, user-manual, or onboarding-tour changes** are needed or expected.
