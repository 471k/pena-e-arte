using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Feedback.Commands;

public record UpdateFeedbackStatusCommand(
    Guid Id,
    UpdateFeedbackStatusRequest Request) : IRequest<FeedbackReportResponse>;

public class UpdateFeedbackStatusHandler(IAppDbContext db)
    : IRequestHandler<UpdateFeedbackStatusCommand, FeedbackReportResponse>
{
    public async Task<FeedbackReportResponse> Handle(UpdateFeedbackStatusCommand command, CancellationToken ct)
    {
        FeedbackReport report = await db.FeedbackReports
            .FirstOrDefaultAsync(r => r.Id == command.Id, ct)
            ?? throw new NotFoundException(nameof(FeedbackReport), command.Id);

        FeedbackStatus status = Enum.Parse<FeedbackStatus>(command.Request.Status, ignoreCase: true);
        report.UpdateStatus(status, command.Request.IssuerNote);
        await db.SaveChangesAsync(ct);

        return SubmitFeedbackHandler.Map(report);
    }
}
