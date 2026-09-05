using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ConductReports.Commands;

public record FileStudioConductReportCommand(
    string Slug,
    Guid AppointmentId,
    Guid ReporterUserId,
    string ReporterName,
    ReportCategory Category,
    string Reason,
    IReadOnlyList<string>? AttachmentUrls) : IRequest, IFileConductReportCommand;

public class FileStudioConductReportValidator(IR2Service r2)
    : FileConductReportValidatorBase<FileStudioConductReportCommand>(r2);

public class FileStudioConductReportHandler(
    IAppDbContext db, INotificationService notifications, ILogger<FileStudioConductReportHandler> logger)
    : IRequestHandler<FileStudioConductReportCommand>
{
    public async Task Handle(FileStudioConductReportCommand command, CancellationToken ct)
    {
        // Approved: public portfolio lookup — see architecture.md AllowAnonymous Exceptions.
        Studio studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Slug == command.Slug && s.IsActive, ct)
            ?? throw new NotFoundException(nameof(Studio), command.Slug);

        // Approved: cross-tenant ownership check — identical join shape to
        // CreateStudioReviewHandler, EXCEPT no AppointmentStatus.Completed filter and no
        // "already reported" exclusion (see FileArtistConductReportHandler for the reasoning).
        var appointment = await db.Appointments
            .IgnoreQueryFilters()
            .Where(a => a.Id == command.AppointmentId)
            .Join(db.Clients.IgnoreQueryFilters(),
                  a => a.ClientId,
                  c => c.Id,
                  (a, c) => new { Appointment = a, ClientUserId = c.UserId })
            .FirstOrDefaultAsync(ct);

        bool ownedByReporterAtThisStudio = appointment is not null
            && appointment.Appointment.StudioId == studio.Id
            && appointment.ClientUserId == command.ReporterUserId;

        if (!ownedByReporterAtThisStudio)
            throw new NotFoundException(nameof(Appointment), command.AppointmentId);

        // studio is already loaded above (it IS the filing target) — passed straight through,
        // no second query.
        await ConductReportFilingHelper.FileAsync(
            db, notifications, logger, studio,
            () => ConductReport.ForStudio(
                studio.Id,
                command.AppointmentId,
                command.ReporterUserId,
                command.ReporterName,
                command.Category,
                command.Reason,
                command.AttachmentUrls),
            ct);
    }
}
