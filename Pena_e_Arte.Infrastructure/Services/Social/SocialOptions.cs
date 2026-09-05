namespace Pena_e_Arte.Infrastructure.Services.Social;

/// <summary>
/// Signing key for ISocialOAuthStateSigner. Bound the same way InstagramOptions is —
/// appsettings.json carries the section shape with an empty-string placeholder, the
/// real base64-encoded 32-byte key is supplied via environment variable in every real
/// deployment. A separate key from Instagram:TokenEncryptionKey — this signer now also
/// authenticates studio-subject callbacks, which IInstagramStateSigner was never scoped
/// to trust.
/// </summary>
public class SocialSigningOptions
{
    public const string Section = "Social";

    public string StateSigningKey { get; init; } = "";
}

public class TikTokOptions
{
    public const string Section = "Social:TikTok";

    public string ClientKey { get; init; } = "";
    public string ClientSecret { get; init; } = "";
    public string RedirectUri { get; init; } = "";
}

public class FacebookOptions
{
    public const string Section = "Social:Facebook";

    public string AppId { get; init; } = "";
    public string AppSecret { get; init; } = "";
    public string RedirectUri { get; init; } = "";
}

public class XOptions
{
    public const string Section = "Social:X";

    public string ClientId { get; init; } = "";
    public string ClientSecret { get; init; } = "";
    public string RedirectUri { get; init; } = "";

    /// <summary>App-only bearer token, used by XBioChecker for the manual verification
    /// path's read-only lookups — distinct from the ClientId/ClientSecret pair above,
    /// which is used for the user-context OAuth Connect path.</summary>
    public string BearerToken { get; init; } = "";
}

public class YouTubeOptions
{
    public const string Section = "Social:YouTube";

    public string ClientId { get; init; } = "";
    public string ClientSecret { get; init; } = "";
    public string RedirectUri { get; init; } = "";

    /// <summary>API key for YouTubeBioChecker's manual verification path — needs no
    /// OAuth from the target channel, only a Google Cloud API key.</summary>
    public string ApiKey { get; init; } = "";
}
