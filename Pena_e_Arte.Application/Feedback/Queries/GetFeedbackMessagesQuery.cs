using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Feedback.Queries;

public record GetFeedbackMessagesQuery(Guid FeedbackReportId) : IRequest<List<FeedbackMessageResponse>>;

public class GetFeedbackMessagesHandler(IAppDbContext db, ICurrentUser user, ICurrentTenant tenant)
    : IRequestHandler<GetFeedbackMessagesQuery, List<FeedbackMessageResponse>>
{
    public async Task<List<FeedbackMessageResponse>> Handle(GetFeedbackMessagesQuery query, CancellationToken ct)
    {
        await FeedbackAccessGuard.LoadAccessibleReportAsync(db, query.FeedbackReportId, user, tenant, ct);

        return await db.FeedbackMessages
            .Where(m => m.FeedbackReportId == query.FeedbackReportId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new FeedbackMessageResponse(
                m.Id, m.FeedbackReportId, m.AuthorUserId, m.AuthorRole, m.Body, m.CreatedAt))
            .ToListAsync(ct);
    }
}
