using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.Social;

/// <summary>
/// HMAC-SHA256 signs (subjectType, subjectId, platform, issuedAt) carried in the generic social
/// OAuth `state` param — same signing/encoding shape as InstagramStateSigner (payload
/// dot base64(hmac)), different payload and a separate key
/// (Social:StateSigningKey, not Instagram:TokenEncryptionKey). The issued-at stamp bounds a
/// captured `state`'s replay window to 15 minutes.
/// Payload format: "{subjectType}|{subjectId:N}|{platform}|{unixSeconds}".
/// </summary>
public sealed class SocialOAuthStateSigner : ISocialOAuthStateSigner
{
    /// <summary>How long a signed state stays valid after it is issued.</summary>
    public static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Tolerated clock skew for a state stamped slightly in the future (multi-instance API).</summary>
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

    private readonly byte[] _key;
    private readonly TimeProvider _time;

    public SocialOAuthStateSigner(IOptions<SocialSigningOptions> options) : this(options, TimeProvider.System) { }

    // internal: a public second constructor would make DI's constructor choice ambiguous.
    internal SocialOAuthStateSigner(IOptions<SocialSigningOptions> options, TimeProvider time)
    {
        _key = Convert.FromBase64String(options.Value.StateSigningKey);
        _time = time;
    }

    public string Sign(SocialLinkSubjectType subjectType, Guid subjectId, SocialPlatform platform)
    {
        string payload = BuildPayload(subjectType, subjectId, platform, _time.GetUtcNow().ToUnixTimeSeconds());
        byte[] hmac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));
        return $"{payload}.{Convert.ToBase64String(hmac)}";
    }

    public bool TryValidate(
        string state,
        out SocialLinkSubjectType subjectType,
        out Guid subjectId,
        out SocialPlatform platform)
    {
        subjectType = default;
        subjectId = Guid.Empty;
        platform = default;

        int dot = state.LastIndexOf('.');
        if (dot < 0) return false;

        string payload = state[..dot];
        string providedSig = state[(dot + 1)..];

        string[] parts = payload.Split('|');
        if (parts.Length != 4) return false;

        if (!Enum.TryParse(parts[0], out SocialLinkSubjectType parsedSubjectType)) return false;
        if (!Guid.TryParseExact(parts[1], "N", out Guid parsedSubjectId)) return false;
        if (!Enum.TryParse(parts[2], out SocialPlatform parsedPlatform)) return false;
        if (!long.TryParse(parts[3], out long issuedAtUnix)) return false;

        byte[] expected;
        byte[] provided;
        try
        {
            expected = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));
            provided = Convert.FromBase64String(providedSig);
        }
        catch (FormatException)
        {
            return false;
        }

        // Signature first: only a state this API signed gets its timestamp interpreted at all.
        if (!CryptographicOperations.FixedTimeEquals(expected, provided)) return false;
        if (!IsFresh(issuedAtUnix)) return false;

        subjectType = parsedSubjectType;
        subjectId = parsedSubjectId;
        platform = parsedPlatform;
        return true;
    }

    private bool IsFresh(long issuedAtUnix)
    {
        DateTimeOffset now = _time.GetUtcNow();
        DateTimeOffset issuedAt;
        try
        {
            issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAtUnix);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        return issuedAt <= now + ClockSkew && now - issuedAt <= StateLifetime;
    }

    private static string BuildPayload(SocialLinkSubjectType subjectType, Guid subjectId, SocialPlatform platform, long issuedAtUnix) =>
        $"{subjectType}|{subjectId:N}|{platform}|{issuedAtUnix}";
}
