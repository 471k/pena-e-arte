using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// Format (Meta's documented one): "{base64url(signature)}.{base64url(payload)}" where the
/// signature is HMAC-SHA256 of the still-encoded payload string, keyed with the app secret.
/// </summary>
public sealed class MetaSignedRequestParser : IMetaSignedRequestParser
{
    private readonly byte[] _appSecret;

    public MetaSignedRequestParser(IOptions<InstagramOptions> options)
    {
        _appSecret = Encoding.UTF8.GetBytes(options.Value.AppSecret);
    }

    public bool TryGetUserId(string signedRequest, out string userId)
    {
        userId = "";

        // Fail closed: with no secret configured nothing can be verified, so nothing is accepted.
        if (_appSecret.Length == 0) return false;

        int dot = signedRequest.IndexOf('.');
        if (dot <= 0 || dot == signedRequest.Length - 1) return false;

        string encodedSignature = signedRequest[..dot];
        string encodedPayload = signedRequest[(dot + 1)..];

        byte[] providedSignature;
        byte[] payloadBytes;
        try
        {
            providedSignature = Base64Url.DecodeFromChars(encodedSignature);
            payloadBytes = Base64Url.DecodeFromChars(encodedPayload);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] expectedSignature = HMACSHA256.HashData(_appSecret, Encoding.UTF8.GetBytes(encodedPayload));
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, providedSignature)) return false;

        // Only a payload this app's secret signed gets parsed at all.
        try
        {
            using JsonDocument document = JsonDocument.Parse(payloadBytes);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            if (!root.TryGetProperty("algorithm", out JsonElement algorithm)
                || !string.Equals(algorithm.GetString(), "HMAC-SHA256", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!root.TryGetProperty("user_id", out JsonElement user)) return false;

            // Meta sends user_id as a string for some products and a bare number for others.
            string? parsed = user.ValueKind switch
            {
                JsonValueKind.String => user.GetString(),
                JsonValueKind.Number => user.GetRawText(),
                _ => null,
            };

            if (string.IsNullOrWhiteSpace(parsed)) return false;

            userId = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
