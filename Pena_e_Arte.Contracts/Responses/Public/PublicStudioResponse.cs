namespace Pena_e_Arte.Contracts.Responses.Public;

public record PublicStudioResponse(
    Guid StudioId,
    string Name,
    string Slug,
    string City,
    string? Description,
    string? CoverImageUrl,
    string? PhoneNumber,
    string? InstagramHandle,
    double? AverageRating,
    int ReviewCount,
    IReadOnlyList<string> GalleryImages,
    IReadOnlyList<PublicArtistSummary> Artists,
    bool ShowBookingCta);
