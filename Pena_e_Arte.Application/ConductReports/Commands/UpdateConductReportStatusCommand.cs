using FluentValidation;
using MediatR;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ConductReports.Commands;

// Known minor gap (flagged rather than silently "fixed" — see architecture.md Decisions Log):
// this command doesn't carry the target studio id directly, so an admin-authored status
// change (no ICurrentTenant set) falls back to AuditLogBehavior's `null` default for
// AuditStudioId, meaning the audit row isn't attributed to the report's actual studio. A clean
// fix needs either a second constructor step (look up report.StudioId before dispatch, which a
// command record can't do on its own) or a small refactor to IAuditableCommand — both out of
// scope here. The owner path is unaffected: AuditLogBehavior's ICurrentTenant fallback already
// attributes the row to the owner's own studio correctly.
public record UpdateConductReportStatusCommand(Guid ReportId, ReportStatus Status, string? ResolutionNote)
    : IRequest, IAuditableCommand
{
    public string AuditAction => AuditActions.ConductReportStatusUpdated;
    public string AuditTargetType => AuditTargetTypes.ConductReport;
    public Guid AuditTargetId => ReportId;
}

public class UpdateConductReportStatusValidator : AbstractValidator<UpdateConductReportStatusCommand>
{
    public UpdateConductReportStatusValidator()
    {
        RuleFor(x => x.ResolutionNote).MaximumLength(2000);
    }
}

public class UpdateConductReportStatusHandler(
    IAppDbContext db, ICurrentUser user, ICurrentTenant tenant)
    : IRequestHandler<UpdateConductReportStatusCommand>
{
    public async Task Handle(UpdateConductReportStatusCommand command, CancellationToken ct)
    {
        ConductReport report = await ConductReportAuthorizationGuard.LoadReadableReportAsync(
            db, command.ReportId, user, tenant, ct);

        ConductReportAuthorizationGuard.EnsureCanChangeStatus(report, user);

        report.UpdateStatus(command.Status, command.ResolutionNote);
        await db.SaveChangesAsync(ct);
    }
}
