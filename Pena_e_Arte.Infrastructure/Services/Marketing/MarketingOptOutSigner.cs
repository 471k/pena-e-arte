using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.Marketing;

/// <summary>
/// HMAC-SHA256 signs a bare clientId — same "payload.base64(hmac)" shape as
/// SocialOAuthStateSigner, different (single-field) payload and a separate key
/// (Marketing:StateSigningKey).
/// </summary>
public sealed class MarketingOptOutSigner(IOptions<MarketingOptOutOptions> options) : IMarketingOptOutSigner
{
    private readonly byte[] _key = Convert.FromBase64String(options.Value.StateSigningKey);

    public string Sign(Guid clientId)
    {
        string payload = clientId.ToString("N");
        byte[] hmac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));
        return $"{payload}.{Convert.ToBase64String(hmac)}";
    }

    public bool TryValidate(string token, out Guid clientId)
    {
        clientId = Guid.Empty;

        int dot = token.LastIndexOf('.');
        if (dot < 0) return false;

        string payload = token[..dot];
        string providedSig = token[(dot + 1)..];

        if (!Guid.TryParseExact(payload, "N", out Guid parsedClientId)) return false;

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

        clientId = parsedClientId;
        return true;
    }
}

/// <summary>Signing key for IMarketingOptOutSigner. Bound the same way SocialSigningOptions
/// is — appsettings.json carries the section shape with an empty-string placeholder, the real
/// base64-encoded 32-byte key is supplied via environment variable in every real deployment.</summary>
public class MarketingOptOutOptions
{
    public const string Section = "Marketing";

    public string StateSigningKey { get; init; } = "";
}
