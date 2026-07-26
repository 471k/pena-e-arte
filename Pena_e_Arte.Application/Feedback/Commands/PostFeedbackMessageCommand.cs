using MediatR;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Feedback.Commands;

public record PostFeedbackMessageCommand(Guid FeedbackReportId, PostFeedbackMessageRequest Request)
    : IRequest<FeedbackMessageResponse>;

public class PostFeedbackMessageHandler(
    IAppDbContext db,
    ICurrentUser user,
    ICurrentTenant tenant,
    IRealtimeNotifier realtime)
    : IRequestHandler<PostFeedbackMessageCommand, FeedbackMessageResponse>
{
    public async Task<FeedbackMessageResponse> Handle(PostFeedbackMessageCommand command, CancellationToken ct)
    {
        FeedbackReport report = await FeedbackAccessGuard.LoadAccessibleReportAsync(
            db, command.FeedbackReportId, user, tenant, ct);

        FeedbackMessage message = FeedbackMessage.Create(
            report.Id, user.UserId, user.Role, command.Request.Body);
        db.FeedbackMessages.Add(message);

        // Replying to a closed ticket reopens it — the studio-side user is signaling it
        // isn't actually resolved. Issuer replies don't reopen (issuer is the one closing it).
        bool isStudioSideReply = !string.Equals(user.Role, "issuer", StringComparison.OrdinalIgnoreCase);
        if (isStudioSideReply && report.Status is FeedbackStatus.Resolved or FeedbackStatus.Dismissed)
        {
            report.UpdateStatus(FeedbackStatus.Open, report.IssuerNote);
        }

        await db.SaveChangesAsync(ct);

        FeedbackMessageResponse response = Map(message);
        await realtime.NotifyTicketAsync(report.Id, "SupportMessageReceived", response, ct);

        return response;
    }

    internal static FeedbackMessageResponse Map(FeedbackMessage m) =>
        new(m.Id, m.FeedbackReportId, m.AuthorUserId, m.AuthorRole, m.Body, m.CreatedAt);
}
