namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// One active external-API credential per studio. The plaintext secret is never stored —
/// only its SHA-256 hash, plus a short prefix for display so an owner can confirm which key
/// is active without the full secret ever being retrievable again after creation.
/// </summary>
public class StudioApiKey : TenantEntity
{
    public string KeyHash { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
