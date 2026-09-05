# Overnight Prompt — Social Media Verification (Artist & Owner Profiles)

## Pre-flight

- Read first: `CLAUDE.md`, `docs/claude/backend.md`, `docs/claude/database.md`, `docs/claude/conventions.md`, `feature-request-social-media-verification.md` (the spec this prompt implements — read it in full, it has the reasoning this prompt only summarizes).
- Also read the real files this prompt builds on top of, not just this document's excerpts of them: `Pena_e_Arte.Domain/Entities/InstagramConnection.cs`, `Pena_e_Arte.Domain/Interfaces/IInstagramService.cs`, `Pena_e_Arte.Domain/Interfaces/IInstagramStateSigner.cs`, `Pena_e_Arte.Infrastructure/Services/InstagramService.cs`, `Pena_e_Arte.Infrastructure/Services/AesTokenEncryptor.cs`, `Pena_e_Arte.API/Endpoints/InstagramEndpoints.cs`, `frontend/src/features/artists/components/InstagramTab.tsx`, `frontend/src/features/studios/components/StudioProfilePage.tsx`.
- **This is a large, multi-platform feature. Read "Out of Scope" and the per-platform notes in Part 4/5 before starting** — three of the five platforms (Facebook, X, YouTube) need this session to write real, correct provider code but their exact OAuth/API request shapes are called out explicitly as "confirm against current platform docs before implementing" rather than hardcoded from this prompt's memory, the same way the codebase's own `feature-request-two-sided-referrals.md` flagged "verify against the currently pinned Stripe.net SDK version before implementing" instead of guessing. Do not invent endpoint URLs or param names for those three providers if the current docs don't match what's below — match the docs, and note in the commit/PR description exactly what changed from this spec's assumption.
- Two of the five platforms (Facebook, X) also have **external, non-code dependencies that cannot be completed inside this session**: a Meta app review for the permissions this needs, and an X API paid-tier subscription respectively. This prompt builds all the code so both work the moment real credentials exist, but ships with those two platforms' options empty by default — see Part 2's "config-gated providers" design. Do not treat empty credentials as a bug to fix; it's the intended state until a human completes those external steps.

---

## Context — current state (verified against live source, 2026-08-22)

- **Artist Instagram OAuth is fully built and hardened** (`InstagramConnection`, `IInstagramService`, `InstagramSyncJob`, `InstagramEndpoints.cs`). It syncs portfolio photos. It has been through three rounds of bug fixes already (suspended-studio checks on the public feed and the sync job, rate limiting on the OAuth callback) — documented in `architecture.md`'s Decisions Log. **Do not modify this code.** Everything in this prompt is additive.
- **`PublicArtistResponse` exposes no Instagram field at all** — the OAuth connection's existence is invisible to clients today; only the synced photos are shown via a separate endpoint, with no verified badge or confirmed handle anywhere.
- **`Studio.InstagramHandle`** is a free-text `string?` with zero verification — set via `UpdateMyStudioCommand`, displayed via `PublicStudioResponse.InstagramHandle`, edited via a single labeled `<Input>` on `StudioProfilePage.tsx`.
- **RBAC policies available**: `ClientOnly`, `ClientAndAbove`, `ArtistAndAbove`, `OwnerOnly` (roles `owner` **and** `issuer`), `IssuerOnly` (`Pena_e_Arte.API/Extensions/AuthorizationExtensions.cs`).
- **`ISecretsProvider.GetSecretAsync(string key, CancellationToken ct)`** — Vault-backed, fail-closed (throws, never returns null), key format `"<path>:<field>"`.
- **`IAppSettings.BaseUrl`** — the one property this app already uses to build redirect URLs (see `InstagramEndpoints.HandleCallback`'s `$"{appSettings.BaseUrl}/artists?instagram=denied"` pattern).
- **`ICurrentTenant`** — `StudioId`, `IsSet`, `SetTenant(Guid)`. Not guaranteed set for an `issuer`-role caller acting cross-tenant — **never trust `ICurrentTenant.StudioId` as the target studio for an `OwnerOnly` action; resolve the target's real `StudioId` from the target entity itself** (exactly how `InstagramConnection`'s own doc comment already describes doing this for artists).
- **The app already has a *different*, narrower Google integration** — `IOAuthTokenValidator` validates a Google-issued ID token for "Sign in with Google" login. That is **not** the same thing as an OAuth authorization-code flow with API scopes, and cannot be reused for reading YouTube channel data. Do not conflate the two or assume the existing `Google:ClientId` config entry already covers this feature's YouTube OAuth needs.
- **AuditActions / AuditTargetTypes** (`Pena_e_Arte.Domain/Constants/AuditActions.cs`) is the existing pattern for owner/issuer-visible audit trail entries — extend it, don't invent a parallel logging mechanism.

---

## Decisions (already made with the product owner — do not re-litigate)

