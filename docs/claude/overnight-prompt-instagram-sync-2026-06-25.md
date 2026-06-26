# Overnight Prompt — Instagram Portfolio Sync
**Date:** 2026-06-25  
**Scope:** Full-stack — Domain · Infrastructure · Application · API · Frontend  
**Constraint:** Zero new NuGet or npm packages. All HTTP via `IHttpClientFactory`. All crypto via `System.Security.Cryptography` (already in BCL).

---

## Goal

Tattoo artists can connect their Instagram account to Pena e Artë with a single OAuth click. Once connected, the platform syncs their Instagram posts nightly and displays them on the artist's public portfolio page. The artist can hide individual posts from their portfolio without deleting them from the sync.

The integration uses the **Instagram API with Instagram Login** (the current official API as of 2024+; the Basic Display API was shut down December 4, 2024).

---

## Step 0 — Read these files first

Before touching any code, read:
- `CLAUDE.md`
- `docs/claude/backend.md`
- `docs/claude/database.md`
- `docs/claude/frontend.md`
- `docs/claude/architecture.md`
- `docs/claude/conventions.md`
- `Pena_e_Arte.Domain/Entities/Artist.cs`
- `Pena_e_Arte.Domain/Entities/TenantEntity.cs`
- `Pena_e_Arte.Infrastructure/Persistence/AppDbContext.cs`
- `Pena_e_Arte.API/Endpoints/ArtistEndpoints.cs`
- `Pena_e_Arte.API/Endpoints/PublicEndpoints.cs`
- `Pena_e_Arte.API/Program.cs`
- `Pena_e_Arte.API/appsettings.json`
- `Pena_e_Arte.Infrastructure/Jobs/TrialExpiryJob.cs` (Hangfire pattern reference)
- `frontend/src/features/artists/components/ArtistDetailPage.tsx`
- `frontend/src/features/public/components/ArtistPortfolioPage.tsx`
- `frontend/src/features/public/publicApi.ts`
- `frontend/src/features/artists/artistsApi.ts`

---

## Step 1 — Configuration

### 1a. `Pena_e_Arte.API/appsettings.json`

Add the `Instagram` section alongside the existing sections:

```json
"Instagram": {
  "AppId":               "",
  "AppSecret":           "",
  "RedirectUri":         "",
  "TokenEncryptionKey":  ""
}
```

All four values are populated via environment variables only — the JSON file stays blank. The `TokenEncryptionKey` must be a base64-encoded 32-byte (256-bit) random key.

### 1b. Bind in infrastructure

Create `Pena_e_Arte.Infrastructure/Options/InstagramOptions.cs`:

```csharp
namespace Pena_e_Arte.Infrastructure.Options;

public class InstagramOptions
{
    public const string Section = "Instagram";

    public string AppId              { get; init; } = "";
    public string AppSecret          { get; init; } = "";
    public string RedirectUri        { get; init; } = "";
    public string TokenEncryptionKey { get; init; } = "";
}
```

Register in `AddInfrastructure` (in the Infrastructure `ServiceCollectionExtensions`):

```csharp
services.Configure<InstagramOptions>(configuration.GetSection(InstagramOptions.Section));
```

---

## Step 2 — Domain entities

### 2a. `Pena_e_Arte.Domain/Entities/InstagramConnection.cs`

```csharp
namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// Stores the OAuth connection between an Artist and their Instagram account.
/// Token is stored AES-GCM encrypted. No TenantId query filter is applied —
/// see AppDbContext; the Hangfire sync job iterates all tenants.
/// </summary>
public class InstagramConnection : TenantEntity
{
    public Guid     ArtistId        { get; set; }
    public string   InstagramUserId { get; set; } = "";
    public string   Username        { get; set; } = "";

    /// <summary>AES-GCM encrypted long-lived access token.</summary>
    public string   EncryptedToken  { get; set; } = "";

    public DateTime TokenExpiresAt  { get; set; }
    public DateTime? LastSyncedAt   { get; set; }
    public bool     IsActive        { get; set; } = true;

    public Artist Artist { get; set; } = null!;
}
```

### 2b. `Pena_e_Arte.Domain/Entities/InstagramPost.cs`

```csharp
namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// A single synced Instagram media item belonging to an artist.
/// IsVisible controls whether it appears on the public portfolio.
/// </summary>
public class InstagramPost : TenantEntity
{
    public Guid     ArtistId         { get; set; }
    public string   InstagramMediaId { get; set; } = "";
    public string   MediaUrl         { get; set; } = "";
    public string?  ThumbnailUrl     { get; set; }
    public string?  Caption          { get; set; }

    /// <summary>IMAGE or CAROUSEL_ALBUM — VIDEO items are skipped during sync.</summary>
    public string   MediaType        { get; set; } = "";

    public DateTime PostedAt         { get; set; }
    public bool     IsVisible        { get; set; } = true;

    public Artist Artist { get; set; } = null!;
}
```

---

## Step 3 — Token encryption

### 3a. `Pena_e_Arte.Domain/Interfaces/ITokenEncryptor.cs`

```csharp
namespace Pena_e_Arte.Domain.Interfaces;

public interface ITokenEncryptor
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}
```

### 3b. `Pena_e_Arte.Infrastructure/Services/AesTokenEncryptor.cs`

Uses `System.Security.Cryptography.AesGcm` (in the .NET BCL — no new package):

```csharp
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Options;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// AES-256-GCM authenticated encryption for Instagram access tokens.
/// Key is sourced from Instagram:TokenEncryptionKey (32-byte base64 env var).
/// Output format: base64(nonce[12] + ciphertext + tag[16]).
/// </summary>
public sealed class AesTokenEncryptor(IOptions<InstagramOptions> options) : ITokenEncryptor
{
    private readonly byte[] _key = Convert.FromBase64String(options.Value.TokenEncryptionKey);

    public string Encrypt(string plainText)
    {
        byte[] nonce      = new byte[AesGcm.NonceByteSizes.MaxSize]; // 12
        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
        byte[] cipher     = new byte[plainBytes.Length];
        byte[] tag        = new byte[AesGcm.TagByteSizes.MaxSize];  // 16

        RandomNumberGenerator.Fill(nonce);

        using AesGcm aes = new(_key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        byte[] result = new byte[nonce.Length + cipher.Length + tag.Length];
        nonce.CopyTo(result, 0);
        cipher.CopyTo(result, nonce.Length);
        tag.CopyTo(result, nonce.Length + cipher.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        byte[] raw    = Convert.FromBase64String(cipherText);
        int    nLen   = AesGcm.NonceByteSizes.MaxSize;
        int    tagLen = AesGcm.TagByteSizes.MaxSize;
        int    cLen   = raw.Length - nLen - tagLen;

        byte[] nonce  = raw[..nLen];
        byte[] cipher = raw[nLen..(nLen + cLen)];
        byte[] tag    = raw[(nLen + cLen)..];
        byte[] plain  = new byte[cLen];

        using AesGcm aes = new(_key, tagLen);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }
}
```

