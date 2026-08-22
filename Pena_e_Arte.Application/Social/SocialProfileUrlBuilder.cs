using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Social;

/// <summary>Builds public profile URLs server-side so the frontend never has to know
/// each platform's URL shape.</summary>
public static class SocialProfileUrlBuilder
{
    public static string Build(SocialPlatform platform, string handle) => platform switch
    {
        SocialPlatform.Instagram => $"https://instagram.com/{handle}",
        SocialPlatform.TikTok => $"https://www.tiktok.com/@{handle}",
        SocialPlatform.Facebook => $"https://facebook.com/{handle}",
        SocialPlatform.X => $"https://x.com/{handle}",
        SocialPlatform.YouTube => $"https://www.youtube.com/@{handle}",
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null),
    };
}
