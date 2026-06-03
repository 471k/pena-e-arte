namespace Pena_e_Arte.Contracts.Responses;

public record PaymentIntentResponse(
    Guid   PaymentId,
    string ClientSecret,
    string Status);