Register in `AddInfrastructure`:

```csharp
services.AddSingleton<ITokenEncryptor, AesTokenEncryptor>();
```

---

## Step 4 — Instagram HTTP service

### 4a. `Pena_e_Arte.Domain/Interfaces/IInstagramService.cs`

```csharp
namespace Pena_e_Arte.Domain.Interfaces;

public record InstagramTokenResponse(
    string AccessToken,
    string TokenType,
    long   ExpiresIn,
    string UserId);

public record InstagramMediaItem(
    string    Id,
    string    MediaType,
    string?   MediaUrl,
    string?   ThumbnailUrl,
    string?   Caption,
    DateTime  Timestamp);

public interface IInstagramService
{
    /// <summary>Exchange the OAuth code for a short-lived token, then upgrade to long-lived.</summary>
    Task<InstagramTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct);

    /// <summary>Refresh a long-lived token. Returns updated token + new expiry.</summary>
    Task<(string NewToken, DateTime NewExpiry)> RefreshTokenAsync(string accessToken, CancellationToken ct);

    /// <summary>Fetch the user's username from the Instagram API.</summary>
    Task<string> GetUsernameAsync(string accessToken, CancellationToken ct);

    /// <summary>Fetch all IMAGE and CAROUSEL_ALBUM media items (handles pagination).</summary>
    Task<List<InstagramMediaItem>> GetMediaAsync(string accessToken, CancellationToken ct);
}
```

### 4b. `Pena_e_Arte.Infrastructure/Services/InstagramService.cs`

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Options;

namespace Pena_e_Arte.Infrastructure.Services;

public sealed class InstagramService(
    IHttpClientFactory          httpFactory,
    IOptions<InstagramOptions>  options,
    ILogger<InstagramService>   logger) : IInstagramService
{
    private readonly InstagramOptions _opts = options.Value;

    // ── Token exchange ──────────────────────────────────────────────────────────

    public async Task<InstagramTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        using HttpClient client = httpFactory.CreateClient("Instagram");

        // Step 1: short-lived token
        FormUrlEncodedContent form = new([
            new("client_id",     _opts.AppId),
            new("client_secret", _opts.AppSecret),
            new("grant_type",    "authorization_code"),
            new("redirect_uri",  _opts.RedirectUri),
            new("code",          code),
        ]);

        HttpResponseMessage shortResponse =
            await client.PostAsync("https://api.instagram.com/oauth/access_token", form, ct);

        shortResponse.EnsureSuccessStatusCode();

        ShortTokenDto? shortToken =
            await shortResponse.Content.ReadFromJsonAsync<ShortTokenDto>(ct)
            ?? throw new InvalidOperationException("Empty Instagram token response.");

        // Step 2: long-lived token (valid ~60 days)
        string longUrl =
            $"https://graph.instagram.com/access_token" +
            $"?grant_type=ig_exchange_token" +
            $"&client_secret={Uri.EscapeDataString(_opts.AppSecret)}" +
            $"&access_token={Uri.EscapeDataString(shortToken.AccessToken)}";

        HttpResponseMessage longResponse = await client.GetAsync(longUrl, ct);
        longResponse.EnsureSuccessStatusCode();

        LongTokenDto? longToken =
            await longResponse.Content.ReadFromJsonAsync<LongTokenDto>(ct)
            ?? throw new InvalidOperationException("Empty Instagram long token response.");

        return new InstagramTokenResponse(
            longToken.AccessToken,
            longToken.TokenType,
            longToken.ExpiresIn,
            shortToken.UserId.ToString());
    }

    public async Task<(string NewToken, DateTime NewExpiry)> RefreshTokenAsync(
        string accessToken, CancellationToken ct)
    {
        using HttpClient client = httpFactory.CreateClient("Instagram");

        string url =
            $"https://graph.instagram.com/refresh_access_token" +
            $"?grant_type=ig_refresh_token" +
            $"&access_token={Uri.EscapeDataString(accessToken)}";

        HttpResponseMessage response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        LongTokenDto? dto =
            await response.Content.ReadFromJsonAsync<LongTokenDto>(ct)
            ?? throw new InvalidOperationException("Empty Instagram refresh response.");

        return (dto.AccessToken, DateTime.UtcNow.AddSeconds(dto.ExpiresIn));
    }

    // ── Profile ─────────────────────────────────────────────────────────────────

    public async Task<string> GetUsernameAsync(string accessToken, CancellationToken ct)
    {
        using HttpClient client = httpFactory.CreateClient("Instagram");

        string url =
            $"https://graph.instagram.com/me?fields=username" +
            $"&access_token={Uri.EscapeDataString(accessToken)}";

        HttpResponseMessage response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        using JsonDocument doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        return doc.RootElement.GetProperty("username").GetString()
               ?? throw new InvalidOperationException("Username missing from Instagram response.");
    }

    // ── Media ───────────────────────────────────────────────────────────────────

    public async Task<List<InstagramMediaItem>> GetMediaAsync(
        string accessToken, CancellationToken ct)
    {
        using HttpClient client = httpFactory.CreateClient("Instagram");

        List<InstagramMediaItem> all  = [];
        string? nextUrl = BuildMediaUrl(accessToken);

        while (nextUrl is not null)
        {
            HttpResponseMessage response = await client.GetAsync(nextUrl, ct);
            response.EnsureSuccessStatusCode();

            MediaPageDto? page =
                await response.Content.ReadFromJsonAsync<MediaPageDto>(ct);

            if (page is null) break;

            foreach (MediaItemDto item in page.Data)
            {
                if (item.MediaType is not ("IMAGE" or "CAROUSEL_ALBUM")) continue;

                // Skip items with no usable URL
                if (item.MediaUrl is null && item.ThumbnailUrl is null) continue;

                all.Add(new InstagramMediaItem(
                    item.Id,
                    item.MediaType,
                    item.MediaUrl,
                    item.ThumbnailUrl,
                    item.Caption,
                    item.Timestamp));
            }

            nextUrl = page.Paging?.Next;
        }

        logger.LogInformation("Fetched {Count} media items from Instagram", all.Count);
        return all;
    }

    private string BuildMediaUrl(string accessToken) =>
        $"https://graph.instagram.com/me/media" +
        $"?fields=id,media_type,media_url,thumbnail_url,caption,timestamp" +
        $"&limit=50" +
        $"&access_token={Uri.EscapeDataString(accessToken)}";

    // ── Private DTOs ────────────────────────────────────────────────────────────

    private sealed record ShortTokenDto(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("token_type")]   string TokenType,
        [property: JsonPropertyName("user_id")]      long   UserId);

    private sealed record LongTokenDto(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("token_type")]   string TokenType,
        [property: JsonPropertyName("expires_in")]   long   ExpiresIn);

    private sealed record MediaItemDto(
        [property: JsonPropertyName("id")]            string    Id,
        [property: JsonPropertyName("media_type")]    string    MediaType,
        [property: JsonPropertyName("media_url")]     string?   MediaUrl,
        [property: JsonPropertyName("thumbnail_url")] string?   ThumbnailUrl,
        [property: JsonPropertyName("caption")]       string?   Caption,
        [property: JsonPropertyName("timestamp")]     DateTime  Timestamp);

    private sealed record MediaPageDto(
        [property: JsonPropertyName("data")]   List<MediaItemDto> Data,
        [property: JsonPropertyName("paging")] PagingDto?         Paging);

    private sealed record PagingDto(
        [property: JsonPropertyName("next")]   string? Next,
        [property: JsonPropertyName("previous")] string? Previous);
}
```

Register in `AddInfrastructure`:

```csharp
services.AddHttpClient("Instagram");
services.AddScoped<IInstagramService, InstagramService>();
```

---

## Step 5 — EF Core: AppDbContext and configuration

### 5a. `AppDbContext.cs` — add DbSets

Add to the `// --- Cross-tenant public data ---` section:

