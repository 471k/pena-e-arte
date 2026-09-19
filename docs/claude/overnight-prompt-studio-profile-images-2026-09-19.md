# Overnight Prompt — Studio & Profile Images (Logo, Cover, Artist Photo, Account Avatar)

> Feed this file directly to Claude Code as the task prompt. It is self-contained: exact
> files, exact current code (re-verified against live source on 2026-09-19, corrections to
> the originating spec noted inline where the spec and the repo disagreed), exact target
> code, exact tests, exact docs to sync. Read the whole file before writing anything — later
> phases depend on decisions made in §2–§4, and Phase 0 is a hard gate on Phase A.

**Date logged:** 2026-09-19
**Requested by:** Phi
**Origin:** 2026-09-19 — the owner of "Ink Me harder Studio" asked how to set a studio
profile picture for the Discover page; there is no way to. Same session: "all users should
be able to add their own image if they want — optional." Elaborated into
`docs/claude/feature-spec-studio-and-profile-images-2026-09-19.md` (draft spec, status
"not an implementation prompt yet"). This file supersedes that draft as the executable
plan: it resolves every decision in the draft's §15, corrects three factual errors the
draft's own author flagged as unverified or got wrong, and adds the concrete gaps the draft
didn't know to ask about (see §2.11–§2.13).
**Mode:** Fully autonomous, no user present. Do not stop to ask questions — where a genuine
product decision is still open, it is listed in §3 as a "do not build blind" item with a
default; take the default and keep moving. Where this file states a fact about current code,
it was re-read from the live source on 2026-09-19 (commit `34ca731c`) — if your checkout
disagrees, trust your checkout and note the drift in your final report, but do not silently
change approach without flagging it.

**Before starting**, run:

```bash
git add -A && git commit -m "checkpoint: before studio/profile images feature" --allow-empty
git checkout -b feature/studio-profile-images
```

Commit at the end of **each** phase (§6–§9) on this branch, not just at the very end —
if something in a later phase goes wrong, the earlier phases must still be reviewable and
revertable independently, per D8 (§2.8).

---

## 1. Goal

Let every studio (logo + cover), every artist (a per-studio professional photo), and every
signed-in user (an account avatar) attach an optional image, with one shared upload
pipeline, safe-by-construction processing (server-side re-encode, EXIF/GPS stripped, no
client-supplied URLs ever stored), and a graceful initials/monogram fallback everywhere —
exactly as specified in `docs/claude/feature-spec-studio-and-profile-images-2026-09-19.md`
§1–§4, with the corrections in §2.11–§2.13 below applied. Nothing is ever required; no flow
is blocked, nagged, or reordered for a missing image.

Applicable `CLAUDE.md` non-negotiable rules for this change: #1 (tenant isolation — studio
images scope through `ICurrentTenant`, account avatars deliberately do not, see §2.12),
#2 (RBAC — every new route gets `.RequireAuthorization()` with a named policy, no new
`AllowAnonymous` route is introduced), #3 (never log PII — see §7.7), #4 (secrets via env/
Vault — R2 credentials are already handled this way, unchanged), #5 (structured logs only),
#6 (industry-standard benchmark — see §10), #7 (Help sync — woven into every phase below,
not an appendix).

---

## 2. Decisions already made — implement as specified, do not re-litigate

These resolve the draft spec's §15 table. Each is the spec's own recommendation, accepted,
unless marked "**revised**" — those are corrected after re-verifying the live repo tonight.

### 2.1 D1 — Two studio images, not one
Add `Studio.LogoUrl` (square). Keep `Studio.CoverImageUrl` (existing column, name and
meaning unchanged — confirmed still `public string? CoverImageUrl { get; set; }` at
`Pena_e_Arte.Domain/Entities/Studio.cs:11`, nullable, `HasMaxLength(500)` in the EF config,
read by `GetNearbyStudiosQuery`, `GetPublicStudioQuery`, `GetMyStudiosQuery`, written by
nothing — verified via repo-wide search, no hits outside read queries/DTOs/EF
config/migrations). My Studios currently doubles this column as a small avatar
(`StudioAvatar` component) — that is exactly the bug a separate `LogoUrl` fixes (§7.3).

