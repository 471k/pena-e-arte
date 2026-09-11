namespace Pena_e_Arte.Contracts.Responses;

public record WebhookDeliveryResponse(
    Guid Id,
    string EventType,
    int? ResponseStatusCode,
    bool Succeeded,
    string? ErrorMessage,
    DateTime AttemptedAt);