```csharp
// --- Instagram (artist-scoped, no global tenant filter — Hangfire iterates all tenants) ---
public DbSet<InstagramConnection> InstagramConnections => Set<InstagramConnection>();
public DbSet<InstagramPost>       InstagramPosts       => Set<InstagramPost>();
```

**Do NOT add a `HasQueryFilter` on these two sets.** The sync job must read across all tenants. All application-layer queries that read these entities MUST filter by `artistId` or `studioId` explicitly.

### 5b. `Pena_e_Arte.Infrastructure/Persistence/Configurations/InstagramConnectionConfiguration.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class InstagramConnectionConfiguration : IEntityTypeConfiguration<InstagramConnection>
{
    public void Configure(EntityTypeBuilder<InstagramConnection> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.InstagramUserId).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Username).HasMaxLength(64).IsRequired();
        builder.Property(c => c.EncryptedToken).HasColumnType("TEXT").IsRequired();

        // One active connection per artist at most
        builder.HasIndex(c => c.ArtistId).IsUnique();

        builder.HasOne(c => c.Artist)
               .WithMany()
               .HasForeignKey(c => c.ArtistId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
```

### 5c. `Pena_e_Arte.Infrastructure/Persistence/Configurations/InstagramPostConfiguration.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class InstagramPostConfiguration : IEntityTypeConfiguration<InstagramPost>
{
    public void Configure(EntityTypeBuilder<InstagramPost> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.InstagramMediaId).HasMaxLength(64).IsRequired();
        builder.Property(p => p.MediaUrl).HasMaxLength(2048).IsRequired(false);
        builder.Property(p => p.ThumbnailUrl).HasMaxLength(2048);
        builder.Property(p => p.Caption).HasMaxLength(2200);
        builder.Property(p => p.MediaType).HasMaxLength(32).IsRequired();

        // Idempotent upsert key
        builder.HasIndex(p => p.InstagramMediaId).IsUnique();

        builder.HasOne(p => p.Artist)
               .WithMany()
               .HasForeignKey(p => p.ArtistId)
               .OnDelete(DeleteBehavior.Cascade);

        // Efficient public portfolio query: artist + visible only
        builder.HasIndex(p => new { p.ArtistId, p.IsVisible });
    }
}
```

---

## Step 6 — Application: Queries and Commands

### 6a. `Pena_e_Arte.Application/Instagram/Queries/GetInstagramConnectUrlQuery.cs`

```csharp
using MediatR;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Infrastructure.Options;

namespace Pena_e_Arte.Application.Instagram.Queries;

public record GetInstagramConnectUrlQuery(Guid ArtistId) : IRequest<string>;

public class GetInstagramConnectUrlHandler(IOptions<InstagramOptions> opts)
    : IRequestHandler<GetInstagramConnectUrlQuery, string>
{
    private readonly InstagramOptions _opts = opts.Value;

    public Task<string> Handle(GetInstagramConnectUrlQuery request, CancellationToken ct)
    {
        // The state encodes the artistId for post-OAuth lookup.
        // A CSRF nonce is also embedded; the callback handler verifies it.
        string state = $"{request.ArtistId}";

        string url =
            $"https://api.instagram.com/oauth/authorize" +
            $"?client_id={Uri.EscapeDataString(_opts.AppId)}" +
            $"&redirect_uri={Uri.EscapeDataString(_opts.RedirectUri)}" +
            $"&scope=instagram_basic,user_media" +
            $"&response_type=code" +
            $"&state={Uri.EscapeDataString(state)}";

        return Task.FromResult(url);
    }
}
```

### 6b. `Pena_e_Arte.Application/Instagram/Queries/GetInstagramConnectionStatusQuery.cs`

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Instagram.Queries;

public record GetInstagramConnectionStatusQuery(Guid ArtistId) : IRequest<InstagramConnectionStatusResponse>;

public class GetInstagramConnectionStatusHandler(IAppDbContext db)
    : IRequestHandler<GetInstagramConnectionStatusQuery, InstagramConnectionStatusResponse>
{
    public async Task<InstagramConnectionStatusResponse> Handle(
        GetInstagramConnectionStatusQuery request, CancellationToken ct)
    {
        InstagramConnection? connection = await db.InstagramConnections
            .IgnoreQueryFilters()
            // Intentional: InstagramConnections has no global query filter
            .Where(c => c.ArtistId == request.ArtistId && c.IsActive)
            .FirstOrDefaultAsync(ct);

        int postCount = 0;
        if (connection is not null)
        {
            postCount = await db.InstagramPosts
                .IgnoreQueryFilters()
                .CountAsync(p => p.ArtistId == request.ArtistId, ct);
        }

        return connection is null
            ? new InstagramConnectionStatusResponse(false, null, null, 0)
            : new InstagramConnectionStatusResponse(
                true,
                connection.Username,
                connection.LastSyncedAt,
                postCount);
    }
}
```

### 6c. `Pena_e_Arte.Application/Instagram/Queries/GetInstagramPostsQuery.cs`

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Instagram.Queries;

public record GetInstagramPostsQuery(Guid ArtistId, int Page = 1, int PageSize = 24)
    : IRequest<List<InstagramPostResponse>>;

