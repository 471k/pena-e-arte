using System.Linq.Expressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Feedback.Queries;

// type and status are optional filters (null/empty = all)
public record GetFeedbackReportsQuery(
    string? Type = null,
    string? Status = null) : IRequest<List<FeedbackReportResponse>>;

public class GetFeedbackReportsHandler(IAppDbContext db)
    : IRequestHandler<GetFeedbackReportsQuery, List<FeedbackReportResponse>>
{
    // Shared with GetMyFeedbackReportsHandler — an Expression (not a compiled Func) so EF
    // Core can translate it into the SQL projection rather than materializing full entities.
    internal static readonly Expression<Func<FeedbackReport, FeedbackReportResponse>> ToResponse = r =>
        new FeedbackReportResponse(
            r.Id,
            r.Type.ToString(),
            r.Title,
            r.Body,
            r.Status.ToString(),
            r.StudioName,
            r.SubmitterRole,
            r.IssuerNote,
            r.CreatedAt,
            r.ResolvedAt,
            r.AttachmentUrls);

    public async Task<List<FeedbackReportResponse>> Handle(GetFeedbackReportsQuery query, CancellationToken ct)
    {
        IQueryable<FeedbackReport> q = db.FeedbackReports.OrderByDescending(r => r.CreatedAt);

        if (!string.IsNullOrEmpty(query.Type) && Enum.TryParse(query.Type, ignoreCase: true, out FeedbackType type))
            q = q.Where(r => r.Type == type);

        if (!string.IsNullOrEmpty(query.Status) && Enum.TryParse(query.Status, ignoreCase: true, out FeedbackStatus status))
            q = q.Where(r => r.Status == status);

        return await q.Select(ToResponse).ToListAsync(ct);
    }
}
