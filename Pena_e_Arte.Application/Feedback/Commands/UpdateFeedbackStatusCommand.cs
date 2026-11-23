using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Feedback.Commands;

// Audited (D04 follow-up, 23 Nov). The entry records the new status and WHETHER the internal note changed,
// never the note's text: it is admin-written text about a named user's feedback, the field behind the
// AdminNote leak (#371). AuditMetadataBuilder whitelists fields, so Request.AdminNote is never serialized.
public record UpdateFeedbackStatusCommand(
    Guid Id,
    UpdateFeedbackStatusRequest Request) : IRequest<FeedbackReportResponse>, IAuditableCommand
{
    public string AuditAction => AuditActions.FeedbackStatusUpdated;
    public string AuditTargetType => AuditTargetTypes.FeedbackReport;
    public Guid AuditTargetId => Id;
    public Guid? AuditStudioId => ReportStudioId;

    /// <summary>The report's studio, so the entry shows under "Only this studio". Set by the handler.</summary>
    public Guid? ReportStudioId { get; set; }

    /// <summary>Whether the internal note's stored value changed. Set by the handler; audit metadata only.</summary>
    public bool AdminNoteChanged { get; set; }
}

public class UpdateFeedbackStatusHandler(IAppDbContext db)
    : IRequestHandler<UpdateFeedbackStatusCommand, FeedbackReportResponse>
{
    public async Task<FeedbackReportResponse> Handle(UpdateFeedbackStatusCommand command, CancellationToken ct)
    {
        FeedbackReport report = await db.FeedbackReports
            .FirstOrDefaultAsync(r => r.Id == command.Id, ct)
            ?? throw new NotFoundException(nameof(FeedbackReport), command.Id);

        FeedbackStatus status = Enum.Parse<FeedbackStatus>(command.Request.Status, ignoreCase: true);
        command.ReportStudioId = report.StudioId;
        string? noteBefore = report.AdminNote;
        report.UpdateStatus(status, command.Request.AdminNote);
        command.AdminNoteChanged = !string.Equals(noteBefore, report.AdminNote, StringComparison.Ordinal);
        await db.SaveChangesAsync(ct);

        return Map(report);
    }

    // Admin-only endpoint, so the admin's shape (includes AdminNote).
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
