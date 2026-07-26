using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Common;

public static class ClientAccountExtensions
{
    /// <summary>
    /// Resolves the tenant's Client record for the logged-in user.
    /// Primary match is Client.UserId; when absent (accounts created before the
    /// registration linkage existed, or clients pre-created by the studio), it
    /// falls back to an email match and heals the link by stamping UserId.
    /// Tenant query filters apply — this never crosses studios.
    /// </summary>
    public static async Task<Client?> FindClientForUserAsync(
        this IAppDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        Client? client = await db.Clients
            .FirstOrDefaultAsync(c => c.UserId == currentUser.UserId, ct);
        if (client is not null) return client;

        if (string.IsNullOrEmpty(currentUser.Email)) return null;

        client = await db.Clients
            .FirstOrDefaultAsync(c => c.UserId == null && c.Email == currentUser.Email, ct);

        if (client is not null)
        {
            client.UserId = currentUser.UserId; // heal the missing link
            client.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return client;
    }

    /// <summary>
    /// Approved exception #5 (see docs/claude/database.md "Tenant Isolation Rules"):
    /// looks up a user's Client membership at a studio OTHER than the one currently
    /// scoped by the request's JWT, for the multi-studio "switch active studio" flow.
    /// Must never be used to read another tenant's data — only to check/create the
    /// caller's OWN membership at a studio they explicitly asked to join.
    /// </summary>
    public static Task<Client?> FindClientForUserAtStudioAsync(
        this IAppDbContext db, Guid userId, Guid studioId, CancellationToken ct) =>
        db.Clients.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.StudioId == studioId && c.DeletedAt == null, ct);

    /// <summary>
    /// Approved exception #5 — finds the user's oldest Client record across ALL
    /// studios, used only to seed name/email/phone when auto-provisioning a new
    /// membership at a studio they've never joined. Never used to copy medical data.
    /// </summary>
    public static Task<Client?> FindAnyClientRecordForUserAsync(
        this IAppDbContext db, Guid userId, CancellationToken ct) =>
        db.Clients.IgnoreQueryFilters()
            .Where(c => c.UserId == userId && c.DeletedAt == null)
            .OrderBy(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);
}
