namespace Pena_e_Arte.Contracts.Requests;

public record RegisterUserRequest(string Email, string Password, string Role, Guid? StudioId, string? FirstName = null);
