using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Interfaces;
using VaultSharp;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.Token;
using VaultSharp.V1.Commons;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// <see cref="ISecretsProvider"/> backed by HashiCorp Vault (KV v2) via VaultSharp. Default
/// backend per CLAUDE.md rule 4; locally it targets the Vault dev-mode docker-compose service.
/// Swapping to Infisical/Doppler is a new implementation of this interface, not a rewrite —
/// see docs/infra/ADR-0002-secrets-management.md.
/// </summary>
public class VaultSecretsProvider : ISecretsProvider
{
    private readonly IVaultClient _client;
    private readonly VaultOptions _opts;

    public VaultSecretsProvider(IOptions<VaultOptions> options)
    {
        _opts = options.Value;
        IAuthMethodInfo auth = new TokenAuthMethodInfo(_opts.Token);
        _client = new VaultClient(new VaultClientSettings(_opts.Address, auth));
    }

    public async Task<string> GetSecretAsync(string key, CancellationToken ct)
    {
        (string path, string field) = SplitKey(key);

        Secret<SecretData> secret;
        try
        {
            secret = await _client.V1.Secrets.KeyValue.V2
                .ReadSecretAsync(path: path, mountPoint: _opts.MountPoint);
        }
        catch (Exception ex)
        {
            // Fail closed: an unreachable/erroring backend must throw, never yield a null
            // credential that a downstream caller silently proceeds with.
            throw new InvalidOperationException(
                $"Could not resolve secret at path '{path}' from Vault.", ex);
        }

        if (secret?.Data?.Data is null
            || !secret.Data.Data.TryGetValue(field, out object? value)
            || value is null)
        {
            throw new InvalidOperationException(
                $"Secret '{key}' is missing or empty — refusing to proceed with no credential (fail closed).");
        }

        return value.ToString()!;
    }

    private static (string Path, string Field) SplitKey(string key)
    {
        int idx = key.LastIndexOf(':');
        return idx < 0 ? (key, "value") : (key[..idx], key[(idx + 1)..]);
    }
}
