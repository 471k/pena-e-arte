using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pena_e_Arte.Infrastructure.Services;
using VaultSharp;
using VaultSharp.Core;
using VaultSharp.V1.Commons;
using VaultSharp.V1.SecretsEngines.KeyValue.V2;

namespace Pena_e_Arte.UnitTests.Services;

/// <summary>
/// IVaultClient is injected (see InfrastructureServiceExtensions) specifically so this can run
/// against a substitute instead of a real Vault server — this repo's Vault instance is
/// initialized but was never previously exercised by any live code path.
/// </summary>
public class VaultSecretsProviderTests
{
    private readonly IVaultClient _client = Substitute.For<IVaultClient>();
    private readonly IKeyValueSecretsEngineV2 _kv = Substitute.For<IKeyValueSecretsEngineV2>();

    public VaultSecretsProviderTests()
    {
        _client.V1.Secrets.KeyValue.V2.Returns(_kv);
    }

    private VaultSecretsProvider CreateSut() =>
        new(_client, Options.Create(new VaultOptions { Address = "http://vault", Token = "t", MountPoint = "secret" }));

    [Fact]
    public async Task GetSecretAsync_FieldPresent_ReturnsValue()
    {
        _kv.ReadSecretAsync("studios/x/pok", null, "secret", null)
            .Returns(MakeSecret(new Dictionary<string, object> { ["keyId"] = "abc123" }));

        string result = await CreateSut().GetSecretAsync("studios/x/pok:keyId", default);

        result.Should().Be("abc123");
    }

    [Fact]
    public async Task GetSecretAsync_NoColon_ReadsValueField()
    {
        _kv.ReadSecretAsync("studios/x/pok", null, "secret", null)
            .Returns(MakeSecret(new Dictionary<string, object> { ["value"] = "plain" }));

        string result = await CreateSut().GetSecretAsync("studios/x/pok", default);

        result.Should().Be("plain");
    }

    [Fact]
    public async Task GetSecretAsync_FieldMissing_ThrowsFailClosed()
    {
        _kv.ReadSecretAsync("studios/x/pok", null, "secret", null)
            .Returns(MakeSecret(new Dictionary<string, object> { ["keySecret"] = "shh" }));

        Func<Task> act = () => CreateSut().GetSecretAsync("studios/x/pok:keyId", default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetSecretAsync_VaultThrows_ThrowsFailClosed()
    {
        _kv.ReadSecretAsync("studios/x/pok", null, "secret", null)
            .Returns(Task.FromException<Secret<SecretData>>(new HttpRequestException("unreachable")));

        Func<Task> act = () => CreateSut().GetSecretAsync("studios/x/pok:keyId", default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetSecretAsync_NoExistingSecret_FallsBackToPlainCreateWrite()
    {
        // PatchSecretAsync requires an existing version at the path — Vault returns 404 for a
        // path that's never been written, which is the expected shape for a studio's first-ever
        // connect, not a failure.
        _kv.PatchSecretAsync("studios/x/pok", Arg.Any<PatchSecretDataRequest>(), "secret")
            .Returns(Task.FromException<Secret<CurrentSecretMetadata>>(
                new VaultApiException(HttpStatusCode.NotFound, "no value found")));

        await CreateSut().SetSecretAsync(
            "studios/x/pok",
            new Dictionary<string, string> { ["keyId"] = "k1", ["keySecret"] = "s1" },
            default);

        await _kv.Received(1).WriteSecretAsync(
            "studios/x/pok",
            Arg.Is<Dictionary<string, object>>(d =>
                d.Count == 2 && (string)d["keyId"] == "k1" && (string)d["keySecret"] == "s1"),
            null, "secret");
    }

    [Fact]
    public async Task SetSecretAsync_ExistingSecret_PatchesRatherThanReplacingWholeVersion()
    {
        // Merging with whatever else is already at this path happens inside Vault itself via
        // PatchSecretAsync (a JSON merge patch), not via a client-side read-modify-write — so
        // this only needs to assert the new fields are sent as a patch, not that they were
        // merged with a prior read (Vault owns that merge, atomically, server-side).
        await CreateSut().SetSecretAsync(
            "studios/x/pok",
            new Dictionary<string, string> { ["keyId"] = "new-value" },
            default);

        await _kv.Received(1).PatchSecretAsync(
            "studios/x/pok",
            Arg.Is<PatchSecretDataRequest>(r => r.Data.Count == 1 && (string)r.Data["keyId"] == "new-value"),
            "secret");
        await _kv.DidNotReceive().WriteSecretAsync(
            Arg.Any<string>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<int?>(), Arg.Any<string>());
    }

    [Fact]
    public async Task SetSecretAsync_PatchFailsForReasonOtherThanNotFound_ThrowsWrappedAndNeverFallsBackToWrite()
    {
        _kv.PatchSecretAsync("studios/x/pok", Arg.Any<PatchSecretDataRequest>(), "secret")
            .Returns(Task.FromException<Secret<CurrentSecretMetadata>>(new HttpRequestException("unreachable")));

        Func<Task> act = () => CreateSut().SetSecretAsync(
            "studios/x/pok", new Dictionary<string, string> { ["keyId"] = "k" }, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _kv.DidNotReceive().WriteSecretAsync(
            Arg.Any<string>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<int?>(), Arg.Any<string>());
    }

    [Fact]
    public async Task SetSecretAsync_WriteFallbackFails_ThrowsWrapped()
    {
        _kv.PatchSecretAsync("studios/x/pok", Arg.Any<PatchSecretDataRequest>(), "secret")
            .Returns(Task.FromException<Secret<CurrentSecretMetadata>>(
                new VaultApiException(HttpStatusCode.NotFound, "no value found")));
        _kv.WriteSecretAsync("studios/x/pok", Arg.Any<Dictionary<string, object>>(), null, "secret")
            .Returns(Task.FromException<Secret<CurrentSecretMetadata>>(new HttpRequestException("write failed")));

        Func<Task> act = () => CreateSut().SetSecretAsync(
            "studios/x/pok", new Dictionary<string, string> { ["keyId"] = "k" }, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetSecretAsync_PatchCanceled_PropagatesCancellationUnwrapped()
    {
        _kv.PatchSecretAsync("studios/x/pok", Arg.Any<PatchSecretDataRequest>(), "secret")
            .Returns(Task.FromException<Secret<CurrentSecretMetadata>>(new OperationCanceledException()));

        Func<Task> act = () => CreateSut().SetSecretAsync(
            "studios/x/pok", new Dictionary<string, string> { ["keyId"] = "k" }, default);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetSecretAsync_ReadCanceled_PropagatesCancellationUnwrapped()
    {
        _kv.ReadSecretAsync("studios/x/pok", null, "secret", null)
            .Returns(Task.FromException<Secret<SecretData>>(new OperationCanceledException()));

        Func<Task> act = () => CreateSut().GetSecretAsync("studios/x/pok:keyId", default);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static Task<Secret<SecretData>> MakeSecret(Dictionary<string, object> data) =>
        Task.FromResult(new Secret<SecretData> { Data = new SecretData { Data = data } });
}
