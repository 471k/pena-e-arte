namespace Pena_e_Arte.Contracts.Responses.Public;

public record PublicStudioHoursResponse(DayOfWeek DayOfWeek, TimeSpan StartTime, TimeSpan EndTime, bool IsOpen);

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
    string Timezone);
