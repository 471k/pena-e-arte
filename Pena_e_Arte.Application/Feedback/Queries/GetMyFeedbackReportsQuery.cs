using System.Linq.Expressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Feedback.Queries;

public record GetMyFeedbackReportsQuery(string? Type = null) : IRequest<List<MyFeedbackReportResponse>>;

public class GetMyFeedbackReportsHandler(IAppDbContext db, ICurrentUser user, ICurrentTenant tenant)
    : IRequestHandler<GetMyFeedbackReportsQuery, List<MyFeedbackReportResponse>>
{
    // The submitter's projection: no AdminNote (the admin's internal note). An Expression so EF
    // translates it into the SQL projection; the note column isn't even read.
    internal static readonly Expression<Func<FeedbackReport, MyFeedbackReportResponse>> ToSubmitterResponse = r =>
        new MyFeedbackReportResponse(
            r.Id,
            r.Type.ToString(),
            r.Title,
            r.Body,
            r.Status.ToString(),
            r.StudioName,
            r.SubmitterRole,
            r.CreatedAt,
            r.ResolvedAt,
            r.AttachmentUrls);

    public async Task<List<MyFeedbackReportResponse>> Handle(GetMyFeedbackReportsQuery query, CancellationToken ct)
    {
        IQueryable<FeedbackReport> q = db.FeedbackReports
            .Where(r => r.SubmitterUserId == user.UserId && r.StudioId == tenant.StudioId)
            .OrderByDescending(r => r.CreatedAt);

        if (!string.IsNullOrEmpty(query.Type) && Enum.TryParse(query.Type, ignoreCase: true, out FeedbackType type))
            q = q.Where(r => r.Type == type);

        return await q.Select(ToSubmitterResponse).ToListAsync(ct);
    }
}
