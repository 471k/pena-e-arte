using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.Social;

public sealed class SocialOAuthProviderFactory(IEnumerable<ISocialOAuthProvider> providers)
    : ISocialOAuthProviderFactory
{
    private readonly Dictionary<SocialPlatform, ISocialOAuthProvider> _byPlatform =
        providers.ToDictionary(p => p.Platform);

    public ISocialOAuthProvider GetProvider(SocialPlatform platform) =>
        _byPlatform.TryGetValue(platform, out ISocialOAuthProvider? provider)
            ? provider
            : throw new InvalidOperationException($"No ISocialOAuthProvider registered for {platform}.");
}
