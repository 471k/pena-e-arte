using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Public.Queries;

public record SitemapUrlEntry(string Path, DateTime LastModified);

public record GetSitemapUrlsQuery : IRequest<List<SitemapUrlEntry>>;

public class GetSitemapUrlsHandler(IAppDbContext db)
    : IRequestHandler<GetSitemapUrlsQuery, List<SitemapUrlEntry>>
{
    public async Task<List<SitemapUrlEntry>> Handle(GetSitemapUrlsQuery query, CancellationToken ct)
    {
        // Approved: public SEO sitemap — same justification as GetPublicStudioQuery/
        // GetPublicArtistQuery (#2 in architecture.md's IgnoreQueryFilters table).
        List<SitemapUrlEntry> studioUrls = await db.Studios
            .IgnoreQueryFilters()
            .Where(s => s.IsActive)
            .Select(s => new SitemapUrlEntry($"/s/{s.Slug}", s.CreatedAt))
            .ToListAsync(ct);

        List<SitemapUrlEntry> artistUrls = await db.Artists
            .IgnoreQueryFilters()
            .Where(a => a.DeletedAt == null && a.IsActive && a.Slug != null)
            .Select(a => new SitemapUrlEntry($"/artist/{a.Slug}", a.UpdatedAt))
            .ToListAsync(ct);

        return studioUrls.Concat(artistUrls).ToList();
    }
}
