using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Social;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetPublicStudioQuery(string Slug) : IRequest<PublicStudioResponse?>;

public class GetPublicStudioHandler(IAppDbContext db)
    : IRequestHandler<GetPublicStudioQuery, PublicStudioResponse?>
{
    public async Task<PublicStudioResponse?> Handle(
        GetPublicStudioQuery query, CancellationToken ct)
    {
        // Approved: public portfolio query — see architecture.md AllowAnonymous Exceptions.
        // Shared with the guest-checkout-booking handlers via PublicStudioLookupExtensions.
        Studio? studio = await db.GetPublishedStudioBySlugAsync(query.Slug, ct);

        if (studio is null) return null;

        // Approved: public portfolio query.
        // DistinctBy guards against data-layer duplicates (e.g., two records with the same Id).
        List<Artist> artists = await db.Artists
            .IgnoreQueryFilters()
            .Include(a => a.Portfolio)
            .Where(a => a.StudioId == studio.Id && a.DeletedAt == null && a.Slug != null)
            .ToListAsync(ct);

        artists = artists.DistinctBy(a => a.Id).ToList();

        List<Guid> artistIds = artists.Select(a => a.Id).ToList();

        // Per-artist review aggregates.
        // Approved: public portfolio query.
        Dictionary<Guid, (double Avg, int Count)> artistReviewStats = await db.Reviews
            .Where(r => r.ArtistId != null && artistIds.Contains(r.ArtistId.Value))
            .GroupBy(r => r.ArtistId!.Value)
            .Select(g => new { ArtistId = g.Key, Avg = g.Average(r => (double)r.Rating), Count = g.Count() })
            .ToDictionaryAsync(x => x.ArtistId, x => (x.Avg, x.Count), ct);

        // Studio-level review aggregate.
        // Approved: public portfolio query.
        var studioReviewStats = await db.Reviews
            .Where(r => r.StudioId == studio.Id)
            .GroupBy(_ => 1)
            .Select(g => new { Avg = g.Average(r => (double)r.Rating), Count = g.Count() })
            .FirstOrDefaultAsync(ct);

        // Gallery: up to 3 images per artist, max 9 total, round-robin so no single artist dominates.
        // Prefer Fresh/Healed tattoo photos over Design images — a design/flash sketch only fills a
        // slot when an artist has fewer than 3 non-Design images. Newest first within each group.
        List<List<string>> imagesByArtist = artists
            .Select(a =>
            {
                List<PortfolioImage> ordered = a.Portfolio.OrderByDescending(p => p.CreatedAt).ToList();
                List<PortfolioImage> nonDesign = ordered.Where(p => p.Category != PortfolioImageCategory.Design).ToList();
                List<PortfolioImage> designs = ordered.Where(p => p.Category == PortfolioImageCategory.Design).ToList();
                return nonDesign.Concat(designs).Take(3).Select(p => p.ImageUrl).ToList();
            })
            .Where(imgs => imgs.Count > 0)
            .ToList();

        List<string> galleryImages = [];
        int maxSlots = 9;
        for (int i = 0; i < 3 && galleryImages.Count < maxSlots; i++)
        {
            foreach (List<string> imgs in imagesByArtist)
            {
                if (i < imgs.Count && galleryImages.Count < maxSlots)
                    galleryImages.Add(imgs[i]);
            }
        }

        // Approved: public portfolio query — same class as the other reads in this handler.
        List<SocialAccountLink> studioSocialLinks = await db.SocialAccountLinks
            .Where(s => s.SubjectType == SocialLinkSubjectType.Studio && s.SubjectId == studio.Id)
            .OrderBy(s => s.Platform)
            .ToListAsync(ct);

        IReadOnlyList<PublicSocialLinkResponse> socialLinks = studioSocialLinks
            .Select(s => new PublicSocialLinkResponse(
                s.Platform.ToString(), s.Handle, s.IsVerified, SocialProfileUrlBuilder.Build(s.Platform, s.Handle)))
            .ToList();

        IReadOnlyList<PublicArtistSummary> artistSummaries = artists
            .Select(a =>
            {
                (double avg, int count) = artistReviewStats.GetValueOrDefault(a.Id, (0, 0));
                return new PublicArtistSummary(
                    a.Id,
                    $"{a.FirstName} {a.LastName}",
                    a.Slug!,
                    a.Bio,
                    a.ProfileImageUrl,
                    a.Specializations,
                    count > 0 ? Math.Round(avg, 1) : null,
                    count);
            })
            .ToList();

        return new PublicStudioResponse(
            studio.Id,
            studio.Name,
            studio.Slug,
            studio.City,
            studio.Latitude,
            studio.Longitude,
            studio.Description,
            studio.CoverImageUrl,
            studio.PhoneNumber,
            studioReviewStats is { Count: > 0 } ? Math.Round(studioReviewStats.Avg, 1) : null,
            studioReviewStats?.Count ?? 0,
            galleryImages,
            artistSummaries,
            ShowBookingCta: true,
            socialLinks);
    }
}
