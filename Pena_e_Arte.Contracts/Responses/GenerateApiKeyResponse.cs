namespace Pena_e_Arte.Contracts.Responses;

/// <summary>The only response that ever carries the plaintext key — it is not retrievable again.</summary>
public record GenerateApiKeyResponse(
    string ApiKey,
    string KeyPrefix,
    DateTime CreatedAt);
