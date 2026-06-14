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
            client.UserId    = currentUser.UserId; // heal the missing link
            client.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return client;
    }
}
