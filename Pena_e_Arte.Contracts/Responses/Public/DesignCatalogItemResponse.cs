namespace Pena_e_Arte.Contracts.Responses.Public;

public record DesignCatalogItemResponse(
    Guid Id,
    string Title,
    string? Description,
    decimal? Price,
    Guid ArtistId,
    string ArtistName,
    string? ImageUrl);
