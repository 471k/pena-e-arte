namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// One outbound webhook endpoint per studio — mirrors StudioApiKey's "single active
/// credential, regenerate replaces it" simplicity rather than a multi-endpoint
/// subscription model. EncryptedSecret is round-trippable (ITokenEncryptor, AES-256-GCM)
/// rather than hashed like StudioApiKey.KeyHash — delivery needs the raw secret to
/// compute each request's HMAC signature, so it can never be a one-way hash.
/// </summary>
public class WebhookEndpoint : TenantEntity
{
    public string Url { get; set; } = string.Empty;
    public string EncryptedSecret { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int ConsecutiveFailureCount { get; set; }
    public DateTime? LastDeliveryAt { get; set; }
    public bool? LastDeliverySucceeded { get; set; }
}
