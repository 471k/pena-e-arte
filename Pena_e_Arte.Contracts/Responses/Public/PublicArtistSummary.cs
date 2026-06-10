namespace Pena_e_Arte.Contracts.Responses.Public;

public record PublicArtistSummary(
    Guid    ArtistId,
    string  Name,
    string  Slug,
    string? Bio);
