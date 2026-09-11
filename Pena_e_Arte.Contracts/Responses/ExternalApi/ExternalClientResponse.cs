namespace Pena_e_Arte.Contracts.Responses.ExternalApi;

public record ExternalClientResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    DateTime CreatedAt);
