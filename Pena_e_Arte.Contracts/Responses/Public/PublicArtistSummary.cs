namespace Pena_e_Arte.Contracts.Responses.Public;

public record PublicArtistSummary(
    Guid    ArtistId,
    string  Name,
    string  Slug,
    string? Bio,
    string? ProfileImageUrl,   // circular avatar; null → show monogram
    string? Specializations,   // comma-separated e.g. "Blackwork, Mandala"
    double? AverageRating,     // null = no reviews yet
    int     ReviewCount);