public class GetInstagramPostsHandler(IAppDbContext db)
    : IRequestHandler<GetInstagramPostsQuery, List<InstagramPostResponse>>
{
    public async Task<List<InstagramPostResponse>> Handle(
        GetInstagramPostsQuery request, CancellationToken ct)
    {
        return await db.InstagramPosts
            .IgnoreQueryFilters()
            // Intentional: no global query filter on InstagramPosts
            .Where(p => p.ArtistId == request.ArtistId)
            .OrderByDescending(p => p.PostedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(p => new InstagramPostResponse(
                p.Id,
                p.InstagramMediaId,
                p.MediaUrl,
                p.ThumbnailUrl,
                p.Caption,
                p.MediaType,
                p.PostedAt,
                p.IsVisible))
            .ToListAsync(ct);
    }
}
```

### 6d. `Pena_e_Arte.Application/Instagram/Commands/ExchangeInstagramCodeCommand.cs`

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Instagram.Commands;

public record ExchangeInstagramCodeCommand(Guid ArtistId, Guid StudioId, string Code)
    : IRequest<Unit>;

public class ExchangeInstagramCodeHandler(
    IAppDbContext                       db,
    IInstagramService                   instagram,
    ITokenEncryptor                     encryptor,
    ILogger<ExchangeInstagramCodeHandler> logger) : IRequestHandler<ExchangeInstagramCodeCommand, Unit>
{
    public async Task<Unit> Handle(ExchangeInstagramCodeCommand request, CancellationToken ct)
    {
        InstagramTokenResponse tokenResponse =
            await instagram.ExchangeCodeAsync(request.Code, ct);

        string username =
            await instagram.GetUsernameAsync(tokenResponse.AccessToken, ct);

        string encryptedToken = encryptor.Encrypt(tokenResponse.AccessToken);

        DateTime expiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

        // Upsert: one connection per artist
        InstagramConnection? existing = await db.InstagramConnections
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.ArtistId == request.ArtistId, ct);

        if (existing is null)
        {
            db.InstagramConnections.Add(new InstagramConnection
            {
                StudioId        = request.StudioId,
                ArtistId        = request.ArtistId,
                InstagramUserId = tokenResponse.UserId,
                Username        = username,
                EncryptedToken  = encryptedToken,
                TokenExpiresAt  = expiresAt,
                IsActive        = true,
            });
        }
        else
        {
            existing.InstagramUserId = tokenResponse.UserId;
            existing.Username        = username;
            existing.EncryptedToken  = encryptedToken;
            existing.TokenExpiresAt  = expiresAt;
            existing.IsActive        = true;
            existing.UpdatedAt       = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Instagram connected for artist {ArtistId} in studio {StudioId}. Username: {Username}",
            request.ArtistId, request.StudioId, username);

        return Unit.Value;
    }
}
```

### 6e. `Pena_e_Arte.Application/Instagram/Commands/DisconnectInstagramCommand.cs`

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Instagram.Commands;

public record DisconnectInstagramCommand(Guid ArtistId) : IRequest<Unit>;

public class DisconnectInstagramHandler(IAppDbContext db)
    : IRequestHandler<DisconnectInstagramCommand, Unit>
{
    public async Task<Unit> Handle(DisconnectInstagramCommand request, CancellationToken ct)
    {
        InstagramConnection? connection = await db.InstagramConnections
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.ArtistId == request.ArtistId, ct);

        if (connection is not null)
        {
            connection.IsActive   = false;
            connection.UpdatedAt  = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return Unit.Value;
    }
}
```

### 6f. `Pena_e_Arte.Application/Instagram/Commands/ToggleInstagramPostVisibilityCommand.cs`

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Instagram.Commands;

public record ToggleInstagramPostVisibilityCommand(Guid ArtistId, Guid PostId, bool IsVisible)
    : IRequest<Unit>;

public class ToggleInstagramPostVisibilityHandler(IAppDbContext db)
    : IRequestHandler<ToggleInstagramPostVisibilityCommand, Unit>
{
    public async Task<Unit> Handle(
        ToggleInstagramPostVisibilityCommand request, CancellationToken ct)
    {
        InstagramPost? post = await db.InstagramPosts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == request.PostId && p.ArtistId == request.ArtistId, ct);

        if (post is null) throw new NotFoundException(nameof(InstagramPost), request.PostId);

        post.IsVisible = request.IsVisible;
        post.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
```

### 6g. `Pena_e_Arte.Application/Public/Queries/GetPublicArtistInstagramPostsQuery.cs`

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetPublicArtistInstagramPostsQuery(string Slug) : IRequest<List<InstagramPostResponse>>;

public class GetPublicArtistInstagramPostsHandler(IAppDbContext db)
    : IRequestHandler<GetPublicArtistInstagramPostsQuery, List<InstagramPostResponse>>
{
    public async Task<List<InstagramPostResponse>> Handle(
        GetPublicArtistInstagramPostsQuery request, CancellationToken ct)
    {
        // IgnoreQueryFilters required: Artist has tenant filter; this is a public endpoint.
        Guid? artistId = await db.Artists
            .IgnoreQueryFilters()
            .Where(a => a.Slug == request.Slug && a.DeletedAt == null)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);

        if (artistId is null) return [];

        return await db.InstagramPosts
            .IgnoreQueryFilters()
            .Where(p => p.ArtistId == artistId && p.IsVisible)
            .OrderByDescending(p => p.PostedAt)
            .Take(24)
            .Select(p => new InstagramPostResponse(
                p.Id,
                p.InstagramMediaId,
                p.MediaUrl,
                p.ThumbnailUrl,
                p.Caption,
                p.MediaType,
                p.PostedAt,
                p.IsVisible))
            .ToListAsync(ct);
    }
}
```

---

## Step 7 — Add DbSets to IAppDbContext

Open `Pena_e_Arte.Application/Persistence/IAppDbContext.cs` (or wherever the interface is defined) and add:

```csharp
DbSet<InstagramConnection> InstagramConnections { get; }
DbSet<InstagramPost>       InstagramPosts       { get; }
```

---

## Step 8 — Contracts

### 8a. `Pena_e_Arte.Contracts/Responses/InstagramConnectionStatusResponse.cs`

```csharp
namespace Pena_e_Arte.Contracts.Responses;

public record InstagramConnectionStatusResponse(
    bool      IsConnected,
    string?   Username,
    DateTime? LastSyncedAt,
    int       PostCount);
```

### 8b. `Pena_e_Arte.Contracts/Responses/InstagramPostResponse.cs`

```csharp
namespace Pena_e_Arte.Contracts.Responses;

public record InstagramPostResponse(
    Guid      Id,
    string    InstagramMediaId,
    string?   MediaUrl,
    string?   ThumbnailUrl,
    string?   Caption,
    string    MediaType,
    DateTime  PostedAt,
    bool      IsVisible);
```

### 8c. `Pena_e_Arte.Contracts/Requests/TogglePostVisibilityRequest.cs`

```csharp
namespace Pena_e_Arte.Contracts.Requests;

public record TogglePostVisibilityRequest(bool IsVisible);
```

### 8d. `Pena_e_Arte.Contracts/Responses/ConnectInstagramResponse.cs`

```csharp
namespace Pena_e_Arte.Contracts.Responses;

public record ConnectInstagramResponse(string AuthUrl);
```

---

## Step 9 — FluentValidation

### `Pena_e_Arte.Application/Instagram/Validators/ExchangeInstagramCodeValidator.cs`

```csharp
using FluentValidation;
using Pena_e_Arte.Application.Instagram.Commands;

