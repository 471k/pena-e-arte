namespace Pena_e_Arte.Domain.Constants;

/// <summary>Founder-confirmed public support inbox (2026-08-01). Not a secret. Originally a
/// private const inside SubmitContactRequestHandler; extracted here because
/// FileArtistConductReportHandler/FileStudioConductReportHandler (via ConductReportNotifier)
/// need the same address for the high-severity alert email, and a second private copy
/// would drift.</summary>
public static class PlatformContacts
{
    public const string SupportEmail = "support@tattooos.co";
}
