using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// A per-studio pointer to where an external provider's credential lives in the secrets
/// backend — a Vault PATH/KEY, NEVER the credential value itself. This is the scaffolding for
/// ADR-0001 Article 4(g) ("No platform-level API key... per-tenant secrets in Vault"). No real
/// credential is issued or stored anywhere in this session; this ticket only makes the pointer
/// mechanism exist. Deliberately has no value column — a value must never touch MySQL.
/// </summary>
public class StudioCredentialRef : TenantEntity
{
    public CredentialProvider Provider { get; set; }

    /// <summary>Secrets-backend path/key, e.g. "studios/{studioId}/pok:apiKey". Never a value.</summary>
    public string SecretPath { get; set; } = string.Empty;
}
