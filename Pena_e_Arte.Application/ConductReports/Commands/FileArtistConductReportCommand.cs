using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
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
    IReadOnlyList<string>? AttachmentUrls) : IRequest;

public class FileArtistConductReportValidator : AbstractValidator<FileArtistConductReportCommand>
{
    private const int MaxAttachments = 3;

    public FileArtistConductReportValidator(IR2Service r2)
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(20).MaximumLength(2000);
        RuleFor(x => x.ReporterName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AttachmentUrls)
            .Must(urls => urls == null || urls.Count <= MaxAttachments)
            .WithMessage($"You can attach up to {MaxAttachments} files.");
        RuleForEach(x => x.AttachmentUrls)
            .NotEmpty().MaximumLength(2048).Must(r2.IsR2Url)
            .WithMessage("AttachmentUrls must reference a valid storage URL.")
            .When(x => x.AttachmentUrls is not null);
    }
}

public class FileArtistConductReportHandler(IAppDbContext db, INotificationService notifications)
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

        ConductReport report = ConductReport.ForArtist(
            artist.StudioId,
            artist.Id,
            command.AppointmentId,
            command.ReporterUserId,
            command.ReporterName,
            command.Category,
            command.Reason,
            command.AttachmentUrls);

        db.ConductReports.Add(report);
        await db.SaveChangesAsync(ct);

        await ConductReportNotifier.NotifyIfHighSeverityAsync(db, notifications, report, ct);
    }
}
