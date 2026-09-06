using MediatR;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.ConductReports.Queries;

// Admin-facing cross-tenant read. No IgnoreQueryFilters() needed on ConductReports itself —
// it has no query filter registered at all (see AppDbContext / architecture.md), so this is a
// plain read across every studio's reports, not an approved-usages-table exception. Filterable
// by category, status, and studioId, following GetFeedbackReportsHandler's exact filter-chain
// shape.
public record GetConductReportsQuery(
    string? Category = null, string? Status = null, Guid? StudioId = null)
    : IRequest<List<ConductReportResponse>>;

public class GetConductReportsHandler(IAppDbContext db)
    : IRequestHandler<GetConductReportsQuery, List<ConductReportResponse>>
{
    public async Task<List<ConductReportResponse>> Handle(GetConductReportsQuery query, CancellationToken ct)
    {
        IQueryable<ConductReport> q = db.ConductReports.OrderByDescending(r => r.CreatedAt);

        if (!string.IsNullOrEmpty(query.Category) && Enum.TryParse(query.Category, true, out ReportCategory category))
            q = q.Where(r => r.Category == category);

        if (!string.IsNullOrEmpty(query.Status) && Enum.TryParse(query.Status, true, out ReportStatus status))
            q = q.Where(r => r.Status == status);

        if (query.StudioId is Guid studioId)
            q = q.Where(r => r.StudioId == studioId);

        // Full reporter identity included — admin is always authorized to see it.
        return await ConductReportProjections.ToFullResponseAsync(q, db, ct);
    }
}
