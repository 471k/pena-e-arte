namespace Pena_e_Arte.Contracts.Requests;

public record CreateClientRequest(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    Guid ArtistId);
