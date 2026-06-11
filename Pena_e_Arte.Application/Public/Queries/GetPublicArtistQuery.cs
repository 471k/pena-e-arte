using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetPublicArtistQuery(string Slug) : IRequest<PublicArtistResponse?>;

public class GetPublicArtistHandler(IAppDbContext db)
    : IRequestHandler<GetPublicArtistQuery, PublicArtistResponse?>
{
    public async Task<PublicArtistResponse?> Handle(GetPublicArtistQuery query, CancellationToken ct)
    {
        // Approved: public portfolio query — see architecture.md AllowAnonymous Exceptions
        Artist? artist = await db.Artists
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Slug == query.Slug && a.DeletedAt == null, ct);

        if (artist is null) return null;

        // Approved: public portfolio query — see architecture.md AllowAnonymous Exceptions
        Studio? studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == artist.StudioId && s.IsActive, ct);

        if (studio is null) return null;

        return new PublicArtistResponse(
            artist.Id,
            $"{artist.FirstName} {artist.LastName}",
            artist.Slug!,
            artist.Bio,
            artist.PortfolioImages,
            studio.Name,
            studio.Slug,
            ShowBookingCta: true);
    }
}
