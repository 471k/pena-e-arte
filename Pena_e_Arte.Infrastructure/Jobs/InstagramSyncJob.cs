using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Iterates all active Instagram connections across all tenants, refreshes
/// expiring tokens, fetches new media, and upserts posts. Scheduled nightly
/// at 03:00 UTC. Reads InstagramConnections/InstagramPosts without a tenant
/// filter by design — see AppDbContext.
/// </summary>
public class InstagramSyncJob(
    IAppDbContext             db,
    IInstagramService         instagram,
    ITokenEncryptor           encryptor,
    ILogger<InstagramSyncJob> logger)
{
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        // Skip connections belonging to suspended studios — a suspended tenant shouldn't
        // keep burning Instagram API quota on a nightly sync nobody can see the result of
        // (the public read path already hides these posts via GetPublicArtistInstagramPostsQuery).
        List<InstagramConnection> connections = await db.InstagramConnections
            .Where(c => c.IsActive && db.Studios.Any(s => s.Id == c.StudioId && s.IsActive))
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
                logger.LogError(ex, "Instagram sync failed for artist {ArtistId}", conn.ArtistId);
            }
        }

        logger.LogInformation("InstagramSyncJob complete.");
    }

    private async Task SyncConnectionAsync(InstagramConnection conn, CancellationToken ct)
    {
        string token = encryptor.Decrypt(conn.EncryptedToken);

        if (conn.TokenExpiresAt <= DateTime.UtcNow.AddDays(7))
        {
            try
            {
                (string newToken, DateTime newExpiry) = await instagram.RefreshTokenAsync(token, ct);

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
                conn.IsActive  = false;
                conn.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);

                logger.LogWarning(
                    "Instagram token revoked for artist {ArtistId}. Deactivating.", conn.ArtistId);
                return;
            }
        }

        List<InstagramMediaItem> items = await instagram.GetMediaAsync(token, ct);

        HashSet<string> existingMediaIds = (await db.InstagramPosts
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
            "Synced Instagram for artist {ArtistId}. New posts added: {Added}", conn.ArtistId, added);
    }
}
