namespace Pena_e_Arte.Contracts.Requests;

/// <summary>Public contact-form submission. Relayed to support by email; never persisted.</summary>
public record SubmitContactRequest(string Name, string Email, string Message);
