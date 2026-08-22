# Feature Request — Social Media Verification (Artist & Owner Profiles)

**From:** Product (client-trust request — "how does a client know this artist is really who they say they are")
**Branch suggestion:** `feat/social-media-verification`
**Read first:** `docs/claude/architecture.md` (Feature Module Map §Instagram, IgnoreQueryFilters table, In-App Help Menu section), `docs/claude/database.md`, `docs/claude/conventions.md` — plus the actual Instagram code below. This was scoped by reading the real `InstagramConnection`/`InstagramService`/`InstagramEndpoints` flow and the real `Studio.InstagramHandle` field, not written from a blank page.

Scope decided with the requester: verification covers **Instagram, TikTok, and other platforms** (Facebook, X, YouTube), via **both** an OAuth "Connect" flow (where the platform supports it) **and** a manual bio-code flow (as a fallback for platforms without OAuth, or for accounts that can't/won't OAuth-connect). Section "Open questions" below flags where that scope has real engineering cost and risk the requester should sign off on before this becomes an overnight master prompt — read it before greenlighting implementation, this is not a rubber-stamp.

---

## Business context (why)

A client booking a tattoo wants to know the artist's Instagram (or TikTok, etc.) they're looking at is *actually* that artist's own account, not a handle someone typed into a form. Right now:

- **Owner/Studio side**: `Studio.InstagramHandle` is a free-text string with zero verification. Any owner can type any handle — their own, a competitor's, a celebrity's — and it displays on the public studio page exactly as typed. This is the sharper gap: no authentication of any kind.
- **Artist side**: Ironically the opposite problem. A real, hardened Instagram OAuth connection already exists per-artist (see below) — but it exists purely to pull portfolio photos. The fact that the connection is genuine, OAuth-verified proof of account ownership is never surfaced to a client anywhere. There's no badge, no confirmed `@username` shown next to the artist's name, nothing. The trust signal already exists technically and is completely wasted.

This request closes both gaps with one consistent mechanism: a "Verified" badge next to an artist's or studio's social handle, backed by either an OAuth handshake or a manual bio-code check, following the same visual language the codebase already uses for `ReviewSection.tsx`'s "Verified client" badge (a real precedent for "verified" as a UI concept in this app — not a new pattern being invented from scratch).

---

## What already exists (read this before writing code)

### Artist Instagram OAuth (fully built, hardened — do not modify)

