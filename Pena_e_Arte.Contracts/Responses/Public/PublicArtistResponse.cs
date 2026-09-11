namespace Pena_e_Arte.Contracts.Responses.Public;

public record PublicArtistResponse(
    Guid ArtistId,
    string Name,
    string Slug,
    string? Bio,
    string? ProfileImageUrl,
    IReadOnlyList<ArtistPortfolioImageResponse> PortfolioImages,
    List<string> Specializations,
    decimal? HourlyRate,
    double? AverageRating,
    int ReviewCount,
    string StudioName,
    string StudioSlug,
    bool ShowBookingCta,
    bool IsOwnProfile,
    IReadOnlyList<PublicSocialLinkResponse> SocialLinks);
