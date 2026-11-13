namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// The platform-branding line on client-facing documents (consent PDF, receipt), shown only while the
/// studio's ShowPlatformBranding flag is on (decision SP-03, extended to receipts 13 Nov 2026). One
/// string for the document family so the two cannot drift apart. The booking widget's
/// "Powered by TattooOS" is a separate, web-only surface.
/// </summary>
internal static class DocumentBranding
{
    internal const string GeneratedViaLine = "Generated via TattooOS · tattooos.co";
}
