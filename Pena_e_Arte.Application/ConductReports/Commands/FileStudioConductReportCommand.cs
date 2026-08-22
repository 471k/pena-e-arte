using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
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
    IReadOnlyList<string>? AttachmentUrls) : IRequest;

public class FileStudioConductReportValidator : AbstractValidator<FileStudioConductReportCommand>
{
    private const int MaxAttachments = 3;

    public FileStudioConductReportValidator(IR2Service r2)
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

public class FileStudioConductReportHandler(IAppDbContext db, INotificationService notifications)
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

        ConductReport report = ConductReport.ForStudio(
            studio.Id,
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
