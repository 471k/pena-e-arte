# Feature Spec — Studio & Profile Images (Logo, Cover, Artist Photo, Account Avatar)

> Date: 2026-09-19
> Status: Draft for review — **not** an implementation prompt yet. Section 15 ("Decisions
> needed") must be resolved first; every decision carries a recommendation so it can be
> approved as-is.
> Touches: `Pena_e_Arte.Domain`, `.Contracts`, `.Application` (Studios, Artists, Users, Files),
> `.Infrastructure` (R2 service, jobs, one EF migration), `.API`, `frontend/src/features/{studios,
> artists,clients,messaging,public,auth,admin}`, `frontend/src/shared`, Help Menu
> (`helpContent.ts`), standalone user manual (`index.html`), onboarding tours
> (`ownerTour.ts`, `artistTour.ts`, `clientTour.ts`), `architecture.md` Decisions Log.
> Origin: 2026-09-19 — the owner of "Ink Me harder Studio" asked how to set a studio profile
> picture for the Discover page; there is no way to. Same session: "all users should be able to
> add their own image if they want — optional."

---

## 1. What this is

Today no image on the platform can be set by the person it represents. Every studio shows
initials on Discover, every artist shows initials, every account shows an initial in the header.
The database and every display surface are already prepared for studio and artist images — only
the *write path* is missing.

This spec adds, all **strictly optional** and always falling back to today's initials look:

| Image | Belongs to | Who can set it | Public? |
|---|---|---|---|
| **Studio logo** (square) | Studio | Owner | Yes — Discover, studio page, map, embed |
| **Studio cover** (wide banner) | Studio | Owner | Yes — Discover card, studio page hero, embed, social share |
| **Artist photo** | Artist (per studio) | That artist, or the studio Owner | Yes — studio page, booking, artist portfolio page |
| **Account avatar** | Any signed-in user (client, artist, owner, admin) | The user themself | **No** — signed-in surfaces only (see §11) |

Nothing is ever required. No flow (registration, booking, invite acceptance) may be blocked,
nagged, or reordered because an image is missing.

---

## 2. Current state — verified against live source, 2026-09-19

Read directly from the repo, not inferred from docs:

**The gap**
- `Studio.CoverImageUrl` (`Domain/Entities/Studio.cs:11`, `HasMaxLength(500)`) exists and is
  *read* by `GetNearbyStudiosQuery`, `GetPublicStudioQuery`, `GetMyStudiosQuery`. Nothing writes
  it. A repo-wide search of `*.cs`/`*.ts`/`*.tsx` finds it only in read queries, response DTOs,
  the EF config, tests, and old migrations.
- `Artist.AvatarUrl` (`Domain/Entities/Artist.cs:26`) is the same: read by messaging and public
  DTOs, never written. `UpdateArtistRequest` has no photo field.
- ASP.NET Identity uses the **plain `IdentityUser`** (`AppDbContext : IdentityDbContext<IdentityUser>`,
  `AddIdentityCore<IdentityUser>`). There is nowhere to store an account-level avatar. Owner and
  admin accounts have **no** profile row at all (only clients have `Client`, artists have
  `Artist`).
- The header `UserChip.tsx` derives everything from the JWT (`user.name` = `given_name` claim);
  it renders `initial` in a coloured circle. There is no account/profile settings page — `UserMenu`
  only links "Change email" and "Change password".

**Display surfaces that already handle a URL and need only the write path**
- Discover card — `DiscoverPage.tsx:84` — image area is a fixed `h-40` (160 px), `object-cover`,
  card width ≈ 300–440 px → **≈ 1.9 : 1 to 2.8 : 1**. Falls back to `StudioMonogram`.
- Studio page hero — `StudioPortfolioPage.tsx:340` — fixed `h-72` (288 px) full page width →
  **≈ 4 : 1 or wider** on desktop. Also feeds `og:image` and JSON-LD `image`.
- Embed widget — `EmbedPage.tsx:85` — `h-32` (128 px) banner.
- My Studios — `MyStudiosPage.tsx` `StudioAvatar` renders `coverImageUrl` as a small avatar
  (today the *cover* doubles as the logo — the reason a separate logo is needed).
- Messaging — `ConversationResponse.OtherAvatarUrl`, `ConversationContactResponse.AvatarUrl`;
  resolved only from `Artist.AvatarUrl` (`ConversationEligibility.cs`, `GetConversationsQuery.cs`,
  `CreateConversationCommand.cs`). Clients/owners always resolve to `null`.
- `ArtistCard`, `ArtistDetailPage`, `ClientCard`, `ClientDetailPage`, messaging
  (`ConversationThread`, `MessagesInboxPage`, `NewConversationDialog`) use Radix `Avatar` with
  `AvatarFallback` initials — no `AvatarImage` anywhere.

**Existing upload pipeline (what we can reuse, and what we can't)**
- `POST /api/v1/files/presign` (`FileEndpoints.cs`, `ClientAndAbove`) → `GetPresignedUploadUrlQuery`
  → `IR2Service.GeneratePresignedUploadUrlAsync`. Validator allows `image/jpeg|png|webp|application/pdf`.
  The server generates the file name; only a client-supplied *folder* prefix is kept.
- Object key is always `{tenant.StudioId}/{prefix}/{guid}.{ext}`. **A studio-less client has no
  tenant**, so this route cannot produce a valid key for them → account avatars need their own
  key scheme (§6.1). *(I have not run this path with a null tenant; treat "produces an invalid or
  empty prefix" as the expected behaviour to confirm in a test.)*
- The presigned PUT signs only `Content-Type`. **Nothing enforces file size, real file type, or
  image dimensions** — the backend never observes the upload finishing
  (`StorageReconciliationJob` doc comment says so explicitly).
- The frontend `FileUploadField` / `usePresignedUpload` uploads the raw file straight to R2; the
  resulting `publicUrl` is then handed to whatever save endpoint the feature uses.
- `IR2Service` already has `UploadAsync(byte[])`, `DeleteAsync`, `ListByPrefixAsync`,
  `IsR2Url`, `GetPublicUrl`. `UploadAsync` sets `ContentType` only (no `Cache-Control`).
- `GuestPendingUploadCleanupJob` already implements "list a prefix, delete objects older than N
  hours" — the pattern for abandoned pending uploads.
- `UpdateMyStudioCommand` (`PUT /studios/me`) is a **full-replace** of studio details
  (name/city/lat/lng…). Its own comments show that omitted fields get nulled. Images must **not**
  ride this request.

**Erasure / export / quota systems the feature must join**
- `RetentionPurgeJob` deletes consent-form R2 objects and anonymizes erased clients — it currently
  knows nothing about avatars.
- `ExportMyDataQuery` (client self-service export) presigns R2 URLs for consent PDFs.
- `StorageReconciliationJob` sums every object under `{studioId}/` into
  `Studio.StorageUsageBytes`; `PlanLimitBehavior` gates commands implementing `IQuotaCheckedCommand`.
- `AuditLogBehavior` + `IAuditableCommand` is the standard way to audit a command; audit metadata
  is whitelisted and PII-scrubbed (`AuditMetadataBuilder`).
- `ImpersonationAllowList` is documented as "the entire security surface" for support
  impersonation route scope.

**Not verifiable from the repo**
- The Cloudflare Worker that proxies public R2 reads: I did not find its config in the repo
  (my search of infra/deploy/CI files turned up nothing). Whether it forwards/overrides `Cache-Control` must be checked in the Cloudflare
  dashboard before §6.5 is relied on.
- I did not re-verify any competitor's current UI in this session. §12's benchmark statements
  rest on CLAUDE.md rule 6's comparison set and general category knowledge — re-check the named
  products' current onboarding before sign-off.

---

## 3. Goals and non-goals

**Goals**
1. Every role can attach an optional image where one is meaningful (table in §1).
2. One consistent, secure pipeline for all four image types — not four bespoke ones.
3. Every image surface falls back to today's initials rendering with zero layout shift.
4. Images are safe by construction: validated server-side, EXIF/GPS stripped, resized, no
   arbitrary external URLs stored, old files deleted, personal data erasable.
5. Fully accessible and mobile-first; Help, manual and tours updated in the same change.

**Non-goals (explicitly out of scope — do not build)**
- Automated content moderation / nudity scanning (manual takedown only, §11.4).
- Video, GIF/animated images, SVG uploads (SVG is an XSS vector — never accept it).
- Per-studio *client-facing* avatars for guests who have no account.
- Custom-domain / white-label email branding using the logo (future; logo is stored so this is
  additive later).
- Replacing portfolio image upload (`PortfolioImage`) — untouched.
- Plan gating. Identity images are not a paid feature (D9).

---

## 4. Image specifications

Accepted **source** formats for every type: `image/jpeg`, `image/png`, `image/webp`.
HEIC/HEIF is not accepted server-side; iOS Safari converts to JPEG for `<input accept>` lists
that name jpeg/png, so mobile users are not blocked. SVG, GIF, AVIF, TIFF, BMP: rejected.

| | Account avatar | Artist photo | Studio logo | Studio cover |
|---|---|---|---|---|
| Aspect | 1 : 1 (crop) | 1 : 1 (crop) | 1 : 1 (crop) | free landscape, **focal point** (§4.1) |
| Max source file | 5 MB | 5 MB | 5 MB | 10 MB |
| Min source size | 256 × 256 | 256 × 256 | 256 × 256 | 1200 × 400 |
| Max decoded pixels | 25 MP | 25 MP | 25 MP | 25 MP |
| Output variants (WebP, q≈80) | 128, 512 | 128, 512 | 128, 512 | 640, 1280, 1920 wide |
| Extra output | — | — | — | `og.jpg` 1200 × 630 (JPEG — see below) |
| Target output size | ≤ 60 KB (512) | ≤ 60 KB | ≤ 60 KB | ≤ 250 KB (1920) |

- **Why `og.jpg` is JPEG:** social scrapers (WhatsApp, iMessage, some X/Facebook paths) have
  historically been unreliable with WebP `og:image`; JPEG is universally safe.
- **Decompression-bomb guard:** the pixel limit is checked from the image *header* before any
  full decode; over-limit → reject without decoding.
- **Transparency:** PNG/WebP transparency is preserved for logo and avatar (WebP output keeps
  alpha); the cover is flattened onto the surface colour.

### 4.1 Cover framing — why a focal point, not a fixed crop

The same cover is displayed at ~2 : 1 (Discover card), ~4 : 1+ (studio hero, 288 px tall across a
1280 px viewport), ~3.5 : 1 (embed), and 1.91 : 1 (social share). No single crop rectangle suits
all of them. The industry-standard answer (Shopify, Contentful, most booking SaaS) is **one
master image + a focal point**:

- Owner uploads any landscape image; the app stores the resized master and
  `CoverFocalX`, `CoverFocalY` ∈ [0, 1] (default 0.5, 0.5).
- Every surface renders with CSS `object-position: {x*100}% {y*100}%`.
- The server derives `og.jpg` by cropping 1.91 : 1 around the focal point.
- The upload dialog shows **live previews of all surfaces** (Discover card, studio hero, share
  card) so the owner drags one focal dot and sees the result everywhere.

---

## 5. Data model

One additive EF migration. **All columns nullable / defaulted → zero-downtime deploy, no backfill.**

**`Studio`** (existing table)
- `LogoUrl` `string?` `HasMaxLength(500)` — new.
- `CoverImageUrl` — **kept as-is** (name and meaning unchanged, so every existing consumer and
  test keeps working).
- `CoverFocalX`, `CoverFocalY` `double` default `0.5` — new.

**`Artist`** (existing) — `AvatarUrl` reused as the artist's *studio-specific professional
photo*. No schema change.

**`UserProfile`** (new table, **not** a `TenantEntity`)
- `UserId` `Guid` PK — same type as `UserOnboardingState.UserId`; matches the Identity user id.
- `AvatarUrl` `string?` (500), `AvatarUpdatedAt` `DateTime?`, `CreatedAt`.
- No EF tenant query filter — same "non-tenant-scoped, authorization enforced in handlers" shape
  as `UserOnboardingState`/`FeedbackReport`/`AuditLogEntry`. **Consequence: every read/write
  handler for this table must scope by `currentUser.UserId` (or be an explicit `AdminOnly` path);
  there is no filter to save us.** Document it in the "IgnoreQueryFilters approved usages"
  neighbourhood of `architecture.md`.
- Rows are created lazily on first avatar set — no backfill, no row for users who never upload.

**Why a side table and not `ApplicationUser : IdentityUser`:** the Identity type is threaded
through `UserManager<IdentityUser>`, `IdentityDbContext<IdentityUser>`, `AddIdentityCore`, the
admin bootstrapper and `IdentityService`. Changing it is a wide, risky refactor for one column.
A side table also keeps this feature clear of Identity **claims** — the dual-role owner/artist
identity-claim regression on 2026-09-17 (PR #148 → hotfix #150) is exactly why avatar data must
**not** be modelled as claims or keyed on `Artist.UserId` (D2).

**URL convention (backward compatible).** Each stored URL points at the *largest* variant:
`{cdn}/{prefix}/{imageId}/{size}.webp`. Sibling variants share the folder, so a tiny frontend
helper `imageVariant(url, width)` swaps the size token to build `srcSet`. If a stored URL does
not match the convention (legacy/seeded), the helper returns it unchanged. `imageId` is a new
GUID per upload → URLs are **immutable and cache-busting** (§6.5).

---

## 6. Storage and upload pipeline

### 6.1 Key layout

| Purpose | Key |
|---|---|
| Pending upload (any type) | `pending/{scope}/{guid}.{ext}` where `{scope}` = `user-{userId}` |
| Account avatar | `avatars/{userId}/{imageId}/{128\|512}.webp` |
| Artist photo | `{studioId}/artists/{artistId}/{imageId}/{128\|512}.webp` |
| Studio logo | `{studioId}/branding/logo/{imageId}/{128\|512}.webp` |
| Studio cover | `{studioId}/branding/cover/{imageId}/{640\|1280\|1920}.webp` + `og.jpg` |

- Studio-owned images live under `{studioId}/…` → automatically counted by
  `StorageReconciliationJob` (small, correct).
- Account avatars live under `avatars/` → **not** counted against any studio's quota (they are
  personal, and a studio-less client has no studio). The new avatar endpoints therefore **must
  not** implement `IQuotaCheckedCommand`.
- `pending/` is outside every studio prefix and is never publicly referenced.

### 6.2 Two-phase flow (presign → commit)

Existing pattern is "presign → client PUTs → client saves the URL". That trusts the client for
file type, size, and the URL itself. For images we add a server-side **commit** step:

1. `POST …/presign` — server validates the request, mints `pending/{scope}/{guid}.{ext}`
   presigned PUT (15 min) and returns `{ uploadUrl, uploadKey }`.
2. Client PUTs the (already client-side-resized) file directly to R2.
3. `PUT …` (commit) with `{ uploadKey, focal? }` — server:
   a. Verifies `uploadKey` starts with the caller's own `pending/{scope}/` (cannot claim
      another user's upload; cannot supply an arbitrary URL — **no client-supplied URL is ever
      stored**).
   b. `HEAD` the object: reject if size > limit or stored content-type ≠ requested type.
   c. Streams the object with a hard byte cap; sniffs magic bytes (JPEG `FFD8FF`, PNG
      `89504E47`, WebP `RIFF….WEBP`) — extension/Content-Type alone are not trusted.
   d. Reads dimensions from the header; enforces min size and the decoded-pixel cap.
   e. Decodes, **auto-orients (EXIF orientation), strips ALL metadata (EXIF/GPS/XMP/ICC
      author fields)**, resizes to each variant, encodes.
   f. `PutObject` each variant with `Cache-Control: public, max-age=31536000, immutable`.
   g. In one DB transaction: writes the new URL (the previous URL is kept in memory for h).
   h. After commit: best-effort `DeleteAsync` of the previous image's variants and of the
      pending object; failures are logged and swept by the cleanup job (§6.4).
4. Response returns the new URLs; the frontend invalidates the relevant RTK Query tags.

A repeated commit with the same `uploadKey` returns 404/409 (the pending object is gone) — safe
double-click behaviour.

### 6.3 Image-processing library (see D3)

Server-side decode/re-encode is what makes EXIF-stripping and format-truth enforceable — the
client is untrusted. Recommendation: **SixLabors.ImageSharp** (fully managed → memory-safe when
decoding hostile input). It is dual-licensed (Six Labors Split License: free for qualifying
open-source/small-revenue use, commercial licence otherwise) — **confirm the terms apply to this
business before adding it**. Alternatives: `SkiaSharp` (MIT, native), `Magick.NET` (Apache 2,
native). Not an ORM/data-access library, so the CLAUDE.md "never add" rule does not apply, but a
new dependency should still be called out in the PR description.

### 6.4 Cleanup

- **Replace/remove:** delete the previous variants (best-effort, after commit).
- **Abandoned pending uploads:** new `PendingImageCleanupJob` (Hangfire, hourly), copying
  `GuestPendingUploadCleanupJob`'s list-prefix/delete-older-than pattern for `pending/`, TTL
  2 hours. (Optionally also an R2 lifecycle rule on `pending/` — belt and braces; infra note.)
- **Orphan sweep:** the same job compares `avatars/` + `*/branding/` + `*/artists/` object
  folders against DB references weekly and deletes unreferenced folders older than 24 h (covers
  failed post-commit deletes).
- Follow `architecture.md` "Hangfire Job Conventions" for registration/idempotency.

### 6.5 Caching and CDN

Because every upload gets a fresh `imageId`, objects are immutable → long `immutable`
`Cache-Control` is safe and the browser/Cloudflare cache-hit rate is high. **Verify the R2 public
read Worker does not strip/override `Cache-Control`** (not in repo, §2). `IR2Service.UploadAsync`
needs an added `cacheControl` parameter (default `null` → current behaviour) — additive, no
existing caller changes. The service worker only intercepts same-origin requests (PR #141), so
cross-origin CDN images are unaffected by it.

---

## 7. API

All routes under `/api/v1`. **No new `AllowAnonymous` endpoint** — public display uses the
existing public DTOs, so the `architecture.md` "AllowAnonymous Exceptions" table is unchanged.
Every command has a FluentValidation validator (CLAUDE.md rule). New rate-limit policy
`image-upload`: **10 requests / 10 min / user** (Redis-backed like the others in
`RateLimitingExtensions.cs`); applied to every presign and commit route below.

### 7.1 Account avatar — any signed-in role

| Method & route | Policy | Notes |
|---|---|---|
| `GET /users/me/profile` | `ClientAndAbove` | `{ avatarUrl, avatarThumbUrl }`; used by header + account page |
| `POST /users/me/avatar/presign` | `ClientAndAbove` | body `{ contentType, sizeBytes }` |
| `PUT /users/me/avatar` | `ClientAndAbove` | body `{ uploadKey }` (commit) |
| `DELETE /users/me/avatar` | `ClientAndAbove` | idempotent; deletes files |

Scoped strictly to `currentUser.UserId`; the route contains no user id, so there is no IDOR
surface. **Must work with no tenant** (studio-less client) — the handlers do not touch
`ICurrentTenant`.

### 7.2 Studio logo and cover — Owner

| Method & route | Policy |
|---|---|
| `POST /studios/me/images/{kind}/presign` (`kind` = `logo` \| `cover`) | `OwnerOnly` |
| `PUT /studios/me/images/{kind}` — body `{ uploadKey, focalX?, focalY? }` | `OwnerOnly` |
| `PATCH /studios/me/images/cover/focal-point` — body `{ x, y }` | `OwnerOnly` |
| `DELETE /studios/me/images/{kind}` | `OwnerOnly` |

Studio comes from the tenant (`tenant.StudioId`), never from the body. `kind` is validated
against a closed set. These are separate commands — **not** fields on `UpdateStudioRequest`
(§2: that PUT is full-replace).

### 7.3 Artist photo

| Method & route | Policy | Extra authorization |
|---|---|---|
| `POST /artists/{id}/photo/presign` | `ArtistAndAbove` | see below |
| `PUT /artists/{id}/photo` | `ArtistAndAbove` | see below |
| `DELETE /artists/{id}/photo` | `ArtistAndAbove` | see below |

Handler-level rule (the 2026-07-02 Artist QA pass found 11 artist-scope leaks — do not repeat):
an `artist` may act only when `artist.UserId == currentUser.UserId`; an `owner` may act on any
artist **in their own tenant** (tenant query filter + explicit check); anyone else → 404 (not 403,
to avoid leaking existence).

### 7.4 Platform-admin moderation

| Method & route | Policy | Notes |
|---|---|---|
| `DELETE /admin/studios/{id}/images/{kind}` | `AdminOnly` | body `{ reason }` (closed enum) |
| `DELETE /admin/users/{userId}/avatar` | `AdminOnly` | body `{ reason }` |
| `DELETE /admin/artists/{id}/photo` | `AdminOnly` | body `{ reason }` |

Uses `IgnoreQueryFilters()` — must be added to the `architecture.md` "IgnoreQueryFilters()
Approved Usages" list in the same change (explicit `admin` role check, per CLAUDE.md rule 1).
Every admin removal writes an `AuditLogEntry` (`Studio.ImageRemovedByAdmin` etc.) with the
reason enum and no free text/PII; the studio owner is notified by the existing notification
service ("An image on your studio was removed by TattooOS support — reason…").

### 7.5 Response shape changes (additive)

- `StudioResponse`, `MyStudioResponse`, `PublicStudioResponse`, `NearbyStudioResponse`,
  `PublicStudioSummary`-style DTOs: add `LogoUrl`, `CoverFocalX`, `CoverFocalY`.
- `ConversationResponse.OtherAvatarUrl` / `ConversationContactResponse.AvatarUrl`: unchanged
  names; now resolved for **all** roles (§8.2).
- Client list/detail responses (owner/artist-facing): add `AvatarUrl` resolved via §8.2.

---

## 8. Backend implementation notes

### 8.1 New/changed types (all `var`-free per CLAUDE.md)

- Domain: `UserProfile` entity; `Studio.LogoUrl`, `CoverFocalX/Y`; `ImageKind` enum
  (`Logo`, `Cover`, `ArtistPhoto`, `AccountAvatar`).
- Application:
  - `Images/` shared: `IImageProcessor` (interface in Domain/Application, impl in
    Infrastructure), `ImageProcessingOptions` per `ImageKind` (the §4 table as data),
    `ImageCommitService` (steps 3a–3h so the four features share one implementation).
  - Commands/validators/handlers: `PresignStudioImageCommand`, `SetStudioImageCommand`,
    `SetCoverFocalPointCommand`, `RemoveStudioImageCommand`, `PresignAccountAvatarCommand`,
    `SetAccountAvatarCommand`, `RemoveAccountAvatarCommand`, `PresignArtistPhotoCommand`,
    `SetArtistPhotoCommand`, `RemoveArtistPhotoCommand`, `AdminRemoveImageCommand`;
    query `GetMyProfileQuery`.
  - Set/Remove commands implement `IAuditableCommand` (`Studio.ImageUpdated`, `User.AvatarUpdated`,
    …) — metadata limited to `kind` and `imageId`.
- Infrastructure: `ImageProcessor` (library per D3); `R2Service.UploadAsync` cache-control
  overload; `PendingImageCleanupJob`; EF config + migration.

### 8.2 One avatar resolver (prevents drift)

Today avatar logic is duplicated in three messaging files. Add `IAvatarResolver`:

- **Artist-facing surfaces** (studio page, booking, artist portfolio): `Artist.AvatarUrl` →
  account avatar of `Artist.UserId` → initials.
- **Account/messaging/staff surfaces**: account avatar → (if the user is an artist in the
  current tenant) `Artist.AvatarUrl` → initials.
- Batch API (`ResolveManyAsync(IEnumerable<Guid> userIds)`) — one query per page, **no N+1**
  in list endpoints (clients list, conversations, contacts).
- Replace the three messaging call sites with the resolver in the same change.

### 8.3 Erasure, export, deletion (compliance — do not skip)

- **`RetentionPurgeJob.AnonymizeErasedClientsAsync`**: when anonymizing a client, also delete the
  user's account avatar objects and null `UserProfile.AvatarUrl`.
- **Owner/artist/admin account deletion** and the recently shipped **client self-deletion**
  path: delete avatar objects and the `UserProfile` row in the same operation. (Fix for the
  cross-studio fan-out bug in PR #153 is the reference for "an account spans several tenants" —
  avatar is account-level, so delete once, not per studio.)
- **Studio deletion/closure**: studio images live under `{studioId}/` — include the prefix in
  whatever R2 cleanup studio deletion performs (verify it does one; if not, that is a
  pre-existing gap to flag, not to silently fix here).
- **Artist removal** (`fix/artist-rejoin-after-removal` behaviour): artist photo is deleted with
  the artist row's soft/hard purge; a *re-invited* artist starts with no photo.
- **`ExportMyDataQuery`**: include the avatar as a presigned, time-limited link (same mechanism
  as consent PDFs) — GDPR data-portability parity.
- **Client archive** (`ArchivedAt`) does **not** touch the avatar (archive is non-destructive).

### 8.4 Logging and audit

Serilog structured logs only, with `tenant_id`, `user_id`, `request_id` (rule 3). Log
`kind`, `imageId`, `bytesIn`, `bytesOut`, `durationMs`, rejection *reason code*. **Never** log
file names, the user's name/email, or full URLs of personal avatars. Add a Prometheus counter
`image_uploads_total{kind,outcome}` and histogram `image_processing_seconds{kind}` following the
existing observability conventions; alert on sustained `outcome="processing_error"`.

### 8.5 Failure semantics

Commit is all-or-nothing from the user's view: any failure before step g leaves the *previous*
image intact and returns a stable error code (`IMAGE_TOO_LARGE`, `IMAGE_TOO_SMALL`,
`IMAGE_UNSUPPORTED_TYPE`, `IMAGE_CORRUPT`, `IMAGE_UPLOAD_NOT_FOUND`, `IMAGE_RATE_LIMITED`) mapped
by `ExceptionMiddleware`; the frontend maps each code to a human message.

---

## 9. Frontend

Follow `docs/claude/frontend.md`. No `any`; RTK Query only; `console.log` banned.

### 9.1 New shared building blocks (`frontend/src/shared`)

- `components/ImageUploadDialog.tsx` — the single upload UX for all four kinds. Steps: pick →
  (crop for square kinds / focal-point + previews for cover) → confirm → uploading (progress) →
  done. Client-side: read file, apply EXIF orientation, resize to the variant-max on a canvas,
  re-encode (this also strips EXIF before it ever leaves the device — defence in depth; the
  server re-does it authoritatively).
- `components/UserAvatar.tsx`, `StudioLogo.tsx`, `StudioCover.tsx` — thin wrappers that render the
  image via `imageVariant()`/`srcSet`, and the initials/monogram fallback via the existing
  Radix `Avatar`/`StudioMonogram` (same dimensions → no layout shift). Built on
  `ImageWithFallback` so a 404/deleted object degrades to initials, not a broken glyph.
- `utils/imageVariant.ts` — `imageVariant(url: string | null, width: number): string | null`.
- `hooks/useImageUpload.ts` — presign → PUT → commit orchestration, abort support, typed errors.
  Reuses (does not modify) `usePresignedUpload`.
- Crop/focal UI: new dependency `react-easy-crop` (MIT) for the square crop; focal-point picker
  is a small in-house component (a draggable dot over the image). Flag the dependency in the PR.

### 9.2 Where each owner-facing control lives

| Role | Control | Location |
|---|---|---|
| Owner | **Studio images** card: logo + cover + focal preview | Top of **Studio Settings** (above "Studio details") |
| Owner | Artist photo (for any artist) | Artist detail page header |
| Artist | Own photo | Artist detail page header (their own record) + **My Profile** |
| Client / Artist / Owner / Admin | **Profile photo** | **new `/account/profile` page** ("Profile" item in `UserMenu`, above Change email) |
| Admin | Remove image (moderation) | Admin studio detail page + admin user detail — "Remove image" with reason select |

Each control: current image (or fallback) · **Upload / Change** · **Remove** (confirm dialog) ·
helper text with size/format guidance · success toast · inline typed error.

### 9.3 Display sweep (replace ad-hoc initials with the shared components)

- **Header** `UserChip` → `UserAvatar` (account avatar; skeleton state unchanged).
- **Discover** studio card → cover (with focal point) + **logo badge overlapping bottom-left**
  when both exist; logo alone replaces the monogram tile when there is no cover; both absent →
  today's `StudioMonogram`. Map popups/markers → logo.
- **Studio page** hero → cover (focal), logo as overlapping header avatar; `og:image` → `og.jpg`
  (fallback: current default), JSON-LD `logo` + `image`.
- **Embed** banner → cover; logo in the header row.
- **My Studios** `StudioAvatar` → `logo ?? monogram` (stop using the cover as an avatar).
- **Artist** cards/detail/public artist page/booking artist picker → artist photo (resolver).
- **Clients** `ClientCard`/`ClientDetailPage` (owner/artist-facing only) → client account avatar.
- **Messaging** thread, inbox, new-conversation dialog → resolver-provided avatar for all roles.
- **Admin** studio list/detail → logo.

### 9.4 Cache invalidation (RTK Query)

Add tags `UserProfile`, `Studio` (already present — verify), `Artist`, `MyStudios`,
`Conversations`, `Clients`. Setting/removing an image invalidates the tags for every surface that
shows it. Memory note from PR #145: RTK Query keeps stale data on error — the profile query must
clear/refetch on logout and on account switch (dual-role Owner⇄Artist switch, studio switch) so a
previous user's avatar can never flash for the next.

### 9.5 Accessibility (WCAG 2.1 AA — see `accessibility-audit-2026-09-05.md`)

- File picker is a real `<button>` + hidden `<input type="file">` with `aria-describedby` for
  the format/size hint; fully keyboard-operable; drag-and-drop is an *enhancement*, never the
  only path.
- Crop/focal UI must be operable without a pointer: arrow keys nudge the crop/focal dot; a
  numeric "Position" fallback is not required, but arrow-key support + visible focus is.
- Alt text: logo `alt="{studio name} logo"`; avatar/photo beside a visible name → `alt=""`
  (decorative); cover → `alt=""` (decorative; the studio name is adjacent). Never put the file
  name or "image" in alt.
- Upload progress announced via `aria-live="polite"`; errors via `role="alert"`.
- Initials fallback keeps ≥ 4.5 : 1 contrast (existing tokens).
- `prefers-reduced-motion`: no crop/zoom animations. Dark/light themes both verified.

### 9.6 Mobile

- Dialog is full-screen on small viewports; touch pinch/drag to crop; `<input accept=
  "image/jpeg,image/png,image/webp">` (no `capture` attribute — let the OS offer camera *or*
  library); large hit targets (≥ 44 px). Verify in a real device-emulated browser pass — the
  recent mobile baseline (PR #62) found bugs only a manual pass caught.

---

## 10. Role matrix

| Action | client | artist | owner | admin |
|---|---|---|---|---|
| Set/remove **own account avatar** | ✅ | ✅ | ✅ | ✅ |
| Set/remove **own artist photo** | — | ✅ | ✅ (if dual-role) | — |
| Set/remove **another artist's photo** (same studio) | — | ❌ | ✅ | — |
| Set/remove **studio logo/cover** | — | ❌ | ✅ | ❌ (moderation-remove only) |
| **Remove** any image for moderation | — | — | — | ✅ (audited, reasoned) |
| See a client's avatar | self | their studio's clients | their studio's clients | ✅ (support) |
| See studio logo/cover/artist photo | public | public | public | public |

**Dual-role owner-artist (from 2026-09-16/17 work):** an owner who is also an artist has one
account avatar and one `Artist.AvatarUrl`. Resolver rules in §8.2 decide which shows where; the
feature adds **no Identity claims** and does not read/write anything keyed on `Artist.UserId`
beyond the artist-photo authorization check above.

**Support impersonation:** an impersonating admin must **not** be able to change the impersonated
user's images. `ImpersonationAllowList` is the route-scope gate — confirm the new routes are
*not* on it (default-deny) and add a test that an impersonation token gets
`IMPERSONATION_SCOPE_DENIED` on every set/remove route. (I have not read the allow-list's
default; verify.)

---

## 11. Privacy, safety, compliance

1. **EXIF/GPS stripped** on the server for every upload (§6.2e). A phone photo can carry the
   uploader's home coordinates — for a tattoo-studio owner working from home this is a real risk.
2. **Client avatars are never public.** They appear only to staff of studios where that person
   is a client, and in that person's own UI. No public DTO, sitemap, OG tag, or embed includes
   them. Add a unit test that every `Public*Response` mapper contains no client/account avatar.
3. **Artist photos are public by design** (the artist is a public-facing professional) — the
   upload dialog says so in plain language ("This photo appears on your public profile").
   Account avatars are **not** public.
4. **Moderation (v1 = manual).** Public images (logo, cover, artist photo) can be removed by an
   admin (§7.4). Users can already file reports via the existing conduct-report / feedback flows;
   add "Report image" to the public studio/artist page **only if** D6 approves it (default:
   admin takedown + existing "Report a problem", no new report UI in v1).
5. **No external URLs.** Only server-processed objects under our bucket can be stored; the DB
   never receives a client-supplied URL (prevents tracking pixels, hotlinked/changeable images,
   and viewer-IP leakage to third parties).
6. **Erasure & export** — §8.3.
7. **Minors.** Tattoo services are 18+; no special handling beyond the existing terms.
8. **Content-Security-Policy.** No `img-src` CSP directive was found in the repo's configs
   (`nginx`/`.cs`/`.html`); if one is added at the edge (Cloudflare) it must allow the R2 CDN
   origin. Note for infra, not a code change.

---

## 12. Industry benchmark (CLAUDE.md rule 6)

Category standard (vertical booking/scheduling SaaS — Vagaro, Fresha, Boulevard, Mindbody,
GlossGenius, Zenoti — and platform-admin norms): a business **logo and cover photo** on the
public profile; **staff/provider photos** on booking and profile pages; a **user profile photo**
in the signed-in shell; upload with crop/preview; remove/replace; graceful initials fallback;
platform-admin ability to remove abusive images with an audit trail. This spec meets each item.

Deliberately *ahead* of the minimum: focal-point cover with live multi-surface preview, EXIF
stripping, server-side re-encoding, immutable-CDN caching, GDPR-complete erasure and export.

Deliberate v1 gaps to **flag, not hide**: no automated content moderation; no multi-photo studio
gallery beyond the portfolio (already exists separately); no logo-on-emails/branded receipts
(additive later). *(Caveat: benchmark statements not re-verified against the vendors' live UIs
this session — see §2.)*

---

## 13. Help sync (CLAUDE.md rule 7 — required in the same change)

- `frontend/src/features/help/helpContent.ts`: new/updated entries — "Add your studio logo and
  cover photo" (owner), "Change your profile photo" (all roles), "Add or change an artist's
  photo" (owner/artist), "Why was my image removed?" (all), "What size/format should my photo
  be?" (all). Update the existing Studio Settings and Discover-related entries (currently
  `helpContent.ts:779`, `:1195`, `:1217` mention Studio Settings steps).
- `frontend/public/user-manual/index.html`: same topics, matching the manual's heading/anchor
  style (≈247 existing headings).
- Tours: `ownerTour.ts` — add an optional step pointing at the Studio images card (must be
  *skippable* and must not gate the tour); `artistTour.ts` — mention profile photo;
  `clientTour.ts` — mention profile photo in the account menu. Update any step whose anchor moves
  because the new card sits above "Studio details".
- The CI "Help stays in sync" guardrail will fail the PR without these — do not use
  `[skip-help-sync]`.

`architecture.md` (same change): add a Decisions Log entry ("Images: two-phase upload,
`UserProfile` side table, focal-point cover, resolver"), list the new admin
`IgnoreQueryFilters()` usage, and add `UserProfile` to the Feature Module Map.

---

## 14. Testing plan

**Unit (Application layer — CLAUDE.md requires tests for business logic)**
- Validators: each command/`kind`/content-type/size bound; path-traversal in `uploadKey`;
  `kind` closed set; focal point ∈ [0, 1].
- Commit service: rejects oversize, wrong magic bytes, wrong header dimensions, over-pixel-cap,
  mismatched Content-Type, another user's `uploadKey`, replayed `uploadKey`.
- Authorization: artist cannot set a colleague's photo; owner cannot touch another tenant's
  artist; client/artist/owner cannot set studio images; admin removal requires reason; every
  route denied under an impersonation token.
- Resolver: precedence rules; batch resolution issues one query.
- Erasure: anonymized client → avatar objects deleted + `UserProfile.AvatarUrl` null.
- Public mappers contain no client/account avatar.

**Integration (real MySQL — the tenant-filter class of bug is only catchable there, per the
2026-09-11 finding):** `UserProfile` non-tenant reads/writes scoped to the caller; studio image
commands honour tenant; admin `IgnoreQueryFilters()` path; migration applies on an existing
dataset with zero data change. Use a fake `IR2Service` + real `ImageProcessor` on fixture images
(JPEG with EXIF-GPS, PNG with alpha, WebP, a truncated file, a decompression-bomb PNG, a
polyglot/renamed `.exe`).

**Frontend (vitest)** for each shared component (fallback, srcset, error → initials),
`useImageUpload` state machine, each settings card, and `imageVariant` (including legacy URLs).

**E2E (Playwright, real browser — mandatory, not optional):** jsdom cannot catch stuck
`pointer-events`/focus-trap bugs from nested Radix overlays (a known limitation of this codebase's test setup). The upload dialog
is a nested overlay inside Studio Settings and the account page. Cover: upload → crop → save →
appears in header/Discover; remove; error paths; keyboard-only path; mobile viewport.
Also check vitest and e2e separately — "vitest green" has not implied "e2e green" on this project before.

**Manual browser pass before merge** (auth/tenant/session runtime change): all four roles,
multi-studio client, dual-role owner-artist switch (no avatar flash across switch), studio-less
client, logout/login as a different user, staging then prod. Boot the API for real to verify DI
wiring for the new services (green unit tests bypass the container).

---

## 15. Decisions needed

| # | Decision | Recommendation |
|---|---|---|
| **D1** | One studio image or two (logo + cover)? | **Two.** Cover is a wide banner; logo is a small square. Reusing one for both crops badly and is why My Studios currently shows a cropped banner as an avatar. |
| **D2** | Account avatar storage: side table vs custom `ApplicationUser`. | **`UserProfile` side table.** Avoids a wide Identity refactor; stays clear of claims (see the 2026-09-17 claim regression). |
| **D3** | Server-side processing library. | **ImageSharp** (managed, memory-safe) **after confirming licence terms**; else SkiaSharp/Magick.NET. Do not ship client-side-only processing — the client is untrusted. |
| **D4** | Artist photo vs account avatar precedence. | Artist photo is a per-studio *override*; resolution order in §8.2. |
| **D5** | Client avatar visibility. | Staff of studios where they're a client only; never public. |
| **D6** | Moderation in v1. | Admin takedown + existing report flows; **no** automated scanning (tattoo art legitimately includes nudity; automated scanning needs its own policy decision). No new "Report image" UI unless you want it. |
| **D7** | Crop dependency. | `react-easy-crop` (MIT) for square crops; in-house focal-point picker. |
| **D8** | Delivery phasing. | **3 PRs:** (1) shared pipeline + studio logo/cover + admin removal + Help; (2) account avatar for all roles + `/account/profile` + resolver + messaging + erasure/export; (3) artist photo + display sweep. Each independently shippable, additive migration. |
| **D9** | Plan gating. | **None.** Basic identity is not a paid feature; no `IQuotaCheckedCommand` on account avatars. |
| **D10** | R2 `Cache-Control` via the Worker. | Verify in Cloudflare before relying on `immutable`; if the Worker overrides it, fix the Worker (infra) rather than weakening the design. |

---

## 16. Rollout

- Migration is **purely additive** (new nullable columns + one new table) → safe to deploy
  ahead of the code; backward-compatible with old pods during the rolling update (old code
  ignores the columns).
- Order: CD ships to prod and staging together (current pipeline). Verify on **staging first**
  in a real browser, then prod — and, per the "check production alongside staging" lesson, check
  prod's state explicitly after.
- Feature is inert until someone uploads — no flag needed. If we want a kill-switch for the
  image-processing dependency, gate the *commit* endpoints behind an `Images:Enabled` config
  (default true) so a bad decode-library CVE can be neutralized without a redeploy of the rest.
- Post-deploy checks: upload one image per kind on staging; confirm variants exist in R2, `pending/`
  object deleted, `Cache-Control` header present at the public URL, `og.jpg` renders in a
  share-preview debugger, no `image_processing_seconds` outliers, no errors in API logs.

---

## 17. Acceptance criteria

1. An owner can add, change, and remove a studio logo and cover from Studio Settings, set the
   cover focal point with live previews, and see them on Discover, the studio page, the embed
   and My Studios within seconds.
2. Any signed-in user (client, artist, owner, admin) can add, change, and remove a profile photo
   from `/account/profile`; it appears in the header and (for staff-visible contexts) lists and
   messaging. A studio-less client can do this.
3. An artist can set their own photo; an owner can set any artist's photo in their studio; no
   one else can; results appear on the public studio and artist pages.
4. With no images set, every surface renders exactly as it does today; no layout shift.
5. Uploads are rejected with clear messages for: wrong type, too large, too small, corrupt,
   over-pixel-cap, and rate-limit; a rejected upload leaves the previous image intact.
6. Stored images contain **no** EXIF/GPS metadata (verified against a GPS-tagged fixture).
7. No client-supplied URL can be stored; no user can commit another user's pending upload.
8. Replacing/removing an image deletes the old objects; abandoned pending uploads are swept.
9. Erasing/deleting an account deletes its avatar objects; the data export includes the avatar.
10. Admin can remove any studio/artist/user image with a reason; the action is audit-logged and
    the studio owner is notified.
11. Impersonation cannot change any image.
12. Public responses/pages never expose a client or account avatar.
13. WCAG 2.1 AA checks pass (keyboard-only, screen-reader labels, contrast, reduced motion).
14. Help Menu, standalone manual, and affected tour steps updated; CI Help-sync gate green.
15. `dotnet test`, `pnpm test`, `pnpm lint`, `pnpm build`, and the Playwright e2e suite all green;
    real-browser manual pass completed on staging, then prod.

---

## 18. Risks

| Risk | Mitigation |
|---|---|
| Hostile image (decompression bomb, malformed header, polyglot) crashes/exhausts the API | Header-first pixel cap, byte cap, managed decoder, run commit under a request timeout + memory-bounded stream; fixtures in §14 |
| Processing latency on the request thread | Sizes are small (≤ 10 MB, ≤ 25 MP); measure; if p95 > 2 s move commit processing to a Hangfire job with a polling status endpoint (design commit as idempotent so this is a swap, not a rewrite) |
| Orphaned R2 objects → cost/privacy leak | Post-commit delete + hourly pending sweep + weekly orphan sweep (§6.4) |
| Avatar flash of previous user after account/studio switch | Clear `UserProfile` cache on auth change; covered by e2e (§9.4) |
| New non-tenant table read without a scope → cross-user leak | No route takes a user id except admin; handler tests assert scoping; integration test with two users |
| Cover looks wrong on some surface | Focal point + live previews; `object-position` everywhere; visual e2e on the three surfaces |
| Licence issue with the imaging library | D3 gate before any code |
| CDN caching not as designed | D10 check; content-addressed URLs make cache correctness independent of TTL |
