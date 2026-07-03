using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Feedback.Queries;

// type and status are optional filters (null/empty = all)
public record GetFeedbackReportsQuery(
    string? Type   = null,
    string? Status = null) : IRequest<List<FeedbackReportResponse>>;

public class GetFeedbackReportsHandler(IAppDbContext db)
    : IRequestHandler<GetFeedbackReportsQuery, List<FeedbackReportResponse>>
{
    public async Task<List<FeedbackReportResponse>> Handle(GetFeedbackReportsQuery query, CancellationToken ct)
    {
        IQueryable<FeedbackReport> q = db.FeedbackReports.OrderByDescending(r => r.CreatedAt);

        if (!string.IsNullOrEmpty(query.Type) && Enum.TryParse(query.Type, ignoreCase: true, out FeedbackType type))
            q = q.Where(r => r.Type == type);

        if (!string.IsNullOrEmpty(query.Status) && Enum.TryParse(query.Status, ignoreCase: true, out FeedbackStatus status))
            q = q.Where(r => r.Status == status);

        return await q.Select(r => new FeedbackReportResponse(
                r.Id,
                r.Type.ToString(),
                r.Title,
                r.Body,
                r.Status.ToString(),
                r.StudioName,
                r.SubmitterRole,
                r.IssuerNote,
                r.CreatedAt,
                r.ResolvedAt))
            .ToListAsync(ct);
    }
}
