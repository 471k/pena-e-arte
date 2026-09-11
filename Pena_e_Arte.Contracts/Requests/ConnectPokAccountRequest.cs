namespace Pena_e_Arte.Contracts.Requests;

/// <summary>
/// Owner-supplied POK merchant credentials (from the studio's own POK dashboard — see
/// docs/payments/ADR-0001-payment-providers.md). keyId/keySecret are written to Vault and never
/// persisted to MySQL or logged; only the merchantId (a routing value, not a secret) lands on
/// Studio.
/// </summary>
public record ConnectPokAccountRequest(string KeyId, string KeySecret, string MerchantId);
