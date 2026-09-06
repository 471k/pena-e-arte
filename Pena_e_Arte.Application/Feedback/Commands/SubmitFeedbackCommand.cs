using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Feedback.Commands;

public record SubmitFeedbackCommand(SubmitFeedbackRequest Request) : IRequest<FeedbackReportResponse>;

public class SubmitFeedbackHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    ICurrentUser user)
    : IRequestHandler<SubmitFeedbackCommand, FeedbackReportResponse>
{
    public async Task<FeedbackReportResponse> Handle(SubmitFeedbackCommand command, CancellationToken ct)
    {
        Studio studio = await db.Studios
            .FirstOrDefaultAsync(s => s.Id == tenant.StudioId, ct)
            ?? throw new InvalidOperationException("Studio not found for current tenant.");

        FeedbackType type = Enum.Parse<FeedbackType>(command.Request.Type, ignoreCase: true);

        FeedbackReport report = FeedbackReport.Create(
            studioId: tenant.StudioId,
            submitterUserId: user.UserId,
            submitterRole: user.Role,
            studioName: studio.Name,
            type: type,
            title: command.Request.Title,
            body: command.Request.Body,
            attachmentUrls: command.Request.AttachmentUrls);

        db.FeedbackReports.Add(report);
        await db.SaveChangesAsync(ct);

        return Map(report);
    }

    internal static FeedbackReportResponse Map(FeedbackReport r) => new(
        r.Id,
        r.Type.ToString(),
        r.Title,
        r.Body,
        r.Status.ToString(),
        r.StudioName,
        r.SubmitterRole,
        r.AdminNote,
        r.CreatedAt,
        r.ResolvedAt,
        r.AttachmentUrls);
}
