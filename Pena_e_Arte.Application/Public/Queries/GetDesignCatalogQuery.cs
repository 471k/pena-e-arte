using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Public.Queries;

/// <summary>Public, anonymous-callable: every active flash/catalog design a studio's artists
/// offer, for the public "Flash" browsing tab. See architecture.md's AllowAnonymous Exceptions
/// table — same "read-only public images, no PII" shape as the portfolio feed.</summary>
public record GetDesignCatalogQuery(string StudioSlug) : IRequest<List<DesignCatalogItemResponse>>;

public class GetDesignCatalogHandler(IAppDbContext db)
    : IRequestHandler<GetDesignCatalogQuery, List<DesignCatalogItemResponse>>
{
    public async Task<List<DesignCatalogItemResponse>> Handle(
        GetDesignCatalogQuery query, CancellationToken ct)
    {
        Studio studio = await db.GetPublishedStudioBySlugAsync(query.StudioSlug, ct)
            ?? throw new NotFoundException(nameof(Studio), query.StudioSlug);

        // IgnoreQueryFilters: anonymous caller, no ambient tenant — same shape as every other
        // public handler resolving data by the studio.Id looked up above.
        List<Design> catalogItems = await db.Designs
            .IgnoreQueryFilters()
            .Include(d => d.Artist)
            .Include(d => d.Revisions)
            .Where(d => d.StudioId == studio.Id && d.DeletedAt == null
                        && d.IsCatalogItem && d.ClientId == null)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        return catalogItems.Select(d =>
        {
            DesignRevision? latest = d.Revisions.OrderByDescending(r => r.VersionNumber).FirstOrDefault();
            return new DesignCatalogItemResponse(
                d.Id, d.Title, d.Description, d.Price,
                d.ArtistId, $"{d.Artist.FirstName} {d.Artist.LastName}".Trim(),
                latest?.FileUrl);
        }).ToList();
    }
}
