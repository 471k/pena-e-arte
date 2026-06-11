namespace Pena_e_Arte.Contracts.Responses;

public record ClientResponse(
    Guid     Id,
    Guid     StudioId,
    string   FirstName,
    string   LastName,
    string   Email,
    string?  Phone,
    DateTime CreatedAt,
    Guid?    UserId);
