namespace Pena_e_Arte.Domain.Models;

public record PortableTattooRecord(
    string BodyLocation,
    IReadOnlyList<string> PhotoUrls,
    string Description,
    DateTime CompletedAt,
    string ArtistFirstName);
