using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ConductReports.Commands;

public record FileArtistConductReportCommand(
    string Slug,
    Guid AppointmentId,
    Guid ReporterUserId,
    string ReporterName,
    ReportCategory Category,
    string Reason,
    IReadOnlyList<string>? AttachmentUrls) : IRequest, IFileConductReportCommand;

public class FileArtistConductReportValidator(IR2Service r2)
    : FileConductReportValidatorBase<FileArtistConductReportCommand>(r2);

public class FileArtistConductReportHandler(
    IAppDbContext db, INotificationService notifications, ILogger<FileArtistConductReportHandler> logger)
    : IRequestHandler<FileArtistConductReportCommand>
{
    public async Task Handle(FileArtistConductReportCommand command, CancellationToken ct)
    {
        // Approved: public portfolio lookup, same as CreateArtistReviewHandler.
        Artist artist = await db.Artists
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Slug == command.Slug && a.DeletedAt == null, ct)
            ?? throw new NotFoundException(nameof(Artist), command.Slug);

        // Approved: cross-tenant ownership check — identical join shape to
        // CreateArtistReviewHandler, EXCEPT no AppointmentStatus.Completed filter and no
        // "already reported" exclusion (a conduct report is about an incident during an
        // appointment the studio controls the status of — gating on Completed would let a
        // studio dodge every report by never marking the appointment complete; a client may
        // also reasonably file more than one report against the same visit).
        var appointment = await db.Appointments
            .IgnoreQueryFilters()
            .Where(a => a.Id == command.AppointmentId)
            .Join(db.Clients.IgnoreQueryFilters(),
                  a => a.ClientId,
                  c => c.Id,
                  (a, c) => new { Appointment = a, ClientUserId = c.UserId })
            .FirstOrDefaultAsync(ct);

        bool ownedByReporterWithThisArtist = appointment is not null
            && appointment.Appointment.ArtistId == artist.Id
            && appointment.ClientUserId == command.ReporterUserId;

        // 404, not a generic error — same "don't reveal another client's appointment exists"
        // convention as RescheduleAppointmentHandler / CreateArtistReviewHandler.
        if (!ownedByReporterWithThisArtist)
            throw new NotFoundException(nameof(Appointment), command.AppointmentId);

        // Not already loaded anywhere else on this path — the artist lookup above only ever
        // touches Artists, so this is the one query needed to get the Studio for the alert
        // email, not a re-query of something the handler already has (contrast the studio
        // handler, which passes the studio it already resolved as the filing target itself).
        Studio studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == artist.StudioId, ct)
            ?? throw new NotFoundException(nameof(Studio), artist.StudioId);

        await ConductReportFilingHelper.FileAsync(
            db, notifications, logger, studio,
            () => ConductReport.ForArtist(
                artist.StudioId,
                artist.Id,
                command.AppointmentId,
                command.ReporterUserId,
                command.ReporterName,
                command.Category,
                command.Reason,
                command.AttachmentUrls),
            ct);
    }
}
