using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Interfaces;

public interface ISocialOAuthProviderFactory
{
    /// <summary>
    /// Resolves the provider for a platform. All five SocialPlatform values always have a
    /// registered provider class, so this should never throw in normal operation —
    /// ISocialOAuthProvider.IsConfigured (not the absence of a provider) is the runtime
    /// gate for "not available on this deployment yet".
    /// </summary>
    ISocialOAuthProvider GetProvider(SocialPlatform platform);
}
