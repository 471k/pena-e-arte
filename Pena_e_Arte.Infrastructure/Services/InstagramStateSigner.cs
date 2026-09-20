using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// HMAC-SHA256 signs the artistId carried in the Instagram OAuth `state` param, together with the
/// moment it was issued so a captured `state` can't be replayed later — an OAuth round-trip takes
/// seconds, so a 15-minute window is generous. Reuses Instagram:TokenEncryptionKey as HMAC key
/// material — a separate secret isn't warranted for this scope.
/// Format: "{artistId:N}|{unixSeconds}.{base64(hmac)}".
/// </summary>
public sealed class InstagramStateSigner : IInstagramStateSigner
{
    /// <summary>How long a signed state stays valid after it is issued.</summary>
    public static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Tolerated clock skew for a state stamped slightly in the future (multi-instance API).</summary>
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

    private readonly byte[] _key;
    private readonly TimeProvider _time;

    public InstagramStateSigner(IOptions<InstagramOptions> options) : this(options, TimeProvider.System) { }

    // internal: a public second constructor would make DI's constructor choice ambiguous.
    internal InstagramStateSigner(IOptions<InstagramOptions> options, TimeProvider time)
    {
        _key = Convert.FromBase64String(options.Value.TokenEncryptionKey);
        _time = time;
    }

    public string Sign(Guid artistId)
    {
        string payload = $"{artistId:N}|{_time.GetUtcNow().ToUnixTimeSeconds()}";
        byte[] hmac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));
        return $"{payload}.{Convert.ToBase64String(hmac)}";
    }

    public bool TryValidate(string state, out Guid artistId)
    {
        artistId = Guid.Empty;

        int dot = state.LastIndexOf('.');
        if (dot < 0) return false;

        string payload = state[..dot];
        string providedSig = state[(dot + 1)..];

        string[] parts = payload.Split('|');
        if (parts.Length != 2) return false;

        if (!Guid.TryParseExact(parts[0], "N", out Guid parsed)) return false;
        if (!long.TryParse(parts[1], out long issuedAtUnix)) return false;

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

        artistId = parsed;
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
}
