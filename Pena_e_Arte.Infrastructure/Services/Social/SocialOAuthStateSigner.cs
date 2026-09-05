using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.Social;

/// <summary>
/// HMAC-SHA256 signs (subjectType, subjectId, platform) carried in the generic social
/// OAuth `state` param — same signing/encoding shape as InstagramStateSigner (payload
/// dot base64(hmac)), different payload and a separate key
/// (Social:StateSigningKey, not Instagram:TokenEncryptionKey).
/// Payload format: "{subjectType}|{subjectId:N}|{platform}".
/// </summary>
public sealed class SocialOAuthStateSigner(IOptions<SocialSigningOptions> options) : ISocialOAuthStateSigner
{
    private readonly byte[] _key = Convert.FromBase64String(options.Value.StateSigningKey);

    public string Sign(SocialLinkSubjectType subjectType, Guid subjectId, SocialPlatform platform)
    {
        string payload = BuildPayload(subjectType, subjectId, platform);
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
        if (parts.Length != 3) return false;

        if (!Enum.TryParse(parts[0], out SocialLinkSubjectType parsedSubjectType)) return false;
        if (!Guid.TryParseExact(parts[1], "N", out Guid parsedSubjectId)) return false;
        if (!Enum.TryParse(parts[2], out SocialPlatform parsedPlatform)) return false;

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

        if (!CryptographicOperations.FixedTimeEquals(expected, provided)) return false;

        subjectType = parsedSubjectType;
        subjectId = parsedSubjectId;
        platform = parsedPlatform;
        return true;
    }

    private static string BuildPayload(SocialLinkSubjectType subjectType, Guid subjectId, SocialPlatform platform) =>
        $"{subjectType}|{subjectId:N}|{platform}";
}
