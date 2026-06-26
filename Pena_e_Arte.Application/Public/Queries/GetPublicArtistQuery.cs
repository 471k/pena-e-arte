using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetPublicArtistQuery(string Slug, Guid? CurrentUserId)
    : IRequest<PublicArtistResponse?>;

public class GetPublicArtistHandler(IAppDbContext db)
    : IRequestHandler<GetPublicArtistQuery, PublicArtistResponse?>
{
    public async Task<PublicArtistResponse?> Handle(
        GetPublicArtistQuery query, CancellationToken ct)
    {
        // Approved: public portfolio query — see architecture.md AllowAnonymous Exceptions.
        Artist? artist = await db.Artists
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Slug == query.Slug && a.DeletedAt == null, ct);

        if (artist is null) return null;

        // Approved: public portfolio query.
        Studio? studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == artist.StudioId && s.IsActive, ct);

        if (studio is null) return null;

        // Artist-level review aggregate.
        // Approved: public portfolio query.
        var reviewStats = await db.Reviews
            .Where(r => r.ArtistId == artist.Id)
            .GroupBy(_ => 1)
            .Select(g => new { Avg = g.Average(r => (double)r.Rating), Count = g.Count() })
            .FirstOrDefaultAsync(ct);

        bool isOwnProfile = query.CurrentUserId.HasValue
                         && artist.UserId == query.CurrentUserId;

        return new PublicArtistResponse(
            artist.Id,
            $"{artist.FirstName} {artist.LastName}",
            artist.Slug!,
            artist.Bio,
            artist.ProfileImageUrl,
            artist.PortfolioImages,
            artist.Specializations,
            artist.HourlyRate,
            reviewStats is { Count: > 0 } ? Math.Round(reviewStats.Avg, 1) : null,
            reviewStats?.Count ?? 0,
            studio.Name,
            studio.Slug,
            ShowBookingCta: true,
            isOwnProfile);
    }
}
