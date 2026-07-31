namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// Resolves a secret value from the configured secrets backend (Vault by default — see
/// docs/infra/ADR-0002-secrets-management.md). The abstraction exists so the backend can be
/// swapped (Vault → Infisical/Doppler) by adding one implementation class, not a rewrite.
/// </summary>
public interface ISecretsProvider
{
    /// <summary>
    /// Resolves a secret by key. Throws if the secret cannot be resolved — it MUST never
    /// return null and let a downstream call proceed with no credential (fail closed).
    /// </summary>
    /// <param name="key">
    /// Backend key, format "<path>:<field>" (e.g. "studios/{studioId}/pok:apiKey"). A key
    /// with no ":" reads the field "value" at that path.
    /// </param>
    Task<string> GetSecretAsync(string key, CancellationToken ct);
}
