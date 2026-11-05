using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Appointments.Common;

/// <summary>
/// Whether the caller may act on an appointment that belongs to an artist: an artist only on a session
/// assigned to their own artist profile, owners and admins on any session in the studio (the tenant
/// filter scopes the studio). Callers turn "false" into 404, never 403, so a guessed id confirms nothing.
/// Used where an artist's action moves money on a session (no-show, Phi 2026-11-05), matching the
/// payment read scoped in PR #335.
/// </summary>
internal static class AssignedArtistAccess
{
    public static async Task<bool> MayActOnAsync(
        IAppDbContext db, ICurrentUser currentUser, Guid? appointmentArtistId, CancellationToken ct)
    {
        if (currentUser.Role != "artist")
            return true;

        Guid? myArtistId = await db.Artists
            .Where(a => a.UserId == currentUser.UserId)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);

        // No artist profile means no assigned sessions; an unassigned request (null) is never "mine".
        return myArtistId is not null && appointmentArtistId == myArtistId;
    }
}
