using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// One platform's manual bio-code verification check, backed by that platform's own
/// official public-read API — never HTML scraping (a ToS violation for every platform
/// this feature covers, and technically brittle against JS-rendered profile pages/bot
/// detection). Every platform gets a registered implementation; IsSupported reports
/// whether that platform actually has a suitable official API for this (TikTok does
/// not — its Display API only reads the *authenticated* user's own data).
/// </summary>
public interface ISocialBioChecker
{
    SocialPlatform Platform { get; }

    /// <summary>
    /// False when this platform has no official public-read API suitable for this check
    /// — callers must surface "manual verification isn't available for this platform,
    /// use Connect instead" rather than attempting a check.
    /// </summary>
    bool IsSupported { get; }

    Task<bool> BioContainsCodeAsync(string handle, string code, CancellationToken ct);
}
