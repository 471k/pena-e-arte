namespace Pena_e_Arte.Contracts.Requests;

public record RequestChangeEmailRequest(string CurrentPassword, string NewEmail);
