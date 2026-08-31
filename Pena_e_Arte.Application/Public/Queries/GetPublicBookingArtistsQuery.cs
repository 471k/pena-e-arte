using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetPublicBookingArtistsQuery(string StudioSlug)
    : IRequest<IReadOnlyList<PublicBookingArtistResponse>>;

public class GetPublicBookingArtistsHandler(IAppDbContext db)
    : IRequestHandler<GetPublicBookingArtistsQuery, IReadOnlyList<PublicBookingArtistResponse>>
{
    public async Task<IReadOnlyList<PublicBookingArtistResponse>> Handle(
        GetPublicBookingArtistsQuery query, CancellationToken ct)
    {
        // Approved: public/anonymous studio-slug resolution — same predicate as GetPublicStudioHandler.
        Studio studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Slug == query.StudioSlug && s.IsActive && s.IsPublished, ct)
            ?? throw new NotFoundException(nameof(Studio), query.StudioSlug);

        // Approved: public/anonymous — cross-tenant artist list for the guest booking picker.
        return await db.Artists
            .IgnoreQueryFilters()
            .Where(a => a.StudioId == studio.Id && a.IsActive && a.DeletedAt == null)
            .Select(a => new PublicBookingArtistResponse(
                a.Id, a.FirstName + " " + a.LastName, a.ProfileImageUrl, a.Specializations, a.HourlyRate))
            .ToListAsync(ct);
    }
}
