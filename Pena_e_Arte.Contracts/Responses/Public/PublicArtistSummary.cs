namespace Pena_e_Arte.Contracts.Responses.Public;

public record PublicArtistSummary(
    Guid ArtistId,
    string Name,
    string Slug,
    string? Bio,
    string? ProfileImageUrl,   // circular avatar; null → show monogram
    List<string> Specializations,   // canonical TattooStyle values, e.g. ["blackwork", "geometric"]
    double? AverageRating,     // null = no reviews yet
    int ReviewCount);
