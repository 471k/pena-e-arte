using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Social;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

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
            .Include(a => a.Portfolio)
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

        // Approved: public portfolio query — same class as the other reads in this handler.
        List<SocialAccountLink> artistSocialLinks = await db.SocialAccountLinks
            .Where(s => s.SubjectType == SocialLinkSubjectType.Artist && s.SubjectId == artist.Id)
            .OrderBy(s => s.Platform)
            .ToListAsync(ct);

        IReadOnlyList<PublicSocialLinkResponse> socialLinks = artistSocialLinks
            .Select(s => new PublicSocialLinkResponse(
                s.Platform.ToString(), s.Handle, s.IsVerified, SocialProfileUrlBuilder.Build(s.Platform, s.Handle)))
            .ToList();

        return new PublicArtistResponse(
            artist.Id,
            $"{artist.FirstName} {artist.LastName}",
            artist.Slug!,
            artist.Bio,
            artist.ProfileImageUrl,
            artist.Portfolio
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new ArtistPortfolioImageResponse(p.Id, p.ImageUrl, p.Style, p.Category))
                .ToList(),
            artist.Specializations,
            artist.HourlyRate,
            reviewStats is { Count: > 0 } ? Math.Round(reviewStats.Avg, 1) : null,
            reviewStats?.Count ?? 0,
            studio.Name,
            studio.Slug,
            ShowBookingCta: true,
            isOwnProfile,
            socialLinks);
    }
}
