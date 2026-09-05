using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.Social;

/// <summary>
/// Wraps the existing, already-hardened IInstagramService rather than duplicating its
/// HTTP calls — this is what lets a studio connect Instagram using the exact same
/// underlying API integration an artist already uses. Do not add a second Instagram
/// HTTP client here.
/// </summary>
public sealed class InstagramSocialOAuthProvider(
    IInstagramService instagram,
    IOptions<InstagramOptions> options) : ISocialOAuthProvider
{
    public SocialPlatform Platform => SocialPlatform.Instagram;

    public bool IsConfigured => !string.IsNullOrEmpty(options.Value.AppId);

    public string BuildAuthorizationUrl(string state) => instagram.BuildAuthorizationUrl(state);

    public async Task<SocialOAuthTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        InstagramTokenResponse token = await instagram.ExchangeCodeAsync(code, ct);
        return new SocialOAuthTokenResponse(
            token.AccessToken, token.UserId, DateTime.UtcNow.AddSeconds(token.ExpiresIn));
    }

    public Task<string> GetUsernameAsync(string accessToken, CancellationToken ct) =>
        instagram.GetUsernameAsync(accessToken, ct);
}
