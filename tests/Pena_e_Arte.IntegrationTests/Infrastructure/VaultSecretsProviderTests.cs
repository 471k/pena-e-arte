using FluentAssertions;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Infrastructure.Services;
using VaultSharp;
using VaultSharp.V1.AuthMethods.Token;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

public class VaultSecretsProviderTests
{
    [Fact]
    public async Task GetSecretAsync_ResolvesWrittenSecret_AndFailsClosedOnMissing()
    {
        string? addr = Environment.GetEnvironmentVariable("VAULT_ADDR");
        string? token = Environment.GetEnvironmentVariable("VAULT_TOKEN");
        if (string.IsNullOrEmpty(addr) || string.IsNullOrEmpty(token))
        {
            // Env-gated: the happy-path assertions only run where the dev-mode Vault is
            // available (set VAULT_ADDR/VAULT_TOKEN). The fail-closed contract is covered by
            // GetSecretAsync_UnreachableVault_FailsClosed, which needs no container and always
            // runs in CI. xUnit 2.9.3 has no dynamic Assert.Skip, so this returns rather than
            // reporting a skip.
            return;
        }

        VaultOptions opts = new() { Address = addr, Token = token, MountPoint = "secret" };

        // Seed a secret via VaultSharp directly (KV v2).
        IVaultClient client = new VaultClient(
            new VaultClientSettings(addr, new TokenAuthMethodInfo(token)));
        string path = $"studios/{Guid.NewGuid():N}/pok";
        await client.V1.Secrets.KeyValue.V2.WriteSecretAsync(
            path: path,
            data: new Dictionary<string, object> { ["apiKey"] = "s3cr3t-value" },
            mountPoint: "secret");

        VaultSecretsProvider provider = new(Options.Create(opts));

        // Happy path — resolves the field.
        string value = await provider.GetSecretAsync($"{path}:apiKey", default);
        value.Should().Be("s3cr3t-value");

        // Missing field → throws (fail closed), never returns null.
        Func<Task> missingField = () => provider.GetSecretAsync($"{path}:nope", default);
        await missingField.Should().ThrowAsync<InvalidOperationException>();

        // Missing path → throws (fail closed).
        Func<Task> missingPath = () =>
            provider.GetSecretAsync("studios/does-not-exist/pok:apiKey", default);
        await missingPath.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetSecretAsync_UnreachableVault_FailsClosed()
    {
        // No container needed — a dead address must throw, never yield a null credential.
        VaultOptions opts = new() { Address = "http://127.0.0.1:1", Token = "x", MountPoint = "secret" };
        VaultSecretsProvider provider = new(Options.Create(opts));

        Func<Task> act = () => provider.GetSecretAsync("any/path:field", default);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
