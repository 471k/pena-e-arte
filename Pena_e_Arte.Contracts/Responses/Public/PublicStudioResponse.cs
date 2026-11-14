namespace Pena_e_Arte.Contracts.Responses.Public;

public record PublicStudioHoursResponse(DayOfWeek DayOfWeek, TimeSpan StartTime, TimeSpan EndTime, bool IsOpen);

/// <param name="ShowPlatformBranding">Whether the website embed (/embed/:slug) shows "Powered by TattooOS":
/// the studio's own <c>Studio.ShowPlatformBranding</c>, the same value its emails, consent forms and receipts
/// follow. This endpoint is anonymous, so it carries only this resolved yes/no, which the rendered widget shows
/// anyone anyway. Never add the plan, <c>AllowBrandingRemoval</c> or anything else describing what the studio is
/// entitled to: that is commercial information about a customer (Desktop, 14 Nov 2026; a test enforces it).
/// The public studio and artist pages on tattooos.co deliberately ignore it and always show the line.</param>
public record PublicStudioResponse(
    Guid StudioId,
    string Name,
    string Slug,
    string City,
    double Latitude,
    double Longitude,
    string? Description,
    string? CoverImageUrl,
    string? PhoneNumber,
    double? AverageRating,
    int ReviewCount,
    IReadOnlyList<string> GalleryImages,
    IReadOnlyList<PublicArtistSummary> Artists,
    bool ShowBookingCta,
    IReadOnlyList<PublicSocialLinkResponse> SocialLinks,
    IReadOnlyList<PublicStudioHoursResponse> Hours,
    string Timezone,
    bool ShowPlatformBranding,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? PostalCode = null,
    string Currency = "",
    string CountryCode = "");
