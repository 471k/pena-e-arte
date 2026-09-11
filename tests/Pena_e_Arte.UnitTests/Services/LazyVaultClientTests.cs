using FluentAssertions;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.UnitTests.Services;

/// <summary>
/// Constructing the real VaultClient throws on an empty/invalid Vault:Address — registering that
/// construction directly as the DI factory made every IPaymentProvider-resolving endpoint crash
/// at DI-resolution time in any environment without Vault configured (the shipped default).
/// LazyVaultClient defers construction to first real use so the failure surfaces there instead.
/// </summary>
public class LazyVaultClientTests
{
    [Fact]
    public void Constructing_WithEmptyAddress_DoesNotThrow()
    {
        Action act = () => _ = new LazyVaultClient(
            Options.Create(new VaultOptions { Address = "", Token = "", MountPoint = "secret" }));

        act.Should().NotThrow();
    }

    [Fact]
    public void AccessingV1_WithEmptyAddress_ThrowsOnFirstUse()
    {
        LazyVaultClient client = new(Options.Create(new VaultOptions { Address = "", Token = "", MountPoint = "secret" }));

        Action act = () => _ = client.V1;

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void AccessingV1_WithValidAddress_Succeeds()
    {
        LazyVaultClient client = new(
            Options.Create(new VaultOptions { Address = "http://127.0.0.1:8200", Token = "t", MountPoint = "secret" }));

        Action act = () => _ = client.V1;

        act.Should().NotThrow();
    }
}
