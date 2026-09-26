using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Public.Queries;

/// <summary>A sitemap row. <c>LastModified</c> is null for static pages, which have no meaningful date.</summary>
public record SitemapUrlEntry(string Path, DateTime? LastModified);

public static class SitemapPaths
{
    // Public, indexable marketing surfaces. Policy pages stay indexable but are deliberately not
    // listed. Keep in step with frontend/src/shared/seo/siteRoutes.ts (a frontend test asserts it).
    public static readonly IReadOnlyList<string> Marketing =
    [
        "/", "/features", "/pricing", "/use/booking", "/use/deposits",
        "/use/consent-forms", "/faq", "/discover", "/contact",
    ];
}

public record GetSitemapUrlsQuery : IRequest<List<SitemapUrlEntry>>;

public class GetSitemapUrlsHandler(IAppDbContext db)
    : IRequestHandler<GetSitemapUrlsQuery, List<SitemapUrlEntry>>
{
    public async Task<List<SitemapUrlEntry>> Handle(GetSitemapUrlsQuery query, CancellationToken ct)
    {
        List<SitemapUrlEntry> marketingUrls = SitemapPaths.Marketing
            .Select(path => new SitemapUrlEntry(path, null))
            .ToList();

        // Approved: public SEO sitemap — same justification as GetPublicStudioQuery/
        // GetPublicArtistQuery (#2 in architecture.md's IgnoreQueryFilters table). Only pages that
        // actually render are listed: a studio page requires IsActive && IsPublished
        // (GetPublishedStudioBySlugAsync), an artist page requires the artist's studio to be active
        // (artists deliberately do NOT require IsPublished — see architecture.md, "IsActive vs
        // IsPublished").
        List<SitemapUrlEntry> studioUrls = await db.Studios
            .IgnoreQueryFilters()
            .Where(s => s.IsActive && s.IsPublished)
            .Select(s => new SitemapUrlEntry($"/s/{s.Slug}", s.CreatedAt))
            .ToListAsync(ct);

        List<SitemapUrlEntry> artistUrls = await db.Artists
            .IgnoreQueryFilters()
            .Where(a => a.DeletedAt == null && a.IsActive && a.Slug != null
                && db.Studios.IgnoreQueryFilters().Any(s => s.Id == a.StudioId && s.IsActive))
            .Select(a => new SitemapUrlEntry($"/artist/{a.Slug}", a.UpdatedAt))
            .ToListAsync(ct);

        return marketingUrls.Concat(studioUrls).Concat(artistUrls).ToList();
    }
}
