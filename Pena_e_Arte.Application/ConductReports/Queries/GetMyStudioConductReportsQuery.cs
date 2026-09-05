using MediatR;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ConductReports.Queries;

public record GetMyStudioConductReportsQuery(string? Status = null) : IRequest<List<ConductReportResponse>>;

public class GetMyStudioConductReportsHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<GetMyStudioConductReportsQuery, List<ConductReportResponse>>
{
    public async Task<List<ConductReportResponse>> Handle(
        GetMyStudioConductReportsQuery query, CancellationToken ct)
    {
        IQueryable<ConductReport> q = db.ConductReports
            .Where(r => r.StudioId == tenant.StudioId)
            .OrderByDescending(r => r.CreatedAt);

        if (!string.IsNullOrEmpty(query.Status) && Enum.TryParse(query.Status, true, out ReportStatus status))
            q = q.Where(r => r.Status == status);

        // Full reporter identity included — owner is always authorized to see it for reports
        // targeting their own studio.
        return await ConductReportProjections.ToFullResponseAsync(q, db, ct);
    }
}
