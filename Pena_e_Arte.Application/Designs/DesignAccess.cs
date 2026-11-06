using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Designs;

/// <summary>
/// Who may open one design and its versions (2026-11-06, Phi's go after the E04c report). The same people
/// the design list (GetDesignsHandler) already shows it to: the design's own client, the artist who owns
/// it, and the studio's owners and admins (tenant filter). Before this, GET designs/{id} and
/// GET designs/{id}/revisions checked only the tenant, so any client or artist in the studio holding a
/// design's id could read it and get presigned links to its images.
///
/// Callers answer 404 when this is false, never 403, so a guessed or forwarded id doesn't confirm the
/// design exists (the same rule as ReviewDesignHandler and the client appointment read).
/// Public share links (GetSharedDesignQuery) and the flash catalog are separate reads and unaffected.
/// </summary>
public static class DesignAccess
{
    public static async Task<bool> MayReadAsync(
        IAppDbContext db, ICurrentUser currentUser, Design design, CancellationToken ct)
    {
        switch (currentUser.Role)
        {
            case "owner":
            case "admin":
                return true;
            case "artist":
                return await db.Artists.AnyAsync(a => a.Id == design.ArtistId && a.UserId == currentUser.UserId, ct);
            case "client":
                if (design.ClientId is not { } clientId) return false;
                Client? me = await db.FindClientForUserAsync(currentUser, ct);
                return me is not null && me.Id == clientId;
            default:
                return false;
        }
    }
}
