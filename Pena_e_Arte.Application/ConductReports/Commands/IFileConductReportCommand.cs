namespace Pena_e_Arte.Application.ConductReports.Commands;

/// <summary>
/// Shared shape of <see cref="FileArtistConductReportCommand"/> and
/// <see cref="FileStudioConductReportCommand"/> — lets
/// <see cref="FileConductReportValidatorBase{T}"/> validate both with one rule set instead of
/// two byte-identical copies. Records implement this automatically via their positional
/// properties, no extra code needed on either command.
/// </summary>
public interface IFileConductReportCommand
{
    Guid AppointmentId { get; }
    string Reason { get; }
    string ReporterName { get; }
    IReadOnlyList<string>? AttachmentUrls { get; }
}