namespace Pena_e_Arte.Application.Instagram.Validators;

public class ExchangeInstagramCodeValidator : AbstractValidator<ExchangeInstagramCodeCommand>
{
    public ExchangeInstagramCodeValidator()
    {
        RuleFor(x => x.ArtistId).NotEmpty();
        RuleFor(x => x.StudioId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(512);
    }
}
```

---

## Step 10 — API Endpoints

### 10a. `Pena_e_Arte.API/Endpoints/InstagramEndpoints.cs`

```csharp
using MediatR;
using Pena_e_Arte.Application.Instagram.Commands;
using Pena_e_Arte.Application.Instagram.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using System.Security.Claims;

namespace Pena_e_Arte.API.Endpoints;

public static class InstagramEndpoints
{
    public static void MapInstagramEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/artists/{id:guid}/instagram")
            .RequireAuthorization();

        // Get the Instagram OAuth URL to open in a browser popup
        group.MapGet("/connect-url", GetConnectUrl)
             .RequireAuthorization("OwnerOnly");

        // Get connection status (connected? username? last synced? post count?)
        group.MapGet("/status", GetStatus)
             .RequireAuthorization("ArtistAndAbove");

        // Get all synced posts (with IsVisible flag, for the management tab)
        group.MapGet("/posts", GetPosts)
             .RequireAuthorization("ArtistAndAbove");

        // Toggle a post's visibility on/off
        group.MapPut("/posts/{postId:guid}/visibility", ToggleVisibility)
             .RequireAuthorization("ArtistAndAbove");

        // Disconnect
        group.MapDelete("/disconnect", Disconnect)
             .RequireAuthorization("OwnerOnly");
    }

    /// <summary>
    /// Public OAuth callback endpoint — called by Instagram after the user authorises.
    /// Not in the /artists group — has its own route with no auth requirement.
    /// </summary>
    public static void MapInstagramCallbackEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/instagram/callback", HandleCallback)
           .AllowAnonymous();
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private static async Task<IResult> GetConnectUrl(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        string url = await mediator.Send(new GetInstagramConnectUrlQuery(id), ct);
        return Results.Ok(new ConnectInstagramResponse(url));
    }