1. **Platforms**: Instagram, TikTok, Facebook, X, YouTube. All five get code written for both verification paths in this pass, config-gated per platform (see Part 2) so partially-configured platforms don't break the build or the app at runtime.
2. **Verification methods**: both OAuth "Connect" (where a real API supports it) and a manual bio/profile-code check. **The manual check uses each platform's own official public-read API where one exists — never HTML scraping.** Where no such API exists for a platform (TikTok), the manual path is not built for that platform; OAuth is that platform's only verification route. This was chosen explicitly over scraping (ToS risk, technical fragility) and over "add a code to your bio then screenshot it for support to review" (a support-queue UX the product owner did not choose).
3. **Studio-level OAuth token retention**: discard the access token immediately after confirming the handle. No refresh job, no long-lived secret at rest for the studio case — there's no ongoing sync need, only a one-time identity check.
4. **`SocialAccountLink` is new and additive.** `InstagramConnection` is untouched and keeps owning artist photo sync exactly as today. The new table becomes the one place every "Verified" badge reads from, across both subjects and all five platforms — including Instagram, where the existing OAuth success handler gets one new line to also write into this table.
5. **Re-verification**: OAuth-verified links get folded into a periodic Hangfire job (same cadence family as `InstagramSyncJob`) that re-checks token validity and un-verifies on failure. Manual-code-verified links are a one-time proof-of-ownership snapshot — no ongoing recheck (recurring automated checks against a platform's public API for this purpose risk hitting rate limits for no real benefit; a link can always be re-verified manually by request).

---

## Part 1 — Domain + Enums + EF Core

### 1a. `Pena_e_Arte.Domain/Enums/SocialPlatform.cs`

```csharp
namespace Pena_e_Arte.Domain.Enums;

public enum SocialPlatform
{
    Instagram,
    TikTok,
    Facebook,
    X,
    YouTube
}
```

### 1b. `Pena_e_Arte.Domain/Enums/SocialLinkSubjectType.cs`

```csharp
namespace Pena_e_Arte.Domain.Enums;

public enum SocialLinkSubjectType
{
    Artist,
    Studio
}
```

### 1c. `Pena_e_Arte.Domain/Enums/SocialVerificationMethod.cs`

```csharp
namespace Pena_e_Arte.Domain.Enums;

public enum SocialVerificationMethod
{
    OAuthConnect,
    ManualBioCode
}
```

### 1d. `Pena_e_Arte.Domain/Entities/SocialAccountLink.cs`

```csharp
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// One artist's or studio's link to one social platform, with optional verification.
/// No global query filter (see AppDbContext) — same documented shape as
/// InstagramConnection. Every handler must filter by (SubjectType, SubjectId) explicitly
/// and resolve/verify the real owning StudioId from the target entity, never trust
/// ICurrentTenant blindly (an issuer-role caller may have no tenant set at all).
///
/// For a Studio-subject row, StudioId == SubjectId (the studio's own id) — Studio is
/// issuer-level/unfiltered, so this is a self-referential tenant key, not a bug.
/// For an Artist-subject row, StudioId is that artist's real tenant.
/// </summary>
public class SocialAccountLink : TenantEntity
{
    public SocialLinkSubjectType SubjectType { get; set; }
    public Guid SubjectId { get; set; }               // ArtistId or StudioId
    public SocialPlatform Platform { get; set; }
    public string Handle { get; set; } = "";           // display handle, no leading '@'
    public bool IsVerified { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public SocialVerificationMethod? VerificationMethod { get; set; }

    // OAuth path only — null otherwise. Discarded immediately for Studio-subject rows
    // per Decision 3 above; kept + refreshed for Artist-subject rows per Decision 5.
    public string? ExternalUserId { get; set; }
    public string? EncryptedToken { get; set; }
    public DateTime? TokenExpiresAt { get; set; }

    // Manual bio-code path only — null once verified or expired.
    public string? PendingVerificationCode { get; set; }
    public DateTime? PendingCodeExpiresAt { get; set; }
}
```

### 1e. `AppDbContext.cs`

Add the `DbSet` **without** a `HasQueryFilter` call — mirror exactly how `InstagramConnection` is registered (grep `AppDbContext.cs` for the `InstagramConnection`/`InstagramPost` `DbSet` lines and place this alongside them, same comment style explaining why there's no filter).

```csharp
public DbSet<SocialAccountLink> SocialAccountLinks => Set<SocialAccountLink>();
```

Also add to `Pena_e_Arte.Application/Persistence/IAppDbContext.cs` (the interface `AppDbContext` implements — check its current `DbSet<InstagramConnection>` line and add this next to it in the same style).

### 1f. `Pena_e_Arte.Infrastructure/Persistence/Configurations/SocialAccountLinkConfiguration.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class SocialAccountLinkConfiguration : IEntityTypeConfiguration<SocialAccountLink>
{
    public void Configure(EntityTypeBuilder<SocialAccountLink> builder)
    {
        builder.ToTable("social_account_links");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedOnAdd();

        builder.Property(s => s.StudioId).IsRequired();
        builder.Property(s => s.SubjectType).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.Platform).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.VerificationMethod).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.Handle).HasMaxLength(60).IsRequired();
        builder.Property(s => s.EncryptedToken).HasMaxLength(2048);
        builder.Property(s => s.PendingVerificationCode).HasMaxLength(32);

        builder.HasIndex(s => new { s.SubjectType, s.SubjectId, s.Platform })
               .IsUnique()
               .HasDatabaseName("ix_social_account_links_subject_platform");

        // No HasOne/WithMany navigation — SubjectId is polymorphic (Artist or Studio),
        // same reasoning InstagramConnection uses a direct FK only because it's
        // single-subject; this entity deliberately has none.
    }
}
```

### 1g. Migration

```bash
dotnet ef migrations add AddSocialAccountLinks \
  --project Pena_e_Arte.Infrastructure --startup-project Pena_e_Arte.API
