using FluentValidation;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ConductReports.Commands;

/// <summary>
/// Shared rule set for <see cref="FileArtistConductReportValidator"/> and
/// <see cref="FileStudioConductReportValidator"/> — was two byte-identical validator classes;
/// this is the one place to change the rules going forward.
/// </summary>
public abstract class FileConductReportValidatorBase<T> : AbstractValidator<T>
    where T : IFileConductReportCommand
{
    private const int MaxAttachments = 3;

    protected FileConductReportValidatorBase(IR2Service r2)
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
