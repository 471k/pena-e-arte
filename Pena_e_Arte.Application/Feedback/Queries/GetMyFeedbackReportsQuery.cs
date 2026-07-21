using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Feedback.Queries;

public record GetMyFeedbackReportsQuery(string? Type = null) : IRequest<List<FeedbackReportResponse>>;

public class GetMyFeedbackReportsHandler(IAppDbContext db, ICurrentUser user, ICurrentTenant tenant)
    : IRequestHandler<GetMyFeedbackReportsQuery, List<FeedbackReportResponse>>
{
    public async Task<List<FeedbackReportResponse>> Handle(GetMyFeedbackReportsQuery query, CancellationToken ct)
    {
        IQueryable<FeedbackReport> q = db.FeedbackReports
            .Where(r => r.SubmitterUserId == user.UserId && r.StudioId == tenant.StudioId)
            .OrderByDescending(r => r.CreatedAt);

        if (!string.IsNullOrEmpty(query.Type) && Enum.TryParse(query.Type, ignoreCase: true, out FeedbackType type))
            q = q.Where(r => r.Type == type);

        return await q.Select(GetFeedbackReportsHandler.ToResponse).ToListAsync(ct);
    }
}