```

### 1h. Studio backfill (same migration, `Up()` method, raw SQL or a one-time data-seed step — confirm which pattern this codebase's existing backfill migrations use, e.g. `AddStudioContactInfo`, and match it exactly rather than inventing a new backfill style)

Backfill every non-null, non-empty `Studio.InstagramHandle` into an **unverified** `SocialAccountLink` row:

```sql
INSERT INTO social_account_links
  (id, studio_id, subject_type, subject_id, platform, handle, is_verified, created_at, updated_at)
SELECT
  UUID(), id, 'Studio', id, 'Instagram', instagram_handle, 0, NOW(), NOW()
FROM studios
WHERE instagram_handle IS NOT NULL AND instagram_handle <> '';
```

Do **not** drop `Studio.InstagramHandle` in this migration — see Part 8 and `database.md`'s zero-downtime column-removal order. Schedule the column drop as a documented follow-up, not silently forgotten.

---

## Part 2 — Config + platform provider registry (config-gated, partial-rollout safe)

### 2a. `Pena_e_Arte.API/appsettings.json`

Add a `Social` section, sibling to the existing `Instagram` section (leave `Instagram` exactly as-is — it still powers the artist photo-sync flow):

```json
"Social": {
  "TikTok":   { "ClientKey": "", "ClientSecret": "", "RedirectUri": "" },
  "Facebook": { "AppId": "",     "AppSecret": "",     "RedirectUri": "" },
  "X":        { "ClientId": "",  "ClientSecret": "",  "RedirectUri": "", "BearerToken": "" },
  "YouTube":  { "ClientId": "",  "ClientSecret": "",  "RedirectUri": "", "ApiKey": "" }
}
```

`BearerToken` (X) and `ApiKey` (YouTube) are for the **manual-check** path's app-level public read calls — distinct from the OAuth `ClientId`/`ClientSecret` pair used for the **Connect** path. A platform can have one configured without the other (e.g. YouTube's manual check can work from just an `ApiKey` even before OAuth `ClientId`/`ClientSecret` exist).

### 2b. `SocialOptions.cs` (`Pena_e_Arte.Infrastructure/Services/`)

One options class per platform (`TikTokOptions`, `FacebookOptions`, `XOptions`, `YouTubeOptions`), each bound the same way `InstagramOptions` already is (`services.Configure<TOptions>(configuration.GetSection(...))`). Follow `InstagramOptions.cs`'s exact shape/`const string Section` pattern — read that file before writing these four.

### 2c. Provider registry — the mechanism that makes "ship all five, OAuth where possible" safe

```csharp
// Pena_e_Arte.Domain/Interfaces/ISocialOAuthProvider.cs
namespace Pena_e_Arte.Domain.Interfaces;

public record SocialOAuthTokenResponse(string AccessToken, string? ExternalUserId, DateTime? ExpiresAt);

public interface ISocialOAuthProvider
{
    SocialPlatform Platform { get; }
    /// <summary>False when this platform's OAuth client credentials aren't configured yet
    /// (e.g. Facebook/X pending external app review). Callers must check this before
    /// building a connect URL and return a clear "not yet available" response, not a
    /// generic 500.</summary>
    bool IsConfigured { get; }
    string BuildAuthorizationUrl(string state);
    Task<SocialOAuthTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct);
    Task<string> GetUsernameAsync(string accessToken, CancellationToken ct);
}

// Pena_e_Arte.Domain/Interfaces/ISocialOAuthProviderFactory.cs
public interface ISocialOAuthProviderFactory
{
    ISocialOAuthProvider GetProvider(SocialPlatform platform); // throws PlatformNotSupportedException if none registered at all (should never happen — all 5 get a provider class); IsConfigured is the runtime gate, not this.
}
```

Register all five implementations (Instagram's wraps the **existing** `IInstagramService` rather than duplicating its HTTP calls — see Part 4a) via DI; `SocialOAuthProviderFactory` resolves by `SocialPlatform` from `IEnumerable<ISocialOAuthProvider>`. Every endpoint that starts a connect flow checks `provider.IsConfigured` first and returns a `409 Conflict` (or your existing convention for "feature not yet available" — check for a precedent instead of inventing a new response shape) with a message like `"{platform} isn't connected on this server yet."` rather than attempting the flow with empty credentials.

---

## Part 3 — Generic OAuth state signer (separate from the artist-only `IInstagramStateSigner` — do not touch that one)

```csharp
// Pena_e_Arte.Domain/Interfaces/ISocialOAuthStateSigner.cs
namespace Pena_e_Arte.Domain.Interfaces;

