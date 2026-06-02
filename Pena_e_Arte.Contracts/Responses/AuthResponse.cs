namespace Pena_e_Arte.Contracts.Responses;

public record AuthResponse(string AccessToken, string TokenType = "Bearer");
