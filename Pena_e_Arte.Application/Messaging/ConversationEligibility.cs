using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Messaging;

internal record EligibleContact(Guid UserId, string Role, string DisplayName, string? AvatarUrl);

/// <summary>
/// Shared by both GetConversationContactsQuery (the "who can I start a new thread with"
/// list for the UI) and CreateConversationCommand's handler (the actual write-path check)
/// so the two can never drift — same reasoning as FeedbackAccessGuard.
/// </summary>
internal static class ConversationEligibility
{
    /// <summary>Every user the given caller is allowed to message, per messaging Decision 4:
    /// client → their appointment/assigned artists + owner; artist → their appointment/
    /// assigned clients + owner; owner → every active artist + every client. Rows with no
    /// linked login (UserId null) are excluded — you cannot message someone who can't log
    /// in to read it.</summary>
    public static async Task<List<EligibleContact>> GetContactsAsync(
        IAppDbContext db, IIdentityService identity, Guid studioId, Guid callerUserId, string callerRole,
        CancellationToken ct)
    {
        List<EligibleContact> contacts = [];

        // Admin is deliberately never a messaging participant (messaging Decision 1) —
        // admin already has FeedbackReport/SupportHub for platform support. Without this
        // explicit exclusion, an admin request would fall through every role branch below
        // and still pick up the unconditional "owner is reachable by anyone" contact added
        // at the end of this method.
        if (string.Equals(callerRole, "admin", StringComparison.OrdinalIgnoreCase))
            return contacts;

        if (string.Equals(callerRole, "client", StringComparison.OrdinalIgnoreCase))
        {
            Domain.Entities.Client? client = await db.Clients
                .FirstOrDefaultAsync(c => c.UserId == callerUserId, ct);
            if (client is null) return contacts;

            HashSet<Guid> artistIds = [.. await db.Appointments
                .Where(a => a.ClientId == client.Id && a.ArtistId != null)
                .Select(a => a.ArtistId!.Value)
                .Distinct()
                .ToListAsync(ct)];
            if (client.ArtistId is { } assignedId) artistIds.Add(assignedId);

            // IsActive mirrors the owner-branch filter below and every other client-facing
            // surface (booking, artist listings) — a deactivated artist shouldn't be
            // messageable even if a past appointment/assignment still references them.
            List<Domain.Entities.Artist> artists = await db.Artists
                .Where(a => artistIds.Contains(a.Id) && a.UserId != null && a.IsActive)
                .ToListAsync(ct);
            contacts.AddRange(artists.Select(a =>
                new EligibleContact(a.UserId!.Value, "artist", $"{a.FirstName} {a.LastName}", a.AvatarUrl)));
        }
        else if (string.Equals(callerRole, "artist", StringComparison.OrdinalIgnoreCase))
        {
            Domain.Entities.Artist? artist = await db.Artists
                .FirstOrDefaultAsync(a => a.UserId == callerUserId, ct);
            if (artist is null) return contacts;

            HashSet<Guid> clientIds = [.. await db.Appointments
                .Where(a => a.ArtistId == artist.Id)
                .Select(a => a.ClientId)
                .Distinct()
                .ToListAsync(ct)];
            List<Guid> assignedClientIds = await db.Clients
                .Where(c => c.ArtistId == artist.Id)
                .Select(c => c.Id)
                .ToListAsync(ct);
            foreach (Guid id in assignedClientIds) clientIds.Add(id);

            List<Domain.Entities.Client> clients = await db.Clients
                .Where(c => clientIds.Contains(c.Id) && c.UserId != null)
                .ToListAsync(ct);
            contacts.AddRange(clients.Select(c =>
                new EligibleContact(c.UserId!.Value, "client", $"{c.FirstName} {c.LastName}", null)));
        }
        else if (string.Equals(callerRole, "owner", StringComparison.OrdinalIgnoreCase))
        {
            List<Domain.Entities.Artist> artists = await db.Artists
                .Where(a => a.IsActive && a.UserId != null)
                .ToListAsync(ct);
            contacts.AddRange(artists.Select(a =>
                new EligibleContact(a.UserId!.Value, "artist", $"{a.FirstName} {a.LastName}", a.AvatarUrl)));

            List<Domain.Entities.Client> clients = await db.Clients
                .Where(c => c.UserId != null)
                .ToListAsync(ct);
            contacts.AddRange(clients.Select(c =>
                new EligibleContact(c.UserId!.Value, "client", $"{c.FirstName} {c.LastName}", null)));
        }

        // Owner is reachable by anyone (client or artist) in the studio, unconditionally.
        if (!string.Equals(callerRole, "owner", StringComparison.OrdinalIgnoreCase))
        {
            (Guid? ownerUserId, string ownerName) = await TryResolveOwnerAsync(db, identity, studioId, ct);
            if (ownerUserId is { } id && id != callerUserId)
                contacts.Add(new EligibleContact(id, "owner", ownerName, null));
        }

        return contacts;
    }

    public static async Task<bool> IsEligibleAsync(
        IAppDbContext db, IIdentityService identity, Guid studioId, Guid callerUserId, string callerRole,
        Guid recipientUserId, CancellationToken ct)
    {
        List<EligibleContact> contacts =
            await GetContactsAsync(db, identity, studioId, callerUserId, callerRole, ct);
        return contacts.Any(c => c.UserId == recipientUserId);
    }

    public static async Task<(Guid? UserId, string DisplayName)> TryResolveOwnerAsync(
        IAppDbContext db, IIdentityService identity, Guid studioId, CancellationToken ct)
    {
        Domain.Entities.Studio? studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == studioId, ct);
        if (studio is null) return (null, "Studio Owner");

        Guid? ownerUserId = await identity.GetUserIdByEmailAsync(studio.OwnerEmail, ct);
        if (ownerUserId is null) return (null, "Studio Owner");

        string? displayName = await identity.GetUserDisplayNameAsync(studio.OwnerEmail, ct);
        return (ownerUserId, displayName ?? "Studio Owner");
    }
}