public interface ISocialOAuthStateSigner
{
    string Sign(SocialLinkSubjectType subjectType, Guid subjectId, SocialPlatform platform);
    bool TryValidate(string state, out SocialLinkSubjectType subjectType, out Guid subjectId, out SocialPlatform platform);
}
```

Implement `SocialOAuthStateSigner` with the same HMAC-SHA256 signing approach as `InstagramStateSigner` (check its implementation file, not just the interface, for the exact signing/encoding scheme — key length, encoding, delimiter choice — and reuse the same secret-resolution pattern via `ISecretsProvider`, a **new** Vault key, not the Instagram one). Payload: `{subjectType}|{subjectId}|{platform}` (or whatever delimiter-safe encoding `InstagramStateSigner` already uses — match it, don't invent a different one two files apart in the same codebase).

The artist-Instagram connect/callback flow keeps using `IInstagramStateSigner`, completely unchanged. Only the new endpoints in Part 6 use this new signer.

---

## Part 4 — Platform OAuth providers

### 4a. Instagram — wraps the existing service, does not duplicate it

```csharp
public sealed class InstagramSocialOAuthProvider(IInstagramService instagram, IOptions<InstagramOptions> options) : ISocialOAuthProvider
{
    public SocialPlatform Platform => SocialPlatform.Instagram;
    public bool IsConfigured => !string.IsNullOrEmpty(options.Value.AppId);

    public string BuildAuthorizationUrl(string state) => instagram.BuildAuthorizationUrl(state);

    public async Task<SocialOAuthTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        var token = await instagram.ExchangeCodeAsync(code, ct);
        return new SocialOAuthTokenResponse(token.AccessToken, token.UserId, DateTime.UtcNow.AddSeconds(token.ExpiresIn));
    }

    public Task<string> GetUsernameAsync(string accessToken, CancellationToken ct) =>
        instagram.GetUsernameAsync(accessToken, ct);
}
```

This is what lets a **studio** connect Instagram using the exact same underlying Instagram API integration an **artist** already uses — no second Instagram HTTP client, no duplicated token-exchange logic.

### 4b. TikTok — new, low-friction (Login Kit for Web, no business-account requirement)

New `ITikTokHttpClient`/`TikTokSocialOAuthProvider` mirroring `InstagramService.cs`'s internal HTTP-call structure (same `HttpClientFactory`-based pattern, same `IOptions<TikTokOptions>` injection). **Confirm the current authorize/token/userinfo endpoint paths and required scopes against TikTok's live developer docs before writing the HTTP calls** — this is stable enough to name here (`https://www.tiktok.com/v2/auth/authorize/` for the authorize step, token exchange via `https://open.tiktokapis.com/v2/oauth/token/`, user info via `https://open.tiktokapis.com/v2/user/info/`) but confirm exact required scopes/response field names before implementing `ExchangeCodeAsync`/`GetUsernameAsync` — TikTok's Developer Portal is the source of truth, not this document's memory of it.

### 4c. Facebook — reuses the same Meta Graph API family as Instagram, but is its own app permission set

New `FacebookSocialOAuthProvider`. Facebook Login's OAuth dialog (`https://www.facebook.com/v{version}/dialog/oauth`) and token exchange (`https://graph.facebook.com/v{version}/oauth/access_token`) are stable, well-documented endpoints — but **the exact permission scope needed to read a Page's public identity, and whether that scope requires Meta App Review before it works for non-test users, needs confirming against Meta's current Graph API version and App Review requirements before this ships to real users.** This is exactly the "external dependency, not just code" case flagged in Pre-flight — write the provider class and its config wiring regardless (so it compiles and is ready), but do not assume App Review is unnecessary.

### 4d. X — new, and the platform most likely to need a paid API tier

