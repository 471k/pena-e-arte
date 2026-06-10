namespace Pena_e_Arte.Contracts.Responses.Public;

public record PublicArtistResponse(
    Guid                  ArtistId,
    string                Name,
    string                Slug,
    string?               Bio,
    IReadOnlyList<string> PortfolioImages,
    string                StudioName,
    string                StudioSlug,
    bool                  ShowBookingCta);