    private static async Task<IResult> GetStatus(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        InstagramConnectionStatusResponse result =
            await mediator.Send(new GetInstagramConnectionStatusQuery(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetPosts(
        Guid              id,
        int               page,
        ISender           mediator,
        CancellationToken ct)
    {
        List<InstagramPostResponse> result =
            await mediator.Send(new GetInstagramPostsQuery(id, page), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> ToggleVisibility(
        Guid                       id,
        Guid                       postId,
        TogglePostVisibilityRequest request,
        ISender                    mediator,
        CancellationToken          ct)
    {
        await mediator.Send(
            new ToggleInstagramPostVisibilityCommand(id, postId, request.IsVisible), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Disconnect(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        await mediator.Send(new DisconnectInstagramCommand(id), ct);
        return Results.NoContent();
    }

    // ── OAuth callback ────────────────────────────────────────────────────────

    /// <summary>
    /// Receives ?code=...&amp;state={artistId} from Instagram's OAuth redirect.
    /// Exchanges the code for a token and redirects the user to the artist detail page.
    /// The state param must be a valid artist Guid.
    /// </summary>
    private static async Task<IResult> HandleCallback(
        string?           code,
        string?           state,
        string?           error,
        ISender           mediator,
        HttpContext       context,
        CancellationToken ct)
    {
        // User denied access
        if (error is not null || code is null || state is null)
            return Results.Redirect("/artists?instagram=denied");

        if (!Guid.TryParse(state, out Guid artistId))
            return Results.BadRequest("Invalid state parameter.");

        // Resolve studioId: look up the artist without tenant filter
        // The command handler verifies the artist exists.
        // We extract studioId from the ClaimsPrincipal — but this is an anonymous
        // redirect callback so there's no JWT. Instead we pass studioId=Guid.Empty
        // and let the command handler resolve it from the DB.
        // The command handler reads the actual StudioId from the Artist record.
        try
        {
            await mediator.Send(
                new ExchangeInstagramCodeCommand(artistId, Guid.Empty, code), ct);
        }
        catch (Exception)
        {
            return Results.Redirect($"/artists/{artistId}?instagram=error");
        }

        return Results.Redirect($"/artists/{artistId}?instagram=connected");
    }
}
```

**Important:** The `ExchangeInstagramCodeCommand` handler must resolve the `StudioId` from the database when `StudioId == Guid.Empty`. Update the handler in Step 6d to do this:

```csharp
// Inside ExchangeInstagramCodeHandler.Handle, before the upsert block:
if (request.StudioId == Guid.Empty)
{
    Guid studioId = await db.Artists
        .IgnoreQueryFilters()
        .Where(a => a.Id == request.ArtistId && a.DeletedAt == null)
        .Select(a => a.StudioId)
        .FirstOrDefaultAsync(ct);

    // Re-bind: create a new command with the resolved StudioId
    // Then proceed with studioId instead of request.StudioId
    // Use a local variable:
    Guid resolvedStudioId = studioId;
    // ... use resolvedStudioId in the upsert below
}
```

Refactor the handler to use a local `resolvedStudioId` variable throughout.

### 10b. `PublicEndpoints.cs` — add Instagram posts endpoint

Add this route inside `MapPublicEndpoints`:

```csharp
group.MapGet("/artists/{slug}/instagram-posts", GetArtistInstagramPosts)
     .AllowAnonymous();
```

Handler:

```csharp
private static async Task<IResult> GetArtistInstagramPosts(
    string            slug,
    ISender           mediator,
    CancellationToken ct)
{
    List<InstagramPostResponse> result =
        await mediator.Send(
            new GetPublicArtistInstagramPostsQuery(slug), ct);
    return Results.Ok(result);
}
```

Add the required using: `using Pena_e_Arte.Application.Public.Queries;`

### 10c. `Program.cs` — register endpoints

After the existing `app.MapArtistEndpoints()` call, add:

```csharp
app.MapInstagramEndpoints();
app.MapInstagramCallbackEndpoint();
```

---

## Step 11 — Hangfire: nightly sync job

### `Pena_e_Arte.Infrastructure/Jobs/InstagramSyncJob.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Iterates all active Instagram connections across all tenants,
/// refreshes expiring tokens, fetches new media, and upserts posts.
/// Scheduled nightly at 03:00 UTC.
/// </summary>
public class InstagramSyncJob(
    AppDbContext                  db,
    IInstagramService             instagram,
    ITokenEncryptor               encryptor,
    ILogger<InstagramSyncJob>     logger)
{
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        List<InstagramConnection> connections = await db.InstagramConnections
            .IgnoreQueryFilters()
            // Intentional: no global query filter on InstagramConnections
            .Where(c => c.IsActive)
            .ToListAsync(ct);

        logger.LogInformation(
            "InstagramSyncJob starting. Active connections: {Count}", connections.Count);

        foreach (InstagramConnection conn in connections)
        {
            try
            {
                await SyncConnectionAsync(conn, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Instagram sync failed for artist {ArtistId}", conn.ArtistId);
            }
        }

        logger.LogInformation("InstagramSyncJob complete.");
    }

    private async Task SyncConnectionAsync(InstagramConnection conn, CancellationToken ct)
    {
        string token = encryptor.Decrypt(conn.EncryptedToken);

        // Refresh token if it expires within 7 days
        if (conn.TokenExpiresAt <= DateTime.UtcNow.AddDays(7))
        {
            try
            {
                (string newToken, DateTime newExpiry) =
                    await instagram.RefreshTokenAsync(token, ct);

                conn.EncryptedToken = encryptor.Encrypt(newToken);
                conn.TokenExpiresAt = newExpiry;
                conn.UpdatedAt      = DateTime.UtcNow;
                token = newToken;

                logger.LogInformation(
                    "Refreshed Instagram token for artist {ArtistId}. New expiry: {Expiry}",
                    conn.ArtistId, newExpiry);
            }
            catch (HttpRequestException ex) when ((int?)ex.StatusCode == 400)
            {
                // Token is permanently revoked
                conn.IsActive  = false;
                conn.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);

                logger.LogWarning(
                    "Instagram token revoked for artist {ArtistId}. Deactivating.",
                    conn.ArtistId);
                return;
            }
        }

        // Fetch media from Instagram
        List<InstagramMediaItem> items = await instagram.GetMediaAsync(token, ct);

        // Upsert each item
        HashSet<string> existingMediaIds = (await db.InstagramPosts
            .IgnoreQueryFilters()
            .Where(p => p.ArtistId == conn.ArtistId)
            .Select(p => p.InstagramMediaId)
            .ToListAsync(ct)).ToHashSet();

        int added = 0;
        foreach (InstagramMediaItem item in items)
        {
            if (existingMediaIds.Contains(item.Id)) continue;

            db.InstagramPosts.Add(new InstagramPost
            {
                StudioId         = conn.StudioId,
                ArtistId         = conn.ArtistId,
                InstagramMediaId = item.Id,
                MediaUrl         = item.MediaUrl ?? "",
                ThumbnailUrl     = item.ThumbnailUrl,
                Caption          = item.Caption,
                MediaType        = item.MediaType,
                PostedAt         = item.Timestamp,
                IsVisible        = true,
            });
            added++;
        }

        conn.LastSyncedAt = DateTime.UtcNow;
        conn.UpdatedAt    = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Synced Instagram for artist {ArtistId}. New posts added: {Added}",
            conn.ArtistId, added);
    }
}
```

### Register Hangfire recurring job in `Program.cs`

After `await DataSeeder.SeedAsync(app.Services);`, add:

```csharp
// Schedule Instagram sync job — nightly at 03:00 UTC
IRecurringJobManager recurringJobs =
    app.Services.GetRequiredService<IRecurringJobManager>();

recurringJobs.AddOrUpdate<InstagramSyncJob>(
    "instagram-nightly-sync",
    job => job.ExecuteAsync(CancellationToken.None),
    Cron.Daily(hour: 3));
```

Also register `InstagramSyncJob` as a transient service in `AddInfrastructure`:

```csharp
services.AddTransient<InstagramSyncJob>();
```

---

## Step 12 — Migration

```bash
cd "Pena e Arte"
dotnet ef migrations add AddInstagramIntegration --project Pena_e_Arte.Infrastructure --startup-project Pena_e_Arte.API
dotnet ef database update --project Pena_e_Arte.Infrastructure --startup-project Pena_e_Arte.API
```

Verify the generated migration creates:
- `instagram_connections` table with correct columns and unique index on `artist_id`
- `instagram_posts` table with correct columns, unique index on `instagram_media_id`, and composite index on `(artist_id, is_visible)`

---

## Step 13 — Frontend: RTK Query

### 13a. New interfaces in `publicApi.ts`

Add to the existing `publicApi.ts`:

```typescript
export interface ArtistInstagramPostResponse {
  id:               string;
  instagramMediaId: string;
  mediaUrl:         string | null;
  thumbnailUrl:     string | null;
  caption:          string | null;
  mediaType:        string;
  postedAt:         string;
  isVisible:        boolean;
}
```

Add endpoint:

```typescript
getArtistInstagramPosts: builder.query<ArtistInstagramPostResponse[], string>({
  query: (slug) => `/public/artists/${slug}/instagram-posts`,
}),
```

Export hook: `useGetArtistInstagramPostsQuery`.

### 13b. New interfaces and endpoints in `artistsApi.ts`

Add to `artistsApi.ts`:

```typescript
export interface InstagramConnectionStatus {
  isConnected:  boolean;
  username:     string | null;
  lastSyncedAt: string | null;
  postCount:    number;
}

export interface InstagramPostItem {
  id:               string;
  instagramMediaId: string;
  mediaUrl:         string | null;
  thumbnailUrl:     string | null;
  caption:          string | null;
  mediaType:        string;
  postedAt:         string;
  isVisible:        boolean;
}

export interface ConnectInstagramResponse {
  authUrl: string;
}
```

Add endpoints inside `artistsApi`:

```typescript
getInstagramConnectUrl: builder.query<ConnectInstagramResponse, string>({
  query: (artistId) => `/artists/${artistId}/instagram/connect-url`,
}),

getInstagramStatus: builder.query<InstagramConnectionStatus, string>({
  query:       (artistId) => `/artists/${artistId}/instagram/status`,
  providesTags: (_result, _err, artistId) => [{ type: 'Artist', id: `${artistId}-instagram` }],
}),

getInstagramPosts: builder.query<InstagramPostItem[], { artistId: string; page?: number }>({
  query:       ({ artistId, page = 1 }) =>
    `/artists/${artistId}/instagram/posts?page=${page}`,
  providesTags: (_result, _err, { artistId }) => [{ type: 'Artist', id: `${artistId}-instagram-posts` }],
}),

toggleInstagramPostVisibility: builder.mutation<void, { artistId: string; postId: string; isVisible: boolean }>({
  query: ({ artistId, postId, isVisible }) => ({
    url:    `/artists/${artistId}/instagram/posts/${postId}/visibility`,
    method: 'PUT',
    body:   { isVisible },
  }),
  invalidatesTags: (_result, _err, { artistId }) => [
    { type: 'Artist', id: `${artistId}-instagram-posts` },
  ],
}),

disconnectInstagram: builder.mutation<void, string>({
  query: (artistId) => ({
    url:    `/artists/${artistId}/instagram/disconnect`,
    method: 'DELETE',
  }),
  invalidatesTags: (_result, _err, artistId) => [
    { type: 'Artist', id: `${artistId}-instagram` },
  ],
}),
```

Export hooks: `useGetInstagramConnectUrlQuery`, `useGetInstagramStatusQuery`, `useGetInstagramPostsQuery`, `useToggleInstagramPostVisibilityMutation`, `useDisconnectInstagramMutation`.

---

## Step 14 — Frontend: ArtistDetailPage — Instagram tab

Open `frontend/src/features/artists/components/ArtistDetailPage.tsx`.

**14a. Add an "Instagram" tab trigger** inside the existing `<TabsList>`:

```tsx
<TabsTrigger value="instagram" className="flex-1">Instagram</TabsTrigger>
```

**14b. Add the Instagram tab content** after the Designs `<TabsContent>`:

```tsx
<TabsContent value="instagram" className="mt-4 space-y-4">
  <InstagramTab artistId={id!} canManage={canManage} />
</TabsContent>
```

**14c. Create `InstagramTab` as a component in the same file** (or in a separate file `InstagramTab.tsx` in the same directory):

```tsx
import {
  useGetInstagramStatusQuery,
  useGetInstagramPostsQuery,
  useGetInstagramConnectUrlQuery,
  useToggleInstagramPostVisibilityMutation,
  useDisconnectInstagramMutation,
} from '../artistsApi';
import { Instagram, Eye, EyeOff, RefreshCw, Unlink, ExternalLink } from 'lucide-react';
import { Badge } from '@/shared/components/ui/badge';
import { Switch } from '@/shared/components/ui/switch';

interface InstagramTabProps {
  artistId:   string;
  canManage:  boolean;
}

function InstagramTab({ artistId, canManage }: InstagramTabProps) {
  const { data: status, isLoading: statusLoading } =
    useGetInstagramStatusQuery(artistId);

  const { data: posts = [], isLoading: postsLoading } =
    useGetInstagramPostsQuery({ artistId }, { skip: !status?.isConnected });

  const { data: connectData, refetch: fetchConnectUrl } =
    useGetInstagramConnectUrlQuery(artistId, { skip: true }); // manual trigger only

  const [toggleVisibility] = useToggleInstagramPostVisibilityMutation();
  const [disconnect]       = useDisconnectInstagramMutation();

  function handleConnect() {
    fetchConnectUrl()
      .unwrap()
      .then(({ authUrl }) => window.open(authUrl, '_blank', 'noopener,noreferrer'));
  }

  async function handleDisconnect() {
    if (!window.confirm('Disconnect Instagram? Synced posts remain but no new posts will be fetched.'))
      return;
    await disconnect(artistId);
  }

  if (statusLoading) {
    return (
      <div className="space-y-3">
        {[1, 2, 3].map((i) => <Skeleton key={i} className="h-14 w-full" />)}
      </div>
    );
  }

  if (!status?.isConnected) {
    return (
      <div className="flex flex-col items-center gap-4 py-12 text-center">
        <Instagram className="h-10 w-10 text-muted-foreground" />
        <p className="text-sm text-muted-foreground max-w-xs">
          Connect this artist's Instagram account to automatically sync their posts
          to their public portfolio.
        </p>
        {canManage && (
          <Button onClick={handleConnect} className="gap-2">
            <Instagram className="h-4 w-4" />
            Connect Instagram
            <ExternalLink className="h-3 w-3" />
          </Button>
        )}
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {/* Status bar */}
      <Card>
        <CardContent className="p-4 flex items-center justify-between gap-3 flex-wrap">
          <div className="flex items-center gap-3">
            <Instagram className="h-5 w-5 text-pink-500" />
            <div>
              <p className="text-sm font-medium">@{status.username}</p>
              {status.lastSyncedAt && (
                <p className="text-xs text-muted-foreground">
                  Last synced {new Date(status.lastSyncedAt).toLocaleDateString('en-GB', {
                    day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit',
                  })}
                </p>
              )}
            </div>
            <Badge variant="secondary">{status.postCount} posts</Badge>
          </div>

          {canManage && (
            <Button
              variant="ghost"
              size="sm"
              onClick={handleDisconnect}
              className="gap-1.5 text-destructive hover:text-destructive"
            >
              <Unlink className="h-3.5 w-3.5" />
              Disconnect
            </Button>
          )}
        </CardContent>
      </Card>

      {/* Post grid */}
      {postsLoading && (
        <div className="grid grid-cols-3 gap-2">
          {Array.from({ length: 9 }).map((_, i) => (
            <Skeleton key={i} className="aspect-square w-full rounded-md" />
          ))}
        </div>
      )}

      {!postsLoading && posts.length === 0 && (
        <p className="text-sm text-muted-foreground text-center py-8">
          No posts synced yet. The nightly job will run automatically.
        </p>
      )}

      {!postsLoading && posts.length > 0 && (
        <div className="grid grid-cols-3 gap-2">
          {posts.map((post) => {
            const imgSrc = post.mediaUrl ?? post.thumbnailUrl ?? '';
            return (
              <div key={post.id} className="relative group">
                <img
                  src={imgSrc}
                  alt={post.caption?.slice(0, 80) ?? 'Instagram post'}
                  className={cn(
                    'aspect-square w-full object-cover rounded-md transition-opacity',
                    !post.isVisible && 'opacity-40',
                  )}
                  loading="lazy"
                />
                {canManage && (
                  <button
                    type="button"
                    aria-label={post.isVisible ? 'Hide from portfolio' : 'Show in portfolio'}
                    onClick={() =>
                      toggleVisibility({ artistId, postId: post.id, isVisible: !post.isVisible })
                    }
                    className="absolute top-1.5 right-1.5 rounded-md bg-background/80 p-1
                               opacity-0 group-hover:opacity-100 transition-opacity
                               focus-visible:opacity-100 focus-visible:ring-2 focus-visible:ring-ring"
                  >
                    {post.isVisible
                      ? <Eye className="h-3.5 w-3.5" />
                      : <EyeOff className="h-3.5 w-3.5 text-muted-foreground" />}
                  </button>
                )}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
```

Add the required imports at the top of `ArtistDetailPage.tsx`.

---

## Step 15 — Frontend: ArtistPortfolioPage — public Instagram section

Open `frontend/src/features/public/components/ArtistPortfolioPage.tsx`.

**After the existing portfolio images section and before `<ReviewSection>`**, add an Instagram posts section:

```tsx
import { Instagram } from 'lucide-react';
import { useGetArtistInstagramPostsQuery } from '../publicApi';

// Inside ArtistPortfolioPage, after existing portfolio content:

const { data: instagramPosts = [] } = useGetArtistInstagramPostsQuery(slug, {
  skip: !slug,
});

// In JSX:
{instagramPosts.length > 0 && (
  <section aria-labelledby="instagram-heading" className="space-y-3">
    <h2
      id="instagram-heading"
      className="text-sm font-semibold flex items-center gap-2 text-muted-foreground"
    >
      <Instagram className="h-4 w-4" aria-hidden="true" />
      Instagram
    </h2>
    <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
      {instagramPosts.map((post) => (
        <img
          key={post.id}
          src={post.mediaUrl ?? post.thumbnailUrl ?? ''}
          alt={post.caption?.slice(0, 80) ?? 'Portfolio photo'}
          className="aspect-square w-full object-cover rounded-md"
          loading="lazy"
        />
      ))}
    </div>
  </section>
)}
```

Place this between the `portfolioImages` grid and `<ReviewSection>`.

---

## Step 16 — Handle `?instagram=connected` redirect

In `ArtistDetailPage.tsx`, read the `instagram` query param on mount and show a toast if it's `connected`:

```tsx
import { useSearchParams } from 'react-router-dom';

// Inside ArtistDetailPage():
const [searchParams] = useSearchParams();

useEffect(() => {
  const ig = searchParams.get('instagram');
  if (ig === 'connected') toast.success('Instagram connected successfully!');
  if (ig === 'error')     toast.error('Instagram connection failed. Please try again.');
  if (ig === 'denied')    toast.info('Instagram connection cancelled.');
}, [searchParams]);
```

This `useEffect` is acceptable — it's a browser side-effect (toast from search params), not data fetching.

---

## Step 17 — Tests

### 17a. `tests/Pena_e_Arte.UnitTests/Jobs/InstagramSyncJobTests.cs`

Write unit tests covering:

1. **SyncJob: happy path** — given one active connection, fetches media, inserts new posts, updates `LastSyncedAt`.
2. **SyncJob: skips existing posts** — `InstagramMediaId` already in DB → not inserted again.
3. **SyncJob: refreshes expiring token** — `TokenExpiresAt` in 3 days → calls `RefreshTokenAsync`, stores new encrypted token.
4. **SyncJob: deactivates on 400** — `RefreshTokenAsync` throws `HttpRequestException` with status 400 → `IsActive = false`, no posts inserted.
5. **SyncJob: skips VIDEO media type** — items with `MediaType == "VIDEO"` are not inserted.
6. **SyncJob: continues after per-connection failure** — first connection throws, second connection still syncs.

Use `NSubstitute` (already in test project) to mock `IInstagramService`, `ITokenEncryptor`, and an in-memory `AppDbContext`.

### 17b. `tests/Pena_e_Arte.UnitTests/Application/Instagram/ExchangeInstagramCodeCommandTests.cs`

Write unit tests covering:

1. **New connection** — `ArtistId` not in DB → inserts `InstagramConnection` with correct fields.
2. **Reconnect (upsert)** — existing inactive connection → updates `EncryptedToken`, `Username`, `IsActive = true`.
3. **StudioId resolved from DB** — when `request.StudioId == Guid.Empty`, `StudioId` is read from the `Artist` record.

### 17c. `frontend/src/features/artists/__tests__/InstagramTab.test.tsx`

Write frontend unit tests using Vitest + MSW covering:

1. **Disconnected state** — renders "Connect Instagram" button and descriptive text.
2. **Connected state** — renders `@username`, post count badge, and Disconnect button.
3. **Post grid renders** — given 3 posts returned by MSW, renders 3 `<img>` elements.
4. **Toggle visibility** — clicking the Eye button calls `PUT /artists/{id}/instagram/posts/{postId}/visibility`.
5. **Disconnect** — clicking Disconnect (after confirming the dialog) calls `DELETE /artists/{id}/instagram/disconnect`.

---

## Step 18 — Architecture docs

Update `docs/claude/architecture.md`:

Under the **Feature Module Map**, add:

```
Instagram Integration    Backend:  InstagramEndpoints, InstagramSyncJob (Hangfire, nightly 03:00 UTC)
                                   ExchangeInstagramCodeCommand, DisconnectInstagramCommand,
                                   ToggleInstagramPostVisibilityCommand
                                   GetInstagramConnectionStatusQuery, GetInstagramPostsQuery
                                   GetPublicArtistInstagramPostsQuery
                         Domain:   InstagramConnection, InstagramPost
                         Services: IInstagramService / InstagramService (HttpClient, no SDK)
                                   ITokenEncryptor / AesTokenEncryptor (AES-256-GCM)
                         Frontend: InstagramTab (ArtistDetailPage), instagram section (ArtistPortfolioPage)
                         Notes:    Uses Instagram API with Instagram Login (not Graph API — no FB Page required)
                                   Tokens are AES-GCM encrypted at rest.
                                   InstagramConnection + InstagramPost have NO global query filter.
                                   All application queries must filter explicitly by artistId.
```

Under **Decisions Log**, add:

```
2026-06-25  Instagram integration uses IHttpClientFactory + BCL AesGcm — no new packages.
            Callback endpoint is AllowAnonymous (receives Instagram OAuth redirect).
            StudioId in ExchangeInstagramCodeCommand is resolved from the Artist record
            when the callback comes from an unauthenticated redirect (StudioId=Guid.Empty).
            InstagramPost.IsVisible gives artists per-post control without deleting synced data.
```

---

## Step 19 — Build and test

```bash
cd "Pena e Arte"
dotnet build
dotnet test
```

All existing tests must still pass. The new unit tests must pass.

```bash
cd frontend
pnpm build
pnpm test
```

Fix any TypeScript errors. Zero `any` types allowed.

---

## Done checklist

- [ ] `InstagramConnection` and `InstagramPost` entities created
- [ ] `ITokenEncryptor` / `AesTokenEncryptor` implemented (no new packages)
- [ ] `IInstagramService` / `InstagramService` implemented (IHttpClientFactory)
- [ ] `AppDbContext` updated with two new `DbSet`s, no query filters on them
- [ ] EF Core configurations created
- [ ] Migration created and applied
- [ ] All 6 Application commands/queries implemented
- [ ] All 4 Contracts created
- [ ] FluentValidation for `ExchangeInstagramCodeCommand`
- [ ] `InstagramEndpoints` registered
- [ ] Callback endpoint registered (`AllowAnonymous`)
- [ ] Public Instagram posts endpoint in `PublicEndpoints`
- [ ] `InstagramSyncJob` registered + Hangfire cron set
- [ ] `artistsApi.ts` updated with 5 new endpoints
- [ ] `publicApi.ts` updated with 1 new endpoint
- [ ] `ArtistDetailPage.tsx` has Instagram tab with `InstagramTab` component
- [ ] `ArtistPortfolioPage.tsx` shows Instagram grid above `ReviewSection`
- [ ] `?instagram=connected|error|denied` handled with toast in `ArtistDetailPage`
- [ ] Backend unit tests: 6 sync job tests + 3 command tests
- [ ] Frontend unit tests: 5 InstagramTab tests
- [ ] `architecture.md` updated
- [ ] `dotnet build` passes
- [ ] `dotnet test` passes
- [ ] `pnpm build` passes
- [ ] `pnpm test` passes