### 2.2 D2 — Account avatar: new `UserProfile` side table, not a custom `ApplicationUser`
Confirmed tonight: `AppDbContext : IdentityDbContext<IdentityUser>`
(`Pena_e_Arte.Infrastructure/Persistence/AppDbContext.cs:13`), `AddIdentityCore<IdentityUser>`
(`InfrastructureServiceExtensions.cs:52`), and three more call sites use
`UserManager<IdentityUser>` directly (`AdminBootstrapper.cs`, `DataSeeder.cs` ×2,
`IdentityService.cs`). Changing the generic type argument is a wide, high-risk refactor for
one column — not worth it for this feature. `UserOnboardingState`
(`Pena_e_Arte.Domain/Entities/UserOnboardingState.cs`) is the exact precedent to copy: a
`Guid UserId` (non-nullable, confirmed by reading the file tonight — it is **not**
`Guid?`), no tenant query filter, private setters + a static factory + a `MarkComplete()`-
style mutator method. `UserProfile` follows the same shape (§6.1 has the exact class).
**No Identity claims, ever** — the 2026-09-17 dual-role claim regression (commit history
around PR #148→#150, `fix: don't strip an owner's own tenant claim on self-delete`) is
exactly the class of bug avoiding claims here sidesteps.

### 2.3 D3 — ImageSharp, gated on a license check (Phase 0, not optional)
Recommendation accepted, **with the license check promoted from a footnote to a hard Phase
0 gate** (§5) — this codebase has never shipped a dual-licensed dependency check as a
blocking step before, but it does already carry one dual-licensed package precedent
(`QuestPDF` 2025.3.0, Community/Commercial split, in
`Pena_e_Arte.Infrastructure.csproj`) — cite that precedent in the PR description so the
choice doesn't look novel. Confirmed no image-processing library is referenced anywhere in
`*.csproj` tonight (SixLabors.ImageSharp, SkiaSharp, Magick.NET: zero hits). If Six Labors'
Split License terms don't clearly apply to this business, fall back to `SkiaSharp` (MIT) —
same interface (`IImageProcessor`), swap the implementation, no other code changes.

### 2.4 D4 — Artist photo precedence
Accepted as specified in the draft's §8.2, **with the field name corrected** — see §2.11.

### 2.5 D5 — Client avatar visibility
Accepted: staff of studios where the person is a client only, never public. No schema
change needed beyond what §6.1 already does.

### 2.6 D6 — Moderation v1 is manual-only
Accepted: admin takedown (§7.5) + the existing conduct-report/feedback flows. No automated
content scanning, no new "Report image" UI.

### 2.7 D7 — `react-easy-crop` (MIT) for square crops
Accepted. Confirmed not currently in `frontend/package.json` (zero hits tonight) — this is
a genuinely new dependency, flag it in the PR description per `CLAUDE.md`'s "never add a
new ORM" spirit not applying here (this isn't a data-access library) but new-dependency
flagging still applying.

### 2.8 D8 — Three phases, each independently committed, in one overnight session
Phase A (§6): shared pipeline + studio logo/cover + admin removal + Help. Phase B (§7):
account avatar for all roles + `/account/profile` + `IAvatarResolver` + messaging + erasure/
export. Phase C (§8): artist-photo write path + full display sweep. Each phase gets its own
EF migration (three small additive migrations, not one large one — matches D8's "each
independently shippable" intent) and its own commit. If you run out of time, stop at the end
of a completed phase, never mid-phase — an incomplete Phase C (say) must not leave Phase A/B
in a broken state.

### 2.9 D9 — No plan/quota gating on identity images
Confirmed: none of the new commands implement `IQuotaCheckedCommand`
(`Pena_e_Arte.Domain/Interfaces/IQuotaCheckedCommand.cs` — picked up by `PlanLimitBehavior`
in the MediatR pipeline). Studio logo/cover objects still live under `{studioId}/…` so they
still count toward `Studio.StorageUsageBytes` via the existing `StorageReconciliationJob`
sweep (§7 of that job's own doc comment, confirmed unchanged tonight) — that's a side effect
of the key layout, not a deliberate quota check, and is correct/desired (storage is still
finite even if this feature doesn't gate on plan tier).

### 2.10 D10 — R2 `Cache-Control` via the Cloudflare Worker: verify, don't assume (Phase 0 gate)
Promoted from a footnote to a hard Phase 0 gate (§5) for the same reason as D3 — the whole
point of content-addressed `{imageId}` URLs (§6.3) is that `immutable` caching is safe, and
that's only true if the Worker actually forwards the header. No Worker config was found in
this repo tonight either (searched `k8s/`, `docker/`, root — nothing) — this has to be
checked in the Cloudflare dashboard directly, which this autonomous session cannot reach.

### 2.11 **NEW — the draft spec conflated two different Artist columns; resolved here**
The draft's §2 says `Artist.AvatarUrl` "is read by messaging and public DTOs, never
written," and §5 says to reuse `AvatarUrl` as the artist-photo field. **This is wrong.**
Reading `Pena_e_Arte.Domain/Entities/Artist.cs` tonight shows **two** separate nullable
string columns:

```csharp
public string? ProfileImageUrl { get; set; }   // line 20 — added 2026-06-26 (migration
                                                // 20260626131403_AddArtistProfileImageUrl)
...
/// <summary>URL of the artist's profile photo. Null when not set — show initials fallback in the UI.</summary>
public string? AvatarUrl { get; set; }         // line 26 — added 2026-06-30, 4 days later
                                                // (migration 20260630215325_AddArtistIsActiveAndAvatarUrl)
```

They are **not** the same field doing double duty — they are two unreconciled columns from
two different features, four days apart, that were never merged:

- `ProfileImageUrl` is the one actually read by every **public-facing** surface:
  `GetPublicArtistQuery.cs:62`, `GetPublicBookingArtistsQuery.cs:29`,
  `GetPublicStudioQuery.cs:108`, and exposed on `PublicArtistResponse` and
  `PublicArtistSummary` (whose own comment literally says `// circular avatar; null → show
  monogram`).
- `AvatarUrl` is read **only** by messaging: `CreateConversationCommand.cs:77`,
  `ConversationEligibility.cs:55,86`, `GetConversationContactsQuery.cs:36`,
  `GetConversationsQuery.cs:75` — plus echoed back in `CreateArtistCommand.cs:125`'s
  response projection.
- **Neither column has a single writer anywhere in the codebase** (confirmed: no
  `ProfileImageUrl =` or `AvatarUrl =` assignment outside EF migrations, and `DataSeeder.cs`
  doesn't set either for seed artists). Both are 100% dead today. There is no data to
  migrate and no backwards-compatibility risk in picking one.

**Decision: canonicalize on `ProfileImageUrl`.** It's already the one every public surface
reads, which is exactly what "artist photo" means in this feature (§1's table — artist photo
is public by design). Do **not** add a third column.

- Phase C (§8) is where the artist-photo **write path** ships (presign/commit/remove
  commands write to `Artist.ProfileImageUrl`).
- Phase B (§7)'s `IAvatarResolver` and its four messaging call sites are updated to read
  `Artist.ProfileImageUrl` instead of `Artist.AvatarUrl` — this is not a new decision, it's
  the same "replace the messaging call sites with the resolver" work the draft spec already
  planned for Phase B (§8.2 of the draft), just pointed at the correct column.
- `Artist.AvatarUrl` becomes dead code once Phase B ships. Do not delete the column tonight
  (that's a breaking migration for zero benefit under time pressure) — add a one-line XML
  doc comment marking it `[Obsolete-by-convention: superseded by ProfileImageUrl, see
  docs/claude/architecture.md Decisions Log 2026-09-19 — kept only until a follow-up
  migration drops it]` and stop referencing it anywhere in new code. Log the follow-up
  cleanup (drop the column) as a `database.md` note, not a task for tonight.
- Update `docs/claude/database.md`'s Artist section to describe both columns and this
  resolution (§9.4).

### 2.12 **NEW — confirmed, not hypothetical: account avatar routes must never touch `ICurrentTenant`**
The draft spec flagged this as untested ("I have not run this path with a null tenant").
Tonight's read of `ICurrentTenant`/`CurrentTenantService`
(`Pena_e_Arte.Infrastructure/Services/CurrentTenantService.cs`) shows:

```csharp
public class CurrentTenantService : ICurrentTenant
{
    private Guid _studioId;
    public Guid StudioId => _studioId;                    // non-nullable Guid
    public bool IsSet => _studioId != Guid.Empty;
    public void SetTenant(Guid studioId) => _studioId = studioId;
}
```

`StudioId` does **not** throw when unset — it silently returns `Guid.Empty`. If the account-
avatar presign handler reused `GetPresignedUploadUrlHandler`'s key-building pattern
(`$"{tenant.StudioId}/{prefix}/{fileName}"`), every studio-less client's "presigned" upload
would land under the literal path `00000000-0000-0000-0000-000000000000/…` — a single shared
prefix every tenant-less user on the platform would collide into. This is worse than the
draft's own guess ("produces an invalid or empty prefix").

**Make it structurally impossible, not just avoided by convention.** The new
`PresignAccountAvatarCommand`/`SetAccountAvatarCommand`/`RemoveAccountAvatarCommand` handlers
must not take `ICurrentTenant` as a constructor dependency at all — omit it from the
constructor entirely so the mistake can't compile back in during a later edit. Add a unit
test that constructs each handler and asserts (via reflection or simply by the constructor
signature) that `ICurrentTenant` is not among its dependencies, or — simpler and just as
effective — an integration test that calls all three avatar routes as a studio-less client
(`tenant.IsSet == false`) and asserts success with keys under `avatars/{userId}/…`, never
under `Guid.Empty/…` or any `{studioId}/…` prefix.

### 2.13 **NEW — confirmed: `ImpersonationAllowList` already denies every new route by construction**
The draft spec hedged here too ("I have not read the allow-list's default; verify"). Tonight's
read of `Pena_e_Arte.API/Middleware/ImpersonationAllowList.cs` shows:

```csharp
public static bool IsAllowed(string method, PathString path)
{
    if (!HttpMethods.IsGet(method)) return false;
    ...
}
```

It is **GET-only, deny-everything-else, no exceptions, by design** ("every POST/PUT/PATCH/
DELETE is denied while impersonating, full stop, no exceptions in this phase" — the class's
own doc comment). Every new route this feature adds (all thirteen: nine presign/set/remove
routes across §6–§8, three focal/moderation routes, one profile GET) except the single `GET
/users/me/profile` are non-GET, and even that GET is not on the allow-list. **No change to
`ImpersonationAllowList` is needed or wanted** — adding an entry would be a regression, not a
fix. Do add a regression test (§7.6, §8.6) confirming an impersonation-scoped token gets
denied on every write route — this is a belt-and-braces test against a future refactor of
the allow-list's blanket rule, not a fix for a gap that exists today.

### 2.14 **NEW — `IR2Service` cannot do steps 3b/3c of the commit flow today; two methods must be added**
The draft's §6.2 assumes the commit step can "HEAD the object" and "stream the object with a
hard byte cap; sniff magic bytes." Tonight's read of
`Pena_e_Arte.Domain/Interfaces/IR2Service.cs` and its only implementation,
`Pena_e_Arte.Infrastructure/Services/R2Service.cs`, shows **no method exists for either** —
the interface has `GeneratePresignedUploadUrlAsync`, two `GeneratePresignedReadUrlAsync`
overloads, `IsR2Url`, `UploadAsync(byte[])`, `DeleteAsync`, `GetPublicUrl`, and
`ListByPrefixAsync`. There is nothing that returns object metadata without downloading it,
and nothing that returns a readable stream. Add two new members (additive, no existing
caller changes):

```csharp
// IR2Service.cs — new members
Task<R2ObjectInfo?> HeadObjectAsync(string objectKey, CancellationToken ct); // null = not found
Task<Stream> OpenReadStreamAsync(string objectKey, CancellationToken ct);
```

`R2Service.HeadObjectAsync` wraps `IAmazonS3.GetObjectMetadataAsync` (catch `AmazonS3Exception`
with `StatusCode == NotFound`, return `null`); `OpenReadStreamAsync` wraps
`IAmazonS3.GetObjectAsync(...).ResponseStream`. Both are needed by `ImageCommitService`
(§6.4) — do this as part of Phase A, before anything that calls it.

### 2.15 **NEW — the standalone manual has two copies; only one is live**
The draft's §13 hedged on which of `frontend/public/user-manual/index.html` or
`docs/user-manual.html` is "currently-live." Confirmed tonight by file metadata:
`frontend/public/user-manual/index.html` is 355,207 bytes, last modified 2026-09-18
(yesterday); `docs/user-manual.html` is 106,851 bytes, last modified 2026-07-25, and still
defines a CSS custom property `--issuer` in its `:root` block — a naming fossil from before
the issuer→admin rename (migration `20260906171400_RenameIssuerRoleToAdmin`, confirmed in
the migrations directory). No file anywhere in `frontend/src` or the API references
`user-manual` by path, so there's no routing config to double-check — the recency and size
gap alone make it unambiguous. **`frontend/public/user-manual/index.html` is the live copy.**
Use it for §6.6/§7.9/§8.7. `docs/user-manual.html` is a stale, abandoned duplicate — do not
edit it tonight (out of scope), but add one line to your final report flagging it for
deletion or a redirect in a future housekeeping pass; do not delete it yourself without being
asked, since that's a judgment call for whoever owns doc cleanup.

---

## 3. Decisions to flag, not decide — do not build blind

- **Automated content moderation** for public images (nudity/abuse detection). Tattoo art
  legitimately includes nudity, so an automated scanner needs its own policy decision before
  it's safe to build. **Do not build any scanning tonight.** v1 is manual takedown only (§2.6).
- **Plan gating on identity images.** Explicitly decided against for now (§2.9) — if that
  changes later, it's a new `IQuotaCheckedCommand` on the relevant commands, not a rebuild.
- **Branded email/receipt logo.** Storing the logo makes this additive later; do not wire it
  into notification templates tonight — out of scope.
- **`docs/user-manual.html` cleanup** (§2.15). Flag in the final report; do not act on it
  without being asked.
- **R2 `Cache-Control` forwarding by the Cloudflare Worker** (§2.10/§5.1) and **the
  ImageSharp license** (§2.3/§5.2) are Phase 0 gates, not silent assumptions — if either
  check comes back negative, stop and follow the fallback in §5, do not proceed as if it
  passed.
- **Whether a "Report image" UI should exist.** Default: no (§2.6). If asked to add one
  later, it's additive to the existing conduct-report flow, not a new subsystem.

---

## 4. Scope boundary — do not touch

- `PortfolioImage` upload/review flow (`Designs/`, `Portfolio*`) — untouched, unrelated
  feature, already has its own upload path.
- `LoginRequest`, `LoginCommand`, `IdentityService.GenerateJwt`, `TenantMiddleware`, or any
  JWT claim shape — this feature adds **zero** claims (§2.2). If you find yourself about to
  add a claim for "does this user have an avatar," stop — that's resolved via
  `GET /users/me/profile`, not a claim.
- `UpdateMyStudioCommand` / `UpdateStudioRequest` — do **not** add `LogoUrl`/`CoverImageUrl`/
  `CoverFocalX`/`CoverFocalY` fields to this request. See §6.2 for why (it's a sharper reason
  than the draft spec gave — read it, don't skip it).
- `GetPresignedUploadUrlQuery` / `GetPresignedUploadUrlHandler` / `FileEndpoints.cs`'s
  existing `POST /api/v1/files/presign` route — leave entirely alone. All new upload flows
  are new, separate commands/routes (§6.5, §7.2, §8.2). Do not add a `kind` parameter to the
  existing generic presign endpoint.
- `ImpersonationAllowList.cs` — do not add entries for the new routes (§2.13). If you find
  yourself editing this file for this feature, stop; you've misread §2.13.
- `Artist.AvatarUrl` — do not write to it anywhere in new code (§2.11). Reading it is also
  gone by the end of Phase B.
- `docs/user-manual.html` (the stale copy, §2.15) and any file under `Pena_e_Arte.Contracts`
  unrelated to this feature's new request/response records.
- Stripe/POK payment code, Hangfire job **registration** wiring beyond adding the one new job
  in §6.7 (don't touch existing job schedules).

---

## 5. Phase 0 — Pre-flight gates (run first, before any code)

### 5.1 R2 `Cache-Control` via the Cloudflare Worker (D10, §2.10)
You cannot reach the Cloudflare dashboard from this autonomous session. Do not guess. Ship
Phase A/B/C using `Cache-Control: public, max-age=31536000, immutable` as designed (§6.3),
but add a one-line TODO comment at the `R2Service.UploadAsync` call site in
`ImageCommitService` (§6.4) reading: `// TODO(2026-09-19): verify the Cloudflare Worker
forwards this header before relying on it for cache-hit rate — see
docs/claude/overnight-prompt-studio-profile-images-2026-09-19.md §2.10`. Note this
explicitly in your final report (§13) as an unverified-by-this-session item, not a done item.

### 5.2 ImageSharp license (D3, §2.3)
Attempt to add `SixLabors.ImageSharp` (latest stable). Check its license terms
(`https://www.nuget.org/packages/SixLabors.ImageSharp` license link, or the package's own
`LICENSE`/`COMMERCIAL_LICENSE.md` if bundled) against this business's situation the same way
you'd sanity-check any dependency license — if you cannot make a confident determination
within this autonomous session, **default to `SkiaSharp` (MIT)** instead and note in your
final report that ImageSharp needs a human license decision before it's reintroduced. Either
way, isolate the choice behind `IImageProcessor` (§6.4) so swapping later is a one-file change.

---

## 6. Phase A — Shared pipeline + Studio logo/cover + Admin moderation + Help

### 6.1 Domain + migration

`Pena_e_Arte.Domain/Entities/Studio.cs` — add three properties (after `CoverImageUrl`):

```csharp
public string? LogoUrl { get; set; }
public double CoverFocalX { get; set; } = 0.5;
public double CoverFocalY { get; set; } = 0.5;
```

New file `Pena_e_Arte.Domain/Entities/ImageKind.cs`:

```csharp
namespace Pena_e_Arte.Domain.Entities;

public enum ImageKind
{
    Logo,
    Cover,
    ArtistPhoto,
    AccountAvatar,
}
```

New file `Pena_e_Arte.Domain/Entities/UserProfile.cs` (mirrors `UserOnboardingState`'s shape
exactly — private setters, static factory, explicit mutator):

```csharp
namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// A signed-in user's account-level profile image. Not tenant-scoped — the same person can
/// be a client of several studios, and an owner/artist can switch tenants; the avatar
/// follows the account, not the tenant. No EF query filter (same shape as
/// UserOnboardingState/FeedbackReport/AuditLogEntry) — every handler touching this table
/// MUST scope by the caller's own UserId, or be an explicit AdminOnly path. There is no
/// filter to save you. See docs/claude/architecture.md's "IgnoreQueryFilters() Approved
/// Usages" table for the admin-removal path's entry.
/// </summary>
public class UserProfile
{
    private UserProfile() { }

    public Guid UserId { get; private set; }
    public string? AvatarUrl { get; private set; }
    public DateTime? AvatarUpdatedAt { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public static UserProfile Create(Guid userId) => new() { UserId = userId };

    public void SetAvatar(string url)
    {
        AvatarUrl = url;
        AvatarUpdatedAt = DateTime.UtcNow;
    }

    public void RemoveAvatar()
    {
        AvatarUrl = null;
        AvatarUpdatedAt = DateTime.UtcNow;
    }
}
```

`UserProfile.UserId` is the PK (configure in `AppDbContext`'s `OnModelCreating`:
`builder.Entity<UserProfile>().HasKey(x => x.UserId);` — no separate `Id`, since it's a 1:1
extension of the Identity user, same spirit as `UserOnboardingState` but keyed directly on
the user rather than having its own surrogate key, since there's exactly one row per user
here (unlike onboarding state, which has one row per user *per role*)).

Rows are created lazily on first `SetAccountAvatarCommand` — no backfill.

**Migration** (Phase A's own, additive only):

```bash
dotnet ef migrations add AddStudioLogoAndFocalPointAndUserProfile --project Pena_e_Arte.Infrastructure
```

Confirm the generated migration only adds columns/a table (no data loss, no non-null
without a default) before running `dotnet ef database update`.

### 6.2 Why studio images are separate commands, not fields on `UpdateStudioRequest` (corrected reasoning)

The draft spec justified this by claiming `UpdateMyStudioCommand` is "a full-replace... its
own comments show that omitted fields get nulled." **That's not what the handler does.**
Reading `UpdateMyStudioHandler.Handle` tonight shows it's a **selective** updater — it sets
exactly `Name`, `City`, `Latitude`, `Longitude`, `PhoneNumber`, `Timezone`, `AddressLine1/2`,
`PostalCode`, and conditionally `Nipt`. It does **not** touch `Description`,
`CoverImageUrl`, `PokMerchantId`, or anything else not in that list — so today, nothing about
this handler would null an image field just by existing.

The real, sharper reason to keep images separate: this handler already has a **documented
scar from exactly this failure mode**. Its own comment block explains why
`InstagramHandle` is deliberately *not* written here anymore: `"the frontend form no longer
collects it... writing it unconditionally would silently null out the legacy column... on
every unrelated save."` That's the Instagram-handle regression this feature must not repeat.
If `LogoUrl`/`CoverImageUrl`/`CoverFocalX/Y` were added as fields on
`UpdateStudioRequest`, the Studio Settings form would have to always round-trip the
current image URLs on every unrelated save (name change, phone number edit, etc.) or risk
silently nulling them the exact way Instagram once was. Separate commands
(`SetStudioImageCommand`, `RemoveStudioImageCommand`, `SetCoverFocalPointCommand`) avoid the
failure mode by construction, matching the pattern the codebase already moved *toward* for
Instagram (a dedicated `UpdateSocialHandleCommand` writing to `SocialAccountLink`, per that
same comment). Cite this precedent, not "full replace," in the PR description.

### 6.3 `IImageProcessor` and the per-kind processing table

New file `Pena_e_Arte.Domain/Interfaces/IImageProcessor.cs`:

```csharp
namespace Pena_e_Arte.Domain.Interfaces;

public record ImageVariant(int Width, byte[] Bytes, string ContentType);

public record ProcessedImage(IReadOnlyList<ImageVariant> Variants, int SourceWidth, int SourceHeight);

public interface IImageProcessor
{
    /// <summary>Reads only the header — no full decode — to get dimensions cheaply, so the
    /// decompression-bomb pixel cap can reject before any expensive work.</summary>
    (int Width, int Height)? ReadDimensions(ReadOnlySpan<byte> header);

    /// <summary>Auto-orients (EXIF), strips ALL metadata, resizes to each requested width,
    /// re-encodes to WebP (or JPEG for the og-image variant), preserving alpha for square
    /// kinds and flattening for cover.</summary>
    ProcessedImage Process(byte[] sourceBytes, ImageProcessingOptions options);
}

public record ImageProcessingOptions(
    ImageKind Kind,
    IReadOnlyList<int> OutputWidths,
    bool PreserveTransparency,
    bool IncludeOgJpeg,
    double FocalX = 0.5,
    double FocalY = 0.5);
```

`ImageProcessingOptions` per kind (a small static lookup, e.g.
`ImageProcessingDefaults.For(ImageKind kind)`), encoding the draft spec's §4 table verbatim:
account avatar / artist photo / logo → widths `[128, 512]`, square crop, alpha preserved, no
og-jpeg; cover → widths `[640, 1280, 1920]`, landscape, flattened, `IncludeOgJpeg: true`.
Byte/pixel/dimension caps (5 MB / 10 MB source, 25 MP decoded-pixel cap, 256×256 /
1200×400 minimums) are enforced by `ImageCommitService` (§6.4) before `Process` is ever
called, using `ReadDimensions` on just the header bytes.

`Pena_e_Arte.Infrastructure/Services/ImageProcessor.cs` implements this against whichever
library survives §5.2. Magic-byte sniffing (JPEG `FF D8 FF`, PNG `89 50 4E 47`, WebP
`RIFF….WEBP`) happens in `ImageCommitService`, not here — this class only ever receives
bytes already confirmed to be a real image of the claimed type.

### 6.4 `ImageCommitService` — the shared two-phase commit logic

New file `Pena_e_Arte.Application/Images/ImageCommitService.cs`. This is the one
implementation of draft-spec §6.2 steps 3a–3h, shared by all four image kinds' commit
handlers (studio logo/cover here in Phase A; account avatar in Phase B; artist photo in
Phase C):

```csharp
public interface IImageCommitService
{
    Task<CommittedImage> CommitAsync(
        string pendingUploadKey,
        string expectedOwnerPrefix,       // e.g. "pending/user-{userId}/" — caller-scoped
        string targetKeyPrefix,            // e.g. "{studioId}/branding/logo/{imageId}/"
        ImageProcessingOptions options,
        CancellationToken ct);
}

public record CommittedImage(string PrimaryUrl, IReadOnlyDictionary<int, string> UrlsByWidth, string? OgJpegUrl);
```

Steps (verbatim from the draft, now grounded in the two new `IR2Service` members from
§2.14):

1. Validate `pendingUploadKey` starts with `expectedOwnerPrefix` — throw
   `ImageUploadNotFoundException` (maps to `IMAGE_UPLOAD_NOT_FOUND`, §6.8) otherwise. This
   is the only thing standing between one user and another user's pending upload — get the
   test for this right (§6.9).
2. `await r2.HeadObjectAsync(pendingUploadKey, ct)` — null → `IMAGE_UPLOAD_NOT_FOUND`
   (already committed or expired). Size over the kind's max → `IMAGE_TOO_LARGE`.
3. `await r2.OpenReadStreamAsync(...)`, read into a bounded `byte[]` (cap at the same max
   size — never trust the HEAD response alone), sniff magic bytes against the three
   supported formats → mismatch or unrecognized → `IMAGE_UNSUPPORTED_TYPE`.
4. `processor.ReadDimensions(bytes)` → null (corrupt header) → `IMAGE_CORRUPT`; under the
   kind's minimum → `IMAGE_TOO_SMALL`; decoded-pixel product over 25,000,000 → reject as
   `IMAGE_TOO_LARGE` **without calling `Process`**.
5. `processor.Process(bytes, options)` → catch any decode exception as `IMAGE_CORRUPT`
   (never let a malformed file's exception escape raw — this is the hostile-input boundary,
   see the fixtures in §6.9).
6. For each variant, `await r2.UploadAsync(targetKeyPrefix + $"{variant.Width}.webp",
   variant.Bytes, variant.ContentType, cacheControl: "public, max-age=31536000, immutable",
   ct)` — this requires the `cacheControl` overload on `R2Service.UploadAsync` mentioned in
   the draft's §6.5; add it now (additive optional parameter, default `null` → today's
   behavior, zero existing call sites change).
7. Return the `CommittedImage`; the caller (a MediatR command handler) is responsible for
   the DB write (new URL) and for remembering the *previous* URL so it can delete it after
   `SaveChangesAsync` succeeds (best-effort `DeleteAsync` per variant, logged on failure,
   swept by §6.7 if it fails).
8. Best-effort delete the now-committed `pendingUploadKey` itself.

Rate limiting (§6.6) and authorization happen in the calling command/handler, not here —
this service is purely the shared mechanical part.

### 6.5 Studio logo/cover — commands, validators, endpoints

New commands in `Pena_e_Arte.Application/Studios/Commands/Images/`:
`PresignStudioImageCommand`, `SetStudioImageCommand`, `SetCoverFocalPointCommand`,
`RemoveStudioImageCommand`. `kind` is validated against `ImageKind.Logo`/`ImageKind.Cover`
only (a studio image command receiving `ArtistPhoto`/`AccountAvatar` is a bug, not a
409 — validator rejects it outright). Studio comes from `tenant.StudioId` — never from the
request body (the endpoint route already scopes to `/studios/me/...`).

`SetStudioImageCommand`/`RemoveStudioImageCommand` implement `IAuditableCommand`
(`AuditAction: "Studio.ImageUpdated"` / `"Studio.ImageRemoved"`,
`AuditTargetType: "Studio"`, `AuditTargetId: tenant.StudioId`) — metadata limited to `kind`
and the new `imageId` (never a full URL, never anything else).

Endpoints in a new `Pena_e_Arte.API/Endpoints/StudioImageEndpoints.cs` (called from
`MapStudioEndpoints` or its own `MapStudioImageEndpoints` extension, your call, follow
whichever grouping convention `StudioEndpoints.cs` already uses):

```
POST   /api/v1/studios/me/images/{kind}/presign      OwnerOnly
PUT    /api/v1/studios/me/images/{kind}               OwnerOnly
PATCH  /api/v1/studios/me/images/cover/focal-point    OwnerOnly
DELETE /api/v1/studios/me/images/{kind}               OwnerOnly
```

All four get `.RequireRateLimiting("image-upload")` (§6.6). `{kind}` route parameter maps to
`logo` / `cover` only for these routes — validate the route string against a closed set in
the endpoint handler before it ever reaches MediatR (reject anything else with 400, not by
falling through to the enum validator).

Response DTOs — additive fields only: `StudioResponse`, `MyStudioResponse`,
`PublicStudioResponse`, `NearbyStudioResponse` (or whatever the exact public DTO names are —
confirm against `GetNearbyStudiosQuery`/`GetPublicStudioQuery`/`GetMyStudiosQuery`'s actual
return types before editing, the draft's §7.5 names may not match 1:1) each gain `LogoUrl`,
`CoverFocalX`, `CoverFocalY`.

### 6.6 Rate limiting

New Redis-backed policy in `RateLimitingExtensions.cs`, following the exact
`AddRedisPolicy(...)` pattern already there (see the confirmed current file — a `//` comment
table above the policy list documents each one; add a row): `image-upload | 10 | 10 min`,
partitioned by the authenticated user (`AuthenticatedPartitionKey`, the same helper
`billing`/`external-api` already use — every image route requires auth, so IP-partitioning
would be wrong here same as those two). Applied to every presign/commit/remove route across
all three phases (studio, account, artist) — add it once in Phase A, reference it in
Phases B and C.

### 6.7 Cleanup job

New `Pena_e_Arte.Infrastructure/Jobs/PendingImageCleanupJob.cs`, copying
`GuestPendingUploadCleanupJob`'s exact structure (list-prefix → filter by age → delete,
one-failure-doesn't-block-the-rest, structured log with counts only): sweeps `pending/`
(the new prefix root, distinct from that job's `appointments/guest-pending/`), TTL 2 hours
(shorter than the guest job's 48h — these are authenticated uploads expected to complete in
minutes, not abandoned guest forms). Register it in Hangfire alongside the existing jobs
(same registration file/pattern `StorageReconciliationJob`/`GuestPendingUploadCleanupJob`
use) at an hourly cadence. A second, weekly orphan sweep (compares `avatars/`+`*/branding/`+
`*/artists/` folders against DB references) can be the same job class with a second
`RunOrphanSweepAsync` method and a second Hangfire recurring-job registration at a different
cron — don't over-engineer two separate classes for this.

### 6.8 Error codes

`IMAGE_TOO_LARGE`, `IMAGE_TOO_SMALL`, `IMAGE_UNSUPPORTED_TYPE`, `IMAGE_CORRUPT`,
`IMAGE_UPLOAD_NOT_FOUND`, `IMAGE_RATE_LIMITED` — new exception types mapped in whatever
central exception-to-HTTP-response middleware already exists (find it — `NotFoundException`
and `DuplicateNiptException` are both already mapped somewhere; add these five alongside
them, same mechanism, same file).

### 6.9 Admin moderation removal (studio images only in Phase A; user/artist removal ships with their phases)

`DELETE /admin/studios/{id}/images/{kind}` — `AdminOnly`, body `{ reason }` (closed enum:
`InappropriateContent`, `Impersonation`, `CopyrightClaim`, `Other`). Uses
`IgnoreQueryFilters()` to find the studio across tenants — **add this usage to
`docs/claude/architecture.md`'s "IgnoreQueryFilters() Approved Usages" table in this same
change** (§9.4), with the explicit `admin`-role check right next to it per `CLAUDE.md` rule
1. Implements `IAuditableCommand` (`Studio.ImageRemovedByAdmin`, metadata = `kind` + `reason`
   enum, never free text). Sends the existing notification-service template to the studio
   owner ("An image on your studio was removed by TattooOS support — reason: …") — reuse
   whatever notification-sending abstraction other admin actions already use, don't build a
   new one.

### 6.10 Frontend — shared building blocks + studio settings card

`frontend/src/shared/components/`: `ImageUploadDialog.tsx` (pick → crop/focal-point →
confirm → uploading → done, per draft §9.1), `StudioLogo.tsx`, `StudioCover.tsx`,
`ImageWithFallback.tsx` (404/deleted → initials, not a broken glyph). `frontend/src/shared/
utils/imageVariant.ts` (`imageVariant(url, width)`; unrecognized URL shape → returned
unchanged, covers legacy/seeded data). `frontend/src/shared/hooks/useImageUpload.ts`
(presign → PUT → commit, reuses but does not modify `usePresignedUpload`). New dependency
`react-easy-crop` (§2.7) for the square-crop UI; the focal-point picker is a small in-house
draggable-dot component (no library — per draft §9.1/§9.5, must support arrow-key nudging
for keyboard operability).

Studio Settings gets a new "Studio images" card **above** "Studio details" (per the draft's
§9.2 placement) — this means the onboarding-tour selector for the existing
`owner-studio-profile-nav`/first-settings-card step in `ownerTour.ts` needs its target
re-checked (§6.11) since a new element now sits above it in DOM order, even though the
selector itself (`data-tour="owner-studio-profile-nav"` targets the *nav link*, not the
card, per the confirmed `ownerTourSteps` array tonight — so this specific step is
unaffected; double-check there isn't a second step targeting something inside the Studio
Settings page itself that a new card pushes down).

### 6.11 Display sweep (Phase A's slice — logo/cover only; artist/avatar sweep is Phase C)

- Discover card (`DiscoverPage.tsx`) → cover (with `object-position` from focal point) +
  logo badge overlapping bottom-left when both exist; logo alone replaces the monogram tile
  when there's no cover; both absent → today's `StudioMonogram`, unchanged.
- Studio page hero (`StudioPortfolioPage.tsx`) → cover (focal), logo as an overlapping
  header avatar; `og:image` → the new `og.jpg` variant (fallback: current default); JSON-LD
  `logo` + `image` fields added.
- Embed (`EmbedPage.tsx`) → cover banner; logo in the header row.
- My Studios (`MyStudiosPage.tsx`) `StudioAvatar` → `logo ?? monogram` — **stop using the
  cover as an avatar** (this is the bug D1 exists to fix, confirm the old cover-as-avatar
  code path is actually removed, not left as a fallback before the monogram).
- Admin studio list/detail → logo.
- RTK Query: add `Studio` tag invalidation (confirm the tag already exists — the draft
  assumed so, verify against the actual `studiosApi` slice) on every set/remove-image
  mutation.

### 6.12 Help sync (Phase A's slice — mandatory in this same commit, not a follow-up)

`frontend/src/features/help/helpContent.ts` — add one new article: id
`owner-studio-images`, `roles: [Owner]`, route `/studios/me`, title "Add your studio logo
and cover photo," summary/steps/tips per the draft's §13, `relatedArticleIds` including
`owner-studio-profile` (the existing studio-details article, confirmed present tonight) and
`owner-branding`. Update the existing `owner-studio-profile` article's `steps`/`tips` to
mention the new "Studio images" card sits above the main form (confirmed this article's
current content tonight covers name/address/phone/description/NIPT/timezone/hours — it does
not currently mention images at all, so this is a genuine addition, not a rewrite).

`frontend/public/user-manual/index.html` (the confirmed-live copy, §2.15) — add a matching
section, same heading/anchor style as its existing ~247 headings (don't touch
`docs/user-manual.html`).

`frontend/src/features/help/tours/ownerTour.ts` — add one new, skippable step targeting a
new `data-tour="owner-studio-images-card"` attribute you add to the new card; do not
renumber or remove existing steps, just insert this one in DOM order (before the existing
`owner-studio-profile-nav` step, since the card now sits above studio details on the page —
though note per §6.10 that existing step targets the *nav link*, not page content, so
sequencing relative to it is a minor UX call, not a correctness issue).

`docs/claude/architecture.md` — add a Decisions Log entry: "Images: two-phase upload
(presign→commit), UserProfile side table for account avatars, focal-point cover with
multi-surface preview, IAvatarResolver precedence (owner-artist dual-role aware),
ProfileImageUrl canonicalized as the artist-photo field (AvatarUrl deprecated, see database
.md)" — dated 2026-09-19. Add the new admin `IgnoreQueryFilters()` usage to that table
(§6.9). Add `UserProfile` and `ImageKind`/`Studio.LogoUrl` to the Feature Module Map.

---

## 7. Phase B — Account avatar (all roles) + `/account/profile` + resolver + messaging + erasure/export

### 7.1 `UserProfile` migration
Already created in Phase A (§6.1) if you're running all three phases in one session as
intended (§2.8) — the table exists by the time Phase B starts. If Phase A and B are somehow
split across sessions, Phase B's own migration only needs to exist if Phase A's didn't
already add `UserProfile` — don't create it twice.

### 7.2 Commands, validator, endpoints
`PresignAccountAvatarCommand`, `SetAccountAvatarCommand`, `RemoveAccountAvatarCommand`,
query `GetMyProfileQuery`. **No `ICurrentTenant` dependency in any of these three command
handlers** (§2.12 — this is load-bearing, re-read it). Scoped to `currentUser.UserId`
(whatever the existing current-user-resolution abstraction is called — confirm the exact
interface name, likely `ICurrentUser`, by grepping alongside `ICurrentTenant`, since the
draft didn't name it precisely).

```
GET    /users/me/profile           ClientAndAbove
POST   /users/me/avatar/presign    ClientAndAbove
PUT    /users/me/avatar            ClientAndAbove
DELETE /users/me/avatar            ClientAndAbove
```

Key layout: `avatars/{userId}/{imageId}/{128|512}.webp` — outside every studio prefix,
confirmed by design to be excluded from `StorageReconciliationJob`'s per-studio sweep (it
only lists `{studio.Id}/` prefixes, so `avatars/` is naturally invisible to it — no code
change needed to exclude it, just don't accidentally nest it under a studio prefix).

`SetAccountAvatarCommand`/`RemoveAccountAvatarCommand` implement `IAuditableCommand`
(`User.AvatarUpdated`/`User.AvatarRemoved`, `AuditTargetType: "User"`,
`AuditTargetId: currentUser.UserId`, `AuditStudioId: null` — genuinely not tenant-scoped,
per `IAuditableCommand.AuditStudioId`'s own doc comment about falling back to `null` for
"commands that don't expose a resolvable studio id").

Rate limit: `image-upload` policy from §6.6, same as everywhere else.

### 7.3 `IAvatarResolver` — one implementation, batch-safe

New `Pena_e_Arte.Application/Images/IAvatarResolver.cs`:

```csharp
public interface IAvatarResolver
{
    Task<string?> ResolveForArtistFacingAsync(Guid artistId, CancellationToken ct);
    Task<string?> ResolveForAccountFacingAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, string?>> ResolveManyAccountFacingAsync(
        IEnumerable<Guid> userIds, CancellationToken ct);
}
```

Precedence (corrected field name, per §2.11):

- **Artist-facing** (studio page, booking, artist portfolio page): `Artist.ProfileImageUrl`
  → account avatar of `Artist.UserId` (via `UserProfile`) → `null` (frontend shows
  initials).
- **Account-facing** (header, messaging, staff lists): account avatar → (if the user is an
  artist in the *current* tenant — check via `ICurrentTenant.StudioId` and the artist's
  `TenantEntity` scoping, since `Artist` is tenant-scoped) `Artist.ProfileImageUrl` → `null`.

`ResolveManyAccountFacingAsync` issues one query for the batch (join `UserProfile` on the
id set) — no N+1 in `GetConversationsQuery`/`GetConversationContactsQuery`/clients-list
endpoints once they switch to it.

**Replace all four messaging call sites in this same change**
(`CreateConversationCommand.cs:77`, `ConversationEligibility.cs:55,86`,
`GetConversationContactsQuery.cs:36`, `GetConversationsQuery.cs:75`) — each currently reads
`a.AvatarUrl` directly; route them through `IAvatarResolver` instead (account-facing
precedence, since messaging is an account-facing surface, not artist-facing) so
`Artist.AvatarUrl` has zero remaining readers by the end of this phase, matching §2.11's
plan exactly.

### 7.4 `/account/profile` page + header wiring
New route `/account/profile`, added to `UserMenu.tsx` as a new "Profile" item **above**
"Change email" (confirmed tonight: `UserMenu.tsx` currently has exactly two items — "Change
password" and "Change email" — plus logout; "Profile" is a genuinely new third item, not a
rename of anything existing). `UserChip.tsx` (confirmed tonight: renders `user.name`/
`user.email` split and a colored-initial circle, zero image logic today) gets its initial
circle replaced by the new `UserAvatar.tsx` shared component (§6.10-style wrapper, same
dimensions, so there's no layout shift — `UserChip`'s current `h-7 w-7` circle size is the
target size to preserve).

### 7.5 RTK Query cache invalidation and the account-switch flash bug
Add a `UserProfile` tag. Per the draft's §9.4 (PR #145 finding, confirmed real by the commit
history — `2e3caac7 fix: refresh 'my artist profile' UI immediately after self-delete` is
adjacent evidence this codebase has hit stale-RTK-Query-cache bugs on identity changes
before): the profile query must clear/refetch on logout and on the dual-role Owner⇄Artist
switch (`c4d9e99e feat: add header Owner/Artist switcher for dual-role owners` — confirmed
this switcher exists) and on studio switch, so a previous user's or previous tenant's
avatar can never flash for the next context. Write the e2e test for this specifically
(§7.8) — it's a real, already-precedented bug class in this codebase, not a hypothetical.

### 7.6 Impersonation
Add the regression test described in §2.13 — confirm all four new avatar routes return
`IMPERSONATION_SCOPE_DENIED` (or whatever the existing denial response shape is — check how
`ImpersonationAllowList` denial surfaces to the client, likely via `TenantMiddleware`) under
an impersonation-scoped token. Expect this test to pass with **zero** production code
changes, per §2.13 — if it fails, you've found a real gap in `TenantMiddleware`'s enforcement
of the allow-list, which is a bigger finding than this feature and should be flagged loudly
in your final report, not silently patched as a side effect of this PR.

### 7.7 Erasure, export, deletion (compliance — do not skip)

- `RetentionPurgeJob.AnonymizeErasedClientsAsync` — when anonymizing a client, also call
  `DeleteAsync` on the user's avatar objects (both variants) and null `UserProfile.AvatarUrl`
  (via `RemoveAvatar()`, not direct field access — respects the entity's own encapsulation).
  Locate the exact method by that name in `Pena_e_Arte.Infrastructure/Jobs/
  RetentionPurgeJob.cs` before editing — read its current body first, don't guess its shape
  from the draft spec alone.
- Owner/artist/admin account deletion and the client self-deletion path (confirmed shipped
  recently: `34ca731c Fix cross-studio erasure fan-out; add client archive, self-service
  export, and erasure cancel` is the most recent commit on this branch tonight) — delete
  avatar objects and the `UserProfile` row in the same operation, once per account (not per
  tenant/studio the account touches — `UserProfile` is account-level by design, same
  "delete once" lesson the cross-studio fan-out fix (PR #153, referenced in that commit)
  already had to learn the hard way for other account-level data).
- `ExportMyDataQuery` — include the avatar as a presigned, time-limited read URL, same
  mechanism it already uses for consent PDFs (read the query's current body first — confirm
  its actual presign-generation call site before adding to it).
- Client archive (`ArchivedAt`, confirmed to exist per that same recent commit) does **not**
  touch the avatar — archive is non-destructive, matches the draft's own §8.3 note.

### 7.8 Frontend tests
Vitest: `UserAvatar` (fallback, srcset, error→initials), `useImageUpload` state machine,
the new `/account/profile` page states (loading/error/empty/populated), `imageVariant`
(including a legacy/malformed URL passthrough case). Playwright e2e: upload → crop → save →
avatar appears in header immediately; remove; the account-switch-flash regression from
§7.5 specifically (switch Owner→Artist, or switch studios as a multi-studio client, and
assert the previous context's avatar is never momentarily visible); studio-less client can
set an avatar (the §2.12 scenario, end-to-end through the real UI, not just the handler
unit test).

### 7.9 Help sync (Phase B's slice)
`helpContent.ts` — new article id `account-profile-photo`, `roles: [Client, Artist, Owner,
Admin]`, route `/account/profile`, title "Change your profile photo." `frontend/public/
user-manual/index.html` — matching section. `clientTour.ts` — mention the profile photo
option in the account-menu step (find the existing account-menu-related step, or add one if
none exists — read the file first). `artistTour.ts`, `ownerTour.ts` — one-line mention each
if they already have an account-menu-adjacent step; otherwise state explicitly in your final
report that no tour change was needed and why (per the project rule: an explicit "no" with
reasoning, never a silent skip).

`docs/claude/architecture.md` — extend the same Decisions Log entry from §6.12 (don't create
a second entry for the same feature) noting Phase B shipped the resolver and the messaging
field migration off `AvatarUrl`. `docs/claude/database.md` — document `UserProfile` (new
table) and the `Artist.ProfileImageUrl`/`AvatarUrl` resolution from §2.11 in the Artist
section.

---

## 8. Phase C — Artist photo write path + full display sweep

### 8.1 Commands, endpoints
`PresignArtistPhotoCommand`, `SetArtistPhotoCommand`, `RemoveArtistPhotoCommand`. Write
target is `Artist.ProfileImageUrl` (§2.11 — **not** `AvatarUrl`).

```
POST   /api/v1/artists/{id}/photo/presign     ArtistAndAbove
PUT    /api/v1/artists/{id}/photo              ArtistAndAbove
DELETE /api/v1/artists/{id}/photo              ArtistAndAbove
```

Authorization (handler-level, matching the pattern the 2026-07-01 Artist QA pass
established after finding 11 scope leaks — commit `overnight-prompt-artist-qa-polish-2026-
07-01.md` is the precedent to reread if you need the exact shape of that fix): an `artist`
may act only when `artist.UserId == currentUser.UserId`; an `owner` may act on any artist in
their **own tenant** (both the tenant query filter and an explicit
`artist.StudioId == tenant.StudioId`-equivalent check — belt and braces, don't rely on the
filter alone for an authorization decision); anyone else → **404, not 403** (existence
leak). Write a test per role combination — this is the single highest-risk authorization
surface in this entire feature given the documented history of leaks here.

Key layout: `{studioId}/artists/{artistId}/{imageId}/{128|512}.webp` — inside the studio
prefix, so it's automatically counted by `StorageReconciliationJob`.

`SetArtistPhotoCommand`/`RemoveArtistPhotoCommand` implement `IAuditableCommand`
(`Artist.PhotoUpdated`/`Artist.PhotoRemoved`, `AuditTargetType: "Artist"`,
`AuditTargetId: artistId`, `AuditStudioId: artist.StudioId` explicitly — this one *can*
resolve a studio id directly from the command, per `IAuditableCommand.AuditStudioId`'s own
doc comment about explicit targets on admin-style commands).

### 8.2 Admin moderation extension
`DELETE /admin/artists/{id}/photo` and `DELETE /admin/users/{userId}/avatar` — same shape as
§6.9's studio-image admin route, same reason enum, same audit pattern, same
`IgnoreQueryFilters()` table update (one combined `architecture.md` edit covering all three
admin routes from across the three phases, not three separate edits).

### 8.3 Full display sweep
- Artist cards/detail, public artist page, booking artist picker → `IAvatarResolver`'s
  artist-facing resolution (§7.3), replacing whatever ad-hoc `ProfileImageUrl`/initials
  logic each currently has.
- Client `ClientCard`/`ClientDetailPage` (owner/artist-facing only, never public, per §2.5)
  → account-facing resolver.
- Messaging thread/inbox/new-conversation dialog — already switched to the resolver in
  Phase B (§7.3); confirm nothing regressed.
- Public mapper unit test (§8.5) — every `Public*Response` mapper contains no client/account
  avatar field, full stop.

### 8.4 Rate limit, error codes, cleanup job
Reuse `image-upload` (§6.6), the five error codes (§6.8), and `PendingImageCleanupJob`
(§6.7) — no new infrastructure needed in this phase, only new call sites.

### 8.5 Tests
Unit: authorization matrix (§8.1); resolver precedence (both directions, including the
dual-role owner-artist case — an owner who is also an artist has one account avatar and one
`Artist.ProfileImageUrl`, confirm the resolver picks correctly for both an artist-facing and
an account-facing view of the same person); public-mapper-contains-no-private-avatar test.
Integration (real MySQL, per the project's own "tenant-filter bugs are only catchable there"
finding, 2026-09-11): artist-photo commands respect tenant scoping; the full authorization
matrix from §8.1 against a real database, not mocks. E2E: artist sets own photo, appears on
public artist page within seconds; owner sets a colleague's photo; a third artist is denied;
mobile viewport pass (per the draft's §9.6 note that PR #62's mobile baseline bugs were only
caught by a manual/real-browser pass, not jsdom).

### 8.6 Impersonation regression test
Same as §7.6, applied to the three new artist/admin routes.

### 8.7 Help sync (Phase C's slice — closes out the feature)
`helpContent.ts` — new article `owner-artist-photo` (`roles: [Owner, Artist]`), and
`admin-image-moderation` (`roles: [Admin]`, "Why was an image removed?" / "Remove an
inappropriate image"). `frontend/public/user-manual/index.html` — matching sections.
`artistTour.ts` — one step mentioning "add your photo" if the artist's own profile page has
a tour step to attach it to (read the file first, per §7.9's same instruction). `ownerTour.ts`
— one line noting artists can be given a photo from the artist detail page, attached to
whatever step already covers "Add your artists" (confirmed present: the
`owner-add-artist-nav` step).

Final `docs/claude/architecture.md` Decisions Log entry update (same entry as §6.12/§7.9,
now complete across all three phases) and Feature Module Map row for the whole feature.

---

## 9. Constraints (restated — apply to all three phases)

- No new NuGet/npm package beyond the two explicitly approved here (`SixLabors.ImageSharp`
  or its `SkiaSharp` fallback per §5.2; `react-easy-crop` per §2.7) without flagging it as a
  prerequisite decision in your final report.
- No `useEffect` for data fetching (RTK Query only) — the approved exceptions, if any, are
  whatever `frontend.md` already documents; this feature introduces none of its own.
- TypeScript strict, no `any`, anywhere in the new frontend code.
- Explicit C# types, no `var` for non-obvious types, anywhere in the new backend code.
- No business logic in Minimal API endpoint handlers — MediatR command/query + FluentValidation
  validator for every one, no exceptions.
- Tenant isolation via EF Core global query filters everywhere, **except** the specific,
  individually-flagged `IgnoreQueryFilters()` usages in §6.9/§8.2 (admin moderation) and the
  pre-existing non-tenant-scoped pattern `UserProfile` deliberately follows (§6.1) — every
  other read/write in this feature goes through the normal tenant-scoped `Artist`/`Studio`
  queries.
- Every new endpoint has `.RequireAuthorization()` with a named policy — no new
  `AllowAnonymous` route anywhere in this feature (public display reuses existing public
  DTOs/queries, it doesn't need new anonymous routes).
- Never log PII — image processing logs `kind`, `imageId`, `bytesIn`, `bytesOut`,
  `durationMs`, and the rejection reason code only; never a file name, a user's name/email,
  or a full personal-avatar URL. Structured Serilog only, `tenant_id`/`user_id`/`request_id`
  on every log line per `CLAUDE.md` rule 3.
- Tests ship with every phase — see §6.9(tests are folded into each phase's own section
  above, not a separate appendix)/§7.8/§8.5.
- Add a Prometheus counter `image_uploads_total{kind,outcome}` and histogram
  `image_processing_seconds{kind}` following whatever the existing observability
  conventions file (`architecture.md`'s relevant section) already establishes for other
  counters — don't invent a new metrics pattern for this feature.

---

## 10. Industry-standard benchmark note (CLAUDE.md rule #6)

Re-checked against the current market tonight (2026-09-19): Vagaro, Fresha, Boulevard,
Mindbody, Zenoti, and GlossGenius are all still active, still the comparison set the market
itself uses (confirmed via current "vs" and "alternatives" comparison content from
PatientNow, Zenoti's own blog, Pabau, TheSalonBusiness, and GlossGenius's own site, all
published in 2026) — no shutdowns, no new dominant entrant that changes this feature's
target bar. A business logo + cover photo on the public profile, staff/provider photos on
booking and profile pages, a personal profile photo in the signed-in shell, upload with
crop/preview, remove/replace, and a graceful initials fallback are baseline expected
functionality across this entire category and have been for years — this spec meets every
item. Deliberately ahead of the minimum: focal-point cover with live multi-surface preview,
server-side EXIF stripping and re-encoding, immutable-CDN caching, and GDPR-complete erasure/
export (§7.7). Deliberate v1 gaps, flagged not hidden (§3): no automated content moderation,
no branded-email logo usage yet, no multi-photo studio gallery beyond the existing portfolio
feature.

Sources consulted tonight: [Vagaro alternatives 2026 — PatientNow](https://www.patientnow.com/resources/compare/vagaro-alternatives), [Best salon software 2026 — Zenoti's The Check-in](https://www.zenoti.com/thecheckin/best-salon-management-software-2026), [Fresha alternatives 2026 — Pabau](https://pabau.com/blog/fresha-alternatives/), [Best salon software guide 2026 — TheSalonBusiness](https://thesalonbusiness.com/best-salon-software/), [Vagaro alternatives — GlossGenius](https://glossgenius.com/blog/vagaro-alternatives).

---

## 11. Do-not-build-blind list (full implementation-ready specs exist above; these do not ship tonight regardless)

| Item | Why it's spec'd but not built | Where the spec lives |
|---|---|---|
| Automated content moderation | Needs a policy decision (tattoo art legitimately includes nudity) that this session cannot make | §2.6, §3 |
| Plan/quota gating on identity images | Deliberately deferred; wire up `IQuotaCheckedCommand` later if the business decides otherwise | §2.9 |
| Branded email/receipt logo | Additive later — logo is stored, just not consumed by notification templates yet | §3 |
| "Report image" UI | Existing conduct-report/feedback flow covers this for now | §2.6 |
| Dropping `Artist.AvatarUrl` | Breaking migration for zero benefit tonight; do only once all readers are confirmed gone | §2.11 |
| `docs/user-manual.html` deletion | Judgment call for whoever owns doc housekeeping, not this feature | §2.15, §3 |

---

## 12. Test requirements summary (see each phase's own §X.5/§X.8/§X.9 for the full list)

Every layer gets tests in the same phase the code ships, not a follow-up: FluentValidation
validators (bounds, closed enums, path-traversal), `ImageCommitService` against real
fixture images (a JPEG with GPS EXIF, a PNG with alpha, a WebP, a truncated file, a
decompression-bomb PNG, a polyglot/renamed `.exe`), authorization matrices per role per
route, the resolver's precedence rules, erasure/export coverage, public-mapper privacy
tests, RTK Query cache-invalidation/account-switch-flash e2e, keyboard-only upload-dialog
e2e, and a real-browser manual pass on staging before prod (auth/tenant/session runtime
changes get this per existing project convention). Run `dotnet test`, `pnpm test`,
`pnpm lint`, `pnpm build`, and the Playwright suite — all four must be green before this is
considered done, and vitest passing does not imply e2e passing (explicitly called out in the
draft spec as a real, previously-observed gap on this project).

---

## 13. Final verification checklist — do not declare done until every row passes

- [ ] `dotnet build` clean, `dotnet test` green, `pnpm build` clean, `pnpm test` green,
      `pnpm lint` clean, Playwright e2e suite green.
- [ ] No file outside this prompt's touched list was modified (spot-check `git diff --stat`
      against §4's do-not-touch list).
- [ ] No PII in any new log line (grep the new code for `LogInformation`/`LogWarning`/
      `LogError` calls and manually confirm each argument list).
- [ ] Every new endpoint has `.RequireAuthorization("<PolicyName>")` — no bare
      `.RequireAuthorization()` and no missing call.
- [ ] `Artist.AvatarUrl` has zero remaining non-migration readers or writers (§2.11) —
      confirm with a repo search before committing Phase B.
- [ ] The studio-less-client avatar scenario (§2.12) has a passing integration test, not
      just a code-review assertion.
- [ ] The impersonation-denial regression tests (§2.13, §7.6, §8.6) pass with **zero**
      changes to `ImpersonationAllowList.cs` — if you touched that file, revert and
      re-read §2.13.
- [ ] Help Menu, standalone manual (`frontend/public/user-manual/index.html`, confirmed
      live per §2.15 — not `docs/user-manual.html`), and every affected tour step are
      updated, with an explicit stated reason anywhere the answer was "no change needed."
- [ ] `docs/claude/architecture.md` has one Decisions Log entry covering the whole feature
      (built up across the three phases, not three separate entries), the three admin
      `IgnoreQueryFilters()` usages listed, and a Feature Module Map row.
- [ ] `docs/claude/database.md` documents `UserProfile`, `Studio.LogoUrl`/`CoverFocalX/Y`,
      and the `ProfileImageUrl`/`AvatarUrl` resolution.
- [ ] Your final report explicitly flags, as unverified by this session: the Cloudflare
      Worker `Cache-Control` forwarding (§2.10/§5.1) and the ImageSharp-vs-SkiaSharp license
      determination actually made (§2.3/§5.2), plus the stale `docs/user-manual.html`
      (§2.15) as a housekeeping suggestion, not a task completed.
- [ ] Three commits exist on `feature/studio-profile-images`, one per phase, each
      independently reviewable.

---

## 14. Final deliverable spec

Three commits on branch `feature/studio-profile-images` (one per phase, per §2.8), each
containing that phase's migration, backend, frontend, tests, and Help-sync files together
(never split Help sync into a follow-up commit). Suggested commit messages:

```
feat: shared image pipeline + studio logo/cover + admin removal (Phase A)

feat: account avatar for all roles + resolver + messaging migration off AvatarUrl (Phase B)

feat: artist photo write path + full avatar/photo display sweep (Phase C)
```

Updated/new files, by phase:

- **Phase A:** `Studio.cs`, `ImageKind.cs`, `UserProfile.cs`, new migration,
  `IR2Service.cs`+`R2Service.cs` (two new methods), `IImageProcessor.cs`+`ImageProcessor.cs`,
  `ImageCommitService.cs`, `Studios/Commands/Images/*`, `StudioImageEndpoints.cs`,
  `RateLimitingExtensions.cs`, `PendingImageCleanupJob.cs`, admin studio-image removal
  command+endpoint, `architecture.md` (IgnoreQueryFilters row + new Decisions Log entry +
  Feature Module Map row), frontend shared components (§6.10), Studio Settings card, display
  sweep (§6.11), `helpContent.ts`, `frontend/public/user-manual/index.html`, `ownerTour.ts`.
- **Phase B:** avatar commands/query/endpoints, `IAvatarResolver.cs`+impl, four messaging
  call-site edits, `/account/profile` page, `UserChip.tsx`/`UserMenu.tsx` edits,
  `RetentionPurgeJob.cs`/account-deletion paths/`ExportMyDataQuery.cs` edits, RTK Query tag +
  logout/switch invalidation, `helpContent.ts`, manual, `clientTour.ts` (+ artist/owner if
  applicable), `architecture.md` + `database.md` updates.
- **Phase C:** artist-photo commands/endpoints, admin artist/user-avatar removal,
  full display sweep (§8.3), `helpContent.ts`, manual, `artistTour.ts`/`ownerTour.ts`,
  final `architecture.md` entry.

This document itself (`docs/claude/overnight-prompt-studio-profile-images-2026-09-19.md`)
supersedes `docs/claude/feature-spec-studio-and-profile-images-2026-09-19.md` as the
executable record — leave the draft spec file in place (it's still useful historical
context for *why* each decision was made) but do not treat it as authoritative where this
document corrects it (§2.11, §2.12, §2.13, §2.14, §2.15, §6.2).
