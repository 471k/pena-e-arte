using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// HMAC-SHA256 signs the artistId carried in the Instagram OAuth `state` param.
/// Reuses Instagram:TokenEncryptionKey as HMAC key material — a separate secret
/// isn't warranted for this scope. Format: "{artistId:N}.{base64(hmac)}".
/// </summary>
public sealed class InstagramStateSigner(IOptions<InstagramOptions> options) : IInstagramStateSigner
{
    private readonly byte[] _key = Convert.FromBase64String(options.Value.TokenEncryptionKey);

    public string Sign(Guid artistId)
    {
        string payload = artistId.ToString("N");
        byte[] hmac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));
        return $"{payload}.{Convert.ToBase64String(hmac)}";
    }

    public bool TryValidate(string state, out Guid artistId)
    {
        artistId = Guid.Empty;

        int dot = state.IndexOf('.');
        if (dot < 0) return false;

        string payload = state[..dot];
        string providedSig = state[(dot + 1)..];

        if (!Guid.TryParseExact(payload, "N", out Guid parsed)) return false;

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

        artistId = parsed;
        return true;
    }
}