- `InstagramConnection` (`Pena_e_Arte.Domain/Entities/InstagramConnection.cs`): `TenantEntity`, **no global query filter** (documented on the entity itself — the nightly sync job iterates all tenants; every handler must filter by `ArtistId` and verify tenant manually). Fields: `ArtistId`, `InstagramUserId`, `Username`, `EncryptedToken` (AES-256-GCM via `AesTokenEncryptor`), `TokenExpiresAt`, `LastSyncedAt`, `IsActive`.
- `IInstagramService` (`Pena_e_Arte.Domain/Interfaces/IInstagramService.cs`): `BuildAuthorizationUrl`, `ExchangeCodeAsync`, `RefreshTokenAsync`, `GetUsernameAsync`, `GetMediaAsync`. Implementation targets the **Instagram API with Instagram Login** (current official API since the Basic Display API's Dec 2024 shutdown) — this requires the connecting Instagram account to be a **Business or Creator account**, not a personal one. That constraint carries forward into everything below.
- `IInstagramStateSigner`: HMAC-signs the OAuth `state` param carrying `artistId` through the redirect, so the anonymous callback can trust it. **Artist-scoped only** — signs a bare `Guid artistId`, nothing else.
- Endpoints (`InstagramEndpoints.cs`, all under `/api/v1/artists/{id:guid}/instagram`): `GET /connect-url` (`OwnerOnly`), `GET /status` (`ArtistAndAbove`), `GET /posts` (`ArtistAndAbove`), `PUT /posts/{postId}/visibility` (`ArtistAndAbove`), `DELETE /disconnect` (`OwnerOnly`). Anonymous callback at `GET /api/v1/instagram/callback`, rate-limited (`"public-write"`).
- `InstagramSyncJob` (Hangfire, nightly): refreshes tokens, upserts `InstagramPost` rows, and — per two already-shipped bug fixes in `architecture.md` — correctly skips connections belonging to a suspended studio and checks `Studio.IsActive` before serving public posts. **This code has already been through three rounds of hardening** (suspension check, rate limiting on the callback, sync-job studio-status check — all documented in `architecture.md`'s Decisions Log). Treat it as load-bearing and do not touch it; anything new should be additive.
- Frontend: `InstagramTab.tsx` (owner-facing, on the artist detail page) — connect/disconnect button (`canConnect` = `OwnerOnly`), post grid with per-post visibility toggle (`canManagePosts` = `ArtistAndAbove`). This is the UI pattern to extend, not replace.
- **Public exposure gap**: `PublicArtistResponse` (`Pena_e_Arte.Contracts/Responses/Public/PublicArtistResponse.cs`) has **no Instagram field at all** — not connected status, not username, nothing. The only public trace of the connection is the separate `GET /api/v1/public/artists/{slug}/instagram-posts` endpoint (photos only, via `GetPublicArtistInstagramPostsQuery`). A client sees photos that *look* like they're from Instagram but has zero explicit confirmation the account belongs to this artist.

### Studio/Owner Instagram (unverified free text)

- `Studio.InstagramHandle` (`Pena_e_Arte.Domain/Entities/Studio.cs`): `string?`, no OAuth, no format validation beyond a 60-char max on the frontend form. Set via `UpdateMyStudioCommand`/`UpdateStudioBrandingCommand`, displayed via `PublicStudioResponse.InstagramHandle`.
- Frontend: `StudioProfilePage.tsx` — a single labeled text input, "Instagram handle (optional)", `zod` max-length only.
- No equivalent connect/OAuth flow exists for studios at all today.

### The one existing "Verified" UI precedent

- `ReviewSection.tsx` renders a **"Verified client"** badge when `review.isVerifiedBooking` is true (backed by a real cross-tenant appointment check — see `architecture.md` IgnoreQueryFilters entries 19–21). This is the closest existing pattern for "a checkmark that means something backed by real data" — the new social badge should look and read consistently with it, not invent a new visual language.

---

## Recommended architecture

Don't extend `InstagramConnection` to also cover studios or other platforms — its shape (photo sync, `ArtistId`-only, no global filter with manual tenant checks baked into every caller) is specific to the artist-portfolio-sync use case and is already hardened; widening it multiplies risk to code that's been bug-fixed three times already. Instead, add a **new, additive, platform-agnostic entity** that is the single source of truth for the "is this social handle verified" question, for both subjects and every platform:

```csharp
// Pena_e_Arte.Domain/Enums/SocialPlatform.cs
public enum SocialPlatform { Instagram, TikTok, Facebook, X, YouTube }

// Pena_e_Arte.Domain/Enums/SocialLinkSubjectType.cs
public enum SocialLinkSubjectType { Artist, Studio }

// Pena_e_Arte.Domain/Enums/SocialVerificationMethod.cs
public enum SocialVerificationMethod { OAuthConnect, ManualBioCode }
```

```csharp
// Pena_e_Arte.Domain/Entities/SocialAccountLink.cs
/// <summary>
/// One artist's or studio's link to one social platform, with optional verification.
/// Same "no global query filter" shape as InstagramConnection (see AppDbContext) — a
/// Studio-subject row's StudioId equals its own SubjectId (self-referential tenant key,
/// consistent with Studio being issuer-level/unfiltered); an Artist-subject row's StudioId
/// is the artist's own tenant. Every handler must filter explicitly and verify the caller's
/// tenant/role owns SubjectId — do not rely on a query filter that isn't there.
/// </summary>
public class SocialAccountLink : TenantEntity
{
    public SocialLinkSubjectType SubjectType { get; set; }
    public Guid SubjectId { get; set; }               // ArtistId or StudioId
    public SocialPlatform Platform { get; set; }
    public string Handle { get; set; } = "";           // display handle, e.g. "inkbyana" (no leading @)
    public bool IsVerified { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public SocialVerificationMethod? VerificationMethod { get; set; }

    // OAuth path only (null otherwise) — token kept only as long as needed, see §4 below
    public string? ExternalUserId { get; set; }
    public string? EncryptedToken { get; set; }
    public DateTime? TokenExpiresAt { get; set; }

    // Manual bio-code path only (null once verified or expired)
    public string? PendingVerificationCode { get; set; }
    public DateTime? PendingCodeExpiresAt { get; set; }
}
```

Unique index on `(SubjectType, SubjectId, Platform)` — one link per platform per subject.

For **Instagram specifically**, this table does not replace `InstagramConnection` — it sits alongside it. When `ExchangeInstagramCodeHandler` (the existing artist OAuth callback handler) succeeds, it additionally upserts a `SocialAccountLink` row (`Platform = Instagram`, `SubjectType = Artist`, `VerificationMethod = OAuthConnect`, `IsVerified = true`, `Handle`/`ExternalUserId` from the same response already fetched). `InstagramConnection` keeps owning the photo-sync lifecycle exactly as today; `SocialAccountLink` becomes the one place every badge, everywhere, reads from — an artist's badge and a studio's badge are rendered by the exact same frontend component reading the exact same shape of data, regardless of platform or verification method.

---

## What needs to change

### 1. Domain + migration

- Add `SocialAccountLink`, the three enums above.
- `AppDbContext`: add `DbSet<SocialAccountLink>`, **no `HasQueryFilter`** (matches `InstagramConnection`'s documented exception — this is not a new kind of gap, it's the same one, so it doesn't need a new numbered `IgnoreQueryFilters` table entry the way an `IgnoreQueryFilters()` *call* would; it needs the same "no filter, manual check" comment convention `InstagramConnection` already carries).
- `IEntityTypeConfiguration<SocialAccountLink>` per `database.md`'s pattern — table `social_account_links`, unique index `ix_social_account_links_subject_platform` on `(subject_type, subject_id, platform)`.
- Migration name: `AddSocialAccountLinks` (matches `AddInstagramIntegration`'s precedent).
- **Studio backfill**: on the same migration or a fast-follow data migration, backfill existing non-null `Studio.InstagramHandle` values into an *unverified* `SocialAccountLink` row (`IsVerified = false`, `VerificationMethod = null`) so the new table becomes the single source of truth immediately, per `database.md`'s zero-downtime migration order (add nullable → deploy dual-write if needed → backfill → cut over reads → drop old column in a later release). Do **not** drop `Studio.InstagramHandle` in this same change — cut reads over first, remove the column in a later release once the frontend fully reads from `SocialAccountLink`.

### 2. A new, separate OAuth state signer — don't touch `IInstagramStateSigner`

```csharp
public interface ISocialOAuthStateSigner
{
    string Sign(SocialLinkSubjectType subjectType, Guid subjectId, SocialPlatform platform);
    bool TryValidate(string state, out SocialLinkSubjectType subjectType, out Guid subjectId, out SocialPlatform platform);
}
```

New implementation (HMAC, same shape as `InstagramStateSigner`, different signed payload). The existing artist Instagram connect/callback endpoints keep using `IInstagramStateSigner` unchanged; only the *new* studio-Instagram and TikTok flows use this one. This keeps the already-hardened artist Instagram code path completely untouched.

### 3. Studio-level Instagram OAuth (new)

New endpoints, `OwnerOnly` (matches the existing artist connect/disconnect policy), under `/api/v1/studios/{id:guid}/social/instagram/...`:

- `GET /connect-url` → `ISocialOAuthStateSigner.Sign(Studio, studioId, Instagram)` → reuses **the existing** `IInstagramService.BuildAuthorizationUrl`.
- Callback (new anonymous route, e.g. `GET /api/v1/social/instagram/callback`, rate-limited same as the artist one) → `IInstagramService.ExchangeCodeAsync` → `GetUsernameAsync` → upsert `SocialAccountLink(Studio, studioId, Instagram, verified)`.
- **Recommend not persisting a long-lived token for the studio case at all.** Unlike the artist flow, there's no ongoing sync need here — this is a one-time identity check ("does this access token belong to the account whose handle we're about to display as verified"). Exchange the code, call `GetUsernameAsync`, record the verified handle, then discard the token rather than encrypting and storing it. Less secret material at rest, smaller blast radius if this table is ever compromised, and it avoids needing a refresh/expiry job for a value nothing reads again. Flagged as a recommendation, not a requirement — see open questions.
- `DELETE /disconnect` → clears verification (`IsVerified = false`, `VerificationMethod = null`), keeps `Handle` if the owner had one, `OwnerOnly`.
- `GET /status` → `OwnerOnly` (studio settings) — no `ArtistAndAbove`-equivalent audience here since there's no second "own profile" role on the studio side the way an artist has for their own connection.

### 4. TikTok OAuth (new platform, new service)

TikTok's OAuth (Login Kit for Web) is the second-lowest-friction platform to add after Instagram — no business-account requirement, well-documented redirect flow. New interface mirroring `IInstagramService`'s shape:

```csharp
public interface ITikTokService
{
    string BuildAuthorizationUrl(string state);
    Task<TikTokTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct);
    Task<string> GetUsernameAsync(string accessToken, CancellationToken ct);
}
```

New `TikTokOptions` (client ID/secret via the existing `ISecretsProvider`/Vault pattern — see the Decisions Log entry on per-tenant/platform secrets; do not hardcode, do not add a bespoke config path). New endpoints under `/api/v1/{artists|studios}/{id}/social/tiktok/...`, same shape as §3, same `ISocialOAuthStateSigner`, same anonymous rate-limited callback pattern, `OwnerOnly` for connect/disconnect on both subject types (mirrors the existing artist-Instagram policy — an artist doesn't self-connect either).

### 5. Manual bio-code verification (Facebook, X, YouTube — and as a fallback for Instagram/TikTok accounts that can't/won't OAuth)

- `POST /api/v1/{artists|studios}/{id}/social/{platform}/request-code` (`OwnerOnly`) → generates a short random code (e.g. `PENA-A1B2C3`), stores `PendingVerificationCode` + `PendingCodeExpiresAt` (recommend 48h), returns the code and instructions ("add this to your bio, then click Verify").
- `POST /api/v1/{artists|studios}/{id}/social/{platform}/verify-code` (`OwnerOnly`) → checks whether the code is present on the platform's public profile for `Handle`.

**Read this before building the check itself — this is the part of the requested scope with real risk attached, not a routine implementation detail:**

Automatically fetching and scanning a competitor platform's public profile HTML for a code string is functionally scraping. Instagram, TikTok, Facebook, and X all explicitly restrict automated scraping of their properties in their Terms of Service, independent of whether the page is "public" — and technically it's brittle against these platforms specifically (JS-rendered SPAs, login walls, bot detection that can rate-limit or block the studio's own outbound IP). This is a materially different risk profile from the OAuth paths above, which call each platform's sanctioned API. Two safer alternatives, either of which changes what actually gets built here:

- **oEmbed-based check**: Instagram, Facebook, TikTok, X, and YouTube all publish official oEmbed endpoints (ToS-compliant, no scraping). The catch: oEmbed generally returns metadata for a specific *post* URL, not a profile's bio text — so "put a code in your bio" may not be checkable via oEmbed at all, depending on platform. Needs a per-platform spike before committing to this as the mechanism (e.g., a "verify by posting once with this code in the caption, then paste the post URL" flow might work with oEmbed where "add this to your bio" doesn't).
- **Manual issuer review**: the owner submits the code + a link to their profile; an issuer-role support person visually confirms it, same trust model early X/Twitter verification used before it was automated. Zero ToS/scraping risk, but it's a support queue, not a self-serve instant flow — a UX trade-off, not just an engineering one.

**Recommend resolving which of these two (or a per-platform mix) before this becomes an overnight prompt** — building an automated scraper first and finding out post-hoc it violates a platform's ToS (with the studio's own credentials/IP on the line) is the wrong order of operations. This is flagged again in Open Questions below.

### 6. Public API changes

- `PublicArtistResponse` gains `IReadOnlyList<PublicSocialLinkResponse> SocialLinks` — `(SocialPlatform Platform, string Handle, bool IsVerified, string ProfileUrl)`. `ProfileUrl` built server-side per platform (`https://instagram.com/{handle}`, etc.) so the frontend never constructs platform URLs itself.
- `PublicStudioResponse`: same `SocialLinks` list, **replacing** the flat `InstagramHandle` field in the response shape (backed by the migrated data per §1) — keep `InstagramHandle` in the entity/DB for one release per the zero-downtime convention, but stop returning it once `SocialLinks` covers the same data.
- `InstagramConnectionStatusResponse` (existing, artist owner-settings-only) is unaffected — it's a different, owner-facing surface with different fields (`PostCount`, `LastSyncedAt`) that `SocialAccountLink` doesn't need to duplicate.

### 7. Frontend

- New `<VerifiedSocialBadge>` (`shared/components/`) — small checkmark + tooltip ("Verified Instagram account"), visually consistent with `ReviewSection.tsx`'s existing "Verified client" badge (same badge variant/color language, not a new visual system).
- New `SocialLinksCard`/`SocialLinksTab` (owner-facing, replaces the single Instagram text input on `StudioProfilePage.tsx` and extends `InstagramTab.tsx`'s pattern) — per platform: either a "Connect" button (OAuth platforms) or a "Get verification code" flow (manual platforms), each showing the resulting verified badge once done. Follow `InstagramTab.tsx`'s existing popup-window pattern for the OAuth button (`window.open` synchronously on click, before the awaited fetch, to avoid popup blockers — already solved once in this codebase, don't re-solve it differently).
- Public pages (`ArtistPortfolioPage.tsx`, `StudioPortfolioPage.tsx`): render each social link with its platform icon, `@handle`, and `<VerifiedSocialBadge>` when verified; unverified links still show as a plain outbound link (an owner who hasn't verified yet shouldn't lose their handle display, just the badge).
- Platform icons: confirm brand-guideline-compliant usage (Instagram/TikTok/Facebook/X/YouTube each have specific logo-usage rules) with whoever owns frontend/brand review before shipping — not an engineering call, flagged in Open Questions.

### 8. Help-sync (CLAUDE.md rule #7 — not optional)

- `frontend/src/features/help/helpContent.ts`: update the existing Studio Settings entry (currently says "...address, phone, Instagram, description...") to describe the new multi-platform + verification flow; add/extend the artist-profile entry covering the (now multi-platform) social tab and what the "Verified" badge means to a client viewing the public page.
- `frontend/public/user-manual/index.html`: mirror the same content per the existing "keep these two surfaces in sync" rule.
- `frontend/src/features/help/tours/ownerTour.ts` / `artistTour.ts`: check whether either currently targets the Instagram tab/field (none currently do, based on what's in the repo today) — if this feature adds a new prominent setup step, consider whether it belongs in the tour, but don't force a tour step in for its own sake if the existing tours don't cover comparable settings fields either.

### 9. Tests

- Unit: `ISocialOAuthStateSigner` sign/validate round-trip (including tamper rejection, mirroring `InstagramStateSignerTests` if that exists); manual-code request/verify flow (correct code passes, expired code rejected, wrong code rejected, idempotent re-verification); `SocialAccountLink` upsert on artist Instagram OAuth success writes both `InstagramConnection` and `SocialAccountLink` correctly.
- Integration: studio Instagram connect → callback → `SocialAccountLink` verified, mirrors `ExchangeInstagramCodeHandler`'s existing test coverage style; suspended-studio check (same class of bug already fixed twice for the artist Instagram path — write the equivalent test for the studio path from day one instead of discovering it in a third bug-hunt).
- Frontend: `SocialLinksTab`/`SocialLinksCard` tests mirroring `InstagramTab.test.tsx`'s existing structure; public page tests asserting the verified badge only renders when `IsVerified` is true.

---

## Constraints (per project rules)

- **Tenant isolation**: `SocialAccountLink` has no global query filter (documented exception, same shape as `InstagramConnection`) — every handler must filter by `SubjectId` + verify the caller's tenant/role owns that subject explicitly. Do not assume the lack of a filter is safe by default.
- **RBAC**: every new endpoint needs `.RequireAuthorization()` with an explicit policy — `OwnerOnly` for all connect/disconnect/request-code/verify-code actions on both subjects (matches the existing artist-Instagram precedent where the artist doesn't self-manage the connection either), `ArtistAndAbove` or public/anonymous only where the existing Instagram endpoints already use that level.
- **Never log PII**: never log OAuth tokens (even the encrypted form) or full profile URLs alongside `tenant_id`/`user_id`; handle/username on its own is public-facing display data already returned by the public API, but keep logging to `tenant_id`/`user_id`/`request_id`/platform enum only, per the existing rule.
- **Secrets never in source**: every new platform's OAuth client ID/secret goes through `ISecretsProvider`/Vault (the already-established pattern from the Decisions Log's per-tenant secrets entry), not appsettings, not hardcoded.
- **Structured logs only**, **RequireAuthorization on every new endpoint except the anonymous, rate-limited OAuth callbacks** (which authenticate via the signed `state` param instead, matching the existing Instagram callback's documented exception).
- **Industry-standard benchmark (rule #6)**: verified-social badges are standard on Vagaro/Fresha/Boulevard/GlossGenius-tier profiles for the primary platform (Instagram); TikTok/Facebook/X/YouTube verification is not something these benchmark platforms commonly do today — flagging this explicitly since the requested scope goes beyond what the benchmark set actually does, not because it's wrong, but because it's a deliberate above-benchmark investment the requester should be making with eyes open, not by default.

---

## Open questions for whoever picks this up

1. **Manual verification mechanism** (§5) — automated scraping vs. oEmbed-where-possible vs. issuer manual review. This is the single highest-impact open decision in this spec: it changes what code actually gets written, and shipping an automated scraper without resolving it risks both a ToS violation and an unreliable feature (bot walls, JS rendering) that looks broken to users. Needs a decision before this becomes an overnight prompt.
2. **Phasing across five platforms** — Instagram (reuse) and TikTok (Login Kit, low friction) are the two platforms where OAuth is a reasonable near-term investment. Facebook Graph API, X API v2 (current pricing tiers make even read-only automated checks costly/rate-limited), and YouTube (Google OAuth consent-screen verification requirements for sensitive scopes at scale) each carry their own app-registration and review-timeline cost. Recommend confirming whether all five ship together or Instagram+TikTok ship first with the rest manual-only (or deferred) until there's real artist/studio demand for them specifically.
3. **Studio OAuth token retention** (§3) — this spec recommends discarding the token immediately after the identity check rather than persisting it, since there's no ongoing sync need on the studio side. Confirm that's acceptable, or specify a reason to keep it (e.g. planned future studio-level content sync analogous to the artist one).
4. **Re-verification over time** — an artist/studio could revoke platform-side access without disconnecting in-app, or delete a manual bio code after passing the one-time check. Recommend: OAuth-verified links get folded into a periodic reverification job (refresh failure → un-verify), similar cadence to `InstagramSyncJob`; manual-code links are treated as a one-time proof-of-ownership snapshot with no ongoing recheck (continuous automated rechecking is exactly the scraping-risk pattern flagged in #1). Confirm this asymmetry is acceptable.
5. **`Studio.InstagramHandle` column removal timing** — this spec backfills into `SocialAccountLink` and stops *reading* the old column from the public API, but explicitly does not drop it in the same release, per the zero-downtime convention. Confirm the follow-up removal gets scheduled rather than left indefinitely.
6. **Platform icon/brand usage** — confirm brand-guideline compliance for displaying Instagram/TikTok/Facebook/X/YouTube logos with whoever owns frontend/brand review; not resolved in this spec.
