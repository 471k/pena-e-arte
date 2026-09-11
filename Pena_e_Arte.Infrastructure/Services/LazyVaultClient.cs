using Microsoft.Extensions.Options;
using VaultSharp;
using VaultSharp.V1;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.Token;

namespace Pena_e_Arte.Infrastructure.Services;

// VaultClient's constructor validates Vault:Address eagerly and throws UriFormatException on an
// empty/invalid URI. Registering that construction directly as the IVaultClient singleton factory
// made the throw happen at DI-resolution time, crashing every endpoint that resolves
// IPaymentProvider in any environment where Vault:Address isn't configured — including every
// checked-in default (appsettings.json, .env.example, docker-compose.yml). This wrapper defers
// construction to first real use, so an unconfigured Vault fails closed inside
// VaultSecretsProvider's existing try/catch instead of crashing DI.
internal sealed class LazyVaultClient(IOptions<VaultOptions> options) : IVaultClient
{
    private readonly Lazy<IVaultClient> _inner = new(() =>
    {
        VaultOptions opts = options.Value;
        IAuthMethodInfo auth = new TokenAuthMethodInfo(opts.Token);
        return new VaultClient(new VaultClientSettings(opts.Address, auth));
    });

    public VaultClientSettings Settings => _inner.Value.Settings;

    public IVaultClientV1 V1 => _inner.Value.V1;
}
