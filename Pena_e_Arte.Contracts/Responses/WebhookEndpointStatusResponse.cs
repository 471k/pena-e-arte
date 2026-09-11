namespace Pena_e_Arte.Contracts.Responses;

public record WebhookEndpointStatusResponse(
    bool HasEndpoint,
    string? Url,
    bool IsActive,
    DateTime? CreatedAt,
    DateTime? LastDeliveryAt,
    bool? LastDeliverySucceeded);
