using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Utilities;

namespace Pena_e_Arte.Application.Artists;

/// <summary>
/// Shared artist-slug uniqueness loop — used by both CreateArtistHandler and
/// AcceptStudioJoinInviteHandler so the two Artist-creation paths don't each carry their
/// own copy of the same while-loop.
/// </summary>
public static class ArtistSlugHelper
{
    public static async Task<string> GenerateUniqueSlugAsync(
        IAppDbContext db, string firstName, string lastName, CancellationToken ct)
    {
        string baseSlug = SlugHelper.GenerateSlug($"{firstName} {lastName}");
        string slug = baseSlug;
        int counter = 2;
        // IgnoreQueryFilters: slug must be globally unique for public portfolio URLs
        while (await db.Artists.IgnoreQueryFilters().AnyAsync(a => a.Slug == slug && a.DeletedAt == null, ct))
        {
            slug = $"{baseSlug}-{counter}";
            counter++;
        }

        return slug;
    }
}
