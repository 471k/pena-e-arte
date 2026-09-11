namespace Pena_e_Arte.Contracts.Responses;

/// <summary>Secret is only ever returned here, once, at creation/regeneration time —
/// never again afterward, same one-time-reveal contract as GenerateApiKeyResponse.</summary>
public record GenerateWebhookSecretResponse(string Url, string Secret, DateTime CreatedAt);