New `XSocialOAuthProvider` (OAuth 2.0 with PKCE, per X API v2's current auth model — `https://twitter.com/i/oauth2/authorize`, token exchange at `https://api.twitter.com/2/oauth2/token`, user lookup at `https://api.twitter.com/2/users/me`). **Confirm current required scopes (`tweet.read users.read` at minimum) and which access tier (Free/Basic/Pro) the `users/me` and username-lookup endpoints require before implementing** — X's API access tiers and pricing have changed multiple times; treat anything this document says about tier requirements as unverified until checked against X's current developer docs.

### 4e. YouTube — new, and distinct from the existing "Sign in with Google" integration

New `YouTubeSocialOAuthProvider` using Google's real OAuth 2.0 authorization-code flow (`https://accounts.google.com/o/oauth2/v2/auth` with scope `https://www.googleapis.com/auth/youtube.readonly`, token exchange at `https://oauth2.googleapis.com/token`) — **not** `IOAuthTokenValidator` (that only validates an already-issued ID token for login; it has no authorization-code exchange or scoped-access-token capability). `GetUsernameAsync` calls YouTube Data API v3's `channels.list?mine=true&part=snippet` with the resulting access token. Needs its own Google Cloud OAuth client (can live under the same Google Cloud project as the existing login client, but needs new scopes/consent-screen entries — **confirm with whoever manages that Cloud project whether the OAuth consent screen needs re-verification for a new sensitive-adjacent scope**, since YouTube read scopes can trigger Google's verification requirements for apps with many users).

---

## Part 5 — Manual bio-code verification (official public-read APIs only — no scraping)

### 5a. Code lifecycle

```csharp
// Pena_e_Arte.Application/Social/Commands/RequestSocialVerificationCodeCommand.cs
public record RequestSocialVerificationCodeCommand(
    SocialLinkSubjectType SubjectType, Guid SubjectId, SocialPlatform Platform)
    : IRequest<RequestSocialVerificationCodeResult>;

public record RequestSocialVerificationCodeResult(string Code, DateTime ExpiresAt);
```

Handler: resolves/creates the `SocialAccountLink` row, generates a code like `PENA-{6 random alphanumeric chars, unambiguous set — exclude 0/O/1/I}`, sets `PendingVerificationCode`/`PendingCodeExpiresAt = now + 48h`, saves, returns it. `OwnerOnly`.

### 5b. Per-platform checker abstraction

```csharp
// Pena_e_Arte.Domain/Interfaces/ISocialBioChecker.cs
public interface ISocialBioChecker
{
    SocialPlatform Platform { get; }
    /// <summary>False when this platform has no official public-read API suitable for
    /// this check (TikTok) — callers must surface "manual verification isn't available
    /// for this platform, use Connect instead" rather than attempting a check.</summary>
    bool IsSupported { get; }
    Task<bool> BioContainsCodeAsync(string handle, string code, CancellationToken ct);
}
```

- **`InstagramBioChecker` / `FacebookBioChecker`** — Meta Graph API **Business Discovery** field (`GET /{app-scoped-ig-user-id}?fields=business_discovery.username({handle}){biography}`, using the app's own long-lived Business/Creator access token, not the target's). Officially sanctioned for exactly this "read another public Business/Creator account's public data" use case — not scraping. **Only works when the target account is a Business or Creator account** — same constraint the existing Instagram OAuth flow already has. A personal Instagram/Facebook account can't be manually verified this way; it needs the OAuth path instead (which has the same Business/Creator requirement — flag to the owner in the UI that a personal account genuinely cannot be verified on either path, that's a real platform limitation, not a gap in this feature).
- **`YouTubeBioChecker`** — YouTube Data API v3, `GET /youtube/v3/channels?forHandle={handle}&part=snippet&key={ApiKey}` (confirm `forHandle` vs. the older `forUsername` param against the current API version — YouTube moved to handle-based lookups relatively recently), reads `items[0].snippet.description`. Needs only an API key, no OAuth from the target — lowest-friction of all five manual checks.
- **`XBioChecker`** — X API v2, `GET /2/users/by/username/{username}?user.fields=description`, app-only Bearer token. **Confirm current access-tier requirements before relying on this** (Part 4d's caveat applies here too — the read tier available at Free access has changed repeatedly).
- **`TikTokBioChecker`**`.IsSupported => false`. TikTok has no general-purpose "read any public account's bio via an app-level API key/token" endpoint documented as stable/available; its Display API only reads the *authenticated* user's own data. Verification for TikTok is OAuth-only in this feature — do not attempt a scraper as a substitute; that's exactly the risk Decision 2 ruled out.

### 5c. Verify endpoint

```csharp
// Pena_e_Arte.Application/Social/Commands/VerifySocialBioCodeCommand.cs
public record VerifySocialBioCodeCommand(
    SocialLinkSubjectType SubjectType, Guid SubjectId, SocialPlatform Platform)
    : IRequest<VerifySocialBioCodeResult>;

public record VerifySocialBioCodeResult(bool Verified, string? FailureReason);
```

Handler: loads the pending `SocialAccountLink`, 422s if no pending code or it's expired ("Request a new code and try again"), 422s if `!checker.IsSupported` ("This platform can't be verified this way — use Connect instead"), otherwise calls `BioContainsCodeAsync`; on match sets `IsVerified = true`, `VerifiedAt = now`, `VerificationMethod = ManualBioCode`, clears the pending code; on no match returns a friendly retry message (the code may take a minute to propagate on the platform's side) without clearing the pending code so the owner can just retry. `OwnerOnly`.

---

## Part 6 — Application layer + API endpoints

Follow `Pena_e_Arte.Application/Instagram/`'s existing folder shape for a new `Pena_e_Arte.Application/Social/` folder (`Commands/`, `Queries/`, `Validators/`). Beyond the two commands already shown in Part 5:

- `GetSocialConnectUrlQuery(SubjectType, SubjectId, Platform)` — `OwnerOnly`. Resolves the provider via `ISocialOAuthProviderFactory`, 409s if `!IsConfigured`, otherwise signs state via `ISocialOAuthStateSigner` and returns `provider.BuildAuthorizationUrl(state)`.
- `ExchangeSocialOAuthCodeCommand(SubjectType, SubjectId, Platform, Code)` — called from the **anonymous** callback only (never exposed as an authenticated endpoint — matches how `ExchangeInstagramCodeCommand` is only ever invoked from `HandleCallback`). Exchanges the code, calls `GetUsernameAsync`, upserts the `SocialAccountLink` (`IsVerified = true`, `VerificationMethod = OAuthConnect`). For `SubjectType.Studio`, discard the token per Decision 3 (`EncryptedToken`/`TokenExpiresAt` left null). For `SubjectType.Artist` + `Platform.Instagram`, this is also the hook point from Part 7 — see below, this command is *not* used for that specific combination, `ExchangeInstagramCodeCommand` stays the entry point and gains one extra line instead.
- `DisconnectSocialAccountCommand(SubjectType, SubjectId, Platform)` — `OwnerOnly`. Clears verification fields, keeps `Handle`.
- `GetSocialLinksQuery(SubjectType, SubjectId)` — `ArtistAndAbove` for an artist's own subject, `OwnerOnly` for a studio subject (an artist has no "own studio" concept to view via this route) — returns all platforms' links for that subject, verified or not, for the owner-facing settings UI.

### API endpoints — one generic route family, not one per platform

```csharp
// Pena_e_Arte.API/Endpoints/SocialEndpoints.cs
RouteGroupBuilder artistGroup = app.MapGroup("/api/v1/artists/{id:guid}/social").RequireAuthorization();
artistGroup.MapGet("/", GetArtistSocialLinks).RequireAuthorization("ArtistAndAbove");
artistGroup.MapGet("/{platform}/connect-url", GetConnectUrl).RequireAuthorization("OwnerOnly");
artistGroup.MapDelete("/{platform}/disconnect", Disconnect).RequireAuthorization("OwnerOnly");
artistGroup.MapPost("/{platform}/request-code", RequestCode).RequireAuthorization("OwnerOnly");
artistGroup.MapPost("/{platform}/verify-code", VerifyCode).RequireAuthorization("OwnerOnly");

RouteGroupBuilder studioGroup = app.MapGroup("/api/v1/studios/{id:guid}/social").RequireAuthorization();
// same five routes, all OwnerOnly (no ArtistAndAbove-equivalent viewer on the studio side)

// Anonymous, rate-limited the same way the existing Instagram callback is ("public-write" —
// confirm that's still the correct policy name in RateLimitingExtensions.cs before reusing it)
app.MapGet("/api/v1/social/{platform}/callback", HandleSocialCallback)
   .AllowAnonymous()
   .RequireRateLimiting("public-write");
```

`{platform}` binds as a route string, parsed to `SocialPlatform` with a 400 on an unrecognized value (don't let an arbitrary string reach the provider factory). `HandleSocialCallback` validates `state` via `ISocialOAuthStateSigner.TryValidate` (recovering `subjectType`, `subjectId`, `platform`), then dispatches to `ExchangeSocialOAuthCodeCommand` — for the one exception (`Artist` + `Instagram`), redirect through the *existing* Instagram callback path instead so `InstagramConnection` stays the single writer for that specific combination; see Part 7.

---

## Part 7 — Hook the existing Instagram artist flow into the new table (the only change to Instagram code in this entire prompt)

In `ExchangeInstagramCodeCommand`'s handler (`Pena_e_Arte.Application/Instagram/Commands/ExchangeInstagramCodeCommand.cs`), after the existing `InstagramConnection` is successfully saved, add an upsert into `SocialAccountLink`:

```csharp
// after the existing InstagramConnection save succeeds:
SocialAccountLink? link = await db.SocialAccountLinks
    .FirstOrDefaultAsync(s => s.SubjectType == SocialLinkSubjectType.Artist
                            && s.SubjectId == artistId
                            && s.Platform == SocialPlatform.Instagram, ct);
if (link is null)
{
    link = new SocialAccountLink
    {
        StudioId = artist.StudioId,     // resolved from the artist already loaded above in this handler, not ICurrentTenant
        SubjectType = SocialLinkSubjectType.Artist,
        SubjectId = artistId,
        Platform = SocialPlatform.Instagram,
    };
    db.SocialAccountLinks.Add(link);
}
link.Handle = connection.Username;
link.IsVerified = true;
link.VerifiedAt = DateTime.UtcNow;
link.VerificationMethod = SocialVerificationMethod.OAuthConnect;
link.ExternalUserId = connection.InstagramUserId;
```

And in `DisconnectInstagramCommand`'s handler, clear the corresponding `SocialAccountLink`'s verification the same way `DisconnectSocialAccountCommand` does (don't delete the row — an owner disconnecting shouldn't lose the last-known handle display).

This is the only edit to any file under `Pena_e_Arte.Application/Instagram/` or `Pena_e_Arte.Domain/Entities/InstagramConnection.cs` in this entire prompt. Do not touch `InstagramSyncJob.cs`, `InstagramService.cs`, or any test file under the existing `Instagram/` test folders.

---

## Part 8 — Public API changes

### 8a. `Pena_e_Arte.Contracts/Responses/Public/PublicSocialLinkResponse.cs` (new)

```csharp
namespace Pena_e_Arte.Contracts.Responses.Public;

public record PublicSocialLinkResponse(
    string Platform,     // SocialPlatform.ToString() — confirm existing convention for enum-to-string in public responses (HasConversion<string> elsewhere uses the enum name directly; match that instead of inventing a different casing)
    string Handle,
    bool IsVerified,
    string ProfileUrl);  // built server-side, e.g. https://instagram.com/{handle} — frontend never constructs platform URLs itself
```

### 8b. `PublicArtistResponse` — add `IReadOnlyList<PublicSocialLinkResponse> SocialLinks`

### 8c. `PublicStudioResponse` — add `IReadOnlyList<PublicSocialLinkResponse> SocialLinks`, **stop returning** `InstagramHandle` from this response (keep the DB column per Part 1h — this is a response-shape change only, not a schema change). Update every handler that constructs `PublicStudioResponse`/`PublicArtistResponse` (`GetPublicStudioQuery`, `GetPublicArtistQuery` — grep for all construction sites, don't assume there's only one) to populate `SocialLinks` from `db.SocialAccountLinks.Where(...)` filtered to the subject, `IsVerified` or not (unverified links still display as a plain link, per the frontend spec below), ordered by `Platform`.

### 8d. Frontend contract types

Update `publicApi.ts`'s existing `PublicArtistResponse`/`PublicStudioResponse` TypeScript interfaces to match — check every existing consumer of `studio.instagramHandle` in the frontend (`StudioPortfolioPage.tsx` at minimum) and migrate it to read `socialLinks` instead.

---

## Part 9 — Frontend

### 9a. `<VerifiedSocialBadge>` (`frontend/src/shared/components/VerifiedSocialBadge.tsx`, new)

Small checkmark + tooltip, visually consistent with `ReviewSection.tsx`'s existing "Verified client" badge — read that component first and reuse its badge variant/color token rather than inventing a new one.

### 9b. Owner-facing settings

- Replace `StudioProfilePage.tsx`'s single Instagram `<Input>` with a new `SocialLinksCard` (`frontend/src/features/studios/components/SocialLinksCard.tsx`) — one row per platform (Instagram, TikTok, Facebook, X, YouTube), each showing: current handle (editable as plain text if unverified), a `<VerifiedSocialBadge>` if verified, and either a "Connect" button (if `provider.isConfigured` — a platform's connect button should be hidden or disabled with a tooltip when the backend reports it's not configured yet, not shown as a broken action) or a "Get verification code" flow for platforms/accounts using the manual path.
- Extend `InstagramTab.tsx` (artist detail page) with the same badge treatment for the artist's *own* Instagram connection status — it already shows `@{status.username}`; add `<VerifiedSocialBadge>` next to it. This tab stays Instagram-only (photo sync is Instagram-specific); the new multi-platform `SocialLinksCard` pattern is a **separate** section on the artist detail page for the other four platforms (artist doesn't get photo sync for TikTok/Facebook/X/YouTube in this pass — only verification).
- Follow `InstagramTab.tsx`'s existing popup-window pattern exactly for every new "Connect" button (`window.open("about:blank", "_blank")` synchronously on click, before the awaited fetch, to avoid popup blockers) — this was already solved once in this codebase; don't re-solve it differently per platform.

### 9c. Public pages

`ArtistPortfolioPage.tsx` and `StudioPortfolioPage.tsx`: render `socialLinks` — platform icon + `@handle` + `<VerifiedSocialBadge>` when verified, plain outbound link (no badge) when not. Confirm brand-guideline-compliant icon usage for all five platforms with whoever owns frontend/brand review before shipping — flagged in the spec as not an engineering call, still true here.

### 9d. `?social=connected` / `?social=denied` / `?social=error` redirect handling

Mirror the existing `?instagram=connected` handling already in `ArtistDetailPage.tsx`/wherever the redirect lands — generalize or duplicate the pattern for the new generic callback's redirect target (`{BaseUrl}/artists/{id}?social=connected&platform=instagram` or the studio equivalent).

---

## Part 10 — Tests

- **Unit**: `SocialOAuthStateSigner` sign/validate round-trip incl. tamper rejection (mirror `InstagramStateSignerTests` if it exists); `RequestSocialVerificationCodeCommand`/`VerifySocialBioCodeCommand` handler tests (correct code passes, expired/wrong code rejected, unsupported platform 422s); `ExchangeInstagramCodeCommand`'s handler test extended to assert the new `SocialAccountLink` upsert happens alongside the existing `InstagramConnection` write — do not create a new test file that duplicates the existing one's setup, extend it.
- **Integration**: studio Instagram connect → callback → verified `SocialAccountLink`, including the suspended-studio check (write this test **from day one** — this exact class of bug (public data leaking for a suspended studio) has already been found and fixed twice for the artist Instagram path; don't let a third instance of it ship for the studio path). `GetSocialLinksQuery` tenant-isolation test (an owner cannot fetch another studio's links; issuer can).
- **Frontend**: `SocialLinksCard.test.tsx` mirroring `InstagramTab.test.tsx`'s structure; public-page tests asserting the badge only renders when `isVerified` is true and that an unconfigured platform's Connect button is hidden/disabled rather than clickable-but-broken.

---

## Part 11 — Help sync (CLAUDE.md rule #7 — not optional)

### 11a. `frontend/src/features/help/helpContent.ts`

Update the existing Studio Settings entry (currently: "...address, phone, Instagram, description...") to describe the new multi-platform section and what "Connect" vs. "Get a code" mean. Add/extend the artist-profile entry to cover the new social section and explain, in client-facing terms, what the "Verified" badge means (this is the whole point of the feature — the help copy should say something like *"A green check means we've directly confirmed this is the same account, not something anyone else could type in."*).

### 11b. `frontend/public/user-manual/index.html`

Mirror the same content, per the existing "keep both surfaces in sync" rule.

### 11c. Onboarding tours

Check `frontend/src/features/help/tours/ownerTour.ts` and `artistTour.ts` for whether either currently targets the Instagram tab/field — if neither does today (confirm before assuming), don't force a new tour step in just for this feature; match the bar the existing tours already set for what earns a step.

---

## Part 12 — Architecture doc updates

- **Feature Module Map** (`docs/claude/architecture.md`): extend the existing "Instagram:" entry (or add a new "Social Verification:" entry alongside it) describing `SocialAccountLink`, the five providers, and the config-gated rollout design — future readers need to know why Facebook/X/YouTube might be "built but inactive" without re-deriving it from code.
- **Decisions Log**: add an entry for this feature following the existing entries' style (what shipped, what was verified, what was explicitly deferred) — include the "config-gated, partial-rollout-safe" design decision and the "manual verification via official APIs, not scraping" decision, since both are exactly the kind of reasoning this log exists to preserve.
- **No new `IgnoreQueryFilters` table entry** — `SocialAccountLink` has no filter to bypass, same as `InstagramConnection`; that entity's "no global filter" note in `AppDbContext` is the right place for the explanation, not the `IgnoreQueryFilters()`-call table (which is for entities that *do* have a filter and explicitly skip it).

---

## Out of Scope — flagged explicitly, not silently dropped

- **Studio-level content sync** (pulling a studio's own Instagram/TikTok/etc. posts, the way `InstagramSyncJob` does for artists) — this feature is verification-only for studios. Decision 3 (discard the OAuth token after the identity check) explicitly forecloses this without a follow-up feature request.
- **Completing Meta App Review, X's paid API tier signup, or Google OAuth consent-screen re-verification** — these are external, human, non-code steps. This prompt leaves the corresponding platforms shipped-but-inactive (`IsConfigured == false`) until someone completes them and adds the resulting credentials via `ISecretsProvider`/Vault.
- **TikTok manual-code verification** — not built; no suitable official public-read API exists. TikTok verification is OAuth-only.
- **Re-verification (periodic recheck) for manual-code-verified links** — deliberately one-time per Decision 5; only OAuth-verified links get the recurring recheck job.
- **Platform icon/logo brand-guideline sign-off** — needs a human decision from whoever owns frontend/brand, not resolved by this prompt.
- **Removing `Studio.InstagramHandle` from the database** — column stays for a future release per the zero-downtime convention; only the public API response shape changes in this pass.

---

## Definition of Done

```
Backend
  [ ] SocialAccountLink entity, 3 enums, EF configuration, migration (incl. backfill) applied cleanly to a fresh DB
  [ ] ISocialOAuthProvider + factory; 5 provider classes registered; IsConfigured gate verified for at least one unconfigured platform (returns 409, not 500)
  [ ] ISocialBioChecker + 4 checker classes (Instagram, Facebook, YouTube, X) — TikTok checker present but IsSupported == false
  [ ] ISocialOAuthStateSigner — new, separate from IInstagramStateSigner; IInstagramStateSigner unmodified
  [ ] ExchangeInstagramCodeCommand extended (the one sanctioned edit to existing Instagram code); DisconnectInstagramCommand extended to match
  [ ] All Social endpoints: RequireAuthorization with the correct policy on every route, anonymous callback rate-limited
  [ ] PublicArtistResponse / PublicStudioResponse expose SocialLinks; every construction site updated, not just one
  [ ] dotnet build clean; dotnet test green (new + all existing Instagram tests, unmodified, still passing)

Frontend
  [ ] VerifiedSocialBadge component, visually matching ReviewSection's "Verified client" badge
  [ ] SocialLinksCard (owner settings, both artist and studio); InstagramTab extended with the badge
  [ ] Public pages render socialLinks correctly, verified and unverified states both covered
  [ ] pnpm tsc / lint / test / build all clean

Cross-cutting (CLAUDE.md rules #6/#7 — not optional)
  [ ] helpContent.ts updated for both the owner Studio Settings entry and the artist profile entry
  [ ] Standalone user manual updated to match
  [ ] Onboarding tours checked; only touched if the existing bar for a tour step is actually met
  [ ] architecture.md Feature Module Map + Decisions Log updated
  [ ] Benchmarked against the Industry-Standard Benchmark Set (architecture.md) — note explicitly that Instagram verification matches the benchmark set's own bar and that TikTok/Facebook/X/YouTube verification goes beyond it, per this feature's own scope decision
```

---

## Hard Rules Reminder

Tenant isolation on every new query/command (resolve `StudioId` from the target entity, never blindly from `ICurrentTenant`). `RequireAuthorization` on every endpoint except the one documented anonymous callback. Never log OAuth tokens, encrypted or not — log `tenant_id`/`user_id`/`request_id`/platform enum only. Every secret (all five platforms' client IDs/secrets/API keys) goes through `ISecretsProvider`/Vault — appsettings.json's empty string placeholders are the *shape*, never the real value, exactly like the existing `Instagram` section already demonstrates. No `Console.WriteLine`/`console.log`. No new ORM. No unprotected endpoint outside the one documented, rate-limited, signed-state anonymous callback.
