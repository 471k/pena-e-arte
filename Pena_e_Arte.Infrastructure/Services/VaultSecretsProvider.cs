using System.Net;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Interfaces;
using VaultSharp;
using VaultSharp.Core;
using VaultSharp.V1.Commons;
using VaultSharp.V1.SecretsEngines.KeyValue.V2;

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

    // IVaultClient is injected (built once in InfrastructureServiceExtensions) rather than
    // constructed here, so this class can be unit-tested against a substitute client instead of
    // a real Vault server.
    public VaultSecretsProvider(IVaultClient client, IOptions<VaultOptions> options)
    {
        _client = client;
        _opts = options.Value;
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
        catch (OperationCanceledException)
        {
            // Real cancellation, not a Vault failure — must propagate as-is, not get wrapped
            // into the fail-closed InvalidOperationException below.
            throw;
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

    public async Task SetSecretAsync(string path, IReadOnlyDictionary<string, string> fields, CancellationToken ct)
    {
        Dictionary<string, object> data = fields.ToDictionary(f => f.Key, f => (object)f.Value);

        // PatchSecretAsync applies `data` as a JSON merge patch inside Vault itself, merging with
        // whatever's already at this path (KV v2's plain write replaces the whole version)
        // atomically, server-side. A prior version of this method read-then-merged-then-wrote in
        // application code instead, which raced: two concurrent SetSecretAsync calls to the same
        // path could each read the same starting point, and the second write would silently drop
        // the first call's fields. Patch requires an existing version at the path, so a brand-new
        // path (a studio's first-ever connect) falls back to a plain create write below.
        try
        {
            await _client.V1.Secrets.KeyValue.V2
                .PatchSecretAsync(path, new PatchSecretDataRequest { Data = data }, _opts.MountPoint);
            return;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (VaultApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            // No existing secret at this path yet — expected on first connect; fall through to
            // create it below rather than a failure.
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not write secret at path '{path}' to Vault.", ex);
        }

        try
        {
            await _client.V1.Secrets.KeyValue.V2
                .WriteSecretAsync(path: path, data: data, mountPoint: _opts.MountPoint);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not write secret at path '{path}' to Vault.", ex);
        }
    }
}
