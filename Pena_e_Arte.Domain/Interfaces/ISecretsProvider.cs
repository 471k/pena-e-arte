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

    /// <summary>
    /// Writes one or more fields to a secret at <paramref name="path"/>, merging with whatever
    /// already exists there (a KV v2 path can hold several fields — e.g. a studio's POK entry
    /// holds both "keyId" and "keySecret" under one path). Never logs the values.
    /// </summary>
    Task SetSecretAsync(string path, IReadOnlyDictionary<string, string> fields, CancellationToken ct);
}
