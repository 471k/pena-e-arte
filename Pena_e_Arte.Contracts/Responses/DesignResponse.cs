namespace Pena_e_Arte.Contracts.Responses;

public record DesignResponse(
    Guid     Id,
    Guid     StudioId,
    Guid     ClientId,
    Guid     ArtistId,
    string   Title,
    string?  Description,
    DateTime CreatedAt);
