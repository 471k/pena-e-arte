using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ConductReports.Queries;

public record GetMyConductReportsAsArtistQuery : IRequest<List<ConductReportResponse>>;

public class GetMyConductReportsAsArtistHandler(IAppDbContext db, ICurrentUser user)
    : IRequestHandler<GetMyConductReportsAsArtistQuery, List<ConductReportResponse>>
{
    public async Task<List<ConductReportResponse>> Handle(
        GetMyConductReportsAsArtistQuery query, CancellationToken ct)
    {
        // Tenant-filtered by the caller's own JWT tenant — an artist only ever authenticates
        // within their own studio, so this naturally resolves their own Artist row.
        Artist? me = await db.Artists.FirstOrDefaultAsync(a => a.UserId == user.UserId, ct);
        if (me is null) return [];

        IQueryable<ConductReport> q = db.ConductReports
            .Where(r => r.ArtistId == me.Id)
            .OrderByDescending(r => r.CreatedAt);

        // Redacted — reporter identity fields always null. Do NOT reuse ToFullResponseAsync
        // here even though the shape matches; that helper is only for owner/admin callers.
        // This is the single most important guarantee in this whole feature: see
        // ConductReportProjections.Map's `redact` branch and the accompanying tests.
        return await ConductReportProjections.ToRedactedResponseAsync(q, db, ct);
    }
}
