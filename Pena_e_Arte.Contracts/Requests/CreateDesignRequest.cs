namespace Pena_e_Arte.Contracts.Requests;

public record CreateDesignRequest(
    Guid    ClientId,
    Guid    ArtistId,
    string  Title,
    string? Description);
