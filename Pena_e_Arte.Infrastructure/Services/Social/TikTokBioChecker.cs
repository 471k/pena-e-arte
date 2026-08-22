using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.Social;

/// <summary>
/// TikTok has no general-purpose "read any public account's bio via an app-level
/// API key/token" endpoint documented as stable/available — its Display API only
/// reads the *authenticated* user's own data. Verification for TikTok is OAuth-only
/// in this feature; this class exists only so the registry always has an entry for
/// every platform, and always reports IsSupported == false. Do not attempt a scraper
/// as a substitute — that is exactly the ToS/reliability risk this design ruled out.
/// </summary>
public sealed class TikTokBioChecker : ISocialBioChecker
{
    public SocialPlatform Platform => SocialPlatform.TikTok;

    public bool IsSupported => false;

    public Task<bool> BioContainsCodeAsync(string handle, string code, CancellationToken ct) =>
        throw new InvalidOperationException(
            "TikTok has no manual bio-code verification path — check IsSupported before calling this.");
}
