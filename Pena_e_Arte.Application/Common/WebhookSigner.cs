using System.Security.Cryptography;
using System.Text;

namespace Pena_e_Arte.Application.Common;

/// <summary>
/// HMAC-SHA256 request signing for outbound webhook deliveries — the receiving end
/// recomputes the same signature over (timestamp + "." + body) using the shared secret
/// to verify authenticity and reject replays outside its own tolerance window. Same
/// scheme Stripe/GitHub webhooks use; deliberately not just signing the body alone so a
/// captured-and-replayed request is at least detectable by timestamp.
/// </summary>
public static class WebhookSigner
{
    public static string Sign(string secret, string timestamp, string body)
    {
        byte[] key = Encoding.UTF8.GetBytes(secret);
        byte[] message = Encoding.UTF8.GetBytes($"{timestamp}.{body}");
        byte[] hash = HMACSHA256.HashData(key, message);
        return Convert.ToHexStringLower(hash);
    }

    public static string GenerateSecret()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        return "whsec_" + Convert.ToHexStringLower(bytes);
    }
}
