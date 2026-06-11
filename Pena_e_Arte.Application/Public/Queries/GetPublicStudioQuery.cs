using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetPublicStudioQuery(string Slug) : IRequest<PublicStudioResponse?>;

public class GetPublicStudioHandler(IAppDbContext db)
    : IRequestHandler<GetPublicStudioQuery, PublicStudioResponse?>
{
    public async Task<PublicStudioResponse?> Handle(GetPublicStudioQuery query, CancellationToken ct)
    {
        // Approved: public portfolio query — see architecture.md AllowAnonymous Exceptions
        Studio? studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Slug == query.Slug && s.IsActive, ct);

        if (studio is null) return null;

        // Approved: public portfolio query — see architecture.md AllowAnonymous Exceptions
        List<Artist> artists = await db.Artists
            .IgnoreQueryFilters()
            .Where(a => a.StudioId == studio.Id && a.DeletedAt == null && a.Slug != null)
            .ToListAsync(ct);

        return new PublicStudioResponse(
            studio.Id,
            studio.Name,
            studio.Slug,
            studio.City,
            studio.Description,
            studio.CoverImageUrl,
            artists.Select(a => new PublicArtistSummary(
                a.Id, $"{a.FirstName} {a.LastName}", a.Slug!, a.Bio)).ToList(),
            ShowBookingCta: true);
    }
}
