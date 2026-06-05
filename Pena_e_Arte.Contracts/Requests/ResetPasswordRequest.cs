namespace Pena_e_Arte.Contracts.Requests;

public record ResetPasswordRequest(
    string Email,
    string Token,
    string NewPassword);
