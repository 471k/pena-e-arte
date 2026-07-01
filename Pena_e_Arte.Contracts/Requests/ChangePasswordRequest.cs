namespace Pena_e_Arte.Contracts.Requests;

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
