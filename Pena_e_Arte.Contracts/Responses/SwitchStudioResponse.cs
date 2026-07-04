namespace Pena_e_Arte.Contracts.Responses;

public record SwitchStudioResponse(
    string AccessToken, string RefreshToken, bool IsNewMembership, string TokenType = "Bearer");
