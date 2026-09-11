namespace Pena_e_Arte.Contracts.Responses;

/// <summary>Never carries keyId/keySecret — only whether a connection exists and its merchantId
/// (a routing value, not a secret), for the owner-facing "Connect POK" settings card.</summary>
public record PokConnectionStatusResponse(bool Connected, string? MerchantId);
